namespace ControleSala.Comum;

/// <summary>
/// Seleção e remoção dos arquivos da limpeza, dentro do perfil do aluno. É aqui que decidimos o que apagar
/// (respeitando as exceções). O papel de parede fica no agente (depende de API do Windows).
/// </summary>
public static partial class LimpezaArquivos
{
    /// <summary>Pastas do aluno a esvaziar, conforme a configuração (Downloads, Área de Trabalho, Documentos, Temp).</summary>
    public static List<string> PastasParaEsvaziar(string perfil, LimpezaConfig c)
    {
        var lista = new List<string>();
        if (c.Downloads) lista.Add(Path.Combine(perfil, "Downloads"));
        if (c.AreaTrabalho) lista.Add(Path.Combine(perfil, "Desktop"));
        if (c.Documentos) lista.Add(Path.Combine(perfil, "Documents"));
        if (c.Temporarios) lista.Add(Path.Combine(perfil, "AppData", "Local", "Temp"));
        return SemAtalhos(perfil, lista);
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

        return SemAtalhos(perfil, alvos);
    }

    /// <summary>
    /// O caminho, abaixo de <paramref name="raiz"/>, passa por um atalho de pasta (junção ou link)? O aluno consegue criar um sem
    /// ser administrador: trocar a pasta Documents por um atalho para o Windows faria a limpeza apagar outra coisa.
    /// </summary>
    public static bool PassaPorAtalho(string raiz, string caminho)
    {
        int tamanhoDaRaiz = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raiz)).Length;
        for (string? atual = Path.GetFullPath(caminho); atual != null && atual.Length > tamanhoDaRaiz; atual = Path.GetDirectoryName(atual))
            if (EhAtalho(atual))
                return true;
        return false;
    }

    static List<string> SemAtalhos(string perfil, List<string> caminhos) => caminhos.Where(c => !PassaPorAtalho(perfil, c)).ToList();

    /// <summary>Sem poder olhar o caminho, conta como atalho: na dúvida, a limpeza não toca.</summary>
    static bool EhAtalho(string caminho)
    {
        try { return (File.GetAttributes(caminho) & FileAttributes.ReparsePoint) != 0; }
        catch (UnauthorizedAccessException) { return true; }
        catch (IOException) { return false; }   // não existe
    }

    /// <summary>
    /// Apaga tudo dentro de <paramref name="pasta"/>, menos o que casar com as exceções. Devolve quantos itens removeu.
    /// Nunca apaga: pasta compartilhada na rede (<paramref name="compartilhadas"/>) nem a pasta que tem uma dentro, e atalho
    /// para pasta de rede (ex.: "Trabalhos" apontando para \\servidor\trabalhos), que faz parte da sala e não é do aluno.
    /// </summary>
    public static int EsvaziarPasta(string pasta, IReadOnlyCollection<string> excecoes, IReadOnlyCollection<string>? compartilhadas = null)
    {
        if (!Directory.Exists(pasta) || EhAtalho(pasta)) return 0;
        compartilhadas ??= [];
        if (compartilhadas.Any(c => MesmoOuDentro(pasta, c))) return 0;
        int removidos = 0;
        foreach (var item in SafeEntries(pasta))
        {
            string nome = Path.GetFileName(item);
            if (nome.Length == 0 || Excecao(nome, excecoes)) continue;
            if (compartilhadas.Any(c => MesmoOuDentro(c, item)) || AtalhoDeRede(item)) continue;
            if (Remover(item)) removidos++;
        }
        return removidos;
    }

    /// <summary><paramref name="caminho"/> é <paramref name="pasta"/> ou fica dentro dela?</summary>
    static bool MesmoOuDentro(string caminho, string pasta)
    {
        string c = Path.TrimEndingDirectorySeparator(caminho), p = Path.TrimEndingDirectorySeparator(pasta);
        return c.Equals(p, StringComparison.OrdinalIgnoreCase)
            || c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Atalho do Windows (.lnk ou .url) que aponta para uma pasta de rede. O .lnk guarda o caminho de rede (\\servidor\pasta)
    /// mesmo quando o atalho foi feito por uma unidade mapeada (Z:), então basta procurar o "\\" dentro dele.
    /// </summary>
    public static bool AtalhoDeRede(string caminho)
    {
        string ext = Path.GetExtension(caminho);
        if (!ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".url", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var info = new FileInfo(caminho);
            if (!info.Exists || info.Length > 1_000_000) return false;
            byte[] bytes = File.ReadAllBytes(caminho);
            foreach (var texto in new[] { System.Text.Encoding.Unicode.GetString(bytes), System.Text.Encoding.Latin1.GetString(bytes) })
                if (CaminhoDeRede().IsMatch(texto)) return true;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // sem conseguir ler: na dúvida, o atalho fica (é pequeno e não é arquivo do aluno)
        }
    }

    // \\servidor\pasta (não pega \\?\ nem \\.\) ou file://servidor/pasta
    [System.Text.RegularExpressions.GeneratedRegex(@"\\\\[A-Za-z0-9][A-Za-z0-9._$-]*\\|file://[A-Za-z0-9][A-Za-z0-9._-]*/", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex CaminhoDeRede();

    /// <summary>
    /// As pastas deste PC compartilhadas na rede (as que aparecem para os outros PCs), lidas do registro do Windows.
    /// As do próprio Windows (C$, ADMIN$, IPC$) não entram.
    /// </summary>
    public static List<string> PastasCompartilhadas()
    {
        try
        {
            using var chave = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\LanmanServer\Shares");
            if (chave is null) return [];
            return PastasCompartilhadas(chave.GetValueNames().Select(n => chave.GetValue(n) as string[] ?? []));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    /// <summary>Tira o "Path=" de cada compartilhamento (cada um é uma lista de linhas "Nome=valor").</summary>
    public static List<string> PastasCompartilhadas(IEnumerable<string[]> compartilhamentos) =>
        compartilhamentos.Select(linhas => linhas.FirstOrDefault(l => l.StartsWith("Path=", StringComparison.OrdinalIgnoreCase)))
            .OfType<string>().Select(l => l[5..].Trim()).Where(c => c.Length > 3) // "C:\" inteiro não é pasta de aluno
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>A Lixeira de uma conta, uma pasta por disco: &lt;disco&gt;\$Recycle.Bin\&lt;SID&gt;. As outras contas não entram.</summary>
    public static List<string> PastasDaLixeira(IEnumerable<string> discos, string sid) =>
        discos.Select(disco => Path.Combine(disco, "$Recycle.Bin", sid)).Where(Directory.Exists).ToList();

    /// <summary>Quantos bytes um arquivo ou uma pasta ocupam agora. O que não dá para ler não conta, e atalhos de pasta não são seguidos.</summary>
    public static long Tamanho(string caminho)
    {
        try
        {
            if (File.Exists(caminho)) return new FileInfo(caminho).Length;
            if (!Directory.Exists(caminho)) return 0;
            var opcoes = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            return new DirectoryInfo(caminho).EnumerateFiles("*", opcoes).Sum(a => a.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Apaga um arquivo ou uma pasta (com conteúdo). Não lança: devolve false se não deu.</summary>
    public static bool Remover(string caminho)
    {
        try
        {
            if (Directory.Exists(caminho))
            {
                if (!EhAtalho(caminho)) LimparSomenteLeitura(caminho);
                Directory.Delete(caminho, recursive: true);   // de um atalho, apaga só o atalho, nunca o destino
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

    /// <summary>
    /// O nome casa com uma exceção, com ou sem a extensão: o Windows esconde ".lnk" e, por padrão, ".docx", ".pdf"..., então
    /// o professor escreve "Trabalhos" para o atalho "Trabalhos.lnk" ou "prova" para "prova.docx".
    /// </summary>
    static bool Excecao(string nome, IReadOnlyCollection<string> excecoes)
    {
        string semExtensao = Path.GetFileNameWithoutExtension(nome);
        return excecoes.Select(e => e.Trim()).Where(e => e.Length > 0).Any(e =>
            string.Equals(e, nome, StringComparison.OrdinalIgnoreCase) || string.Equals(e, semExtensao, StringComparison.OrdinalIgnoreCase));
    }

    static void LimparSomenteLeitura(string pasta)
    {
        try
        {
            var opcoes = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var f in Directory.EnumerateFiles(pasta, "*", opcoes))
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
