using System.Collections.Concurrent;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>O que o professor mandou desligar ou reiniciar (a grade mostra "Desligado" em vez da última imagem).</summary>
public enum Desligamento { Nenhum, Desligando, Reiniciando }

/// <summary>
/// O que a grade mostra da conexão (com carência para quedas curtas), o desligamento pedido pelo professor e o estado que
/// ele quer manter no PC (tela bloqueada, internet bloqueada, som mudo), reaplicado sozinho quando o PC reconecta.
/// </summary>
sealed partial class ConexaoComputador
{
    /// <summary>Quanto um comando espera um PC que está reconectando voltar, antes de desistir.</summary>
    static readonly TimeSpan EsperaPorReconexao = TimeSpan.FromSeconds(10);
    /// <summary>Pediu para desligar e o PC continuou ligado esse tempo todo: o Windows não desligou, volta ao normal.</summary>
    const long DesligamentoExpiraMs = 90_000;

    long _caiuEm;        // TickCount64 da queda de uma conexão que estava aberta; 0 = nenhuma queda pendente
    int _geracao;        // quantas conexões já foram abertas (muda a cada reconexão)
    int _desligamento;   // Desligamento
    int _geracaoDoDesligamento;
    long _desligamentoEm;

    // Estado que o professor quer manter neste PC, por assunto ("tela", "internet", "som"). Só os restritivos ficam guardados.
    readonly ConcurrentDictionary<string, (AcaoComando Acao, string Argumento)> _desejado = new();

    /// <summary>Estado para mostrar ao professor: uma queda de poucos segundos não aparece (o PC reconecta sozinho).</summary>
    public EstadoConexao EstadoVisivel
    {
        get
        {
            var real = _estado;
            if (real == EstadoConexao.Online || DesligamentoAtual != Desligamento.Nenhum) return real;
            return RegrasDeComando.NaCarencia(Volatile.Read(ref _caiuEm), Environment.TickCount64) ? EstadoConexao.Online : real;
        }
    }

    /// <summary>Desligar/reiniciar pedido pelo professor, até o PC voltar (ou ficar claro que não desligou).</summary>
    public Desligamento DesligamentoAtual
    {
        get
        {
            var d = (Desligamento)Volatile.Read(ref _desligamento);
            if (d == Desligamento.Nenhum) return d;
            bool online = _estado == EstadoConexao.Online;
            bool mesmaConexao = Volatile.Read(ref _geracao) == _geracaoDoDesligamento;
            bool voltou = online && !mesmaConexao;
            bool naoDesligou = online && mesmaConexao && Environment.TickCount64 - Volatile.Read(ref _desligamentoEm) > DesligamentoExpiraMs;
            if (!voltou && !naoDesligou) return d;
            Volatile.Write(ref _desligamento, (int)Desligamento.Nenhum);
            return Desligamento.Nenhum;
        }
    }

    void MarcarDesligamento(bool reiniciar)
    {
        _geracaoDoDesligamento = Volatile.Read(ref _geracao);
        Volatile.Write(ref _desligamentoEm, Environment.TickCount64);
        Volatile.Write(ref _desligamento, (int)(reiniciar ? Desligamento.Reiniciando : Desligamento.Desligando));
    }

    void LembrarEstado(AcaoComando acao, string argumento)
    {
        if (RegrasDeComando.AssuntoDoEstado(acao) is not { } assunto) return;
        if (RegrasDeComando.Restritivo(acao, argumento)) _desejado[assunto] = (acao, argumento);
        else _desejado.TryRemove(assunto, out _);
    }

    /// <summary>O que este PC deve continuar tendo (para a tela de testes e para reaplicar).</summary>
    internal IReadOnlyCollection<(AcaoComando Acao, string Argumento)> EstadoDesejado => [.. _desejado.Values];

    /// <summary>
    /// Depois de reconectar: o agente pode ter reiniciado (a tela bloqueada some) ou o PC pode ter religado.
    /// Reaplica o que o professor deixou valendo nesta sessão do programa.
    /// </summary>
    void ReaplicarEstadoDesejado()
    {
        var pendentes = EstadoDesejado;
        if (pendentes.Count == 0) return;
        _ = Task.Run(async () =>
        {
            foreach (var (acao, argumento) in pendentes)
            {
                var (ok, mensagem) = await ExecutarComandoAsync(acao, argumento, _cancelar.Token);
                RegistroConexoes.Anotar(Dados, $"reaplicado depois de reconectar: {RegistroAtividades.NomeDoComando(acao)} — {(ok ? "ok" : mensagem)}");
            }
        });
    }

    /// <summary>Espera o PC estar online (no máximo <paramref name="limite"/>). Volta na hora se já estiver.</summary>
    async Task<bool> EsperarOnlineAsync(TimeSpan limite, CancellationToken ct)
    {
        long fim = Environment.TickCount64 + (long)limite.TotalMilliseconds;
        while (!(_estado == EstadoConexao.Online && _fluxo != null))
        {
            if (Environment.TickCount64 >= fim || ct.IsCancellationRequested || _cancelar.IsCancellationRequested) return false;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
        return true;
    }
}
