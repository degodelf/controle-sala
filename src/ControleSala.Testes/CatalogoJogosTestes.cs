using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class CatalogoJogosTestes
{
    [Fact]
    public void Lista_e_toda_valida_e_sem_repeticao()
    {
        foreach (var s in CatalogoJogos.Sites)
            Assert.True(Sites.Normalizar(s) == s, $"site inválido: {s}");
        Assert.Equal(CatalogoJogos.Sites.Count, CatalogoJogos.Sites.Distinct().Count());
        Assert.Equal(CatalogoJogos.Sites.Count, CatalogoJogos.Dominios.Count);
        Assert.Contains("roblox.com", CatalogoJogos.Dominios);
        Assert.Contains("now.gg", CatalogoJogos.Dominios);
        Assert.Contains("RobloxPlayerBeta", CatalogoJogos.Programas);
    }

    [Fact]
    public void Educativos_nunca_sao_bloqueados()
    {
        foreach (var e in CatalogoJogos.Educativos)
            Assert.False(CatalogoJogos.EhJogo(e, []), $"{e} seria bloqueado");
        // Minecraft Education: o minecraft.net inteiro não pode entrar (education.minecraft.net é subdomínio dele)
        Assert.DoesNotContain("minecraft.net", CatalogoJogos.Dominios);
        Assert.DoesNotContain("Minecraft.Education", CatalogoJogos.Programas);
    }

    [Theory]
    [InlineData("google.com")]
    [InlineData("sites.google.com")]
    [InlineData("login.live.com")]
    [InlineData("office.com")]
    [InlineData("xboxlive.com")]
    [InlineData("minecraft.net")]
    [InlineData("youtube.com")]
    [InlineData("sed.sc.gov.br")]
    [InlineData("canva.com")]
    [InlineData("wikipedia.org")]
    [InlineData("github.io")]
    [InlineData("pokemon.com")]
    public void Sites_comuns_nao_sao_jogo(string site) => Assert.False(CatalogoJogos.EhJogo(site, []));

    [Theory]
    [InlineData("www.friv.com")]
    [InlineData("m.roblox.com")]
    [InlineData("c0.rbxcdn.com")]
    [InlineData("html5.gamedistribution.com")]
    [InlineData("classic.minecraft.net")]
    [InlineData("store.steampowered.com")]
    public void Sites_da_lista_e_subdominios_sao_jogo(string site) => Assert.True(CatalogoJogos.EhJogo(site, []));

    [Theory]
    [InlineData("friv5online.com", "friv5online.com")]
    [InlineData("www.frivland.net", "frivland.net")]
    [InlineData("unblocked-games-66.com", "unblocked-games-66.com")]
    [InlineData("poki2.online", "poki2.online")]
    [InlineData("roblox-unblocked.netlify.app", "roblox-unblocked.netlify.app")] // só aquele endereço, não o netlify.app
    public void Copias_de_jogo_sao_pegas_pelo_nome(string nome, string bloquear) =>
        Assert.Equal(bloquear, CatalogoJogos.Suspeito(nome));

    [Fact]
    public void Site_aprendido_tambem_conta()
    {
        Assert.False(CatalogoJogos.EhJogo("jogoqualquer.com", []));
        Assert.True(CatalogoJogos.EhJogo("www.jogoqualquer.com", ["jogoqualquer.com"]));
    }

    [Fact]
    public void Regra_de_dns_dos_jogos_cabe_na_linha_de_comando_e_e_separada()
    {
        string cmd = Sites.ComandoRegraDns(Sites.NomesDaRegraDns(CatalogoJogos.Dominios, []), CatalogoJogos.ComentarioRegraDns);
        Assert.True(cmd.Length < 30_000);
        Assert.Contains($"$_.Comment -eq '{CatalogoJogos.ComentarioRegraDns}'", cmd);
        Assert.NotEqual(Sites.ComentarioRegraDns, CatalogoJogos.ComentarioRegraDns);
        Assert.NotEqual(FiltroPermanente.ComentarioRegraDns, CatalogoJogos.ComentarioRegraDns);
    }

    [Fact]
    public void Liberar_a_internet_nao_tira_os_jogos_e_liberar_os_jogos_nao_tira_os_sites()
    {
        string hosts = Sites.MontarHosts("", ["youtube.com"]);
        hosts = Sites.MontarHosts(hosts, ["friv.com"], CatalogoJogos.InicioHosts, CatalogoJogos.FimHosts);
        hosts = Sites.MontarHosts(hosts, ["pornhub.com"], FiltroPermanente.InicioHosts, FiltroPermanente.FimHosts);

        string semSites = Sites.RemoverSecao(hosts);                                              // Liberar a internet
        Assert.Contains("0.0.0.0 friv.com", semSites);
        Assert.DoesNotContain("youtube.com", semSites);

        string semJogos = Sites.RemoverSecao(hosts, CatalogoJogos.InicioHosts, CatalogoJogos.FimHosts); // Liberar jogos
        Assert.Contains("0.0.0.0 youtube.com", semJogos);
        Assert.Contains("0.0.0.0 pornhub.com", semJogos);
        Assert.DoesNotContain("friv.com", semJogos);
    }

    [Theory]
    [InlineData(SituacaoJogos.Bloqueados, null, "bloqueados")]
    [InlineData(SituacaoJogos.Liberados, null, "liberados")]
    [InlineData(SituacaoJogos.Liberados, 900, "liberados:900")]
    [InlineData(SituacaoJogos.Nunca, null, "nunca")]
    public void Estado_vai_e_volta(SituacaoJogos situacao, int? segundos, string texto)
    {
        var estado = new EstadoJogos(situacao, segundos);
        Assert.Equal(texto, estado.Serializar());
        Assert.Equal(estado, EstadoJogos.Ler(texto));
    }

    [Fact]
    public void Estado_estranho_e_ignorado()
    {
        Assert.Null(EstadoJogos.Ler("talvez"));
        Assert.Null(EstadoJogos.Ler("liberados:-5"));
        Assert.Null(EstadoJogos.Ler(null));
    }

    [Fact]
    public void Selo_mostra_o_tempo_que_falta()
    {
        var agora = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal("🎮 Liberado", EstadoJogos.TextoDoSelo(null, agora));
        Assert.Equal("🎮 15 min", EstadoJogos.TextoDoSelo(agora.AddMinutes(15), agora));
        Assert.Equal("🎮 15 min", EstadoJogos.TextoDoSelo(agora.AddMinutes(14).AddSeconds(10), agora));
        Assert.Equal("🎮 1 min", EstadoJogos.TextoDoSelo(agora.AddSeconds(20), agora));
        Assert.Equal("🎮 1 min", EstadoJogos.TextoDoSelo(agora.AddSeconds(-5), agora));
    }

    [Fact]
    public void Argumentos_dos_comandos()
    {
        Assert.Equal("", ArgumentosJogos.Bloquear(false));
        Assert.True(ArgumentosJogos.Ler(ArgumentosJogos.Bloquear(true)).FecharNavegador);
        Assert.Equal(15, ArgumentosJogos.Ler(ArgumentosJogos.Liberar(15)).Minutos);
        Assert.Equal(0, ArgumentosJogos.Ler(ArgumentosJogos.Liberar(0)).Minutos);
        Assert.Equal(ArgumentosJogos.MaximoMinutos, ArgumentosJogos.Ler("minutos=99999").Minutos);
        Assert.Equal(0, ArgumentosJogos.Ler("minutos=abc; lixo").Minutos);
        Assert.True(ArgumentosJogos.Ler(ArgumentosJogos.Config(true)).BloquearAoLigar);
        Assert.False(ArgumentosJogos.Ler(ArgumentosJogos.Config(false)).BloquearAoLigar);
        Assert.Null(ArgumentosJogos.Ler("").BloquearAoLigar);
    }

    [Fact]
    public void Opcoes_do_professor_ficam_guardadas()
    {
        string arquivo = Path.Combine(Path.GetTempPath(), $"cs-jogos-{Guid.NewGuid():N}.json");
        try
        {
            Assert.False(ConfigJogos.Existe(arquivo));
            new ConfigJogos { FecharNavegador = true, BloquearAoLigar = true }.Salvar(arquivo);
            Assert.True(ConfigJogos.Existe(arquivo));
            var c = ConfigJogos.Carregar(arquivo);
            Assert.True(c.FecharNavegador);
            Assert.True(c.BloquearAoLigar);
        }
        finally { File.Delete(arquivo); }
    }

    [Fact]
    public void Jogos_nao_sao_reaplicados_pelo_professor_e_podem_repetir()
    {
        // o PC do aluno guarda a situação; reaplicar fecharia o navegador de novo a cada reconexão
        Assert.False(RegrasDeComando.Restritivo(AcaoComando.BloquearJogos, ""));
        Assert.Null(RegrasDeComando.AssuntoDoEstado(AcaoComando.BloquearJogos));
        Assert.True(RegrasDeComando.PodeRepetir(AcaoComando.BloquearJogos));
        Assert.True(RegrasDeComando.PodeRepetir(AcaoComando.LiberarJogos));
        Assert.Equal("Liberar jogos", RegistroAtividades.NomeDoComando(AcaoComando.LiberarJogos));
        Assert.Equal("por 15 min", RegistroAtividades.DetalheDoComando(AcaoComando.LiberarJogos, ArgumentosJogos.Liberar(15)));
        Assert.Equal("e fechou o navegador", RegistroAtividades.DetalheDoComando(AcaoComando.BloquearJogos, ArgumentosJogos.Bloquear(true)));
    }

    [Fact]
    public void Mensagens_dos_jogos_no_protocolo()
    {
        var msg = Protocolo.MontarEstadoJogos(new EstadoJogos(SituacaoJogos.Liberados, 600));
        Assert.Equal((byte)TipoMensagem.EstadoJogos, msg[4]);
        Assert.NotEqual((byte)TipoMensagem.ConfigJogos, (byte)TipoMensagem.EstadoJogos);
    }
}
