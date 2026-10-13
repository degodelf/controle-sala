using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class VerificadorAtualizacaoTestes
{
    static string Json(string tag, string assetNome = "ControleSala-Setup-9.9.9.exe", bool draft = false, bool prerelease = false, string body = "Novidades") =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "body": "{{body}}",
          "assets": [
            { "name": "checksums.txt", "browser_download_url": "https://x/checksums.txt" },
            { "name": "{{assetNome}}", "browser_download_url": "https://x/{{assetNome}}" }
          ]
        }
        """;

    [Fact]
    public void Versao_mais_nova_com_instalador_e_detectada()
    {
        var a = VerificadorAtualizacao.Analisar(Json("v0.4.0"), "0.3.0");
        Assert.NotNull(a);
        Assert.Equal("0.4.0", a!.Versao);
        Assert.Equal("https://x/ControleSala-Setup-9.9.9.exe", a.UrlInstalador);
        Assert.Equal("Novidades", a.Notas);
    }

    [Theory]
    [InlineData("v0.3.0")]  // igual
    [InlineData("v0.2.9")]  // mais antiga
    [InlineData("0.3.0")]   // sem o v, igual
    public void Versao_igual_ou_antiga_nao_avisa(string tag) =>
        Assert.Null(VerificadorAtualizacao.Analisar(Json(tag), "0.3.0"));

    [Fact]
    public void Rascunho_e_pre_lancamento_sao_ignorados()
    {
        Assert.Null(VerificadorAtualizacao.Analisar(Json("v0.4.0", draft: true), "0.3.0"));
        Assert.Null(VerificadorAtualizacao.Analisar(Json("v0.4.0", prerelease: true), "0.3.0"));
    }

    [Fact]
    public void Sem_instalador_anexado_nao_avisa()
    {
        var json = """{ "tag_name": "v0.4.0", "assets": [ { "name": "leiame.txt", "browser_download_url": "https://x/leiame.txt" } ] }""";
        Assert.Null(VerificadorAtualizacao.Analisar(json, "0.3.0"));
    }

    [Fact]
    public void Json_invalido_ou_vazio_nao_quebra()
    {
        Assert.Null(VerificadorAtualizacao.Analisar("não é json", "0.3.0"));
        Assert.Null(VerificadorAtualizacao.Analisar("[]", "0.3.0"));
        Assert.Null(VerificadorAtualizacao.Analisar("{}", "0.3.0"));
    }

    [Fact]
    public void Versao_atual_desconhecida_avisa_qualquer_versao() =>
        Assert.NotNull(VerificadorAtualizacao.Analisar(Json("v0.1.0"), null));
}
