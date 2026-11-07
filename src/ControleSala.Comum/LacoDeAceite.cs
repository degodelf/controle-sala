using System.Net.Sockets;

namespace ControleSala.Comum;

/// <summary>Aceita conexões até o servidor ser desligado. Um erro numa conexão nunca para o laço: o PC sumiria para o professor.</summary>
public static class LacoDeAceite
{
    static readonly TimeSpan PausaDepoisDeErro = TimeSpan.FromMilliseconds(200);

    /// <param name="aceitar">Espera a próxima conexão.</param>
    /// <param name="ligado">O servidor continua ligado? Falso depois de parar.</param>
    /// <param name="nova">Recebe cada conexão aceita.</param>
    /// <param name="aviso">Recebe a descrição de um erro que não parou o laço.</param>
    public static async Task ExecutarAsync(Func<Task<TcpClient>> aceitar, Func<bool> ligado, Action<TcpClient> nova, Action<string>? aviso = null)
    {
        while (true)
        {
            TcpClient tcp;
            try
            {
                tcp = await aceitar();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception) when (!ligado())
            {
                return;
            }
            catch (Exception ex)
            {
                // Ex.: o cliente desistiu no meio da conexão, ou faltou recurso por um instante.
                aviso?.Invoke($"Erro ao aceitar uma conexão: {ex.Message}");
                await Task.Delay(PausaDepoisDeErro);
                continue;
            }
            nova(tcp);
        }
    }
}
