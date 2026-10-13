using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class LimpezaConfigTestes
{
    [Fact]
    public void Serializar_e_desserializar()
    {
        var c = new LimpezaConfig { Downloads = true, Documentos = false, Excecoes = ["Trabalhos"], Agendada = true, Dias = [1, 3, 5], Hora = "17:30" };
        var volta = LimpezaConfig.Desserializar(c.Serializar())!;
        Assert.True(volta.Downloads);
        Assert.False(volta.Documentos);
        Assert.Equal(["Trabalhos"], volta.Excecoes);
        Assert.Equal([1, 3, 5], volta.Dias);
        Assert.Equal("17:30", volta.Hora);
    }

    [Fact]
    public void Serializar_inclui_opcoes_do_defender()
    {
        var c = new LimpezaConfig { AtualizarDefender = true, VerificarDefender = true, DefenderVarreduraCompleta = true };
        var volta = LimpezaConfig.Desserializar(c.Serializar())!;
        Assert.True(volta.AtualizarDefender);
        Assert.True(volta.VerificarDefender);
        Assert.True(volta.DefenderVarreduraCompleta);
    }

    [Fact]
    public void Defender_desligado_por_padrao()
    {
        var c = new LimpezaConfig();
        Assert.False(c.AtualizarDefender);
        Assert.False(c.VerificarDefender);
    }

    [Theory]
    [InlineData("18:00", true)]
    [InlineData("07:05", true)]
    [InlineData("23:59", true)]
    [InlineData("24:00", false)]
    [InlineData("7:00", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void Hora_valida(string hora, bool esperado) => Assert.Equal(esperado, LimpezaConfig.HoraValida(hora));

    [Fact]
    public void Resumo_agenda()
    {
        Assert.Equal("Agendamento desligado", new LimpezaConfig { Agendada = false }.ResumoAgenda());
        Assert.Equal("Seg, Qua, Sex às 18:00", new LimpezaConfig { Agendada = true, Dias = [5, 1, 3], Hora = "18:00" }.ResumoAgenda());
    }

    [Fact]
    public void Desserializar_lixo_devolve_nulo() => Assert.Null(LimpezaConfig.Desserializar("não é json"));
}

public sealed class LimpezaArquivosTestes : IDisposable
{
    readonly string _raiz = Path.Combine(Path.GetTempPath(), "cs-limpeza-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, true); }

    string Criar(params string[] partes)
    {
        string p = Path.Combine([_raiz, .. partes]);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void Esvaziar_apaga_arquivos_e_subpastas()
    {
        Criar("Downloads", "a.pdf");
        Criar("Downloads", "sub", "b.txt");
        int n = LimpezaArquivos.EsvaziarPasta(Path.Combine(_raiz, "Downloads"), []);
        Assert.True(n >= 2);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_raiz, "Downloads")));
        Assert.True(Directory.Exists(Path.Combine(_raiz, "Downloads"))); // a pasta em si fica
    }

    [Fact]
    public void Esvaziar_respeita_excecoes()
    {
        Criar("Documents", "apagar.txt");
        Criar("Documents", "Trabalhos", "prova.docx");
        Criar("Documents", "NÃO APAGAR.pdf");
        LimpezaArquivos.EsvaziarPasta(Path.Combine(_raiz, "Documents"), ["Trabalhos", "NÃO APAGAR.pdf"]);
        Assert.False(File.Exists(Path.Combine(_raiz, "Documents", "apagar.txt")));
        Assert.True(Directory.Exists(Path.Combine(_raiz, "Documents", "Trabalhos")));      // exceção por pasta
        Assert.True(File.Exists(Path.Combine(_raiz, "Documents", "NÃO APAGAR.pdf")));        // exceção por arquivo
    }

    [Fact]
    public void Excecao_nao_diferencia_maiusculas()
    {
        Criar("Desktop", "MeuTrabalho");
        LimpezaArquivos.EsvaziarPasta(Path.Combine(_raiz, "Desktop"), ["meutrabalho"]);
        Assert.True(File.Exists(Path.Combine(_raiz, "Desktop", "MeuTrabalho")));
    }

    [Fact]
    public void Esvaziar_pasta_inexistente_nao_quebra() =>
        Assert.Equal(0, LimpezaArquivos.EsvaziarPasta(Path.Combine(_raiz, "NaoExiste"), []));

    [Fact]
    public void Remover_apaga_readonly()
    {
        string f = Criar("ro.txt");
        File.SetAttributes(f, FileAttributes.ReadOnly);
        Assert.True(LimpezaArquivos.Remover(f));
        Assert.False(File.Exists(f));
    }

    [Fact]
    public void Pastas_para_esvaziar_segue_a_config()
    {
        var c = new LimpezaConfig { Downloads = true, AreaTrabalho = false, Documentos = true, Temporarios = true };
        var pastas = LimpezaArquivos.PastasParaEsvaziar("C:\\Users\\aluno", c);
        Assert.Contains("C:\\Users\\aluno\\Downloads", pastas);
        Assert.Contains("C:\\Users\\aluno\\Documents", pastas);
        Assert.Contains(Path.Combine("C:\\Users\\aluno", "AppData", "Local", "Temp"), pastas);
        Assert.DoesNotContain("C:\\Users\\aluno\\Desktop", pastas);
    }

    [Fact]
    public void Caminhos_de_navegadores_incluem_chrome_edge()
    {
        var alvos = LimpezaArquivos.CaminhosNavegadores("C:\\Users\\aluno");
        Assert.Contains(alvos, a => a.Contains("Chrome") && a.Contains("Cache"));
        Assert.Contains(alvos, a => a.Contains("Chrome") && a.EndsWith("Cookies"));
        Assert.Contains(alvos, a => a.Contains("Edge") && a.EndsWith("History"));
    }
}
