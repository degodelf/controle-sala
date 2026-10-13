using System.IO;
using System.Windows;
using ControleSala.Comum;
using Microsoft.Win32;

namespace ControleSala.Professor;

/// <summary>Cria a sala (escola, nome e chave secreta) e gera o pacote protegido por senha que leva a chave aos PCs dos alunos.</summary>
public partial class JanelaSala : Window
{
    const string FiltroPacote = "Pacote de sala do ControleSala|*.controlesala";

    public JanelaSala()
    {
        InitializeComponent();
        Atualizar();
    }

    void Atualizar()
    {
        var sala = Sessao.Sala;
        PainelCriar.Visibility = sala == null ? Visibility.Visible : Visibility.Collapsed;
        PainelPronta.Visibility = sala == null ? Visibility.Collapsed : Visibility.Visible;
        if (sala == null)
        {
            TextoAjuda.Text = "Antes de usar, crie a sala. O programa gera uma chave secreta que só os computadores " +
                              "da sala conhecem: sem ela ninguém consegue ver nem controlar os computadores dos alunos.";
            return;
        }
        TextoAjuda.Text = "A sala está criada neste computador.";
        TextoEscola.Text = sala.Escola;
        TextoSala.Text = sala.Sala;
        TextoImpressao.Text = sala.Impressao();
    }

    void Criar_Click(object sender, RoutedEventArgs e)
    {
        string escola = CampoEscola.Text.Trim(), nome = CampoSala.Text.Trim();
        if (escola.Length == 0 || nome.Length == 0)
        {
            TextoMensagem.Text = "Preencha o nome da escola e o nome da sala.";
            return;
        }
        try
        {
            Sessao.DefinirSala(ConfigSala.Nova(escola, nome));
            TextoMensagem.Text = "";
            Atualizar();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TextoMensagem.Text = $"Não foi possível salvar a sala: {ex.Message}";
        }
    }

    void GerarPacote_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        if (Sessao.Sala is not { } sala) return;

        var senha = new JanelaSenhaPacote("Senha do pacote",
            "Escolha uma senha para proteger o pacote. Ela será pedida na instalação de cada computador de aluno. " +
            "Anote em lugar seguro: sem ela o pacote não abre.", confirmar: true) { Owner = this };
        if (senha.ShowDialog() != true) return;

        var salvar = new SaveFileDialog
        {
            Title = "Salvar pacote da sala",
            Filter = FiltroPacote,
            FileName = NomeDeArquivo(sala.Sala) + ".controlesala",
        };
        if (salvar.ShowDialog(this) != true) return;

        try
        {
            File.WriteAllBytes(salvar.FileName, PacoteSala.Exportar(sala, senha.Senha));
            TextoMensagem.Text = $"Pacote salvo em {salvar.FileName}. Copie para o pendrive junto com o ControleSala-Setup e apague do pendrive depois de instalar.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TextoMensagem.Text = $"Não foi possível salvar: {ex.Message}";
        }
    }

    void Restaurar_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        var abrir = new OpenFileDialog { Title = "Abrir pacote da sala", Filter = FiltroPacote };
        if (abrir.ShowDialog(this) != true) return;

        var senha = new JanelaSenhaPacote("Senha do pacote", "Digite a senha que foi escolhida ao gerar o pacote.", confirmar: false) { Owner = this };
        if (senha.ShowDialog() != true) return;

        ConfigSala nova;
        try
        {
            nova = PacoteSala.Importar(File.ReadAllBytes(abrir.FileName), senha.Senha);
        }
        catch (SenhaPacoteIncorretaException)
        {
            TextoMensagem.Text = "Senha incorreta.";
            return;
        }
        catch (Exception ex) when (ex is PacoteInvalidoException or IOException)
        {
            TextoMensagem.Text = ex.Message;
            return;
        }

        if (Sessao.Sala != null && Sessao.Sala.Impressao() != nova.Impressao()
            && MessageBox.Show(this,
                $"Este computador já tem outra sala ({Sessao.Sala.Sala}).\n\nSubstituir pela sala \"{nova.Sala}\" do pacote? " +
                "Os computadores de aluno que estão com a chave antiga deixarão de aceitar este professor.",
                "Substituir a sala", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        Sessao.DefinirSala(nova);
        Atualizar();
        TextoMensagem.Text = $"Sala \"{nova.Sala}\" restaurada.";
    }

    static string NomeDeArquivo(string nome)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) nome = nome.Replace(c, '_');
        return nome.Trim();
    }
}
