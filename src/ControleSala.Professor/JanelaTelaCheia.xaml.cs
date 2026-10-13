using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Um computador da sala em tela cheia: pede ao agente o vídeo completo (alto FPS) enquanto estiver aberta.</summary>
public partial class JanelaTelaCheia : Window
{
    readonly ConexaoComputador _conexao;
    readonly ConexaoComputador.UsarQuadro _usarQuadro;
    WriteableBitmap? _bitmap;

    long _inicioEstat = Stopwatch.GetTimestamp();
    long _bytesAntes;
    int _quadrosExibidos;
    double _somaMsAgente, _somaMsProfessor;

    internal JanelaTelaCheia(ConexaoComputador conexao)
    {
        _conexao = conexao;
        _usarQuadro = UsarQuadro;
        InitializeComponent();
        Title = $"{conexao.Dados.Nome} — tela";
        TextoCreditos.Text = Creditos.Linha;
        TextoMensagem.Text = "Aguardando imagem...";
        _bytesAntes = conexao.BytesRecebidos;
        conexao.Modo = ModoVideo.Completo;
        CompositionTarget.Rendering += AoRenderizar;
    }

    void AoRenderizar(object? sender, EventArgs e)
    {
        bool desenhou = _conexao.PegarQuadroNovo(_usarQuadro);
        if (_conexao.Estado != EstadoConexao.Online)
            TextoMensagem.Text = _conexao.Estado == EstadoConexao.Conectando ? "Conectando..." : $"Offline — {_conexao.Detalhe}";
        else if (desenhou)
            TextoMensagem.Text = "";

        long agora = Stopwatch.GetTimestamp();
        double s = (double)(agora - _inicioEstat) / Stopwatch.Frequency;
        if (s < 1) return;
        long bytes = _conexao.BytesRecebidos;
        double n = Math.Max(1, _quadrosExibidos);
        TextoEstatisticas.Text = _quadrosExibidos == 0 ? "" :
            $"{_quadrosExibidos / s,5:0.0} fps | {(bytes - _bytesAntes) * 8 / s / 1e6,5:0.00} Mbps | " +
            $"agente (captura→envio) {_somaMsAgente / n,5:0.0} ms | professor (chegada→tela) {_somaMsProfessor / n,5:0.0} ms";
        _inicioEstat = agora;
        _bytesAntes = bytes;
        _quadrosExibidos = 0;
        _somaMsAgente = _somaMsProfessor = 0;
    }

    void UsarQuadro(IntPtr bgra, int largura, int altura, int stride, long momentoRecebido, int usAgente)
    {
        // Os primeiros quadros depois de abrir ainda podem ser miniaturas; aparecem esticados por um instante.
        if (_bitmap == null || _bitmap.PixelWidth != largura || _bitmap.PixelHeight != altura)
        {
            _bitmap = new WriteableBitmap(largura, altura, 96, 96, PixelFormats.Bgr32, null);
            Tela.Source = _bitmap;
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, largura, altura), bgra, stride * altura, stride);
        _quadrosExibidos++;
        _somaMsAgente += usAgente / 1000.0;
        _somaMsProfessor += (Stopwatch.GetTimestamp() - momentoRecebido) * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>Texto da barra de estatísticas e da mensagem (usado pela ferramenta de captura).</summary>
    internal string Resumo => $"{TextoEstatisticas.Text} | {TextoMensagem.Text}";

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    void Window_Closed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= AoRenderizar;
        _conexao.Modo = ModoVideo.Miniatura; // volta para a grade leve
    }
}
