using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>Guarda o resultado da última limpeza deste PC, para o professor receber mesmo se o programa dele estava fechado na hora.</summary>
static class RelatoLimpeza
{
    static string Caminho => Path.Combine(LimpezaExecutor.PastaDados, "limpeza-resultado.json");

    public static ResultadoLimpeza? Atual { get; private set; }
    public static event Action<ResultadoLimpeza>? Mudou;

    public static void Carregar()
    {
        try
        {
            if (File.Exists(Caminho)) Atual = ResultadoLimpeza.Desserializar(File.ReadAllText(Caminho));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // sem acesso ao arquivo: fica sem histórico
        }
    }

    public static void Registrar(ResultadoLimpeza resultado)
    {
        Atual = resultado;
        try
        {
            Directory.CreateDirectory(LimpezaExecutor.PastaDados);
            File.WriteAllText(Caminho, resultado.Serializar());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // o resultado continua valendo em memória
        }
        Mudou?.Invoke(resultado);
    }
}
