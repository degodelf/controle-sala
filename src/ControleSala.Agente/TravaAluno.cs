using System.Runtime.InteropServices;

namespace ControleSala.Agente;

/// <summary>
/// Trava o mouse e o teclado físicos do aluno enquanto o professor controla (opção do professor). O professor reenvia
/// "travar" a cada poucos segundos; se parar de chegar (professor fechou/caiu), a trava solta sozinha — o aluno nunca fica
/// preso. O Ctrl+Alt+Del do Windows continua funcionando mesmo travado (válvula de escape de segurança).
/// </summary>
static class TravaAluno
{
    const long ExpiraMs = 4000;   // sem reforço por mais que isto, destrava sozinho
    static volatile bool _desejado;
    static volatile bool _ativa;
    static long _ultimoReforco;
    static System.Threading.Timer? _watchdog;
    static readonly Lock _trava = new();

    /// <summary>Liga/desliga a trava. "ligar" também serve de reforço (o professor reenvia periodicamente).</summary>
    public static void Definir(bool ligar)
    {
        _desejado = ligar;
        if (ligar) Interlocked.Exchange(ref _ultimoReforco, Environment.TickCount64);
        GarantirWatchdog();
        Aplicar();
    }

    static void GarantirWatchdog()
    {
        if (_watchdog != null) return;
        lock (_trava)
            _watchdog ??= new System.Threading.Timer(_ => Aplicar(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    static void Aplicar()
    {
        bool alvo = _desejado && (Environment.TickCount64 - Interlocked.Read(ref _ultimoReforco) < ExpiraMs);
        if (alvo == _ativa) return;
        _ativa = alvo;
        if (!alvo) _desejado = false;
        // BlockInput precisa rodar na thread da área de trabalho (a de interface).
        InterfaceAgente.Postar(() => { try { BlockInput(alvo); } catch (Exception) { } });
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool BlockInput([MarshalAs(UnmanagedType.Bool)] bool fBlockIt);
}
