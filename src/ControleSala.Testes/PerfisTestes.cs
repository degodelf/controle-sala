using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public sealed class PerfisTestes : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "controlesala-testes-" + Guid.NewGuid().ToString("N"));
    readonly RelogioFalso _relogio = new();

    string Arquivo => Path.Combine(_pasta, "professores.json");
    RepositorioPerfis Novo() => new(Arquivo, _relogio);

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void Perfis_persistem_entre_aberturas_do_programa()
    {
        Novo().Adicionar("Diego");
        var perfil = Assert.Single(Novo().Perfis);
        Assert.Equal("Diego", perfil.Nome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nome_vazio_e_recusado(string nome) =>
        Assert.Throws<ArgumentException>(() => Novo().Adicionar(nome));

    [Fact]
    public void Nome_repetido_e_recusado_sem_diferenciar_maiusculas()
    {
        var repo = Novo();
        repo.Adicionar("Diego");
        Assert.Throws<ArgumentException>(() => repo.Adicionar("  diego "));
    }

    [Fact]
    public void Remover_nao_apaga_o_ultimo_professor()
    {
        var repo = Novo();
        var a = repo.Adicionar("Ana");
        var b = repo.Adicionar("Bia");
        Assert.True(repo.Remover(a));
        Assert.False(repo.Remover(b));
        Assert.Single(repo.Perfis);
    }

    [Fact]
    public void Arquivo_corrompido_vira_copia_e_recomeca_vazio()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Arquivo, "{ isto não é json");
        var repo = Novo();
        Assert.Empty(repo.Perfis);
        Assert.True(File.Exists(Arquivo + ".corrompido"));
        repo.Adicionar("Diego"); // e volta a funcionar
        Assert.Single(Novo().Perfis);
    }

    [Fact]
    public void Arquivo_de_versao_com_pin_abre_sem_senha_e_o_pin_some_do_disco()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Arquivo, """
            [{"Nome":"Diego","Sal":"Erf5SxzfAHRatBI1sH3Q3Q==","Resumo":"fHYJ5Q413PROOKqKX9tT/TW2Z1q4/VQup+1BNCx72kI=",
              "Iteracoes":600000,"Falhas":3,"BloqueadoAte":null,"RegrasAceitas":1,"RegrasAceitasEm":"2026-10-10T08:00:00+00:00"}]
            """);

        var perfil = Assert.Single(Novo().Perfis);

        Assert.Equal("Diego", perfil.Nome);
        Assert.Equal(1, perfil.RegrasAceitas);       // quem já aceitou as regras não precisa aceitar de novo
        string texto = File.ReadAllText(Arquivo);
        Assert.DoesNotContain("Resumo", texto);
        Assert.DoesNotContain("Sal", texto);
    }

    [Fact]
    public void Renomear_nao_aceita_nome_de_outro_professor()
    {
        var repo = Novo();
        repo.Adicionar("Maria");
        var diego = repo.Adicionar("Diego");
        Assert.Throws<ArgumentException>(() => repo.Renomear(diego, "maria"));
        repo.Renomear(diego, "DIEGO"); // o próprio nome com outra grafia pode
        Assert.Equal("DIEGO", diego.Nome);
    }

    [Fact]
    public void Aceite_das_regras_fica_gravado_com_a_data()
    {
        var repo = Novo();
        var p = repo.Adicionar("Diego");
        Assert.Equal(0, p.RegrasAceitas);

        repo.AceitarRegras(p, 1);

        var salvo = Novo().Perfis[0];
        Assert.Equal(1, salvo.RegrasAceitas);
        Assert.Equal(_relogio.GetUtcNow(), salvo.RegrasAceitasEm);
    }

    sealed class RelogioFalso : TimeProvider
    {
        readonly DateTimeOffset _agora = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _agora;
    }
}
