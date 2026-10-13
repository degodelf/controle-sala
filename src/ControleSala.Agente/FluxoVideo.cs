using System.Diagnostics;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Um fluxo de vídeo do agente: sua própria conversão na GPU, seu encoder e seu ritmo.
/// Completo = tela inteira em alto FPS (só quadros novos). Miniatura = imagem pequena, poucos quadros por segundo,
/// todos quadros-chave (cada miniatura se decodifica sozinha; perder uma não estraga as seguintes).
/// </summary>
sealed unsafe class FluxoVideo : IDisposable
{
    readonly SaidaNv12 _saida;
    readonly CodificadorH264 _codificador;
    readonly bool _soQuadrosChave;
    readonly long _intervalo;
    readonly long _inicio = Stopwatch.GetTimestamp();
    long _proximoEnvio;
    long _ultimaChaveForcada;
    bool _pendente;
    int _pedidoChave;

    public ModoVideo Modo { get; }
    public string NomeEncoder => _codificador.Nome;
    public int Largura => _saida.Largura;
    public int Altura => _saida.Altura;
    /// <summary>Último momento (Stopwatch) em que algum professor precisava deste fluxo.</summary>
    public long UltimoUso { get; set; } = Stopwatch.GetTimestamp();
    public Estatisticas Estat { get; } = new();

    public FluxoVideo(CapturaDxgi captura, ModoVideo modo, int largura, int altura, int fps, int kbps, string? encoder)
    {
        Modo = modo;
        _soQuadrosChave = modo == ModoVideo.Miniatura;
        _intervalo = Stopwatch.Frequency / fps;
        _saida = captura.CriarSaida(largura, altura, qualidade: _soQuadrosChave);
        try
        {
            _codificador = new CodificadorH264(_saida.Largura, _saida.Altura, fps, kbps, encoder, gop: _soQuadrosChave ? 1 : null);
        }
        catch
        {
            _saida.Dispose();
            throw;
        }
    }

    /// <summary>Chegou imagem nova na tela: o próximo quadro deste fluxo pode ser enviado quando der o tempo.</summary>
    public void NovaImagem()
    {
        _pendente = true;
        Estat.Capturados++;
    }

    /// <summary>Alguém precisa de um quadro-chave agora (acabou de conectar, trocou de modo ou perdeu quadros).</summary>
    public void PedirChave() => Interlocked.Exchange(ref _pedidoChave, 1);

    /// <summary>Quantos ticks faltam para o próximo envio (0 = já; long.MaxValue = nada a enviar).</summary>
    public long TicksAteProximo(long agora)
    {
        if (Volatile.Read(ref _pedidoChave) == 1) return 0;
        return _pendente ? Math.Max(0, _proximoEnvio - agora) : long.MaxValue;
    }

    public void Processar(CapturaDxgi captura, ServidorVideo servidor, long agora)
    {
        // Quadros-chave são grandes: no máximo um a cada 250 ms, mesmo com vários pedidos.
        bool pedido = Volatile.Read(ref _pedidoChave) == 1
            && agora - _ultimaChaveForcada > Stopwatch.Frequency / 4
            && Interlocked.Exchange(ref _pedidoChave, 0) == 1;
        bool vencido = _pendente && agora >= _proximoEnvio;
        if (!captura.TemImagem || !(pedido || vencido))
            return;

        long t0 = Stopwatch.GetTimestamp();
        _saida.Atualizar();
        _saida.CopiarPara(_codificador.PrepararQuadro());
        long t1 = Stopwatch.GetTimestamp();
        long pts = (t0 - _inicio) * 1000 / Stopwatch.Frequency + 1;
        _codificador.Codificar(pts, pedido || _soQuadrosChave, (dados, tamanho, chave) =>
        {
            int usAgente = (int)((Stopwatch.GetTimestamp() - captura.MomentoCaptura) * 1_000_000 / Stopwatch.Frequency);
            servidor.TransmitirVideo(Protocolo.MontarVideo(pts, chave, usAgente, dados, tamanho), chave, Modo);
            Estat.Bytes += tamanho;
        });
        long t2 = Stopwatch.GetTimestamp();

        if (pedido) _ultimaChaveForcada = t2;
        _pendente = false;
        _proximoEnvio = Math.Max(_proximoEnvio + _intervalo, t0);
        Estat.Enviados++;
        Estat.TicksCopia += t1 - t0;
        Estat.TicksCodificacao += t2 - t1;
    }

    public void Dispose()
    {
        _codificador.Dispose();
        _saida.Dispose();
    }
}

sealed class Estatisticas
{
    public int Capturados, Enviados;
    public long Bytes, TicksCopia, TicksCodificacao;
    long _inicio = Stopwatch.GetTimestamp();

    public bool Pronta(long agora) => agora - _inicio >= Stopwatch.Frequency * 2;

    public string Resumo(long agora)
    {
        double s = (double)(agora - _inicio) / Stopwatch.Frequency;
        double ms(long ticks) => Enviados == 0 ? 0 : ticks * 1000.0 / Stopwatch.Frequency / Enviados;
        string r = $"{Enviados / s,5:0.0} fps enviados ({Capturados / s:0} capturados) | {Bytes * 8 / s / 1e6,5:0.00} Mbps | " +
                   $"cópia GPU→RAM {ms(TicksCopia):0.0} ms | codificação {ms(TicksCodificacao):0.0} ms";
        Capturados = Enviados = 0;
        Bytes = TicksCopia = TicksCodificacao = 0;
        _inicio = agora;
        return r;
    }
}
