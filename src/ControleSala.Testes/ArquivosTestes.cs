using System.Security.Cryptography;
using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class NomesDeArquivoTestes
{
    [Theory]
    [InlineData("atividade.pdf")]
    [InlineData("Atividade 1 — ç ã é.docx")]
    [InlineData("foto.JPG")]
    [InlineData("a.b.c.txt")]
    [InlineData("😀 prova.png")]
    [InlineData("COMANDO.txt")] // começa com COM mas não é COM1..9
    [InlineData("aula.pptx")]
    [InlineData("planilha.xlsx")]
    [InlineData("musica.mp3")]
    [InlineData("video.mp4")]
    [InlineData("materiais.zip")]
    public void Nomes_normais_sao_aceitos(string nome) => Assert.Null(NomesDeArquivo.Validar(nome));

    [Theory]
    [InlineData("sem extensão")]
    [InlineData("arquivo.xyz")]
    [InlineData("pagina.html")]   // html não está na lista de permitidos
    [InlineData("imagem.svg")]    // svg pode conter script: fora da lista
    [InlineData("codigo.py")]
    public void Tipos_fora_da_lista_de_permitidos_sao_recusados(string nome) => Assert.NotNull(NomesDeArquivo.Validar(nome));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("..\\evil.txt")]
    [InlineData("../evil.txt")]
    [InlineData("pasta\\arquivo.txt")]
    [InlineData("C:\\Windows\\x.txt")]
    [InlineData("C:x.txt")]
    [InlineData("arquivo.txt:fluxo")]          // fluxo alternativo de dados (NTFS)
    [InlineData("a*b.txt")]
    [InlineData("a?b.txt")]
    [InlineData("a|b.txt")]
    [InlineData("a\"b.txt")]
    [InlineData("a\0b.txt")]
    [InlineData("tab\there.txt")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("termina com ponto.")]
    [InlineData("termina com espaço ")]
    [InlineData(" começa com espaço.txt")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("NUL.pdf")]
    [InlineData("Com1.doc")]
    [InlineData("LPT9")]
    [InlineData("aux .txt")]
    public void Nomes_perigosos_ou_invalidos_sao_recusados(string? nome) => Assert.NotNull(NomesDeArquivo.Validar(nome));

    [Theory]
    [InlineData("programa.exe")]
    [InlineData("PROGRAMA.EXE")]
    [InlineData("script.bat")]
    [InlineData("script.ps1")]
    [InlineData("macro.vbs")]
    [InlineData("instalador.msi")]
    [InlineData("atalho.lnk")]
    [InlineData("atalho.url")]
    [InlineData("biblioteca.dll")]
    [InlineData("relatorio.pdf.exe")]            // dupla extensão: vale a última
    [InlineData("tela.scr")]
    public void Tipos_executaveis_sao_recusados(string nome) => Assert.Contains("não podem ser enviados", NomesDeArquivo.Validar(nome));

    [Fact]
    public void Direcao_do_texto_invertida_nao_esconde_a_extensao()
    {
        // U+202E faz "atividade‮fdp.exe" parecer "atividadeexe.pdf"
        Assert.NotNull(NomesDeArquivo.Validar("atividade\u202Efdp.exe"));
        Assert.NotNull(NomesDeArquivo.Validar("atividade\u202Etxt.pdf"));
        Assert.NotNull(NomesDeArquivo.Validar("zero\u200Bwidth.txt"));
    }

    [Fact]
    public void Nome_longo_demais_e_recusado()
    {
        Assert.Null(NomesDeArquivo.Validar(new string('a', NomesDeArquivo.TamanhoMaximoNome - 4) + ".txt"));
        Assert.NotNull(NomesDeArquivo.Validar(new string('a', NomesDeArquivo.TamanhoMaximoNome)+ ".txt"));
    }
}

public sealed class RecebedorArquivosTestes : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "controlesala-testes-" + Guid.NewGuid().ToString("N"), "recebidos");
    long _livre = long.MaxValue / 2;
    readonly RelogioFalso _relogio = new();

    RecebedorArquivos Novo() => new(_pasta, _ => _livre, _relogio);
    static byte[] Dados(int n) => RandomNumberGenerator.GetBytes(n);
    static byte[] Hash(byte[] d) => SHA256.HashData(d);

    public void Dispose()
    {
        var raiz = Path.GetDirectoryName(_pasta)!;
        if (Directory.Exists(raiz)) Directory.Delete(raiz, true);
    }

    /// <summary>Envia o arquivo inteiro em blocos, como o professor faz.</summary>
    ResultadoArquivo Enviar(RecebedorArquivos r, uint id, string nome, byte[] dados, int bloco = 1000)
    {
        var ini = r.Iniciar(id, nome, dados.Length);
        if (!ini.Ok) return ini;
        for (int i = 0; i < dados.Length; i += bloco)
        {
            var d = r.Dados(id, dados.AsSpan(i, Math.Min(bloco, dados.Length - i)));
            if (!d.Ok) return d;
        }
        return r.Fim(id, Hash(dados));
    }

    [Fact]
    public void Recebe_o_arquivo_inteiro_com_o_mesmo_conteudo()
    {
        using var r = Novo();
        var dados = Dados(10_500);
        var res = Enviar(r, 1, "Atividade 1.pdf", dados);
        Assert.True(res.Ok, res.Mensagem);
        Assert.Equal(Path.Combine(_pasta, "Atividade 1.pdf"), res.Caminho);
        Assert.Equal(dados, File.ReadAllBytes(res.Caminho!));
        Assert.False(r.Ativo);
        Assert.Empty(Directory.GetFiles(_pasta, "*.parte")); // nenhum temporário sobrou
    }

    [Fact]
    public void Arquivo_vazio_funciona()
    {
        using var r = Novo();
        var res = Enviar(r, 1, "vazio.txt", []);
        Assert.True(res.Ok, res.Mensagem);
        Assert.Empty(File.ReadAllBytes(res.Caminho!));
    }

    [Fact]
    public void Arquivo_so_aparece_com_o_nome_final_depois_de_completo()
    {
        using var r = Novo();
        var dados = Dados(5000);
        Assert.True(r.Iniciar(1, "prova.pdf", dados.Length).Ok);
        r.Dados(1, dados.AsSpan(0, 2500));
        Assert.False(File.Exists(Path.Combine(_pasta, "prova.pdf")));            // ainda não
        Assert.All(Directory.GetFiles(_pasta), f => Assert.EndsWith(".parte", f)); // só o temporário
        Assert.All(Directory.GetFiles(_pasta), f => Assert.True(File.GetAttributes(f).HasFlag(FileAttributes.Hidden)));
    }

    [Fact]
    public void Enviar_de_novo_substitui_a_versao_anterior()
    {
        using var r = Novo();
        Assert.True(Enviar(r, 1, "lista.txt", Dados(100)).Ok);
        var nova = Dados(200);
        Assert.True(Enviar(r, 2, "lista.txt", nova).Ok);
        Assert.Equal(nova, File.ReadAllBytes(Path.Combine(_pasta, "lista.txt")));
    }

    [Fact]
    public void Hash_errado_recusa_apaga_o_temporario_e_nao_cria_o_arquivo()
    {
        using var r = Novo();
        var dados = Dados(3000);
        Assert.True(r.Iniciar(1, "x.txt", dados.Length).Ok);
        r.Dados(1, dados);
        var res = r.Fim(1, Hash(Dados(10)));
        Assert.False(res.Ok);
        Assert.Contains("diferente", res.Mensagem);
        Assert.False(File.Exists(Path.Combine(_pasta, "x.txt")));
        Assert.Empty(Directory.GetFiles(_pasta));
        Assert.False(r.Ativo);
    }

    [Fact]
    public void Arquivo_incompleto_e_recusado()
    {
        using var r = Novo();
        var dados = Dados(3000);
        r.Iniciar(1, "x.txt", dados.Length);
        r.Dados(1, dados.AsSpan(0, 2000));
        var res = r.Fim(1, Hash(dados));
        Assert.False(res.Ok);
        Assert.Contains("incompleto", res.Mensagem);
        Assert.Empty(Directory.GetFiles(_pasta));
    }

    [Fact]
    public void Mais_dados_do_que_o_declarado_cancela_o_envio()
    {
        using var r = Novo();
        r.Iniciar(1, "x.txt", 100);
        var res = r.Dados(1, Dados(101));
        Assert.False(res.Ok);
        Assert.False(r.Ativo);
        Assert.Empty(Directory.GetFiles(_pasta));
    }

    [Theory]
    [InlineData("..\\fora.txt")]
    [InlineData("../fora.txt")]
    [InlineData("C:\\Windows\\fora.txt")]
    [InlineData("programa.exe")]
    [InlineData("CON")]
    [InlineData("a.txt:ads")]
    public void Nome_perigoso_e_recusado_no_agente_e_nada_e_gravado(string nome)
    {
        using var r = Novo();
        var res = r.Iniciar(1, nome, 10);
        Assert.False(res.Ok);
        Assert.False(r.Ativo);
        Assert.False(Directory.Exists(_pasta) && Directory.GetFileSystemEntries(_pasta).Length > 0);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_pasta)!, "fora.txt")));
    }

    [Fact]
    public void Arquivo_maior_que_o_limite_e_recusado()
    {
        using var r = Novo();
        Assert.False(r.Iniciar(1, "enorme.iso", RecebedorArquivos.TamanhoMaximo + 1).Ok);
        Assert.False(r.Iniciar(1, "negativo.txt", -1).Ok);
    }

    [Fact]
    public void Sem_espaco_livre_e_recusado_e_a_reserva_e_respeitada()
    {
        _livre = 10_000 + RecebedorArquivos.Reserva - 1;
        using var r = Novo();
        Assert.False(r.Iniciar(1, "a.txt", 10_000).Ok);
        _livre = 10_000 + RecebedorArquivos.Reserva;
        Assert.True(r.Iniciar(1, "a.txt", 10_000).Ok);
    }

    [Fact]
    public void So_um_envio_por_vez()
    {
        using var r = Novo();
        Assert.True(r.Iniciar(1, "a.txt", 10).Ok);
        var segundo = r.Iniciar(2, "b.txt", 10);
        Assert.False(segundo.Ok);
        Assert.True(r.Ativo); // o primeiro continua valendo
        Assert.Equal("a.txt", r.NomeAtivo);
    }

    [Fact]
    public void Dados_de_outro_envio_ou_sem_envio_sao_recusados_sem_derrubar_o_atual()
    {
        using var r = Novo();
        Assert.False(r.Dados(1, [1, 2, 3]).Ok);   // nada em andamento
        r.Iniciar(5, "a.txt", 10);
        Assert.False(r.Dados(6, [1, 2, 3]).Ok);   // id de outro envio
        Assert.False(r.Fim(6, new byte[32]).Ok);
        Assert.True(r.Ativo);
        Assert.True(r.Dados(5, [1, 2, 3]).Ok);
    }

    [Fact]
    public void Cancelar_apaga_o_temporario()
    {
        using var r = Novo();
        r.Iniciar(1, "a.txt", 100);
        r.Dados(1, Dados(50));
        r.Cancelar();
        Assert.False(r.Ativo);
        Assert.Empty(Directory.GetFiles(_pasta));
        Assert.True(r.Iniciar(2, "a.txt", 10).Ok); // e dá para começar outro
    }

    [Fact]
    public void Envio_parado_e_detectado_pelo_tempo()
    {
        using var r = Novo();
        Assert.False(r.Parado(TimeSpan.FromSeconds(30)));
        r.Iniciar(1, "a.txt", 100);
        _relogio.Avancar(TimeSpan.FromSeconds(20));
        Assert.False(r.Parado(TimeSpan.FromSeconds(30)));
        r.Dados(1, Dados(10)); // chegou algo: o relógio de inatividade recomeça
        _relogio.Avancar(TimeSpan.FromSeconds(20));
        Assert.False(r.Parado(TimeSpan.FromSeconds(30)));
        _relogio.Avancar(TimeSpan.FromSeconds(11));
        Assert.True(r.Parado(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Arquivo_aberto_por_outro_programa_da_erro_claro_e_nao_deixa_lixo()
    {
        using var r = Novo();
        Assert.True(Enviar(r, 1, "aberto.txt", Dados(100)).Ok);
        using var preso = new FileStream(Path.Combine(_pasta, "aberto.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
        var res = Enviar(r, 2, "aberto.txt", Dados(100));
        Assert.False(res.Ok);
        Assert.Contains("aberto", res.Mensagem);
        Assert.Empty(Directory.GetFiles(_pasta, "*.parte"));
        Assert.False(r.Ativo);
    }

    [Fact]
    public void Limpeza_de_restos_apaga_so_temporarios_antigos()
    {
        Directory.CreateDirectory(_pasta);
        string velho = Path.Combine(_pasta, ".1-aaaa.parte"), novo = Path.Combine(_pasta, ".2-bbbb.parte"), meu = Path.Combine(_pasta, "tarefa.txt");
        File.WriteAllText(velho, "x"); File.WriteAllText(novo, "x"); File.WriteAllText(meu, "x");
        File.SetLastWriteTimeUtc(velho, DateTime.UtcNow.AddDays(-3));
        RecebedorArquivos.LimparRestos(_pasta, TimeSpan.FromDays(1));
        Assert.False(File.Exists(velho));
        Assert.True(File.Exists(novo));   // recente: pode ser de um envio em andamento
        Assert.True(File.Exists(meu));    // arquivo do aluno nunca é tocado
    }

    sealed class RelogioFalso : TimeProvider
    {
        DateTimeOffset _agora = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _agora;
        public void Avancar(TimeSpan t) => _agora += t;
    }
}

public class ProtocoloArquivoTestes
{
    static (TipoMensagem Tipo, byte[] Corpo) Ler(byte[] msg)
    {
        using var s = new MemoryStream(msg);
        byte[] buf = new byte[16];
        var r = Protocolo.Ler(s, ref buf)!.Value;
        return (r.Tipo, buf[..r.Tamanho]);
    }

    [Fact]
    public void Inicio_ida_e_volta_com_acentos()
    {
        var (tipo, corpo) = Ler(Protocolo.MontarArquivoInicio(7, 123_456_789_012, "Redação — versão 2.docx"));
        Assert.Equal(TipoMensagem.ArquivoInicio, tipo);
        var i = Protocolo.LerArquivoInicio(corpo)!.Value;
        Assert.Equal((7u, 123_456_789_012L, "Redação — versão 2.docx"), (i.Id, i.Tamanho, i.Nome));
    }

    [Fact]
    public void Inicio_truncado_ou_com_tamanho_de_nome_mentiroso_e_recusado()
    {
        var (_, corpo) = Ler(Protocolo.MontarArquivoInicio(1, 10, "a.txt"));
        Assert.Null(Protocolo.LerArquivoInicio(corpo[..^1]));
        Assert.Null(Protocolo.LerArquivoInicio(corpo[..10]));
        Assert.Null(Protocolo.LerArquivoInicio([]));
        Assert.Null(Protocolo.LerArquivoInicio([.. corpo, 0x41])); // sobra byte
    }

    [Fact]
    public void Dados_fim_cancelar_levam_o_id()
    {
        var bloco = RandomNumberGenerator.GetBytes(Protocolo.BlocoArquivo);
        var (t1, c1) = Ler(Protocolo.MontarArquivoDados(9, bloco));
        Assert.Equal(TipoMensagem.ArquivoDados, t1);
        Assert.Equal(9u, Protocolo.LerIdArquivo(c1));
        Assert.Equal(bloco, c1[4..]);

        var hash = SHA256.HashData(bloco);
        var (t2, c2) = Ler(Protocolo.MontarArquivoFim(9, hash));
        Assert.Equal(TipoMensagem.ArquivoFim, t2);
        Assert.Equal(hash, c2[4..]);

        var (t3, c3) = Ler(Protocolo.MontarArquivoCancelar(9));
        Assert.Equal(TipoMensagem.ArquivoCancelar, t3);
        Assert.Equal(9u, Protocolo.LerIdArquivo(c3));
        Assert.Null(Protocolo.LerIdArquivo([1, 2]));
    }

    [Theory]
    [InlineData(EstadoArquivo.Aceito, "")]
    [InlineData(EstadoArquivo.Concluido, "recebido.")]
    [InlineData(EstadoArquivo.Erro, "Não há espaço — ç")]
    public void Resposta_ida_e_volta(EstadoArquivo estado, string texto)
    {
        var (tipo, corpo) = Ler(Protocolo.MontarArquivoResposta(3, estado, texto));
        Assert.Equal(TipoMensagem.ArquivoResposta, tipo);
        Assert.Equal(new RespostaArquivo(3, estado, texto), Protocolo.LerArquivoResposta(corpo));
    }

    [Fact]
    public void Resposta_invalida_e_recusada()
    {
        var (_, corpo) = Ler(Protocolo.MontarArquivoResposta(3, EstadoArquivo.Erro, "x"));
        Assert.Null(Protocolo.LerArquivoResposta(corpo[..^1]));
        corpo[4] = 99; // estado inexistente
        Assert.Null(Protocolo.LerArquivoResposta(corpo));
    }
}
