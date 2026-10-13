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
