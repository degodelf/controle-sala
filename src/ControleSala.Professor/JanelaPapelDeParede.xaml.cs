using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ControleSala.Professor;

public partial class JanelaPapelDeParede : Window
{
    static readonly (int Largura, int Altura)[] Resolucoes = [(1920, 1080), (1366, 768), (1600, 900), (1280, 1024)];
    const string FiltroImagens = "Imagens|*.png;*.jpg;*.jpeg;*.bmp|Todos os arquivos|*.*";

    readonly ConfigPapelDeParede _config = ConfigPapelDeParede.Carregar();
    BitmapSource? _fundo, _logo;
    bool _carregando = true;

    public JanelaPapelDeParede()
    {
        InitializeComponent();
        CarregarImagens();

        ListaPosicao.SelectedIndex = (int)_config.Posicao;
        DeslizeTamanho.Value = _config.TamanhoPercent;
        TextoTamanho.Text = $"{_config.TamanhoPercent}%";
        int r = Array.FindIndex(Resolucoes, x => x.Largura == _config.Largura && x.Altura == _config.Altura);
        ListaResolucao.SelectedIndex = Math.Max(r, 0);
        CaixaCredito.IsChecked = _config.MostrarCredito;
        CampoFundo.Text = _config.Fundo ?? "";
        CampoLogo.Text = _config.Logo ?? "";

        _carregando = false;
        Atualizar();
    }

    void CarregarImagens()
    {
        TextoAviso.Text = "";
        _fundo = Abrir(_config.Fundo, "o fundo");
        _logo = Abrir(_config.Logo, "o logo");
    }

    BitmapSource? Abrir(string? caminho, string nome)
    {
        if (string.IsNullOrEmpty(caminho)) return null;
        try
        {
            return PapelDeParede.CarregarImagem(caminho);
        }
        catch (Exception)
        {
            TextoAviso.Text += $"Não foi possível abrir {nome} ({Path.GetFileName(caminho)}). ";
            return null;
        }
    }

    void EscolherFundo_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog { Title = "Imagem de fundo", Filter = FiltroImagens };
        if (dialogo.ShowDialog(this) != true) return;
        _config.Fundo = dialogo.FileName;
        CampoFundo.Text = dialogo.FileName;
        CarregarImagens();
        Atualizar();
    }

    void EscolherLogo_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog { Title = "Logotipo da escola", Filter = FiltroImagens };
        if (dialogo.ShowDialog(this) != true) return;
        _config.Logo = dialogo.FileName;
        CampoLogo.Text = dialogo.FileName;
        CarregarImagens();
        Atualizar();
    }

    void Opcao_Alterada(object sender, RoutedEventArgs e) => Atualizar();

    void Tamanho_Alterado(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TextoTamanho == null) return; // evento disparado durante o InitializeComponent
        TextoTamanho.Text = $"{(int)e.NewValue}%";
        Atualizar();
    }

    void LerControles()
    {
        _config.Posicao = (PosicaoLogo)Math.Max(ListaPosicao.SelectedIndex, 0);
        _config.TamanhoPercent = (int)DeslizeTamanho.Value;
        var (largura, altura) = Resolucoes[Math.Max(ListaResolucao.SelectedIndex, 0)];
        _config.Largura = largura;
        _config.Altura = altura;
        _config.MostrarCredito = CaixaCredito.IsChecked == true;
    }

    void Atualizar()
    {
        if (_carregando) return;
        LerControles();
        Previa.Source = PapelDeParede.Compor(_config, _fundo, _logo);
    }

    void SalvarImagem_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new SaveFileDialog
        {
            Title = "Salvar papel de parede",
            Filter = "Imagem PNG|*.png",
            FileName = "papel-de-parede-sala.png",
        };
        if (dialogo.ShowDialog(this) != true) return;
        try
        {
            LerControles();
            PapelDeParede.SalvarPng(PapelDeParede.Compor(_config, _fundo, _logo), dialogo.FileName);
            TextoAviso.Text = $"Imagem salva em {dialogo.FileName}";
        }
        catch (Exception ex)
        {
            TextoAviso.Text = $"Não foi possível salvar: {ex.Message}";
        }
    }

    void Window_Closed(object? sender, EventArgs e)
    {
        try
        {
            LerControles();
            _config.Salvar();
            // Salva também o papel de parede composto (fundo + logo), que é enviado aos alunos na limpeza.
            PapelDeParede.SalvarPng(PapelDeParede.Compor(_config, _fundo, _logo), Sessao.CaminhoPapelComposto);
            Sessao.AtualizarLimpeza();
        }
        catch (Exception)
        {
            // Sem permissão para gravar a configuração: não impede de fechar a janela.
        }
    }
}
