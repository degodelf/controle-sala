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

    public static (bool Ok, string Mensagem) Executar(LimpezaConfig c)
    {
        if (Impedimento() is { } motivo) return (false, motivo);
        string perfil = Perfil()!;

        bool temTela = InterfaceAgenteDisponivel();
        if (temTela) InterfaceAgente.NaInterface(() => JanelasAluno.MostrarLimpeza(
            File.Exists(CaminhoImagemLimpeza) ? CaminhoImagemLimpeza : null));
        try
        {
            if (c.Navegadores) FecharNavegadores();

            int itens = 0;
            foreach (var pasta in LimpezaArquivos.PastasParaEsvaziar(perfil, c))
                itens += LimpezaArquivos.EsvaziarPasta(pasta, c.Excecoes);

            if (c.Navegadores)
                foreach (var alvo in LimpezaArquivos.CaminhosNavegadores(perfil))
                    if (LimpezaArquivos.Remover(alvo)) itens++;

            if (c.Lixeira) EsvaziarLixeira();
            if (c.RestaurarPapelDeParede && File.Exists(CaminhoPapel)) RestaurarPapel(CaminhoPapel);

            string extra = AgenteDefender.Iniciar(c);
            return (true, $"Limpeza concluída ({itens} itens removidos).{extra}");
        }
        finally
        {
            if (temTela) InterfaceAgente.NaInterface(JanelasAluno.EsconderLimpeza);
        }
    }

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

    static void EsvaziarLixeira()
    {
        try { SHEmptyRecycleBin(0, null, 0x7 /* sem confirmação, som, progresso */); }
        catch (Exception) { }
    }

    /// <summary>Aplica agora o papel de parede já recebido (se houver). Chamado pelo comando "aplicar papel de parede" do professor.</summary>
    public static (bool, string) AplicarPapelGuardado()
    {
        if (!File.Exists(CaminhoPapel)) return (false, "Nenhum papel de parede foi enviado a este computador.");
        RestaurarPapel(CaminhoPapel);
        return (true, "Papel de parede aplicado.");
    }

    /// <summary>Define o papel de parede. Como o agente é SYSTEM, abre um processo na conta do aluno para aplicar nele.</summary>
    static void RestaurarPapel(string caminho)
    {
        if (EhSystem)
        {
            LancadorNaSessao.IniciarComoUsuario(Environment.ProcessPath!, $"--definir-papel \"{caminho}\"", out _);
        }
        else
        {
            DefinirPapelDeParede(caminho);
        }
    }

    /// <summary>Aplica o papel de parede na conta atual (chamado pelo modo --definir-papel, já rodando como o aluno).</summary>
    public static void DefinirPapelDeParede(string caminho)
    {
        try { SystemParametersInfo(0x0014 /* SPI_SETDESKWALLPAPER */, 0, caminho, 0x01 | 0x02 /* atualiza e avisa */); }
        catch (Exception) { }
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBin(nint janela, string? raiz, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint acao, uint param, string valor, uint flags);
}
