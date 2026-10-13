using System.Text.Json;

namespace ControleSala.Comum;

/// <summary>O que a limpeza apaga na conta do aluno, as exceções e quando ela roda. Guardada pelo professor e enviada aos agentes.</summary>
public sealed class LimpezaConfig
{
    public bool Downloads { get; set; } = true;
    public bool AreaTrabalho { get; set; } = true;
    public bool Documentos { get; set; } = true;
    /// <summary>Histórico, cache e cookies dos navegadores (Chrome, Edge, Firefox).</summary>
    public bool Navegadores { get; set; } = true;
    public bool Lixeira { get; set; } = true;
    public bool Temporarios { get; set; } = true;
    /// <summary>Restaurar o papel de parede padrão (fundo + logo da escola) depois de limpar.</summary>
    public bool RestaurarPapelDeParede { get; set; } = true;

    /// <summary>Atualizar o Windows Defender (baixar as definições novas) na limpeza.</summary>
    public bool AtualizarDefender { get; set; }
    /// <summary>Fazer uma verificação (varredura) com o Windows Defender na limpeza.</summary>
    public bool VerificarDefender { get; set; }
    /// <summary>Varredura completa (demora mais); se falso, verificação rápida.</summary>
    public bool DefenderVarreduraCompleta { get; set; }

    /// <summary>Nomes de arquivos/pastas que a limpeza NÃO apaga (ex.: "Trabalhos", "NÃO APAGAR").</summary>
    public List<string> Excecoes { get; set; } = [];

    /// <summary>Limpeza agendada ligada?</summary>
    public bool Agendada { get; set; }
    /// <summary>Dias da semana em que roda (0 = domingo ... 6 = sábado).</summary>
    public List<int> Dias { get; set; } = [];
    /// <summary>Horário "HH:mm" (24h).</summary>
    public string Hora { get; set; } = "18:00";

    public LimpezaConfig Copia() => (LimpezaConfig)MemberwiseClone();

    public static bool HoraValida(string? hora) =>
        TimeOnly.TryParseExact(hora, "HH:mm", out _);

    /// <summary>Resumo da agenda para mostrar ao professor (ex.: "Seg, Qua, Sex às 18:00").</summary>
    public string ResumoAgenda()
    {
        if (!Agendada || Dias.Count == 0) return "Agendamento desligado";
        string[] nomes = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];
        var dias = Dias.Where(d => d is >= 0 and <= 6).OrderBy(d => d).Select(d => nomes[d]);
        return $"{string.Join(", ", dias)} às {Hora}";
    }

    public static LimpezaConfig Carregar(string? caminho = null)
    {
        string c = caminho ?? Caminho;
        try
        {
            if (File.Exists(c))
                return JsonSerializer.Deserialize<LimpezaConfig>(File.ReadAllText(c)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return new();
    }

    public void Salvar(string? caminho = null)
    {
        string c = caminho ?? Caminho;
        Directory.CreateDirectory(Path.GetDirectoryName(c)!);
        File.WriteAllText(c, Serializar());
    }

    public string Serializar() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    public static LimpezaConfig? Desserializar(string json)
    {
        try { return JsonSerializer.Deserialize<LimpezaConfig>(json); }
        catch (JsonException) { return null; }
    }

    static string Caminho => Path.Combine(Pastas.Dados, "limpeza.json");
}
