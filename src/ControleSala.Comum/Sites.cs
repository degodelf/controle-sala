using System.Text;
using System.Text.Json;

namespace ControleSala.Comum;

/// <summary>
/// Bloqueio de sites por nome. O professor monta uma lista de sites (ex.: facebook.com) e o agente os bloqueia de dois jeitos:
/// no arquivo "hosts" do Windows (o site e o www, apontando para um endereço morto) e numa regra de DNS do Windows (NRPT)
/// que pega o site e todos os subdomínios dele (m., music., qualquer um) e terminações inteiras como ".bet". Vale nas duas pontas.
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

    /// <summary>Marca de "terminação inteira": "*.bet" bloqueia qualquer site que termine em .bet.</summary>
    public const string PrefixoTerminacao = "*.";

    /// <summary>"*.bet" → "bet"; "*.bet.br" → "bet.br". Null se não for uma terminação válida.</summary>
    public static string? NormalizarTerminacao(string? token)
    {
        if (token is null) return null;
        var t = token.Trim().ToLowerInvariant();
        if (!t.StartsWith(PrefixoTerminacao, StringComparison.Ordinal)) return null;
        t = t[PrefixoTerminacao.Length..];
        if (t.Length is 0 or > 63 || t.StartsWith('.') || t.EndsWith('.') || t.Contains("..")) return null;
        foreach (char c in t)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
                return null;
        return t;
    }

    /// <summary>O que o professor mandou bloquear: sites (com todos os subdomínios) e terminações inteiras ("*.bet").</summary>
    public static (List<string> Dominios, List<string> Terminacoes) SepararTudo(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return ([], []);
        var partes = texto.Split([',', ';', '\n', '\r', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var terminacoes = partes.Select(NormalizarTerminacao).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var dominios = partes.Where(p => !p.StartsWith(PrefixoTerminacao, StringComparison.Ordinal))
            .Select(Normalizar).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return (dominios, terminacoes);
    }

    /// <summary>
    /// Nomes da regra de DNS: cada site vale com todos os subdomínios ("youtube.com" e ".youtube.com"), e cada terminação
    /// vale inteira (".bet"). Quem já está coberto por outro (m.youtube.com, sob youtube.com) não entra de novo.
    /// </summary>
    public static List<string> NomesDaRegraDns(IEnumerable<string> dominios, IEnumerable<string> terminacoes)
    {
        var term = terminacoes.Select(t => t.ToLowerInvariant()).Distinct().ToList();
        var doms = dominios.Select(d => d.ToLowerInvariant()).Distinct().ToList();
        static bool Sob(string nome, string pai) => nome.EndsWith("." + pai, StringComparison.Ordinal);
        var resultado = new List<string>();
        foreach (var t in term.Where(t => !term.Any(o => o != t && Sob(t, o))))
            resultado.Add("." + t);
        foreach (var d in doms)
        {
            if (term.Any(t => Sob(d, t)) || doms.Any(o => o != d && Sob(d, o))) continue;
            resultado.Add(d);
            resultado.Add("." + d);
        }
        return resultado;
    }

    /// <summary>Comentário que identifica a regra de DNS do ControleSala (para trocar ou tirar só a nossa).</summary>
    public const string ComentarioRegraDns = "ControleSala sites bloqueados";

    /// <summary>
    /// Comando do PowerShell que troca a regra de DNS do ControleSala pelos nomes dados (ou só tira, se a lista for vazia).
    /// Os nomes mandam a consulta para um servidor que não existe neste PC (127.0.0.1), então o site não abre.
    /// Os nomes já passaram pela validação (só letras, números, ponto e hífen), então não há como escapar das aspas.
    /// </summary>
    /// <param name="comentario">Qual regra do ControleSala: a dos sites do professor (padrão) ou a do filtro permanente.</param>
    public static string ComandoRegraDns(IReadOnlyCollection<string> nomes, string comentario = ComentarioRegraDns)
    {
        if (comentario.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is ' ' or '-')))
            throw new ArgumentException($"Comentário inválido para a regra de DNS: {comentario}");
        foreach (var n in nomes)
            if (n.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')))
                throw new ArgumentException($"Nome inválido para a regra de DNS: {n}");
        var sb = new StringBuilder();
        sb.Append("$ErrorActionPreference='Stop'; ");
        sb.Append($"Get-DnsClientNrptRule | Where-Object {{ $_.Comment -eq '{comentario}' }} | ForEach-Object {{ Remove-DnsClientNrptRule -Name $_.Name -Force }}; ");
        if (nomes.Count > 0)
            sb.Append($"Add-DnsClientNrptRule -Namespace @({string.Join(",", nomes.Select(n => $"'{n}'"))}) -NameServers '127.0.0.1' -Comment '{comentario}' | Out-Null; ");
        sb.Append("Clear-DnsClientCache");
        return sb.ToString();
    }

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
    /// <param name="inicio">Marcador da seção: a dos sites do professor (padrão) ou a do filtro permanente.</param>
    public static string MontarHosts(string hostsAtual, IEnumerable<string> sites, string inicio = Inicio, string fim = Fim)
    {
        string semSecao = RemoverSecao(hostsAtual, inicio, fim);
        if (MontarSecao(sites, inicio, fim) is not { } secao) return semSecao;
        return semSecao.TrimEnd('\r', '\n') + "\r\n" + secao;
    }

    /// <summary>Só a seção (do marcador de início ao de fim, com a quebra de linha final); null se não sobrar site válido.</summary>
    public static string? MontarSecao(IEnumerable<string> sites, string inicio = Inicio, string fim = Fim)
    {
        var validos = sites.Select(Normalizar).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (validos.Count == 0) return null;
        var sb = new StringBuilder();
        sb.Append(inicio).Append("\r\n");
        foreach (var d in validos)
        {
            sb.Append("0.0.0.0 ").Append(d).Append("\r\n");
            sb.Append("0.0.0.0 www.").Append(d).Append("\r\n");
        }
        sb.Append(fim).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Tira a nossa seção do "hosts", deixando o resto intacto (inclusive a outra seção do ControleSala).</summary>
    public static string RemoverSecao(string hostsAtual, string inicio = Inicio, string fim = Fim)
    {
        int ini = hostsAtual.IndexOf(inicio, StringComparison.Ordinal);
        if (ini < 0) return hostsAtual;
        int ate = hostsAtual.IndexOf(fim, ini, StringComparison.Ordinal);
        if (ate < 0) return hostsAtual[..ini].TrimEnd('\r', '\n') + "\r\n";
        ate += fim.Length;
        // engole a quebra de linha logo após o marcador final
        while (ate < hostsAtual.Length && hostsAtual[ate] is '\r' or '\n') ate++;
        string antes = hostsAtual[..ini].TrimEnd('\r', '\n');
        string depois = hostsAtual[ate..];
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
