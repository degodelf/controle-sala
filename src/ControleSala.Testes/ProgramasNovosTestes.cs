using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class ProgramasNovosTestes
{
    [Fact]
    public void Lista_inicial_tem_chrome_e_edge_e_eles_passam_na_validacao()
    {
        Assert.Contains(Programas.Padrao, p => p.Comando == "chrome.exe" && p.NomeProcesso == "chrome");
        Assert.Contains(Programas.Padrao, p => p.Comando == "msedge.exe" && p.NomeProcesso == "msedge");
        Assert.All(Programas.Padrao, p => Assert.Null(Programas.ValidarComando(p.Comando)));
    }

    [Fact]
    public void Lista_antiga_ganha_chrome_e_edge_uma_vez_so()
    {
        var lista = new List<ProgramaPermitido> { new("Calculadora", "calc.exe") };
        var oferecidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(Programas.AcrescentarNovos(lista, oferecidos));
        Assert.Contains(lista, p => p.NomeProcesso == "chrome");
        Assert.Contains(lista, p => p.NomeProcesso == "msedge");

        // O professor tirou o Chrome: não volta na próxima vez.
        lista.RemoveAll(p => p.NomeProcesso == "chrome");
        Assert.False(Programas.AcrescentarNovos(lista, oferecidos));
        Assert.DoesNotContain(lista, p => p.NomeProcesso == "chrome");
    }

    [Fact]
    public void Quem_ja_tinha_o_chrome_com_outro_nome_nao_ganha_repetido()
    {
        var lista = new List<ProgramaPermitido> { new("Navegador", @"C:\Program Files\Google\Chrome\Application\chrome.exe") };
        Programas.AcrescentarNovos(lista, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Single(lista, p => p.NomeProcesso.Equals("chrome", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Carregar_lista_salva_acrescenta_e_guarda_que_ja_ofereceu()
    {
        string pasta = Path.Combine(Path.GetTempPath(), "cs-programas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            string arquivo = Path.Combine(pasta, "programas.json");
            Programas.Salvar([new("Paint", "mspaint.exe")], arquivo);
            var primeira = Programas.Carregar(arquivo);
            Assert.Equal(3, primeira.Count);
            Programas.Salvar(primeira.Where(p => p.NomeProcesso != "msedge"), arquivo); // professor tirou o Edge
            Assert.DoesNotContain(Programas.Carregar(arquivo), p => p.NomeProcesso == "msedge");
        }
        finally { Directory.Delete(pasta, recursive: true); }
    }
}
