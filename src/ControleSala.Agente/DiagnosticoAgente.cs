using System.Diagnostics.Eventing.Reader;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Diagnóstico deste PC, pedido pelo professor (Ajuda → Coletar diagnóstico da sala): versão, rede, eventos de erro/aviso do
/// Windows dos últimos dias (cabo/placa de rede caindo, IP repetido, desligamentos...) e o fim do agente.log e do servico.log.
/// Não inclui nada do aluno: nem tela, nem arquivos, nem o que ele digita.
/// </summary>
static class DiagnosticoAgente
{
    const int MaxBytesAgenteLog = 250_000, MaxBytesServicoLog = 80_000, MaxEventos = 150;
    static readonly TimeSpan PeriodoEventos = TimeSpan.FromDays(3);

    public static string Montar()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== ControleSala — diagnóstico de {NomeDoPc.Ler() ?? Environment.MachineName} ({Environment.MachineName}) ===");
        sb.AppendLine($"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"Versão do agente: {Versoes.Atual}");
        sb.AppendLine($"Windows: {RuntimeInformation.OSDescription}");
        var ligado = TimeSpan.FromMilliseconds(Environment.TickCount64);
        sb.AppendLine($"Ligado há: {(int)ligado.TotalHours} h {ligado.Minutes} min");
        Tentar(sb, "Placas de rede", Rede);
        Tentar(sb, $"Eventos de erro/aviso do Windows (últimos {PeriodoEventos.TotalDays:0} dias, mais recentes primeiro)", Eventos);
        string pastaLogs = Path.Combine(Path.GetDirectoryName(ConfigAgente.CaminhoPadrao)!, "logs");
        Tentar(sb, "servico.log (fim)", s => s.Append(FimDoArquivo(Path.Combine(pastaLogs, "servico.log"), MaxBytesServicoLog)));
        Tentar(sb, "atualizacao.log — instalador da última atualização (fim)", s => s.Append(FimDoArquivo(AtualizacaoAgente.CaminhoLog, 40_000)));
        Tentar(sb, "agente.log (fim)", s => s.Append(FimDoArquivo(Path.Combine(pastaLogs, "agente.log"), MaxBytesAgenteLog)));
        return sb.ToString();
    }

    static void Tentar(StringBuilder sb, string titulo, Action<StringBuilder> parte)
    {
        sb.AppendLine().AppendLine($"=== {titulo} ===");
        try { parte(sb); }
        catch (Exception ex) { sb.AppendLine($"(não foi possível ler: {ex.Message})"); }
    }

    static void Rede(StringBuilder sb)
    {
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var ips = n.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(a => a.Address.ToString());
            string velocidade = n.OperationalStatus == OperationalStatus.Up && n.Speed > 0 ? $"{n.Speed / 1_000_000} Mbps" : "-";
            sb.AppendLine($"{n.Name} | {n.Description} | {n.NetworkInterfaceType} | {n.OperationalStatus} | {velocidade} | IP {string.Join(", ", ips)}");
        }
    }

    static void Eventos(StringBuilder sb)
    {
        long ms = (long)PeriodoEventos.TotalMilliseconds;
        var consulta = new EventLogQuery("System", PathType.LogName,
            $"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= {ms}]]]")
        { ReverseDirection = true };
        using var leitor = new EventLogReader(consulta);
        int n = 0;
        for (var ev = leitor.ReadEvent(); ev != null && n < MaxEventos; ev = leitor.ReadEvent(), n++)
        {
            using (ev)
            {
                string texto;
                try { texto = ev.FormatDescription() ?? ""; }
                catch (EventLogException) { texto = ""; }
                texto = texto.ReplaceLineEndings(" ").Trim();
                if (texto.Length > 300) texto = texto[..300] + "...";
                sb.AppendLine($"{ev.TimeCreated:dd/MM HH:mm:ss} | {ev.LevelDisplayNameSeguro()} | {ev.ProviderName} #{ev.Id} | {texto}");
            }
        }
        if (n == 0) sb.AppendLine("(nenhum)");
    }

    static string LevelDisplayNameSeguro(this EventRecord ev)
    {
        try { return ev.LevelDisplayName ?? ev.Level?.ToString() ?? "?"; }
        catch (EventLogException) { return ev.Level?.ToString() ?? "?"; }
    }

    /// <summary>O fim do arquivo (o agente.log fica aberto para escrita pelo próprio agente, por isso o compartilhamento).</summary>
    static string FimDoArquivo(string caminho, int maxBytes)
    {
        if (!File.Exists(caminho)) return "(arquivo não existe)" + Environment.NewLine;
        using var arquivo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long inicio = Math.Max(0, arquivo.Length - maxBytes);
        arquivo.Seek(inicio, SeekOrigin.Begin);
        using var leitor = new StreamReader(arquivo, Encoding.UTF8);
        string texto = leitor.ReadToEnd();
        if (inicio > 0)
        {
            int quebra = texto.IndexOf('\n');
            texto = "(...início cortado...)" + Environment.NewLine + (quebra >= 0 ? texto[(quebra + 1)..] : texto);
        }
        return texto;
    }
}
