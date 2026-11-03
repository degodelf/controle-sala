using System.Windows;
using System.Windows.Input;

namespace ControleSala.Professor;

public partial class JanelaAtalhos : Window
{
    sealed record Linha(AcaoAtalho Acao, string Rotulo, string Gesto);

    readonly RepositorioAtalhos _atalhos;
    bool _capturando;

    internal JanelaAtalhos(RepositorioAtalhos atalhos)
    {
        _atalhos = atalhos;
        InitializeComponent();
        Atualizar();
    }

    void Atualizar()
    {
        var selecionada = (Lista.SelectedItem as Linha)?.Acao;
        Lista.ItemsSource = RepositorioAtalhos.Catalogo
            .Select(it => new Linha(it.Acao, it.Rotulo, _atalhos.Gesto(it.Acao) is { Length: > 0 } g ? g : "—"))
            .ToList();
        if (selecionada is { } a) Lista.SelectedItem = (Lista.ItemsSource as IEnumerable<Linha>)!.FirstOrDefault(l => l.Acao == a);
    }

    void Definir_Click(object sender, RoutedEventArgs e)
    {
        if (Lista.SelectedItem is not Linha linha) { TextoMensagem.Text = "Escolha uma ação na lista."; return; }
        _capturando = true;
        TextoMensagem.Text = $"Pressione a combinação de teclas para “{linha.Rotulo}” (Esc cancela)...";
    }

    void Remover_Click(object sender, RoutedEventArgs e)
    {
        if (Lista.SelectedItem is not Linha linha) { TextoMensagem.Text = "Escolha uma ação na lista."; return; }
        _capturando = false;
        _atalhos.Definir(linha.Acao, "");
        Atualizar();
        TextoMensagem.Text = $"“{linha.Rotulo}” ficou sem atalho.";
    }

    void Restaurar_Click(object sender, RoutedEventArgs e)
    {
        _capturando = false;
        _atalhos.RestaurarPadroes();
        Atualizar();
        TextoMensagem.Text = "Atalhos voltaram ao padrão.";
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturando) return;
        var tecla = RepositorioAtalhos.TeclaReal(e);
        if (tecla == Key.Escape) { _capturando = false; TextoMensagem.Text = "Cancelado."; e.Handled = true; return; }

        var gesto = RepositorioAtalhos.Formatar(Keyboard.Modifiers, tecla);
        if (gesto.Length == 0) return; // ainda só com o modificador pressionado: espera a tecla
        if (Lista.SelectedItem is Linha linha)
        {
            _atalhos.Definir(linha.Acao, gesto);
            Atualizar();
            TextoMensagem.Text = $"“{linha.Rotulo}” agora é {gesto}.";
        }
        _capturando = false;
        e.Handled = true;
    }
}
