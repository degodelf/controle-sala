using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ControleSala.Comum;

/// <param name="Etiqueta">Prova de que o PC tem a chave da sala (ver <see cref="Descoberta.DaSala"/>). Vazia em agentes antigos ou sem sala.</param>
public readonly record struct AgenteEncontrado(string Nome, string Endereco, int Porta, string Etiqueta = "");

/// <summary>
/// Cada agente avisa na rede local, a cada poucos segundos, "estou aqui" (nome do PC e porta do vídeo) por UDP.
/// O professor escuta e oferece os PCs encontrados. O anúncio não dá acesso a nada: conectar continua exigindo
/// o que a camada de segurança exigir. A etiqueta diz só "tenho a chave desta sala": com ela, o professor põe na lista
/// sozinho os PCs da própria sala, sem pegar os de outro laboratório da mesma rede.
/// </summary>
public static class Descoberta
{
    public const int Porta = 47811;
    const string Marca = "ControleSala";

    public static byte[] MontarAnuncio(string nome, int portaVideo, byte[]? chaveSala = null)
    {
        var campos = new Dictionary<string, object> { ["app"] = Marca, ["v"] = 1, ["nome"] = nome, ["porta"] = portaVideo };
        if (chaveSala != null) campos["sala"] = Etiqueta(Limpar(nome), portaVideo, chaveSala);
        return JsonSerializer.SerializeToUtf8Bytes(campos);
    }

    /// <summary>Resumo do anúncio feito com a chave da sala. Não revela a chave nem dá acesso a nada.</summary>
    static string Etiqueta(string nome, int porta, byte[] chaveSala) =>
        Convert.ToBase64String(HMACSHA256.HashData(chaveSala, Encoding.UTF8.GetBytes($"ControleSala anuncio v1\n{porta}\n{nome}")).AsSpan(0, 16));

    /// <summary>True se o anúncio foi feito por um PC que tem a chave desta sala.</summary>
    public static bool DaSala(AgenteEncontrado achado, byte[]? chaveSala) =>
        chaveSala != null && achado.Etiqueta.Length > 0
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(achado.Etiqueta), Encoding.ASCII.GetBytes(Etiqueta(achado.Nome, achado.Porta, chaveSala)));

    static string Limpar(string nome) => new string(nome.Where(c => !char.IsControl(c)).ToArray()).Trim();

    public static bool TentarLer(ReadOnlySpan<byte> dados, out string nome, out int porta) =>
        TentarLer(dados, out nome, out porta, out _);

    public static bool TentarLer(ReadOnlySpan<byte> dados, out string nome, out int porta, out string etiqueta)
    {
        nome = "";
        porta = 0;
        etiqueta = "";
        if (dados.Length is 0 or > 512) return false;
        try
        {
            using var doc = JsonDocument.Parse(dados.ToArray());
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object
                || raiz.GetProperty("app").GetString() != Marca
                || raiz.GetProperty("v").GetInt32() != 1)
                return false;
            string n = Limpar(raiz.GetProperty("nome").GetString() ?? "");
            int p = raiz.GetProperty("porta").GetInt32();
            if (n.Length is 0 or > 60 || p is < 1 or > 65535) return false;
            (nome, porta) = (n, p);
            if (raiz.TryGetProperty("sala", out var sala) && sala.ValueKind == JsonValueKind.String)
                etiqueta = sala.GetString() ?? "";
            return true;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Escuta anúncios até <paramref name="ct"/> cancelar, avisando cada anúncio válido. Vários podem escutar ao mesmo tempo.</summary>
    public static async Task EscutarAsync(Action<AgenteEncontrado> aoAchar, int porta = Porta, CancellationToken ct = default)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, porta));
        try
        {
            while (true)
            {
                var pacote = await udp.ReceiveAsync(ct);
                if (TentarLer(pacote.Buffer, out string nome, out int portaVideo, out string etiqueta))
                    aoAchar(new AgenteEncontrado(nome, pacote.RemoteEndPoint.Address.ToString(), portaVideo, etiqueta));
            }
        }
        catch (OperationCanceledException)
        {
            // fim do tempo de busca (ou o chamador cancelou)
        }
    }

    /// <summary>Escuta anúncios durante <paramref name="duracao"/> e devolve os agentes distintos que apareceram.</summary>
    public static async Task<List<AgenteEncontrado>> ProcurarAsync(TimeSpan duracao, int porta = Porta, CancellationToken ct = default)
    {
        var achados = new Dictionary<(string, int), AgenteEncontrado>();
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(duracao);
        await EscutarAsync(a => achados[(a.Endereco, a.Porta)] = a, porta, limite.Token);
        return [.. achados.Values];
    }
}
