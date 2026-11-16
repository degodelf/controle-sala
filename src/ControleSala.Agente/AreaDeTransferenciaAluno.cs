using System.Runtime.InteropServices;
using System.Windows.Forms;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Copiar e colar entre o aluno e o professor, só enquanto o professor está com a tela do aluno aberta (controlando):
/// o texto que o aluno copia (Ctrl+C) vai para o professor; o texto que o professor manda é colocado na área de
/// transferência do aluno. Fora disso, nada do que o aluno copia sai do computador dele.
/// </summary>
sealed partial class AreaDeTransferenciaAluno : IDisposable
{
    const int IntervaloMs = 400;

    readonly ServidorVideo _servidor;
    readonly System.Threading.Timer _relogio;
    uint _ultimaSequencia;
    bool _olhando;
    int _ocupado;

    public AreaDeTransferenciaAluno(ServidorVideo servidor)
    {
        _servidor = servidor;
        _relogio = new System.Threading.Timer(_ => Verificar(), null, IntervaloMs, IntervaloMs);
    }

    void Verificar()
    {
        if (Interlocked.Exchange(ref _ocupado, 1) == 1) return;
        try
        {
            bool aberto = _servidor.Precisa(ModoVideo.Completo);
            uint sequencia = GetClipboardSequenceNumber();
            if (!aberto)
            {
                _olhando = false;
                return;
            }
            if (!_olhando)
            {
                // Acabaram de abrir a tela: o que já estava copiado antes não é enviado, só o que o aluno copiar daqui em diante.
                _olhando = true;
                _ultimaSequencia = sequencia;
                return;
            }
            if (sequencia == _ultimaSequencia) return;
            _ultimaSequencia = sequencia;

            string? texto = null;
            InterfaceAgente.NaInterface(() => texto = LerTexto());
            if (texto != null && Protocolo.MontarAreaDeTransferencia(texto) is { } msg)
                _servidor.TransmitirParaTelaAberta(msg);
        }
        catch (Exception ex)
        {
            Programa.Log($"Área de transferência: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _ocupado, 0);
        }
    }

    /// <summary>Texto que o professor mandou: vai para a área de transferência do aluno (antes do Ctrl+V que vem atrás).</summary>
    public void Colocar(string texto)
    {
        if (texto.Length == 0 || texto.Length > Protocolo.MaxTextoCopiado) return;
        // Postar: fica na mesma fila do controle remoto, então o texto já está lá quando o Ctrl+V do professor chegar.
        InterfaceAgente.Postar(() =>
        {
            try
            {
                Clipboard.SetDataObject(texto, copy: true, retryTimes: 10, retryDelay: 50);
                _ultimaSequencia = GetClipboardSequenceNumber(); // não devolve para o professor o que veio dele
            }
            catch (Exception ex) when (ex is ExternalException or ThreadStateException)
            {
                Programa.Log($"Não foi possível colocar o texto do professor na área de transferência: {ex.Message}");
            }
        });
    }

    static string? LerTexto()
    {
        for (int tentativa = 0; tentativa < 5; tentativa++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : null;
            }
            catch (ExternalException)
            {
                Thread.Sleep(40); // outro programa está com a área de transferência aberta agora
            }
        }
        return null;
    }

    public void Dispose() => _relogio.Dispose();

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();
}
