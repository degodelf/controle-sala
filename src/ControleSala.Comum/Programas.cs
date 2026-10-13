using System.Text.Json;

namespace ControleSala.Comum;

/// <summary>Um programa da lista do professor: um nome amigável e o que abrir no PC do aluno.</summary>
public sealed record ProgramaPermitido(string Nome, string Comando)
{
    /// <summary>Nome do processo (sem caminho nem .exe), usado para fechar o programa.</summary>
    public string NomeProcesso => Path.GetFileNameWithoutExtension(Comando);
}

/// <summary>
/// Regras dos programas que o professor pode abrir no PC do aluno. O professor monta a lista uma vez (nome amigável +
/// programa); no dia a dia só escolhe o nome. Interpretadores que rodam código solto (cmd, powershell...) são proibidos.
/// </summary>
public static class Programas
{
    /// <summary>Interpretadores e ferramentas que executam código arbitrário: nunca entram na lista.</summary>
    public static readonly IReadOnlySet<string> Proibidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "rundll32", "regsvr32",
        "wmic", "bash", "wsl", "python", "python3", "node", "conhost", "powershell_ise",
    };

    /// <summary>Lista inicial sugerida (programas do próprio Windows, que existem em qualquer PC).</summary>
    public static IReadOnlyList<ProgramaPermitido> Padrao =>
    [
        new("Calculadora", "calc.exe"),
        new("Bloco de Notas", "notepad.exe"),
        new("Paint", "mspaint.exe"),
        new("Explorador de Arquivos", "explorer.exe"),
    ];

    /// <summary>Mensagem de erro, ou null se o programa pode entrar na lista / ser aberto.</summary>
    public static string? ValidarComando(string? comando)
    {
        comando = comando?.Trim() ?? "";
        if (comando.Length == 0) return "Informe o programa a abrir.";
        if (comando.Length > 400) return "O caminho do programa é longo demais.";
        if (comando.IndexOfAny(['"', '<', '>', '|', '\n', '\r', '\t']) >= 0)
            return "O caminho do programa tem caracteres inválidos.";

        string tipo = Path.GetExtension(comando);
        if (tipo.Length != 0 && !tipo.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return "Só é possível abrir programas (.exe).";

        string nomeProcesso = Path.GetFileNameWithoutExtension(comando);
        if (nomeProcesso.Length == 0) return "Informe o nome do programa.";
        if (Proibidos.Contains(nomeProcesso))
            return $"\"{nomeProcesso}\" não pode ser aberto (executa comandos e seria um risco).";
        return null;
    }

    public static string? ValidarNome(string? nome)
    {
        nome = nome?.Trim() ?? "";
        if (nome.Length == 0) return "Dê um nome ao programa (ex.: Calculadora).";
        if (nome.Length > 40) return "O nome é longo demais (máximo 40 letras).";
        return null;
    }

    /// <summary>Lista de programas do professor (<c>programas.json</c>), com a lista inicial na primeira vez.</summary>
    public static List<ProgramaPermitido> Carregar(string? caminho = null)
    {
        string c = caminho ?? Caminho;
        try
        {
            if (File.Exists(c))
                return JsonSerializer.Deserialize<List<ProgramaPermitido>>(File.ReadAllText(c)) ?? [.. Padrao];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // arquivo ruim: volta para a lista inicial
        }
        return [.. Padrao];
    }

    public static void Salvar(IEnumerable<ProgramaPermitido> lista, string? caminho = null)
    {
        string c = caminho ?? Caminho;
        Directory.CreateDirectory(Path.GetDirectoryName(c)!);
        File.WriteAllText(c, JsonSerializer.Serialize(lista, new JsonSerializerOptions { WriteIndented = true }));
    }

    static string Caminho => Path.Combine(Pastas.Dados, "programas.json");
}
