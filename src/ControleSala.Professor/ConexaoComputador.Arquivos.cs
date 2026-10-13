using System.IO;
using System.Threading.Channels;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Envio de arquivos (separado do resto porque usa async, que não pode ficar em código "unsafe").</summary>
sealed partial class ConexaoComputador
{
    /// <summary>Envia uma mensagem e diz se deu certo (ao contrário de <see cref="Enviar"/>, que ignora falhas).</summary>
    bool TentarEnviar(byte[] mensagem)
    {
        var fluxo = _fluxo;
        if (fluxo == null) return false;
        try
        {
            lock (_escrita) fluxo.Write(mensagem);
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Envia um comando ao agente e espera o resultado (ou um tempo-limite). Usado pela janela de comandos.</summary>
    public async Task<(bool Ok, string Mensagem)> EnviarComandoAsync(AcaoComando acao, string argumento, CancellationToken ct)
    {
        if (Estado != EstadoConexao.Online)
            return (false, "computador offline");
        uint id = (uint)Interlocked.Increment(ref _proximoIdComando);
        var canal = Channel.CreateUnbounded<RespostaComando>();
        _comandos[id] = canal;
        try
        {
            if (!TentarEnviar(Protocolo.MontarComando(id, acao, argumento)))
                return (false, "sem conexão com o computador");
            using var tempo = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tempo.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var r = await canal.Reader.ReadAsync(tempo.Token);
                return (r.Ok, r.Mensagem);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (false, "o computador não respondeu");
            }
        }
        catch (OperationCanceledException)
        {
            return (false, "cancelado");
        }
        finally
        {
            _comandos.TryRemove(id, out _);
        }
    }

    sealed class EnvioFalhouException(string mensagem) : Exception(mensagem);

    /// <summary>
    /// Envia um arquivo a este computador: pede para começar, manda em blocos de 64 KB, manda o SHA-256 e espera o agente
    /// conferir. O arquivo só aparece para o aluno depois de conferido.
    /// Com <paramref name="atualizacao"/>, o arquivo é um instalador assinado e <paramref name="nome"/> é a versão: o agente
    /// confere a assinatura e se atualiza.
    /// </summary>
    public async Task<(bool Ok, string Mensagem)> EnviarArquivoAsync(string caminho, string nome, long tamanho, byte[] sha256,
        IProgress<long>? progresso, CancellationToken ct, bool atualizacao = false)
    {
        uint id = (uint)Interlocked.Increment(ref _proximoIdArquivo);
        var canal = Channel.CreateUnbounded<RespostaArquivo>();
        _transferencias[id] = canal;
        try
        {
            var inicio = atualizacao ? Protocolo.MontarAtualizacaoInicio(id, tamanho, nome) : Protocolo.MontarArquivoInicio(id, tamanho, nome);
            if (!TentarEnviar(inicio))
                throw new EnvioFalhouException("sem conexão com o computador");
            var aceito = await EsperarResposta(canal, TimeSpan.FromSeconds(15), ct)
                ?? throw new EnvioFalhouException("o computador não respondeu");
            if (aceito.Estado == EstadoArquivo.Erro) throw new EnvioFalhouException(aceito.Mensagem);

            await Task.Run(async () =>
            {
                await using var arquivo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
                var bloco = new byte[Protocolo.BlocoArquivo];
                long enviados = 0;
                int n;
                while ((n = await arquivo.ReadAsync(bloco, ct)) > 0)
                {
                    // Se o agente já recusou (disco cheio, por exemplo), para de mandar.
                    if (canal.Reader.TryRead(out var r) && r.Estado == EstadoArquivo.Erro)
                        throw new EnvioFalhouException(r.Mensagem);
                    if (!TentarEnviar(Protocolo.MontarArquivoDados(id, bloco.AsSpan(0, n))))
                        throw new EnvioFalhouException("a conexão caiu durante o envio");
                    enviados += n;
                    progresso?.Report(enviados);
                }
                if (enviados != tamanho) throw new EnvioFalhouException("o arquivo foi alterado durante o envio");
            }, ct);

            if (!TentarEnviar(Protocolo.MontarArquivoFim(id, sha256)))
                throw new EnvioFalhouException("a conexão caiu no fim do envio");
            var fim = await EsperarResposta(canal, TimeSpan.FromSeconds(120), ct)
                ?? throw new EnvioFalhouException("o computador não confirmou o recebimento");
            return fim.Estado == EstadoArquivo.Concluido ? (true, fim.Mensagem) : (false, fim.Mensagem);
        }
        catch (EnvioFalhouException ex)
        {
            TentarEnviar(Protocolo.MontarArquivoCancelar(id));
            return (false, ex.Message);
        }
        catch (OperationCanceledException)
        {
            TentarEnviar(Protocolo.MontarArquivoCancelar(id));
            return (false, "envio cancelado");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TentarEnviar(Protocolo.MontarArquivoCancelar(id));
            return (false, $"não foi possível ler o arquivo: {ex.Message}");
        }
        finally
        {
            _transferencias.TryRemove(id, out _);
        }
    }

    /// <summary>Próxima resposta do agente, ou null se passar o tempo. Cancelamento de verdade (<paramref name="ct"/>) é repassado.</summary>
    static async Task<RespostaArquivo?> EsperarResposta(Channel<RespostaArquivo> canal, TimeSpan limite, CancellationToken ct)
    {
        using var tempo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        tempo.CancelAfter(limite);
        try
        {
            return await canal.Reader.ReadAsync(tempo.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
