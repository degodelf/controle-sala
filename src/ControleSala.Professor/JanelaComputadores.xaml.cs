using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

public partial class JanelaComputadores : Window
{
    readonly RepositorioSala _sala;

    public JanelaComputadores(RepositorioSala sala, bool abrirProcurar = false)
    {
        _sala = sala;
        InitializeComponent();
        Atualizar();
        // Vindo do botão "Procurar" da barra de ferramentas: já abre a busca na rede.
        if (abrirProcurar) Loaded += (_, _) => Procurar_Click(this, new RoutedEventArgs());
    }

    ComputadorSala? Selecionado => Lista.SelectedItem as ComputadorSala;

    void Atualizar(ComputadorSala? selecionar = null)
    {
        Lista.ItemsSource = _sala.Computadores.ToList();
        if (selecionar != null) Lista.SelectedItem = selecionar;
    }

    bool EscolheuAlgum()
    {
        if (Selecionado != null) return true;
        TextoMensagem.Text = "Escolha um computador na lista.";
        return false;
    }

    void Adicionar_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        var janela = new JanelaEditarComputador(_sala) { Owner = this };
        if (janela.ShowDialog() == true) Atualizar(janela.Resultado);
    }

    void Editar_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        if (!EscolheuAlgum()) return;
        var janela = new JanelaEditarComputador(_sala, Selecionado) { Owner = this };
        if (janela.ShowDialog() == true) Atualizar(janela.Resultado);
    }

    void Lista_DuploClique(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Selecionado != null) Editar_Click(sender, e);
    }

    void Remover_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        if (!EscolheuAlgum()) return;
        var c = Selecionado!;
        if (MessageBox.Show(this, $"Remover {c.Nome} da sala?\n\nO programa dele não é desinstalado; ele só deixa de aparecer aqui.",
                "Remover computador", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _sala.Remover(c);
        Atualizar();
    }

    void Subir_Click(object sender, RoutedEventArgs e) => Mover(-1);
    void Descer_Click(object sender, RoutedEventArgs e) => Mover(+1);

    void Mover(int delta)
    {
        TextoMensagem.Text = "";
        if (!EscolheuAlgum()) return;
        var c = Selecionado!;
        if (_sala.Mover(c, delta)) Atualizar(c);
    }

    void Procurar_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        var janela = new JanelaProcurar(_sala) { Owner = this };
        janela.ShowDialog();
        Atualizar();
        if (janela.Adicionados > 0)
            TextoMensagem.Text = $"{janela.Adicionados} computador{(janela.Adicionados == 1 ? "" : "es")} adicionado{(janela.Adicionados == 1 ? "" : "s")}.";
    }
}
