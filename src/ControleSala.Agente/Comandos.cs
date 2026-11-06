using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Executa os comandos do professor no PC do aluno. Cada ação roda na conta do próprio aluno (não como SYSTEM):
/// no serviço, via <see cref="LancadorNaSessao.IniciarComoUsuario"/>; em teste (console), direto.
/// </summary>
static class Comandos
{
    static bool EhSystem
    {
        get
        {
            try { return WindowsIdentity.GetCurrent().IsSystem; }
            catch (Exception) { return false; }
        }
    }

    public static (bool Ok, string Mensagem) Executar(Comando comando, string? professor = null)
    {
        try
        {
            return comando.Acao switch
            {
                AcaoComando.AbrirLink => AbrirLink(comando.Argumento),
                AcaoComando.Mensagem => Mensagem(comando.Argumento, professor),
                AcaoComando.BloquearTela => BloquearTela(comando.Argumento),
                AcaoComando.DesbloquearTela => Desbloquear(),
                AcaoComando.AbrirPrograma => AbrirPrograma(comando.Argumento),
                AcaoComando.FecharPrograma => FecharPrograma(comando.Argumento),
                AcaoComando.Desligar => DesligarOuReiniciar(reiniciar: false),
                AcaoComando.Reiniciar => DesligarOuReiniciar(reiniciar: true),
                AcaoComando.Logoff => Logoff(),
                AcaoComando.Silenciar => Silenciar(comando.Argumento),
                AcaoComando.BloquearInternet => BloquearInternet(comando.Argumento),
                AcaoComando.LiberarInternet => LiberarInternet(),
                AcaoComando.Limpeza => Limpeza(comando.Argumento),
                AcaoComando.DefinirPapel => LimpezaExecutor.AplicarPapelGuardado(),
                AcaoComando.IniciarAplicativo => IniciarAplicativo(comando.Argumento),
                _ => (false, "Este comando ainda não está disponível nesta versão."),
            };
        }
        catch (Exception ex)
        {
            return (false, $"Erro ao executar: {ex.Message}");
        }
    }

    static (bool, string) Mensagem(string texto, string? professor)
    {
        texto = texto.Trim();
        if (texto.Length == 0) return (false, "A mensagem está vazia.");
        if (texto.Length > 500) texto = texto[..500];
        InterfaceAgente.NaInterface(() => JanelasAluno.MostrarMensagem(texto, professor));
        return (true, "Mensagem exibida.");
    }

    static (bool, string) BloquearTela(string texto)
    {
        if (texto.Length > 200) texto = texto[..200];
        InterfaceAgente.NaInterface(() => JanelasAluno.Bloquear(texto));
        return (true, "Tela bloqueada.");
    }

    static (bool, string) Desbloquear()
    {
        InterfaceAgente.NaInterface(JanelasAluno.Desbloquear);
        return (true, "Tela desbloqueada.");
    }

    static (bool, string) AbrirPrograma(string comando)
    {
        if (Programas.ValidarComando(comando) is { } erro) return (false, erro);
        return Iniciar(ResolverPrograma(comando.Trim()), "", "Programa aberto.");
    }

    /// <summary>"Iniciar aplicativo" (estilo Veyon): roda o programa OU comando digitado, sem lista de permitidos.
    /// Roda na conta do aluno (sem admin). Ex.: "winword", "winword C:\arquivo.docx", "cmd /c ...".</summary>
    static (bool, string) IniciarAplicativo(string linha)
    {
        linha = (linha ?? "").Trim();
        if (linha.Length == 0) return (false, "Digite o programa ou comando a iniciar.");
        var (programa, args) = SepararComando(linha);
        return Iniciar(ResolverPrograma(programa), args, "Aplicativo iniciado.");
    }

    static (bool, string) Iniciar(string programa, string args, string okMsg)
    {
        if (EhSystem)
        {
            if (!LancadorNaSessao.IniciarComoUsuario(programa, args, out string e))
                return (false, $"Não foi possível iniciar: {e}.");
        }
        else
        {
            // Modo de teste (console): inicia na própria conta.
            Process.Start(new ProcessStartInfo(programa, args) { UseShellExecute = true });
        }
        return (true, okMsg);
    }

    /// <summary>Separa o primeiro item (programa, podendo vir entre aspas) do resto (argumentos).</summary>
    static (string programa, string args) SepararComando(string linha)
    {
        if (linha.StartsWith('"'))
        {
            int fim = linha.IndexOf('"', 1);
            if (fim > 0) return (linha[1..fim], linha[(fim + 1)..].Trim());
        }
        int esp = linha.IndexOf(' ');
        return esp < 0 ? (linha, "") : (linha[..esp], linha[(esp + 1)..].Trim());
    }

    /// <summary>Resolve o caminho de um programa registrado no "App Paths" (Word, Excel, navegadores...) quando vem só o nome.</summary>
    static string ResolverPrograma(string programa)
    {
        if (Path.IsPathRooted(programa)) return programa;
        string nome = programa.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? programa : programa + ".exe";
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + nome);
            if (k?.GetValue(null) is string caminho && caminho.Trim().Trim('"') is { Length: > 0 } c)
                return c;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
        return programa; // deixa o Windows procurar no PATH
    }

    static (bool, string) FecharPrograma(string nomeProcesso)
    {
        // Aceita "winword", "winword.exe" ou um caminho; usa só o nome do processo.
        string nome = Path.GetFileNameWithoutExtension(nomeProcesso.Trim());
        if (nome.Length == 0) return (false, "Informe o programa a fechar.");
        if (Programas.Proibidos.Contains(nome)) return (false, "Este programa não pode ser controlado.");

        var processos = Process.GetProcessesByName(nome);
        if (processos.Length == 0) return (true, "O programa não estava aberto.");

        int fechados = 0;
        foreach (var p in processos)
        {
            try
            {
                // Tenta fechar com jeito (deixa salvar); se não fechar em 2 s, encerra.
                if (p.CloseMainWindow() && p.WaitForExit(2000)) { fechados++; continue; }
                p.Kill();
                fechados++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // já tinha fechado, ou é um processo de outra conta que não dá para encerrar
            }
            finally { p.Dispose(); }
        }
        return (true, fechados > 0 ? $"{fechados} janela(s) fechada(s)." : "Não foi possível fechar.");
    }

    static (bool, string) Limpeza(string argumento)
    {
        // O argumento traz a configuração (JSON) da limpeza; se vier vazio, usa a guardada neste PC.
        var config = string.IsNullOrWhiteSpace(argumento)
            ? LimpezaConfig.Carregar(Path.Combine(LimpezaExecutor.PastaDados, "limpeza.json"))
            : LimpezaConfig.Desserializar(argumento);
        if (config == null) return (false, "Configuração de limpeza inválida.");
        if (LimpezaExecutor.Impedimento() is { } motivo) return (false, motivo);
        if (Interlocked.Exchange(ref _limpando, 1) == 1) return (false, "Já há uma limpeza em andamento neste computador.");

        // Responde na hora: o professor não fica esperando a contagem nem a limpeza terminar.
        _ = Task.Run(() =>
        {
            try
            {
                JanelasAluno.PerguntarLimpeza(SegundosAvisoLimpeza, ImagemDaLimpeza(), podeAdiar: false);
                var (_, mensagem) = LimpezaExecutor.Executar(config);
                Programa.Log($"Limpar agora: {mensagem}");
            }
            catch (Exception ex)
            {
                Programa.Log($"Limpar agora falhou: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _limpando, 0);
            }
        });
        return (true, $"Aviso enviado ao aluno. A limpeza começa em {SegundosAvisoLimpeza} segundos.");
    }

    const int SegundosAvisoLimpeza = 30;
    static int _limpando;

    static string? ImagemDaLimpeza() => File.Exists(LimpezaExecutor.CaminhoImagemLimpeza) ? LimpezaExecutor.CaminhoImagemLimpeza : null;

    const string RegraFirewall = "ControleSala Bloqueio Internet";

    // Arquivo hosts (pode ser trocado em teste pela variável CONTROLESALA_HOSTS).
    static string CaminhoHosts => Environment.GetEnvironmentVariable("CONTROLESALA_HOSTS")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    /// <param name="argumento">Vazio = bloqueia TODA a internet (firewall). Com sites = bloqueia só esses sites (hosts).</param>
    static (bool, string) BloquearInternet(string argumento)
    {
        var sites = Sites.Separar(argumento);
        if (sites.Count > 0)
            return BloquearSites(sites);

        // Bloqueia toda a internet, mas mantém a rede local (para o professor continuar alcançando o agente).
        // remoteip = tudo, menos as faixas privadas (10/8, 172.16/12, 192.168/16).
        const string publico = "1.0.0.0-9.255.255.255,11.0.0.0-172.15.255.255,172.32.0.0-192.167.255.255,192.169.0.0-223.255.255.255";
        Netsh($"advfirewall firewall delete rule name=\"{RegraFirewall}\"");
        int r = Netsh($"advfirewall firewall add rule name=\"{RegraFirewall}\" dir=out action=block " +
                      $"remoteip={publico} profile=any enable=yes");
        return r == 0 ? (true, "Internet bloqueada (a rede local continua funcionando).")
                      : (false, $"Não foi possível bloquear a internet (código {r}). Precisa do serviço (administrador).");
    }

    static (bool, string) BloquearSites(List<string> sites)
    {
        try
        {
            string hosts = CaminhoHosts;
            string atual = File.Exists(hosts) ? File.ReadAllText(hosts) : "";
            File.WriteAllText(hosts, Sites.MontarHosts(atual, sites));
            LimparCacheDns();
            return (true, $"{sites.Count} site(s) bloqueado(s).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, $"Não foi possível bloquear os sites: {ex.Message} (precisa do serviço).");
        }
    }

    internal static (bool, string) LiberarInternet()
    {
        // Libera os dois: tira a regra de firewall e a seção de sites do hosts.
        Netsh($"advfirewall firewall delete rule name=\"{RegraFirewall}\"");
        try
        {
            string hosts = CaminhoHosts;
            if (File.Exists(hosts))
            {
                string atual = File.ReadAllText(hosts);
                string limpo = Sites.RemoverSecao(atual);
                if (limpo != atual) { File.WriteAllText(hosts, limpo); LimparCacheDns(); }
            }
            return (true, "Internet e sites liberados.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, $"Não foi possível liberar os sites: {ex.Message}.");
        }
    }

    static int Netsh(string argumentos)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe"), argumentos)
            { UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(10000);
            return p?.ExitCode ?? -1;
        }
        catch (Exception) { return -1; }
    }

    static void LimparCacheDns()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ipconfig.exe"), "/flushdns")
            { UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(5000);
        }
        catch (Exception) { /* não é essencial */ }
    }

    static (bool, string) Silenciar(string argumento)
    {
        // "1" = silenciar; "0" = reativar o som.
        bool mudo = argumento.Trim() != "0";
        Audio.DefinirMudo(mudo);
        return (true, mudo ? "Som silenciado." : "Som reativado.");
    }

    const int SegundosAviso = 20;

    static (bool, string) DesligarOuReiniciar(bool reiniciar)
    {
        // O shutdown.exe já mostra ao aluno o aviso do Windows com a contagem regressiva.
        string acao = reiniciar ? "reiniciar" : "desligar";
        var inicio = new ProcessStartInfo("shutdown.exe",
            $"{(reiniciar ? "/r" : "/s")} /t {SegundosAviso} /c \"O computador vai {acao} a pedido do professor.\"")
        { UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(inicio);
        p?.WaitForExit(5000);
        if (p is { ExitCode: not 0 })
            return (false, $"O Windows recusou {acao} (código {p.ExitCode}).");
        return (true, $"O computador vai {acao} em {SegundosAviso} segundos.");
    }

    static (bool, string) Logoff()
    {
        // Avisa o aluno e encerra a sessão alguns segundos depois (dá tempo de salvar).
        InterfaceAgente.NaInterface(() => JanelasAluno.MostrarMensagem(
            $"A sua sessão vai ser encerrada em {SegundosAviso} segundos (professor). Salve o que estiver aberto.", null));
        if (!EhSystem)
            return (true, "Em modo de teste o logoff não é executado (precisa do serviço).");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(SegundosAviso));
            LancadorNaSessao.Logoff();
        });
        return (true, $"A sessão será encerrada em {SegundosAviso} segundos.");
    }

    static (bool, string) AbrirLink(string link)
    {
        if (Links.Validar(link, out string url) is { } erro)
            return (false, erro);

        // Abre no navegador padrão do aluno. explorer.exe <url> aciona o navegador padrão rodando na conta dele.
        if (EhSystem)
        {
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            if (!LancadorNaSessao.IniciarComoUsuario(explorer, $"\"{url}\"", out string e))
                return (false, $"Não foi possível abrir o link: {e}.");
        }
        else
        {
            // Modo de teste (console, já na conta do usuário): abre pelo shell.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        return (true, "Link aberto.");
    }
}
