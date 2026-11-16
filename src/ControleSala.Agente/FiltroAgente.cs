using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Mantém ligado o <see cref="FiltroPermanente"/> (apostas e sites +18) neste PC: seção própria no hosts e regra de DNS própria,
/// separadas das do professor, então \"Liberar a internet\" não mexe nelas. Confere ao ligar e de hora em hora (barato: só lê o
/// hosts e um marcador); o PowerShell da regra de DNS só roda quando a lista mudou ou a regra não foi confirmada.
/// </summary>
static class FiltroAgente
{
    static System.Threading.Timer? _relogio;
    static int _rodando;

    static string Marcador => Path.Combine(Path.GetDirectoryName(ConfigAgente.CaminhoPadrao)!, "filtro.txt");

    public static void Iniciar() =>
        _relogio ??= new System.Threading.Timer(_ => Garantir(), null, TimeSpan.FromSeconds(5), TimeSpan.FromHours(1));

    static void Garantir()
    {
        if (Interlocked.Exchange(ref _rodando, 1) == 1) return;
        try
        {
            var (dominios, terminacoes) = FiltroPermanente.Listas();
            bool mudou = false;

            string hosts = Comandos.CaminhoHosts;
            string atual = File.Exists(hosts) ? File.ReadAllText(hosts) : "";
            string? secao = Sites.MontarSecao(dominios, FiltroPermanente.InicioHosts, FiltroPermanente.FimHosts);
            if (secao != null && !atual.Contains(secao, StringComparison.Ordinal))
            {
                File.WriteAllText(hosts, Sites.MontarHosts(atual, dominios, FiltroPermanente.InicioHosts, FiltroPermanente.FimHosts));
                mudou = true;
            }

            string? feito = File.Exists(Marcador) ? File.ReadAllText(Marcador).Trim() : null;
            if (feito != FiltroPermanente.Assinatura)
            {
                if (Comandos.AplicarRegraDns(Sites.NomesDaRegraDns(dominios, terminacoes), FiltroPermanente.ComentarioRegraDns) is { } erro)
                    Programa.Log($"Filtro permanente: regra de DNS não aplicada ({erro}); o hosts continua valendo.");
                else
                    File.WriteAllText(Marcador, FiltroPermanente.Assinatura);
                mudou = true;
            }
            if (mudou) Comandos.LimparCacheDns();
            GarantirPoliticas();
        }
        catch (Exception ex)
        {
            Programa.Log($"Filtro permanente: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _rodando, 0);
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
            string hosts = Comandos.CaminhoHosts;
            if (File.Exists(hosts))
            {
                string atual = File.ReadAllText(hosts);
                string limpo = Sites.RemoverSecao(atual, FiltroPermanente.InicioHosts, FiltroPermanente.FimHosts);
                if (limpo != atual) File.WriteAllText(hosts, limpo);
            }
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
            Comandos.LimparCacheDns();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Filtro: {ex.Message}");
        }
    }
}
