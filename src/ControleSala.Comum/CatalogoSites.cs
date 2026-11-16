using System.Globalization;
using System.Text;

namespace ControleSala.Comum;

/// <summary>
/// Sites conhecidos, cada um com todos os endereços que ele usa (o YouTube não é só youtube.com: os vídeos vêm de
/// googlevideo.com, as miniaturas de ytimg.com...). Fica dentro do programa, sem consultar a internet: funciona offline e
/// não manda para fora o que o professor digita. Na lista do professor, um site do catálogo é guardado como "@chave".
/// </summary>
public static class CatalogoSites
{
    public sealed record Item(string Chave, string Nome, string Categoria, string[] Enderecos, string[] Apelidos)
    {
        /// <summary>Como fica guardado na lista de sites do professor.</summary>
        public string Entrada => Prefixo + Chave;
    }

    public const string Prefixo = "@";

    // Só para organizar o catálogo (a janela mostra os sites um a um, pelo que o professor digita).
    // Apostas e sites +18 não estão aqui: ficam sempre bloqueados no PC do aluno (ver FiltroPermanente).
    public const string Redes = "Redes sociais", Mensagens = "Mensagens", Videos = "Vídeos e streaming",
        Jogos = "Jogos", Ia = "Inteligência artificial e respostas prontas", Musica = "Música";

    static Item I(string chave, string nome, string categoria, string enderecos, string apelidos = "") =>
        new(chave, nome, categoria, enderecos.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            apelidos.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static readonly IReadOnlyList<Item> Itens =
    [
        // ---- vídeos ----
        I("youtube", "YouTube", Videos,
            "youtube.com m.youtube.com music.youtube.com youtu.be youtube-nocookie.com youtubekids.com ytimg.com i.ytimg.com " +
            "s.ytimg.com googlevideo.com youtubei.googleapis.com youtube.googleapis.com yt3.ggpht.com yt3.googleusercontent.com",
            "yt, you tube"),
        I("tiktok", "TikTok", Videos, "tiktok.com m.tiktok.com vm.tiktok.com tiktokv.com tiktokcdn.com tiktokcdn-us.com musical.ly", "tik tok"),
        I("kwai", "Kwai", Videos, "kwai.com kwai.net kwaicdn.com"),
        I("twitch", "Twitch", Videos, "twitch.tv m.twitch.tv clips.twitch.tv ttvnw.net jtvnw.net twitchcdn.net"),
        I("netflix", "Netflix", Videos, "netflix.com nflxvideo.net nflximg.net nflxext.com nflxso.net"),
        I("primevideo", "Prime Video", Videos, "primevideo.com", "amazon prime"),
        I("disney", "Disney+", Videos, "disneyplus.com disney-plus.net dssott.com bamgrid.com", "disney plus"),
        I("globoplay", "Globoplay", Videos, "globoplay.globo.com"),
        I("max", "Max (HBO)", Videos, "max.com hbomax.com", "hbo"),
        I("crunchyroll", "Crunchyroll", Videos, "crunchyroll.com", "anime"),
        I("plutotv", "Pluto TV", Videos, "pluto.tv"),

        // ---- redes sociais ----
        I("instagram", "Instagram", Redes, "instagram.com cdninstagram.com ig.me instagr.am", "insta"),
        I("facebook", "Facebook", Redes, "facebook.com m.facebook.com fb.com fb.me messenger.com m.me", "face, messenger"),
        I("x", "X (Twitter)", Redes, "x.com twitter.com twimg.com t.co", "twitter"),
        I("threads", "Threads", Redes, "threads.net threads.com"),
        I("snapchat", "Snapchat", Redes, "snapchat.com sc-cdn.net", "snap"),
        I("pinterest", "Pinterest", Redes, "pinterest.com pinimg.com pin.it"),
        I("reddit", "Reddit", Redes, "reddit.com redd.it redditmedia.com redditstatic.com"),
        I("discord", "Discord", Redes, "discord.com discord.gg discordapp.com discordapp.net discord.media"),
        I("tumblr", "Tumblr", Redes, "tumblr.com"),

        // ---- mensagens ----
        I("whatsapp", "WhatsApp Web", Mensagens, "whatsapp.com web.whatsapp.com whatsapp.net wa.me", "zap, wpp"),
        I("telegram", "Telegram", Mensagens, "telegram.org web.telegram.org telegram.me t.me"),

        // ---- jogos ----
        I("roblox", "Roblox", Jogos, "roblox.com rbxcdn.com"),
        I("poki", "Poki", Jogos, "poki.com poki.com.br"),
        I("friv", "Friv", Jogos, "friv.com"),
        I("clickjogos", "Click Jogos", Jogos, "clickjogos.com.br"),
        I("jogos360", "Jogos 360", Jogos, "jogos360.com.br"),
        I("crazygames", "CrazyGames", Jogos, "crazygames.com crazygames.com.br"),
        I("miniclip", "Miniclip", Jogos, "miniclip.com"),
        I("y8", "Y8", Jogos, "y8.com"),
        I("coolmath", "Coolmath Games", Jogos, "coolmathgames.com"),
        I("agario", "Agar.io", Jogos, "agar.io"),
        I("slither", "Slither.io", Jogos, "slither.io"),
        I("krunker", "Krunker", Jogos, "krunker.io"),
        I("gartic", "Gartic", Jogos, "gartic.io gartic.com.br gartic.com"),
        I("steam", "Steam", Jogos, "steampowered.com steamcommunity.com steamstatic.com"),
        I("epicgames", "Epic Games", Jogos, "epicgames.com", "fortnite"),

        // ---- inteligência artificial / respostas prontas (para provas) ----
        I("chatgpt", "ChatGPT", Ia, "chatgpt.com openai.com oaistatic.com oaiusercontent.com", "openai, gpt"),
        I("gemini", "Gemini (Google)", Ia, "gemini.google.com bard.google.com", "bard"),
        I("copilot", "Copilot (Microsoft)", Ia, "copilot.microsoft.com"),
        I("claude", "Claude", Ia, "claude.ai"),
        I("perplexity", "Perplexity", Ia, "perplexity.ai"),
        I("deepseek", "DeepSeek", Ia, "deepseek.com"),
        I("metaai", "Meta AI", Ia, "meta.ai"),
        I("grok", "Grok", Ia, "grok.com"),
        I("brainly", "Brainly", Ia, "brainly.com.br brainly.com"),

        // ---- música ----
        I("spotify", "Spotify", Musica, "spotify.com scdn.co spotifycdn.com"),
        I("deezer", "Deezer", Musica, "deezer.com dzcdn.net"),
        I("soundcloud", "SoundCloud", Musica, "soundcloud.com sndcdn.com"),
    ];

    public static Item? PorEntrada(string entrada) =>
        entrada.StartsWith(Prefixo, StringComparison.Ordinal)
            ? Itens.FirstOrDefault(i => i.Chave.Equals(entrada[Prefixo.Length..], StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>
    /// Transforma a lista do professor no que vai para o agente: os endereços de cada site do catálogo, os sites digitados e
    /// as terminações (\"*.bet\"). Entradas que não servem (ou de um catálogo mais novo) são ignoradas.
    /// </summary>
    public static List<string> Expandir(IEnumerable<string> entradas)
    {
        var saida = new List<string>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entrada in entradas)
        {
            IEnumerable<string> enderecos = PorEntrada(entrada) is { } item ? item.Enderecos
                : entrada.StartsWith(Prefixo, StringComparison.Ordinal) ? [] : [entrada];
            foreach (var e in enderecos)
            {
                string? valido = Sites.NormalizarTerminacao(e) is { } t ? Sites.PrefixoTerminacao + t : Sites.Normalizar(e);
                if (valido != null && vistos.Add(valido)) saida.Add(valido);
            }
        }
        return saida;
    }

    /// <summary>Uma sugestão para o que o professor está digitando: um site do catálogo ou um endereço montado.</summary>
    public sealed record Sugestao(string Entrada, string Titulo, string Detalhe);

    /// <summary>
    /// Sugestões enquanto o professor digita: sites do catálogo que combinam (pelo nome, apelido ou endereço) e, se ele digitou
    /// só uma palavra (\"friv\"), os endereços prováveis (friv.com, friv.com.br).
    /// </summary>
    public static List<Sugestao> Sugerir(string? texto, int maximo = 8)
    {
        var resultado = new List<Sugestao>();
        string busca = Simplificar(texto);
        if (busca.Length == 0) return resultado;

        int Pontos(Item i)
        {
            string nome = Simplificar(i.Nome), chave = Simplificar(i.Chave);
            if (nome == busca || chave == busca) return 0;
            if (nome.StartsWith(busca) || chave.StartsWith(busca)) return 1;
            if (i.Apelidos.Any(a => Simplificar(a).StartsWith(busca))) return 2;
            if (i.Enderecos.Any(e => Simplificar(e).StartsWith(busca))) return 3;
            if (busca.Length >= 3 && (nome.Contains(busca) || i.Enderecos.Any(e => Simplificar(e).Contains(busca)))) return 4;
            return -1;
        }

        foreach (var (item, _) in Itens.Select(i => (i, p: Pontos(i))).Where(x => x.p >= 0).OrderBy(x => x.p).ThenBy(x => x.i.Nome))
            resultado.Add(new Sugestao(item.Entrada, item.Nome, Resumo(item)));

        string digitado = (texto ?? "").Trim();
        if (Sites.Normalizar(digitado) is { } dominio)
            resultado.Add(new Sugestao(dominio, dominio, "este endereço e todos os subdomínios dele"));
        else if (Sites.NormalizarTerminacao(digitado) is { } terminacao)
            resultado.Add(new Sugestao(Sites.PrefixoTerminacao + terminacao, $"Tudo que termina em .{terminacao}", "qualquer site com essa terminação"));
        else if (Sites.Normalizar(digitado + ".com") is { } comCom && !digitado.Contains('.'))
        {
            resultado.Add(new Sugestao(comCom, comCom, "este endereço e todos os subdomínios dele"));
            resultado.Add(new Sugestao(comCom + ".br", comCom + ".br", "este endereço e todos os subdomínios dele"));
        }
        return resultado.DistinctBy(s => s.Entrada).Take(maximo).ToList();
    }

    /// <summary>"youtube.com, youtu.be, googlevideo.com e mais 9".</summary>
    public static string Resumo(Item item)
    {
        var principais = item.Enderecos.Take(3).Select(e => e.StartsWith(Sites.PrefixoTerminacao, StringComparison.Ordinal) ? "tudo ." + e[2..] : e);
        int resto = item.Enderecos.Length - 3;
        return string.Join(", ", principais) + (resto > 0 ? $" e mais {resto}" : "");
    }

    /// <summary>Minúsculas, sem acento e sem espaço/ponto/hífen: "Click Jogos" e "clickjogos" se encontram.</summary>
    static string Simplificar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder();
        foreach (char c in s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
                sb.Append(c);
        return sb.ToString();
    }
}
