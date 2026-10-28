using System.Runtime.InteropServices;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Aplica no PC do aluno os eventos de mouse/teclado que o professor envia no controle remoto (SendInput).
/// O agente roda como SYSTEM na sessão do aluno, então a entrada vai para a área de trabalho ativa.
/// As coordenadas chegam normalizadas (0..65535) sobre a tela, para valer em qualquer resolução.
/// </summary>
static class EntradaRemota
{
    public static void Aplicar(EventoEntrada e)
    {
        switch (e.Tipo)
        {
            case TipoEntrada.MouseMover:
                MouseAbsoluto(e.X, e.Y, MOUSEEVENTF_MOVE);
                break;
            case TipoEntrada.MouseBotao:
                MouseAbsoluto(e.X, e.Y, MOUSEEVENTF_MOVE | FlagBotao(e.Botao, e.Pressionado));
                break;
            case TipoEntrada.MouseRoda:
                MouseAbsoluto(e.X, e.Y, MOUSEEVENTF_MOVE);
                MouseRoda(e.Roda);
                break;
            case TipoEntrada.Tecla:
                Tecla(e.Tecla, e.Pressionado, e.Estendida);
                break;
        }
    }

    static void MouseAbsoluto(ushort x, ushort y, uint flags)
    {
        var entrada = new INPUT
        {
            type = INPUT_MOUSE,
            U = { mi = new MOUSEINPUT { dx = x, dy = y, dwFlags = flags | MOUSEEVENTF_ABSOLUTE } },
        };
        Enviar(entrada);
    }

    static void MouseRoda(short delta)
    {
        var entrada = new INPUT
        {
            type = INPUT_MOUSE,
            U = { mi = new MOUSEINPUT { mouseData = unchecked((uint)delta), dwFlags = MOUSEEVENTF_WHEEL } },
        };
        Enviar(entrada);
    }

    static void Tecla(ushort vk, bool pressionado, bool estendida)
    {
        ushort scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC); // código de varredura: alguns programas só reagem a ele
        uint flags = (pressionado ? 0u : KEYEVENTF_KEYUP) | (estendida ? KEYEVENTF_EXTENDEDKEY : 0u);
        var entrada = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
        };
        Enviar(entrada);
    }

    static uint FlagBotao(BotaoMouse botao, bool pressionado) => botao switch
    {
        BotaoMouse.Direito => pressionado ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
        BotaoMouse.Meio => pressionado ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
        _ => pressionado ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
    };

    static void Enviar(INPUT entrada)
    {
        var vetor = new[] { entrada };
        SendInput(1, vetor, Marshal.SizeOf<INPUT>());
    }

    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_WHEEL = 0x0800;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;
    const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUniao
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUniao U; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint uCode, uint uMapType);
}
