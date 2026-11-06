using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Serviço do Windows (SYSTEM) que mantém o agente rodando na sessão de quem está usando o PC.
/// O processo do agente roda com a identidade SYSTEM (não com a do aluno), por isso o aluno não consegue
/// encerrá-lo pelo Gerenciador de Tarefas, nem abrir nada além do cartão informativo da bandeja.
/// </summary>
sealed class ServicoAgente : ServiceBase
{
    public const string Nome = "ControleSalaAgente";
    const int SegundosEntreChecagensDaPorta = 15, ChecagensSemPortaAntesDeReiniciar = 4;

    readonly ManualResetEvent _parar = new(false);
    Thread? _monitor;
    Process? _filho;
    uint _sessaoDoFilho = uint.MaxValue;
    DateTime _inicioDoFilho, _proximaTentativa;
    int _quedas, _segundosDesdeChecagem, _checagensSemPorta;
    bool _tentouLiberarPorta;

    public ServicoAgente()
    {
        ServiceName = Nome;
        CanStop = true;
        CanHandleSessionChangeEvent = true;
    }

    protected override void OnStart(string[] args)
    {
        _parar.Reset();
        _monitor = new Thread(Monitorar) { IsBackground = true, Name = "Monitor de sessão" };
        _monitor.Start();
    }

    protected override void OnStop()
    {
        _parar.Set();
        _monitor?.Join(5000);
        Encerrar();
    }

    void Monitorar()
    {
        Registrar($"Serviço iniciado (versão {Versoes.Atual}).");
        while (!_parar.WaitOne(1000))
        {
            try
            {
                uint sessao = LancadorNaSessao.SessaoAtiva();
                if (sessao == uint.MaxValue) continue; // nenhuma sessão ativa agora (ex.: troca de usuário)

                bool vivo = _filho is { HasExited: false };
                if (vivo && sessao == _sessaoDoFilho)
                {
                    VigiarPorta();
                    continue;
                }

                if (_filho != null && !vivo) ContarQueda();
                Encerrar();
                if (DateTime.UtcNow < _proximaTentativa) continue;

                if (_quedas >= 2) LiberarPortaSeBloqueada();
                _filho = LancadorNaSessao.Iniciar(sessao, Environment.ProcessPath!, "--sessao");
                _sessaoDoFilho = sessao;
                _inicioDoFilho = DateTime.UtcNow;
                _segundosDesdeChecagem = _checagensSemPorta = 0;
                Registrar(_filho == null
                    ? $"Não foi possível iniciar o agente na sessão {sessao}."
                    : $"Agente iniciado na sessão {sessao} (processo {_filho.Id}).");
                if (_filho == null) ContarQueda();
            }
            catch (Exception ex)
            {
                Registrar($"Erro no monitor: {ex.Message}");
            }
        }
    }

    /// <summary>Agente que cai logo depois de subir espera cada vez mais para religar; o que ficou de pé recomeça a conta.</summary>
    void ContarQueda()
    {
        _quedas = DateTime.UtcNow - _inicioDoFilho < VigiaAgente.TempoMinimoVivo ? _quedas + 1 : 0;
        var espera = VigiaAgente.EsperaAposQuedas(_quedas);
        _proximaTentativa = DateTime.UtcNow + espera;
        if (_quedas > 0)
            Registrar($"O agente caiu logo depois de subir ({_quedas}x seguidas). Nova tentativa em {espera.TotalSeconds:0} s. Veja agente.log.");
    }

    /// <summary>Agente vivo sem abrir a porta fica invisível para o professor: depois de cerca de um minuto assim, é reiniciado.</summary>
    void VigiarPorta()
    {
        if (++_segundosDesdeChecagem < SegundosEntreChecagensDaPorta) return;
        _segundosDesdeChecagem = 0;
        if (PortaEscutando())
        {
            _checagensSemPorta = 0;
            return;
        }
        if (++_checagensSemPorta < ChecagensSemPortaAntesDeReiniciar) return;
        Registrar($"O agente está aberto, mas sem escutar a porta {Protocolo.PortaPadrao}. Reiniciando o agente.");
        Encerrar();
    }

    static bool PortaEscutando()
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == Protocolo.PortaPadrao);
        }
        catch (NetworkInformationException)
        {
            return true; // sem como saber: não derruba um agente que pode estar bem
        }
    }

    /// <summary>
    /// O Windows pode reservar a porta do agente para a virtualização (erro 10013) e aí o agente não sobe. Faz a mesma reserva
    /// do instalador, uma vez por início do serviço, porque parar o winnat não é para ficar repetindo.
    /// </summary>
    void LiberarPortaSeBloqueada()
    {
        if (_tentouLiberarPorta || !PortaBloqueada()) return;
        _tentouLiberarPorta = true;
        Registrar($"O Windows bloqueou a porta {Protocolo.PortaPadrao} (erro 10013). Tentando reservá-la.");
        string reservar = $"int ipv4 add excludedportrange protocol=tcp startport={Protocolo.PortaPadrao} numberofports=1 store=persistent";
        int codigo = Rodar("netsh.exe", reservar);
        if (codigo != 0)
        {
            Rodar("net.exe", "stop winnat");
            codigo = Rodar("netsh.exe", reservar);
            Rodar("net.exe", "start winnat");
        }
        Registrar(codigo == 0 ? "Porta reservada." : $"Não foi possível reservar a porta (código {codigo}). Reiniciar o computador pode liberá-la.");
    }

    static bool PortaBloqueada()
    {
        var teste = new TcpListener(IPAddress.Any, Protocolo.PortaPadrao);
        try
        {
            teste.Start();
            return false;
        }
        catch (SocketException ex)
        {
            return ex.SocketErrorCode == SocketError.AccessDenied;
        }
        finally
        {
            teste.Stop();
        }
    }

    static int Rodar(string programa, string argumentos)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, programa), argumentos)
            { UseShellExecute = false, CreateNoWindow = true });
            if (p == null) return -1;
            if (p.WaitForExit(30000)) return p.ExitCode;
            p.Kill();
            return -1;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return -1;
        }
    }

    void Encerrar()
    {
        try
        {
            // Só o agente, NÃO a árvore: o que ele abriu continua (programas abertos pelo professor para o aluno e,
            // na atualização pela rede, o próprio instalador — que é quem para este serviço).
            if (_filho is { HasExited: false }) _filho.Kill(entireProcessTree: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // já tinha terminado
        }
        _filho?.Dispose();
        _filho = null;
    }

    static void Registrar(string mensagem)
    {
        try
        {
            string pasta = Path.Combine(Path.GetDirectoryName(ConfigAgente.CaminhoPadrao)!, "logs");
            Directory.CreateDirectory(pasta);
            string arquivo = Path.Combine(pasta, "servico.log");
            if (File.Exists(arquivo) && new FileInfo(arquivo).Length > 1_000_000) File.Delete(arquivo);
            File.AppendAllText(arquivo, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {mensagem}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // sem onde gravar: segue sem registro
        }
    }
}

/// <summary>Abre um processo na sessão de um usuário a partir do serviço, usando o token do próprio serviço (SYSTEM).</summary>
static partial class LancadorNaSessao
{
    const uint TokenDuplicate = 0x0002, TokenQuery = 0x0008;
    const uint MaximumAllowed = 0x02000000, CreateNoWindow = 0x08000000, CreateUnicodeEnvironment = 0x00000400;
    const int SecurityImpersonation = 2, TokenPrimary = 1, TokenSessionId = 12, WtsUserName = 5;

    [StructLayout(LayoutKind.Sequential)]
    struct StartupInfo
    {
        public int cb;
        public nint lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public nint lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation
    {
        public nint hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [LibraryImport("kernel32.dll")] private static partial uint WTSGetActiveConsoleSessionId();
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint processo, uint acesso, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateTokenEx(nint token, uint acesso, nint atributos, int nivel, int tipo, out nint novo);

    [LibraryImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetTokenInformation(nint token, int classe, ref uint valor, uint tamanho);

    [LibraryImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessAsUser(nint token, string? aplicativo, string linhaDeComando, nint atribProcesso, nint atribThread,
        [MarshalAs(UnmanagedType.Bool)] bool herdar, uint flags, nint ambiente, string? diretorio, ref StartupInfo inicio, out ProcessInformation info);

    [LibraryImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQueryUserToken(uint sessao, out nint token);

    [LibraryImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSLogoffSession(nint servidor, uint sessao, [MarshalAs(UnmanagedType.Bool)] bool esperar);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformation(nint servidor, uint sessao, int classe, out nint texto, out uint tamanho);

    [LibraryImport("wtsapi32.dll")] private static partial void WTSFreeMemory(nint memoria);

    [LibraryImport("userenv.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateEnvironmentBlock(out nint ambiente, nint token, [MarshalAs(UnmanagedType.Bool)] bool herdar);

    [LibraryImport("userenv.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyEnvironmentBlock(nint ambiente);

    [LibraryImport("userenv.dll", EntryPoint = "GetUserProfileDirectoryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetUserProfileDirectory(nint token, [Out] char[]? caminho, ref uint tamanho);

    /// <summary>Pasta do perfil de quem está usando o PC (ex.: C:\Users\aluno). Null se não houver sessão. Só no serviço (SYSTEM).</summary>
    public static string? PerfilDaSessao()
    {
        uint sessao = WTSGetActiveConsoleSessionId();
        if (sessao == uint.MaxValue) return null;
        nint token = 0;
        try
        {
            if (!WTSQueryUserToken(sessao, out token)) return null;
            uint tam = 0;
            GetUserProfileDirectory(token, null, ref tam); // 1ª chamada só descobre o tamanho
            if (tam == 0) return null;
            var buffer = new char[tam];
            return GetUserProfileDirectory(token, buffer, ref tam) ? new string(buffer, 0, (int)tam - 1) : null;
        }
        finally
        {
            if (token != 0) CloseHandle(token);
        }
    }

    /// <summary>Quem entrou no Windows nesta sessão; vazio ou null na tela de login.</summary>
    public static string? UsuarioDaSessao()
    {
        uint sessao = WTSGetActiveConsoleSessionId();
        if (sessao == uint.MaxValue || !WTSQuerySessionInformation(0, sessao, WtsUserName, out nint texto, out _)) return null;
        try
        {
            return Marshal.PtrToStringUni(texto);
        }
        finally
        {
            WTSFreeMemory(texto);
        }
    }

    /// <summary>Sessão do console (a de quem está na frente do PC); uint.MaxValue se não houver.</summary>
    public static uint SessaoAtiva() => WTSGetActiveConsoleSessionId();

    /// <summary>Encerra a sessão de quem está usando o PC (logoff). Precisa do serviço (SYSTEM).</summary>
    public static bool Logoff()
    {
        uint sessao = WTSGetActiveConsoleSessionId();
        return sessao != uint.MaxValue && WTSLogoffSession(0, sessao, false);
    }

    /// <summary>
    /// Abre um processo como o USUÁRIO que está usando o PC (não como SYSTEM). É o que serve para abrir o navegador,
    /// um programa etc. — rodando na conta do aluno, com o ambiente dele. Só funciona quando o agente é SYSTEM (serviço).
    /// </summary>
    public static bool IniciarComoUsuario(string exe, string argumentos, out string erro)
    {
        erro = "";
        uint sessao = WTSGetActiveConsoleSessionId();
        if (sessao == uint.MaxValue) { erro = "ninguém está usando o computador agora"; return false; }

        nint token = 0, ambiente = 0, desktop = 0;
        try
        {
            if (!WTSQueryUserToken(sessao, out token)) { erro = $"não foi possível obter a conta do usuário ({Marshal.GetLastWin32Error()})"; return false; }
            CreateEnvironmentBlock(out ambiente, token, false); // se falhar, segue com ambiente 0

            desktop = Marshal.StringToHGlobalUni("winsta0\\default");
            var inicio = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>(), lpDesktop = desktop };
            uint flags = CreateNoWindow | (ambiente != 0 ? CreateUnicodeEnvironment : 0);
            // Programa sem caminho (ex.: "calc.exe") é resolvido pelo PATH do usuário; nesse caso não força o diretório.
            string? diretorio = Path.IsPathRooted(exe) ? Path.GetDirectoryName(exe) : null;
            if (!CreateProcessAsUser(token, null, $"\"{exe}\" {argumentos}", 0, 0, false, flags, ambiente,
                    diretorio, ref inicio, out var info))
            {
                erro = $"não foi possível abrir ({Marshal.GetLastWin32Error()})";
                return false;
            }
            CloseHandle(info.hThread);
            CloseHandle(info.hProcess);
            return true;
        }
        finally
        {
            if (desktop != 0) Marshal.FreeHGlobal(desktop);
            if (ambiente != 0) DestroyEnvironmentBlock(ambiente);
            if (token != 0) CloseHandle(token);
        }
    }

    public static Process? Iniciar(uint sessao, string exe, string argumentos)
    {
        nint meuToken = 0, token = 0, desktop = 0;
        try
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenDuplicate | TokenQuery, out meuToken)) return null;
            if (!DuplicateTokenEx(meuToken, MaximumAllowed, 0, SecurityImpersonation, TokenPrimary, out token)) return null;
            if (!SetTokenInformation(token, TokenSessionId, ref sessao, sizeof(uint))) return null;

            desktop = Marshal.StringToHGlobalUni("winsta0\\default");
            var inicio = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>(), lpDesktop = desktop };
            if (!CreateProcessAsUser(token, null, $"\"{exe}\" {argumentos}", 0, 0, false, CreateNoWindow, 0,
                    Path.GetDirectoryName(exe), ref inicio, out var info))
                return null;

            CloseHandle(info.hThread);
            CloseHandle(info.hProcess);
            try { return Process.GetProcessById(info.dwProcessId); }
            catch (ArgumentException) { return null; } // já terminou: conta como não ter subido
        }
        finally
        {
            if (desktop != 0) Marshal.FreeHGlobal(desktop);
            if (token != 0) CloseHandle(token);
            if (meuToken != 0) CloseHandle(meuToken);
        }
    }
}
