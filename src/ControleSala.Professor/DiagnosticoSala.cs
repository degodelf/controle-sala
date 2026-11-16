using System.IO;
using System.IO.Compression;
using System.Text;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Monta o .zip do diagnóstico da sala: um arquivo por PC, o diário de quedas deste PC e um resumo da sala.</summary>
static class DiagnosticoSala
{
    static readonly TimeSpan EsperaPorPc = TimeSpan.FromSeconds(30);

    /// <returns>O caminho do .zip e os PCs que não mandaram diagnóstico.</returns>
    public static async Task<(string Zip, List<string> Faltaram)> ColetarAsync(IReadOnlyList<ConexaoComputador> conexoes)
    {
        string pasta = Path.Combine(Path.GetTempPath(), $"controlesala-diagnostico-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pasta);
        try
        {
            var faltaram = new System.Collections.Concurrent.ConcurrentBag<string>();
            var resumo = new StringBuilder();
            resumo.AppendLine($"ControleSala {Versoes.Atual} — diagnóstico da sala {Sessao.Sala?.Sala} ({Sessao.Sala?.Escola})");
            resumo.AppendLine($"Gerado em {DateTime.Now:dd/MM/yyyy HH:mm:ss} no PC do professor {Environment.MachineName}");
            resumo.AppendLine();
            foreach (var c in conexoes)
                resumo.AppendLine($"{c.Dados.Nome} | {c.Dados.Endereco}:{c.Dados.Porta} | {c.Estado} {c.Detalhe} | agente {c.VersaoAgente ?? "?"} | MAC {c.Dados.Mac ?? "-"}");

            await Task.WhenAll(conexoes.Select(async c =>
            {
                string? texto = await c.PedirDiagnosticoAsync(EsperaPorPc, CancellationToken.None);
                if (texto == null) { faltaram.Add(c.Dados.Nome); return; }
                await File.WriteAllTextAsync(Path.Combine(pasta, NomeDeArquivo(c.Dados.Nome) + ".txt"), texto);
            }));

            await File.WriteAllTextAsync(Path.Combine(pasta, "_sala.txt"), resumo.ToString());
            foreach (var log in new[] { RegistroConexoes.Caminho, Path.ChangeExtension(RegistroConexoes.Caminho, ".anterior.log") })
                if (File.Exists(log)) File.Copy(log, Path.Combine(pasta, "_professor-" + Path.GetFileName(log)));

            string area = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string zip = Path.Combine(area, $"ControleSala-diagnostico-{DateTime.Now:yyyy-MM-dd-HHmm}.zip");
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(pasta, zip);
            return (zip, faltaram.Order(StringComparer.CurrentCultureIgnoreCase).ToList());
        }
        finally
        {
            try { Directory.Delete(pasta, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>"PC 03" → "PC 03"; tira o que o Windows não aceita em nome de arquivo.</summary>
    static string NomeDeArquivo(string nome)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        string limpo = new([.. nome.Select(c => invalidos.Contains(c) ? '_' : c)]);
        return limpo.Trim().Length == 0 ? "computador" : limpo.Trim();
    }
}
