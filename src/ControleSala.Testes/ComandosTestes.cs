using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class LinksTestes
{
    [Theory]
    [InlineData("https://www.escola.edu.br", "https://www.escola.edu.br/")]
    [InlineData("http://exemplo.com/pagina?a=1", "http://exemplo.com/pagina?a=1")]
    [InlineData("escola.edu.br", "https://escola.edu.br/")]                 // sem esquema vira https
    [InlineData("  https://x.com/  ", "https://x.com/")]                    // espaços são aparados
    [InlineData("www.youtube.com/watch?v=abc", "https://www.youtube.com/watch?v=abc")]
    public void Links_http_https_sao_aceitos_e_normalizados(string entrada, string esperado)
    {
        Assert.Null(Links.Validar(entrada, out var norm));
        Assert.Equal(esperado, norm);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]  // abrir arquivo local: NÃO
    [InlineData("ftp://servidor/arquivo")]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html,<script>")]
    [InlineData("mailto:alguem@x.com")]
    [InlineData("\\\\servidor\\compartilhado")]           // caminho de rede
    [InlineData("C:\\pasta\\programa.exe")]
    [InlineData("http://")]                                // sem host
    public void Links_perigosos_ou_invalidos_sao_recusados(string? entrada)
    {
        Assert.NotNull(Links.Validar(entrada, out _));
        Assert.False(Links.EhValido(entrada));
    }

    [Fact]
    public void Link_longo_demais_e_recusado() =>
        Assert.NotNull(Links.Validar("https://x.com/" + new string('a', Links.TamanhoMaximo), out _));
}

public class ProtocoloComandoTestes
{
    static (TipoMensagem Tipo, byte[] Corpo) Ler(byte[] msg)
    {
        using var s = new MemoryStream(msg);
        byte[] buf = new byte[16];
        var r = Protocolo.Ler(s, ref buf)!.Value;
        return (r.Tipo, buf[..r.Tamanho]);
    }

    [Theory]
    [InlineData(AcaoComando.AbrirLink, "https://escola.edu.br/")]
    [InlineData(AcaoComando.Mensagem, "Guardem o celular — ç ã")]
    [InlineData(AcaoComando.BloquearTela, "")]
    [InlineData(AcaoComando.Desligar, "")]
    public void Comando_ida_e_volta(AcaoComando acao, string arg)
    {
        var (tipo, corpo) = Ler(Protocolo.MontarComando(42, acao, arg));
        Assert.Equal(TipoMensagem.Comando, tipo);
        Assert.Equal(new Comando(42, acao, arg), Protocolo.LerComando(corpo));
    }

    [Fact]
    public void Comando_truncado_e_recusado()
    {
        var (_, corpo) = Ler(Protocolo.MontarComando(1, AcaoComando.AbrirLink, "x"));
        Assert.Null(Protocolo.LerComando(corpo[..^1]));
        Assert.Null(Protocolo.LerComando([]));
        Assert.Null(Protocolo.LerComando([.. corpo, 0x41])); // byte sobrando
    }

    [Theory]
    [InlineData(true, "aberto")]
    [InlineData(false, "não foi possível: ç")]
    public void Resposta_ida_e_volta(bool ok, string msg)
    {
        var (tipo, corpo) = Ler(Protocolo.MontarComandoResposta(7, ok, msg));
        Assert.Equal(TipoMensagem.ComandoResposta, tipo);
        Assert.Equal(new RespostaComando(7, ok, msg), Protocolo.LerComandoResposta(corpo));
    }
}

public class ProgramasTestes
{
    [Theory]
    [InlineData("calc.exe")]
    [InlineData("notepad")]
    [InlineData("C:\\Program Files\\App\\programa.exe")]
    [InlineData("mspaint.exe")]
    public void Programas_validos_sao_aceitos(string comando) => Assert.Null(Programas.ValidarComando(comando));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("cmd")]
    [InlineData("cmd.exe")]
    [InlineData("powershell.exe")]
    [InlineData("PowerShell")]
    [InlineData("wscript.exe")]
    [InlineData("mshta")]
    [InlineData("rundll32.exe")]
    [InlineData("script.bat")]
    [InlineData("macro.vbs")]
    [InlineData("app.exe\" & calc")]       // tentativa de injeção com aspas
    [InlineData("a|b.exe")]
    public void Programas_perigosos_ou_invalidos_sao_recusados(string? comando) => Assert.NotNull(Programas.ValidarComando(comando));

    [Fact]
    public void Nome_do_processo_vem_do_comando()
    {
        Assert.Equal("winword", new ProgramaPermitido("Word", "C:\\Office\\winword.exe").NomeProcesso);
        Assert.Equal("calc", new ProgramaPermitido("Calculadora", "calc.exe").NomeProcesso);
    }

    [Theory]
    [InlineData("Calculadora")]
    [InlineData("Word da escola")]
    public void Nomes_amigaveis_validos(string nome) => Assert.Null(Programas.ValidarNome(nome));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nome_amigavel_vazio_recusado(string nome) => Assert.NotNull(Programas.ValidarNome(nome));

    [Fact]
    public void Lista_padrao_so_tem_programas_validos()
    {
        Assert.NotEmpty(Programas.Padrao);
        Assert.All(Programas.Padrao, p => Assert.Null(Programas.ValidarComando(p.Comando)));
    }

    [Fact]
    public void Salvar_e_carregar_a_lista()
    {
        string arq = Path.Combine(Path.GetTempPath(), "controlesala-prog-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var lista = new List<ProgramaPermitido> { new("Calculadora", "calc.exe"), new("Word", "winword.exe") };
            Programas.Salvar(lista, arq);
            var volta = Programas.Carregar(arq);
            Assert.Equal(lista, volta);
        }
        finally { File.Delete(arq); }
    }

    [Fact]
    public void Arquivo_inexistente_devolve_a_lista_padrao() =>
        Assert.Equal(Programas.Padrao, Programas.Carregar(Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid().ToString("N") + ".json")));
}

public class WakeOnLanTestes
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aa-bb-cc-dd-ee-ff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aabbccddeeff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("AABB.CCDD.EEFF", "AA:BB:CC:DD:EE:FF")]
    [InlineData(" 00:11:22:33:44:55 ", "00:11:22:33:44:55")]
    public void Normaliza_formatos_de_mac(string entrada, string esperado) =>
        Assert.Equal(esperado, WakeOnLan.Normalizar(entrada));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("AA:BB:CC:DD:EE")]        // curto
    [InlineData("AA:BB:CC:DD:EE:FF:00")]  // longo
    [InlineData("GG:BB:CC:DD:EE:FF")]     // caractere inválido
    [InlineData("não é mac")]
    public void Macs_invalidos(string? entrada)
    {
        Assert.Null(WakeOnLan.Normalizar(entrada));
        Assert.False(WakeOnLan.EhValido(entrada));
    }

    [Fact]
    public void Pacote_magico_tem_102_bytes_e_estrutura_certa()
    {
        var p = WakeOnLan.MontarPacote("AA:BB:CC:DD:EE:FF");
        Assert.Equal(102, p.Length);
        Assert.All(p[..6], b => Assert.Equal(0xFF, b));
        byte[] mac = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];
        for (int r = 0; r < 16; r++)
            Assert.Equal(mac, p.Skip(6 + r * 6).Take(6));
    }

    [Fact]
    public void Montar_pacote_com_mac_invalido_lanca() =>
        Assert.Throws<ArgumentException>(() => WakeOnLan.MontarPacote("xyz"));
}

public class ProtocoloMacTestes
{
    [Fact]
    public void EnderecoMac_ida_e_volta()
    {
        var msg = Protocolo.MontarEnderecoMac("AA:BB:CC:DD:EE:FF");
        using var s = new MemoryStream(msg);
        byte[] buf = new byte[64];
        var r = Protocolo.Ler(s, ref buf)!.Value;
        Assert.Equal(TipoMensagem.EnderecoMac, r.Tipo);
        Assert.Equal("AA:BB:CC:DD:EE:FF", Protocolo.LerEnderecoMac(buf.AsSpan(0, r.Tamanho)));
    }
}
