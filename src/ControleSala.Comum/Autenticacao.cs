using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ControleSala.Comum;

public sealed class AutenticacaoException(string mensagem) : Exception(mensagem);

/// <summary>
/// Aperto de mão entre agente e professor. Só quem tem a chave da sala consegue abrir o canal; depois tudo que passa
/// (vídeo e comandos) vai criptografado.
///
/// 1. Agente → [1][nA: 16 bytes aleatórios]            (ou [0] se este agente não usa chave: só para teste local)
/// 2. Professor → [nB: 16 aleatórios][HMAC(chave, "professor"|nA|nB)]
/// 3. Agente confere, responde [HMAC(chave, "agente"|nA|nB)]  → o professor confere que o agente também tem a chave
/// 4. As duas pontas derivam duas chaves AES-256-GCM (uma para cada sentido) com HKDF(chave, salt = nA|nB).
/// </summary>
public static class Handshake
{
    const int TamNonce = 16, TamProva = 32;
    const byte SemChave = 0, ComChave = 1;

    /// <summary>Lado do agente. Sem chave devolve o próprio fluxo (conexão de teste, sem proteção).</summary>
    public static Stream Agente(Stream fluxo, byte[]? chave)
    {
        if (chave == null)
        {
            fluxo.WriteByte(SemChave);
            fluxo.Flush();
            return fluxo;
        }

        byte[] nA = RandomNumberGenerator.GetBytes(TamNonce);
        fluxo.Write([ComChave, .. nA]);
        fluxo.Flush();

        byte[] resposta = LerExato(fluxo, TamNonce + TamProva);
        byte[] nB = resposta[..TamNonce];
        if (!CryptographicOperations.FixedTimeEquals(resposta.AsSpan(TamNonce), Prova(chave, "professor", nA, nB)))
            throw new AutenticacaoException("A chave da sala apresentada não confere.");

        fluxo.Write(Prova(chave, "agente", nA, nB));
        fluxo.Flush();
        return new CanalCifrado(fluxo, Derivar(chave, nA, nB, "agente->professor"), Derivar(chave, nA, nB, "professor->agente"));
    }

    /// <summary>Lado do professor. <paramref name="aceitaSemChave"/>: aceitar agente de teste sem proteção (só em 127.0.0.1).</summary>
    public static Stream Professor(Stream fluxo, byte[]? chave, bool aceitaSemChave)
    {
        int modo = fluxo.ReadByte();
        if (modo < 0) throw new AutenticacaoException("O agente encerrou a conexão.");
        if (modo == SemChave)
        {
            if (!aceitaSemChave)
                throw new AutenticacaoException("O agente desse computador não está configurado com a chave da sala.");
            return fluxo;
        }
        if (modo != ComChave) throw new AutenticacaoException("O agente respondeu algo inesperado (versão diferente?).");
        if (chave == null)
            throw new AutenticacaoException("Este computador exige a chave da sala, que não está configurada neste programa.");

        byte[] nA = LerExato(fluxo, TamNonce);
        byte[] nB = RandomNumberGenerator.GetBytes(TamNonce);
        fluxo.Write([.. nB, .. Prova(chave, "professor", nA, nB)]);
        fluxo.Flush();

        byte[] provaAgente;
        try
        {
            provaAgente = LerExato(fluxo, TamProva);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            throw new AutenticacaoException("O agente recusou a chave da sala (é de outra sala?).");
        }
        if (!CryptographicOperations.FixedTimeEquals(provaAgente, Prova(chave, "agente", nA, nB)))
            throw new AutenticacaoException("O agente não conhece a chave desta sala.");
        return new CanalCifrado(fluxo, Derivar(chave, nA, nB, "professor->agente"), Derivar(chave, nA, nB, "agente->professor"));
    }

    static byte[] Prova(byte[] chave, string papel, byte[] nA, byte[] nB) =>
        HMACSHA256.HashData(chave, (byte[])[.. Encoding.ASCII.GetBytes(papel), .. nA, .. nB]);

    static byte[] Derivar(byte[] chave, byte[] nA, byte[] nB, string sentido) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, chave, 32, salt: [.. nA, .. nB], info: Encoding.ASCII.GetBytes("ControleSala v1 " + sentido));

    static byte[] LerExato(Stream fluxo, int n)
    {
        var b = new byte[n];
        fluxo.ReadExactly(b);
        return b;
    }
}

/// <summary>
/// Fluxo criptografado por cima de outro fluxo: cada escrita vira um ou mais registros
/// <c>[tamanho: int32][texto cifrado][etiqueta GCM de 16 bytes]</c>, com AES-256-GCM e contador como nonce.
/// Registro alterado, repetido, fora de ordem ou cortado faz a leitura falhar. Uma thread escreve e outra lê.
/// </summary>
public sealed class CanalCifrado : Stream
{
    const int Etiqueta = 16, TamNonce = 12, MaxTextoClaro = 256 * 1024, MaxRegistro = MaxTextoClaro + Etiqueta;

    readonly Stream _interno;
    readonly AesGcm _envio, _recebimento;
    readonly SemaphoreSlim _travaEnvio = new(1, 1);
    readonly byte[] _cifrado = new byte[MaxRegistro];
    readonly byte[] _claro = new byte[MaxTextoClaro];
    ulong _contEnvio, _contRecebimento;
    int _pos, _tam;

    public CanalCifrado(Stream interno, byte[] chaveEnvio, byte[] chaveRecebimento)
    {
        _interno = interno;
        _envio = new AesGcm(chaveEnvio, Etiqueta);
        _recebimento = new AesGcm(chaveRecebimento, Etiqueta);
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => _interno.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> origem)
    {
        Span<byte> nonce = stackalloc byte[TamNonce];
        _travaEnvio.Wait();
        try
        {
            while (!origem.IsEmpty)
            {
                int n = Math.Min(origem.Length, MaxTextoClaro);
                var registro = new byte[4 + n + Etiqueta];
                BinaryPrimitives.WriteInt32LittleEndian(registro, n + Etiqueta);
                nonce.Clear();
                BinaryPrimitives.WriteUInt64LittleEndian(nonce, _contEnvio++);
                _envio.Encrypt(nonce, origem[..n], registro.AsSpan(4, n), registro.AsSpan(4 + n, Etiqueta), registro.AsSpan(0, 4));
                _interno.Write(registro);
                origem = origem[n..];
            }
        }
        finally
        {
            _travaEnvio.Release();
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> destino)
    {
        if (destino.IsEmpty) return 0;
        while (_pos == _tam)
            if (!CarregarRegistro())
                return 0; // o outro lado fechou a conexão
        int n = Math.Min(destino.Length, _tam - _pos);
        _claro.AsSpan(_pos, n).CopyTo(destino);
        _pos += n;
        return n;
    }

    bool CarregarRegistro()
    {
        Span<byte> cabecalho = stackalloc byte[4];
        int lidos = 0;
        while (lidos < 4)
        {
            int r = _interno.Read(cabecalho[lidos..]);
            if (r == 0)
            {
                if (lidos == 0) return false;
                throw new EndOfStreamException("Conexão cortada no meio de um registro.");
            }
            lidos += r;
        }

        int tamanho = BinaryPrimitives.ReadInt32LittleEndian(cabecalho);
        if (tamanho < Etiqueta || tamanho > MaxRegistro)
            throw new CryptographicException("Registro com tamanho inválido.");
        _interno.ReadExactly(_cifrado, 0, tamanho);

        int n = tamanho - Etiqueta;
        Span<byte> nonce = stackalloc byte[TamNonce];
        nonce.Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(nonce, _contRecebimento++);
        _recebimento.Decrypt(nonce, _cifrado.AsSpan(0, n), _cifrado.AsSpan(n, Etiqueta), _claro.AsSpan(0, n), cabecalho);
        (_pos, _tam) = (0, n);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _envio.Dispose();
            _recebimento.Dispose();
            _interno.Dispose();
        }
        base.Dispose(disposing);
    }
}
