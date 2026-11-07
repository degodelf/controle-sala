using System.Security.Principal;
using System.Text.Json;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Faz a limpeza agendada rodar no próprio agente em sessão (que já é SYSTEM na sessão do aluno). De tempos em tempos
/// checa se está na hora; se estiver, mostra o aviso ao aluno (com "Adiar 1 hora") e, se seguir, faz a limpeza com a
/// tela de carregamento. Assim o aviso e o carregamento aparecem de verdade (a Tarefa do Windows rodava invisível).
/// </summary>
sealed class AgendadorLimpeza : IDisposable
{
    const int SegundosAviso = 60;
    static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(30);
    static string CaminhoEstado => Path.Combine(LimpezaExecutor.PastaDados, "agenda-estado.json");

    readonly System.Threading.Timer _relogio;
    readonly Lock _trava = new();
    bool _ocupado;
    bool _encerrado;

    sealed class Estado
    {
        public DateTime? UltimaExecucao { get; set; }
        public DateTime? AdiadoPara { get; set; }
    }

    AgendadorLimpeza() => _relogio = new System.Threading.Timer(_ => Verificar(), null, Intervalo, Intervalo);

    /// <summary>Liga o agendador. Só faz sentido no agente em sessão rodando como SYSTEM (o serviço).</summary>
    public static AgendadorLimpeza? Iniciar()
    {
        try { if (!WindowsIdentity.GetCurrent().IsSystem) return null; } catch { return null; }
        // Remove uma Tarefa do Windows antiga de versões anteriores (o agendamento agora é feito aqui, não pelo schtasks).
        try { ConfigLimpezaAgente.RemoverAgendamento(); } catch (Exception) { }
        return new AgendadorLimpeza();
    }

    void Verificar()
    {
        lock (_trava) { if (_ocupado || _encerrado) return; _ocupado = true; }
        try
        {
            string caminhoConfig = Path.Combine(LimpezaExecutor.PastaDados, "limpeza.json");
            var config = LimpezaConfig.Carregar(caminhoConfig);
            var estado = LerEstado();
            if (!config.Agendada && estado.AdiadoPara != null)
            {
                estado.AdiadoPara = null; // pausada: esquece o adiamento, senão limparia fora de hora ao retomar
                SalvarEstado(estado);
            }
            if (!AgendaLimpeza.DeveRodar(config, DateTime.Now, estado.UltimaExecucao, estado.AdiadoPara)) return;
            if (LancadorNaSessao.SessaoAtiva() == uint.MaxValue) return; // ninguém usando o PC
            if (!LimpezaExecutor.Trava.TentarPegar()) return; // "Limpar agora" em andamento: confere de novo no próximo tique
            try { Limpar(caminhoConfig); }
            finally { LimpezaExecutor.Trava.Soltar(); }
        }
        catch (Exception) { /* nunca derruba o agente por causa do agendamento */ }
        finally { lock (_trava) _ocupado = false; }
    }

    /// <summary>Avisa o aluno e limpa. Chamado com a trava: um "Limpar agora" pode ter acabado de terminar, então confere tudo de novo.</summary>
    static void Limpar(string caminhoConfig)
    {
        var config = LimpezaConfig.Carregar(caminhoConfig);
        var estado = LerEstado();
        if (!AgendaLimpeza.DeveRodar(config, DateTime.Now, estado.UltimaExecucao, estado.AdiadoPara)) return;

        var escolha = JanelasAluno.PerguntarLimpeza(SegundosAviso,
            File.Exists(LimpezaExecutor.CaminhoImagemLimpeza) ? LimpezaExecutor.CaminhoImagemLimpeza : null);

        config = LimpezaConfig.Carregar(caminhoConfig); // o professor pode ter pausado com o aviso na tela
        if (!config.Agendada) return;

        if (escolha == ResultadoAviso.Adiar)
        {
            estado.AdiadoPara = DateTime.Now.AddHours(1);
            SalvarEstado(estado);
            return;
        }

        LimpezaExecutor.Executar(config, automatica: true); // mostra a tela de carregamento e limpa
        MarcarLimpezaFeita();
    }

    /// <summary>Esta limpeza vale pela agendada ou adiada daquele horário: o aluno não passa por outra logo em seguida.</summary>
    public static void MarcarLimpezaFeita()
    {
        var estado = LerEstado();
        estado.UltimaExecucao = DateTime.Now;
        estado.AdiadoPara = null;
        SalvarEstado(estado);
    }

    static Estado LerEstado()
    {
        try
        {
            if (File.Exists(CaminhoEstado))
                return JsonSerializer.Deserialize<Estado>(File.ReadAllText(CaminhoEstado)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        return new();
    }

    static void SalvarEstado(Estado e)
    {
        try
        {
            Directory.CreateDirectory(LimpezaExecutor.PastaDados);
            File.WriteAllText(CaminhoEstado, JsonSerializer.Serialize(e));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        lock (_trava) _encerrado = true;
        _relogio.Dispose();
    }
}
