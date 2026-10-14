using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Aceita conexões de professores e envia o vídeo do modo que cada um pediu, descartando quadros se a rede não der conta.
/// Com chave da sala, cada conexão só vale depois do aperto de mão (e passa criptografada); sem chave, só serve para teste local.
/// </summary>
sealed class ServidorVideo : IDisposable
{
    const int MaxApertosDeMaoAoMesmoTempo = 8;

    readonly TcpListener _ouvinte;
    readonly byte[]? _chave;
    readonly List<ConexaoProfessor> _conexoes = [];
    readonly Lock _trava = new();
    readonly SemaphoreSlim _vagasHandshake = new(MaxApertosDeMaoAoMesmoTempo);

    /// <summary>Algum professor precisa de um quadro-chave do fluxo desse modo (escolheu o modo, perdeu quadros ou pediu).</summary>
    public event Action<ModoVideo>? PedidoQuadroChave;
    public event Action<string>? Aviso;
    /// <summary>Quem está conectado mudou (entrou, saiu ou se identificou).</summary>
    public event Action<string[]>? ProfessoresMudaram;
    /// <summary>Um arquivo do professor chegou inteiro e conferido: (professor, nome do arquivo).</summary>
    public event Action<string, string>? ArquivoRecebido;

    readonly string _pastaRecebidos;
    readonly bool _liberarParaAlunos;
    bool _pastaPronta;

    /// <summary>Mensagem "Olá" (tamanho da tela, encoder...) enviada a quem conectar.</summary>
    public byte[]? Ola { get; set; }
    /// <summary>Mensagem com o MAC deste PC, enviada a quem conecta (para o Wake-on-LAN).</summary>
    public byte[]? MacMsg { get; set; }

    public ManualResetEventSlim TemProfessores { get; } = new(false);

    public int Quantidade
    {
        get { lock (_trava) return _conexoes.Count; }
    }

    /// <summary>Nomes (ou endereços, se ainda não se identificaram) de quem está vendo esta tela.</summary>
    public string[] Professores
    {
        get { lock (_trava) return [.. _conexoes.Select(c => c.Nome ?? c.Endereco)]; }
    }

    /// <summary>Algum professor conectado quer receber esse modo?</summary>
    public bool Precisa(ModoVideo modo)
    {
        lock (_trava) return _conexoes.Any(c => c.Modo == modo);
    }

    /// <param name="pastaRecebidos">Onde os arquivos do professor são gravados.</param>
    /// <param name="liberarParaAlunos">Dá permissão de alterar/apagar a qualquer usuário (pasta padrão do PC do aluno).</param>
    public ServidorVideo(IPAddress endereco, int porta, byte[]? chave, string pastaRecebidos, bool liberarParaAlunos)
    {
        _chave = chave;
        _pastaRecebidos = pastaRecebidos;
        _liberarParaAlunos = liberarParaAlunos;
        _ouvinte = new TcpListener(endereco, porta);
        _ouvinte.Start();
        _ = AceitarAsync();
    }

    async Task AceitarAsync()
    {
        while (true)
        {
            TcpClient tcp;
            try
            {
                tcp = await _ouvinte.AcceptTcpClientAsync();
            }
            catch (Exception) when (!_ouvinte.Server.IsBound)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            // O aperto de mão roda fora do laço de aceitar: um cliente lento ou mal-intencionado não trava os outros.
            if (!_vagasHandshake.Wait(0))
            {
                tcp.Dispose();
                continue;
            }
            _ = Task.Run(() => IniciarConexao(tcp));
        }
    }

    void IniciarConexao(TcpClient tcp)
    {
        string origem = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
        try
        {
            tcp.NoDelay = true;
            tcp.ReceiveTimeout = tcp.SendTimeout = 5000; // quem não completa o aperto de mão em 5 s é descartado
            var fluxo = Handshake.Agente(tcp.GetStream(), _chave);
            tcp.ReceiveTimeout = tcp.SendTimeout = 0;

            var conexao = new ConexaoProfessor(tcp, fluxo, this);
            lock (_trava)
            {
                _conexoes.Add(conexao);
                TemProfessores.Set();
            }
            Aviso?.Invoke($"Professor conectado ({conexao.Endereco}) — ESTA TELA ESTÁ SENDO TRANSMITIDA.");
            if (Ola != null)
                conexao.EnfileirarControle(Ola);
            if (MacMsg != null)
                conexao.EnfileirarControle(MacMsg);
            conexao.EnfileirarControle(Protocolo.MontarVersaoAgente(Versoes.Atual)); // o professor vê quem precisa atualizar
            conexao.Iniciar();
            AvisarMudanca();
        }
        catch (Exception ex)
        {
            Aviso?.Invoke($"Conexão recusada de {origem}: {ex.Message}");
            tcp.Dispose();
        }
        finally
        {
            _vagasHandshake.Release();
        }
    }

    public void TransmitirVideo(byte[] mensagem, bool chave, ModoVideo modo)
    {
        ConexaoProfessor[] lista;
        lock (_trava) lista = [.. _conexoes];
        foreach (var c in lista)
            if (c.Modo == modo)
                c.EnfileirarVideo(mensagem, chave);
    }

    public void TransmitirControle(byte[] mensagem)
    {
        ConexaoProfessor[] lista;
        lock (_trava) lista = [.. _conexoes];
        foreach (var c in lista)
            c.EnfileirarControle(mensagem);
    }

    internal void SolicitarQuadroChave(ModoVideo modo) => PedidoQuadroChave?.Invoke(modo);

    internal void AvisarMudanca() => ProfessoresMudaram?.Invoke(Professores);

    internal void AvisarArquivo(string professor, string arquivo)
    {
        Aviso?.Invoke($"Arquivo recebido de {professor}: {arquivo}");
        ArquivoRecebido?.Invoke(professor, arquivo);
    }

    internal void RegistrarAviso(string mensagem) => Aviso?.Invoke(mensagem);

    /// <summary>Um recebedor novo para cada professor conectado; prepara a pasta (com as permissões) na primeira vez.</summary>
    internal RecebedorArquivos NovoRecebedor()
    {
        lock (_trava)
        {
            if (!_pastaPronta)
            {
                try
                {
                    PastaRecebidos.Garantir(_pastaRecebidos, _liberarParaAlunos);
                    RecebedorArquivos.LimparRestos(_pastaRecebidos, TimeSpan.Zero); // primeiro envio desde que o agente ligou: o que sobrou é de uma queda anterior
                    _pastaPronta = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Aviso?.Invoke($"Não foi possível preparar a pasta de recebidos ({_pastaRecebidos}): {ex.Message}");
                }
            }
        }
        return new RecebedorArquivos(_pastaRecebidos);
    }

    internal void Remover(ConexaoProfessor conexao, string motivo)
    {
        int restantes;
        lock (_trava)
        {
            if (!_conexoes.Remove(conexao)) return;
            restantes = _conexoes.Count;
            if (restantes == 0) TemProfessores.Reset();
        }
        Aviso?.Invoke($"Professor desconectado ({conexao.Endereco}): {motivo}. Restam {restantes}.");
        AvisarMudanca();
    }

    public void Dispose()
    {
        _ouvinte.Stop();
        ConexaoProfessor[] lista;
        lock (_trava) lista = [.. _conexoes];
        foreach (var c in lista)
            c.Fechar("agente encerrado");
    }
}

sealed class ConexaoProfessor(TcpClient tcp, Stream fluxo, ServidorVideo servidor)
{
    // Poucos quadros na fila: se encher, é melhor descartar do que acumular atraso.
    readonly Channel<byte[]> _fila = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(4) { SingleReader = true });
    volatile bool _esperandoChave = true;
    int _modo = -1; // -1 = ainda não escolheu: só recebe o "Olá"
    int _fechado;

    // Recebimento de arquivos (um por vez): a thread de leitura e o vigia de inatividade usam o mesmo recebedor.
    readonly Lock _travaArquivo = new();
    RecebedorArquivos? _recebedor;
    // Atualização do agente: recebedor à parte (outra pasta, sem a lista de tipos); as mensagens de dados/fim vão
    // para ele quando o id é o da atualização em andamento.
    RecebedorArquivos? _recebedorAtualizacao;
    uint? _idAtualizacao;
    System.Threading.Timer? _vigia;
    uint _ultimoErroEnviado = uint.MaxValue;
    long _tamanhoRecebendo;
    static readonly TimeSpan LimiteParado = TimeSpan.FromSeconds(30);

    public string Endereco { get; } = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
    /// <summary>Nome que o professor informou ao conectar (null até chegar).</summary>
    public volatile string? Nome;

    public ModoVideo? Modo => Volatile.Read(ref _modo) is var m and >= 0 ? (ModoVideo)m : null;

    public void Iniciar()
    {
        _ = EnviarAsync();
        new Thread(Ler) { IsBackground = true, Name = $"Leitura {Endereco}" }.Start();
    }

    public void EnfileirarControle(byte[] mensagem) => _fila.Writer.TryWrite(mensagem);

    /// <summary>Resposta que não pode se perder (ao contrário dos quadros de vídeo): espera ter lugar na fila.</summary>
    void Responder(uint id, EstadoArquivo estado, string mensagem)
    {
        try
        {
            _fila.Writer.WriteAsync(Protocolo.MontarArquivoResposta(id, estado, mensagem)).AsTask().Wait(5000);
        }
        catch (Exception ex) when (ex is AggregateException or System.Threading.Channels.ChannelClosedException or OperationCanceledException)
        {
            // a conexão já fechou
        }
    }

    void ErroDeArquivo(uint id, string mensagem)
    {
        if (id == _ultimoErroEnviado) return; // blocos que ainda estavam a caminho não geram uma enxurrada de erros
        _ultimoErroEnviado = id;
        Responder(id, EstadoArquivo.Erro, mensagem);
    }

    void ReceberComando(ReadOnlySpan<byte> corpo)
    {
        if (Protocolo.LerComando(corpo) is not { } comando) return;
        var (ok, mensagem) = Comandos.Executar(comando, Nome);
        servidor.RegistrarAviso($"Comando \"{comando.Acao}\" de {Nome ?? Endereco}: {(ok ? "ok" : "falhou")} — {mensagem}");
        try
        {
            _fila.Writer.WriteAsync(Protocolo.MontarComandoResposta(comando.Id, ok, mensagem)).AsTask().Wait(5000);
        }
        catch (Exception ex) when (ex is AggregateException or System.Threading.Channels.ChannelClosedException or OperationCanceledException)
        {
            // a conexão já fechou
        }
    }

    void ReceberArquivo(TipoMensagem tipo, ReadOnlySpan<byte> corpo)
    {
        if (Protocolo.LerIdArquivo(corpo) is not { } id)
            throw new InvalidDataException("mensagem de arquivo inválida");

        lock (_travaArquivo)
        {
            if (tipo == TipoMensagem.AtualizacaoInicio || id == _idAtualizacao)
            {
                ReceberAtualizacao(tipo, id, corpo);
                return;
            }
            switch (tipo)
            {
                case TipoMensagem.ArquivoInicio:
                    var inicio = Protocolo.LerArquivoInicio(corpo) ?? throw new InvalidDataException("início de arquivo inválido");
                    _recebedor ??= servidor.NovoRecebedor();
                    var ini = _recebedor.Iniciar(id, inicio.Nome, inicio.Tamanho);
                    if (!ini.Ok) { ErroDeArquivo(id, ini.Mensagem); break; }
                    _tamanhoRecebendo = inicio.Tamanho;
                    _ultimoErroEnviado = uint.MaxValue;
                    _vigia ??= new System.Threading.Timer(_ => VigiarInatividade(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
                    Responder(id, EstadoArquivo.Aceito, ini.Mensagem);
                    break;

                case TipoMensagem.ArquivoDados:
                    var dados = _recebedor?.Dados(id, corpo[4..]) ?? new ResultadoArquivo(false, "Nenhum envio em andamento.");
                    if (!dados.Ok) ErroDeArquivo(id, dados.Mensagem);
                    break;

                case TipoMensagem.ArquivoFim:
                    string? nome = _recebedor?.NomeAtivo;
                    var fim = _recebedor?.Fim(id, corpo[4..]) ?? new ResultadoArquivo(false, "Nenhum envio em andamento.");
                    if (!fim.Ok) { ErroDeArquivo(id, fim.Mensagem); break; }
                    Responder(id, EstadoArquivo.Concluido, fim.Mensagem);
                    servidor.AvisarArquivo(Nome ?? Endereco, nome ?? "arquivo");
                    break;

                case TipoMensagem.ArquivoCancelar:
                    _recebedor?.Cancelar();
                    break;
            }
        }
    }

    /// <summary>Instalador enviado pelo professor para atualizar este PC (chamado com <see cref="_travaArquivo"/>).</summary>
    void ReceberAtualizacao(TipoMensagem tipo, uint id, ReadOnlySpan<byte> corpo)
    {
        switch (tipo)
        {
            case TipoMensagem.AtualizacaoInicio:
                var inicio = Protocolo.LerArquivoInicio(corpo) ?? throw new InvalidDataException("início de atualização inválido");
                _recebedorAtualizacao?.Cancelar();
                _recebedorAtualizacao = AtualizacaoAgente.NovoRecebedor();
                var ini = _recebedorAtualizacao.Iniciar(id, AtualizacaoAgente.NomeArquivo, inicio.Tamanho);
                if (!ini.Ok) { _idAtualizacao = null; ErroDeArquivo(id, ini.Mensagem); break; }
                _idAtualizacao = id;
                _ultimoErroEnviado = uint.MaxValue;
                _vigia ??= new System.Threading.Timer(_ => VigiarInatividade(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
                servidor.RegistrarAviso($"Recebendo atualização (versão {inicio.Nome}) de {Nome ?? Endereco}.");
                Responder(id, EstadoArquivo.Aceito, ini.Mensagem);
                break;

            case TipoMensagem.ArquivoDados:
                var dados = _recebedorAtualizacao?.Dados(id, corpo[4..]) ?? new ResultadoArquivo(false, "Nenhuma atualização em andamento.");
                if (!dados.Ok) ErroDeArquivo(id, dados.Mensagem);
                break;

            case TipoMensagem.ArquivoFim:
                var fim = _recebedorAtualizacao?.Fim(id, corpo[4..]) ?? new ResultadoArquivo(false, "Nenhuma atualização em andamento.");
                _idAtualizacao = null;
                if (!fim.Ok || fim.Caminho == null) { ErroDeArquivo(id, fim.Mensagem); break; }
                var (ok, mensagem) = AtualizacaoAgente.Instalar(fim.Caminho, Nome, servidor.RegistrarAviso);
                servidor.RegistrarAviso($"Atualização de {Nome ?? Endereco}: {mensagem}");
                if (ok) Responder(id, EstadoArquivo.Concluido, mensagem);
                else ErroDeArquivo(id, mensagem);
                break;

            case TipoMensagem.ArquivoCancelar:
                _recebedorAtualizacao?.Cancelar();
                _idAtualizacao = null;
                break;
        }
    }

    void VigiarInatividade()
    {
        lock (_travaArquivo)
        {
            if (_recebedor is { } r && r.Parado(LimiteParado))
            {
                r.Cancelar();
                servidor.RegistrarAviso($"Envio de arquivo de {Nome ?? Endereco} cancelado: ficou parado por {LimiteParado.TotalSeconds:0} s.");
            }
            if (_recebedorAtualizacao is { } a && a.Parado(LimiteParado))
            {
                a.Cancelar();
                _idAtualizacao = null;
                servidor.RegistrarAviso($"Atualização enviada por {Nome ?? Endereco} cancelada: ficou parada por {LimiteParado.TotalSeconds:0} s.");
            }
        }
    }

    public void EnfileirarVideo(byte[] mensagem, bool chave)
    {
        // Sem o quadro-chave, os quadros seguintes não podem ser decodificados.
        if (_esperandoChave && !chave)
            return;
        if (_fila.Writer.TryWrite(mensagem))
        {
            if (chave) _esperandoChave = false;
            return;
        }
        // Fila cheia: a rede ou o PC do professor não está dando conta. Descarta e recomeça do próximo quadro-chave.
        _esperandoChave = true;
        if (Modo is { } modo) servidor.SolicitarQuadroChave(modo);
    }

    async Task EnviarAsync()
    {
        try
        {
            await foreach (var msg in _fila.Reader.ReadAllAsync())
                await fluxo.WriteAsync(msg);
        }
        catch (Exception ex)
        {
            Fechar(ex.Message);
        }
    }

    void Ler()
    {
        var buffer = new byte[256];
        try
        {
            while (Protocolo.Ler(fluxo, ref buffer) is { } msg)
            {
                switch (msg.Tipo)
                {
                    case TipoMensagem.DefinirModo:
                        var novo = Protocolo.LerModo(buffer.AsSpan(0, msg.Tamanho));
                        Volatile.Write(ref _modo, (int)novo);
                        _esperandoChave = true; // o fluxo novo começa num quadro-chave
                        servidor.SolicitarQuadroChave(novo);
                        break;
                    case TipoMensagem.PedirQuadroChave:
                        if (Modo is { } atual) servidor.SolicitarQuadroChave(atual);
                        break;
                    case TipoMensagem.ArquivoInicio:
                    case TipoMensagem.AtualizacaoInicio:
                    case TipoMensagem.ArquivoDados:
                    case TipoMensagem.ArquivoFim:
                    case TipoMensagem.ArquivoCancelar:
                        ReceberArquivo(msg.Tipo, buffer.AsSpan(0, msg.Tamanho));
                        break;
                    case TipoMensagem.Identificacao:
                        string nome = Protocolo.LerIdentificacao(buffer.AsSpan(0, msg.Tamanho));
                        Nome = nome.Length == 0 ? null : nome;
                        servidor.AvisarMudanca();
                        break;
                    case TipoMensagem.Comando:
                        ReceberComando(buffer.AsSpan(0, msg.Tamanho));
                        break;
                    case TipoMensagem.Entrada:
                        if (Protocolo.LerEntrada(buffer.AsSpan(0, msg.Tamanho)) is { } ev)
                        {
                            IndicadorControle.Tocar(Nome); // aviso visível ao aluno
                            if (ev.Tipo == TipoEntrada.Travar)
                                TravaAluno.Definir(ev.Pressionado); // trava/destrava o mouse+teclado do aluno
                            else
                                InterfaceAgente.Postar(() => EntradaRemota.Aplicar(ev)); // injeta na área de trabalho ativa
                        }
                        break;
                    case TipoMensagem.ConfigLimpeza:
                        ConfigLimpezaAgente.Receber(Protocolo.LerConfigLimpeza(buffer.AsSpan(0, msg.Tamanho)));
                        break;
                    case TipoMensagem.ArquivoConfig:
                        if (Protocolo.LerArquivoConfig(buffer.AsSpan(0, msg.Tamanho)) is { } ac)
                            ConfigLimpezaAgente.ReceberImagem(ac.Tipo, ac.Conteudo);
                        break;
                }
            }
            Fechar("conexão encerrada pelo professor");
        }
        catch (Exception ex)
        {
            Fechar(ex.Message);
        }
    }

    public void Fechar(string motivo)
    {
        if (Interlocked.Exchange(ref _fechado, 1) == 1) return;
        _vigia?.Dispose();
        lock (_travaArquivo)
        {
            _recebedor?.Dispose(); // envio pela metade: apaga o temporário
            _recebedorAtualizacao?.Dispose();
        }
        _fila.Writer.TryComplete();
        try { fluxo.Dispose(); } catch (Exception) { /* já estava fechado */ }
        tcp.Dispose();
        servidor.Remover(this, motivo);
    }
}
