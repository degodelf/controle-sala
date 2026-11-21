using System.Text.RegularExpressions;

namespace ControleSala.Comum;

/// <summary>
/// Segunda parte do <see cref="FiltroPermanente"/>: pega casas de apostas e sites +18 que não estão na lista pelo NOME do
/// site. Essas plataformas (888game, tigrinho...) trocam de endereço toda hora, então o agente olha os nomes que o PC
/// acabou de procurar e, se o nome tiver uma dessas palavras, passa a bloquear aquele site para sempre neste PC.
/// Só entram palavras que não aparecem em site comum: "bet" sozinho pegaria "alphabet", então não entra.
/// </summary>
public static partial class FiltroPorNome
{
    /// <summary>No máximo isto de sites aprendidos (o mais antigo sai).</summary>
    public const int MaximoAprendidos = 1000;

    static readonly string[] Palavras =
    [
        // apostas em geral
        "aposta", "cassino", "casino", "sportsbet", "sportsbook", "jogodobicho",
        // "tigrinho" e os outros jogos de cassino mais jogados aqui, e quem faz esses jogos
        "tigrinho", "fortunetiger", "fortuneox", "fortunerabbit", "fortunemouse", "fortunedragon",
        "pgsoft", "pragmaticplay", "spribe",
        // marcas (pegam também os endereços de outros países: .es, .it, .pt...)
        "888game", "888casino", "888sport", "888poker", "888bet", "bet365", "1xbet", "22bet", "melbet", "mostbet",
        "betwinner", "linebet", "megapari", "betandyou", "parimatch", "rollbit", "roobet", "betway", "bwin", "unibet",
        "williamhill", "ladbrokes", "paddypower", "betvictor", "betfair", "pokerstars", "leovegas", "casumo", "betsson",
        "draftkings", "fanduel", "betmgm", "brazino", "betano", "sportingbet", "pixbet", "estrelabet", "esportesdasorte",
        "betnacional", "novibet", "superbet", "vaidebet", "betsul", "br4bet", "lotogreen", "pagbet", "betboo",
        "betmotion", "jonbet", "galerabet", "f12bet", "betpix", "bet7k", "4rabet",
        // +18
        "porn", "xxx", "hentai", "xvideos", "xnxx", "xhamster", "redtube", "onlyfans", "chaturbate", "stripchat",
        "bongacams", "brasileirinhas",
    ];

    /// <summary>Nomes de site comuns que terminam em "bet" e não são aposta.</summary>
    static readonly string[] TerminamEmBetMasNaoSao = ["alphabet", "alfabet", "elizabet", "diabet", "tibet", "sorbet", "sherbet"];

    // 777game, 888pg, 999win, 5555bet (as plataformas de "tigrinho" com número repetido)
    [GeneratedRegex(@"(\d)\1{2,}(game|bet|pg|win|jogo|slot)")]
    private static partial Regex NumeroRepetido();

    // bet365, bet7k, 1xbet, 22bet
    [GeneratedRegex(@"bet\d|\dx?bet")]
    private static partial Regex BetComNumero();

    /// <summary>Terminações de dois pedaços em que o nome do site vem antes (exemplo.com.br, e não com.br).</summary>
    static readonly HashSet<string> TerminacoesDuplas = new(StringComparer.Ordinal)
    {
        "com.br", "net.br", "org.br", "edu.br", "gov.br", "art.br", "blog.br", "app.br", "bet.br", "jus.br", "mil.br",
        "tv.br", "eco.br", "ind.br", "inf.br", "br.com", "us.com", "eu.com", "uk.com",
        "com.es", "com.pt", "com.ar", "com.mx", "com.co", "com.pe", "com.py", "com.uy", "com.bo", "com.ve", "com.ec",
        "co.uk", "org.uk", "co.za", "com.au", "co.in", "co.jp", "com.cn", "com.tr", "co.nz", "com.ng", "co.ke",
    };

    /// <summary>"m.888game.es" → "888game.es"; "x.exemplo.com.br" → "exemplo.com.br".</summary>
    public static string DominioPrincipal(string host)
    {
        var partes = host.Split('.');
        if (partes.Length <= 2) return host;
        string ultimasDuas = partes[^2] + "." + partes[^1];
        int pegar = TerminacoesDuplas.Contains(ultimasDuas) ? 3 : 2;
        return string.Join('.', partes[^pegar..]);
    }

    /// <summary>Um pedaço do nome (sem pontos) parece de aposta ou +18?</summary>
    public static bool Parece(string pedaco)
    {
        string p = pedaco.ToLowerInvariant().Replace("-", "");
        if (p.Length < 3) return false;
        foreach (var palavra in Palavras)
            if (p.Contains(palavra, StringComparison.Ordinal)) return true;
        if (NumeroRepetido().IsMatch(p) || BetComNumero().IsMatch(p)) return true;
        // pixbet, vaidebet, estrelabet... (mas não alphabet)
        return p.Length > 4 && p.EndsWith("bet", StringComparison.Ordinal) && !TerminamEmBetMasNaoSao.Any(p.EndsWith);
    }

    /// <summary>
    /// O que bloquear por causa deste nome, ou null se ele não parece de aposta nem +18. Se a palavra está no nome do próprio
    /// site (888game.es), bloqueia o site inteiro; se está só num subdomínio de outro (tigrinho.netlify.app), bloqueia só
    /// aquele endereço, para não derrubar o site dos outros.
    /// </summary>
    public static string? Suspeito(string? nome) => Suspeito(nome, Parece);

    /// <summary>O mesmo, com outra regra para dizer se um pedaço do nome é suspeito (o botão Jogos usa outras palavras).</summary>
    public static string? Suspeito(string? nome, Func<string, bool> parece)
    {
        string? host = Sites.Normalizar(nome);
        if (host is null || System.Net.IPAddress.TryParse(host, out _)) return null;
        string principal = DominioPrincipal(host);
        if (parece(principal[..principal.IndexOf('.')])) return principal;
        if (host.Length == principal.Length) return null;
        string antes = host[..^(principal.Length + 1)];
        return antes.Split('.').Any(parece) ? host : null;
    }

    /// <summary>O nome já está bloqueado pela lista fixa ou por um site aprendido (ele mesmo ou um "pai" dele)?</summary>
    public static bool JaBloqueado(string host, IReadOnlyCollection<string> dominios, IReadOnlyCollection<string> terminacoes)
    {
        foreach (var t in terminacoes)
            if (host.EndsWith("." + t, StringComparison.Ordinal)) return true;
        for (string? n = host; n != null; n = n.IndexOf('.') is var i and >= 0 ? n[(i + 1)..] : null)
            if (dominios.Contains(n)) return true;
        return false;
    }

    /// <summary>Nomes de site soltos num texto (a lista do "ipconfig /displaydns", em qualquer idioma).</summary>
    public static IEnumerable<string> NomesNoTexto(string texto)
    {
        foreach (Match m in NomeDeSite().Matches(texto))
            if (Sites.Normalizar(m.Value) is { } n && !System.Net.IPAddress.TryParse(n, out _))
                yield return n;
    }

    [GeneratedRegex(@"(?<![\w.-])(?:[a-zA-Z0-9-]{1,63}\.)+[a-zA-Z][a-zA-Z0-9-]{1,62}(?![\w-])")]
    private static partial Regex NomeDeSite();

    // ---- sites aprendidos, guardados no PC do aluno ----

    public static string CaminhoPadrao => Path.Combine(Path.GetDirectoryName(ConfigAgente.CaminhoPadrao)!, "filtro-aprendido.txt");

    public static List<string> Carregar(string? caminho = null)
    {
        try
        {
            string c = caminho ?? CaminhoPadrao;
            if (!File.Exists(c)) return [];
            return File.ReadAllLines(c).Select(Sites.Normalizar).OfType<string>()
                .Distinct(StringComparer.Ordinal).TakeLast(MaximoAprendidos).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Muda quando a lista fixa ou os sites aprendidos mudam (o agente só reaplica a regra de DNS quando precisa).</summary>
    public static string Assinatura(IEnumerable<string> aprendidos) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            FiltroPermanente.Assinatura + "\n" + string.Join("\n", aprendidos))))[..16];

    public static void Salvar(IEnumerable<string> sites, string? caminho = null)
    {
        string c = caminho ?? CaminhoPadrao;
        Directory.CreateDirectory(Path.GetDirectoryName(c)!);
        File.WriteAllLines(c, sites.TakeLast(MaximoAprendidos));
    }
}
