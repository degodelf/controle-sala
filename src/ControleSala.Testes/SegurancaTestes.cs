#pragma warning disable xUnit1031 // estes testes usam conexões TCP de verdade e esperam de propósito
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public sealed class HandshakeTestes
{
    static readonly byte[] ChaveA = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    static readonly byte[] ChaveB = Enumerable.Range(101, 32).Select(i => (byte)i).ToArray();

    /// <summary>Abre uma conexão TCP de verdade na própria máquina e devolve as duas pontas.</summary>
    static (NetworkStream Agente, NetworkStream Professor, IDisposable Fechar) Par()
    {
        var ouvinte = new TcpListener(IPAddress.Loopback, 0);
        ouvinte.Start();
        var cliente = new TcpClient();
        cliente.Connect(IPAddress.Loopback, ((IPEndPoint)ouvinte.LocalEndpoint).Port);
        var servidor = ouvinte.AcceptTcpClient();
        ouvinte.Stop();
        foreach (var t in new[] { cliente, servidor }) { t.ReceiveTimeout = 5000; t.SendTimeout = 5000; }
        return (servidor.GetStream(), cliente.GetStream(), new Fecha(cliente, servidor));
    }

    sealed class Fecha(params TcpClient[] c) : IDisposable { public void Dispose() { foreach (var x in c) x.Dispose(); } }

    static (Stream Agente, Stream Professor) Conectar(byte[]? chaveAgente, byte[]? chaveProfessor, bool aceitaSemChave = false)
    {
        var (a, p, _) = Par();
        var tarefaAgente = Task.Run(() => Handshake.Agente(a, chaveAgente));
        Stream professor;
        try
        {
            professor = Handshake.Professor(p, chaveProfessor, aceitaSemChave);
        }
        catch
        {
            a.Dispose(); // como o agente real: fecha a conexão se algo deu errado
            try { tarefaAgente.Wait(); } catch (AggregateException) { /* o erro do agente é esperado */ }
            throw;
        }
        return (tarefaAgente.GetAwaiter().GetResult(), professor);
    }

    [Fact]
    public void Mesma_chave_abre_o_canal_nos_dois_sentidos()
    {
        var (agente, professor) = Conectar(ChaveA, ChaveA);
        var paraProfessor = System.Text.Encoding.UTF8.GetBytes("olá professor");
        var paraAgente = System.Text.Encoding.UTF8.GetBytes("olá agente");
        agente.Write(paraProfessor);
        professor.Write(paraAgente);

        var recebidoProfessor = new byte[paraProfessor.Length];
        professor.ReadExactly(recebidoProfessor);
        var recebidoAgente = new byte[paraAgente.Length];
        agente.ReadExactly(recebidoAgente);

        Assert.Equal("olá professor", System.Text.Encoding.UTF8.GetString(recebidoProfessor));
        Assert.Equal("olá agente", System.Text.Encoding.UTF8.GetString(recebidoAgente));
    }

    [Fact]
    public void Chaves_diferentes_nao_abrem_o_canal()
    {
        Assert.Throws<AutenticacaoException>(() => Conectar(ChaveA, ChaveB));
    }

    [Fact]
    public void Professor_sem_chave_e_recusado_por_agente_com_chave()
    {
        var erro = Assert.Throws<AutenticacaoException>(() => Conectar(ChaveA, null));
        Assert.Contains("chave da sala", erro.Message);
    }

    [Fact]
    public void Professor_com_chave_recusa_agente_sem_chave_a_menos_que_seja_teste_local()
    {
        Assert.Throws<AutenticacaoException>(() => Conectar(null, ChaveA, aceitaSemChave: false));

        var (agente, professor) = Conectar(null, ChaveA, aceitaSemChave: true); // 127.0.0.1: segue sem proteção
        agente.Write([1, 2, 3]);
        var b = new byte[3];
        professor.ReadExactly(b);
        Assert.Equal<byte>([1, 2, 3], b);
    }

    [Fact]
    public void Mensagens_grandes_atravessam_em_varios_registros()
    {
        var (agente, professor) = Conectar(ChaveA, ChaveA);
        var dados = RandomNumberGenerator.GetBytes(700_000); // maior que o limite de um registro
        var envio = Task.Run(() => agente.Write(dados));
        var recebido = new byte[dados.Length];
        professor.ReadExactly(recebido);
        envio.Wait();
        Assert.Equal(dados, recebido);
    }

    [Fact]
    public void O_que_passa_pela_rede_nao_contem_o_texto_original()
    {
        var (a, p, _) = Par();
        var proxima = Task.Run(() => Handshake.Agente(a, ChaveA));
        using var espiao = new Espiao(p);
        var professor = Handshake.Professor(espiao, ChaveA, false);
        var agente = proxima.GetAwaiter().GetResult();

        var segredo = "SEGREDO-DA-TELA-DO-ALUNO"u8.ToArray();
        agente.Write(segredo);
        var lido = new byte[segredo.Length];
        professor.ReadExactly(lido);

        Assert.Equal(segredo, lido);
        Assert.False(espiao.Visto.AsSpan().IndexOf(segredo) >= 0, "o texto apareceu em claro na conexão");
    }

    [Fact]
    public void Registro_adulterado_na_rede_e_detectado()
    {
        var (a, p, _) = Par();
        var proxima = Task.Run(() => Handshake.Agente(a, ChaveA));
        using var espiao = new Espiao(p) { AdulterarProximaLeituraDepoisDe = 49 }; // depois do aperto de mão
        var professor = Handshake.Professor(espiao, ChaveA, false);
        var agente = proxima.GetAwaiter().GetResult();

        agente.Write("mensagem importante"u8);
        Assert.ThrowsAny<CryptographicException>(() => professor.ReadExactly(new byte[19]));
    }

    [Fact]
    public void Registro_repetido_e_detectado()
    {
        // O mesmo registro entregue duas vezes (replay) falha na segunda: o contador (nonce) já avançou.
        var chaveEnvio = RandomNumberGenerator.GetBytes(32);
        var ida = new MemoryStream();
        var emissor = new CanalCifrado(ida, chaveEnvio, RandomNumberGenerator.GetBytes(32));
        emissor.Write([9, 9, 9]);
        byte[] registro = ida.ToArray();

        var repetido = new MemoryStream([.. registro, .. registro]);
        var receptor = new CanalCifrado(repetido, RandomNumberGenerator.GetBytes(32), chaveEnvio);
        var b = new byte[3];
        receptor.ReadExactly(b);
        Assert.ThrowsAny<CryptographicException>(() => receptor.ReadExactly(b));
    }

    [Fact]
    public void Conexao_fechada_vira_fim_de_leitura()
    {
        var (agente, professor) = Conectar(ChaveA, ChaveA);
        agente.Dispose();
        Assert.Equal(0, professor.Read(new byte[8]));
    }

    /// <summary>Fluxo que guarda tudo que passa e, se pedido, troca um bit depois de N bytes lidos.</summary>
    sealed class Espiao(Stream interno) : Stream
    {
        readonly List<byte> _visto = [];
        int _lidos;
        public int? AdulterarProximaLeituraDepoisDe { get; set; }
        public byte[] Visto { get { lock (_visto) return [.. _visto]; } }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = interno.Read(buffer, offset, count);
            for (int i = 0; i < n; i++)
            {
                if (AdulterarProximaLeituraDepoisDe is { } limite && _lidos + i == limite + 8)
                    buffer[offset + i] ^= 0x01;
            }
            lock (_visto) _visto.AddRange(buffer.AsSpan(offset, n).ToArray());
            _lidos += n;
            return n;
        }

        public override void Write(byte[] buffer, int offset, int count) => interno.Write(buffer, offset, count);
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => interno.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) interno.Dispose(); base.Dispose(disposing); }
    }
}

public sealed class PacoteSalaTestes
{
    const string Senha = "uma senha boa 123";

    [Fact]
    public void Exportar_e_importar_devolve_a_mesma_sala()
    {
        var sala = ConfigSala.Nova("  Escola Estadual X ", " Laboratório 1 ");
        var volta = PacoteSala.Importar(PacoteSala.Exportar(sala, Senha, voltas: 1000), Senha);
        Assert.Equal("Escola Estadual X", volta.Escola);
        Assert.Equal("Laboratório 1", volta.Sala);
        Assert.Equal(sala.Chave, volta.Chave);
        Assert.Equal(ConfigSala.TamanhoChave, volta.Chave.Length);
    }

    [Fact]
    public void Chaves_novas_sao_diferentes_a_cada_vez() =>
        Assert.NotEqual(ConfigSala.Nova("E", "S").Chave, ConfigSala.Nova("E", "S").Chave);

    [Fact]
    public void Senha_errada_e_recusada()
    {
        var arquivo = PacoteSala.Exportar(ConfigSala.Nova("E", "S"), Senha, 1000);
        Assert.Throws<SenhaPacoteIncorretaException>(() => PacoteSala.Importar(arquivo, "outra senha errada"));
        Assert.Throws<SenhaPacoteIncorretaException>(() => PacoteSala.Importar(arquivo, ""));
    }

    [Fact]
    public void Senha_curta_nao_e_aceita_ao_exportar() =>
        Assert.Throws<ArgumentException>(() => PacoteSala.Exportar(ConfigSala.Nova("E", "S"), "curta", 1000));

    [Fact]
    public void Arquivo_adulterado_em_qualquer_byte_e_recusado()
    {
        var arquivo = PacoteSala.Exportar(ConfigSala.Nova("E", "S"), Senha, 1000);
        for (int i = 0; i < arquivo.Length; i++)
        {
            var estragado = (byte[])arquivo.Clone();
            estragado[i] ^= 0x01;
            Assert.ThrowsAny<Exception>(() => PacoteSala.Importar(estragado, Senha));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Arquivo_que_nao_e_pacote_e_recusado(int tamanho) =>
        Assert.Throws<PacoteInvalidoException>(() => PacoteSala.Importar(new byte[tamanho], Senha));

    [Fact]
    public void Arquivo_nao_mostra_a_chave_nem_os_nomes()
    {
        var sala = ConfigSala.Nova("Escola Secreta", "Sala Secreta");
        var arquivo = PacoteSala.Exportar(sala, Senha, 1000);
        Assert.True(arquivo.AsSpan().IndexOf("Escola Secreta"u8) < 0);
        Assert.True(arquivo.AsSpan().IndexOf(sala.Chave) < 0);
    }

    [Fact]
    public void Arquivos_exportados_da_mesma_sala_diferem()
    {
        var sala = ConfigSala.Nova("E", "S");
        Assert.NotEqual(PacoteSala.Exportar(sala, Senha, 1000), PacoteSala.Exportar(sala, Senha, 1000));
    }
}

public sealed class ConfigSalaArmazenamentoTestes : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "controlesala-testes-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true); }

    [Fact]
    public void Agente_grava_e_le_a_sala()
    {
        string caminho = Path.Combine(_pasta, "agente.json");
        var sala = ConfigSala.Nova("Escola", "Sala 3");
        ConfigAgente.Salvar(sala, caminho, restringir: false);
        var lida = ConfigAgente.Carregar(caminho);
        Assert.NotNull(lida);
        Assert.Equal(sala.Chave, lida!.Chave);
        Assert.Equal("Sala 3", lida.Sala);
    }

    [Fact]
    public void Agente_sem_arquivo_ou_com_arquivo_ruim_devolve_nulo()
    {
        Assert.Null(ConfigAgente.Carregar(Path.Combine(_pasta, "nao-existe.json")));
        Directory.CreateDirectory(_pasta);
        string ruim = Path.Combine(_pasta, "ruim.json");
        File.WriteAllText(ruim, """{"Escola":"E","Sala":"S","Chave":"AAAA"}"""); // chave curta demais
        Assert.Null(ConfigAgente.Carregar(ruim));
        File.WriteAllText(ruim, "não é json");
        Assert.Null(ConfigAgente.Carregar(ruim));
    }

    [Fact]
    public void Professor_guarda_a_chave_protegida_pelo_Windows()
    {
        string caminho = Path.Combine(_pasta, "sala-config.json");
        var sala = ConfigSala.Nova("Escola", "Sala 3");
        ConfigProfessor.Salvar(sala, caminho);

        Assert.True(File.ReadAllBytes(caminho).AsSpan().IndexOf(sala.Chave) < 0);
        Assert.DoesNotContain(Convert.ToBase64String(sala.Chave), File.ReadAllText(caminho));
        var lida = ConfigProfessor.Carregar(caminho);
        Assert.Equal(sala.Chave, lida!.Chave);
    }

    [Fact]
    public void Identificacao_ida_e_volta_e_sem_caracteres_de_controle()
    {
        var msg = Protocolo.MontarIdentificacao("Prof. Diego");
        using var fluxo = new MemoryStream(msg);
        byte[] buffer = new byte[16];
        var lida = Protocolo.Ler(fluxo, ref buffer);
        Assert.Equal(TipoMensagem.Identificacao, lida!.Value.Tipo);
        Assert.Equal("Prof. Diego", Protocolo.LerIdentificacao(buffer.AsSpan(0, lida.Value.Tamanho)));
        Assert.Equal("Ana", Protocolo.LerIdentificacao("An\u0007a\n"u8));
    }
}
