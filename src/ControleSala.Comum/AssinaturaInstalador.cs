using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ControleSala.Comum;

/// <summary>Versão deste programa ("0.3.0"), a mesma do instalador (vem do Directory.Build.props).</summary>
public static class Versoes
{
    public static string Atual { get; } = Formatar(typeof(Versoes).Assembly.GetName().Version);

    static string Formatar(Version? v) => v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    /// <summary><paramref name="versao"/> é mais nova que <paramref name="atual"/>? Versão ilegível nunca é "mais nova".</summary>
    public static bool MaisNova(string versao, string? atual)
    {
        if (!Version.TryParse(versao, out var v)) return false;
        if (atual == null || !Version.TryParse(atual, out var a)) return true; // não sabe a atual (agente antigo): é para atualizar
        return Normalizar(v) > Normalizar(a);
    }

    static Version Normalizar(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}

/// <summary>
/// Assinatura dos instaladores, para a atualização dos PCs dos alunos pela rede. A assinatura vai anexada no FIM do próprio
/// Setup.exe (o Windows e o Inno Setup ignoram bytes depois do programa): o professor lida com um arquivo só.
/// Final do arquivo: [versão UTF-8][uint16 tamanho da versão][assinatura ECDSA P-256, 64 bytes]["CSASSIN1"].
/// A assinatura cobre um prefixo fixo + a versão + o SHA-256 do instalador sem o final. Só quem tem a chave privada
/// (o autor) consegue assinar; o agente confere com a chave pública que vem dentro dele (<see cref="ChaveAtualizacao"/>).
/// </summary>
public static class AssinaturaInstalador
{
    static readonly byte[] Marca = "CSASSIN1"u8.ToArray();
    const int TamanhoAssinatura = 64;
    const int MaxVersao = 32;
    static int TamanhoFinal => 2 + TamanhoAssinatura + Marca.Length;

    static byte[] Mensagem(string versao, byte[] sha256) =>
        [.. "ControleSala instalador v1\n"u8, .. Encoding.UTF8.GetBytes(versao), (byte)'\n', .. sha256];

    /// <summary>Anexa a assinatura no fim do instalador. Lança se a versão for inválida ou se já estiver assinado.</summary>
    public static void Assinar(string arquivo, string versao, ECDsa chavePrivada)
    {
        if (!Version.TryParse(versao, out _) || Encoding.UTF8.GetByteCount(versao) > MaxVersao)
            throw new ArgumentException($"Versão inválida: \"{versao}\".");
        byte[] hash;
        using (var f = File.OpenRead(arquivo))
        {
            if (LerFinal(f) != null) throw new InvalidOperationException("Este instalador já está assinado.");
            f.Position = 0;
            hash = SHA256.HashData(f);
        }
        byte[] assinatura = chavePrivada.SignData(Mensagem(versao, hash), HashAlgorithmName.SHA256);
        byte[] v = Encoding.UTF8.GetBytes(versao);
        var tamanho = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(tamanho, (ushort)v.Length);
        using var s = new FileStream(arquivo, FileMode.Append, FileAccess.Write);
        s.Write([.. v, .. tamanho, .. assinatura, .. Marca]);
    }

    /// <summary>
    /// Confere a assinatura. Devolve a versão do instalador se a assinatura for do autor e o arquivo estiver intacto;
    /// null se não tiver assinatura, se tiver sido alterado ou se for de outra chave.
    /// </summary>
    public static string? Verificar(string arquivo, ECDsa? chavePublica = null)
    {
        try
        {
            using var s = File.OpenRead(arquivo);
            if (LerFinal(s) is not { } f) return null;

            s.Position = 0;
            using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var bloco = new byte[1 << 16];
            long falta = f.TamanhoInstalador;
            while (falta > 0)
            {
                int n = s.Read(bloco, 0, (int)Math.Min(bloco.Length, falta));
                if (n == 0) return null;
                h.AppendData(bloco, 0, n);
                falta -= n;
            }

            using var padrao = chavePublica == null ? ChaveAtualizacao.Publica() : null;
            var chave = chavePublica ?? padrao!;
            return chave.VerifyData(Mensagem(f.Versao, h.GetHashAndReset()), f.Assinatura, HashAlgorithmName.SHA256) ? f.Versao : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Versão anunciada no final do arquivo, SEM conferir a assinatura (só para mostrar). null = não assinado.</summary>
    public static string? VersaoAnunciada(string arquivo)
    {
        try
        {
            using var s = File.OpenRead(arquivo);
            return LerFinal(s)?.Versao;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    sealed record Final(string Versao, byte[] Assinatura, long TamanhoInstalador);

    static Final? LerFinal(Stream s)
    {
        long total = s.Length;
        if (total < TamanhoFinal + 1) return null;
        var fim = new byte[TamanhoFinal];
        s.Position = total - fim.Length;
        s.ReadExactly(fim);
        if (!fim.AsSpan(fim.Length - Marca.Length).SequenceEqual(Marca)) return null;

        int nv = BinaryPrimitives.ReadUInt16LittleEndian(fim);
        long tamanho = total - fim.Length - nv;
        if (nv is 0 or > MaxVersao || tamanho <= 0) return null;
        var v = new byte[nv];
        s.Position = tamanho;
        s.ReadExactly(v);
        string versao = Encoding.UTF8.GetString(v);
        return Version.TryParse(versao, out _) ? new Final(versao, fim[2..(2 + TamanhoAssinatura)], tamanho) : null;
    }
}

/// <summary>
/// Chave PÚBLICA do autor para conferir os instaladores. A privada fica só no PC do autor (protegida pelo Windows),
/// gerada e usada pelo ControleSala.Assinador; ela nunca entra no programa nem no instalador.
/// </summary>
public static class ChaveAtualizacao
{
    /// <summary>SubjectPublicKeyInfo (ECDSA P-256) em Base64. Trocar = só instaladores da chave nova serão aceitos.</summary>
    public const string PublicaBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEq0KJxoa9uPPfoc7gac47YQWfu0czYnumxM833B1Z7M6+Bmsuk/4uT1YtOzf/4g9clH/buguC+gZJlL0Hqkrpkg==";

    public static ECDsa Publica()
    {
        var chave = ECDsa.Create();
        if (PublicaBase64.Length == 0)
            throw new CryptographicException("Este programa foi compilado sem a chave de atualização.");
        chave.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicaBase64), out _);
        return chave;
    }
}
