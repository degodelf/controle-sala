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
    EstadoConexao? _ultimoEstado;
    string _ultimoDetalhe = "";

    public ConexaoComputador Conexao { get; }
    public string Nome => Conexao.Dados.Nome;
    public string Endereco => $"{Conexao.Dados.Endereco}:{Conexao.Dados.Porta}";

    public TileVm(ConexaoComputador conexao) => Conexao = conexao;

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

    public event PropertyChangedEventHandler? PropertyChanged;
    void Mudou([CallerMemberName] string? nome = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}
