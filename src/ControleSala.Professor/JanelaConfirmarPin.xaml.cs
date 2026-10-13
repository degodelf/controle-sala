using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Pede de novo o PIN do professor que está usando o programa, antes de uma ação sensível.</summary>
public partial class JanelaConfirmarPin : Window
{
    readonly RepositorioPerfis _repositorio;
    readonly PerfilProfessor _perfil;
    readonly AvisoDePin _aviso;

    public JanelaConfirmarPin(RepositorioPerfis repositorio, PerfilProfessor perfil, string pergunta)
    {
        _repositorio = repositorio;
        _perfil = perfil;
        InitializeComponent();
        _aviso = new AvisoDePin(TextoErro, BotaoOk);
        CampoPin.PreviewTextInput += CaixaDePin.SoNumeros;
        TextoPergunta.Text = pergunta;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        var r = _repositorio.Verificar(_perfil, CampoPin.Password, out var espera);
        if (r == ResultadoPin.Correto)
        {
            DialogResult = true;
            return;
        }
        CampoPin.Clear();
        CampoPin.Focus();
        _aviso.Mostrar(r, _perfil, espera);
    }
}
