using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class FiltroPermanenteTestes
{
    [Fact]
    public void Lista_e_toda_valida_e_pega_apostas_de_fora_e_adultos()
    {
        foreach (var e in FiltroPermanente.Enderecos)
            Assert.True(Sites.Normalizar(e) == e || Sites.NormalizarTerminacao(e) != null, $"endereço inválido: {e}");
        var (dominios, terminacoes) = FiltroPermanente.Listas();
        Assert.Equal(FiltroPermanente.Enderecos.Count, dominios.Count + terminacoes.Count); // nada se perdeu na validação
        Assert.Contains("bet.br", terminacoes);   // toda casa autorizada no Brasil
        Assert.Contains("bet", terminacoes);
        Assert.Contains("xxx", terminacoes);
        Assert.Contains("888casino.com", dominios); // do exterior
        Assert.Contains("1xbet.com", dominios);
        Assert.Contains("pornhub.com", dominios);
    }

    [Fact]
    public void Regra_de_dns_do_filtro_e_separada_da_do_professor()
    {
        var (dominios, terminacoes) = FiltroPermanente.Listas();
        string cmd = Sites.ComandoRegraDns(Sites.NomesDaRegraDns(dominios, terminacoes), FiltroPermanente.ComentarioRegraDns);
        Assert.Contains($"$_.Comment -eq '{FiltroPermanente.ComentarioRegraDns}'", cmd);
        Assert.DoesNotContain(Sites.ComentarioRegraDns, cmd); // não tira a regra dos sites do professor
        Assert.NotEqual(Sites.ComentarioRegraDns, FiltroPermanente.ComentarioRegraDns);
        Assert.True(cmd.Length < 30_000, "o comando precisa caber na linha de comando do Windows");
    }

    [Fact]
    public void Liberar_a_internet_nao_tira_o_filtro_do_hosts()
    {
        string hosts = "127.0.0.1 localhost\r\n";
        hosts = Sites.MontarHosts(hosts, ["pornhub.com"], FiltroPermanente.InicioHosts, FiltroPermanente.FimHosts);
        hosts = Sites.MontarHosts(hosts, ["youtube.com"]);               // professor bloqueia na aula
        Assert.Contains("0.0.0.0 pornhub.com", hosts);
        Assert.Contains("0.0.0.0 youtube.com", hosts);

        string liberado = Sites.RemoverSecao(hosts);                     // "Liberar a internet"
        Assert.DoesNotContain("youtube.com", liberado);
        Assert.Contains("0.0.0.0 pornhub.com", liberado);                // o filtro continua
        Assert.Contains(Sites.MontarSecao(["pornhub.com"], FiltroPermanente.InicioHosts, FiltroPermanente.FimHosts)!, liberado);

        string semFiltro = Sites.RemoverSecao(liberado, FiltroPermanente.InicioHosts, FiltroPermanente.FimHosts); // desinstalar
        Assert.Equal("127.0.0.1 localhost\r\n", semFiltro);
    }

    [Fact]
    public void Professor_bloquear_de_novo_nao_duplica_nem_tira_o_filtro()
    {
        string hosts = Sites.MontarHosts("", ["xvideos.com"], FiltroPermanente.InicioHosts, FiltroPermanente.FimHosts);
        hosts = Sites.MontarHosts(hosts, ["a.com"]);
        hosts = Sites.MontarHosts(hosts, ["b.com"]);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(hosts, System.Text.RegularExpressions.Regex.Escape(FiltroPermanente.InicioHosts)));
        Assert.Contains("0.0.0.0 xvideos.com", hosts);
        Assert.DoesNotContain("a.com", hosts);
    }

    [Fact]
    public void Trava_o_dns_seguro_nos_navegadores_comuns()
    {
        var p = FiltroPermanente.PoliticasNavegador;
        Assert.Contains(p, x => x.Chave.EndsWith(@"Google\Chrome") && x.Nome == "DnsOverHttpsMode" && (string)x.Valor == "off");
        Assert.Contains(p, x => x.Chave.EndsWith(@"Microsoft\Edge") && x.Nome == "BuiltInDnsClientEnabled" && (int)x.Valor == 0);
        Assert.Contains(p, x => x.Chave.EndsWith(@"Firefox\DNSOverHTTPS") && x.Nome == "Locked" && (int)x.Valor == 1);
        Assert.All(p, x => Assert.True(x.Chave.StartsWith(@"SOFTWARE\Policies\") && x.Valor is string or int));
    }

    [Fact]
    public void Assinatura_muda_so_com_a_lista() =>
        Assert.Matches("^[0-9A-F]{16}$", FiltroPermanente.Assinatura);
}
