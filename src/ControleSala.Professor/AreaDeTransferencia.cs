using System.Runtime.InteropServices;
using System.Windows;

namespace ControleSala.Professor;

/// <summary>Área de transferência do PC do professor, tolerando outro programa que esteja com ela aberta no momento.</summary>
static class AreaDeTransferencia
{
    public static bool Definir(string texto)
    {
        for (int tentativa = 0; tentativa < 5; tentativa++)
        {
            try
            {
                Clipboard.SetDataObject(texto, copy: true);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
        }
        return false;
    }

    public static string? LerTexto()
    {
        for (int tentativa = 0; tentativa < 5; tentativa++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
        }
        return null;
    }
}
