using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>Executa a limpeza no PC do aluno: tela de aviso, apaga os arquivos, esvazia a lixeira e restaura o papel de parede.</summary>
static partial class LimpezaExecutor
{
    /// <summary>Pasta onde ficam os arquivos de configuração e as imagens enviadas pelo professor.</summary>
    public static string PastaDados => Path.GetDirectoryName(ConfigAgente.CaminhoPadrao)!;
    public static string CaminhoPapel => Path.Combine(PastaDados, "papel-de-parede.png");
    public static string CaminhoImagemLimpeza => Path.Combine(PastaDados, "imagem-limpeza.png");

    static bool EhSystem { get { try { return WindowsIdentity.GetCurrent().IsSystem; } catch { return false; } } }

    /// <summary>Pasta do perfil a limpar. Em teste, use a variável CONTROLESALA_PERFIL. Null = não dá para limpar com segurança.</summary>
    static string? Perfil()
    {
        var teste = Environment.GetEnvironmentVariable("CONTROLESALA_PERFIL");
        if (!string.IsNullOrEmpty(teste)) return teste;
        return EhSystem ? LancadorNaSessao.PerfilDaSessao() : null;
    }

    /// <summary>Por que a limpeza não pode rodar agora; null se pode.</summary>
    public static string? Impedimento()
    {
        string? perfil = Perfil();
        if (perfil == null) return "A limpeza só funciona pelo serviço (com um aluno usando o PC).";
        return Directory.Exists(perfil) ? null : "Não foi possível encontrar a pasta do aluno.";
    }

    public static (bool Ok, string Mensagem) Executar(LimpezaConfig c, bool automatica = false, string pedida = "")
    {
        var inicio = DateTimeOffset.Now;
        if (Impedimento() is { } motivo)
        {
            RelatoLimpeza.Registrar(new ResultadoLimpeza(inicio, DateTimeOffset.Now, automatica, pedida, false, 0, motivo));
            return (false, motivo);
        }
        string perfil = Perfil()!;

        bool temTela = InterfaceAgenteDisponivel();
        if (temTela) InterfaceAgente.NaInterface(() => JanelasAluno.MostrarLimpeza(
            File.Exists(CaminhoImagemLimpeza) ? CaminhoImagemLimpeza : null));
        try
        {
            if (c.Navegadores) FecharNavegadores();

            // A conta vem antes: depois de assumir a conta do aluno, o agente não pode mais perguntar isso ao Windows.
            string? conta = c.Lixeira ? ContaDaSessao() : null;
            var (itens, bytes) = NaContaDoAluno(() => ApagarArquivos(c, perfil, conta));

            if (c.RestaurarPapelDeParede && File.Exists(CaminhoPapel) && RestaurarPapel(CaminhoPapel) is { Ok: false } papel)
                Programa.Log($"Papel de parede não aplicado: {papel.Erro}");

            string extra = AgenteDefender.Iniciar(c);
            RelatoLimpeza.Registrar(new ResultadoLimpeza(inicio, DateTimeOffset.Now, automatica, pedida, true, bytes));
            return (true, $"Limpeza concluída ({itens} itens removidos).{extra}");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            string erro = $"Não foi possível usar a conta do aluno: {ex.Message}";
            RelatoLimpeza.Registrar(new ResultadoLimpeza(inicio, DateTimeOffset.Now, automatica, pedida, false, 0, erro));
            return (false, erro);
        }
        finally
        {
            if (temTela) InterfaceAgente.NaInterface(JanelasAluno.EsconderLimpeza);
        }
    }

    /// <summary>
    /// Tudo o que a limpeza apaga é do aluno, então é apagado com as permissões dele, e não como SYSTEM. Assim, mesmo que ele
    /// troque uma pasta por um atalho no meio da limpeza, nada que ele mesmo não pudesse apagar corre risco.
    /// </summary>
    static T NaContaDoAluno<T>(Func<T> acao) => EhSystem ? LancadorNaSessao.NaContaDaSessao(acao) : acao();

    static (int Itens, long Bytes) ApagarArquivos(LimpezaConfig c, string perfil, string? conta)
    {
        int itens = 0;
        long bytes = 0;
        foreach (var pasta in LimpezaArquivos.PastasParaEsvaziar(perfil, c))
        {
            long antes = LimpezaArquivos.Tamanho(pasta); // medido antes e depois: exceções e arquivos em uso não contam
            itens += LimpezaArquivos.EsvaziarPasta(pasta, c.Excecoes);
            bytes += Math.Max(0, antes - LimpezaArquivos.Tamanho(pasta));
        }

        if (c.Navegadores)
            foreach (var alvo in LimpezaArquivos.CaminhosNavegadores(perfil))
            {
                long antes = LimpezaArquivos.Tamanho(alvo);
                if (!LimpezaArquivos.Remover(alvo)) continue;
                itens++;
                bytes += antes;
            }

        if (conta != null)
        {
            var (removidos, liberados) = EsvaziarLixeira(conta);
            itens += removidos;
            bytes += liberados;
        }
        return (itens, bytes);
    }

    static string? ContaDaSessao() => EhSystem ? LancadorNaSessao.ContaDaSessao() : WindowsIdentity.GetCurrent().User?.Value;

    static bool InterfaceAgenteDisponivel()
    {
        try { InterfaceAgente.Iniciar(); return true; } catch (Exception) { return false; }
    }

    static void FecharNavegadores()
    {
        foreach (var nome in new[] { "chrome", "msedge", "firefox", "iexplore", "opera", "brave" })
            foreach (var p in Process.GetProcessesByName(nome))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch (Exception) { } finally { p.Dispose(); }
            }
        Thread.Sleep(500); // dá tempo de os arquivos destravarem
    }

    /// <summary>
    /// Esvazia a Lixeira de quem está usando o PC e devolve quantos itens e bytes saíram. Apaga direto a pasta da conta porque o
    /// SHEmptyRecycleBin, chamado pelo agente, só alcança a Lixeira do SYSTEM, nunca a do aluno.
    /// </summary>
    static (int Itens, long Bytes) EsvaziarLixeira(string sid)
    {
        var discos = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName);
        int itens = 0;
        long bytes = 0;
        foreach (var pasta in LimpezaArquivos.PastasDaLixeira(discos, sid))
        {
            long antes = LimpezaArquivos.Tamanho(pasta);
            itens += LimpezaArquivos.EsvaziarPasta(pasta, []);
            bytes += Math.Max(0, antes - LimpezaArquivos.Tamanho(pasta));
        }
        return (itens, bytes);
    }

    /// <summary>Aplica agora o papel de parede já recebido (se houver). Chamado pelo comando "aplicar papel de parede" do professor.</summary>
    public static (bool, string) AplicarPapelGuardado()
    {
        if (!File.Exists(CaminhoPapel)) return (false, "Nenhum papel de parede foi enviado a este computador.");
        var (ok, erro) = RestaurarPapel(CaminhoPapel);
        return ok ? (true, "Papel de parede aplicado.") : (false, $"Não foi possível aplicar o papel de parede: {erro}.");
    }

    /// <summary>Define o papel de parede. Como o agente é SYSTEM, abre um processo na conta do aluno para aplicar nele.</summary>
    static (bool Ok, string Erro) RestaurarPapel(string caminho)
    {
        if (!EhSystem) return (DefinirPapelDeParede(caminho), "o Windows recusou a imagem");
        try
        {
            ConfigAgente.PermitirLeitura(caminho); // a pasta é só de administradores, e quem aplica é a conta do aluno
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, ex.Message);
        }
        bool ok = LancadorNaSessao.RodarComoUsuario(Environment.ProcessPath!, $"--definir-papel \"{caminho}\"", out string erro);
        return (ok, erro);
    }

    /// <summary>Aplica o papel de parede na conta atual (chamado pelo modo --definir-papel, já rodando como o aluno).</summary>
    public static bool DefinirPapelDeParede(string caminho)
    {
        try
        {
            using (File.OpenRead(caminho)) { }   // o Windows responde "ok" mesmo quando esta conta não consegue ler a imagem
            return SystemParametersInfo(0x0014 /* SPI_SETDESKWALLPAPER */, 0, caminho, 0x01 | 0x02 /* atualiza e avisa */);
        }
        catch (Exception) { return false; }
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint acao, uint param, string valor, uint flags);
}
