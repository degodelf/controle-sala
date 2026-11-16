using System.IO;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>
/// Diário das quedas e reconexões de cada PC (<c>logs\conexoes.log</c> na pasta de dados do professor). Não aparece na tela:
/// serve para descobrir por que uma conexão cai (rede, agente reiniciando...), comparando com o agente.log do aluno.
/// </summary>
static class RegistroConexoes
{
    const long TamanhoMaximo = 1_000_000;
    static readonly Lock _trava = new();

    public static string Caminho => Path.Combine(Pastas.Dados, "logs", "conexoes.log");

    public static void Anotar(ComputadorSala pc, string texto)
    {
        string linha = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {pc.Nome} ({pc.Endereco}): {texto}{Environment.NewLine}";
        lock (_trava)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
                var info = new FileInfo(Caminho);
                if (info.Exists && info.Length > TamanhoMaximo)
                    File.Move(Caminho, Path.ChangeExtension(Caminho, ".anterior.log"), overwrite: true);
                File.AppendAllText(Caminho, linha);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Sem como gravar o diário: não atrapalha a aula.
            }
        }
    }

    /// <summary>"3 min", "45 s", "2 h 10 min".</summary>
    public static string Duracao(TimeSpan t) =>
        t.TotalSeconds < 60 ? $"{t.TotalSeconds:0} s"
        : t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes} min"
        : $"{(int)t.TotalHours} h {t.Minutes} min";
}
