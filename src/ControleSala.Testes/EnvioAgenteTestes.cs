using System.Net.Sockets;
using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class FilaDeEnvioTestes
{
    static byte[] M(byte n) => [n];

    static async Task<List<byte>> Ler(FilaDeEnvio fila)
    {
        fila.Encerrar();
        var lidas = new List<byte>();
        await foreach (var m in fila.LerTudo()) lidas.Add(m[0]);
        return lidas;
    }

    [Fact]
    public async Task Mensagens_de_controle_nao_se_perdem_antes_de_comecar_a_ler()
    {
        var fila = new FilaDeEnvio();
        for (byte i = 1; i <= 6; i++) fila.Controle(M(i));   // Olá, MAC, nome, Defender, limpeza e versão
        Assert.Equal([1, 2, 3, 4, 5, 6], await Ler(fila));
    }

    [Fact]
    public async Task Video_em_excesso_e_descartado_mas_o_controle_ainda_passa()
    {
        var fila = new FilaDeEnvio(maxVideo: 4);
        for (byte i = 1; i <= 4; i++) Assert.True(fila.Video(M(i)));
        Assert.False(fila.Video(M(5)));
        fila.Controle(M(6));
        Assert.Equal([1, 2, 3, 4, 6], await Ler(fila));
    }

    [Fact]
    public async Task Video_lido_libera_lugar()
    {
        var fila = new FilaDeEnvio(maxVideo: 1);
        Assert.True(fila.Video(M(1)));
        Assert.False(fila.Video(M(2)));
        var leitura = fila.LerTudo().GetAsyncEnumerator();
        Assert.True(await leitura.MoveNextAsync());
        Assert.True(fila.Video(M(3)));
        await leitura.DisposeAsync();
    }

    [Fact]
    public void Depois_de_encerrar_nao_aceita_video()
    {
        var fila = new FilaDeEnvio();
        fila.Encerrar();
        Assert.False(fila.Video(M(1)));
        fila.Controle(M(2));   // a conexão fechou: só é ignorada, sem erro
    }
}

public class LacoDeAceiteTestes
{
    [Fact]
    public async Task Erro_de_rede_nao_para_de_aceitar_conexoes()
    {
        int chamada = 0;
        var aceitas = new List<TcpClient>();
        var avisos = new List<string>();
        using var cliente = new TcpClient();

        Task<TcpClient> Aceitar() => ++chamada switch
        {
            1 => throw new SocketException((int)SocketError.ConnectionReset),
            2 => Task.FromResult(cliente),
            _ => throw new ObjectDisposedException("servidor"),
        };
        await LacoDeAceite.ExecutarAsync(Aceitar, () => true, aceitas.Add, avisos.Add);

        Assert.Equal([cliente], aceitas);
        Assert.Single(avisos);
    }

    [Fact]
    public async Task Servidor_desligado_encerra_o_laco()
    {
        static Task<TcpClient> Aceitar() => throw new InvalidOperationException("parou");
        await LacoDeAceite.ExecutarAsync(Aceitar, () => false, _ => Assert.Fail("não devia aceitar"));
    }
}

public class ConferenciaDeAgenteTestes
{
    static readonly byte[] Chave = System.Security.Cryptography.RandomNumberGenerator.GetBytes(ConfigSala.TamanhoChave);

    /// <summary>Um agente de verdade do ponto de vista da rede: aperto de mão e mensagens de abertura.</summary>
    sealed class AgenteFalso : IDisposable
    {
        readonly TcpListener _ouvinte = new(System.Net.IPAddress.Loopback, 0);
        public int Porta { get; }

        public AgenteFalso(byte[]? chave, params byte[][] abertura)
        {
            _ouvinte.Start();
            Porta = ((System.Net.IPEndPoint)_ouvinte.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var tcp = await _ouvinte.AcceptTcpClientAsync();
                    tcp.ReceiveTimeout = 3000;
                    var fluxo = Handshake.Agente(tcp.GetStream(), chave);
                    foreach (var m in abertura) fluxo.Write(m);
                    fluxo.ReadByte();   // espera o outro lado fechar
                }
                catch (Exception e) when (e is IOException or SocketException or AutenticacaoException or ObjectDisposedException) { }
            });
        }

        public void Dispose() => _ouvinte.Stop();
    }

    static byte[] Ola(string maquina) => Protocolo.MontarOla(new Ola(1920, 1080, "teste", maquina));
    static byte[] Versao => Protocolo.MontarVersaoAgente("0.7.8");
    static AgenteEncontrado Anuncio(string nome, int porta) => new(nome, "127.0.0.1", porta, "etiqueta");

    [Fact]
    public async Task Confirma_quem_tem_a_chave_e_o_nome_anunciado()
    {
        using var agente = new AgenteFalso(Chave, Ola("PC-05"), Versao);
        Assert.True(await ConferenciaDeAgente.ConfirmaAsync(Anuncio("PC 05", agente.Porta), Chave));
    }

    [Fact]
    public async Task Nome_escolhido_vale_mais_que_o_nome_do_windows()
    {
        using var agente = new AgenteFalso(Chave, Ola("DESKTOP-A1"), Protocolo.MontarNomeEscolhido("PC 05"), Versao);
        Assert.True(await ConferenciaDeAgente.ConfirmaAsync(Anuncio("PC 05", agente.Porta), Chave));
    }

    [Fact]
    public async Task Recusa_aviso_copiado_por_quem_nao_tem_a_chave()
    {
        var outraChave = System.Security.Cryptography.RandomNumberGenerator.GetBytes(ConfigSala.TamanhoChave);
        using var agente = new AgenteFalso(outraChave, Ola("PC 05"), Versao);
        Assert.False(await ConferenciaDeAgente.ConfirmaAsync(Anuncio("PC 05", agente.Porta), Chave));
    }

    [Fact]
    public async Task Recusa_agente_sem_chave()
    {
        using var agente = new AgenteFalso(null, Ola("PC 05"), Versao);
        Assert.False(await ConferenciaDeAgente.ConfirmaAsync(Anuncio("PC 05", agente.Porta), Chave));
    }

    [Fact]
    public async Task Recusa_outro_pc_da_sala_repetindo_o_aviso_de_um_colega()
    {
        using var agente = new AgenteFalso(Chave, Ola("PC 09"), Versao);
        Assert.False(await ConferenciaDeAgente.ConfirmaAsync(Anuncio("PC 05", agente.Porta), Chave));
    }

    [Fact]
    public async Task Recusa_endereco_onde_nao_ha_nada()
    {
        var ouvinte = new TcpListener(System.Net.IPAddress.Loopback, 0);
        ouvinte.Start();
        int livre = ((System.Net.IPEndPoint)ouvinte.LocalEndpoint).Port;
        ouvinte.Stop();
        Assert.False(await ConferenciaDeAgente.ConfirmaAsync(Anuncio("PC 05", livre), Chave));
    }

    [Fact]
    public async Task Agente_ate_a_0_7_7_que_perdeu_a_versao_ainda_e_confirmado()
    {
        using var agente = new AgenteFalso(Chave, Ola("PC 05"));
        Assert.True(await ConferenciaDeAgente.ConfirmaAsync(Anuncio("PC 05", agente.Porta), Chave, TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task Agente_que_ainda_nao_disse_o_nome_vale_pela_chave()
    {
        using var agente = new AgenteFalso(Chave, Versao);
        Assert.True(await ConferenciaDeAgente.ConfirmaAsync(Anuncio("PC 05", agente.Porta), Chave));
    }
}
