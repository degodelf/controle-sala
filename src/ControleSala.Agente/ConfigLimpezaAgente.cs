using System.Diagnostics;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>Guarda a configuração de limpeza enviada pelo professor e (re)agenda a limpeza automática no Windows.</summary>
static class ConfigLimpezaAgente
{
    const string NomeTarefa = "ControleSalaLimpeza";

    /// <summary>Recebe a configuração de limpeza: guarda em disco e cria/atualiza/remove a tarefa agendada.</summary>
    public static void Receber(string json)
    {
        var config = LimpezaConfig.Desserializar(json);
        if (config == null) return;
        try
        {
            string caminho = Path.Combine(LimpezaExecutor.PastaDados, "limpeza.json");
            Directory.CreateDirectory(LimpezaExecutor.PastaDados);
            File.WriteAllText(caminho, json);
            Agendar(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // sem permissão (fora do serviço): a tarefa não é criada
        }
    }

    /// <summary>Recebe uma imagem de configuração (papel de parede ou tela de limpeza) e salva no lugar certo.</summary>
    public static void ReceberImagem(ArquivoConfigTipo tipo, byte[] conteudo)
    {
        try
        {
            Directory.CreateDirectory(LimpezaExecutor.PastaDados);
            string caminho = tipo == ArquivoConfigTipo.PapelDeParede ? LimpezaExecutor.CaminhoPapel : LimpezaExecutor.CaminhoImagemLimpeza;
            File.WriteAllBytes(caminho, conteudo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Remove a limpeza agendada (desinstalação).</summary>
    public static void RemoverAgendamento() => Schtasks($"/delete /tn {NomeTarefa} /f");

    static void Agendar(LimpezaConfig c)
    {
        Schtasks($"/delete /tn {NomeTarefa} /f"); // sempre refaz do zero
        if (!c.Agendada || c.Dias.Count == 0 || !LimpezaConfig.HoraValida(c.Hora)) return;

        string[] mapa = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];
        string dias = string.Join(",", c.Dias.Where(d => d is >= 0 and <= 6).Distinct().OrderBy(d => d).Select(d => mapa[d]));
        if (dias.Length == 0) return;

        string exe = Environment.ProcessPath!;
        // Roda como SYSTEM na hora marcada; o próprio agente (--limpar) descobre quem está usando o PC e limpa.
        Schtasks($"/create /tn {NomeTarefa} /tr \"\\\"{exe}\\\" --limpar\" /sc weekly /d {dias} /st {c.Hora} /ru SYSTEM /rl HIGHEST /f");
    }

    static int Schtasks(string argumentos)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"), argumentos)
            { UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(10000);
            return p?.ExitCode ?? -1;
        }
        catch (Exception) { return -1; }
    }
}
