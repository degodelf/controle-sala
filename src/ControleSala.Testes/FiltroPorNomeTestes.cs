using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class FiltroPorNomeTestes
{
    [Theory]
    [InlineData("888game.es", "888game.es")]
    [InlineData("www.888game.es", "888game.es")]
    [InlineData("m.888game.es", "888game.es")]
    [InlineData("api.888game1.vip", "888game1.vip")]
    [InlineData("888game-cassino.br.com", "888game-cassino.br.com")]
    [InlineData("777game.cc", "777game.cc")]
    [InlineData("h5.9999win.com", "9999win.com")]
    [InlineData("bet365.es", "bet365.es")]
    [InlineData("1xbet.mobi", "1xbet.mobi")]
    [InlineData("br.betano.com", "betano.com")]
    [InlineData("pixbet.com", "pixbet.com")]
    [InlineData("vaidebet.com", "vaidebet.com")]
    [InlineData("brasilabet.com", "brasilabet.com")]
    [InlineData("fortune-tiger.online", "fortune-tiger.online")]
    [InlineData("plataformatigrinho.com.br", "plataformatigrinho.com.br")]
    [InlineData("cassinoonline.net", "cassinoonline.net")]
    [InlineData("casadeapostas.net", "casadeapostas.net")]
    [InlineData("static.pgsoft-games.com", "pgsoft-games.com")]
    [InlineData("pornhub.org", "pornhub.org")]
    [InlineData("xvideos4.com", "xvideos4.com")]
    public void Pega_aposta_e_adulto_pelo_nome(string nome, string bloquear) =>
        Assert.Equal(bloquear, FiltroPorNome.Suspeito(nome));

    [Fact]
    public void Palavra_so_no_subdominio_de_outro_site_bloqueia_so_aquele_endereco()
    {
        Assert.Equal("tigrinho.netlify.app", FiltroPorNome.Suspeito("tigrinho.netlify.app"));
        Assert.Equal("888game-pp.blogspot.com", FiltroPorNome.Suspeito("888game-pp.blogspot.com"));
    }

    [Theory]
    [InlineData("google.com")]
    [InlineData("www.youtube.com")]
    [InlineData("alphabet.com")]
    [InlineData("nflxvideo.net")]
    [InlineData("occasion.fr")]
    [InlineData("abc.xyz")]
    [InlineData("betterhelp.com")]
    [InlineData("bethesda.net")]
    [InlineData("tibet.org")]
    [InlineData("essex.ac.uk")]
    [InlineData("diabetes.org.br")]
    [InlineData("estudante.sed.sc.gov.br")]
    [InlineData("classroom.google.com")]
    [InlineData("canva.com")]
    [InlineData("pt.wikipedia.org")]
    [InlineData("scratch.mit.edu")]
    [InlineData("wordwall.net")]
    [InlineData("kahoot.it")]
    [InlineData("bingobaker.com")]
    [InlineData("planningpoker.com")]
    [InlineData("proposta.com.br")]
    [InlineData("resposta.net")]
    [InlineData("2048game.com")]
    [InlineData("1001jogos.com.br")]
    [InlineData("poki.com")]
    [InlineData("coolmathgames.com")]
    [InlineData("roblox.com")]
    [InlineData("windowsupdate.com")]
    [InlineData("github.com")]
    [InlineData("142.250.1.1")]
    [InlineData("localhost")]
    [InlineData("")]
    public void Nao_pega_site_comum(string nome) => Assert.Null(FiltroPorNome.Suspeito(nome));

    [Theory]
    [InlineData("google.com.br")]
    [InlineData("youtube.com")]
    [InlineData("globo.com")]
    [InlineData("g1.globo.com")]
    [InlineData("ge.globo.com")]
    [InlineData("sportv.globo.com")]
    [InlineData("uol.com.br")]
    [InlineData("mercadolivre.com.br")]
    [InlineData("instagram.com")]
    [InlineData("facebook.com")]
    [InlineData("whatsapp.com")]
    [InlineData("web.whatsapp.com")]
    [InlineData("wikipedia.org")]
    [InlineData("amazon.com.br")]
    [InlineData("shopee.com.br")]
    [InlineData("magazineluiza.com.br")]
    [InlineData("americanas.com.br")]
    [InlineData("olx.com.br")]
    [InlineData("casasbahia.com.br")]
    [InlineData("netflix.com")]
    [InlineData("nflxvideo.net")]
    [InlineData("spotify.com")]
    [InlineData("twitch.tv")]
    [InlineData("tiktok.com")]
    [InlineData("tiktokcdn.com")]
    [InlineData("x.com")]
    [InlineData("twitter.com")]
    [InlineData("linkedin.com")]
    [InlineData("gov.br")]
    [InlineData("sc.gov.br")]
    [InlineData("sed.sc.gov.br")]
    [InlineData("educacao.sc.gov.br")]
    [InlineData("enem.inep.gov.br")]
    [InlineData("loterias.caixa.gov.br")]
    [InlineData("bb.com.br")]
    [InlineData("itau.com.br")]
    [InlineData("nubank.com.br")]
    [InlineData("picpay.com")]
    [InlineData("mercadopago.com.br")]
    [InlineData("brasilescola.uol.com.br")]
    [InlineData("mundoeducacao.uol.com.br")]
    [InlineData("todamateria.com.br")]
    [InlineData("infoescola.com")]
    [InlineData("khanacademy.org")]
    [InlineData("duolingo.com")]
    [InlineData("office.com")]
    [InlineData("microsoft.com")]
    [InlineData("live.com")]
    [InlineData("outlook.com")]
    [InlineData("microsoftonline.com")]
    [InlineData("bing.com")]
    [InlineData("msn.com")]
    [InlineData("apple.com")]
    [InlineData("icloud.com")]
    [InlineData("stackoverflow.com")]
    [InlineData("w3schools.com")]
    [InlineData("chatgpt.com")]
    [InlineData("openai.com")]
    [InlineData("claude.ai")]
    [InlineData("gemini.google.com")]
    [InlineData("docs.google.com")]
    [InlineData("drive.google.com")]
    [InlineData("meet.google.com")]
    [InlineData("zoom.us")]
    [InlineData("teams.microsoft.com")]
    [InlineData("rbxcdn.com")]
    [InlineData("minecraft.net")]
    [InlineData("epicgames.com")]
    [InlineData("fortnite.com")]
    [InlineData("steampowered.com")]
    [InlineData("steamcommunity.com")]
    [InlineData("discord.com")]
    [InlineData("discordapp.com")]
    [InlineData("reddit.com")]
    [InlineData("pinterest.com")]
    [InlineData("imgur.com")]
    [InlineData("pixabay.com")]
    [InlineData("pixlr.com")]
    [InlineData("cloudflare.com")]
    [InlineData("akamaihd.net")]
    [InlineData("akamaized.net")]
    [InlineData("fastly.net")]
    [InlineData("gstatic.com")]
    [InlineData("googleapis.com")]
    [InlineData("googlevideo.com")]
    [InlineData("ytimg.com")]
    [InlineData("doubleclick.net")]
    [InlineData("googlesyndication.com")]
    [InlineData("googletagmanager.com")]
    [InlineData("facebook.net")]
    [InlineData("fbcdn.net")]
    [InlineData("cdninstagram.com")]
    [InlineData("sentry.io")]
    [InlineData("hotjar.com")]
    [InlineData("clarity.ms")]
    [InlineData("jsdelivr.net")]
    [InlineData("unpkg.com")]
    [InlineData("fontawesome.com")]
    [InlineData("recaptcha.net")]
    [InlineData("friv.com")]
    [InlineData("clickjogos.com.br")]
    [InlineData("jogos360.com.br")]
    [InlineData("crazygames.com")]
    [InlineData("miniclip.com")]
    [InlineData("y8.com")]
    [InlineData("kizi.com")]
    [InlineData("gartic.io")]
    [InlineData("skribbl.io")]
    [InlineData("agar.io")]
    [InlineData("slither.io")]
    [InlineData("krunker.io")]
    [InlineData("chess.com")]
    [InlineData("lichess.org")]
    [InlineData("brainly.com.br")]
    [InlineData("passeidireto.com")]
    [InlineData("studocu.com")]
    [InlineData("scribd.com")]
    [InlineData("quizlet.com")]
    [InlineData("quizizz.com")]
    [InlineData("padlet.com")]
    [InlineData("genially.com")]
    [InlineData("prezi.com")]
    [InlineData("mentimeter.com")]
    [InlineData("edpuzzle.com")]
    [InlineData("geogebra.org")]
    [InlineData("phet.colorado.edu")]
    [InlineData("code.org")]
    [InlineData("tinkercad.com")]
    [InlineData("arduino.cc")]
    [InlineData("espn.com.br")]
    [InlineData("lance.com.br")]
    [InlineData("placar.com.br")]
    [InlineData("flashscore.com")]
    [InlineData("sofascore.com")]
    [InlineData("transfermarkt.com.br")]
    [InlineData("bet.com")]
    [InlineData("betterment.com")]
    [InlineData("alfabetizacao.mec.gov.br")]
    [InlineData("casadocodigo.com.br")]
    [InlineData("middlesex.edu")]
    [InlineData("xboxlive.com")]
    [InlineData("playstation.com")]
    [InlineData("nintendo.com")]
    [InlineData("spotifycdn.com")]
    [InlineData("scdn.co")]
    [InlineData("deezer.com")]
    [InlineData("soundcloud.com")]
    public void Nao_pega_sites_populares(string nome) => Assert.Null(FiltroPorNome.Suspeito(nome));

    [Fact]
    public void Nenhum_site_do_catalogo_do_professor_e_confundido_com_aposta()
    {
        foreach (var item in CatalogoSites.Itens)
            foreach (var e in item.Enderecos.Where(e => !e.StartsWith(Sites.PrefixoTerminacao)))
                Assert.True(FiltroPorNome.Suspeito(e) is null, $"{item.Nome}: {e} seria bloqueado");
    }

    [Theory]
    [InlineData("pagina.exemplo.com.br", "exemplo.com.br")]
    [InlineData("a.b.888game.es", "888game.es")]
    [InlineData("x.br.com", "x.br.com")]
    [InlineData("exemplo.com", "exemplo.com")]
    [InlineData("site.bet.br", "site.bet.br")]
    public void Dominio_principal(string host, string principal) =>
        Assert.Equal(principal, FiltroPorNome.DominioPrincipal(host));

    [Fact]
    public void Ja_bloqueado_pela_lista_fixa_ou_pela_terminacao()
    {
        var (dominios, terminacoes) = FiltroPermanente.Listas();
        var lista = dominios.ToHashSet();
        Assert.True(FiltroPorNome.JaBloqueado("888game.es", lista, terminacoes));        // está na lista fixa
        Assert.True(FiltroPorNome.JaBloqueado("m.888casino.com", lista, terminacoes));   // subdomínio
        Assert.True(FiltroPorNome.JaBloqueado("qualquer.bet.br", lista, terminacoes));   // terminação
        Assert.False(FiltroPorNome.JaBloqueado("888game1.vip", lista, terminacoes));
        Assert.False(FiltroPorNome.JaBloqueado("888casino.com.evil", lista, terminacoes));
    }

    [Fact]
    public void Le_os_nomes_da_lista_do_ipconfig_em_portugues()
    {
        const string saida = """
            Configuração de IP do Windows

                www.888game.es
                ----------------------------------------
                Nome do Registro . . . . . . . . : www.888game.es
                Tipo de Registro . . . . . . . . : 5
                Vida Útil . . . . . . . . . . . .: 120
                Comprimento de Dados . . . . . . : 8
                Seção . . . . . . . . . . . . . .: Resposta
                Registro CNAME  . . . . . . . . .: 888game-es.cdn.cloudflare.net

                Nome do Registro . . . . . . . . : 888game-es.cdn.cloudflare.net
                Registro A (Host) . . . . . . . .: 104.21.32.10

                1.0.0.127.in-addr.arpa
            """;
        var nomes = FiltroPorNome.NomesNoTexto(saida).Distinct().ToList();
        Assert.Contains("888game.es", nomes);
        Assert.Contains("888game-es.cdn.cloudflare.net", nomes);
        Assert.DoesNotContain(nomes, n => n.StartsWith("104."));
        // o site e o endereço do CDN que ele usa (só aquele endereço: o cloudflare.net dos outros sites continua)
        Assert.Equal(["888game.es", "888game-es.cdn.cloudflare.net"], nomes.Select(FiltroPorNome.Suspeito).OfType<string>().Distinct());
    }

    [Fact]
    public void Sites_aprendidos_ficam_guardados_e_mudam_a_assinatura()
    {
        string pasta = Path.Combine(Path.GetTempPath(), "cs-teste-filtro-" + Guid.NewGuid().ToString("N"));
        string arquivo = Path.Combine(pasta, "filtro-aprendido.txt");
        try
        {
            Assert.Empty(FiltroPorNome.Carregar(arquivo));
            FiltroPorNome.Salvar(["888game1.vip", "lixo inválido", "777game.cc"], arquivo);
            Assert.Equal(["888game1.vip", "777game.cc"], FiltroPorNome.Carregar(arquivo));
            Assert.NotEqual(FiltroPorNome.Assinatura([]), FiltroPorNome.Assinatura(["888game1.vip"]));
            Assert.Equal(FiltroPorNome.Assinatura(["a.com"]), FiltroPorNome.Assinatura(["a.com"]));
        }
        finally
        {
            if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true);
        }
    }

    [Fact]
    public void Lista_fixa_tem_o_888game_e_tira_o_quic_dos_navegadores()
    {
        Assert.Contains("888game.es", FiltroPermanente.Enderecos);
        var p = FiltroPermanente.PoliticasNavegador;
        Assert.Contains(p, x => x.Chave.EndsWith(@"Google\Chrome") && x.Nome == "QuicAllowed" && (int)x.Valor == 0);
        Assert.Contains(p, x => x.Chave.EndsWith(@"Microsoft\Edge") && x.Nome == "QuicAllowed" && (int)x.Valor == 0);
    }
}
