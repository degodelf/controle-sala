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
                AcaoComando.Limpeza => Limpeza(comando.Argumento, professor),
                AcaoComando.DefinirPapel => LimpezaExecutor.AplicarPapelGuardado(),
                AcaoComando.IniciarAplicativo => IniciarAplicativo(comando.Argumento),
                AcaoComando.AbrirArquivoRecebido => AbrirArquivoRecebido(comando.Argumento),
                AcaoComando.BloquearJogos => JogosAgente.ComandoBloquear(comando.Argumento),
                AcaoComando.LiberarJogos => JogosAgente.ComandoLiberar(comando.Argumento),
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

    /// <summary>Onde este agente guarda o que o professor envia.</summary>
    public static string PastaDeRecebidos { get; set; } = PastaRecebidos.Padrao;

    /// <summary>Abre, na conta do aluno, um arquivo que acabou de chegar, com o programa padrão do Windows para aquele tipo.</summary>
    static (bool, string) AbrirArquivoRecebido(string nome)
    {
        if (PastaRecebidos.Localizar(PastaDeRecebidos, nome) is not { } caminho)
            return (false, "O arquivo não está na pasta de recebidos.");
        return Iniciar(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"\"{caminho}\"", "Arquivo aberto.");
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

    static (bool, string) Limpeza(string argumento, string? professor)
    {
        // O argumento traz a configuração (JSON) da limpeza; se vier vazio, usa a guardada neste PC.
        var config = string.IsNullOrWhiteSpace(argumento)
            ? LimpezaConfig.Carregar(Path.Combine(LimpezaExecutor.PastaDados, "limpeza.json"))
            : LimpezaConfig.Desserializar(argumento);
        if (config == null) return (false, "Configuração de limpeza inválida.");
        if (LimpezaExecutor.Impedimento() is { } motivo) return (false, motivo);
        if (!LimpezaExecutor.Trava.TentarPegar()) return (false, "Já há uma limpeza em andamento neste computador.");

        // Responde na hora: o professor não fica esperando a contagem nem a limpeza terminar.
        _ = Task.Run(() =>
        {
            try
            {
                JanelasAluno.PerguntarLimpeza(SegundosAvisoLimpeza, ImagemDaLimpeza(), podeAdiar: false);
                var (ok, mensagem) = LimpezaExecutor.Executar(config, pedida: professor ?? "");
                if (ok) AgendadorLimpeza.MarcarLimpezaFeita();
                Programa.Log($"Limpar agora: {mensagem}");
            }
            catch (Exception ex)
            {
                Programa.Log($"Limpar agora falhou: {ex.Message}");
            }
            finally
            {
                LimpezaExecutor.Trava.Soltar();
            }
        });
        return (true, $"Aviso enviado ao aluno. A limpeza começa em {SegundosAvisoLimpeza} segundos.");
    }

    const int SegundosAvisoLimpeza = 30;

    static string? ImagemDaLimpeza() => File.Exists(LimpezaExecutor.CaminhoImagemLimpeza) ? LimpezaExecutor.CaminhoImagemLimpeza : null;

    const string RegraFirewall = "ControleSala Bloqueio Internet";

    // Arquivo hosts (pode ser trocado em teste pela variável CONTROLESALA_HOSTS).
    static readonly object _travaHosts = new();

    /// <summary>
    /// Lê, muda e grava o hosts de uma vez só. O professor (sites), o filtro permanente e os jogos mexem nele, cada um na sua
    /// seção, às vezes ao mesmo tempo: sem a trava, um podia gravar por cima do que o outro acabou de pôr. True se mudou.
    /// </summary>
    internal static bool MudarHosts(Func<string, string> mudar)
    {
        lock (_travaHosts)
        {
            string hosts = CaminhoHosts;
            string atual = File.Exists(hosts) ? File.ReadAllText(hosts) : "";
            string novo = mudar(atual);
            if (novo == atual) return false;
            File.WriteAllText(hosts, novo);
            return true;
        }
    }

    internal static string CaminhoHosts => Environment.GetEnvironmentVariable("CONTROLESALA_HOSTS")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    /// <param name="argumento">Vazio = bloqueia TODA a internet (firewall). Com sites = bloqueia só esses sites (hosts + regra de DNS).</param>
    static (bool, string) BloquearInternet(string argumento)
    {
        var (dominios, terminacoes) = Sites.SepararTudo(argumento);
        if (dominios.Count > 0 || terminacoes.Count > 0)
            return BloquearSites(dominios, terminacoes);

        // Bloqueia toda a internet, mas mantém a rede local (para o professor continuar alcançando o agente).
        // remoteip = tudo, menos as faixas privadas (10/8, 172.16/12, 192.168/16). No IPv6, 2000::/3 é a internet: sobram os endereços locais.
        const string publico = "1.0.0.0-9.255.255.255,11.0.0.0-172.15.255.255,172.32.0.0-192.167.255.255,192.169.0.0-223.255.255.255,2000::/3";
        Netsh($"advfirewall firewall delete rule name=\"{RegraFirewall}\"");
        int r = Netsh($"advfirewall firewall add rule name=\"{RegraFirewall}\" dir=out action=block " +
                      $"remoteip={publico} profile=any enable=yes");
        return r == 0 ? (true, "Internet bloqueada (a rede local continua funcionando).")
                      : (false, $"Não foi possível bloquear a internet (código {r}). Precisa do serviço (administrador).");
    }

    static (bool, string) BloquearSites(List<string> dominios, List<string> terminacoes)
    {
        try
        {
            // 1) hosts: o site e o www (terminações como ".bet" não cabem no hosts).
            MudarHosts(atual => Sites.MontarHosts(atual, dominios));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, $"Não foi possível bloquear os sites: {ex.Message} (precisa do serviço).");
        }
        // 2) regra de DNS do Windows: todos os subdomínios e as terminações. Se o Windows recusar, o hosts continua valendo.
        string? erroDns = AplicarRegraDns(Sites.NomesDaRegraDns(dominios, terminacoes));
        LimparCacheDns();
        int total = dominios.Count + terminacoes.Count;
        return erroDns == null
            ? (true, $"{total} endereço(s) bloqueado(s), com todos os subdomínios.")
            : (true, $"{total} endereço(s) bloqueado(s) (só o endereço e o www: {erroDns}).");
    }

    /// <summary>
    /// Troca a regra de DNS (NRPT) do ControleSala pelos nomes dados; lista vazia só tira a regra. Null = deu certo.
    /// No teste com hosts falso (CONTROLESALA_HOSTS), não mexe no DNS do PC.
    /// </summary>
    internal static string? AplicarRegraDns(IReadOnlyCollection<string> nomes, string comentario = Sites.ComentarioRegraDns)
    {
        if (Environment.GetEnvironmentVariable("CONTROLESALA_HOSTS") != null) return "modo de teste";
        try
        {
            string script = Sites.ComandoRegraDns(nomes, comentario);
            string codificado = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            var inicio = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", codificado })
                inicio.ArgumentList.Add(a);
            using var p = Process.Start(inicio);
            if (p == null) return "o PowerShell não abriu";
            var saida = p.StandardOutput.ReadToEndAsync();
            var erro = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30000)) { try { p.Kill(); } catch (Exception) { } return "o Windows demorou demais"; }
            if (p.ExitCode == 0) return null;
            string texto = erro.Result.Trim();
            return texto.Length == 0 ? $"código {p.ExitCode}" : texto.Split('\n')[0].Trim();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or ArgumentException)
        {
            return ex.Message;
        }
    }

    internal static (bool, string) LiberarInternet()
    {
        // Libera tudo: tira a regra de firewall, a regra de DNS dos sites e a seção de sites do hosts.
        Netsh($"advfirewall firewall delete rule name=\"{RegraFirewall}\"");
        AplicarRegraDns([]);
        try
        {
            if (MudarHosts(atual => Sites.RemoverSecao(atual))) LimparCacheDns();
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

    internal static void LimparCacheDns()
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

    /// <summary>Argumentos do shutdown.exe: na hora (/t 0) e fechando o que estiver aberto (/f), para nada segurar o desligamento.</summary>
    internal static string ArgumentosDesligar(bool reiniciar) => $"{(reiniciar ? "/r" : "/s")} /f /t 0";

    static (bool, string) DesligarOuReiniciar(bool reiniciar)
    {
        // Na hora, sem contagem: o professor já confirmou no programa dele.
        string acao = reiniciar ? "reiniciar" : "desligar";
        var inicio = new ProcessStartInfo("shutdown.exe", ArgumentosDesligar(reiniciar))
        { UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(inicio);
        p?.WaitForExit(5000);
        if (p is { ExitCode: not 0 })
            return (false, $"O Windows recusou {acao} (código {p.ExitCode}).");
        return (true, reiniciar ? "Reiniciando agora." : "Desligando agora.");
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
