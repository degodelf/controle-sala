using System.Runtime.InteropServices;

namespace ControleSala.Agente;

/// <summary>
/// Não deixa o Windows desligar a tela do aluno por falta de uso. Em vários PCs da sala, quando o Windows apagava a tela
/// (10 min parado), o monitor dormia, o PC achava que ficou sem monitor (passava para 1024x768) e a imagem não voltava mais:
/// tela preta até reiniciar. Vale só enquanto o agente está aberto; não muda a configuração de energia do Windows.
/// </summary>
static partial class TelaLigada
{
    const uint Continuo = 0x80000000, TelaNecessaria = 0x00000002; // ES_CONTINUOUS | ES_DISPLAY_REQUIRED

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint estado);

    /// <summary>Chame numa thread que fica viva o tempo todo (o pedido acaba quando ela termina).</summary>
    public static void Manter()
    {
        if (SetThreadExecutionState(Continuo | TelaNecessaria) == 0)
            Programa.Log("Não foi possível manter a tela ligada (o Windows recusou o pedido).");
    }
}
