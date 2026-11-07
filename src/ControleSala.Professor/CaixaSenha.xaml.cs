using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media;

namespace ControleSala.Professor;

/// <summary>Campo de senha com o olhinho que mostra o que foi digitado. Começa escondido e volta a esconder ao limpar.</summary>
public partial class CaixaSenha : UserControl
{
    // O PasswordBox não tem CaretIndex: só este método interno leva o cursor para o fim do texto.
    static readonly MethodInfo? PosicionarCursor =
        typeof(PasswordBox).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic);

    bool _mostrando;

    public CaixaSenha() => InitializeComponent();

    public int MaxLength
    {
        get => Oculta.MaxLength;
        set { Oculta.MaxLength = value; Visivel.MaxLength = value; }
    }

    public string Password => _mostrando ? Visivel.Text : Oculta.Password;

    public void Clear()
    {
        Oculta.Clear();
        Visivel.Clear();
        Trocar(false);
    }

    Control Ativo => _mostrando ? Visivel : Oculta;

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (ReferenceEquals(e.NewFocus, this)) Ativo.Focus();
    }

    void Olho_Click(object sender, RoutedEventArgs e)
    {
        Trocar(!_mostrando);
        Ativo.Focus();
        if (_mostrando) Visivel.CaretIndex = Visivel.Text.Length;
        else PosicionarCursor?.Invoke(Oculta, [Oculta.Password.Length, 0]);
    }

    void Trocar(bool mostrar)
    {
        if (mostrar == _mostrando) return;
        string senha = Password;
        _mostrando = mostrar;
        if (mostrar)
        {
            Visivel.Text = senha;
            Oculta.Clear();
        }
        else
        {
            Oculta.Password = senha;
            Visivel.Clear();
        }
        Oculta.Visibility = mostrar ? Visibility.Collapsed : Visibility.Visible;
        Visivel.Visibility = mostrar ? Visibility.Visible : Visibility.Collapsed;
        Icone.Data = (Geometry)FindResource(mostrar ? "IconeOlhoFechado" : "IconeOlho");
        string dica = mostrar ? "Esconder a senha" : "Mostrar a senha";
        BotaoOlho.ToolTip = dica;
        AutomationProperties.SetName(BotaoOlho, dica);
    }
}
