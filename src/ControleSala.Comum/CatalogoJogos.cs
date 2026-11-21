using System.Text.Json;

namespace ControleSala.Comum;

/// <summary>
/// O que o botão "Jogos" bloqueia: sites de joguinhos, jogos online, lojas e jogos instalados, e jogos que rodam pela nuvem.
/// Fica separado da lista de sites do professor (liberar um não mexe no outro) e do filtro permanente de apostas e +18.
/// Jogos educativos (Escola Games, Wordwall, Kahoot, Scratch, Code.org, GeoGebra, Minecraft Education...) NÃO entram.
/// </summary>
public static class CatalogoJogos
{
    public const string InicioHosts = "# ControleSala — jogos (início)";
    public const string FimHosts = "# ControleSala — jogos (fim)";
    public const string ComentarioRegraDns = "ControleSala jogos";

    /// <summary>Cada site vale com todos os subdomínios dele.</summary>
    public static IReadOnlyList<string> Sites { get; } =
    [
        // sites de joguinhos
        "friv.com", "friv.com.br", "friv2online.com", "friv5online.com", "frivclassic.com",
        "poki.com", "poki.com.br", "poki-gdn.com", "poki-cdn.com",
        "clickjogos.com.br", "clickjogos.uol.com.br", "jogos.uol.com.br", "jogos360.com.br", "ojogos.com.br",
        "1001jogos.com.br", "1001games.com", "papajogos.com.br", "jogalo.com",
        "crazygames.com", "crazygames.com.br", "coolmathgames.com", "coolmath-games.com", "miniclip.com", "y8.com", "kizi.com",
        "agame.com", "gamepix.com", "itch.io", "itch.zone", "armorgames.com", "kongregate.com", "newgrounds.com", "ngfiles.com",
        "addictinggames.com", "silvergames.com", "gameflare.com", "lagged.com", "gamesgames.com", "snokido.com", "gogy.com",
        "kbhgames.com", "plays.org", "gamejolt.com", "gamejolt.net", "html5games.com", "playhop.com",
        // quem entrega os jogos para muitos desses sites (pega os jogos embutidos em outros sites também)
        "gamedistribution.com", "gamemonetize.com", "famobi.com", "softgames.com",
        // jogos .io e online de navegador
        "krunker.io", "slither.io", "agar.io", "diep.io", "shellshock.io", "shellshockers.com", "zombsroyale.io", "surviv.io",
        "moomoo.io", "paper-io.com", "paper.io", "hole-io.com", "smashkarts.io", "bloxd.io", "miniblox.io", "1v1.lol",
        "venge.io", "ev.io", "kirka.io", "deeeep.io", "starve.io", "wormate.io", "yohoho.io", "taming.io", "littlebigsnake.com",
        "gartic.io", "gartic.com.br", "skribbl.io", "classic.minecraft.net",
        // Roblox, lojas e jogos instalados
        "roblox.com", "rbxcdn.com", "rbx.com", "robloxlabs.com",
        "epicgames.com", "fortnite.com", "steampowered.com", "steamcommunity.com", "steamstatic.com",
        "garena.com", "freefiremobile.com", "riotgames.com", "leagueoflegends.com", "playvalorant.com",
        "battle.net", "blizzard.com", "ea.com", "ubisoft.com", "ubi.com", "gog.com",
        // jogos que rodam pela nuvem (o now.gg é o jeito comum de jogar Roblox sem instalar)
        "now.gg", "nowgg.me", "xbox.com", "geforcenow.com", "boosteroid.com", "cloudmoonapp.com", "easyfun.gg",
    ];

    /// <summary>Jogos instalados (nome do processo, sem .exe): fechados na hora e enquanto os jogos estiverem bloqueados.</summary>
    public static IReadOnlyList<string> Programas { get; } =
    [
        "RobloxPlayerBeta", "RobloxPlayerLauncher", "Windows10Universal", // o Roblox da Microsoft Store roda com este nome
        "Minecraft.Windows", "MinecraftLauncher",                          // o Minecraft Education (Minecraft.Education) fica
        "steam", "EpicGamesLauncher", "FortniteClient-Win64-Shipping", "FortniteLauncher",
        "RiotClientServices", "LeagueClient", "VALORANT-Win64-Shipping", "Battle.net", "EADesktop", "upc", "GalaxyClient",
        "XboxPcApp", "GeForceNOW",
        // emuladores de celular (Free Fire e outros jogos de celular no PC)
        "HD-Player", "BlueStacks", "dnplayer", "Nox", "MEmu", "AndroidEmulatorEx", "AndroidEmulatorEn",
        "Solitaire",
    ];

    /// <summary>Educativos que nunca podem ser bloqueados por engano (conferido nos testes).</summary>
    public static IReadOnlyList<string> Educativos { get; } =
    [
        "escolagames.com.br", "wordwall.net", "kahoot.it", "kahoot.com", "scratch.mit.edu", "code.org", "geogebra.org",
        "education.minecraft.net", "khanacademy.org", "quizizz.com", "typingclub.com", "duolingo.com", "chess.com", "lichess.org",
        "geoguessr.com", "rachacuca.com.br", "studio.code.org", "phet.colorado.edu", "tinkercad.com", "makecode.microbit.org",
    ];

    // Enquanto os jogos estão bloqueados, o nome do site também conta: pega as cópias do Friv e os "unblocked games".
    static readonly string[] Palavras =
    [
        "friv", "poki", "unblocked", "crazygames", "coolmath", "jogos360", "clickjogos", "1001jogos", "miniclip",
        "roblox", "rbxcdn", "krunker", "slither", "shellshock", "fortnite", "gamedistribution", "gamemonetize", "joguinhos",
    ];

    static readonly (List<string> Dominios, List<string> Terminacoes) _listas = Comum.Sites.SepararTudo(string.Join(",", Sites));

    /// <summary>Os sites já validados (sem terminações inteiras: nenhuma é só de jogos).</summary>
    public static List<string> Dominios => _listas.Dominios;

    static bool Parece(string pedaco)
    {
        string p = pedaco.ToLowerInvariant().Replace("-", "");
        foreach (var palavra in Palavras)
            if (p.Contains(palavra, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// O que bloquear por causa deste nome (o site inteiro, ou só o endereço se a palavra estiver no subdomínio de outro
    /// site), ou null se ele não parece de jogo. Igual ao <see cref="FiltroPorNome.Suspeito(string?)"/>, com outras palavras.
    /// </summary>
    public static string? Suspeito(string? nome) => FiltroPorNome.Suspeito(nome, Parece);

    /// <summary>O nome é de um jogo da lista (ele ou um site "pai" dele), de um aprendido, ou tem cara de jogo?</summary>
    public static bool EhJogo(string nome, IReadOnlyCollection<string> aprendidos)
    {
        if (Comum.Sites.Normalizar(nome) is not { } host) return false;
        return FiltroPorNome.JaBloqueado(host, [.. Dominios, .. aprendidos], []) || Suspeito(host) != null;
    }
}

/// <summary>Como estão os jogos num PC (o agente manda ao professor ao conectar e quando muda).</summary>
public enum SituacaoJogos { Nunca = 0, Bloqueados = 1, Liberados = 2 }

/// <param name="SegundosRestantes">Liberados por um tempo: quanto falta (conta a partir de quando a mensagem foi montada).</param>
public readonly record struct EstadoJogos(SituacaoJogos Situacao, int? SegundosRestantes = null)
{
    public string Serializar() => Situacao switch
    {
        SituacaoJogos.Bloqueados => "bloqueados",
        SituacaoJogos.Liberados when SegundosRestantes is { } s => $"liberados:{Math.Max(0, s)}",
        SituacaoJogos.Liberados => "liberados",
        _ => "nunca",
    };

    /// <summary>O selo no quadrinho do PC: "🎮 Liberado" (de vez) ou "🎮 12 min" (o que falta, arredondado para cima).</summary>
    public static string TextoDoSelo(DateTime? ateUtc, DateTime agoraUtc)
    {
        if (ateUtc is not { } ate) return "🎮 Liberado";
        int minutos = (int)Math.Ceiling((ate - agoraUtc).TotalMinutes);
        return minutos <= 1 ? "🎮 1 min" : $"🎮 {minutos} min";
    }

    public static EstadoJogos? Ler(string? texto)
    {
        string t = (texto ?? "").Trim().ToLowerInvariant();
        if (t == "bloqueados") return new(SituacaoJogos.Bloqueados);
        if (t == "liberados") return new(SituacaoJogos.Liberados);
        if (t == "nunca") return new(SituacaoJogos.Nunca);
        if (t.StartsWith("liberados:", StringComparison.Ordinal) && int.TryParse(t[10..], out int s) && s >= 0)
            return new(SituacaoJogos.Liberados, s);
        return null;
    }
}

/// <summary>Argumentos dos comandos de jogos ("navegador=1", "minutos=15") e da configuração ("ligar=1").</summary>
public static class ArgumentosJogos
{
    /// <summary>Liberar por um tempo: no máximo 4 horas (passou disso, é liberar de vez).</summary>
    public const int MaximoMinutos = 240;

    public static string Bloquear(bool fecharNavegador) => fecharNavegador ? "navegador=1" : "";
    public static string Liberar(int minutos) => minutos > 0 ? $"minutos={Math.Min(minutos, MaximoMinutos)}" : "";
    public static string Config(bool bloquearAoLigar) => bloquearAoLigar ? "ligar=1" : "ligar=0";

    public static (bool FecharNavegador, int Minutos, bool? BloquearAoLigar) Ler(string? argumento)
    {
        bool navegador = false;
        int minutos = 0;
        bool? ligar = null;
        foreach (var parte in (argumento ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (chave, valor) = parte.IndexOf('=') is var i and > 0 ? (parte[..i].Trim(), parte[(i + 1)..].Trim()) : (parte, "");
            switch (chave.ToLowerInvariant())
            {
                case "navegador": navegador = valor == "1"; break;
                case "minutos" when int.TryParse(valor, out int m) && m > 0: minutos = Math.Min(m, MaximoMinutos); break;
                case "ligar": ligar = valor == "1"; break;
            }
        }
        return (navegador, minutos, ligar);
    }
}

/// <summary>Opções do professor para os jogos, guardadas no PC dele.</summary>
public sealed class ConfigJogos
{
    /// <summary>Ao bloquear, fecha também o navegador (o jogo de navegador que já carregou inteiro para na hora).</summary>
    public bool FecharNavegador { get; set; }

    /// <summary>Os PCs dos alunos já ligam com os jogos bloqueados (sem o professor precisar lembrar a cada aula).</summary>
    public bool BloquearAoLigar { get; set; }

    static string Caminho => Path.Combine(Pastas.Dados, "jogos.json");

    public static bool Existe(string? caminho = null) => File.Exists(caminho ?? Caminho);

    public static ConfigJogos Carregar(string? caminho = null)
    {
        try
        {
            string c = caminho ?? Caminho;
            if (File.Exists(c)) return JsonSerializer.Deserialize<ConfigJogos>(File.ReadAllText(c)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        return new();
    }

    public void Salvar(string? caminho = null)
    {
        string c = caminho ?? Caminho;
        Directory.CreateDirectory(Path.GetDirectoryName(c)!);
        File.WriteAllText(c, JsonSerializer.Serialize(this));
    }
}
