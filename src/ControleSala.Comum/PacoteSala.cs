using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace ControleSala.Comum;

/// <summary>A sala: nome da escola, nome da sala e a chave secreta que os agentes e o professor compartilham.</summary>
public sealed record ConfigSala(string Escola, string Sala, byte[] Chave)
{
    public const int TamanhoChave = 32;

    public static ConfigSala Nova(string escola, string sala) =>
        new(escola.Trim(), sala.Trim(), RandomNumberGenerator.GetBytes(TamanhoChave));

    /// <summary>
    /// Resumo curto da chave (ex.: "A1B2-C3D4") para conferir a olho se o professor e o agente são da mesma sala.
    /// Não revela a chave.
    /// </summary>
    public string Impressao()
    {
        byte[] h = HMACSHA256.HashData(Chave, "ControleSala impressao v1"u8.ToArray());
        return $"{Convert.ToHexString(h.AsSpan(0, 2))}-{Convert.ToHexString(h.AsSpan(2, 2))}";
    }
}

public sealed class PacoteInvalidoException(string mensagem) : Exception(mensagem);
public sealed class SenhaPacoteIncorretaException() : Exception("Senha incorreta ou arquivo danificado.");

/// <summary>
/// Arquivo <c>.controlesala</c> que leva a chave da sala do PC do professor para os PCs dos alunos (pendrive).
/// Fica protegido por uma senha escolhida na hora de gerar: PBKDF2-SHA256 + AES-256-GCM.
/// Formato: "CSALA1\n" | sal(16) | voltas(int32) | nonce(12) | texto cifrado | etiqueta(16).
/// </summary>
public static class PacoteSala
{
    public const int SenhaMinima = 8;
    const int TamSal = 16, TamNonce = 12, TamEtiqueta = 16, MaxVoltas = 10_000_000;
    static readonly byte[] Magia = "CSALA1\n"u8.ToArray();
    static int TamCabecalho => Magia.Length + TamSal + 4;

    sealed record Conteudo(string Escola, string Sala, string Chave);

    public static byte[] Exportar(ConfigSala sala, string senha, int voltas = 600_000)
    {
        if (senha is null || senha.Length < SenhaMinima)
            throw new ArgumentException($"A senha do pacote deve ter pelo menos {SenhaMinima} caracteres.");

        var cabecalho = new byte[TamCabecalho];
        Magia.CopyTo(cabecalho, 0);
        RandomNumberGenerator.Fill(cabecalho.AsSpan(Magia.Length, TamSal));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(cabecalho.AsSpan(Magia.Length + TamSal), voltas);

        byte[] texto = JsonSerializer.SerializeToUtf8Bytes(new Conteudo(sala.Escola, sala.Sala, Convert.ToBase64String(sala.Chave)));
        byte[] nonce = RandomNumberGenerator.GetBytes(TamNonce);
        var cifrado = new byte[texto.Length];
        var etiqueta = new byte[TamEtiqueta];
        using (var aes = new AesGcm(ChaveDaSenha(senha, cabecalho.AsSpan(Magia.Length, TamSal), voltas), TamEtiqueta))
            aes.Encrypt(nonce, texto, cifrado, etiqueta, cabecalho);

        return [.. cabecalho, .. nonce, .. cifrado, .. etiqueta];
    }

    public static ConfigSala Importar(byte[] arquivo, string senha)
    {
        if (arquivo.Length < TamCabecalho + TamNonce + TamEtiqueta || !arquivo.AsSpan(0, Magia.Length).SequenceEqual(Magia))
            throw new PacoteInvalidoException("Este arquivo não é um pacote de sala do ControleSala.");

        int voltas = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(arquivo.AsSpan(Magia.Length + TamSal));
        if (voltas is < 1 or > MaxVoltas)
            throw new PacoteInvalidoException("Pacote de sala inválido.");

        var cabecalho = arquivo.AsSpan(0, TamCabecalho);
        var nonce = arquivo.AsSpan(TamCabecalho, TamNonce);
        int n = arquivo.Length - TamCabecalho - TamNonce - TamEtiqueta;
        var claro = new byte[n];
        try
        {
            using var aes = new AesGcm(ChaveDaSenha(senha ?? "", cabecalho.Slice(Magia.Length, TamSal), voltas), TamEtiqueta);
            aes.Decrypt(nonce, arquivo.AsSpan(TamCabecalho + TamNonce, n), arquivo.AsSpan(arquivo.Length - TamEtiqueta), claro, cabecalho);
        }
        catch (CryptographicException)
        {
            throw new SenhaPacoteIncorretaException();
        }

        try
        {
            var c = JsonSerializer.Deserialize<Conteudo>(claro) ?? throw new JsonException();
            byte[] chave = Convert.FromBase64String(c.Chave);
            if (chave.Length != ConfigSala.TamanhoChave) throw new JsonException();
            return new ConfigSala(c.Escola, c.Sala, chave);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new PacoteInvalidoException("Pacote de sala danificado.");
        }
    }

    static byte[] ChaveDaSenha(string senha, ReadOnlySpan<byte> sal, int voltas) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(senha), sal, voltas, HashAlgorithmName.SHA256, 32);
}

/// <summary>Sala configurada num PC de aluno (<c>%ProgramData%\ControleSala\agente.json</c>), legível só por administrador e SYSTEM.</summary>
public static class ConfigAgente
{
    sealed record Arquivo(string Escola, string Sala, string Chave);

    public static string CaminhoPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ControleSala", "agente.json");

    public static ConfigSala? Carregar(string? caminho = null)
    {
        string c = caminho ?? CaminhoPadrao;
        if (!File.Exists(c)) return null;
        try
        {
            var a = JsonSerializer.Deserialize<Arquivo>(File.ReadAllText(c));
            byte[]? chave = a == null ? null : Convert.FromBase64String(a.Chave);
            return chave is { Length: ConfigSala.TamanhoChave } ? new ConfigSala(a!.Escola, a.Sala, chave) : null;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or IOException)
        {
            return null;
        }
    }

    /// <param name="restringir">Só administradores e SYSTEM acessam a pasta (use sempre no PC do aluno; desligue só em testes).</param>
    public static void Salvar(ConfigSala sala, string? caminho = null, bool restringir = true)
    {
        string c = caminho ?? CaminhoPadrao;
        string pasta = Path.GetDirectoryName(c)!;
        Directory.CreateDirectory(pasta);
        if (restringir) RestringirPasta(pasta);
        File.WriteAllText(c, JsonSerializer.Serialize(new Arquivo(sala.Escola, sala.Sala, Convert.ToBase64String(sala.Chave))));
    }

    public static void RestringirPasta(string pasta)
    {
        var regras = new DirectorySecurity();
        regras.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
            regras.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(pasta).SetAccessControl(regras);
    }
}

/// <summary>Sala configurada no PC do professor (<c>%AppData%\ControleSala\sala-config.json</c>); a chave fica protegida pelo Windows (DPAPI).</summary>
public static class ConfigProfessor
{
    sealed record Arquivo(string Escola, string Sala, string ChaveProtegida);
    static readonly byte[] Entropia = "ControleSala sala-config v1"u8.ToArray();

    public static string CaminhoPadrao => Path.Combine(Pastas.Dados, "sala-config.json");

    public static ConfigSala? Carregar(string? caminho = null)
    {
        string c = caminho ?? CaminhoPadrao;
        if (!File.Exists(c)) return null;
        try
        {
            var a = JsonSerializer.Deserialize<Arquivo>(File.ReadAllText(c));
            if (a == null) return null;
            byte[] chave = ProtectedData.Unprotect(Convert.FromBase64String(a.ChaveProtegida), Entropia, DataProtectionScope.CurrentUser);
            return chave.Length == ConfigSala.TamanhoChave ? new ConfigSala(a.Escola, a.Sala, chave) : null;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or IOException)
        {
            return null;
        }
    }

    public static void Salvar(ConfigSala sala, string? caminho = null)
    {
        string c = caminho ?? CaminhoPadrao;
        Directory.CreateDirectory(Path.GetDirectoryName(c)!);
        byte[] protegida = ProtectedData.Protect(sala.Chave, Entropia, DataProtectionScope.CurrentUser);
        File.WriteAllText(c, JsonSerializer.Serialize(new Arquivo(sala.Escola, sala.Sala, Convert.ToBase64String(protegida))));
    }
}
