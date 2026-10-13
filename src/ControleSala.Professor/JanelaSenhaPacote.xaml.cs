using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Pede a senha do pacote da sala (nova, com confirmação, ou só para abrir um pacote existente).</summary>
public partial class JanelaSenhaPacote : Window
{
    readonly bool _confirmar;

    public string Senha { get; private set; } = "";

    public JanelaSenhaPacote(string titulo, string ajuda, bool confirmar)
    {
        _confirmar = confirmar;
        InitializeComponent();
        Title = titulo;
        TextoTitulo.Text = titulo;
        TextoAjuda.Text = ajuda;
        if (!confirmar) PainelConfirma.Visibility = Visibility.Collapsed;
        Loaded += (_, _) => CampoSenha.Focus();
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_confirmar)
        {
            if (CampoSenha.Password.Length < PacoteSala.SenhaMinima)
            {
                TextoErro.Text = $"A senha deve ter pelo menos {PacoteSala.SenhaMinima} caracteres.";
                return;
            }
            if (CampoSenha.Password != CampoConfirma.Password)
            {
                TextoErro.Text = "As duas senhas não são iguais.";
                return;
            }
        }
        else if (CampoSenha.Password.Length == 0)
        {
            TextoErro.Text = "Digite a senha do pacote.";
            return;
        }
        Senha = CampoSenha.Password;
        DialogResult = true;
    }
}
