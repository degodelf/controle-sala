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
    readonly bool _pinOpcional;

    public PerfilProfessor? Resultado { get; private set; }

    public JanelaNovoProfessor(RepositorioPerfis repositorio, ModoPerfil modo, PerfilProfessor? perfil = null)
    {
        _repositorio = repositorio;
        _modo = modo;
        _perfil = perfil;
        _pinOpcional = modo != ModoPerfil.TrocarPin || !RepositorioPerfis.TemPin(perfil!); // quem já tem PIN só troca, sem deixar em branco
        InitializeComponent();
        _aviso = new AvisoDePin(TextoErro, BotaoOk);
        foreach (var caixa in new[] { CampoPinAtual, CampoPin, CampoConfirma })
            caixa.PreviewTextInput += CaixaDePin.SoNumeros;

        switch (modo)
        {
            case ModoPerfil.PrimeiroAcesso:
                Title = "ControleSala — Primeiro acesso";
                TextoTitulo.Text = "Primeiro acesso";
                TextoAjuda.Text = "Cadastre o primeiro professor. O PIN é opcional: sem ele, o programa abre direto.";
                PainelPinAtual.Visibility = Visibility.Collapsed;
                BotaoCancelar.Content = "Sair";
                BotaoOk.Content = "Começar";
                break;
            case ModoPerfil.Novo:
                Title = "Novo professor";
                TextoTitulo.Text = "Novo professor";
                TextoAjuda.Text = "O PIN é pessoal e opcional. Sem ele, o professor entra sem senha.";
                PainelPinAtual.Visibility = Visibility.Collapsed;
                break;
            case ModoPerfil.TrocarPin:
                bool tinhaPin = RepositorioPerfis.TemPin(perfil!);
                Title = tinhaPin ? "Trocar senha" : "Nome e senha";
                TextoTitulo.Text = tinhaPin ? $"Trocar senha — {perfil!.Nome}" : "Nome e senha";
                TextoAjuda.Text = tinhaPin
                    ? "Digite o PIN atual e escolha o novo."
                    : "O seu nome aparece no registro de atividades. O PIN é opcional: sem ele, o programa abre direto.";
                CampoNome.Text = perfil!.Nome;
                if (tinhaPin) PainelNome.Visibility = Visibility.Collapsed;
                else PainelPinAtual.Visibility = Visibility.Collapsed;   // não há PIN atual a conferir
                if (tinhaPin) TextoRotuloPin.Text = "Novo PIN (4 a 8 números)";
                break;
        }
        if (_pinOpcional) TextoRotuloPin.Text = "PIN (4 a 8 números, opcional)";
        Loaded += (_, _) =>
        {
            if (_modo == ModoPerfil.TrocarPin && PainelNome.Visibility != Visibility.Visible)
                (PainelPinAtual.Visibility == Visibility.Visible ? (UIElement)CampoPinAtual : CampoPin).Focus();
            else
                CampoNome.Focus();
        };
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        _aviso.Limpar();
        if (CampoPin.Password != CampoConfirma.Password)
        {
            TextoErro.Text = "Os dois PINs não são iguais.";
            return;
        }
        bool semPin = _pinOpcional && CampoPin.Password.Length == 0;
        try
        {
            if (_modo == ModoPerfil.TrocarPin)
            {
                if (PainelNome.Visibility == Visibility.Visible) _repositorio.Renomear(_perfil!, CampoNome.Text);
                if (!semPin)
                {
                    var r = _repositorio.TrocarPin(_perfil!, CampoPinAtual.Password, CampoPin.Password, out var espera);
                    if (r != ResultadoPin.Correto)
                    {
                        CampoPinAtual.Clear();
                        _aviso.Mostrar(r, _perfil!, espera);
                        return;
                    }
                }
                Resultado = _perfil;
            }
            else
            {
                Resultado = semPin ? _repositorio.AdicionarSemPin(CampoNome.Text) : _repositorio.Adicionar(CampoNome.Text, CampoPin.Password);
            }
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            TextoErro.Text = ex.Message;
        }
    }
}
