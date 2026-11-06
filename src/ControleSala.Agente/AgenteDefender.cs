using System.Diagnostics;
using System.Text;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Roda a atualização e a verificação do Windows Defender em segundo plano, sem prender o aluno, e guarda o resultado
/// em disco. Uma verificação completa pode levar horas e o PC pode ser desligado no meio: ao reabrir, isso aparece como interrompida.
/// </summary>
static class AgenteDefender
{
    static string Caminho => Path.Combine(LimpezaExecutor.PastaDados, "defender.json");
    static int _rodando;
    static Thread? _execucao;

    public static ResultadoDefender? Atual { get; private set; }
    public static event Action<ResultadoDefender>? Mudou;

    /// <summary>Lê o último resultado guardado. Chamado quando o agente abre.</summary>
    public static void Carregar()
    {
        try
        {
            if (File.Exists(Caminho) && ResultadoDefender.Desserializar(File.ReadAllText(Caminho)) is { } salvo)
                Definir(salvo.Interrompido(DateTimeOffset.Now));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // sem acesso ao arquivo: fica sem histórico
        }
    }

    static void Definir(ResultadoDefender resultado)
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

    /// <summary>Começa o que a limpeza pediu e devolve o texto para a resposta ao professor (vazio se nada foi pedido).</summary>
    public static string Iniciar(LimpezaConfig c)
    {
        if (!c.AtualizarDefender && !c.VerificarDefender) return "";
        if (Interlocked.Exchange(ref _rodando, 1) == 1) return " O Windows Defender já está rodando.";

        var inicio = ResultadoDefender.Iniciado(c.AtualizarDefender, c.VerificarDefender, DateTimeOffset.Now);
        Definir(inicio);
        _execucao = new Thread(() => Rodar(c, inicio)) { IsBackground = true, Name = "Windows Defender" };
        _execucao.Start();
        return " Windows Defender rodando em segundo plano.";
    }

    static void Rodar(LimpezaConfig c, ResultadoDefender inicio)
    {
        try
        {
            string script = ResultadoDefender.Script(c.AtualizarDefender, c.VerificarDefender, c.DefenderVarreduraCompleta);
            using var p = Process.Start(new ProcessStartInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 })
                ?? throw new InvalidOperationException("o PowerShell não abriu");
            string saida = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Definir(inicio.Concluido(saida, DateTimeOffset.Now));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            Definir(inicio.Falhou(ex.Message, DateTimeOffset.Now));
        }
        finally
        {
            Interlocked.Exchange(ref _rodando, 0);
        }
    }

    /// <summary>Espera terminar. Só para o modo de linha de comando, que senão fecharia antes do resultado.</summary>
    public static void EsperarTerminar() => _execucao?.Join();
}
