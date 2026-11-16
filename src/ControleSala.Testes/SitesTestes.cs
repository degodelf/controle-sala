using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class SitesTestes
{
    [Theory]
    [InlineData("facebook.com", "facebook.com")]
    [InlineData("www.youtube.com", "youtube.com")]
    [InlineData("https://www.instagram.com/explore", "instagram.com")]
    [InlineData("HTTP://Exemplo.COM:8080/pagina", "exemplo.com")]
    [InlineData("  tiktok.com  ", "tiktok.com")]
    [InlineData("jogos.exemplo.com.br", "jogos.exemplo.com.br")]
    public void Normaliza_dominios(string entrada, string esperado) => Assert.Equal(esperado, Sites.Normalizar(entrada));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("semponto")]
    [InlineData(".com")]
    [InlineData("a..b.com")]
    [InlineData("site com espaço.com")]
    [InlineData("a_b.com")]
    public void Dominios_invalidos(string? entrada)
    {
        Assert.Null(Sites.Normalizar(entrada));
        Assert.False(Sites.EhValido(entrada));
    }

    [Fact]
    public void Separar_aceita_varios_separadores_e_remove_repetidos()
    {
        var lista = Sites.Separar("facebook.com, www.facebook.com\nyoutube.com  tiktok.com;facebook.com");
        Assert.Equal(["facebook.com", "youtube.com", "tiktok.com"], lista);
    }

    [Fact]
    public void Montar_hosts_adiciona_a_secao_com_o_site_e_o_www()
    {
        string hosts = "127.0.0.1 localhost\r\n";
        string novo = Sites.MontarHosts(hosts, ["facebook.com"]);
        Assert.Contains("127.0.0.1 localhost", novo);         // o conteúdo original fica
        Assert.Contains(Sites.Inicio, novo);
        Assert.Contains("0.0.0.0 facebook.com", novo);
        Assert.Contains("0.0.0.0 www.facebook.com", novo);
        Assert.Contains(Sites.Fim, novo);
    }

    [Fact]
    public void Montar_e_remover_volta_ao_original()
    {
        string hosts = "127.0.0.1 localhost\r\n::1 localhost\r\n";
        string bloqueado = Sites.MontarHosts(hosts, ["facebook.com", "youtube.com"]);
        string liberado = Sites.RemoverSecao(bloqueado);
        Assert.DoesNotContain(Sites.Inicio, liberado);
        Assert.DoesNotContain("facebook.com", liberado);
        Assert.Contains("127.0.0.1 localhost", liberado);
        Assert.Contains("::1 localhost", liberado);
    }

    [Fact]
    public void Montar_de_novo_substitui_a_secao_anterior_sem_duplicar()
    {
        string hosts = "127.0.0.1 localhost\r\n";
        string b1 = Sites.MontarHosts(hosts, ["facebook.com"]);
        string b2 = Sites.MontarHosts(b1, ["youtube.com"]);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(b2, System.Text.RegularExpressions.Regex.Escape(Sites.Inicio)));
        Assert.DoesNotContain("facebook.com", b2);
        Assert.Contains("0.0.0.0 youtube.com", b2);
    }

    [Fact]
    public void Lista_vazia_remove_a_secao()
    {
        string hosts = "127.0.0.1 localhost\r\n";
        string b = Sites.MontarHosts(hosts, ["facebook.com"]);
        Assert.Equal(Sites.RemoverSecao(b), Sites.MontarHosts(b, []));
    }

    [Fact]
    public void Salvar_e_carregar_a_lista()
    {
        string arq = Path.Combine(Path.GetTempPath(), "cs-sites-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Sites.Salvar(["facebook.com", "youtube.com"], arq);
            Assert.Equal(["facebook.com", "youtube.com"], Sites.Carregar(arq));
        }
        finally { File.Delete(arq); }
    }

    [Theory]
    [InlineData("*.bet", "bet")]
    [InlineData("*.BET.br", "bet.br")]
    [InlineData(" *.casino ", "casino")]
    public void Terminacoes_validas(string token, string esperado) => Assert.Equal(esperado, Sites.NormalizarTerminacao(token));

    [Theory]
    [InlineData("bet")]
    [InlineData("*.")]
    [InlineData("*..bet")]
    [InlineData("*.bet.")]
    [InlineData("*.b_t")]
    [InlineData("*.bet'; Remove-Item")]
    public void Terminacoes_invalidas(string token) => Assert.Null(Sites.NormalizarTerminacao(token));

    [Fact]
    public void Separar_tudo_distingue_sites_de_terminacoes()
    {
        var (dominios, terminacoes) = Sites.SepararTudo("youtube.com, *.bet.br,*.bet\nwww.tiktok.com  lixo *.bet");
        Assert.Equal(["youtube.com", "tiktok.com"], dominios);
        Assert.Equal(["bet.br", "bet"], terminacoes);
    }

    [Fact]
    public void Separar_antigo_ignora_terminacoes_como_um_agente_velho()
    {
        Assert.Equal(["youtube.com"], Sites.Separar("youtube.com,*.bet"));
    }

    [Fact]
    public void Regra_de_dns_pega_o_site_e_os_subdominios_sem_repetir_quem_ja_esta_coberto()
    {
        var nomes = Sites.NomesDaRegraDns(["youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be", "stake.bet", "x.bet.br"],
            ["bet", "bet.br"]);
        Assert.Contains("youtube.com", nomes);
        Assert.Contains(".youtube.com", nomes);   // m., music., qualquer subdomínio
        Assert.Contains("youtu.be", nomes);
        Assert.Contains(".youtu.be", nomes);
        Assert.Contains(".bet", nomes);
        Assert.Contains(".bet.br", nomes);
        Assert.DoesNotContain("m.youtube.com", nomes);  // já coberto por .youtube.com
        Assert.DoesNotContain("stake.bet", nomes);      // já coberto por .bet
        Assert.DoesNotContain("x.bet.br", nomes);
    }

    [Fact]
    public void Terminacao_dentro_de_outra_nao_repete()
    {
        // "bet.br" não fica embaixo de ".bet" (é .br), então as duas entram.
        Assert.Equal([".bet", ".bet.br"], Sites.NomesDaRegraDns([], ["bet", "bet.br"]));
        Assert.Equal([".br"], Sites.NomesDaRegraDns([], ["br", "bet.br"]));
    }

    [Fact]
    public void Comando_da_regra_de_dns_troca_so_a_regra_do_controlesala()
    {
        string cmd = Sites.ComandoRegraDns(["youtube.com", ".youtube.com"]);
        Assert.Contains($"$_.Comment -eq '{Sites.ComentarioRegraDns}'", cmd);
        Assert.Contains("Add-DnsClientNrptRule -Namespace @('youtube.com','.youtube.com')", cmd);
        Assert.Contains("-NameServers '127.0.0.1'", cmd);
        Assert.Contains("Clear-DnsClientCache", cmd);

        string liberar = Sites.ComandoRegraDns([]);
        Assert.Contains("Remove-DnsClientNrptRule", liberar);
        Assert.DoesNotContain("Add-DnsClientNrptRule", liberar);
    }

    [Fact]
    public void Comando_da_regra_de_dns_recusa_nome_que_escaparia_das_aspas() =>
        Assert.Throws<ArgumentException>(() => Sites.ComandoRegraDns(["x.com'; Remove-Item C:\\ -Recurse; '"]));
}
