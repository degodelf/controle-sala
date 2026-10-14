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
            // O agendamento agora é feito pelo próprio agente em sessão (AgendadorLimpeza), que relê este arquivo.
            // Remove qualquer Tarefa do Windows criada por versões antigas, para não limpar duas vezes.
            RemoverAgendamento();
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

    /// <summary>Remove a Tarefa do Windows antiga da limpeza (desinstalação e migração de versões anteriores).</summary>
    public static void RemoverAgendamento() => Schtasks($"/delete /tn {NomeTarefa} /f");

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
