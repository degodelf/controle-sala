using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

public enum ModoPerfil { PrimeiroAcesso, Novo, ConfirmarNome }

public partial class JanelaNovoProfessor : Window
{
    readonly RepositorioPerfis _repositorio;
    readonly ModoPerfil _modo;
    readonly PerfilProfessor? _perfil;

    public PerfilProfessor? Resultado { get; private set; }

    public JanelaNovoProfessor(RepositorioPerfis repositorio, ModoPerfil modo, PerfilProfessor? perfil = null)
    {
        _repositorio = repositorio;
        _modo = modo;
        _perfil = perfil;
        InitializeComponent();

        switch (modo)
        {
            case ModoPerfil.PrimeiroAcesso:
                Title = "ControleSala — Primeiro acesso";
                TextoTitulo.Text = "Primeiro acesso";
                TextoAjuda.Text = "Digite o seu nome. Ele aparece no registro de atividades e para o aluno.";
                BotaoCancelar.Content = "Sair";
                BotaoOk.Content = "Começar";
                break;
            case ModoPerfil.Novo:
                Title = "Novo professor";
                TextoTitulo.Text = "Novo professor";
                TextoAjuda.Text = "O nome aparece no registro de atividades e para o aluno.";
                break;
            case ModoPerfil.ConfirmarNome:
                Title = "Seu nome";
                TextoTitulo.Text = "Seu nome";
                TextoAjuda.Text = "O seu nome aparece no registro de atividades e para o aluno.";
                CampoNome.Text = perfil!.Nome;
                break;
        }
        Loaded += (_, _) => CampoNome.Focus();
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_modo == ModoPerfil.ConfirmarNome)
            {
                _repositorio.Renomear(_perfil!, CampoNome.Text);
                Resultado = _perfil;
            }
            else
            {
                Resultado = _repositorio.Adicionar(CampoNome.Text);
            }
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            TextoErro.Text = ex.Message;
        }
    }
}
