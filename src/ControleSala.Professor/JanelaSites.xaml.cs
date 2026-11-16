using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>
/// Monta a lista de sites que o professor pode bloquear durante a aula (começa vazia). Enquanto ele digita, aparecem sugestões
/// do catálogo (com todos os endereços de cada site) e o endereço provável do que foi digitado (ex.: "friv" → friv.com).
/// </summary>
public partial class JanelaSites : Window
{
    /// <summary>Uma linha da lista: o que fica guardado e o que o professor vê.</summary>
    public sealed record LinhaSite(string Entrada, string Titulo, string Detalhe);

    readonly List<string> _entradas;
    readonly ObservableCollection<LinhaSite> _linhas = [];

    public JanelaSites()
    {
        InitializeComponent();
        _entradas = Sites.Carregar();
        Lista.ItemsSource = _linhas;
        Atualizar();
        Loaded += (_, _) => CampoNovo.Focus();
    }

    /// <summary>Como uma entrada guardada aparece na lista.</summary>
    internal static LinhaSite Linha(string entrada)
    {
        if (CatalogoSites.PorEntrada(entrada) is { } item) return new(entrada, item.Nome, CatalogoSites.Resumo(item));
        if (Sites.NormalizarTerminacao(entrada) is { } t) return new(entrada, $"Tudo que termina em .{t}", "qualquer site com essa terminação");
        return new(entrada, entrada, "este endereço e todos os subdomínios dele");
    }

    void Atualizar()
    {
        _linhas.Clear();
        foreach (var e in _entradas) _linhas.Add(Linha(e));
        TextoMensagem.Text = _entradas.Count == 0 ? "" : $"{_entradas.Count} na lista.";
    }

    void Salvar()
    {
        try { Sites.Salvar(_entradas); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TextoMensagem.Text = $"Não foi possível salvar: {ex.Message}"; }
    }

    /// <summary>Põe na lista (sem repetir). Devolve quantas entraram.</summary>
    int Incluir(IEnumerable<string> entradas)
    {
        int n = 0;
        foreach (var e in entradas)
            if (!_entradas.Contains(e, StringComparer.OrdinalIgnoreCase)) { _entradas.Add(e); n++; }
        if (n > 0) { Salvar(); Atualizar(); }
        return n;
    }

    // ---------- sugestões enquanto digita ----------

    void Campo_TextChanged(object sender, TextChangedEventArgs e)
    {
        var sugestoes = CatalogoSites.Sugerir(CampoNovo.Text);
        ListaSugestoes.ItemsSource = sugestoes.Select(s => new LinhaSite(s.Entrada, s.Titulo, s.Detalhe)).ToList();
        ListaSugestoes.SelectedIndex = sugestoes.Count > 0 ? 0 : -1;
        PainelSugestoes.IsOpen = sugestoes.Count > 0 && CampoNovo.IsKeyboardFocused;
    }

    void Campo_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Clicar numa sugestão não fecha antes do clique valer (a lista não pega o foco).
        if (!ListaSugestoes.IsMouseOver) PainelSugestoes.IsOpen = false;
    }

    void Campo_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when PainelSugestoes.IsOpen:
                ListaSugestoes.SelectedIndex = Math.Min(ListaSugestoes.SelectedIndex + 1, ListaSugestoes.Items.Count - 1);
                ListaSugestoes.ScrollIntoView(ListaSugestoes.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when PainelSugestoes.IsOpen:
                ListaSugestoes.SelectedIndex = Math.Max(ListaSugestoes.SelectedIndex - 1, 0);
                ListaSugestoes.ScrollIntoView(ListaSugestoes.SelectedItem);
                e.Handled = true;
                break;
            case Key.Escape when PainelSugestoes.IsOpen:
                PainelSugestoes.IsOpen = false;
                e.Handled = true;
                break;
            case Key.Enter:
                Adicionar_Click(sender, e);
                e.Handled = true;
                break;
        }
    }

    void Sugestao_Click(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is LinhaSite escolhida)
            AdicionarEscolhida(escolhida);
    }

    void AdicionarEscolhida(LinhaSite escolhida)
    {
        int n = Incluir([escolhida.Entrada]);
        CampoNovo.Clear();
        PainelSugestoes.IsOpen = false;
        TextoMensagem.Text = n > 0 ? $"{escolhida.Titulo} adicionado ✓" : $"{escolhida.Titulo} já está na lista.";
        CampoNovo.Focus();
    }

    void Adicionar_Click(object sender, RoutedEventArgs e)
    {
        // Com a lista de sugestões aberta, vale a escolhida (a primeira já vem marcada).
        if (PainelSugestoes.IsOpen && ListaSugestoes.SelectedItem is LinhaSite escolhida)
        {
            AdicionarEscolhida(escolhida);
            return;
        }
        // Vários de uma vez (colados com vírgula ou um por linha) continuam valendo.
        var (dominios, terminacoes) = Sites.SepararTudo(CampoNovo.Text);
        var novos = terminacoes.Select(t => Sites.PrefixoTerminacao + t).Concat(dominios).ToList();
        if (novos.Count == 0)
        {
            TextoMensagem.Text = "Digite o nome do site (ex.: youtube) e escolha na lista, ou o endereço (ex.: exemplo.com).";
            return;
        }
        int n = Incluir(novos);
        CampoNovo.Clear();
        PainelSugestoes.IsOpen = false;
        TextoMensagem.Text = n > 0 ? $"{n} adicionado(s) ✓" : "Já estava na lista.";
    }

    void Remover_Click(object sender, RoutedEventArgs e)
    {
        var escolhidos = Lista.SelectedItems.OfType<LinhaSite>().ToList();
        if (escolhidos.Count == 0) { TextoMensagem.Text = "Escolha um site na lista."; return; }
        foreach (var l in escolhidos) _entradas.Remove(l.Entrada);
        Salvar();
        Atualizar();
    }

    void RemoverTodos_Click(object sender, RoutedEventArgs e)
    {
        if (_entradas.Count == 0) return;
        if (MessageBox.Show(this, "Tirar todos os sites da lista?", "Sites bloqueados", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _entradas.Clear();
        Salvar();
        Atualizar();
    }
}
