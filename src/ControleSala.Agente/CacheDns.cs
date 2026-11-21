using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Os nomes de site que este PC procurou há pouco (o cache de DNS do Windows). Lê direto do Windows; se não der, lê a lista
/// do "ipconfig /displaydns". Nos Windows mais novos as duas só funcionam como administrador (o agente roda como SYSTEM).
/// </summary>
static partial class CacheDns
{
    [StructLayout(LayoutKind.Sequential)]
    struct Entrada
    {
        public nint Proximo;
        public nint Nome;
        public ushort Tipo;
        public ushort Tamanho;
        public uint Marcas;
    }

    [LibraryImport("dnsapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DnsGetCacheDataTable(out nint lista);

    [LibraryImport("dnsapi.dll")]
    private static partial void DnsFree(nint memoria, int tipo);

    const int DnsFreeFlat = 0;

    static bool _peloIpconfig;
    static DateTime _proximoIpconfig;

    /// <summary>Os nomes que estão no cache agora, ou null se não deu para ler desta vez.</summary>
    /// <param name="forcar">Lê mesmo que o ipconfig tenha rodado há pouco (na hora de bloquear os jogos).</param>
    public static List<string>? Nomes(bool forcar = false)
    {
        if (forcar) _proximoIpconfig = DateTime.MinValue;
        if (!_peloIpconfig)
        {
            var lidos = PeloWindows();
            if (lidos is { Count: > 0 } || DateTime.UtcNow < _proximoIpconfig) return lidos;
            // Vazio ou recusado: confere pelo ipconfig (no máximo a cada 30 s). Se ele achar nomes, o jeito direto não
            // funciona neste Windows e o ipconfig passa a ser o jeito.
            _proximoIpconfig = DateTime.UtcNow.AddSeconds(30);
            var doIpconfig = PeloIpconfig();
            if (doIpconfig is { Count: > 0 })
            {
                _peloIpconfig = true;
                Programa.Log("Filtro permanente: lendo os nomes pelo ipconfig (o jeito direto não funciona neste Windows).");
            }
            return doIpconfig ?? lidos;
        }
        if (DateTime.UtcNow < _proximoIpconfig) return null;
        _proximoIpconfig = DateTime.UtcNow.AddSeconds(8); // abrir o ipconfig custa mais; não precisa ser a cada 3 s
        return PeloIpconfig();
    }

    static List<string>? PeloWindows()
    {
        try
        {
            if (!DnsGetCacheDataTable(out nint atual)) return null;
            var nomes = new List<string>();
            while (atual != 0)
            {
                var e = Marshal.PtrToStructure<Entrada>(atual);
                if (e.Nome != 0)
                {
                    if (Marshal.PtrToStringUni(e.Nome) is { Length: > 0 } nome) nomes.Add(nome.ToLowerInvariant());
                    DnsFree(e.Nome, DnsFreeFlat);
                }
                DnsFree(atual, DnsFreeFlat);
                atual = e.Proximo;
            }
            return nomes;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    static List<string>? PeloIpconfig()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ipconfig.exe"), "/displaydns")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            if (p is null) return null;
            var leitura = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(5000)) { try { p.Kill(); } catch (InvalidOperationException) { } return null; }
            return FiltroPorNome.NomesNoTexto(leitura.Result).Distinct().ToList();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Os endereços IPv4 desses nomes, enquanto ainda estão no cache (antes de bloquear). No máximo 2 s.</summary>
    public static List<IPAddress> Enderecos(IEnumerable<string> nomes)
    {
        var tarefas = nomes.Distinct().Take(20).Select(async n =>
        {
            try { return await Dns.GetHostAddressesAsync(n, AddressFamily.InterNetwork); }
            catch (SocketException) { return []; }
        }).ToArray();
        try { Task.WaitAll(tarefas, TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        return tarefas.Where(t => t.IsCompletedSuccessfully).SelectMany(t => t.Result)
            .Where(ip => !IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any)).Distinct().ToList();
    }
}

/// <summary>
/// Derruba as conexões abertas com um site que acabou de ser bloqueado (senão a página aberta continuaria funcionando).
/// Só IPv4: é o que o Windows deixa derrubar. Quem mais estiver no mesmo endereço só reconecta.
/// </summary>
static partial class ConexoesTcp
{
    [StructLayout(LayoutKind.Sequential)]
    struct LinhaComPid { public uint Estado, EnderecoLocal, PortaLocal, EnderecoRemoto, PortaRemota, Pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct Linha { public uint Estado, EnderecoLocal, PortaLocal, EnderecoRemoto, PortaRemota; }

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetExtendedTcpTable(nint tabela, ref int tamanho, [MarshalAs(UnmanagedType.Bool)] bool ordenar,
        int familia, int classe, uint reservado);

    [LibraryImport("iphlpapi.dll")]
    private static partial uint SetTcpEntry(ref Linha linha);

    const int AfInet = 2, SoConexoes = 4; // TCP_TABLE_OWNER_PID_CONNECTIONS
    const uint ApagarConexao = 12;        // MIB_TCP_STATE_DELETE_TCB
    const uint BufferPequeno = 122;

    /// <summary>
    /// Derruba agora e de novo a cada 5 s, por 75 s. O Chrome guarda o endereço do site por até 1 minuto: se a conexão caísse
    /// uma vez só, o jogo reconectava pelo endereço guardado e continuava. Depois desse minuto, o nome já não resolve.
    /// </summary>
    public static void DerrubarPorUmTempo(IReadOnlyCollection<IPAddress> enderecos)
    {
        if (enderecos.Count == 0) return;
        var lista = enderecos.ToList();
        Derrubar(lista);
        int vezes = 0;
        System.Threading.Timer? relogio = null;
        relogio = new System.Threading.Timer(_ =>
        {
            try { Derrubar(lista); }
            catch (Exception) { }
            if (Interlocked.Increment(ref vezes) >= 15) relogio?.Dispose();
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>Quantas conexões foram derrubadas.</summary>
    public static int Derrubar(IEnumerable<IPAddress> enderecos)
    {
        var alvos = enderecos.Where(e => e.AddressFamily == AddressFamily.InterNetwork)
            .Select(e => BitConverter.ToUInt32(e.GetAddressBytes())).ToHashSet();
        if (alvos.Count == 0) return 0;
        nint tabela = 0;
        try
        {
            int tamanho = 0;
            uint r = BufferPequeno;
            for (int tentativa = 0; tentativa < 3 && r == BufferPequeno; tentativa++)
            {
                if (tabela != 0) Marshal.FreeHGlobal(tabela);
                tamanho += 4096;
                tabela = Marshal.AllocHGlobal(tamanho);
                r = GetExtendedTcpTable(tabela, ref tamanho, false, AfInet, SoConexoes, 0);
            }
            if (r != 0) return 0;
            int quantas = Marshal.ReadInt32(tabela), derrubadas = 0, passo = Marshal.SizeOf<LinhaComPid>();
            for (int i = 0; i < quantas; i++)
            {
                var c = Marshal.PtrToStructure<LinhaComPid>(tabela + 4 + i * passo);
                if (!alvos.Contains(c.EnderecoRemoto)) continue;
                var apagar = new Linha
                {
                    Estado = ApagarConexao, EnderecoLocal = c.EnderecoLocal, PortaLocal = c.PortaLocal,
                    EnderecoRemoto = c.EnderecoRemoto, PortaRemota = c.PortaRemota,
                };
                if (SetTcpEntry(ref apagar) == 0) derrubadas++;
            }
            return derrubadas;
        }
        finally
        {
            if (tabela != 0) Marshal.FreeHGlobal(tabela);
        }
    }
}
