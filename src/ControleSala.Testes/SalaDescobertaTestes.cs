using System.Net;
using System.Net.Sockets;
using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public sealed class SalaTestes : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "controlesala-testes-" + Guid.NewGuid().ToString("N"));
    string Arquivo => Path.Combine(_pasta, "sala.json");

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void Adicionar_persiste_e_mantem_a_ordem()
    {
        var sala = new RepositorioSala(Arquivo);
        sala.Adicionar("Mesa 01", "192.168.0.11");
        sala.Adicionar("Mesa 02", "192.168.0.12", 5000);

        var depois = new RepositorioSala(Arquivo).Computadores;
        Assert.Equal(["Mesa 01", "Mesa 02"], depois.Select(c => c.Nome));
        Assert.Equal(Protocolo.PortaPadrao, depois[0].Porta);
        Assert.Equal(5000, depois[1].Porta);
    }

    [Fact]
    public void Nome_escolhido_vale_uma_vez_e_o_professor_pode_trocar_depois()
    {
        var sala = new RepositorioSala(Arquivo);
        var pc = sala.Adicionar("DESKTOP-ABC", "10.0.0.5");

        Assert.True(sala.AplicarNomeEscolhido(pc, "PC 05"));
        Assert.Equal("PC 05", new RepositorioSala(Arquivo).Computadores[0].Nome); // persistiu

        pc = sala.Atualizar(pc, "Mesa do fundo", pc.Endereco, pc.Porta);
        Assert.False(sala.AplicarNomeEscolhido(pc, "PC 05")); // o mesmo nome não desfaz a troca do professor
        Assert.Equal("Mesa do fundo", pc.Nome);

        Assert.True(sala.AplicarNomeEscolhido(pc, "PC 06")); // escolheram outro nome no PC: vale de novo
        Assert.Equal("PC 06", pc.Nome);
    }

    [Theory]
    [InlineData("PC 01", "PC 01")]
    [InlineData("  PC 02\r\n", "PC 02")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Nome_do_pc_e_limpo_antes_de_usar(string? entrada, string? esperado)
    {
        Assert.Equal(esperado, NomeDoPc.Validar(entrada));
    }

    [Fact]
    public void Nome_do_pc_recusa_nome_longo_demais()
    {
        Assert.NotNull(NomeDoPc.Validar(new string('x', NomeDoPc.TamanhoMaximo)));
        Assert.Null(NomeDoPc.Validar(new string('x', NomeDoPc.TamanhoMaximo + 1)));
    }

    [Fact]
    public void Nome_do_pc_fica_gravado_e_chega_inteiro_pelo_protocolo()
    {
        string arquivo = Path.Combine(_pasta, "nome-pc.txt");
        Assert.Null(NomeDoPc.Ler(arquivo));

        NomeDoPc.Salvar("Laboratório 03", arquivo);
        string? lido = NomeDoPc.Ler(arquivo);
        Assert.Equal("Laboratório 03", lido);

        using var fluxo = new MemoryStream(Protocolo.MontarNomeEscolhido(lido!));
        var buffer = new byte[256];
        var msg = Protocolo.Ler(fluxo, ref buffer);
        Assert.Equal(TipoMensagem.NomeEscolhido, msg!.Value.Tipo);
        Assert.Equal("Laboratório 03", Protocolo.LerNomeEscolhido(buffer.AsSpan(0, msg.Value.Tamanho)));
    }

    [Theory]
    [InlineData("PC 18")]
    [InlineData("PC18")]
    [InlineData("pc-18")]
    public void Mudou_de_endereco_acha_pelo_nome_sem_contar_espacos(string nomeNaLista)
    {
        var sala = new RepositorioSala(Arquivo);
        sala.Adicionar("PC 17", "10.0.0.17");
        var pc18 = sala.Adicionar(nomeNaLista, "10.0.0.18");

        var achado = new AgenteEncontrado("PC18", "10.0.0.156", Protocolo.PortaPadrao);
        Assert.Same(pc18, sala.MudouDeEndereco(achado));

        sala.DefinirEndereco(pc18, achado.Endereco);
        Assert.Null(sala.MudouDeEndereco(achado)); // já corrigido: o próximo anúncio não muda nada
        Assert.Equal("10.0.0.156", new RepositorioSala(Arquivo).Computadores[1].Endereco); // persistiu
    }

    [Fact]
    public void Mudou_de_endereco_nao_toma_o_endereco_de_outro_computador()
    {
        var sala = new RepositorioSala(Arquivo);
        sala.Adicionar("PC 01", "10.0.0.1");
        sala.Adicionar("PC 02", "10.0.0.2");
        Assert.Null(sala.MudouDeEndereco(new AgenteEncontrado("PC02", "10.0.0.1", Protocolo.PortaPadrao)));
    }

    [Fact]
    public void Mudou_de_endereco_ignora_nome_repetido_ou_desconhecido()
    {
        var sala = new RepositorioSala(Arquivo);
        sala.Adicionar("PC 01", "10.0.0.1");
        sala.Adicionar("PC01", "10.0.0.2");
        Assert.Null(sala.MudouDeEndereco(new AgenteEncontrado("PC01", "10.0.0.9", Protocolo.PortaPadrao)));
        Assert.Null(sala.MudouDeEndereco(new AgenteEncontrado("PC99", "10.0.0.9", Protocolo.PortaPadrao)));
    }

    [Fact]
    public void Ordenar_por_nome_usa_ordem_natural()
    {
        var sala = new RepositorioSala(Arquivo);
        foreach (var n in new[] { "PC10", "PC2", "PC1", "Sala", "PC20" })
            sala.Adicionar(n, "10.0.0." + sala.Computadores.Count);
        sala.OrdenarPorNome();
        Assert.Equal(["PC1", "PC2", "PC10", "PC20", "Sala"], sala.Computadores.Select(c => c.Nome));
    }

    [Fact]
    public void Numerar_renomeia_em_ordem_com_dois_digitos()
    {
        var sala = new RepositorioSala(Arquivo);
        for (int i = 0; i < 3; i++) sala.Adicionar("qualquer-" + i, "10.0.1." + i);
        sala.Numerar("PC");
        Assert.Equal(["PC 01", "PC 02", "PC 03"], sala.Computadores.Select(c => c.Nome));
        Assert.Equal(["PC 01", "PC 02", "PC 03"], new RepositorioSala(Arquivo).Computadores.Select(c => c.Nome));
    }

    [Theory]
    [InlineData("", "192.168.0.1", 47810)]
    [InlineData("Mesa", "", 47810)]
    [InlineData("Mesa", "isto não é endereço", 47810)]
    [InlineData("Mesa", "192.168.0.1", 0)]
    [InlineData("Mesa", "192.168.0.1", 70000)]
    public void Dados_invalidos_sao_recusados(string nome, string endereco, int porta) =>
        Assert.Throws<ArgumentException>(() => new RepositorioSala(Arquivo).Adicionar(nome, endereco, porta));

    [Fact]
    public void Mesmo_endereco_e_porta_nao_entra_duas_vezes_mas_porta_diferente_entra()
    {
        var sala = new RepositorioSala(Arquivo);
        sala.Adicionar("A", "127.0.0.1", 47810);
        Assert.Throws<ArgumentException>(() => sala.Adicionar("B", "127.0.0.1", 47810));
        sala.Adicionar("C", "127.0.0.1", 47811);
        Assert.Equal(2, sala.Computadores.Count);
    }

    [Fact]
    public void Atualizar_troca_o_objeto_na_mesma_posicao()
    {
        var sala = new RepositorioSala(Arquivo);
        var a = sala.Adicionar("A", "10.0.0.1");
        var b = sala.Adicionar("B", "10.0.0.2");
        var novo = sala.Atualizar(a, "A2", "10.0.0.1", 47810); // mesmo endereço do próprio não conta como repetido
        Assert.NotSame(a, novo);
        Assert.Same(novo, sala.Computadores[0]);
        Assert.Same(b, sala.Computadores[1]);
        Assert.Equal("A2", new RepositorioSala(Arquivo).Computadores[0].Nome);
    }

    [Fact]
    public void Mover_e_remover()
    {
        var sala = new RepositorioSala(Arquivo);
        var a = sala.Adicionar("A", "10.0.0.1");
        var b = sala.Adicionar("B", "10.0.0.2");
        var c = sala.Adicionar("C", "10.0.0.3");

        Assert.False(sala.Mover(a, -1)); // já é o primeiro
        Assert.False(sala.Mover(c, +1)); // já é o último
        Assert.True(sala.Mover(c, -2));
        Assert.Equal(["C", "A", "B"], sala.Computadores.Select(x => x.Nome));

        Assert.True(sala.Remover(a));
        Assert.False(sala.Remover(a));
        Assert.Equal(["C", "B"], new RepositorioSala(Arquivo).Computadores.Select(x => x.Nome));
    }

    [Fact]
    public void Arquivo_corrompido_vira_copia_e_recomeca_vazio()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Arquivo, "[ não é json");
        Assert.Empty(new RepositorioSala(Arquivo).Computadores);
        Assert.True(File.Exists(Arquivo + ".corrompido"));
    }
}

public class DescobertaTestes
{
    [Fact]
    public void Anuncio_ida_e_volta()
    {
        var bytes = Descoberta.MontarAnuncio("JOAQUIM-PC", 47810);
        Assert.True(Descoberta.TentarLer(bytes, out var nome, out var porta));
        Assert.Equal("JOAQUIM-PC", nome);
        Assert.Equal(47810, porta);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("{}")]
    [InlineData("""{"app":"Outro","v":1,"nome":"PC","porta":1}""")]
    [InlineData("""{"app":"ControleSala","v":2,"nome":"PC","porta":1}""")]
    [InlineData("""{"app":"ControleSala","v":1,"nome":"","porta":1}""")]
    [InlineData("""{"app":"ControleSala","v":1,"nome":"PC","porta":0}""")]
    [InlineData("""{"app":"ControleSala","v":1,"nome":"PC","porta":99999}""")]
    [InlineData("""{"app":"ControleSala","v":1,"nome":"PC"}""")]
    [InlineData("""{"app":"ControleSala","v":"1","nome":"PC","porta":1}""")]
    public void Anuncio_invalido_e_ignorado(string texto) =>
        Assert.False(Descoberta.TentarLer(System.Text.Encoding.UTF8.GetBytes(texto), out _, out _));

    [Fact]
    public void Anuncio_gigante_e_ignorado() =>
        Assert.False(Descoberta.TentarLer(new byte[600], out _, out _));

    [Fact]
    public void Caracteres_de_controle_no_nome_sao_removidos()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("""{"app":"ControleSala","v":1,"nome":"PC\u0007 01\n","porta":47810}""");
        Assert.True(Descoberta.TentarLer(bytes, out var nome, out _));
        Assert.Equal("PC 01", nome);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(5, 30)]
    [InlineData(1000, 30)]
    public void Espera_para_religar_o_agente_cresce_ate_30_s(int quedas, int segundos) =>
        Assert.Equal(TimeSpan.FromSeconds(segundos), VigiaAgente.EsperaAposQuedas(quedas));

    [Fact]
    public async Task Escutar_avisa_cada_anuncio_e_para_ao_cancelar()
    {
        int porta = 47800 + Random.Shared.Next(9);
        var recebido = new TaskCompletionSource<AgenteEncontrado>();
        using var fim = new CancellationTokenSource();
        var escuta = Descoberta.EscutarAsync(a => recebido.TrySetResult(a), porta, fim.Token);
        await Task.Delay(300); // dá tempo de abrir o socket de escuta

        using var envio = new UdpClient();
        var anuncio = Descoberta.MontarAnuncio("PC18", 47810);
        await envio.SendAsync(anuncio.AsMemory(), new IPEndPoint(IPAddress.Loopback, porta));

        var achado = await recebido.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new AgenteEncontrado("PC18", "127.0.0.1", 47810), achado);

        fim.Cancel();
        await escuta.WaitAsync(TimeSpan.FromSeconds(3)); // termina sem lançar erro
    }

    [Fact]
    public async Task Procurar_acha_um_agente_que_anuncia_na_mesma_maquina()
    {
        int porta = 47900 + Random.Shared.Next(90);
        var busca = Descoberta.ProcurarAsync(TimeSpan.FromSeconds(1.5), porta);
        await Task.Delay(300); // dá tempo de abrir o socket de escuta

        using var envio = new UdpClient();
        var anuncio = Descoberta.MontarAnuncio("PC-DO-ALUNO", 47810);
        await envio.SendAsync(anuncio.AsMemory(), new IPEndPoint(IPAddress.Loopback, porta));
        await envio.SendAsync(anuncio.AsMemory(), new IPEndPoint(IPAddress.Loopback, porta)); // repetido não duplica
        await envio.SendAsync(new byte[] { 1, 2, 3 }.AsMemory(), new IPEndPoint(IPAddress.Loopback, porta)); // lixo é ignorado

        var achados = await busca;
        var achado = Assert.Single(achados);
        Assert.Equal(new AgenteEncontrado("PC-DO-ALUNO", "127.0.0.1", 47810), achado);
    }
}

public class ProtocoloModoTestes
{
    [Theory]
    [InlineData(ModoVideo.Miniatura)]
    [InlineData(ModoVideo.Completo)]
    public void DefinirModo_ida_e_volta(ModoVideo modo)
    {
        var msg = Protocolo.MontarDefinirModo(modo);
        using var fluxo = new MemoryStream(msg);
        byte[] buffer = new byte[16];
        var lida = Protocolo.Ler(fluxo, ref buffer);
        Assert.Equal(TipoMensagem.DefinirModo, lida!.Value.Tipo);
        Assert.Equal(modo, Protocolo.LerModo(buffer.AsSpan(0, lida.Value.Tamanho)));
    }

    [Fact]
    public void Modo_vazio_ou_desconhecido_vira_miniatura()
    {
        Assert.Equal(ModoVideo.Miniatura, Protocolo.LerModo([]));
        Assert.Equal(ModoVideo.Miniatura, Protocolo.LerModo([200]));
    }
}
