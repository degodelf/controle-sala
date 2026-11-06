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

public class ResultadoDefenderTestes
{
    static readonly DateTimeOffset Inicio = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Fim = Inicio.AddMinutes(9);

    [Fact]
    public void Script_tem_um_try_por_etapa_e_nao_usa_aspas_duplas()
    {
        string s = ResultadoDefender.Script(atualizar: true, verificar: true, varreduraCompleta: false);
        Assert.Contains("try { Update-MpSignature -ErrorAction Stop;", s);
        Assert.Contains("try { Start-MpScan -ScanType QuickScan -ErrorAction Stop;", s);
        Assert.DoesNotContain("\"", s); // vai entre aspas duplas na linha de comando
        Assert.True(s.IndexOf("Update-MpSignature", StringComparison.Ordinal) < s.IndexOf("Start-MpScan", StringComparison.Ordinal));
    }

    [Fact]
    public void Script_so_tem_o_que_foi_pedido()
    {
        string s = ResultadoDefender.Script(atualizar: false, verificar: true, varreduraCompleta: true);
        Assert.DoesNotContain("Update-MpSignature", s);
        Assert.Contains("FullScan", s);
    }

    [Fact]
    public void Tudo_certo()
    {
        var r = ResultadoDefender.Iniciado(true, true, Inicio).Concluido(
            "CS_ATUALIZACAO=ok\r\nCS_VERIFICACAO=ok\r\nCS_ATIVO=True\r\nCS_DEFINICOES=2026-11-05T17:58:00.0000000Z\r\n", Fim);
        Assert.Equal(EtapaDefender.Ok, r.Atualizacao);
        Assert.Equal(EtapaDefender.Ok, r.Verificacao);
        Assert.True(r.AntivirusAtivo);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 17, 58, 0, TimeSpan.Zero), r.Definicoes);
        Assert.False(r.EmAndamento);
        Assert.Equal("atualização ok, verificação ok", r.Resumo());
    }

    [Fact]
    public void Verificacao_vale_mesmo_com_a_atualizacao_falhando()
    {
        var r = ResultadoDefender.Iniciado(true, true, Inicio).Concluido("CS_ATUALIZACAO=erro:sem internet\nCS_VERIFICACAO=ok\n", Fim);
        Assert.Equal(EtapaDefender.Falhou, r.Atualizacao);
        Assert.Equal(EtapaDefender.Ok, r.Verificacao);
        Assert.Equal("Atualização: sem internet", r.Detalhe);
    }

    [Fact]
    public void Etapa_que_nao_respondeu_conta_como_falha()
    {
        var r = ResultadoDefender.Iniciado(true, true, Inicio).Concluido("", Fim);
        Assert.Equal(EtapaDefender.Falhou, r.Atualizacao);
        Assert.Equal(EtapaDefender.Falhou, r.Verificacao);
    }

    [Fact]
    public void Etapa_nao_pedida_continua_nao_pedida()
    {
        var r = ResultadoDefender.Iniciado(false, true, Inicio).Concluido("CS_VERIFICACAO=ok\n", Fim);
        Assert.Equal(EtapaDefender.NaoPedida, r.Atualizacao);
        Assert.Equal(EtapaDefender.Ok, r.Verificacao);
    }

    [Fact]
    public void Pc_desligado_no_meio_aparece_como_interrompida()
    {
        var r = ResultadoDefender.Iniciado(true, true, Inicio) with { Atualizacao = EtapaDefender.Ok };
        var depois = r.Interrompido(Fim);
        Assert.Equal(EtapaDefender.Ok, depois.Atualizacao);
        Assert.Equal(EtapaDefender.Interrompida, depois.Verificacao);
    }

    [Fact]
    public void Resultado_terminado_nao_muda_ao_reabrir()
    {
        var r = ResultadoDefender.Iniciado(true, false, Inicio).Concluido("CS_ATUALIZACAO=ok\n", Fim);
        Assert.Same(r, r.Interrompido(Fim.AddDays(1)));
    }

    [Fact]
    public void Chega_inteiro_pelo_protocolo()
    {
        var r = ResultadoDefender.Iniciado(true, true, Inicio).Concluido("CS_ATUALIZACAO=erro:sem acesso à rede\nCS_VERIFICACAO=ok\nCS_ATIVO=False\n", Fim);
        using var fluxo = new MemoryStream(Protocolo.MontarResultadoDefender(r.Serializar()));
        var buffer = new byte[1024];
        var msg = Protocolo.Ler(fluxo, ref buffer);
        Assert.Equal(TipoMensagem.ResultadoDefender, msg!.Value.Tipo);
        Assert.Equal(r, ResultadoDefender.Desserializar(Protocolo.LerResultadoDefender(buffer.AsSpan(0, msg.Value.Tamanho))));
    }
}

public sealed class RelatoriosTestes : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "controlesala-testes-" + Guid.NewGuid().ToString("N"));
    static readonly DateTimeOffset Base = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    RepositorioRelatorios Novo() => new(_pasta);

    const long Gb = 1024L * 1024 * 1024;

    static ResultadoLimpeza Limpeza(DateTimeOffset inicio, long bytes = Gb, bool ok = true, string detalhe = "") =>
        new(inicio, inicio.AddMinutes(1), Automatica: true, Pedida: "", ok, bytes, detalhe);

    [Fact]
    public void Pcs_da_mesma_limpeza_entram_no_mesmo_relatorio()
    {
        var repo = Novo();
        repo.Registrar("PC 01", Limpeza(Base), "Escola", "Lab");
        repo.Registrar("PC 02", Limpeza(Base.AddMinutes(1)), "Escola", "Lab");

        var r = Assert.Single(Novo().Listar());
        Assert.Equal(2, r.Linhas.Count);
        Assert.Equal(2 * Gb, r.Bytes);
        Assert.True(File.Exists(repo.CaminhoHtml(r)));
    }

    [Fact]
    public void Depois_de_cinco_apaga_o_mais_antigo()
    {
        var repo = Novo();
        for (int dia = 0; dia < 7; dia++) repo.Registrar("PC 01", Limpeza(Base.AddDays(dia)), "Escola", "Lab");

        var lista = Novo().Listar();
        Assert.Equal(RepositorioRelatorios.Guardados, lista.Count);
        Assert.Equal(Base.AddDays(6), lista[0].Inicio);
        Assert.Equal(Base.AddDays(2), lista[^1].Inicio);
        Assert.Equal(RepositorioRelatorios.Guardados, Directory.GetFiles(_pasta, "*.html").Length);
        Assert.Equal(RepositorioRelatorios.Guardados, Directory.GetFiles(_pasta, "*.json").Length);
    }

    [Fact]
    public void Resultado_repetido_nao_gera_aviso_novo()
    {
        var repo = Novo();
        var r = Limpeza(Base);
        Assert.NotNull(repo.Registrar("PC 01", r, "Escola", "Lab"));
        Assert.Null(repo.Registrar("PC 01", r, "Escola", "Lab")); // o agente reenvia a cada conexão
    }

    [Fact]
    public void Resultado_antigo_nao_ressuscita_relatorio_apagado()
    {
        var repo = Novo();
        for (int dia = 10; dia < 15; dia++) repo.Registrar("PC 01", Limpeza(Base.AddDays(dia)), "Escola", "Lab");
        Assert.Null(repo.Registrar("PC 02", Limpeza(Base), "Escola", "Lab"));
        Assert.Equal(RepositorioRelatorios.Guardados, Novo().Listar().Count);
    }

    [Fact]
    public void Html_tem_barra_em_gb_por_pc_e_total_no_final()
    {
        var repo = Novo();
        repo.Registrar("PC 01", Limpeza(Base, 2 * Gb), "Escola", "Lab");
        var r = repo.Registrar("PC 02", Limpeza(Base, Gb), "Escola", "Lab")!;

        string html = r.GerarHtml();
        Assert.Contains("width:100.0%", html); // o maior preenche o trilho
        Assert.Contains("width:50.0%", html);
        Assert.Contains("2,00 GB", html);
        Assert.Contains("3,00 GB", html);
        Assert.Contains("Total em 2 de 2 computadores", html);
        Assert.True(html.IndexOf("PC 02", StringComparison.Ordinal) < html.IndexOf("class=\"total\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Html_mostra_quem_nao_limpou_e_escapa_nomes()
    {
        var repo = Novo();
        repo.Registrar("<b>PC</b>", Limpeza(Base), "Escola & Cia", "Lab");
        var r = repo.Registrar("PC 02", Limpeza(Base, 0, ok: false, detalhe: "pasta do aluno não encontrada"), "Escola & Cia", "Lab")!;

        Assert.Equal(1, r.ComProblema);
        string html = r.GerarHtml();
        Assert.Contains("&lt;b&gt;PC&lt;/b&gt;", html);
        Assert.Contains("Escola &amp; Cia", html);
        Assert.Contains("Não limpou: pasta do aluno não encontrada", System.Net.WebUtility.HtmlDecode(html));
        Assert.Contains("Total em 1 de 2 computadores", html);
        Assert.DoesNotContain("<script", html);
    }

    [Theory]
    [InlineData(0, "0,00 GB")]
    [InlineData(1024, "menos de 0,01 GB")]
    [InlineData(1610612736, "1,50 GB")]
    public void Gb_como_o_windows_mostra(long bytes, string esperado)
    {
        Assert.Equal(esperado, RelatorioLimpeza.EmGb(bytes));
    }

    [Fact]
    public void Lixeira_pega_so_a_pasta_da_conta_em_cada_disco()
    {
        const string aluno = "S-1-5-21-1-1001", admin = "S-1-5-21-1-500";
        string discoC = Path.Combine(_pasta, "C"), discoD = Path.Combine(_pasta, "D"), semLixeira = Path.Combine(_pasta, "E");
        string doAluno = Path.Combine(discoC, "$Recycle.Bin", aluno), doAdmin = Path.Combine(discoC, "$Recycle.Bin", admin);
        string noOutroDisco = Path.Combine(discoD, "$Recycle.Bin", aluno);
        foreach (var pasta in new[] { doAluno, doAdmin, noOutroDisco, semLixeira }) Directory.CreateDirectory(pasta);
        File.WriteAllBytes(Path.Combine(doAluno, "$R1.txt"), new byte[100]);
        File.WriteAllBytes(Path.Combine(noOutroDisco, "$R2.txt"), new byte[50]);
        File.WriteAllBytes(Path.Combine(doAdmin, "$R3.txt"), new byte[70]);

        var pastas = LimpezaArquivos.PastasDaLixeira([discoC, discoD, semLixeira], aluno);
        Assert.Equal([doAluno, noOutroDisco], pastas);
        foreach (var pasta in pastas) LimpezaArquivos.EsvaziarPasta(pasta, []);

        Assert.Equal(0, LimpezaArquivos.Tamanho(doAluno));
        Assert.Equal(0, LimpezaArquivos.Tamanho(noOutroDisco));
        Assert.Equal(70, LimpezaArquivos.Tamanho(doAdmin));   // a Lixeira de outra conta não é tocada
    }

    [Fact]
    public void Tamanho_soma_a_pasta_inteira()
    {
        string pasta = Path.Combine(_pasta, "t", "sub");
        Directory.CreateDirectory(pasta);
        File.WriteAllBytes(Path.Combine(pasta, "a.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(_pasta, "t", "b.bin"), new byte[234]);
        Assert.Equal(1234, LimpezaArquivos.Tamanho(Path.Combine(_pasta, "t")));
        Assert.Equal(0, LimpezaArquivos.Tamanho(Path.Combine(_pasta, "nao-existe")));
    }
}

public sealed class AbrirArquivoRecebidoTestes : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "controlesala-testes-" + Guid.NewGuid().ToString("N"));

    public AbrirArquivoRecebidoTestes() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void Acha_o_arquivo_que_chegou()
    {
        File.WriteAllText(Path.Combine(_pasta, "tarefa.pdf"), "x");
        Assert.Equal(Path.Combine(_pasta, "tarefa.pdf"), PastaRecebidos.Localizar(_pasta, "tarefa.pdf"));
    }

    [Theory]
    [InlineData("nao-existe.pdf")]
    [InlineData(@"..\tarefa.pdf")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("programa.exe")]
    public void Nao_abre_nada_fora_da_pasta_nem_programa(string nome)
    {
        File.WriteAllText(Path.Combine(_pasta, "programa.exe"), "x");
        Assert.Null(PastaRecebidos.Localizar(_pasta, nome));
    }

    [Fact]
    public void Abrir_arquivo_entra_no_registro_com_o_nome()
    {
        Assert.Equal("tarefa.pdf", RegistroAtividades.DetalheDoComando(AcaoComando.AbrirArquivoRecebido, "tarefa.pdf"));
    }
}
