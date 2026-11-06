using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>O que a grade mostra de um computador: miniatura, nome e situação da conexão.</summary>
sealed class TileVm : INotifyPropertyChanged
{
    static readonly Brush Verde = Pincel(0x4C, 0xAF, 0x50);
    static readonly Brush Ambar = Pincel(0xFF, 0xC1, 0x07);
    static readonly Brush Vermelho = Pincel(0xE5, 0x39, 0x35);
    static readonly Brush BordaNormal = Pincel(0x3C, 0x3C, 0x3C);
    static readonly Brush BordaOffline = Pincel(0x5A, 0x2B, 0x2B);

    static Brush Pincel(byte r, byte g, byte b)
    {
        var p = new SolidColorBrush(Color.FromRgb(r, g, b));
        p.Freeze();
        return p;
    }

    double _largura = 300;
    WriteableBitmap? _imagem;
    string _mensagem = "Conectando...";
    Brush _corEstado = Ambar, _corBorda = BordaNormal;
    double _opacidade = 1;
    bool _selecionado;
    EstadoConexao? _ultimoEstado;
    string _ultimoDetalhe = "";

    public ConexaoComputador Conexao { get; }
    public string Nome => Conexao.Dados.Nome;
    public string Endereco => $"{Conexao.Dados.Endereco}:{Conexao.Dados.Porta}";

    /// <summary>Avisa a grade que o nome ou o endereço do computador mudou, sem recriar a conexão.</summary>
    public void AtualizarDados()
    {
        Mudou(nameof(Nome));
        Mudou(nameof(Endereco));
    }

    public TileVm(ConexaoComputador conexao) => Conexao = conexao;

    /// <summary>Se este computador está marcado na grade (contorno azul + selo). A seleção vale para os comandos.</summary>
    public bool Selecionado
    {
        get => _selecionado;
        set { if (_selecionado == value) return; _selecionado = value; Mudou(); }
    }

    public double Largura
    {
        get => _largura;
        set
        {
            if (_largura == value) return;
            _largura = value;
            Mudou();
            Mudou(nameof(AlturaImagem));
        }
    }

    /// <summary>A área da imagem é sempre 16:9 (telas 4:3 ficam com faixas nas laterais).</summary>
    public double AlturaImagem => Math.Round(_largura * 9 / 16);

    public WriteableBitmap? Imagem { get => _imagem; private set { _imagem = value; Mudou(); } }
    public string Mensagem { get => _mensagem; private set { if (_mensagem == value) return; _mensagem = value; Mudou(); } }
    public Brush CorEstado { get => _corEstado; private set { _corEstado = value; Mudou(); } }
    public Brush CorBorda { get => _corBorda; private set { _corBorda = value; Mudou(); } }
    public double Opacidade { get => _opacidade; private set { if (_opacidade == value) return; _opacidade = value; Mudou(); } }

    /// <summary>Atualiza cor, texto e transparência quando a situação da conexão muda (chamado a cada quadro da tela).</summary>
    public void AtualizarEstado()
    {
        var estado = Conexao.Estado;
        string detalhe = Conexao.Detalhe;
        if (estado == _ultimoEstado && detalhe == _ultimoDetalhe) return;
        _ultimoEstado = estado;
        _ultimoDetalhe = detalhe;

        switch (estado)
        {
            case EstadoConexao.Online:
                CorEstado = Verde;
                CorBorda = BordaNormal;
                Opacidade = 1;
                Mensagem = Imagem == null ? "Aguardando imagem..." : "";
                break;
            case EstadoConexao.Conectando:
                CorEstado = Ambar;
                CorBorda = BordaNormal;
                Opacidade = Imagem == null ? 1 : 0.4;
                Mensagem = "Conectando...";
                break;
            default:
                CorEstado = Vermelho;
                CorBorda = BordaOffline;
                Opacidade = 0.35;
                Mensagem = detalhe.Length == 0 ? "Offline" : $"Offline — {detalhe}";
                break;
        }
    }

    /// <summary>Pega o quadro novo (se houver) e desenha na miniatura. Retorna true se desenhou.</summary>
    public bool AtualizarImagem() => Conexao.PegarQuadroNovo(Desenhar);

    void Desenhar(IntPtr bgra, int largura, int altura, int stride, long momentoRecebido, int usAgente)
    {
        if (_imagem == null || _imagem.PixelWidth != largura || _imagem.PixelHeight != altura)
            Imagem = new WriteableBitmap(largura, altura, 96, 96, PixelFormats.Bgr32, null);
        _imagem!.WritePixels(new Int32Rect(0, 0, largura, altura), bgra, stride * altura, stride);
        if (_mensagem.Length != 0 && Conexao.Estado == EstadoConexao.Online)
            Mensagem = "";
        if (_opacidade != 1 && Conexao.Estado == EstadoConexao.Online)
            Opacidade = 1;
    }

    /// <summary>Só para a ferramenta de captura: mostra o computador online, com uma tela inventada (nunca a de um aluno).</summary>
    internal void MostrarExemplo(int numero)
    {
        Imagem = TelaDeExemplo(numero);
        CorEstado = Verde;
        CorBorda = BordaNormal;
        Opacidade = 1;
        Mensagem = "";
    }

    static WriteableBitmap TelaDeExemplo(int numero)
    {
        const int largura = 640, altura = 360;
        (Color Fundo, Color Claro, Color Destaque)[] temas =
        [
            (Color.FromRgb(0x0F, 0x4C, 0x5C), Color.FromRgb(0x2F, 0x7F, 0x8F), Color.FromRgb(0xE3, 0x64, 0x14)),
            (Color.FromRgb(0x2B, 0x3A, 0x67), Color.FromRgb(0x49, 0x6A, 0x81), Color.FromRgb(0x2F, 0x9E, 0x6E)),
            (Color.FromRgb(0x3D, 0x5A, 0x40), Color.FromRgb(0x77, 0x95, 0x6B), Color.FromRgb(0x2B, 0x6C, 0xB0)),
            (Color.FromRgb(0x5B, 0x3A, 0x6B), Color.FromRgb(0x8C, 0x6A, 0xA0), Color.FromRgb(0xC9, 0x8A, 0x12)),
        ];
        var tema = temas[numero % temas.Length];
        Brush Cor(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        var linha = Cor(Color.FromRgb(0xD5, 0xDC, 0xDF));

        var desenho = new DrawingVisual();
        using (var dc = desenho.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(tema.Fundo, tema.Claro, 35), null, new Rect(0, 0, largura, altura));
            dc.DrawRectangle(Cor(Color.FromRgb(0x20, 0x26, 0x2B)), null, new Rect(0, altura - 26, largura, 26));
            int esquerda = 70 + numero % 3 * 22, topo = 28 + numero % 2 * 14;
            dc.DrawRectangle(Brushes.White, null, new Rect(esquerda, topo, 450, 270));
            dc.DrawRectangle(Cor(tema.Destaque), null, new Rect(esquerda, topo, 450, 30));
            for (int i = 0; i < 2 + numero % 2; i++)
                dc.DrawRectangle(Cor(tema.Claro), null, new Rect(esquerda + 24 + i * 138, topo + 52, 124, 86));
            for (int i = 0; i < 5; i++)
                dc.DrawRectangle(linha, null, new Rect(esquerda + 24, topo + 158 + i * 19, 400 - (i + numero) % 4 * 70, 9));
        }
        var quadro = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
        quadro.Render(desenho);
        return new WriteableBitmap(quadro);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Mudou([CallerMemberName] string? nome = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}
