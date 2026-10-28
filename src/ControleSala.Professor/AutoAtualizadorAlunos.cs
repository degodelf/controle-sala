using System.IO;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>
/// Empurra o instalador assinado aos alunos que estão desatualizados — inclusive os que estavam
/// desligados e voltam a ligar depois. Instala em silêncio (o agente roda como serviço) e só avisa
/// que fez. É a opção escolhida pelo professor: age sozinho, sem perguntar.
/// </summary>
sealed class AutoAtualizadorAlunos(Func<IReadOnlyList<ConexaoComputador>> conexoes, Action<string> avisar)
{
    // Depois de enviar, o agente some da rede ~1 min enquanto troca os arquivos; não reenvia nesse meio-tempo.
    static readonly TimeSpan EsperaReenvio = TimeSpan.FromMinutes(3);

    readonly HashSet<ConexaoComputador> _enviando = [];
    readonly Dictionary<ConexaoComputador, DateTime> _ultimoEnvio = [];
    readonly HashSet<ConexaoComputador> _aguardandoVoltar = [];
    InstaladorParaAlunos? _instalador;

    public bool Ativo => _instalador != null;
    public string? Versao => _instalador?.Versao;

    /// <summary>Liga o auto-update para esta versão e grava para continuar mesmo se o professor reiniciar.</summary>
    public void Ativar(InstaladorParaAlunos inst)
    {
        _instalador = inst;
        new AtualizacaoPendente(inst.Versao, inst.Caminho).Salvar();
    }

    /// <summary>Na abertura do programa: retoma um auto-update pendente (ex.: PCs que ficaram desligados).</summary>
    public async Task RetomarAsync()
    {
        if (AtualizacaoPendente.Carregar() is not { } p) return;
        try { _instalador = await Atualizacoes.PrepararAsync(p.Caminho, CancellationToken.None); }
        catch (ArgumentException) { AtualizacaoPendente.Limpar(); }   // sem assinatura/arquivo: desiste em silêncio
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Chamado de tempos em tempos: envia a quem está online e ainda desatualizado; avisa quem já voltou atualizado.</summary>
    public void Verificar()
    {
        if (_instalador is not { } inst) return;
        var atuais = conexoes();

        // Quem recebeu e já voltou com a versão nova: avisa e para de acompanhar.
        foreach (var c in _aguardandoVoltar.ToList())
        {
            if (!atuais.Contains(c)) { _aguardandoVoltar.Remove(c); continue; }
            if (c.Estado == EstadoConexao.Online && c.VersaoAgente == inst.Versao)
            {
                _aguardandoVoltar.Remove(c);
                _ultimoEnvio.Remove(c);
                avisar($"{c.Dados.Nome} atualizado para a versão {inst.Versao} ✓");
            }
        }

        foreach (var c in atuais)
        {
            if (_enviando.Contains(c)) continue;
            if (!Atualizacoes.Desatualizado(c, inst.Versao)) continue;
            if (_ultimoEnvio.TryGetValue(c, out var quando) && DateTime.UtcNow - quando < EsperaReenvio) continue;
            _ = EnviarAsync(inst, c);
        }
    }

    async Task EnviarAsync(InstaladorParaAlunos inst, ConexaoComputador c)
    {
        _enviando.Add(c);
        _ultimoEnvio[c] = DateTime.UtcNow;
        avisar($"Atualizando {c.Dados.Nome}...");
        try
        {
            int ok = await Atualizacoes.EnviarAsync(inst, [c], null, CancellationToken.None);
            if (ok > 0) _aguardandoVoltar.Add(c);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException) { }
        finally { _enviando.Remove(c); }
    }
}
