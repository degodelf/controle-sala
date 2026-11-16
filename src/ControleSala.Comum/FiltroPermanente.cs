using System.Security.Cryptography;
using System.Text;

namespace ControleSala.Comum;

/// <summary>
/// Bloqueio que fica sempre ligado no PC do aluno, sem aparecer no programa e sem aviso: casas de apostas (do Brasil e do
/// exterior) e sites adultos (+18). É aplicado pelo agente ao ligar, separado dos sites que o professor bloqueia na aula,
/// então "Liberar a internet" não mexe nele. Só sai desinstalando o ControleSala.
/// As terminações inteiras pegam qualquer site com elas (.bet, .bet.br, .casino, .xxx, .porn...).
/// </summary>
public static class FiltroPermanente
{
    public const string ComentarioRegraDns = "ControleSala filtro";
    public const string InicioHosts = "# ControleSala — filtro (início)";
    public const string FimHosts = "# ControleSala — filtro (fim)";

    static readonly string[] Apostas =
    [
        // terminações só de jogo/aposta: toda casa autorizada no Brasil usa .bet.br
        "*.bet.br", "*.bet", "*.casino", "*.poker", "*.bingo",
        // exterior (sem autorização no Brasil, mas muito acessadas aqui)
        "888.com", "888casino.com", "888sport.com", "888poker.com", "888games.com", "888bets.com",
        "bet365.com", "1xbet.com", "1win.com", "22bet.com", "melbet.com", "mostbet.com", "betwinner.com", "4rabet.com",
        "linebet.com", "megapari.com", "betandyou.com", "parimatch.com", "stake.com", "stake.us", "rollbit.com", "roobet.com",
        "bc.game", "sportsbet.io", "vbet.com", "betway.com", "bwin.com", "unibet.com", "williamhill.com", "ladbrokes.com",
        "paddypower.com", "betvictor.com", "betfair.com", "pinnacle.com", "pokerstars.com", "leovegas.com", "casumo.com",
        "betsson.com", "draftkings.com", "fanduel.com", "betmgm.com", "brazino777.com", "blaze.com", "jonbet.com",
        // brasileiras com endereço antigo (.com), além do .bet.br
        "betano.com", "sportingbet.com", "pixbet.com", "estrelabet.com", "esportesdasorte.com", "kto.com", "betnacional.com",
        "novibet.com", "superbet.com", "vaidebet.com", "casadeapostas.com", "betsul.com", "aposta1.com", "br4bet.com",
        "bet7k.com", "lotogreen.com", "pagbet.com", "betboo.com", "rivalo.com", "betmotion.com",
    ];

    static readonly string[] Adultos =
    [
        // terminações só de conteúdo adulto
        "*.xxx", "*.porn", "*.sex", "*.adult",
        "pornhub.com", "phncdn.com", "xvideos.com", "xvideos2.com", "xvideos3.com", "xnxx.com", "xhamster.com", "redtube.com",
        "youporn.com", "tube8.com", "spankbang.com", "eporner.com", "youjizz.com", "tnaflix.com", "drtuber.com", "beeg.com",
        "txxx.com", "hclips.com", "porntrex.com", "porn.com", "sex.com", "motherless.com", "pornpics.com", "imagefap.com",
        "brazzers.com", "bangbros.com", "realitykings.com", "naughtyamerica.com", "onlyfans.com", "fansly.com",
        "chaturbate.com", "stripchat.com", "bongacams.com", "livejasmin.com", "cam4.com", "camsoda.com", "myfreecams.com",
        "nhentai.net", "e-hentai.org", "literotica.com", "brasileirinhas.com.br", "sexlog.com", "cameraprive.com",
    ];

    /// <summary>Uma política de navegador (registro do Windows, em HKLM). Valor string = REG_SZ; int = REG_DWORD.</summary>
    public sealed record Politica(string Chave, string Nome, object Valor);

    /// <summary>
    /// Trava o "DNS seguro" dos navegadores: com ele ligado num provedor escolhido pelo aluno, o navegador pularia o hosts e a
    /// regra de DNS. Chrome, Edge e Brave passam a usar o DNS do Windows; o Firefox fica com o DNS seguro desligado e travado.
    /// O navegador mostra "gerenciado pela sua organização" (é o texto dele, não dá para trocar).
    /// </summary>
    public static IReadOnlyList<Politica> PoliticasNavegador { get; } =
    [
        new(@"SOFTWARE\Policies\Google\Chrome", "DnsOverHttpsMode", "off"),
        new(@"SOFTWARE\Policies\Google\Chrome", "BuiltInDnsClientEnabled", 0),
        new(@"SOFTWARE\Policies\Microsoft\Edge", "DnsOverHttpsMode", "off"),
        new(@"SOFTWARE\Policies\Microsoft\Edge", "BuiltInDnsClientEnabled", 0),
        new(@"SOFTWARE\Policies\BraveSoftware\Brave", "DnsOverHttpsMode", "off"),
        new(@"SOFTWARE\Policies\BraveSoftware\Brave", "BuiltInDnsClientEnabled", 0),
        new(@"SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS", "Enabled", 0),
        new(@"SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS", "Locked", 1),
    ];

    /// <summary>Tudo o que fica bloqueado (sites com todos os subdomínios, e terminações \"*.x\").</summary>
    public static IReadOnlyList<string> Enderecos { get; } = [.. Apostas, .. Adultos];

    /// <summary>Os sites e as terminações, já validados.</summary>
    public static (List<string> Dominios, List<string> Terminacoes) Listas() => Sites.SepararTudo(string.Join(",", Enderecos));

    /// <summary>Muda quando a lista muda: o agente só reaplica a regra de DNS quando precisa (rodar o PowerShell leva uns segundos).</summary>
    public static string Assinatura { get; } =
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", Enderecos))))[..16];
}
