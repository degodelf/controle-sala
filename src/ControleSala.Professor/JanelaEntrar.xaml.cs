using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Com mais de um professor no PC, pergunta quem está usando, para o registro de atividades.</summary>
public partial class JanelaEntrar : Window
{
    public PerfilProfessor? Escolhido { get; private set; }

    public JanelaEntrar(RepositorioPerfis repositorio)
    {
        InitializeComponent();
        TextoCreditos.Text = Creditos.Linha;
        ListaProfessores.ItemsSource = repositorio.Perfis;
        ListaProfessores.SelectedIndex = 0;
    }

    void Entrar_Click(object sender, RoutedEventArgs e)
    {
        if (ListaProfessores.SelectedItem is not PerfilProfessor perfil) return;
        Escolhido = perfil;
        DialogResult = true;
    }
}
