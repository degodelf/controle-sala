using System.Windows;
using System.Windows.Threading;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Mostra, computador por computador, se o Windows Defender atualizou e verificou na última limpeza.</summary>
public partial class JanelaDefender : Window
{
    sealed record Linha(string Computador, string Atualizacao, string Verificacao, string Quando, string Detalhe);

    readonly List<ConexaoComputador> _computadores;
    readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromSeconds(2) };

    internal JanelaDefender(List<ConexaoComputador> computadores)
    {
        _computadores = computadores;
        InitializeComponent();
        _relogio.Tick += (_, _) => Atualizar();
        _relogio.Start();
        Atualizar();
    }

    void Atualizar() => Lista.ItemsSource = _computadores.Select(MontarLinha).ToList();

    static Linha MontarLinha(ConexaoComputador c)
    {
        if (c.Estado != EstadoConexao.Online) return new Linha(c.Dados.Nome, "—", "—", "", "offline");
        if (c.Defender is not { } r) return new Linha(c.Dados.Nome, "—", "—", "", "ainda não rodou");
        string detalhe = r.AntivirusAtivo == false ? "Defender desativado neste PC. " + r.Detalhe : r.Detalhe;
        return new Linha(c.Dados.Nome, ResultadoDefender.TextoDaEtapa(r.Atualizacao), ResultadoDefender.TextoDaEtapa(r.Verificacao),
            (r.Fim ?? r.Inicio).ToLocalTime().ToString("dd/MM/yyyy HH:mm"), detalhe);
    }

    void Window_Closed(object? sender, EventArgs e) => _relogio.Stop();
}
