using System.Diagnostics;
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

    readonly ManualResetEvent _parar = new(false);
    Thread? _monitor;
    Process? _filho;
    uint _sessaoDoFilho = uint.MaxValue;

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
        Registrar("Serviço iniciado.");
        while (!_parar.WaitOne(1000))
        {
            try
            {
                uint sessao = LancadorNaSessao.SessaoAtiva();
                if (sessao == uint.MaxValue) continue; // nenhuma sessão ativa agora (ex.: troca de usuário)

                bool vivo = _filho is { HasExited: false };
                if (vivo && sessao == _sessaoDoFilho) continue;

                Encerrar();
                _filho = LancadorNaSessao.Iniciar(sessao, Environment.ProcessPath!, "--sessao");
                _sessaoDoFilho = sessao;
                Registrar(_filho == null
                    ? $"Não foi possível iniciar o agente na sessão {sessao}."
                    : $"Agente iniciado na sessão {sessao} (processo {_filho.Id}).");
                if (_filho == null)
                    _parar.WaitOne(5000); // evita tentar sem parar se algo estiver errado
            }
            catch (Exception ex)
            {
                Registrar($"Erro no monitor: {ex.Message}");
            }
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
            File.AppendAllText(Path.Combine(pasta, "servico.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {mensagem}{Environment.NewLine}");
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
    const int SecurityImpersonation = 2, TokenPrimary = 1, TokenSessionId = 12;

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
            return Process.GetProcessById(info.dwProcessId);
        }
        finally
        {
            if (desktop != 0) Marshal.FreeHGlobal(desktop);
            if (token != 0) CloseHandle(token);
            if (meuToken != 0) CloseHandle(meuToken);
        }
    }
}
