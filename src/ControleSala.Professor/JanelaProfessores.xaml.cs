using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

public partial class JanelaProfessores : Window
{
    sealed record Item(PerfilProfessor Perfil, string Texto);

    readonly RepositorioPerfis _repositorio;
    readonly PerfilProfessor _atual;

    public JanelaProfessores(RepositorioPerfis repositorio, PerfilProfessor atual)
    {
        _repositorio = repositorio;
        _atual = atual;
        InitializeComponent();
        Atualizar();
    }

    void Atualizar()
    {
        Lista.ItemsSource = _repositorio.Perfis
            .Select(p => new Item(p, Rotulo(p)))
            .ToList();
    }

    string Rotulo(PerfilProfessor p) => p == _atual ? $"{p.Nome}   (em uso)" : p.Nome;

    PerfilProfessor? Selecionado => (Lista.SelectedItem as Item)?.Perfil;

    void Adicionar_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        if (new JanelaNovoProfessor(_repositorio, ModoPerfil.Novo) { Owner = this }.ShowDialog() == true)
            Atualizar();
    }

    void Remover_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        if (Selecionado is not { } perfil)
        {
            TextoMensagem.Text = "Escolha um professor na lista.";
            return;
        }
        if (perfil == _atual)
        {
            TextoMensagem.Text = "Você não pode remover quem está usando o programa agora.";
            return;
        }
        if (_repositorio.Perfis.Count <= 1)
        {
            TextoMensagem.Text = "É preciso manter pelo menos um professor.";
            return;
        }
        if (MessageBox.Show(this, $"Remover o professor {perfil.Nome}?", "Remover professor",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (_repositorio.Remover(perfil))
        {
            Atualizar();
            TextoMensagem.Text = $"{perfil.Nome} foi removido.";
        }
    }
}
