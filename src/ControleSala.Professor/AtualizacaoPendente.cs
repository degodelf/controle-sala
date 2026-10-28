using System.IO;
using System.Text.Json;

namespace ControleSala.Professor;

/// <summary>
/// Lembra que há uma atualização a ser empurrada aos alunos — inclusive os que estavam desligados e
/// só voltam a ligar depois. Fica gravada para o auto-update continuar mesmo se o professor reiniciar.
/// </summary>
sealed record AtualizacaoPendente(string Versao, string Caminho)
{
    static string Arquivo => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ControleSala", "atualizacao", "pendente.json");

    public static AtualizacaoPendente? Carregar()
    {
        try
        {
            if (!File.Exists(Arquivo)) return null;
            var p = JsonSerializer.Deserialize<AtualizacaoPendente>(File.ReadAllText(Arquivo));
            return p != null && File.Exists(p.Caminho) ? p : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void Salvar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
            File.WriteAllText(Arquivo, JsonSerializer.Serialize(this));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static void Limpar()
    {
        try { if (File.Exists(Arquivo)) File.Delete(Arquivo); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
