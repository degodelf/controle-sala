using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public sealed class PerfisTestes : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "controlesala-testes-" + Guid.NewGuid().ToString("N"));
    readonly RelogioFalso _relogio = new();

    string Arquivo => Path.Combine(_pasta, "professores.json");
    RepositorioPerfis Novo() => new(Arquivo, iteracoes: 1000, tempo: _relogio);

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("12345678", true)]
    [InlineData("123", false)]
    [InlineData("123456789", false)]
    [InlineData("12a4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("١٢٣٤", false)] // dígitos de outros alfabetos não valem
    public void PinValido_aceita_so_4_a_8_numeros(string? pin, bool esperado) =>
        Assert.Equal(esperado, RepositorioPerfis.PinValido(pin));

    [Fact]
    public void Adicionar_e_verificar_pin()
    {
        var repo = Novo();
        var diego = repo.Adicionar("Diego", "4321");
        Assert.Equal(ResultadoPin.Correto, repo.Verificar(diego, "4321", out _));
        Assert.Equal(ResultadoPin.Incorreto, repo.Verificar(diego, "1234", out _));
    }

    [Fact]
    public void Pin_nao_fica_gravado_em_texto()
    {
        Novo().Adicionar("Diego", "4321");
        string json = File.ReadAllText(Arquivo);
        Assert.DoesNotContain("4321", json);
        Assert.Contains("Diego", json);
    }

    [Fact]
    public void Perfis_persistem_entre_aberturas_do_programa()
    {
        Novo().Adicionar("Diego", "4321");
        var depois = Novo();
        var perfil = Assert.Single(depois.Perfis);
        Assert.Equal("Diego", perfil.Nome);
        Assert.Equal(ResultadoPin.Correto, depois.Verificar(perfil, "4321", out _));
    }

    [Fact]
    public void Mesmo_pin_gera_resumos_diferentes()
    {
        var repo = Novo();
        var a = repo.Adicionar("Ana", "1111");
        var b = repo.Adicionar("Bia", "1111");
        Assert.NotEqual(a.Resumo, b.Resumo);
        Assert.NotEqual(a.Sal, b.Sal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nome_vazio_e_recusado(string nome) =>
        Assert.Throws<ArgumentException>(() => Novo().Adicionar(nome, "1234"));

    [Fact]
    public void Nome_repetido_e_recusado_sem_diferenciar_maiusculas()
    {
        var repo = Novo();
        repo.Adicionar("Diego", "1234");
        Assert.Throws<ArgumentException>(() => repo.Adicionar("  diego ", "5678"));
    }

    [Fact]
    public void Pin_invalido_e_recusado_ao_adicionar() =>
        Assert.Throws<ArgumentException>(() => Novo().Adicionar("Diego", "12"));

    [Fact]
    public void Bloqueia_depois_de_5_erros_e_libera_com_o_tempo()
    {
        var repo = Novo();
        var p = repo.Adicionar("Diego", "4321");

        for (int i = 0; i < RepositorioPerfis.FalhasAteBloquear - 1; i++)
            Assert.Equal(ResultadoPin.Incorreto, repo.Verificar(p, "0000", out _));
        Assert.Equal(ResultadoPin.Bloqueado, repo.Verificar(p, "0000", out var espera));
        Assert.Equal(RepositorioPerfis.BloqueioInicial, espera);

        // Durante o bloqueio nem o PIN certo entra.
        Assert.Equal(ResultadoPin.Bloqueado, repo.Verificar(p, "4321", out _));

        _relogio.Avancar(RepositorioPerfis.BloqueioInicial + TimeSpan.FromSeconds(1));
        Assert.Equal(ResultadoPin.Correto, repo.Verificar(p, "4321", out _));
        Assert.Equal(0, p.Falhas);
    }

    [Fact]
    public void Bloqueio_dobra_a_cada_erro_ate_o_maximo()
    {
        var repo = Novo();
        var p = repo.Adicionar("Diego", "4321");
        TimeSpan ultimo = TimeSpan.Zero;
        for (int i = 0; i < 30; i++)
        {
            _relogio.Avancar(TimeSpan.FromMinutes(10)); // sempre espera o bloqueio acabar
            repo.Verificar(p, "0000", out var espera);
            if (espera > TimeSpan.Zero)
            {
                Assert.True(espera >= ultimo);
                ultimo = espera;
            }
        }
        Assert.Equal(RepositorioPerfis.BloqueioMaximo, ultimo);
    }

    [Fact]
    public void Bloqueio_sobrevive_a_fechar_e_abrir_o_programa()
    {
        var repo = Novo();
        var p = repo.Adicionar("Diego", "4321");
        for (int i = 0; i < RepositorioPerfis.FalhasAteBloquear; i++)
            repo.Verificar(p, "0000", out _);

        var reaberto = Novo();
        Assert.Equal(ResultadoPin.Bloqueado, reaberto.Verificar(reaberto.Perfis[0], "4321", out _));
    }

    [Fact]
    public void Trocar_pin_exige_o_pin_atual()
    {
        var repo = Novo();
        var p = repo.Adicionar("Diego", "4321");

        Assert.Equal(ResultadoPin.Incorreto, repo.TrocarPin(p, "9999", "5555", out _));
        Assert.Equal(ResultadoPin.Correto, repo.Verificar(p, "4321", out _)); // não mudou

        Assert.Equal(ResultadoPin.Correto, repo.TrocarPin(p, "4321", "5555", out _));
        Assert.Equal(ResultadoPin.Incorreto, repo.Verificar(p, "4321", out _));
        Assert.Equal(ResultadoPin.Correto, repo.Verificar(p, "5555", out _));
    }

    [Fact]
    public void Trocar_pin_recusa_pin_novo_invalido()
    {
        var repo = Novo();
        var p = repo.Adicionar("Diego", "4321");
        Assert.Throws<ArgumentException>(() => repo.TrocarPin(p, "4321", "abc", out _));
        Assert.Equal(ResultadoPin.Correto, repo.Verificar(p, "4321", out _));
    }

    [Fact]
    public void Remover_nao_apaga_o_ultimo_professor()
    {
        var repo = Novo();
        var a = repo.Adicionar("Ana", "1111");
        var b = repo.Adicionar("Bia", "2222");
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
        repo.Adicionar("Diego", "4321"); // e volta a funcionar
        Assert.Single(Novo().Perfis);
    }

    [Fact]
    public void Resumo_adulterado_nao_deixa_entrar_nem_derruba_o_programa()
    {
        var repo = Novo();
        var p = repo.Adicionar("Diego", "4321");
        p.Resumo = "isto-nao-e-base64!!";
        Assert.Equal(ResultadoPin.Incorreto, repo.Verificar(p, "4321", out _));
    }

    [Fact]
    public void Professor_sem_pin_entra_sem_senha()
    {
        var repo = Novo();
        var p = repo.AdicionarSemPin("Professor");
        Assert.False(RepositorioPerfis.TemPin(p));
        Assert.Equal(ResultadoPin.Correto, repo.Verificar(p, "", out _));
        Assert.Equal(ResultadoPin.Correto, repo.Verificar(p, "qualquer", out _));
    }

    [Fact]
    public void Professor_sem_pin_persiste_sem_senha()
    {
        Novo().AdicionarSemPin("Professor");
        var perfil = Assert.Single(Novo().Perfis);
        Assert.False(RepositorioPerfis.TemPin(perfil));
    }

    [Fact]
    public void Remover_pin_faz_entrar_sem_senha()
    {
        var repo = Novo();
        var p = repo.Adicionar("Diego", "4321");
        Assert.True(RepositorioPerfis.TemPin(p));

        repo.RemoverPin(p);
        Assert.False(RepositorioPerfis.TemPin(p));
        Assert.Equal(ResultadoPin.Correto, repo.Verificar(p, "", out _));
        Assert.False(RepositorioPerfis.TemPin(Novo().Perfis[0])); // persistiu sem senha
    }

    [Fact]
    public void Definir_senha_num_professor_sem_pin()
    {
        var repo = Novo();
        var p = repo.AdicionarSemPin("Professor");
        // Sem PIN atual, o "PIN atual" vazio passa e define o novo.
        Assert.Equal(ResultadoPin.Correto, repo.TrocarPin(p, "", "5555", out _));
        Assert.True(RepositorioPerfis.TemPin(p));
        Assert.Equal(ResultadoPin.Correto, repo.Verificar(p, "5555", out _));
        Assert.Equal(ResultadoPin.Incorreto, repo.Verificar(p, "0000", out _));
    }

    sealed class RelogioFalso : TimeProvider
    {
        DateTimeOffset _agora = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _agora;
        public void Avancar(TimeSpan t) => _agora += t;
    }
}
