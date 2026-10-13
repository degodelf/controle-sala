using System.Security.Cryptography;
using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class AssinaturaInstaladorTestes : IDisposable
{
    readonly string _pasta = Directory.CreateTempSubdirectory("cs-assinatura-").FullName;
    readonly ECDsa _autor = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose()
    {
        _autor.Dispose();
        Directory.Delete(_pasta, recursive: true);
    }

    string Instalador(int tamanho = 200_000)
    {
        string f = Path.Combine(_pasta, $"Setup-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(f, RandomNumberGenerator.GetBytes(tamanho));
        return f;
    }

    [Fact]
    public void Assinado_confere_e_devolve_a_versao()
    {
        string f = Instalador();
        AssinaturaInstalador.Assinar(f, "0.3.0", _autor);
        Assert.Equal("0.3.0", AssinaturaInstalador.Verificar(f, _autor));
        Assert.Equal("0.3.0", AssinaturaInstalador.VersaoAnunciada(f));
    }

    [Fact]
    public void Assinar_so_acrescenta_no_fim()
    {
        string f = Instalador();
        byte[] antes = File.ReadAllBytes(f);
        AssinaturaInstalador.Assinar(f, "1.2.3", _autor);
        byte[] depois = File.ReadAllBytes(f);
        Assert.True(depois.Length > antes.Length);
        Assert.Equal(antes, depois[..antes.Length]); // o programa em si não muda: continua rodando igual
    }

    [Fact]
    public void Sem_assinatura_e_recusado()
    {
        Assert.Null(AssinaturaInstalador.Verificar(Instalador(), _autor));
        Assert.Null(AssinaturaInstalador.VersaoAnunciada(Instalador()));
    }

    [Fact]
    public void Instalador_alterado_depois_de_assinado_e_recusado()
    {
        string f = Instalador();
        AssinaturaInstalador.Assinar(f, "0.3.0", _autor);
        using (var s = new FileStream(f, FileMode.Open, FileAccess.ReadWrite))
        {
            s.Position = 1000;
            int b = s.ReadByte();
            s.Position = 1000;
            s.WriteByte((byte)(b ^ 0xFF));
        }
        Assert.Null(AssinaturaInstalador.Verificar(f, _autor));
    }

    [Fact]
    public void Versao_trocada_no_final_e_recusada()
    {
        string f = Instalador();
        AssinaturaInstalador.Assinar(f, "0.3.0", _autor);
        byte[] b = File.ReadAllBytes(f);
        // troca "0.3.0" por "9.3.0" (mesmo tamanho) sem assinar de novo
        int pos = b.Length - 8 - 64 - 2 - 5;
        Assert.Equal((byte)'0', b[pos]);
        b[pos] = (byte)'9';
        File.WriteAllBytes(f, b);
        Assert.Equal("9.3.0", AssinaturaInstalador.VersaoAnunciada(f));
        Assert.Null(AssinaturaInstalador.Verificar(f, _autor));
    }

    [Fact]
    public void Assinado_por_outra_chave_e_recusado()
    {
        string f = Instalador();
        using var intruso = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        AssinaturaInstalador.Assinar(f, "0.3.0", intruso);
        Assert.Null(AssinaturaInstalador.Verificar(f, _autor));
    }

    [Fact]
    public void Nao_assina_duas_vezes_nem_versao_invalida()
    {
        string f = Instalador();
        AssinaturaInstalador.Assinar(f, "0.3.0", _autor);
        Assert.Throws<InvalidOperationException>(() => AssinaturaInstalador.Assinar(f, "0.4.0", _autor));
        Assert.Throws<ArgumentException>(() => AssinaturaInstalador.Assinar(Instalador(), "versão nova", _autor));
    }

    [Fact]
    public void Chave_publica_embutida_e_valida()
    {
        using var chave = ChaveAtualizacao.Publica();
        Assert.Equal(256, chave.KeySize);
    }

    [Fact]
    public void Chave_publica_embutida_nao_aceita_assinatura_de_outra_chave()
    {
        string f = Instalador();
        AssinaturaInstalador.Assinar(f, "0.3.0", _autor);
        Assert.Null(AssinaturaInstalador.Verificar(f)); // sem chave = a do programa (do autor de verdade)
    }
}

public class VersoesTestes
{
    [Theory]
    [InlineData("0.3.0", "0.2.0", true)]
    [InlineData("0.10.0", "0.9.0", true)]   // compara números, não texto
    [InlineData("1.0.0", "0.99.9", true)]
    [InlineData("0.3.0", "0.3.0", false)]   // mesma versão: não reinstala
    [InlineData("0.2.0", "0.3.0", false)]   // mais antiga: nunca volta versão
    [InlineData("0.3", "0.3.0", false)]
    [InlineData("0.3.0", "0.3", false)]
    [InlineData("abc", "0.1.0", false)]
    [InlineData("0.3.0", null, true)]       // agente que não informa versão (antigo)
    public void MaisNova(string versao, string? atual, bool esperado) => Assert.Equal(esperado, Versoes.MaisNova(versao, atual));

    [Fact]
    public void Atual_tem_tres_partes() => Assert.Matches(@"^\d+\.\d+\.\d+$", Versoes.Atual);
}

public class ProtocoloAtualizacaoTestes
{
    [Fact]
    public void Inicio_de_atualizacao_usa_o_mesmo_formato_do_inicio_de_arquivo()
    {
        byte[] msg = Protocolo.MontarAtualizacaoInicio(7, 123_456_789, "0.3.0");
        Assert.Equal((byte)TipoMensagem.AtualizacaoInicio, msg[4]);
        var inicio = Protocolo.LerArquivoInicio(msg.AsSpan(5));
        Assert.Equal(new InicioArquivo(7, 123_456_789, "0.3.0"), inicio);
    }

    [Fact]
    public void Versao_do_agente_ida_e_volta()
    {
        byte[] msg = Protocolo.MontarVersaoAgente("0.3.0");
        Assert.Equal((byte)TipoMensagem.VersaoAgente, msg[4]);
        Assert.Equal("0.3.0", Protocolo.LerVersaoAgente(msg.AsSpan(5)));
    }

    [Fact]
    public void Versao_do_agente_ignora_lixo() =>
        Assert.Equal("1.2.3", Protocolo.LerVersaoAgente("1.2.3\r\n<script>"u8));
}

public class RecebedorAtualizacaoTestes : IDisposable
{
    readonly string _pasta = Directory.CreateTempSubdirectory("cs-atualizacao-").FullName;
    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void Recebedor_normal_recusa_programa()
    {
        using var r = new RecebedorArquivos(_pasta, _ => long.MaxValue);
        Assert.False(r.Iniciar(1, "ControleSala-Setup.exe", 10).Ok);
    }

    [Fact]
    public void Recebedor_de_atualizacao_aceita_o_instalador()
    {
        using var r = new RecebedorArquivos(_pasta, _ => long.MaxValue, conferirTipo: false);
        byte[] dados = RandomNumberGenerator.GetBytes(1000);
        Assert.True(r.Iniciar(1, "ControleSala-Setup.exe", dados.Length).Ok);
        Assert.True(r.Dados(1, dados).Ok);
        var fim = r.Fim(1, SHA256.HashData(dados));
        Assert.True(fim.Ok);
        Assert.Equal(Path.Combine(_pasta, "ControleSala-Setup.exe"), fim.Caminho);
    }

    [Fact]
    public void Recebedor_de_atualizacao_continua_impedindo_sair_da_pasta()
    {
        using var r = new RecebedorArquivos(_pasta, _ => long.MaxValue, conferirTipo: false);
        Assert.False(r.Iniciar(1, @"..\fora.exe", 10).Ok);
    }
}
