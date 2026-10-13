using System.Globalization;

namespace ControleSala.Comum;

/// <summary>
/// Regras para os nomes dos arquivos que o professor envia. Valem nas duas pontas: o professor avisa antes de enviar
/// e o agente confere de novo (um professor ou um programa com problema não pode gravar fora da pasta, nem mandar
/// programas para os alunos).
/// </summary>
public static class NomesDeArquivo
{
    public const int TamanhoMaximoNome = 150;

    /// <summary>
    /// Só estes tipos podem ser enviados (lista de permitidos). É mais seguro que bloquear os perigosos um a um:
    /// qualquer tipo que não esteja aqui — inclusive programas e tipos novos — já é recusado por padrão.
    /// São formatos comuns de material escolar: documentos, imagens, áudio, vídeo e arquivos compactados.
    /// </summary>
    public static readonly IReadOnlySet<string> TiposPermitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Documentos e texto
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp",
        ".rtf", ".txt", ".csv", ".md", ".epub",
        // Imagens
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff",
        // Áudio
        ".mp3", ".wav", ".ogg", ".m4a", ".aac", ".flac",
        // Vídeo
        ".mp4", ".m4v", ".avi", ".mkv", ".mov", ".wmv", ".webm",
        // Compactados (o agente nunca abre nem executa; o aluno extrai à mão)
        ".zip", ".rar", ".7z",
    };

    static readonly HashSet<string> Reservados = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "CLOCK$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Mensagem de erro (para mostrar ao professor) ou null se o nome pode ser usado.</summary>
    public static string? Validar(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "O nome do arquivo está vazio.";
        if (nome.Length > TamanhoMaximoNome) return $"O nome do arquivo é longo demais (máximo {TamanhoMaximoNome} letras).";
        if (nome.EndsWith('.') || nome.EndsWith(' ') || nome.StartsWith(' '))
            return "O nome do arquivo não pode começar com espaço nem terminar com ponto ou espaço.";
        if (nome.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "O nome do arquivo tem caracteres que o Windows não aceita (como \\ / : * ? \" < > |).";
        foreach (char c in nome)
        {
            var categoria = char.GetUnicodeCategory(c);
            // Controle, formatação (inclui os que invertem a direção do texto e escondem a extensão), separadores e
            // caracteres sem uso. Letras e emojis fora do plano básico (pares substitutos) são aceitos.
            if (categoria is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
                return "O nome do arquivo tem caracteres invisíveis.";
        }
        // O Windows trata "CON.txt" como o dispositivo CON.
        string baseDoNome = nome.Split('.')[0].TrimEnd();
        if (Reservados.Contains(baseDoNome)) return $"\"{baseDoNome}\" é um nome reservado do Windows.";
        string tipo = Path.GetExtension(nome);
        if (tipo.Length == 0)
            return "O arquivo precisa ter uma extensão (por exemplo, .pdf ou .jpg).";
        if (!TiposPermitidos.Contains(tipo))
            return $"Arquivos do tipo {tipo.ToLowerInvariant()} não podem ser enviados (por segurança só passam documentos, imagens, áudio, vídeo e arquivos compactados).";
        return null;
    }
}
