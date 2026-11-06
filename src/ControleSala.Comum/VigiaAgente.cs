namespace ControleSala.Comum;

/// <summary>Regras do serviço que mantém o agente de pé no PC do aluno. Ficam aqui para dar para testar.</summary>
public static class VigiaAgente
{
    /// <summary>Quem cai antes disso conta como "não conseguiu subir".</summary>
    public static readonly TimeSpan TempoMinimoVivo = TimeSpan.FromSeconds(30);

    /// <summary>Quanto esperar para religar depois de <paramref name="quedas"/> quedas seguidas: nada, 2, 4, 8, 16 e no máximo 30 s.</summary>
    public static TimeSpan EsperaAposQuedas(int quedas) =>
        quedas <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(quedas, 5)));
}
