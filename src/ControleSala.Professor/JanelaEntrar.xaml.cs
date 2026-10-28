using System.Windows;
using System.Windows.Controls;
using ControleSala.Comum;

namespace ControleSala.Professor;

public partial class JanelaEntrar : Window
{
    readonly RepositorioPerfis _repositorio;
    readonly AvisoDePin _aviso;

    public PerfilProfessor? Escolhido { get; private set; }

    public JanelaEntrar(RepositorioPerfis repositorio)
    {
        _repositorio = repositorio;
        InitializeComponent();
        _aviso = new AvisoDePin(TextoErro, BotaoEntrar);
        CampoPin.PreviewTextInput += CaixaDePin.SoNumeros;
        TextoCreditos.Text = Creditos.Linha;
        ListaProfessores.ItemsSource = repositorio.Perfis;
        ListaProfessores.SelectedIndex = 0;
        AtualizarCampoPin();
    }

    void Professor_Alterado(object sender, SelectionChangedEventArgs e)
    {
        if (_aviso == null) return; // evento disparado durante o InitializeComponent
        _aviso.Limpar();
        CampoPin.Clear();
        AtualizarCampoPin();
    }

    /// <summary>Esconde o campo de PIN para quem entra sem senha.</summary>
    void AtualizarCampoPin()
    {
        bool temPin = ListaProfessores.SelectedItem is PerfilProfessor p && RepositorioPerfis.TemPin(p);
        PainelPin.Visibility = temPin ? Visibility.Visible : Visibility.Collapsed;
        if (temPin) CampoPin.Focus(); else BotaoEntrar.Focus();
    }

    void Entrar_Click(object sender, RoutedEventArgs e)
    {
        if (ListaProfessores.SelectedItem is not PerfilProfessor perfil) return;
        var resultado = _repositorio.Verificar(perfil, CampoPin.Password, out var espera);
        if (resultado == ResultadoPin.Correto)
        {
            Escolhido = perfil;
            DialogResult = true;
            return;
        }
        CampoPin.Clear();
        CampoPin.Focus();
        _aviso.Mostrar(resultado, perfil, espera);
    }
}
