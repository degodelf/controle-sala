using System.Diagnostics;
using System.Text.Json;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Botão "Jogos" do professor, no PC do aluno: bloqueia os sites de jogos (<see cref="CatalogoJogos"/>) numa seção própria do
/// hosts e numa regra de DNS própria, derruba a conexão aberta com eles, fecha os jogos instalados e não deixa abrir de novo.
/// A situação fica guardada neste PC: continua depois de reiniciar, a liberação por tempo volta a bloquear sozinha (mesmo
/// com o professor fechado) e, se o professor quiser, o PC já liga com os jogos bloqueados.
/// </summary>
static class JogosAgente
{
    /// <summary>O que fica guardado no PC do aluno.</summary>
    sealed class Situacao
    {
        public bool Usado { get; set; }
        public bool Bloqueados { get; set; }
        public DateTime? LiberadosAteUtc { get; set; }
        public bool FecharNavegador { get; set; }
        public bool BloquearAoLigar { get; set; }
        public DateTime? UltimoLogonUtc { get; set; }
        public List<string> Aprendidos { get; set; } = [];
    }

    const int MaximoAprendidos = 500;

    static readonly object _trava = new();
    static readonly object _travaRegra = new(); // um PowerShell da regra de DNS por vez (professor, tempo e entrada no Windows)
    static Situacao _s = new();
    static ServidorVideo? _servidor;
    static System.Threading.Timer? _vigiaProgramas, _fimDaLiberacao, _vigiaLogon;
    static readonly HashSet<string> _vistos = new(StringComparer.Ordinal);
    static readonly HashSet<string> _jaAvisados = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> _programas = new(CatalogoJogos.Programas, StringComparer.OrdinalIgnoreCase);

    static string Caminho => Path.Combine(Path.GetDirectoryName(ConfigAgente.CaminhoPadrao)!, "jogos.json");
    static string Marcador => Path.Combine(Path.GetDirectoryName(ConfigAgente.CaminhoPadrao)!, "jogos-dns.txt");

    public static bool Bloqueados { get { lock (_trava) return _s.Bloqueados; } }

    /// <summary>Ao subir o agente (no PC do aluno): retoma a situação guardada e avisa o professor quando ela mudar.</summary>
    public static void Iniciar(ServidorVideo servidor)
    {
        _servidor = servidor;
        servidor.EstadoJogos = () => Protocolo.MontarEstadoJogos(EstadoAtual());
        bool bloquear;
        lock (_trava)
        {
            _s = Carregar();
            bool venceu = !_s.Bloqueados && _s.LiberadosAteUtc is { } ate && ate <= DateTime.UtcNow;
            bloquear = _s.Bloqueados || venceu || (EntrouAgora() && _s.BloquearAoLigar);
            Salvar();
        }
        if (bloquear) Bloquear(fecharNavegador: false, motivo: "retomado");
        else AgendarFimDaLiberacao();
        // "Ao ligar o PC" = quando alguém entra no Windows (o agente sobe antes disso, ainda na tela de login).
        _vigiaLogon ??= new System.Threading.Timer(_ => ConferirLogon(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>Alguém entrou no Windows desde a última vez que olhamos? Guarda o horário (chamar com a trava).</summary>
    static bool EntrouAgora()
    {
        if (LancadorNaSessao.HorarioDoLogon() is not { } logon) return false;
        bool novo = _s.UltimoLogonUtc is not { } antes || Math.Abs((logon - antes).TotalSeconds) > 1;
        _s.UltimoLogonUtc = logon;
        return novo;
    }

    /// <summary>Com a opção "bloquear ao ligar", cada entrada no Windows começa com os jogos bloqueados.</summary>
    static void ConferirLogon()
    {
        try
        {
            bool bloquear;
            lock (_trava)
            {
                bool entrou = EntrouAgora();
                if (entrou) Salvar();
                bloquear = entrou && _s.BloquearAoLigar && !_s.Bloqueados;
            }
            if (bloquear) Bloquear(fecharNavegador: false, motivo: "entrou");
        }
        catch (Exception ex)
        {
            Programa.Log($"Jogos: {ex.Message}");
        }
    }

    public static EstadoJogos EstadoAtual()
    {
        lock (_trava)
        {
            if (!_s.Usado) return new(SituacaoJogos.Nunca);
            if (_s.Bloqueados) return new(SituacaoJogos.Bloqueados);
            return _s.LiberadosAteUtc is { } ate
                ? new(SituacaoJogos.Liberados, (int)Math.Ceiling(Math.Max(0, (ate - DateTime.UtcNow).TotalSeconds)))
                : new(SituacaoJogos.Liberados);
        }
    }

    /// <summary>Comando "Bloquear jogos" do professor.</summary>
    public static (bool, string) ComandoBloquear(string argumento)
    {
        var (navegador, _, _) = ArgumentosJogos.Ler(argumento);
        string? erro = Bloquear(navegador, motivo: "professor");
        return erro is null
            ? (true, navegador ? "Jogos bloqueados e navegador fechado." : "Jogos bloqueados.")
            : (true, $"Jogos bloqueados (só pelo hosts: {erro}).");
    }

    /// <summary>Comando "Liberar jogos" do professor (de vez ou por alguns minutos).</summary>
    public static (bool, string) ComandoLiberar(string argumento)
    {
        int minutos = ArgumentosJogos.Ler(argumento).Minutos;
        lock (_trava)
        {
            _s.Usado = true;
            _s.Bloqueados = false;
            _s.LiberadosAteUtc = minutos > 0 ? DateTime.UtcNow.AddMinutes(minutos) : null;
            Salvar();
        }
        PararVigiaDeProgramas();
        string? erro = TirarBloqueio();
        AgendarFimDaLiberacao();
        Avisar();
        string texto = minutos > 0 ? $"Jogos liberados por {minutos} min." : "Jogos liberados.";
        return (true, erro is null ? texto : $"{texto} (aviso: {erro})");
    }

    /// <summary>Configuração do professor que vale mesmo sem comando: ligar o PC já com os jogos bloqueados.</summary>
    public static void ReceberConfig(string config)
    {
        if (ArgumentosJogos.Ler(config).BloquearAoLigar is not { } ligar) return;
        lock (_trava)
        {
            if (_s.BloquearAoLigar == ligar) return;
            _s.BloquearAoLigar = ligar;
            if (ligar) _s.Usado = true;
            Salvar();
        }
        Avisar();
    }

    /// <summary>Bloqueia. Null = tudo certo; senão, por que a regra de DNS não foi aplicada (o hosts continua valendo).</summary>
    static string? Bloquear(bool fecharNavegador, string motivo)
    {
        List<string> aprendidos;
        lock (_trava)
        {
            _s.Usado = true;
            _s.Bloqueados = true;
            _s.LiberadosAteUtc = null;
            if (motivo == "professor") _s.FecharNavegador = fecharNavegador;
            fecharNavegador = fecharNavegador || (motivo == "tempo" && _s.FecharNavegador);
            aprendidos = [.. _s.Aprendidos];
            Salvar();
            _vistos.Clear();     // olha de novo o que já tinha sido aberto antes do bloqueio
            _jaAvisados.Clear();
        }
        _fimDaLiberacao?.Dispose();
        _fimDaLiberacao = null;

        // Os endereços dos jogos abertos agora, antes de bloquear (depois os nomes não resolvem mais).
        var abertos = CacheDns.Nomes(forcar: true)?.Where(n => CatalogoJogos.EhJogo(n, aprendidos)).ToList() ?? [];
        var enderecos = CacheDns.Enderecos(abertos);

        string? erro = AplicarBloqueio(aprendidos);
        FecharJogosAbertos();
        if (fecharNavegador) LimpezaExecutor.FecharNavegadores();
        ConexoesTcp.DerrubarPorUmTempo(enderecos);
        IniciarVigiaDeProgramas();
        Avisar();
        if (motivo != "professor")
            Programa.Log($"Jogos bloqueados ({motivo switch { "tempo" => "acabou o tempo liberado", "entrou" => "aluno entrou no Windows", _ => "situação guardada" }}).");
        return erro;
    }

    /// <summary>Seção no hosts (vale na hora) e regra de DNS (pega todos os subdomínios; mais lenta, vem depois).</summary>
    static string? AplicarBloqueio(List<string> aprendidos)
    {
        lock (_travaRegra) return AplicarBloqueioSemTrava(aprendidos);
    }

    static string? AplicarBloqueioSemTrava(List<string> aprendidos)
    {
        var dominios = CatalogoJogos.Dominios.Concat(aprendidos).Distinct(StringComparer.Ordinal).ToList();
        try
        {
            if (Comandos.MudarHosts(atual => Sites.MontarHosts(atual, dominios, CatalogoJogos.InicioHosts, CatalogoJogos.FimHosts)))
                Comandos.LimparCacheDns();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Programa.Log($"Jogos: não foi possível mexer no hosts ({ex.Message}).");
        }

        string assinatura = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", dominios))))[..16];
        string? feito = File.Exists(Marcador) ? File.ReadAllText(Marcador).Trim() : null;
        if (feito == assinatura) return null;
        string? erro = Comandos.AplicarRegraDns(Sites.NomesDaRegraDns(dominios, []), CatalogoJogos.ComentarioRegraDns);
        if (erro is null) File.WriteAllText(Marcador, assinatura);
        Comandos.LimparCacheDns();
        return erro;
    }

    static string? TirarBloqueio()
    {
        lock (_travaRegra) return TirarBloqueioSemTrava();
    }

    static string? TirarBloqueioSemTrava()
    {
        try
        {
            Comandos.MudarHosts(atual => Sites.RemoverSecao(atual, CatalogoJogos.InicioHosts, CatalogoJogos.FimHosts));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Programa.Log($"Jogos: não foi possível mexer no hosts ({ex.Message}).");
        }
        string? erro = Comandos.AplicarRegraDns([], CatalogoJogos.ComentarioRegraDns);
        try { if (File.Exists(Marcador)) File.Delete(Marcador); } catch (IOException) { }
        Comandos.LimparCacheDns();
        return erro;
    }

    // ---------- jogos instalados ----------

    static void IniciarVigiaDeProgramas() =>
        _vigiaProgramas ??= new System.Threading.Timer(_ => { if (Bloqueados) FecharJogosAbertos(); }, null,
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

    static void PararVigiaDeProgramas()
    {
        _vigiaProgramas?.Dispose();
        _vigiaProgramas = null;
    }

    /// <summary>Fecha os jogos instalados que estiverem abertos (uma passada pela lista de processos).</summary>
    static void FecharJogosAbertos()
    {
        Process[] todos;
        try { todos = Process.GetProcesses(); }
        catch (InvalidOperationException) { return; }
        foreach (var p in todos)
        {
            try
            {
                if (!_programas.Contains(p.ProcessName)) continue;
                p.Kill(entireProcessTree: true);
                bool primeiraVez;
                lock (_trava) primeiraVez = _jaAvisados.Add(p.ProcessName);
                if (primeiraVez) Programa.Log($"Jogos bloqueados: fechou {p.ProcessName}.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // já tinha fechado
            }
            finally { p.Dispose(); }
        }
    }

    // ---------- sites novos com cara de jogo (cópias do Friv, "unblocked games"...) ----------

    /// <summary>Chamado pelo vigia do filtro com os nomes que o PC procurou. Só faz algo com os jogos bloqueados.</summary>
    public static void VerNomes(IReadOnlyCollection<string> nomes)
    {
        List<string> aprendidos;
        var novos = new List<string>();
        var procurados = new List<string>();
        lock (_trava)
        {
            if (!_s.Bloqueados) return;
            if (_vistos.Count > 20_000) _vistos.Clear();
            aprendidos = _s.Aprendidos;
            var jaBloqueados = new HashSet<string>([.. CatalogoJogos.Dominios, .. aprendidos], StringComparer.Ordinal);
            foreach (var nome in nomes)
            {
                if (!_vistos.Add(nome)) continue;
                if (CatalogoJogos.Suspeito(nome) is not { } bloquear) continue;
                if (FiltroPorNome.JaBloqueado(bloquear, jaBloqueados, [])) continue;
                jaBloqueados.Add(bloquear);
                novos.Add(bloquear);
                procurados.Add(nome);
            }
            if (novos.Count == 0) return;
            _s.Aprendidos = [.. aprendidos.Concat(novos).TakeLast(MaximoAprendidos)];
            aprendidos = [.. _s.Aprendidos];
            Salvar();
        }
        var enderecos = CacheDns.Enderecos(procurados);
        AplicarBloqueio(aprendidos);
        ConexoesTcp.DerrubarPorUmTempo(enderecos);
        Programa.Log($"Jogos bloqueados: {novos.Count} site(s) de jogo novo(s) bloqueado(s) pelo nome.");
    }

    // ---------- liberação por tempo ----------

    static void AgendarFimDaLiberacao()
    {
        _fimDaLiberacao?.Dispose();
        _fimDaLiberacao = null;
        DateTime? ate;
        lock (_trava) ate = _s.Bloqueados ? null : _s.LiberadosAteUtc;
        if (ate is not { } fim) return;
        var falta = fim - DateTime.UtcNow;
        if (falta < TimeSpan.Zero) falta = TimeSpan.Zero;
        _fimDaLiberacao = new System.Threading.Timer(_ =>
        {
            bool aindaLiberado;
            lock (_trava) aindaLiberado = !_s.Bloqueados && _s.LiberadosAteUtc == fim;
            if (aindaLiberado) Bloquear(fecharNavegador: false, motivo: "tempo");
        }, null, falta, Timeout.InfiniteTimeSpan);
    }

    // ---------- guardar e avisar ----------

    static void Avisar()
    {
        if (_servidor is { } s) s.TransmitirControle(Protocolo.MontarEstadoJogos(EstadoAtual()));
    }

    static Situacao Carregar()
    {
        try
        {
            if (File.Exists(Caminho)) return JsonSerializer.Deserialize<Situacao>(File.ReadAllText(Caminho)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        return new();
    }

    static void Salvar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
            File.WriteAllText(Caminho, JsonSerializer.Serialize(_s));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Programa.Log($"Jogos: não foi possível guardar a situação ({ex.Message}).");
        }
    }

    /// <summary>Na desinstalação: tira o bloqueio de jogos e o que ficou guardado.</summary>
    public static void Remover()
    {
        TirarBloqueio();
        try { if (File.Exists(Caminho)) File.Delete(Caminho); } catch (IOException) { }
    }
}
