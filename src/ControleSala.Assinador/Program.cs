using System.Buffers.Binary;
using System.Security.Cryptography;
using ControleSala.Comum;

namespace ControleSala.Assinador;

/// <summary>
/// Ferramenta do autor para assinar os instaladores (atualização dos alunos pela rede).
/// A chave privada fica em %AppData%\ControleSala-Assinatura, protegida pelo Windows (só este usuário deste PC abre).
/// Guarde um backup com senha (comando "exportar") fora do PC: sem a chave, os PCs já instalados só aceitam
/// atualização instalando à mão (pendrive).
/// </summary>
static class Program
{
    static readonly string Pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ControleSala-Assinatura");
    static readonly string ArquivoChave = Path.Combine(Pasta, "chave-privada.dpapi");
    static readonly byte[] Entropia = "ControleSala assinatura v1"u8.ToArray();
    static readonly byte[] MagiaBackup = "CSCHAVE1\n"u8.ToArray();
    const int TamSal = 16, TamNonce = 12, TamEtiqueta = 16, Voltas = 600_000;

    static int Main(string[] args)
    {
        try
        {
            return (args.FirstOrDefault(), args.Length) switch
            {
                ("gerar-chave", 1) => GerarChave(),
                ("chave-publica", 1) => MostrarPublica(),
                ("assinar", 3) => Assinar(args[1], args[2]),
                ("verificar", 2) => Verificar(args[1]),
                ("exportar", 2) => Exportar(args[1]),
                ("importar", 2) => Importar(args[1]),
                _ => Ajuda(),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Erro: {ex.Message}");
            return 1;
        }
    }

    static int Ajuda()
    {
        Console.WriteLine("""
            ControleSala.Assinador — assina os instaladores do ControleSala (só o autor usa)
              gerar-chave                  cria a chave de assinatura deste PC (uma vez só)
              chave-publica                mostra a chave pública (vai no código: ChaveAtualizacao.PublicaBase64)
              assinar <Setup.exe> <versão> anexa a assinatura no fim do instalador
              verificar <Setup.exe>        confere com a chave pública que está no programa
              exportar <arquivo>           backup da chave, protegido por senha (guarde fora do PC)
              importar <arquivo>           restaura o backup neste PC
            Códigos de saída: 0 ok, 1 erro, 2 não há chave neste PC.
            """);
        return 1;
    }

    static ECDsa? CarregarChave()
    {
        if (!File.Exists(ArquivoChave)) return null;
        byte[] pkcs8 = ProtectedData.Unprotect(File.ReadAllBytes(ArquivoChave), Entropia, DataProtectionScope.CurrentUser);
        var chave = ECDsa.Create();
        chave.ImportPkcs8PrivateKey(pkcs8, out _);
        CryptographicOperations.ZeroMemory(pkcs8);
        return chave;
    }

    static void SalvarChave(ECDsa chave)
    {
        Directory.CreateDirectory(Pasta);
        byte[] pkcs8 = chave.ExportPkcs8PrivateKey();
        File.WriteAllBytes(ArquivoChave, ProtectedData.Protect(pkcs8, Entropia, DataProtectionScope.CurrentUser));
        CryptographicOperations.ZeroMemory(pkcs8);
    }

    static int GerarChave()
    {
        if (File.Exists(ArquivoChave))
        {
            Console.Error.WriteLine($"Já existe uma chave neste PC ({ArquivoChave}). Não gerei outra.");
            return 1;
        }
        using var chave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SalvarChave(chave);
        Console.WriteLine("Chave criada. Chave pública (coloque em ChaveAtualizacao.PublicaBase64):");
        Console.WriteLine(Convert.ToBase64String(chave.ExportSubjectPublicKeyInfo()));
        Console.WriteLine("IMPORTANTE: faça o backup com \"exportar <arquivo>\" e guarde fora deste PC.");
        return 0;
    }

    static int MostrarPublica()
    {
        using var chave = CarregarChave();
        if (chave == null) return SemChave();
        Console.WriteLine(Convert.ToBase64String(chave.ExportSubjectPublicKeyInfo()));
        return 0;
    }

    static int Assinar(string setup, string versao)
    {
        using var chave = CarregarChave();
        if (chave == null) return SemChave();
        AssinaturaInstalador.Assinar(setup, versao, chave);
        // confere com a chave pública embutida no programa: pega chave trocada antes de distribuir
        if (AssinaturaInstalador.Verificar(setup) != versao)
        {
            Console.Error.WriteLine("Assinado, mas a chave pública do programa NÃO confere com a chave deste PC.");
            return 1;
        }
        Console.WriteLine($"Assinado: {Path.GetFileName(setup)} (versão {versao}).");
        return 0;
    }

    static int Verificar(string setup)
    {
        string? versao = AssinaturaInstalador.Verificar(setup);
        Console.WriteLine(versao != null ? $"Assinatura válida: versão {versao}." : "Sem assinatura válida.");
        return versao != null ? 0 : 1;
    }

    static int Exportar(string destino)
    {
        using var chave = CarregarChave();
        if (chave == null) return SemChave();
        string senha = LerSenha("Senha do backup (mínimo 10 caracteres): ");
        if (senha.Length < 10) throw new ArgumentException("A senha precisa ter pelo menos 10 caracteres.");
        if (LerSenha("Repita a senha: ") != senha) throw new ArgumentException("As senhas não são iguais.");

        var cabecalho = new byte[MagiaBackup.Length + TamSal + 4];
        MagiaBackup.CopyTo(cabecalho, 0);
        RandomNumberGenerator.Fill(cabecalho.AsSpan(MagiaBackup.Length, TamSal));
        BinaryPrimitives.WriteInt32LittleEndian(cabecalho.AsSpan(MagiaBackup.Length + TamSal), Voltas);
        byte[] pkcs8 = chave.ExportPkcs8PrivateKey();
        byte[] nonce = RandomNumberGenerator.GetBytes(TamNonce);
        var cifrado = new byte[pkcs8.Length];
        var etiqueta = new byte[TamEtiqueta];
        using (var aes = new AesGcm(ChaveDaSenha(senha, cabecalho.AsSpan(MagiaBackup.Length, TamSal), Voltas), TamEtiqueta))
            aes.Encrypt(nonce, pkcs8, cifrado, etiqueta, cabecalho);
        CryptographicOperations.ZeroMemory(pkcs8);
        File.WriteAllBytes(destino, [.. cabecalho, .. nonce, .. cifrado, .. etiqueta]);
        Console.WriteLine($"Backup salvo em {destino}. Guarde o arquivo e a senha em lugares separados.");
        return 0;
    }

    static int Importar(string origem)
    {
        if (File.Exists(ArquivoChave))
        {
            Console.Error.WriteLine("Já existe uma chave neste PC. Apague-a antes de importar outra.");
            return 1;
        }
        byte[] b = File.ReadAllBytes(origem);
        int cab = MagiaBackup.Length + TamSal + 4;
        if (b.Length < cab + TamNonce + TamEtiqueta + 1 || !b.AsSpan(0, MagiaBackup.Length).SequenceEqual(MagiaBackup))
            throw new ArgumentException("Este arquivo não é um backup de chave do ControleSala.");
        int voltas = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(MagiaBackup.Length + TamSal));
        if (voltas is < 1 or > 10_000_000) throw new ArgumentException("Backup inválido.");
        string senha = LerSenha("Senha do backup: ");
        int n = b.Length - cab - TamNonce - TamEtiqueta;
        var pkcs8 = new byte[n];
        try
        {
            using var aes = new AesGcm(ChaveDaSenha(senha, b.AsSpan(MagiaBackup.Length, TamSal), voltas), TamEtiqueta);
            aes.Decrypt(b.AsSpan(cab, TamNonce), b.AsSpan(cab + TamNonce, n), b.AsSpan(b.Length - TamEtiqueta), pkcs8, b.AsSpan(0, cab));
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new ArgumentException("Senha incorreta ou arquivo danificado.");
        }
        using var chave = ECDsa.Create();
        chave.ImportPkcs8PrivateKey(pkcs8, out _);
        CryptographicOperations.ZeroMemory(pkcs8);
        SalvarChave(chave);
        Console.WriteLine("Chave restaurada neste PC.");
        return 0;
    }

    static byte[] ChaveDaSenha(string senha, ReadOnlySpan<byte> sal, int voltas) =>
        Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(senha), sal, voltas, HashAlgorithmName.SHA256, 32);

    /// <summary>Lê a senha sem mostrar na tela (ou da variável CONTROLESALA_SENHA_CHAVE, para automatizar).</summary>
    static string LerSenha(string pergunta)
    {
        if (Environment.GetEnvironmentVariable("CONTROLESALA_SENHA_CHAVE") is { Length: > 0 } env) return env;
        Console.Write(pergunta);
        var senha = new System.Text.StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (senha.Length > 0) senha.Length--; continue; }
            if (!char.IsControl(k.KeyChar)) senha.Append(k.KeyChar);
        }
        Console.WriteLine();
        return senha.ToString();
    }

    static int SemChave()
    {
        Console.Error.WriteLine($"Não há chave de assinatura neste PC ({ArquivoChave}). Use \"gerar-chave\" ou \"importar\".");
        return 2;
    }
}
