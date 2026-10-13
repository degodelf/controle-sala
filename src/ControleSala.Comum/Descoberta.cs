using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace ControleSala.Comum;

public readonly record struct AgenteEncontrado(string Nome, string Endereco, int Porta);

/// <summary>
/// Cada agente avisa na rede local, a cada poucos segundos, "estou aqui" (nome do PC e porta do vídeo) por UDP.
/// O professor escuta e oferece os PCs encontrados. O anúncio não dá acesso a nada: conectar continua exigindo
/// o que a camada de segurança exigir.
/// </summary>
public static class Descoberta
{
    public const int Porta = 47811;
    const string Marca = "ControleSala";

    public static byte[] MontarAnuncio(string nome, int portaVideo) =>
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["app"] = Marca, ["v"] = 1, ["nome"] = nome, ["porta"] = portaVideo });

    public static bool TentarLer(ReadOnlySpan<byte> dados, out string nome, out int porta)
    {
        nome = "";
        porta = 0;
        if (dados.Length is 0 or > 512) return false;
        try
        {
            using var doc = JsonDocument.Parse(dados.ToArray());
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object
                || raiz.GetProperty("app").GetString() != Marca
                || raiz.GetProperty("v").GetInt32() != 1)
                return false;
            string n = new string((raiz.GetProperty("nome").GetString() ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            int p = raiz.GetProperty("porta").GetInt32();
            if (n.Length is 0 or > 60 || p is < 1 or > 65535) return false;
            (nome, porta) = (n, p);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Escuta anúncios durante <paramref name="duracao"/> e devolve os agentes distintos que apareceram.</summary>
    public static async Task<List<AgenteEncontrado>> ProcurarAsync(TimeSpan duracao, int porta = Porta, CancellationToken ct = default)
    {
        var achados = new Dictionary<(string, int), AgenteEncontrado>();
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, porta));
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(duracao);
        try
        {
            while (true)
            {
                var pacote = await udp.ReceiveAsync(limite.Token);
                if (TentarLer(pacote.Buffer, out string nome, out int portaVideo))
                {
                    string ip = pacote.RemoteEndPoint.Address.ToString();
                    achados[(ip, portaVideo)] = new AgenteEncontrado(nome, ip, portaVideo);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // fim do tempo de busca (ou o chamador cancelou)
        }
        return [.. achados.Values];
    }
}
