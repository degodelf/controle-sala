using System.Net.Sockets;
using System.Security.Cryptography;

namespace ControleSala.Comum;

/// <summary>
/// A assinatura do anúncio não muda, então qualquer aparelho da rede pode copiar um anúncio e repetir. Por isso a lista da sala só
/// muda depois desta conferência: quem está naquele endereço precisa provar que tem a chave da sala e dizer o nome anunciado.
/// </summary>
public static class ConferenciaDeAgente
{
    static readonly TimeSpan LimitePadrao = TimeSpan.FromSeconds(2);

    public static Task<bool> ConfirmaAsync(AgenteEncontrado achado, byte[] chave, TimeSpan? limite = null) =>
        Task.Run(() => Confirma(achado, chave, limite ?? LimitePadrao));

    static bool Confirma(AgenteEncontrado achado, byte[] chave, TimeSpan limite)
    {
        try
        {
            using var tcp = new TcpClient { NoDelay = true };
            using (var tempo = new CancellationTokenSource(limite))
                tcp.ConnectAsync(achado.Endereco, achado.Porta, tempo.Token).AsTask().GetAwaiter().GetResult();
            tcp.ReceiveTimeout = tcp.SendTimeout = (int)limite.TotalMilliseconds;
            using var fluxo = Handshake.Professor(tcp.GetStream(), chave, aceitaSemChave: false);
            return LerNome(fluxo) is not { } nome || RepositorioSala.MesmoNome(nome, achado.Nome);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or AutenticacaoException
            or CryptographicException or InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>O nome que o agente informa ao conectar; null se ele ainda não disse (tela sem captura, nome não escolhido).</summary>
    static string? LerNome(Stream fluxo)
    {
        string? escolhido = null, maquina = null;
        var buffer = new byte[64 * 1024];
        try
        {
            while (Protocolo.Ler(fluxo, ref buffer) is { } msg)
            {
                var corpo = buffer.AsSpan(0, msg.Tamanho);
                if (msg.Tipo == TipoMensagem.Ola) maquina = Protocolo.LerOla(corpo).Maquina;
                else if (msg.Tipo == TipoMensagem.NomeEscolhido) escolhido = NomeDoPc.Validar(Protocolo.LerNomeEscolhido(corpo));
                else if (msg.Tipo == TipoMensagem.VersaoAgente) break;   // é a última mensagem da abertura
            }
        }
        catch (IOException)
        {
            // Acabou o tempo: até a 0.7.7 a versão podia se perder, e fica valendo o que chegou.
        }
        return escolhido ?? maquina;
    }
}
