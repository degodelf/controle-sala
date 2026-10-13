using System.IO;
using System.Windows;
using ControleSala.Comum;
using Microsoft.Win32;

namespace ControleSala.Professor;

/// <summary>Monta a lista de programas que o professor pode abrir/fechar nos PCs dos alunos.</summary>
public partial class JanelaProgramas : Window
{
    readonly List<ProgramaPermitido> _lista;

    public JanelaProgramas()
    {
        InitializeComponent();
        _lista = Programas.Carregar();
        Atualizar();
    }

    ProgramaPermitido? Selecionado => Lista.SelectedItem as ProgramaPermitido;

    void Atualizar(ProgramaPermitido? selecionar = null)
    {
        Lista.ItemsSource = null;
        Lista.ItemsSource = _lista;
        if (selecionar != null) Lista.SelectedItem = selecionar;
    }

    void Salvar()
    {
        try { Programas.Salvar(_lista); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TextoMensagem.Text = $"Não foi possível salvar: {ex.Message}"; }
    }

    void Adicionar_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        var janela = new JanelaEditarPrograma(null) { Owner = this };
        if (janela.ShowDialog() == true && janela.Resultado is { } p)
        {
            _lista.Add(p);
            Salvar();
            Atualizar(p);
        }
    }

    void Editar_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        if (Selecionado is not { } atual) { TextoMensagem.Text = "Escolha um programa na lista."; return; }
        var janela = new JanelaEditarPrograma(atual) { Owner = this };
        if (janela.ShowDialog() == true && janela.Resultado is { } p)
        {
            _lista[_lista.IndexOf(atual)] = p;
            Salvar();
            Atualizar(p);
        }
    }

    void Lista_DuploClique(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Selecionado != null) Editar_Click(sender, e);
    }

    void Remover_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        if (Selecionado is not { } p) { TextoMensagem.Text = "Escolha um programa na lista."; return; }
        _lista.Remove(p);
        Salvar();
        Atualizar();
    }

    void Restaurar_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Substituir a lista atual pela lista inicial (Calculadora, Bloco de Notas, Paint, Explorador)?",
                "Restaurar lista inicial", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _lista.Clear();
        _lista.AddRange(Programas.Padrao);
        Salvar();
        Atualizar();
        TextoMensagem.Text = "Lista inicial restaurada.";
    }
}

/// <summary>Adiciona ou edita um programa da lista (nome amigável + o que abrir).</summary>
public partial class JanelaEditarPrograma : Window
{
    public ProgramaPermitido? Resultado { get; private set; }

    public JanelaEditarPrograma(ProgramaPermitido? existente)
    {
        InitializeComponent();
        Title = existente == null ? "Adicionar programa" : "Editar programa";
        TextoTitulo.Text = Title;
        CampoNome.Text = existente?.Nome ?? "";
        CampoComando.Text = existente?.Comando ?? "";
        Loaded += (_, _) => CampoNome.Focus();
    }

    void Procurar_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog { Title = "Escolher programa", Filter = "Programas (*.exe)|*.exe" };
        if (dialogo.ShowDialog(this) == true)
        {
            CampoComando.Text = dialogo.FileName;
            if (CampoNome.Text.Trim().Length == 0)
                CampoNome.Text = System.IO.Path.GetFileNameWithoutExtension(dialogo.FileName);
        }
    }

    void Salvar_Click(object sender, RoutedEventArgs e)
    {
        if (Programas.ValidarNome(CampoNome.Text) is { } erroNome) { TextoErro.Text = erroNome; return; }
        if (Programas.ValidarComando(CampoComando.Text) is { } erroCmd) { TextoErro.Text = erroCmd; return; }
        Resultado = new ProgramaPermitido(CampoNome.Text.Trim(), CampoComando.Text.Trim());
        DialogResult = true;
    }
}
