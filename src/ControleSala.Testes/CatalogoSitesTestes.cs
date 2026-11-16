using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class CatalogoSitesTestes
{
    [Fact]
    public void Todo_endereco_do_catalogo_e_valido_e_as_chaves_nao_se_repetem()
    {
        foreach (var item in CatalogoSites.Itens)
            foreach (var e in item.Enderecos)
                Assert.True(Sites.Normalizar(e) == e || Sites.NormalizarTerminacao(e) != null, $"{item.Nome}: endereço inválido {e}");
        Assert.Equal(CatalogoSites.Itens.Count, CatalogoSites.Itens.Select(i => i.Chave.ToLowerInvariant()).Distinct().Count());
    }

    [Fact]
    public void YouTube_vai_com_todos_os_enderecos_dele()
    {
        var enderecos = CatalogoSites.Expandir(["@youtube"]);
        Assert.Contains("youtube.com", enderecos);
        Assert.Contains("youtu.be", enderecos);
        Assert.Contains("googlevideo.com", enderecos); // os vídeos em si
        Assert.Contains("ytimg.com", enderecos);       // as miniaturas
    }

    [Fact]
    public void Apostas_e_adultos_nao_aparecem_no_catalogo_do_professor()
    {
        // Ficam sempre bloqueados no aluno (FiltroPermanente), sem aparecer na lista nem nas sugestões.
        Assert.DoesNotContain(CatalogoSites.Itens, i => i.Enderecos.Any(e => FiltroPermanente.Enderecos.Contains(e)));
        Assert.DoesNotContain(CatalogoSites.Sugerir("bet"), s => s.Entrada.StartsWith(CatalogoSites.Prefixo));
        Assert.Null(CatalogoSites.PorEntrada("@apostas")); // lista antiga com "@apostas": ignorada
    }

    [Fact]
    public void Expandir_mistura_catalogo_sites_digitados_e_ignora_o_que_nao_conhece()
    {
        var enderecos = CatalogoSites.Expandir(["@tiktok", "exemplo.com", "@nao-existe", "@apostas", "lixo", "*.casino", "exemplo.com"]);
        Assert.Contains("tiktok.com", enderecos);
        Assert.Contains("exemplo.com", enderecos);
        Assert.Contains("*.casino", enderecos);
        Assert.DoesNotContain("lixo", enderecos);
        Assert.Single(enderecos, e => e == "exemplo.com");
    }

    [Fact]
    public void Lista_antiga_so_com_enderecos_continua_valendo()
    {
        Assert.Equal(["facebook.com", "youtube.com"], CatalogoSites.Expandir(["facebook.com", "youtube.com"]));
    }

    [Theory]
    [InlineData("you", "@youtube")]
    [InlineData("YouTube", "@youtube")]
    [InlineData("tik tok", "@tiktok")]
    [InlineData("insta", "@instagram")]
    [InlineData("click jogos", "@clickjogos")]
    [InlineData("twitter", "@x")]
    public void Sugere_o_site_do_catalogo_primeiro(string digitado, string esperado) =>
        Assert.Equal(esperado, CatalogoSites.Sugerir(digitado)[0].Entrada);

    [Fact]
    public void Palavra_solta_vira_endereco_com_e_com_br()
    {
        var sugestoes = CatalogoSites.Sugerir("meusite").Select(s => s.Entrada).ToList();
        Assert.Equal(["meusite.com", "meusite.com.br"], sugestoes);
    }

    [Fact]
    public void Endereco_digitado_e_terminacao_viram_sugestao()
    {
        Assert.Contains(CatalogoSites.Sugerir("https://www.exemplo.com.br/x"), s => s.Entrada == "exemplo.com.br");
        Assert.Contains(CatalogoSites.Sugerir("*.bet"), s => s.Entrada == "*.bet");
        Assert.Empty(CatalogoSites.Sugerir("   "));
    }
}
