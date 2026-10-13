using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

public enum ModoPerfil { PrimeiroAcesso, Novo, TrocarPin }

public partial class JanelaNovoProfessor : Window
{
    readonly RepositorioPerfis _repositorio;
    readonly ModoPerfil _modo;
    readonly PerfilProfessor? _perfil;
    readonly AvisoDePin _aviso;

    public PerfilProfessor? Resultado { get; private set; }

    public JanelaNovoProfessor(RepositorioPerfis repositorio, ModoPerfil modo, PerfilProfessor? perfil = null)
    {
        _repositorio = repositorio;
        _modo = modo;
        _perfil = perfil;
        InitializeComponent();
        _aviso = new AvisoDePin(TextoErro, BotaoOk);
        foreach (var caixa in new[] { CampoPinAtual, CampoPin, CampoConfirma })
            caixa.PreviewTextInput += CaixaDePin.SoNumeros;

        switch (modo)
        {
            case ModoPerfil.PrimeiroAcesso:
                Title = "ControleSala — Primeiro acesso";
                TextoTitulo.Text = "Primeiro acesso";
                TextoAjuda.Text = "Cadastre o primeiro professor. Os outros podem ser adicionados depois, em \"Professores...\".";
                PainelPinAtual.Visibility = Visibility.Collapsed;
                BotaoCancelar.Content = "Sair";
                BotaoOk.Content = "Começar";
                break;
            case ModoPerfil.Novo:
                Title = "Novo professor";
                TextoTitulo.Text = "Novo professor";
                TextoAjuda.Text = "O PIN é pessoal: é com ele que o professor entra no programa.";
                PainelPinAtual.Visibility = Visibility.Collapsed;
                break;
            case ModoPerfil.TrocarPin:
                Title = "Trocar PIN";
                TextoTitulo.Text = $"Trocar PIN — {perfil!.Nome}";
                TextoAjuda.Text = "Digite o PIN atual e escolha o novo.";
                PainelNome.Visibility = Visibility.Collapsed;
                TextoRotuloPin.Text = "Novo PIN (4 a 8 números)";
                break;
        }
        Loaded += (_, _) => (_modo == ModoPerfil.TrocarPin ? (UIElement)CampoPinAtual : CampoNome).Focus();
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        _aviso.Limpar();
        if (CampoPin.Password != CampoConfirma.Password)
        {
            TextoErro.Text = "Os dois PINs não são iguais.";
            return;
        }
        try
        {
            if (_modo == ModoPerfil.TrocarPin)
            {
                var r = _repositorio.TrocarPin(_perfil!, CampoPinAtual.Password, CampoPin.Password, out var espera);
                if (r != ResultadoPin.Correto)
                {
                    CampoPinAtual.Clear();
                    _aviso.Mostrar(r, _perfil!, espera);
                    return;
                }
                Resultado = _perfil;
            }
            else
            {
                Resultado = _repositorio.Adicionar(CampoNome.Text, CampoPin.Password);
            }
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            TextoErro.Text = ex.Message;
        }
    }
}
