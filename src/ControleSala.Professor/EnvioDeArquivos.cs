using System.IO;
using System.Security.Cryptography;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Um arquivo já conferido (nome aceito, tamanho dentro do limite) e com o SHA-256 calculado.</summary>
public sealed record ArquivoParaEnviar(string Caminho, string Nome, long Tamanho, byte[] Sha256);

/// <summary>Andamento do envio de um arquivo para um computador. <see cref="Final"/> vem preenchido quando aquele computador terminou tudo.</summary>
internal sealed record ProgressoEnvio(ConexaoComputador Alvo, int ArquivoAtual, int TotalDeArquivos, long Bytes, long TamanhoDoArquivo,
    string? Final, bool Sucesso);

/// <summary>Resultado do envio de um arquivo para um computador (usado pela linha de comando e pelos testes).</summary>
public sealed record ResultadoArquivoEnviado(string Computador, string Arquivo, bool Ok, string Mensagem);

static class EnvioDeArquivos
{
    const int ComputadoresAoMesmoTempo = 6;

    /// <summary>
    /// Confere os arquivos (existem, nome aceito, tamanho dentro do limite) e calcula o SHA-256 de cada um.
    /// Lança <see cref="ArgumentException"/> com a mensagem para o professor se algum não puder ser enviado.
    /// </summary>
    public static async Task<List<ArquivoParaEnviar>> PrepararAsync(IEnumerable<string> caminhos, CancellationToken ct)
    {
        var lista = new List<ArquivoParaEnviar>();
        foreach (string caminho in caminhos)
        {
            var info = new FileInfo(caminho);
            if (!info.Exists) throw new ArgumentException($"O arquivo \"{caminho}\" não foi encontrado.");
            if (NomesDeArquivo.Validar(info.Name) is { } erro) throw new ArgumentException($"\"{info.Name}\": {erro}");
            if (info.Length > RecebedorArquivos.TamanhoMaximo)
                throw new ArgumentException($"\"{info.Name}\" é grande demais (máximo {RecebedorArquivos.TamanhoMaximo / (1024 * 1024 * 1024)} GB).");

            byte[] hash;
            await using (var f = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
                hash = await SHA256.HashDataAsync(f, ct);
            lista.Add(new ArquivoParaEnviar(info.FullName, info.Name, info.Length, hash));
        }
        return lista;
    }

    /// <summary>Envia todos os arquivos para todos os computadores (vários computadores ao mesmo tempo; os arquivos de cada um em fila).</summary>
    public static async Task<List<ResultadoArquivoEnviado>> EnviarAsync(IReadOnlyList<ArquivoParaEnviar> arquivos,
        IReadOnlyList<ConexaoComputador> alvos, IProgress<ProgressoEnvio>? progresso, CancellationToken ct)
    {
        var resultados = new List<ResultadoArquivoEnviado>();
        using var vagas = new SemaphoreSlim(ComputadoresAoMesmoTempo);

        async Task EnviarParaUm(ConexaoComputador alvo)
        {
            await vagas.WaitAsync(ct);
            try
            {
                string nomeAlvo = alvo.Dados.Nome;
                int falhas = 0;
                for (int i = 0; i < arquivos.Count; i++)
                {
                    var arq = arquivos[i];
                    int indice = i + 1;
                    var andamento = progresso == null ? null
                        : new Progress<long>(b => progresso.Report(new ProgressoEnvio(alvo, indice, arquivos.Count, b, arq.Tamanho, null, true)));
                    var (ok, msg) = await alvo.EnviarArquivoAsync(arq.Caminho, arq.Nome, arq.Tamanho, arq.Sha256, andamento, ct);
                    lock (resultados) resultados.Add(new ResultadoArquivoEnviado(nomeAlvo, arq.Nome, ok, msg));
                    if (!ok)
                    {
                        falhas++;
                        progresso?.Report(new ProgressoEnvio(alvo, indice, arquivos.Count, 0, arq.Tamanho, $"Erro em {arq.Nome}: {msg}", false));
                        if (ct.IsCancellationRequested || alvo.Estado != EstadoConexao.Online) break; // sem sentido insistir
                    }
                }
                if (falhas == 0)
                    progresso?.Report(new ProgressoEnvio(alvo, arquivos.Count, arquivos.Count, 0, 0,
                        arquivos.Count == 1 ? "Enviado" : $"{arquivos.Count} arquivos enviados", true));
            }
            finally
            {
                vagas.Release();
            }
        }

        try
        {
            await Task.WhenAll(alvos.Select(EnviarParaUm));
        }
        catch (OperationCanceledException)
        {
            // cancelado pelo professor: os resultados parciais já estão na lista
        }
        return resultados;
    }
}
