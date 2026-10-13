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
}
