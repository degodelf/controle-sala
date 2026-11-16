using System.Runtime.InteropServices;

namespace ControleSala.Professor;

/// <summary>
/// Enquanto a tela do aluno está aberta e em foco, a tecla Windows (sozinha ou em atalhos como Windows+E) vai para o aluno
/// em vez de abrir o menu Iniciar deste PC. O Windows trata essa tecla antes de qualquer janela, então só um gancho de
/// teclado de baixo nível (o mesmo recurso que a Área de Trabalho Remota usa) consegue segurá-la.
/// </summary>
sealed partial class TeclaWindowsRemota : IDisposable
{
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const uint VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    readonly Action<ushort, bool> _enviar;
    readonly ProcGancho _proc; // guardado em campo: o Windows chama de volta e o coletor de lixo não pode levar
    nint _gancho;
    readonly HashSet<uint> _apertadas = [];

    /// <summary>Liga/desliga a captura (a janela do aluno ganhou ou perdeu o foco).</summary>
    public bool Ativa { get; set; }

    /// <param name="enviar">Manda a tecla ao aluno: (código virtual, apertada).</param>
    public TeclaWindowsRemota(Action<ushort, bool> enviar)
    {
        _enviar = enviar;
        _proc = Processar;
        _gancho = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, GetModuleHandleW(null), 0);
    }

    nint Processar(int codigo, nint mensagem, nint dados)
    {
        if (codigo >= 0 && Ativa)
        {
            uint vk = (uint)Marshal.ReadInt32(dados); // KBDLLHOOKSTRUCT.vkCode
            if (vk is VK_LWIN or VK_RWIN)
            {
                int m = (int)mensagem;
                bool apertada = m is WM_KEYDOWN or WM_SYSKEYDOWN;
                if (apertada || m is WM_KEYUP or WM_SYSKEYUP)
                {
                    if (apertada) _apertadas.Add(vk); else _apertadas.Remove(vk);
                    _enviar((ushort)vk, apertada);
                    return 1; // não deixa o menu Iniciar deste PC abrir
                }
            }
        }
        return CallNextHookEx(_gancho, codigo, mensagem, dados);
    }

    /// <summary>A janela perdeu o foco com a tecla apertada: solta no aluno, para ela não ficar "presa" lá.</summary>
    public void SoltarTudo()
    {
        foreach (var vk in _apertadas) _enviar((ushort)vk, false);
        _apertadas.Clear();
    }

    public void Dispose()
    {
        SoltarTudo();
        if (_gancho != 0) UnhookWindowsHookEx(_gancho);
        _gancho = 0;
    }

    delegate nint ProcGancho(int codigo, nint mensagem, nint dados);

    // DllImport (e não LibraryImport): o gerador de P/Invoke não converte delegate.
    [DllImport("user32.dll", SetLastError = true)]
    static extern nint SetWindowsHookExW(int tipo, ProcGancho proc, nint modulo, uint thread);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint gancho, int codigo, nint mensagem, nint dados);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint gancho);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? nome);
}
