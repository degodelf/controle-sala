using System.Threading.Channels;

namespace ControleSala.Comum;

/// <summary>
/// O que falta enviar a um professor, na ordem. Mensagem de controle nunca se perde: versão, resultado da limpeza e respostas
/// precisam chegar. Vídeo tem um limite curto: se a rede não der conta, é melhor descartar quadros do que acumular atraso.
/// </summary>
public sealed class FilaDeEnvio(int maxVideo = 4)
{
    readonly Channel<(byte[] Mensagem, bool Video)> _fila =
        Channel.CreateUnbounded<(byte[], bool)>(new UnboundedChannelOptions { SingleReader = true });
    int _videos;

    public void Controle(byte[] mensagem) => _fila.Writer.TryWrite((mensagem, false));

    /// <summary>False se o vídeo foi descartado (fila cheia ou conexão encerrada).</summary>
    public bool Video(byte[] mensagem)
    {
        if (Interlocked.Increment(ref _videos) <= maxVideo && _fila.Writer.TryWrite((mensagem, true))) return true;
        Interlocked.Decrement(ref _videos);
        return false;
    }

    public async IAsyncEnumerable<byte[]> LerTudo()
    {
        await foreach (var (mensagem, video) in _fila.Reader.ReadAllAsync())
        {
            if (video) Interlocked.Decrement(ref _videos);
            yield return mensagem;
        }
    }

    public void Encerrar() => _fila.Writer.TryComplete();
}
