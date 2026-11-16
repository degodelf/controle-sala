using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class RegrasDeComandoTestes
{
    [Fact]
    public void Queda_curta_continua_aparecendo_online()
    {
        Assert.False(RegrasDeComando.NaCarencia(0, 100_000));           // nunca caiu
        Assert.True(RegrasDeComando.NaCarencia(100_000, 103_000));      // caiu há 3 s: reconectando
        Assert.False(RegrasDeComando.NaCarencia(100_000, 100_000 + RegrasDeComando.CarenciaDeQuedaMs)); // passou da carência
    }

    [Theory]
    [InlineData(AcaoComando.BloquearTela, "", true)]
    [InlineData(AcaoComando.BloquearInternet, "", true)]
    [InlineData(AcaoComando.BloquearInternet, "youtube.com", true)]
    [InlineData(AcaoComando.Silenciar, "1", true)]
    [InlineData(AcaoComando.Silenciar, "0", false)]
    [InlineData(AcaoComando.DesbloquearTela, "", false)]
    [InlineData(AcaoComando.LiberarInternet, "", false)]
    [InlineData(AcaoComando.Mensagem, "oi", false)]
    public void Restritivo_e_o_que_fica_valendo(AcaoComando acao, string argumento, bool esperado) =>
        Assert.Equal(esperado, RegrasDeComando.Restritivo(acao, argumento));

    [Fact]
    public void Bloquear_e_liberar_sao_o_mesmo_assunto()
    {
        Assert.Equal("tela", RegrasDeComando.AssuntoDoEstado(AcaoComando.BloquearTela));
        Assert.Equal("tela", RegrasDeComando.AssuntoDoEstado(AcaoComando.DesbloquearTela));
        Assert.Equal("internet", RegrasDeComando.AssuntoDoEstado(AcaoComando.BloquearInternet));
        Assert.Equal("internet", RegrasDeComando.AssuntoDoEstado(AcaoComando.LiberarInternet));
        Assert.Null(RegrasDeComando.AssuntoDoEstado(AcaoComando.Desligar));
    }

    [Fact]
    public void So_repete_o_que_nao_dobra_o_efeito()
    {
        Assert.True(RegrasDeComando.PodeRepetir(AcaoComando.BloquearTela));
        Assert.True(RegrasDeComando.PodeRepetir(AcaoComando.LiberarInternet));
        Assert.False(RegrasDeComando.PodeRepetir(AcaoComando.Mensagem));   // apareceria duas vezes
        Assert.False(RegrasDeComando.PodeRepetir(AcaoComando.AbrirLink));  // abriria duas abas
        Assert.False(RegrasDeComando.PodeRepetir(AcaoComando.Limpeza));
        Assert.True(RegrasDeComando.SaiDaRede(AcaoComando.Desligar));
        Assert.True(RegrasDeComando.SaiDaRede(AcaoComando.Reiniciar));
        Assert.False(RegrasDeComando.SaiDaRede(AcaoComando.BloquearTela));
    }

    [Fact]
    public void Resumo_diz_em_quais_computadores_nao_chegou()
    {
        Assert.Equal("Bloquear tela: enviado a 24 computadores ✓", RegrasDeComando.ResumoDoEnvio("Bloquear tela", 24, []));
        Assert.Equal("Bloquear tela: enviado a 1 computador ✓", RegrasDeComando.ResumoDoEnvio("Bloquear tela", 1, []));
        Assert.Equal("Bloquear tela: 22 ok — não chegou em PC-14, PC-23.",
            RegrasDeComando.ResumoDoEnvio("Bloquear tela", 24, ["PC-23", "PC-14"]));
        Assert.EndsWith("e mais 2.", RegrasDeComando.ResumoDoEnvio("X", 10, ["a", "b", "c", "d", "e", "f", "g", "h"]));
    }

    [Theory]
    [InlineData("192.168.0.15", true)]
    [InlineData(" 10.0.0.1 ", true)]
    [InlineData("fe80::1", true)]
    [InlineData("192.168.0", false)]   // o Windows entenderia como outro endereço
    [InlineData("PC-03", false)]       // nome do computador não funciona na rede da escola
    [InlineData("192.168.0.256", false)]
    [InlineData("", false)]
    public void Endereco_do_computador_precisa_ser_ip(string endereco, bool esperado) =>
        Assert.Equal(esperado, RepositorioSala.EnderecoIpValido(endereco));
}
