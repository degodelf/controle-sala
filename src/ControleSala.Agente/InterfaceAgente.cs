using System.Windows.Forms;

namespace ControleSala.Agente;

/// <summary>
/// Uma thread de interface (STA, com laço de mensagens do Windows) que vive enquanto o agente roda. É nela que
/// aparecem as janelas mostradas no PC do aluno — o ícone da bandeja, a mensagem do professor, a tela de bloqueio.
/// Os comandos chegam por threads de rede; <see cref="NaInterface"/> leva a execução para esta thread.
/// </summary>
static class InterfaceAgente
{
    static Control? _ponte;
    static readonly ManualResetEventSlim _pronto = new(false);

    /// <summary>Liga a thread de interface (uma vez). Precisa ser chamada antes de mostrar qualquer janela.</summary>
    public static void Iniciar()
    {
        if (_ponte != null) return;
        var t = new Thread(() =>
        {
            _ponte = new Control();
            _ = _ponte.Handle; // cria o identificador da janela nesta thread
            _pronto.Set();
            Application.Run();
        })
        { IsBackground = true, Name = "Interface do agente" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        _pronto.Wait();
    }

    /// <summary>Executa <paramref name="acao"/> na thread de interface e espera terminar.</summary>
    public static void NaInterface(Action acao)
    {
        Iniciar();
        _ponte!.Invoke(acao);
    }

    /// <summary>Executa <paramref name="acao"/> na thread de interface sem esperar (para eventos frequentes, como o controle remoto).</summary>
    public static void Postar(Action acao)
    {
        Iniciar();
        try { _ponte!.BeginInvoke(acao); } catch (Exception) { /* interface encerrando */ }
    }
}
