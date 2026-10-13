using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace ControleSala.Comum;

public readonly record struct ResultadoArquivo(bool Ok, string Mensagem, string? Caminho = null);

/// <summary>Onde os alunos encontram o que o professor enviou.</summary>
public static class PastaRecebidos
{
    public const string Nome = "Arquivos do Professor";

    /// <summary>Dentro da Área de Trabalho Pública: aparece na área de trabalho de todos os usuários do PC.</summary>
    public static string Padrao => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), Nome);

    /// <summary>Cria a pasta. Com <paramref name="liberarParaAlunos"/>, qualquer usuário pode abrir, alterar e apagar o que está nela.</summary>
    public static void Garantir(string pasta, bool liberarParaAlunos)
    {
        Directory.CreateDirectory(pasta);
        if (!liberarParaAlunos) return;
        var info = new DirectoryInfo(pasta);
        var regras = info.GetAccessControl();
        foreach (var (sid, direitos) in new[]
        {
            (WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl),
            (WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl),
            (WellKnownSidType.BuiltinUsersSid, FileSystemRights.Modify),
        })
            regras.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), direitos,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(regras);
    }
}

/// <summary>
/// Recebe um arquivo por vez (o agente tem um recebedor por professor conectado). Grava num arquivo temporário escondido,
/// confere tamanho e SHA-256 e só então troca para o nome final: o aluno nunca vê um arquivo pela metade.
/// Qualquer erro cancela o envio e apaga o temporário.
/// </summary>
public sealed class RecebedorArquivos : IDisposable
{
    public const long TamanhoMaximo = 2L * 1024 * 1024 * 1024;
    /// <summary>Espaço livre que sempre deve sobrar no disco depois do arquivo.</summary>
    public const long Reserva = 512L * 1024 * 1024;

    readonly string _pasta;
    readonly bool _conferirTipo;
    readonly Func<string, long> _espacoLivre;
    readonly TimeProvider _tempo;

    FileStream? _arquivo;
    IncrementalHash? _hash;
    uint _id;
    string _temp = "", _final = "";
    long _tamanho, _recebido;
    DateTimeOffset _ultimaAtividade;

    /// <param name="conferirTipo">
    /// Só aceita os tipos de <see cref="NomesDeArquivo"/>. Desligado só para a atualização do agente, em que o nome é
    /// escolhido pelo próprio agente e o instalador só roda depois de conferida a assinatura do autor.
    /// </param>
    public RecebedorArquivos(string pasta, Func<string, long>? espacoLivre = null, TimeProvider? tempo = null, bool conferirTipo = true)
    {
        _pasta = pasta;
        _conferirTipo = conferirTipo;
        _espacoLivre = espacoLivre ?? (p => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(p))!).AvailableFreeSpace);
        _tempo = tempo ?? TimeProvider.System;
    }

    public bool Ativo => _arquivo != null;
    public string? NomeAtivo => Ativo ? Path.GetFileName(_final) : null;

    public ResultadoArquivo Iniciar(uint id, string nome, long tamanho)
    {
        if (Ativo) return Erro("Já existe um envio em andamento para este computador.");
        if (_conferirTipo && NomesDeArquivo.Validar(nome) is { } problema) return Erro(problema);
        if (tamanho < 0) return Erro("Tamanho de arquivo inválido.");
        if (tamanho > TamanhoMaximo) return Erro($"O arquivo é grande demais (máximo {TamanhoMaximo / (1024 * 1024 * 1024)} GB).");

        string final = Path.GetFullPath(Path.Combine(_pasta, nome));
        string raiz = Path.GetFullPath(_pasta).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!final.StartsWith(raiz, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(final) != nome)
            return Erro("O nome do arquivo aponta para fora da pasta de recebidos.");

        try
        {
            Directory.CreateDirectory(_pasta);
            if (_espacoLivre(_pasta) < tamanho + Reserva) return Erro("Não há espaço livre suficiente neste computador.");
            string temp = Path.Combine(_pasta, $".{id}-{Guid.NewGuid():N}.parte");
            _arquivo = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920);
            File.SetAttributes(temp, FileAttributes.Hidden);
            (_temp, _final, _id, _tamanho, _recebido) = (temp, final, id, tamanho, 0);
            _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            _ultimaAtividade = _tempo.GetUtcNow();
            return new ResultadoArquivo(true, "Pronto para receber.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Abortar();
            return Erro($"Não foi possível criar o arquivo neste computador: {ex.Message}");
        }
    }

    public ResultadoArquivo Dados(uint id, ReadOnlySpan<byte> bloco)
    {
        if (!Ativo || id != _id) return Erro("Envio desconhecido ou já encerrado.");
        if (_recebido + bloco.Length > _tamanho)
        {
            Abortar();
            return Erro("Chegaram mais dados do que o tamanho informado.");
        }
        try
        {
            _arquivo!.Write(bloco);
            _hash!.AppendData(bloco);
            _recebido += bloco.Length;
            _ultimaAtividade = _tempo.GetUtcNow();
            return new ResultadoArquivo(true, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Abortar();
            return Erro($"Falha ao gravar no disco: {ex.Message}");
        }
    }

    public ResultadoArquivo Fim(uint id, ReadOnlySpan<byte> sha256)
    {
        if (!Ativo || id != _id) return Erro("Envio desconhecido ou já encerrado.");
        if (_recebido != _tamanho)
        {
            Abortar();
            return Erro($"O arquivo chegou incompleto ({_recebido} de {_tamanho} bytes).");
        }
        byte[] calculado = _hash!.GetHashAndReset();
        if (sha256.Length != calculado.Length || !CryptographicOperations.FixedTimeEquals(calculado, sha256))
        {
            Abortar();
            return Erro("O arquivo chegou diferente do original (a verificação falhou).");
        }
        string nome = Path.GetFileName(_final), temp = _temp, final = _final;
        try
        {
            _arquivo!.Flush(true);
            _arquivo.Dispose();
            _arquivo = null;
            File.SetAttributes(temp, FileAttributes.Normal);
            File.Move(temp, final, overwrite: true);
            Limpar();
            return new ResultadoArquivo(true, $"{nome} recebido.", final);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Abortar();
            return Erro($"Não foi possível salvar {nome} (está aberto no computador do aluno?): {ex.Message}");
        }
    }

    /// <summary>Desiste do envio atual e apaga o temporário.</summary>
    public void Cancelar() => Abortar();

    /// <summary>Passou <paramref name="limite"/> sem chegar nada de um envio em andamento?</summary>
    public bool Parado(TimeSpan limite) => Ativo && _tempo.GetUtcNow() - _ultimaAtividade > limite;

    /// <summary>Apaga temporários antigos que sobraram (agente reiniciado no meio de um envio).</summary>
    public static void LimparRestos(string pasta, TimeSpan idade)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(pasta, ".*.parte"))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > idade)
                    File.Delete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // pasta ainda não existe ou sem acesso: nada a limpar
        }
    }

    static ResultadoArquivo Erro(string mensagem) => new(false, mensagem);

    void Abortar()
    {
        string temp = _temp;
        try { _arquivo?.Dispose(); } catch (IOException) { /* já estava fechado */ }
        _arquivo = null;
        Limpar();
        if (temp.Length > 0)
        {
            try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* fica para a limpeza de restos */ }
        }
    }

    void Limpar()
    {
        _hash?.Dispose();
        _hash = null;
        (_temp, _final, _id, _tamanho, _recebido) = ("", "", 0, 0, 0);
    }

    public void Dispose() => Abortar();
}
