using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public sealed class RegistroAtividadesTestes : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "controlesala-testes-" + Guid.NewGuid().ToString("N"));
    readonly Relogio _relogio = new() { Agora = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero) };

    RegistroAtividades Novo() => new(_pasta, _relogio);

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void Guarda_quem_fez_o_que_e_mostra_o_mais_novo_primeiro()
    {
        var registro = Novo();
        registro.Registrar("Diego", "Abrir link", "PC 01", "https://escola.sc.gov.br");
        _relogio.Agora = _relogio.Agora.AddMinutes(5);
        registro.Registrar("Maria", "Bloquear a tela", "PC 02");

        var lista = Novo().Ler();
        Assert.Equal(["Maria", "Diego"], lista.Select(a => a.Professor));
        Assert.Equal("PC 01", lista[1].Computador);
        Assert.Equal("https://escola.sc.gov.br", lista[1].Detalhe);
    }

    [Fact]
    public void Linha_danificada_nao_impede_de_ler_o_resto()
    {
        var registro = Novo();
        registro.Registrar("Diego", "Entrou no programa");
        File.AppendAllText(Directory.GetFiles(_pasta).Single(), "{\"Quando\":\"202");
        Assert.Single(Novo().Ler());
    }

    [Fact]
    public void Apaga_so_o_que_passou_de_seis_meses()
    {
        var registro = Novo();
        _relogio.Agora = new DateTimeOffset(2026, 4, 30, 10, 0, 0, TimeSpan.Zero);
        registro.Registrar("Diego", "Abril: passou do prazo");
        _relogio.Agora = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        registro.Registrar("Diego", "Maio: ainda no prazo");
        _relogio.Agora = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
        registro.Registrar("Diego", "Novembro");

        registro.ApagarAntigos();

        Assert.Equal(["Novembro", "Maio: ainda no prazo"], Novo().Ler().Select(a => a.Acao));
    }

    [Fact]
    public void Detalhe_longo_e_cortado_e_sem_quebra_de_linha()
    {
        var registro = Novo();
        registro.Registrar("Diego", "Iniciar aplicativo ou comando", "PC 01", "cmd\r\n/c " + new string('x', 300));
        string detalhe = Novo().Ler()[0].Detalhe;
        Assert.DoesNotContain('\n', detalhe);
        Assert.True(detalhe.Length <= 123);
    }

    [Fact]
    public void Texto_de_mensagem_e_limpeza_nao_entram_no_registro()
    {
        Assert.Equal("", RegistroAtividades.DetalheDoComando(AcaoComando.Mensagem, "conversa particular com o aluno"));
        Assert.Equal("", RegistroAtividades.DetalheDoComando(AcaoComando.Limpeza, "{\"Downloads\":true}"));
        Assert.Equal("https://x", RegistroAtividades.DetalheDoComando(AcaoComando.AbrirLink, "https://x"));
    }

    [Fact]
    public void Todo_comando_tem_nome_para_mostrar()
    {
        foreach (var acao in Enum.GetValues<AcaoComando>())
            Assert.DoesNotContain(RegistroAtividades.NomeDoComando(acao)[1..], char.IsUpper); // nome de código seria "BloquearTela"
    }

    [Fact]
    public void Planilha_abre_no_excel_e_nao_executa_formula()
    {
        Directory.CreateDirectory(_pasta);
        string csv = Path.Combine(_pasta, "saida.csv");
        var quando = new DateTimeOffset(2026, 11, 5, 10, 30, 0, TimeSpan.Zero);
        RegistroAtividades.ExportarCsv([new Atividade(quando, "=Maria \"Prof\"", "Ação", "PC 01", "")], csv);

        byte[] bytes = File.ReadAllBytes(csv);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        string[] linhas = File.ReadAllLines(csv);
        Assert.Equal("Data e hora;Professor;Ação;Computador;Detalhe", linhas[0]);
        Assert.Equal("05/11/2026 10:30:00;\"'=Maria \"\"Prof\"\"\";\"Ação\";\"PC 01\";\"\"", linhas[1]);
    }

    sealed class Relogio : TimeProvider
    {
        public DateTimeOffset Agora { get; set; }
        public override DateTimeOffset GetUtcNow() => Agora;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

public class LimiteTentativasTestes
{
    sealed class Relogio : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    [Fact]
    public void Bloqueia_depois_de_errar_varias_vezes_e_libera_com_o_tempo()
    {
        var relogio = new Relogio();
        var limite = new LimiteTentativas(falhasAteBloquear: 3, TimeSpan.FromMinutes(1), relogio);

        limite.Falhou("10.0.0.9");
        limite.Falhou("10.0.0.9");
        Assert.False(limite.Bloqueado("10.0.0.9"));
        limite.Falhou("10.0.0.9");
        Assert.True(limite.Bloqueado("10.0.0.9"));
        Assert.False(limite.Bloqueado("10.0.0.8")); // o PC vizinho não paga pelo erro

        relogio.Agora = relogio.Agora.AddSeconds(61);
        Assert.False(limite.Bloqueado("10.0.0.9"));
    }

    [Fact]
    public void Acertar_zera_a_conta()
    {
        var limite = new LimiteTentativas(falhasAteBloquear: 2);
        limite.Falhou("10.0.0.9");
        limite.Acertou("10.0.0.9");
        limite.Falhou("10.0.0.9");
        Assert.False(limite.Bloqueado("10.0.0.9"));
    }
}
