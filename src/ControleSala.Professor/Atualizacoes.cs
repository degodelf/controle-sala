using System.IO;
using System.Security.Cryptography;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Instalador com a assinatura do autor conferida, pronto para enviar aos alunos.</summary>
public sealed record InstaladorParaAlunos(string Caminho, string Versao, long Tamanho, byte[] Sha256);

/// <summary>Andamento da atualização de um computador: <see cref="Ok"/> preenchido quando aquele computador terminou.</summary>
internal sealed record ProgressoAtualizacao(ConexaoComputador Alvo, double Percentual, string Mensagem, bool? Ok);

/// <summary>
/// Atualização dos PCs dos alunos pelo professor. O instalador do professor guarda uma cópia de si mesmo
/// (<see cref="CaminhoPadrao"/>); ela é enviada pela conexão da sala e cada agente confere a assinatura antes de instalar.
/// </summary>
static class Atualizacoes
{
    const int ComputadoresAoMesmoTempo = 4;

    /// <summary>Cópia que o instalador guarda ao instalar (dentro de Arquivos de Programas; só leitura para o usuário comum).</summary>
    public static string CaminhoPadrao => Path.Combine(AppContext.BaseDirectory, "atualizacao", "ControleSala-Setup.exe");

    /// <summary>Onde o professor baixa o instalador novo do GitHub: pasta gravável pelo usuário (Arquivos de Programas não é).</summary>
    public static string CaminhoBaixado => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControleSala", "atualizacao", "ControleSala-Setup.exe");

    /// <summary>Agente online com versão conhecida e mais antiga que <paramref name="versao"/>.</summary>
    public static bool Desatualizado(ConexaoComputador c, string versao) =>
        c.Estado == EstadoConexao.Online && c.VersaoAgente is { } v && Versoes.MaisNova(versao, v);

    /// <summary>Confere a assinatura e calcula o SHA-256. Lança <see cref="ArgumentException"/> com a mensagem para o professor.</summary>
    public static async Task<InstaladorParaAlunos> PrepararAsync(string caminho, CancellationToken ct)
    {
        if (!File.Exists(caminho)) throw new ArgumentException("O instalador não foi encontrado.");
        string? versao = await Task.Run(() => AssinaturaInstalador.Verificar(caminho), ct);
        if (versao == null)
            throw new ArgumentException("Este instalador não tem a assinatura do autor do ControleSala (ou foi alterado). Use um instalador oficial.");
        byte[] hash;
        await using (var f = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
            hash = await SHA256.HashDataAsync(f, ct);
        return new InstaladorParaAlunos(caminho, versao, new FileInfo(caminho).Length, hash);
    }

    /// <summary>Envia o instalador a cada computador (alguns ao mesmo tempo). Devolve quantos aceitaram.</summary>
    public static async Task<int> EnviarAsync(InstaladorParaAlunos inst, IReadOnlyList<ConexaoComputador> alvos,
        IProgress<ProgressoAtualizacao>? progresso, CancellationToken ct)
    {
        int aceitos = 0;
        using var vagas = new SemaphoreSlim(ComputadoresAoMesmoTempo);

        async Task EnviarParaUm(ConexaoComputador alvo)
        {
            try
            {
                await vagas.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                progresso?.Report(new ProgressoAtualizacao(alvo, 0, "Cancelado", false));
                return;
            }
            try
            {
                var andamento = progresso == null ? null
                    : new Progress<long>(b => progresso.Report(new ProgressoAtualizacao(alvo, 100.0 * b / inst.Tamanho, $"Enviando... {100.0 * b / inst.Tamanho:0}%", null)));
                var (ok, msg) = await alvo.EnviarArquivoAsync(inst.Caminho, inst.Versao, inst.Tamanho, inst.Sha256, andamento, ct, atualizacao: true);
                if (ok) Interlocked.Increment(ref aceitos);
                progresso?.Report(new ProgressoAtualizacao(alvo, ok ? 100 : 0, msg, ok));
            }
            finally
            {
                vagas.Release();
            }
        }

        await Task.WhenAll(alvos.Select(EnviarParaUm));
        return aceitos;
    }
}
