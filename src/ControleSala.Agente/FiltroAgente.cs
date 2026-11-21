using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Mantém ligado o <see cref="FiltroPermanente"/> (apostas e sites +18) neste PC: seção própria no hosts e regra de DNS própria,
/// separadas das do professor, então "Liberar a internet" não mexe nelas. Confere ao ligar e de hora em hora (barato: só lê o
/// hosts e um marcador); o PowerShell da regra de DNS só roda quando a lista mudou ou a regra não foi confirmada.
/// A cada 3 s olha os nomes que o PC procurou (<see cref="CacheDns"/>): se um tem cara de aposta ou +18
/// (<see cref="FiltroPorNome"/>), o site passa a ficar bloqueado para sempre neste PC e a conexão aberta com ele cai.
/// </summary>
static class FiltroAgente
{
    static System.Threading.Timer? _relogio, _vigia;
    static readonly object _trava = new();
    static List<string> _aprendidos = [];
    static readonly HashSet<string> _vistos = new(StringComparer.Ordinal);
    static int _vigiando;

    static readonly (List<string> Dominios, List<string> Terminacoes) Fixas = FiltroPermanente.Listas();

    static string Marcador => Path.Combine(Path.GetDirectoryName(ConfigAgente.CaminhoPadrao)!, "filtro.txt");

    public static void Iniciar()
    {
        lock (_trava) _aprendidos = FiltroPorNome.Carregar();
        _relogio ??= new System.Threading.Timer(_ => Garantir(), null, TimeSpan.FromSeconds(5), TimeSpan.FromHours(1));
        _vigia ??= new System.Threading.Timer(_ => Vigiar(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(3));
    }

    static void Garantir()
    {
        try
        {
            Aplicar();
            GarantirPoliticas();
        }
        catch (Exception ex)
        {
            Programa.Log($"Filtro permanente: {ex.Message}");
        }
    }

    /// <summary>Põe no hosts e na regra de DNS a lista fixa mais os sites aprendidos (só o que mudou).</summary>
    static void Aplicar(IReadOnlyCollection<System.Net.IPAddress>? derrubar = null)
    {
        lock (_trava)
        {
            var dominios = Fixas.Dominios.Concat(_aprendidos).Distinct(StringComparer.Ordinal).ToList();
            bool mudou = false;

            string? secao = Sites.MontarSecao(dominios, FiltroPermanente.InicioHosts, FiltroPermanente.FimHosts);
            if (secao != null && Comandos.MudarHosts(atual => atual.Contains(secao, StringComparison.Ordinal)
                    ? atual : Sites.MontarHosts(atual, dominios, FiltroPermanente.InicioHosts, FiltroPermanente.FimHosts)))
            {
                Comandos.LimparCacheDns(); // o hosts já vale; a regra de DNS (mais lenta) vem logo depois
                mudou = true;
            }
            if (derrubar is { Count: > 0 }) ConexoesTcp.DerrubarPorUmTempo(derrubar);

            string assinatura = FiltroPorNome.Assinatura(_aprendidos);
            string? feito = File.Exists(Marcador) ? File.ReadAllText(Marcador).Trim() : null;
            if (feito != assinatura)
            {
                if (Comandos.AplicarRegraDns(Sites.NomesDaRegraDns(dominios, Fixas.Terminacoes), FiltroPermanente.ComentarioRegraDns) is { } erro)
                    Programa.Log($"Filtro permanente: regra de DNS não aplicada ({erro}); o hosts continua valendo.");
                else
                    File.WriteAllText(Marcador, assinatura);
                mudou = true;
            }
            if (mudou) Comandos.LimparCacheDns();
        }
    }

    /// <summary>Olha os nomes que o PC acabou de procurar e bloqueia os que têm cara de aposta ou +18.</summary>
    static void Vigiar()
    {
        if (Interlocked.Exchange(ref _vigiando, 1) == 1) return;
        try
        {
            if (CacheDns.Nomes() is not { } nomes) return;
            JogosAgente.VerNomes(nomes); // com os jogos bloqueados, pega também cópias do Friv e "unblocked games"
            if (_vistos.Count > 20_000) _vistos.Clear();
            HashSet<string> jaBloqueados;
            lock (_trava) jaBloqueados = [.. Fixas.Dominios, .. _aprendidos];

            var novos = new List<string>();
            var procurados = new List<string>();
            foreach (var nome in nomes)
            {
                if (!_vistos.Add(nome)) continue;
                if (FiltroPorNome.Suspeito(nome) is not { } bloquear) continue;
                if (FiltroPorNome.JaBloqueado(bloquear, jaBloqueados, Fixas.Terminacoes)) continue;
                jaBloqueados.Add(bloquear);
                novos.Add(bloquear);
                procurados.Add(nome);
            }
            if (novos.Count == 0) return;

            var enderecos = CacheDns.Enderecos(procurados); // antes do bloqueio: depois o nome já não resolve
            lock (_trava)
            {
                _aprendidos = [.. _aprendidos.Concat(novos).Distinct(StringComparer.Ordinal).TakeLast(FiltroPorNome.MaximoAprendidos)];
                FiltroPorNome.Salvar(_aprendidos);
            }
            Aplicar(enderecos);
            Programa.Log($"Filtro permanente: {novos.Count} site(s) novo(s) bloqueado(s) pelo nome.");
        }
        catch (Exception ex)
        {
            Programa.Log($"Filtro permanente (nomes): {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _vigiando, 0);
        }
    }

    /// <summary>Trava o "DNS seguro" dos navegadores (ver <see cref="FiltroPermanente.PoliticasNavegador"/>). Só grava o que mudou.</summary>
    static void GarantirPoliticas()
    {
        foreach (var p in FiltroPermanente.PoliticasNavegador)
        {
            try
            {
                using var chave = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(p.Chave, writable: true);
                object? atual = chave.GetValue(p.Nome);
                if (p.Valor is string texto && atual as string != texto)
                    chave.SetValue(p.Nome, texto, Microsoft.Win32.RegistryValueKind.String);
                else if (p.Valor is int numero && !(atual is int a && a == numero))
                    chave.SetValue(p.Nome, numero, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                Programa.Log($"Filtro permanente: não foi possível travar o DNS seguro ({p.Chave}\\{p.Nome}): {ex.Message}");
            }
        }
    }

    /// <summary>Na desinstalação: tira a seção do hosts, a regra de DNS, a trava do DNS seguro e o marcador.</summary>
    public static void Remover()
    {
        try
        {
            Comandos.MudarHosts(atual => Sites.RemoverSecao(atual, FiltroPermanente.InicioHosts, FiltroPermanente.FimHosts));
            Comandos.AplicarRegraDns([], FiltroPermanente.ComentarioRegraDns);
            foreach (var p in FiltroPermanente.PoliticasNavegador)
            {
                try
                {
                    using var chave = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(p.Chave, writable: true);
                    chave?.DeleteValue(p.Nome, throwOnMissingValue: false);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
            }
            if (File.Exists(Marcador)) File.Delete(Marcador);
            if (File.Exists(FiltroPorNome.CaminhoPadrao)) File.Delete(FiltroPorNome.CaminhoPadrao);
            Comandos.LimparCacheDns();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Filtro: {ex.Message}");
        }
    }
}
