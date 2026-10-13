using System.Diagnostics;
using System.Security.Principal;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Atualização deste PC pelo professor: o instalador chega pela rede (canal da sala), é conferido — assinatura do autor
/// e versão mais nova que a instalada — e só então roda em silêncio. O instalador para o agente, troca os arquivos,
/// mantém a sala e religa o serviço; o PC volta para a grade do professor sozinho.
/// </summary>
static class AtualizacaoAgente
{
    public const string NomeArquivo = "ControleSala-Setup.exe";
    static int _instalando;

    static bool EhSystem { get { try { return WindowsIdentity.GetCurrent().IsSystem; } catch { return false; } } }

    /// <summary>Como serviço: pasta da sala (só administradores e SYSTEM). Em teste, sem administrador: pasta temporária.</summary>
    public static string Pasta => EhSystem
        ? Path.Combine(LimpezaExecutor.PastaDados, "atualizacao")
        : Path.Combine(Path.GetTempPath(), "ControleSala-atualizacao");

    public static RecebedorArquivos NovoRecebedor()
    {
        LimparRestos();
        return new RecebedorArquivos(Pasta, conferirTipo: false);
    }

    /// <summary>Apaga temporários e o instalador de uma atualização anterior (se ele já terminou de rodar).</summary>
    public static void LimparRestos()
    {
        RecebedorArquivos.LimparRestos(Pasta, TimeSpan.Zero);
        try { File.Delete(Path.Combine(Pasta, NomeArquivo)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* ainda rodando: fica para a próxima */ }
    }

    /// <summary>O instalador chegou inteiro (tamanho e SHA-256 conferidos). Confere a assinatura e a versão e instala.</summary>
    public static (bool Ok, string Mensagem) Instalar(string caminho, string? professor, Action<string> aviso)
    {
        string? versao = AssinaturaInstalador.Verificar(caminho);
        if (versao == null)
        {
            Apagar(caminho);
            return (false, "Recusado: o instalador não tem a assinatura do autor do ControleSala (ou foi alterado).");
        }
        if (!Versoes.MaisNova(versao, Versoes.Atual))
        {
            Apagar(caminho);
            return (false, $"Este computador já está na versão {Versoes.Atual} (o instalador é da {versao}).");
        }
        if (!EhSystem)
        {
            Apagar(caminho);
            return (true, $"Instalador da versão {versao} conferido (modo de teste, sem o serviço: não instala).");
        }
        if (Interlocked.Exchange(ref _instalando, 1) == 1)
            return (false, "Já há uma atualização sendo instalada neste computador.");

        aviso($"Atualização para a versão {versao} enviada por {professor ?? "professor"}: instalando.");
        string log = Path.Combine(Pasta, "atualizacao.log");
        // Espera a resposta chegar ao professor antes: o instalador encerra este agente logo no começo.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            try
            {
                Process.Start(new ProcessStartInfo(caminho, $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=\"{log}\"")
                    { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                aviso($"Não foi possível iniciar o instalador: {ex.Message}");
                Interlocked.Exchange(ref _instalando, 0);
            }
        });
        return (true, $"Instalando a versão {versao}. O computador volta para a grade em cerca de 1 minuto.");
    }

    static void Apagar(string caminho)
    {
        try { File.Delete(caminho); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
