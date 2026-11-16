using System.Net;
using System.Net.Sockets;
using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class VigiaEAtualizacaoTestes
{
    [Fact]
    public void Vigia_confirma_a_porta_conectando_de_verdade()
    {
        var ouvinte = new TcpListener(IPAddress.Any, 0);
        ouvinte.Start();
        int porta = ((IPEndPoint)ouvinte.LocalEndpoint).Port;
        try
        {
            Assert.True(VigiaAgente.AceitaConexao(porta, TimeSpan.FromSeconds(3)));
        }
        finally { ouvinte.Stop(); }
        // Ninguém escutando mais nessa porta: aí sim conta como agente sem porta.
        Assert.False(VigiaAgente.AceitaConexao(porta, TimeSpan.FromSeconds(3)));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 6)]
    [InlineData(2, 12)]
    [InlineData(3, 24)]
    [InlineData(4, 30)]
    [InlineData(20, 30)]
    public void Reenvio_da_atualizacao_espera_mais_a_cada_falha(int falhas, int minutos) =>
        Assert.Equal(TimeSpan.FromMinutes(minutos), RegrasDeComando.EsperaParaReenviarAtualizacao(falhas));
}
