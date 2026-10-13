namespace ControleSala.Comum;

/// <summary>
/// Seleção e remoção dos arquivos da limpeza, dentro do perfil do aluno. É aqui que decidimos o que apagar
/// (respeitando as exceções). O Recycle Bin e o papel de parede ficam no agente (dependem de API do Windows).
/// </summary>
public static class LimpezaArquivos
{
    /// <summary>Pastas do aluno a esvaziar, conforme a configuração (Downloads, Área de Trabalho, Documentos, Temp).</summary>
    public static List<string> PastasParaEsvaziar(string perfil, LimpezaConfig c)
    {
        var lista = new List<string>();
        if (c.Downloads) lista.Add(Path.Combine(perfil, "Downloads"));
        if (c.AreaTrabalho) lista.Add(Path.Combine(perfil, "Desktop"));
        if (c.Documentos) lista.Add(Path.Combine(perfil, "Documents"));
        if (c.Temporarios) lista.Add(Path.Combine(perfil, "AppData", "Local", "Temp"));
        return lista;
    }

    /// <summary>Arquivos/pastas de dados dos navegadores (cache, histórico, cookies) dentro do perfil.</summary>
    public static List<string> CaminhosNavegadores(string perfil)
    {
        var alvos = new List<string>();
        string local = Path.Combine(perfil, "AppData", "Local");
        string roaming = Path.Combine(perfil, "AppData", "Roaming");

        // Chrome e Edge: a pasta "Default" de cada um guarda cache, histórico e cookies.
        foreach (var baseNav in new[]
        {
            Path.Combine(local, "Google", "Chrome", "User Data"),
            Path.Combine(local, "Microsoft", "Edge", "User Data"),
        })
            foreach (var nome in new[] { "Cache", "Code Cache", "GPUCache" })
                alvos.Add(Path.Combine(baseNav, "Default", nome));

        foreach (var baseNav in new[]
        {
            Path.Combine(local, "Google", "Chrome", "User Data", "Default"),
            Path.Combine(local, "Microsoft", "Edge", "User Data", "Default"),
        })
            foreach (var arq in new[] { "History", "Cookies", "Web Data", "Login Data", "Visited Links" })
                alvos.Add(Path.Combine(baseNav, arq));

        // Firefox: cada perfil em Profiles\*
        string ffProfiles = Path.Combine(roaming, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(ffProfiles))
            foreach (var p in SafeDir(ffProfiles))
                foreach (var arq in new[] { "cookies.sqlite", "places.sqlite", "formhistory.sqlite" })
                    alvos.Add(Path.Combine(p, arq));
        string ffCache = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(ffCache))
            foreach (var p in SafeDir(ffCache))
                alvos.Add(Path.Combine(p, "cache2"));

        return alvos;
    }

    /// <summary>Apaga tudo dentro de <paramref name="pasta"/>, menos o que casar com as exceções. Devolve quantos itens removeu.</summary>
    public static int EsvaziarPasta(string pasta, IReadOnlyCollection<string> excecoes)
    {
        if (!Directory.Exists(pasta)) return 0;
        int removidos = 0;
        foreach (var item in SafeEntries(pasta))
        {
            string nome = Path.GetFileName(item);
            if (nome.Length == 0 || Excecao(nome, excecoes)) continue;
            if (Remover(item)) removidos++;
        }
        return removidos;
    }

    /// <summary>Apaga um arquivo ou uma pasta (com conteúdo). Não lança: devolve false se não deu.</summary>
    public static bool Remover(string caminho)
    {
        try
        {
            if (Directory.Exists(caminho))
            {
                LimparSomenteLeitura(caminho);
                Directory.Delete(caminho, recursive: true);
                return true;
            }
            if (File.Exists(caminho))
            {
                File.SetAttributes(caminho, FileAttributes.Normal);
                File.Delete(caminho);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // arquivo em uso ou protegido: pula
        }
        return false;
    }

    static bool Excecao(string nome, IReadOnlyCollection<string> excecoes) =>
        excecoes.Any(e => string.Equals(e.Trim(), nome, StringComparison.OrdinalIgnoreCase));

    static void LimparSomenteLeitura(string pasta)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(pasta, "*", SearchOption.AllDirectories))
            {
                var attr = File.GetAttributes(f);
                if (attr.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(f, attr & ~FileAttributes.ReadOnly);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static IEnumerable<string> SafeEntries(string pasta)
    {
        try { return Directory.EnumerateFileSystemEntries(pasta); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    static IEnumerable<string> SafeDir(string pasta)
    {
        try { return Directory.EnumerateDirectories(pasta); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}
