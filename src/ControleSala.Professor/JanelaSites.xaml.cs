using System.IO;
using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Monta a lista de sites que o professor pode bloquear durante a aula.</summary>
public partial class JanelaSites : Window
{
    readonly List<string> _sites;

    public JanelaSites()
    {
        InitializeComponent();
        _sites = Sites.Carregar();
        Atualizar();
        Loaded += (_, _) => CampoNovo.Focus();
    }

    void Atualizar()
    {
        Lista.ItemsSource = null;
        Lista.ItemsSource = _sites;
    }

    void Salvar()
    {
        try { Sites.Salvar(_sites); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TextoMensagem.Text = $"Não foi possível salvar: {ex.Message}"; }
    }

    void Campo_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) Adicionar_Click(sender, e);
    }

    void Adicionar_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        var novos = Sites.Separar(CampoNovo.Text);
        if (novos.Count == 0) { TextoMensagem.Text = "Digite um site válido (ex.: facebook.com)."; return; }
        foreach (var s in novos)
            if (!_sites.Contains(s, StringComparer.OrdinalIgnoreCase))
                _sites.Add(s);
        CampoNovo.Clear();
        Salvar();
        Atualizar();
    }

    void Remover_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        if (Lista.SelectedItem is not string s) { TextoMensagem.Text = "Escolha um site na lista."; return; }
        _sites.Remove(s);
        Salvar();
        Atualizar();
    }
}
