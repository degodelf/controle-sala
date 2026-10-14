using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class EntradaTestes
{
    static EventoEntrada Ida_e_volta(byte[] msg)
    {
        // A mensagem é [int32 tamanho][byte tipo][corpo]; o corpo começa no byte 5.
        Assert.Equal((byte)TipoMensagem.Entrada, msg[4]);
        var ev = Protocolo.LerEntrada(msg.AsSpan(5));
        Assert.NotNull(ev);
        return ev!.Value;
    }

    [Fact]
    public void Mouse_mover()
    {
        var ev = Ida_e_volta(Protocolo.MontarEntradaMouseMover(12345, 54321));
        Assert.Equal(TipoEntrada.MouseMover, ev.Tipo);
        Assert.Equal(12345, ev.X);
        Assert.Equal(54321, ev.Y);
    }

    [Theory]
    [InlineData(BotaoMouse.Esquerdo, true)]
    [InlineData(BotaoMouse.Direito, false)]
    [InlineData(BotaoMouse.Meio, true)]
    public void Mouse_botao(BotaoMouse botao, bool pressionado)
    {
        var ev = Ida_e_volta(Protocolo.MontarEntradaMouseBotao(100, 200, botao, pressionado));
        Assert.Equal(TipoEntrada.MouseBotao, ev.Tipo);
        Assert.Equal(100, ev.X);
        Assert.Equal(200, ev.Y);
        Assert.Equal(botao, ev.Botao);
        Assert.Equal(pressionado, ev.Pressionado);
    }

    [Fact]
    public void Mouse_roda()
    {
        var ev = Ida_e_volta(Protocolo.MontarEntradaMouseRoda(1, 2, -240));
        Assert.Equal(TipoEntrada.MouseRoda, ev.Tipo);
        Assert.Equal(-240, ev.Roda);
    }

    [Theory]
    [InlineData(65, true, false)]   // 'A' apertada
    [InlineData(37, false, true)]   // seta esquerda solta (estendida)
    public void Tecla(ushort vk, bool pressionado, bool estendida)
    {
        var ev = Ida_e_volta(Protocolo.MontarEntradaTecla(vk, pressionado, estendida));
        Assert.Equal(TipoEntrada.Tecla, ev.Tipo);
        Assert.Equal(vk, ev.Tecla);
        Assert.Equal(pressionado, ev.Pressionado);
        Assert.Equal(estendida, ev.Estendida);
    }

    [Fact]
    public void Corpo_invalido_retorna_nulo()
    {
        Assert.Null(Protocolo.LerEntrada([]));
        Assert.Null(Protocolo.LerEntrada([(byte)TipoEntrada.MouseMover, 1])); // curto demais
        Assert.Null(Protocolo.LerEntrada([(byte)TipoEntrada.MouseBotao, 0, 0, 0, 0, 9, 1])); // botão inválido
    }
}
