using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using ControleSala.Comum;

namespace ControleSala.Professor;

public enum EstadoConexao { Conectando, Online, Offline }

/// <summary>
/// Conexão com o agente de um computador da sala. Fica tentando conectar sozinha (a cada 3 s) enquanto o PC estiver
/// desligado, recebe e decodifica o vídeo numa thread própria e guarda só o quadro mais recente.
/// </summary>
sealed unsafe partial class ConexaoComputador : IDisposable
{
    public delegate void UsarQuadro(IntPtr bgra, int largura, int altura, int stride, long momentoRecebido, int usAgente);

    static readonly TimeSpan EsperaEntreTentativas = TimeSpan.FromSeconds(3);

    readonly byte[]? _chave;
    readonly CancellationTokenSource _cancelar = new();
    readonly ManualResetEventSlim _tentarJa = new(false);
    readonly Thread _thread;
    readonly Lock _escrita = new();
    readonly DecodificadorH264.ObterDestino _obterDestino;

    volatile TcpClient? _tcp;
    volatile Stream? _fluxo; // o canal criptografado (ou o fluxo puro, só no teste local sem chave)
    volatile EstadoConexao _estado = EstadoConexao.Conectando;
    volatile string _detalhe = "";
    int _modo = (int)ModoVideo.Miniatura;
    volatile object? _ola; // Ola em caixa: a referência é lida/gravada de uma vez só entre threads

    // Buffer duplo: a thread de rede escreve no "de trás"; a interface lê o "da frente".
    readonly Lock _trava = new();
    byte* _frente, _tras;
    int _largura, _altura, _stride;
    bool _novo;
    long _recebidoFrente;
    int _usAgenteFrente;

    long _bytes;
    long _ultimoPedidoChave;

    // Envio de arquivos: cada envio tem um canal onde chegam as respostas do agente.
    readonly ConcurrentDictionary<uint, Channel<RespostaArquivo>> _transferencias = new();
    int _proximoIdArquivo;
    // Comandos: idem, um canal por comando enviado.
    readonly ConcurrentDictionary<uint, Channel<RespostaComando>> _comandos = new();
    int _proximoIdComando;

    public ComputadorSala Dados { get; }
    public EstadoConexao Estado => _estado;
    /// <summary>Por que está offline (ex.: "sem resposta"); vazio quando online.</summary>
    public string Detalhe => _detalhe;
    public Ola? Ola => _ola is Ola o ? o : null;
    /// <summary>MAC informado pelo agente (para Wake-on-LAN); null até chegar.</summary>
    public volatile string? Mac;
    /// <summary>Versão do agente ("0.3.0"); null até chegar (agentes até a 0.2.0 não informam).</summary>
    public volatile string? VersaoAgente;
    public long BytesRecebidos => Interlocked.Read(ref _bytes);

    /// <summary>O que o professor quer receber. Muda na hora, também com a conexão já aberta.</summary>
    public ModoVideo Modo
    {
        get => (ModoVideo)Volatile.Read(ref _modo);
        set
        {
            Volatile.Write(ref _modo, (int)value);
            Enviar(Protocolo.MontarDefinirModo(value));
        }
    }

    /// <param name="chave">Chave da sala; sem ela, só se conecta a agentes de teste neste mesmo PC.</param>
    public ConexaoComputador(ComputadorSala dados, byte[]? chave)
    {
        Dados = dados;
        _chave = chave;
        _obterDestino = ObterDestino;
        _thread = new Thread(Executar) { IsBackground = true, Name = $"Conexão {dados.Nome}" };
        _thread.Start();
    }

    /// <summary>Derruba a conexão atual (se houver) e tenta de novo agora, sem esperar os 3 s.</summary>
    public void Reconectar()
    {
        _tentarJa.Set();
        try { _tcp?.Dispose(); } catch (ObjectDisposedException) { }
    }

    void Executar()
    {
        while (!_cancelar.IsCancellationRequested)
        {
            var tcp = new TcpClient { NoDelay = true, ReceiveBufferSize = 1 << 20 };
            try
            {
                _estado = EstadoConexao.Conectando;
                _tentarJa.Reset();
                using (var limite = CancellationTokenSource.CreateLinkedTokenSource(_cancelar.Token))
                {
                    limite.CancelAfter(TimeSpan.FromSeconds(2.5));
                    tcp.ConnectAsync(Dados.Endereco, Dados.Porta, limite.Token).AsTask().GetAwaiter().GetResult();
                }
                _tcp = tcp;
                tcp.ReceiveTimeout = 5000; // aperto de mão: quem não responde em 5 s é dado como sem resposta
                bool local = tcp.Client.RemoteEndPoint is IPEndPoint ep && IPAddress.IsLoopback(ep.Address);
                var fluxo = Handshake.Professor(tcp.GetStream(), _chave, aceitaSemChave: local);
                tcp.ReceiveTimeout = 0;
                _fluxo = fluxo;
                VersaoAgente = null; // pode ter sido atualizado desde a última conexão: o agente informa de novo
                _estado = EstadoConexao.Online;
                _detalhe = "";
                if (Sessao.Atual is { } professor)
                    Enviar(Protocolo.MontarIdentificacao(professor.Nome)); // o aluno vê quem está olhando
                EnviarConfigLimpeza(); // manda a configuração/imagens de limpeza para este PC
                Enviar(Protocolo.MontarDefinirModo(Modo)); // sem isso o agente não manda vídeo
                Receber(fluxo);
                _detalhe = "conexão encerrada pelo agente";
            }
            catch (AutenticacaoException ex)
            {
                _detalhe = ex.Message;
            }
            catch (CryptographicException)
            {
                _detalhe = "dados alterados na rede (conexão descartada)";
            }
            catch (OperationCanceledException)
            {
                _detalhe = _cancelar.IsCancellationRequested ? "" : "sem resposta";
            }
            catch (Exception ex)
            {
                _detalhe = ex is SocketException se ? Traduzir(se) : ex.Message;
            }
            finally
            {
                _estado = EstadoConexao.Offline;
                _tcp = null;
                _fluxo = null;
                tcp.Dispose();
                // Quem estava enviando arquivo ou comando para este computador precisa saber que a conexão caiu.
                foreach (var canal in _transferencias.Values)
                    canal.Writer.TryWrite(new RespostaArquivo(0, EstadoArquivo.Erro, "a conexão com o computador caiu"));
                foreach (var canal in _comandos.Values)
                    canal.Writer.TryWrite(new RespostaComando(0, false, "a conexão com o computador caiu"));
            }

            // Espera 3 s (ou até pedirem para tentar já, ou o programa fechar).
            WaitHandle.WaitAny([_cancelar.Token.WaitHandle, _tentarJa.WaitHandle], EsperaEntreTentativas);
        }
    }

    static string Traduzir(SocketException e) => e.SocketErrorCode switch
    {
        SocketError.ConnectionRefused => "agente não está aberto",
        SocketError.TimedOut or SocketError.HostUnreachable or SocketError.NetworkUnreachable => "sem resposta",
        SocketError.HostNotFound or SocketError.NoData => "nome não encontrado",
        _ => e.Message,
    };

    void Receber(Stream stream)
    {
        var buffer = new byte[1 << 20];
        using var decodificador = new DecodificadorH264();
        while (!_cancelar.IsCancellationRequested && Protocolo.Ler(stream, ref buffer) is { } msg)
        {
            long recebido = Stopwatch.GetTimestamp();
            switch (msg.Tipo)
            {
                case TipoMensagem.Ola:
                    _ola = (object)Protocolo.LerOla(buffer.AsSpan(0, msg.Tamanho));
                    break;
                case TipoMensagem.EnderecoMac:
                    Mac = WakeOnLan.Normalizar(Protocolo.LerEnderecoMac(buffer.AsSpan(0, msg.Tamanho)));
                    break;
                case TipoMensagem.VersaoAgente:
                    VersaoAgente = Protocolo.LerVersaoAgente(buffer.AsSpan(0, msg.Tamanho));
                    break;
                case TipoMensagem.ArquivoResposta:
                    if (Protocolo.LerArquivoResposta(buffer.AsSpan(0, msg.Tamanho)) is { } resposta
                        && _transferencias.TryGetValue(resposta.Id, out var canal))
                        canal.Writer.TryWrite(resposta);
                    break;
                case TipoMensagem.ComandoResposta:
                    if (Protocolo.LerComandoResposta(buffer.AsSpan(0, msg.Tamanho)) is { } rc
                        && _comandos.TryGetValue(rc.Id, out var canalCmd))
                        canalCmd.Writer.TryWrite(rc);
                    break;
                case TipoMensagem.Video:
                    Interlocked.Add(ref _bytes, msg.Tamanho);
                    int usAgente = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(9));
                    Decodificar(decodificador, buffer, msg.Tamanho, recebido, usAgente);
                    break;
            }
        }
    }

    void Decodificar(DecodificadorH264 decodificador, byte[] buffer, int tamanho, long recebido, int usAgente)
    {
        int n;
        try
        {
            fixed (byte* p = buffer)
                n = decodificador.Decodificar(p + Protocolo.CabecalhoVideo, tamanho - Protocolo.CabecalhoVideo, _obterDestino);
        }
        catch (InvalidOperationException)
        {
            // Dados que não dá para decodificar (quadro perdido): recomeça do próximo quadro-chave.
            PedirQuadroChave();
            return;
        }
        if (n == 0) return;
        lock (_trava)
        {
            var anterior = _frente;
            _frente = _tras;
            _tras = anterior;
            _recebidoFrente = recebido;
            _usAgenteFrente = usAgente;
            _novo = true;
        }
    }

    byte* ObterDestino(int largura, int altura, out int stride)
    {
        if (largura != _largura || altura != _altura)
        {
            lock (_trava)
            {
                NativeMemory.Free(_frente);
                NativeMemory.Free(_tras);
                _stride = largura * 4;
                _frente = (byte*)NativeMemory.AllocZeroed((nuint)(_stride * altura));
                _tras = (byte*)NativeMemory.AllocZeroed((nuint)(_stride * altura));
                _largura = largura;
                _altura = altura;
                _novo = false;
            }
        }
        stride = _stride;
        return _tras;
    }

    /// <summary>Se chegou quadro novo desde a última chamada, entrega para a interface. Retorna false se não.</summary>
    public bool PegarQuadroNovo(UsarQuadro usar)
    {
        lock (_trava)
        {
            if (!_novo) return false;
            _novo = false;
            usar((IntPtr)_frente, _largura, _altura, _stride, _recebidoFrente, _usAgenteFrente);
            return true;
        }
    }

    /// <summary>Reenvia agora a configuração de limpeza (após o professor alterá-la).</summary>
    public void ReenviarConfigLimpeza() => EnviarConfigLimpeza();

    /// <summary>Manda para este PC a configuração de limpeza e as imagens (papel de parede e tela de limpeza), se existirem.</summary>
    void EnviarConfigLimpeza()
    {
        var pacote = Sessao.PacoteLimpeza;
        if (pacote == null) return;
        Enviar(Protocolo.MontarConfigLimpeza(pacote.Json));
        if (pacote.PapelDeParede != null)
            Enviar(Protocolo.MontarArquivoConfig(ArquivoConfigTipo.PapelDeParede, pacote.PapelDeParede));
        if (pacote.ImagemLimpeza != null)
            Enviar(Protocolo.MontarArquivoConfig(ArquivoConfigTipo.ImagemLimpeza, pacote.ImagemLimpeza));
    }

    void PedirQuadroChave()
    {
        long agora = Stopwatch.GetTimestamp();
        if (agora - _ultimoPedidoChave < Stopwatch.Frequency / 2) return;
        _ultimoPedidoChave = agora;
        Enviar(Protocolo.MontarPedidoChave());
    }

    void Enviar(byte[] mensagem)
    {
        var fluxo = _fluxo;
        if (fluxo == null) return;
        try
        {
            lock (_escrita) fluxo.Write(mensagem);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // A queda é tratada pela thread de leitura.
        }
    }

    public void Dispose()
    {
        _cancelar.Cancel();
        _tentarJa.Set();
        try { _tcp?.Dispose(); } catch (ObjectDisposedException) { }
        if (_thread.IsAlive) _thread.Join(3000);
        lock (_trava)
        {
            NativeMemory.Free(_frente);
            NativeMemory.Free(_tras);
            _frente = _tras = null;
            _largura = _altura = 0;
            _novo = false;
        }
    }
}
