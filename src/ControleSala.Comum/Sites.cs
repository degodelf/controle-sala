using System.Text;
using System.Text.Json;

namespace ControleSala.Comum;

/// <summary>
/// Bloqueio de sites por nome. O professor monta uma lista de sites (ex.: facebook.com) e o agente os bloqueia
/// escrevendo no arquivo "hosts" do Windows (aponta o site para um endereço morto). Vale nas duas pontas.
/// </summary>
public static class Sites
{
    public const string Inicio = "# ControleSala — sites bloqueados (início)";
    public const string Fim = "# ControleSala — sites bloqueados (fim)";

    /// <summary>Tira http://, caminho, porta e espaços, deixando só o domínio. Null se não for um domínio válido.</summary>
    public static string? Normalizar(string? site)
    {
        if (string.IsNullOrWhiteSpace(site)) return null;
        var s = site.Trim().ToLowerInvariant();
        int esquema = s.IndexOf("://", StringComparison.Ordinal);
        if (esquema >= 0) s = s[(esquema + 3)..];
        s = s.Split('/')[0].Split('?')[0].Split(':')[0].Trim();
        if (s.StartsWith("www.", StringComparison.Ordinal)) s = s[4..];
        if (s.Length is 0 or > 253 || !s.Contains('.') || s.StartsWith('.') || s.EndsWith('.') || s.Contains("..")) return null;
        foreach (char c in s)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
                return null;
        return s;
    }

    public static bool EhValido(string? site) => Normalizar(site) != null;

    /// <summary>Divide o texto digitado (um site por linha, vírgula ou espaço) em domínios normalizados e distintos.</summary>
    public static List<string> Separar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return [];
        return texto.Split([',', ';', '\n', '\r', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalizar).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Devolve o conteúdo do "hosts" com a nossa seção trocada pelos sites dados (ou removida, se a lista for vazia).
    /// Cada site é bloqueado como ele mesmo e como www. O resto do arquivo não é tocado.
    /// </summary>
    public static string MontarHosts(string hostsAtual, IEnumerable<string> sites)
    {
        string semSecao = RemoverSecao(hostsAtual);
        var validos = sites.Select(Normalizar).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (validos.Count == 0) return semSecao;

        var sb = new StringBuilder(semSecao.TrimEnd('\r', '\n'));
        sb.Append("\r\n").Append(Inicio).Append("\r\n");
        foreach (var d in validos)
        {
            sb.Append("0.0.0.0 ").Append(d).Append("\r\n");
            sb.Append("0.0.0.0 www.").Append(d).Append("\r\n");
        }
        sb.Append(Fim).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Tira a nossa seção do "hosts", deixando o resto intacto.</summary>
    public static string RemoverSecao(string hostsAtual)
    {
        int ini = hostsAtual.IndexOf(Inicio, StringComparison.Ordinal);
        if (ini < 0) return hostsAtual;
        int fim = hostsAtual.IndexOf(Fim, ini, StringComparison.Ordinal);
        if (fim < 0) return hostsAtual[..ini].TrimEnd('\r', '\n') + "\r\n";
        fim += Fim.Length;
        // engole a quebra de linha logo após o marcador final
        while (fim < hostsAtual.Length && hostsAtual[fim] is '\r' or '\n') fim++;
        string antes = hostsAtual[..ini].TrimEnd('\r', '\n');
        string depois = hostsAtual[fim..];
        return (antes.Length == 0 ? "" : antes + "\r\n") + depois;
    }

    // ---- lista de sites guardada no PC do professor ----

    public static List<string> Carregar(string? caminho = null)
    {
        string c = caminho ?? Caminho;
        try
        {
            if (File.Exists(c))
                return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(c)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return [];
    }

    public static void Salvar(IEnumerable<string> sites, string? caminho = null)
    {
        string c = caminho ?? Caminho;
        Directory.CreateDirectory(Path.GetDirectoryName(c)!);
        File.WriteAllText(c, JsonSerializer.Serialize(sites, new JsonSerializerOptions { WriteIndented = true }));
    }

    static string Caminho => Path.Combine(Pastas.Dados, "sites.json");
}
