namespace ControleSala.Comum;

/// <summary>
/// Decide, de forma pura (fácil de testar), se a limpeza agendada deve rodar agora. Usada pelo agente em sessão:
/// ele verifica de tempos em tempos e, quando esta função diz "sim", mostra o aviso ao aluno e faz a limpeza.
/// </summary>
public static class AgendaLimpeza
{
    /// <summary>Janela, em minutos, depois do horário marcado em que ainda vale rodar (ex.: aluno logou um pouco atrasado).</summary>
    public const int GraceMinutos = 60;

    /// <summary>
    /// Deve rodar a limpeza neste momento?
    /// </summary>
    /// <param name="c">Configuração atual (dias e horário).</param>
    /// <param name="agora">Momento atual.</param>
    /// <param name="ultimaExecucao">Quando rodou pela última vez (null se nunca), para não repetir na mesma janela.</param>
    /// <param name="adiadoPara">Se o aluno adiou, o momento a partir do qual deve rodar (null se não adiou).</param>
    public static bool DeveRodar(LimpezaConfig c, DateTime agora, DateTime? ultimaExecucao, DateTime? adiadoPara)
    {
        // Adiamento ("adiar 1 hora") tem prioridade: quando chega a hora adiada, roda — mesmo fora dos dias marcados.
        if (adiadoPara is { } ad && agora >= ad)
            return ultimaExecucao is not { } ue || ue < ad;

        if (!c.Agendada || c.Dias.Count == 0 || !LimpezaConfig.HoraValida(c.Hora)) return false;
        if (!c.Dias.Contains((int)agora.DayOfWeek)) return false;

        var marcadoHoje = HorarioDoDia(c.Hora, agora.Date);
        if (agora < marcadoHoje || agora > marcadoHoje.AddMinutes(GraceMinutos)) return false;
        if (ultimaExecucao is { } u && u >= marcadoHoje) return false; // já rodou nesta janela de hoje
        return true;
    }

    /// <summary>O horário "HH:mm" aplicado a um dia (ex.: "18:00" em 2026-10-14 → 2026-10-14 18:00).</summary>
    public static DateTime HorarioDoDia(string hora, DateTime dia)
    {
        var t = TimeOnly.ParseExact(hora, "HH:mm");
        return dia.Date.AddHours(t.Hour).AddMinutes(t.Minute);
    }
}
