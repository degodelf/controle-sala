using System.Buffers.Binary;
using System.Text;

namespace ControleSala.Comum;

/// <summary>
/// Protocolo da prova de conceito (v0), sobre TCP.
/// Cada mensagem: [int32 tamanho][byte tipo][corpo], little-endian; "tamanho" conta o tipo + corpo.
/// ATENÇÃO: ainda sem autenticação/criptografia — isso entra antes de qualquer instalação na sala.
/// </summary>
public enum TipoMensagem : byte
{
    /// <summary>Agente → professor: [int32 largura][int32 altura][string encoder][string máquina]</summary>
    Ola = 1,
    /// <summary>Agente → professor: [int64 pts][byte flags][int32 µs no agente][H.264]</summary>
    Video = 2,
    /// <summary>Professor → agente: pede um quadro-chave (IDR) para (re)começar a decodificar.</summary>
    PedirQuadroChave = 10,
    /// <summary>Professor → agente: [byte modo] escolhe o que quer receber. Sem isso o agente não envia vídeo.</summary>
    DefinirModo = 11,
    /// <summary>Professor → agente: [string UTF-8] nome do professor que está vendo a tela (mostrado ao aluno).</summary>
    Identificacao = 13,
    /// <summary>Professor → agente: [uint32 id][int64 tamanho][uint16 n][nome UTF-8] começa o envio de um arquivo.</summary>
    ArquivoInicio = 20,
    /// <summary>Professor → agente: [uint32 id][bytes do arquivo...] (blocos de até 64 KB).</summary>
    ArquivoDados = 21,
    /// <summary>Professor → agente: [uint32 id][SHA-256 do arquivo inteiro: 32 bytes] termina o envio.</summary>
    ArquivoFim = 22,
    /// <summary>Professor → agente: [uint32 id] desiste do envio.</summary>
    ArquivoCancelar = 23,
    /// <summary>Agente → professor: [uint32 id][byte estado][uint16 n][mensagem UTF-8].</summary>
    ArquivoResposta = 24,
    /// <summary>Professor → agente: [uint32 id][byte ação][uint16 n][argumento UTF-8] executa um comando no PC do aluno.</summary>
    Comando = 30,
    /// <summary>Agente → professor: [uint32 id][byte ok][uint16 n][mensagem UTF-8] resultado de um comando.</summary>
    ComandoResposta = 31,
    /// <summary>Agente → professor: [string MAC] endereço de rede da placa, para o professor poder ligar o PC depois.</summary>
    EnderecoMac = 32,
    /// <summary>Professor → agente: [string JSON] guarda a configuração de limpeza e (re)agenda a limpeza automática.</summary>
    ConfigLimpeza = 33,
    /// <summary>Professor → agente: [byte qual][bytes] imagem de configuração (0 = papel de parede, 1 = tela de limpeza).</summary>
    ArquivoConfig = 34,
    /// <summary>
    /// Professor → agente: [uint32 id][int64 tamanho][uint16 n][versão UTF-8] começa o envio de um instalador assinado
    /// (atualização). Os dados, o fim, o cancelamento e as respostas usam as mesmas mensagens do envio de arquivo.
    /// </summary>
    AtualizacaoInicio = 35,
    /// <summary>Agente → professor: [string] versão do agente (ex.: "0.3.0"), enviada ao conectar.</summary>
    VersaoAgente = 36,
}

public enum ArquivoConfigTipo : byte { PapelDeParede = 0, ImagemLimpeza = 1 }

/// <summary>Resposta do agente sobre um envio de arquivo.</summary>
public enum EstadoArquivo : byte { Aceito = 0, Concluido = 1, Erro = 2 }

public readonly record struct RespostaArquivo(uint Id, EstadoArquivo Estado, string Mensagem);
public readonly record struct InicioArquivo(uint Id, long Tamanho, string Nome);

/// <summary>
/// O que o professor manda o PC do aluno fazer. Cada comando novo é só um valor novo aqui, sem mudar o protocolo.
/// Todos exigem o canal autenticado da sala (só o professor com a chave manda).
/// </summary>
public enum AcaoComando : byte
{
    AbrirLink = 1,
    Mensagem = 2,
    BloquearTela = 3,
    DesbloquearTela = 4,
    TransmitirProfessor = 5,
    AbrirPrograma = 6,
    FecharPrograma = 7,
    Silenciar = 8,
    BloquearInternet = 9,
    LiberarInternet = 10,
    Desligar = 11,
    Reiniciar = 12,
    Logoff = 13,
    Limpeza = 14,
}

public readonly record struct Comando(uint Id, AcaoComando Acao, string Argumento);
public readonly record struct RespostaComando(uint Id, bool Ok, string Mensagem);

/// <summary>Miniatura: imagem pequena, poucos quadros por segundo (para a grade). Completo: tela inteira em alto FPS.</summary>
public enum ModoVideo : byte { Miniatura = 0, Completo = 1 }

public readonly record struct Ola(int Largura, int Altura, string Encoder, string Maquina);

public static class Protocolo
{
    public const int PortaPadrao = 47810;
    public const int TamanhoMaximo = 16 * 1024 * 1024;
    public const int CabecalhoVideo = 8 + 1 + 4;
    public const byte FlagChave = 1;

    public static byte[] MontarOla(Ola ola)
    {
        var enc = Encoding.UTF8.GetBytes(ola.Encoder);
        var maq = Encoding.UTF8.GetBytes(ola.Maquina);
        var corpo = new byte[4 + 4 + 2 + enc.Length + 2 + maq.Length];
        var s = corpo.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(s, ola.Largura);
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], ola.Altura);
        BinaryPrimitives.WriteUInt16LittleEndian(s[8..], (ushort)enc.Length);
        enc.CopyTo(s[10..]);
        BinaryPrimitives.WriteUInt16LittleEndian(s[(10 + enc.Length)..], (ushort)maq.Length);
        maq.CopyTo(s[(12 + enc.Length)..]);
        return Montar(TipoMensagem.Ola, corpo);
    }

    public static Ola LerOla(ReadOnlySpan<byte> corpo)
    {
        int largura = BinaryPrimitives.ReadInt32LittleEndian(corpo);
        int altura = BinaryPrimitives.ReadInt32LittleEndian(corpo[4..]);
        int nEnc = BinaryPrimitives.ReadUInt16LittleEndian(corpo[8..]);
        string enc = Encoding.UTF8.GetString(corpo.Slice(10, nEnc));
        int nMaq = BinaryPrimitives.ReadUInt16LittleEndian(corpo[(10 + nEnc)..]);
        string maq = Encoding.UTF8.GetString(corpo.Slice(12 + nEnc, nMaq));
        return new Ola(largura, altura, enc, maq);
    }

    /// <summary>Monta a mensagem de vídeo inteira num só buffer (enviado igual para todos os professores conectados).</summary>
    public static unsafe byte[] MontarVideo(long pts, bool chave, int usAgente, byte* dados, int tamanho)
    {
        var msg = new byte[4 + 1 + CabecalhoVideo + tamanho];
        var s = msg.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(s, 1 + CabecalhoVideo + tamanho);
        s[4] = (byte)TipoMensagem.Video;
        BinaryPrimitives.WriteInt64LittleEndian(s[5..], pts);
        s[13] = chave ? FlagChave : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(s[14..], usAgente);
        new ReadOnlySpan<byte>(dados, tamanho).CopyTo(s[18..]);
        return msg;
    }

    public const int BlocoArquivo = 64 * 1024;

    public static byte[] MontarArquivoInicio(uint id, long tamanho, string nome) =>
        MontarInicio(TipoMensagem.ArquivoInicio, id, tamanho, nome);

    /// <summary>Começo do envio de um instalador para atualizar o agente; <paramref name="versao"/> é só informativa (o agente confere a assinatura).</summary>
    public static byte[] MontarAtualizacaoInicio(uint id, long tamanho, string versao) =>
        MontarInicio(TipoMensagem.AtualizacaoInicio, id, tamanho, versao);

    static byte[] MontarInicio(TipoMensagem tipo, uint id, long tamanho, string nome)
    {
        var n = Encoding.UTF8.GetBytes(nome);
        var corpo = new byte[4 + 8 + 2 + n.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(corpo, id);
        BinaryPrimitives.WriteInt64LittleEndian(corpo.AsSpan(4), tamanho);
        BinaryPrimitives.WriteUInt16LittleEndian(corpo.AsSpan(12), (ushort)n.Length);
        n.CopyTo(corpo, 14);
        return Montar(tipo, corpo);
    }

    /// <summary>Lê um ArquivoInicio; devolve null se a mensagem estiver truncada ou inconsistente.</summary>
    public static InicioArquivo? LerArquivoInicio(ReadOnlySpan<byte> corpo)
    {
        if (corpo.Length < 14) return null;
        int n = BinaryPrimitives.ReadUInt16LittleEndian(corpo[12..]);
        if (corpo.Length != 14 + n) return null;
        return new InicioArquivo(BinaryPrimitives.ReadUInt32LittleEndian(corpo),
            BinaryPrimitives.ReadInt64LittleEndian(corpo[4..]), Encoding.UTF8.GetString(corpo.Slice(14, n)));
    }

    public static byte[] MontarArquivoDados(uint id, ReadOnlySpan<byte> bloco)
    {
        var corpo = new byte[4 + bloco.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(corpo, id);
        bloco.CopyTo(corpo.AsSpan(4));
        return Montar(TipoMensagem.ArquivoDados, corpo);
    }

    public static byte[] MontarArquivoFim(uint id, ReadOnlySpan<byte> sha256)
    {
        var corpo = new byte[4 + sha256.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(corpo, id);
        sha256.CopyTo(corpo.AsSpan(4));
        return Montar(TipoMensagem.ArquivoFim, corpo);
    }

    public static byte[] MontarArquivoCancelar(uint id)
    {
        var corpo = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(corpo, id);
        return Montar(TipoMensagem.ArquivoCancelar, corpo);
    }

    /// <summary>Id no começo do corpo das mensagens de arquivo (ou null se for curta demais).</summary>
    public static uint? LerIdArquivo(ReadOnlySpan<byte> corpo) =>
        corpo.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(corpo) : null;

    public static byte[] MontarArquivoResposta(uint id, EstadoArquivo estado, string mensagem)
    {
        var m = Encoding.UTF8.GetBytes(mensagem.Length > 300 ? mensagem[..300] : mensagem);
        var corpo = new byte[4 + 1 + 2 + m.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(corpo, id);
        corpo[4] = (byte)estado;
        BinaryPrimitives.WriteUInt16LittleEndian(corpo.AsSpan(5), (ushort)m.Length);
        m.CopyTo(corpo, 7);
        return Montar(TipoMensagem.ArquivoResposta, corpo);
    }

    public static RespostaArquivo? LerArquivoResposta(ReadOnlySpan<byte> corpo)
    {
        if (corpo.Length < 7) return null;
        int n = BinaryPrimitives.ReadUInt16LittleEndian(corpo[5..]);
        if (corpo.Length != 7 + n || corpo[4] > (byte)EstadoArquivo.Erro) return null;
        return new RespostaArquivo(BinaryPrimitives.ReadUInt32LittleEndian(corpo), (EstadoArquivo)corpo[4],
            Encoding.UTF8.GetString(corpo.Slice(7, n)));
    }

    public static byte[] MontarComando(uint id, AcaoComando acao, string argumento)
    {
        var a = Encoding.UTF8.GetBytes(argumento);
        var corpo = new byte[4 + 1 + 2 + a.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(corpo, id);
        corpo[4] = (byte)acao;
        BinaryPrimitives.WriteUInt16LittleEndian(corpo.AsSpan(5), (ushort)a.Length);
        a.CopyTo(corpo, 7);
        return Montar(TipoMensagem.Comando, corpo);
    }

    public static Comando? LerComando(ReadOnlySpan<byte> corpo)
    {
        if (corpo.Length < 7) return null;
        int n = BinaryPrimitives.ReadUInt16LittleEndian(corpo[5..]);
        if (corpo.Length != 7 + n) return null;
        return new Comando(BinaryPrimitives.ReadUInt32LittleEndian(corpo), (AcaoComando)corpo[4],
            Encoding.UTF8.GetString(corpo.Slice(7, n)));
    }

    public static byte[] MontarComandoResposta(uint id, bool ok, string mensagem)
    {
        var m = Encoding.UTF8.GetBytes(mensagem.Length > 300 ? mensagem[..300] : mensagem);
        var corpo = new byte[4 + 1 + 2 + m.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(corpo, id);
        corpo[4] = (byte)(ok ? 1 : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(corpo.AsSpan(5), (ushort)m.Length);
        m.CopyTo(corpo, 7);
        return Montar(TipoMensagem.ComandoResposta, corpo);
    }

    public static RespostaComando? LerComandoResposta(ReadOnlySpan<byte> corpo)
    {
        if (corpo.Length < 7) return null;
        int n = BinaryPrimitives.ReadUInt16LittleEndian(corpo[5..]);
        if (corpo.Length != 7 + n) return null;
        return new RespostaComando(BinaryPrimitives.ReadUInt32LittleEndian(corpo), corpo[4] != 0,
            Encoding.UTF8.GetString(corpo.Slice(7, n)));
    }

    public static byte[] MontarEnderecoMac(string mac) =>
        Montar(TipoMensagem.EnderecoMac, Encoding.ASCII.GetBytes(mac.Length > 40 ? mac[..40] : mac));

    public static string LerEnderecoMac(ReadOnlySpan<byte> corpo) =>
        Encoding.ASCII.GetString(corpo.Length > 40 ? corpo[..40] : corpo);

    public static byte[] MontarVersaoAgente(string versao) =>
        Montar(TipoMensagem.VersaoAgente, Encoding.ASCII.GetBytes(versao.Length > 32 ? versao[..32] : versao));

    public static string LerVersaoAgente(ReadOnlySpan<byte> corpo) =>
        new string(Encoding.ASCII.GetString(corpo.Length > 32 ? corpo[..32] : corpo).Where(c => char.IsAsciiDigit(c) || c == '.').ToArray());

    public static byte[] MontarConfigLimpeza(string json) =>
        Montar(TipoMensagem.ConfigLimpeza, Encoding.UTF8.GetBytes(json));

    public static string LerConfigLimpeza(ReadOnlySpan<byte> corpo) => Encoding.UTF8.GetString(corpo);

    public static byte[] MontarArquivoConfig(ArquivoConfigTipo tipo, ReadOnlySpan<byte> conteudo)
    {
        var corpo = new byte[1 + conteudo.Length];
        corpo[0] = (byte)tipo;
        conteudo.CopyTo(corpo.AsSpan(1));
        return Montar(TipoMensagem.ArquivoConfig, corpo);
    }

    public static (ArquivoConfigTipo Tipo, byte[] Conteudo)? LerArquivoConfig(ReadOnlySpan<byte> corpo)
    {
        if (corpo.Length < 1 || corpo[0] > (byte)ArquivoConfigTipo.ImagemLimpeza) return null;
        return ((ArquivoConfigTipo)corpo[0], corpo[1..].ToArray());
    }

    public static byte[] MontarPedidoChave() => Montar(TipoMensagem.PedirQuadroChave, []);

    public static byte[] MontarIdentificacao(string nome) =>
        Montar(TipoMensagem.Identificacao, Encoding.UTF8.GetBytes(nome.Length > 60 ? nome[..60] : nome));

    public static string LerIdentificacao(ReadOnlySpan<byte> corpo) =>
        new string(Encoding.UTF8.GetString(corpo.Length > 240 ? corpo[..240] : corpo).Where(c => !char.IsControl(c)).ToArray()).Trim();

    public static byte[] MontarDefinirModo(ModoVideo modo) => Montar(TipoMensagem.DefinirModo, [(byte)modo]);

    public static ModoVideo LerModo(ReadOnlySpan<byte> corpo) =>
        corpo.Length >= 1 && corpo[0] == (byte)ModoVideo.Completo ? ModoVideo.Completo : ModoVideo.Miniatura;

    static byte[] Montar(TipoMensagem tipo, ReadOnlySpan<byte> corpo)
    {
        var msg = new byte[4 + 1 + corpo.Length];
        BinaryPrimitives.WriteInt32LittleEndian(msg, 1 + corpo.Length);
        msg[4] = (byte)tipo;
        corpo.CopyTo(msg.AsSpan(5));
        return msg;
    }

    /// <summary>
    /// Lê uma mensagem para dentro de <paramref name="buffer"/> (cresce se precisar).
    /// Retorna o tipo e o tamanho do corpo, ou null se a conexão foi fechada.
    /// </summary>
    public static (TipoMensagem Tipo, int Tamanho)? Ler(Stream stream, ref byte[] buffer)
    {
        Span<byte> cab = stackalloc byte[5];
        try
        {
            stream.ReadExactly(cab);
        }
        catch (EndOfStreamException)
        {
            return null;
        }
        int tamanho = BinaryPrimitives.ReadInt32LittleEndian(cab);
        if (tamanho < 1 || tamanho > TamanhoMaximo)
            throw new InvalidDataException($"Mensagem com tamanho inválido: {tamanho}");
        int corpo = tamanho - 1;
        if (buffer.Length < corpo)
            buffer = new byte[Math.Max(corpo, buffer.Length * 2)];
        stream.ReadExactly(buffer, 0, corpo);
        return ((TipoMensagem)cab[4], corpo);
    }
}
