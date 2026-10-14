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

    // ---------- Controle remoto (mouse e teclado) ----------

    bool _controlando;
    long _ultimoMove;
    ushort _ultimoNx, _ultimoNy;
    static readonly long IntervaloMove = Stopwatch.Frequency / 120; // no máximo ~120 envios de movimento por segundo

    void Controlar_Checked(object sender, RoutedEventArgs e)
    {
        _controlando = true;
        TextoDica.Visibility = Visibility.Visible;
        Tela.Cursor = Cursors.Cross;
        Keyboard.Focus(this); // garante que as teclas cheguem a esta janela
    }

    void Controlar_Unchecked(object sender, RoutedEventArgs e)
    {
        _controlando = false;
        TextoDica.Visibility = Visibility.Collapsed;
        Tela.Cursor = Cursors.Arrow;
        if (Tela.IsMouseCaptured) Tela.ReleaseMouseCapture();
    }

    /// <summary>Converte um ponto sobre a imagem em coordenadas 0..65535 da tela do aluno (respeitando as faixas pretas).</summary>
    bool PosicaoNormalizada(Point p, out ushort nx, out ushort ny)
    {
        nx = ny = 0;
        if (_bitmap == null) return false;
        double iw = Tela.ActualWidth, ih = Tela.ActualHeight, fw = _bitmap.PixelWidth, fh = _bitmap.PixelHeight;
        if (iw <= 0 || ih <= 0 || fw <= 0 || fh <= 0) return false;
        double escala = Math.Min(iw / fw, ih / fh);
        double dw = fw * escala, dh = fh * escala, offX = (iw - dw) / 2, offY = (ih - dh) / 2;
        double fx = (p.X - offX) / dw, fy = (p.Y - offY) / dh;
        if (fx is < 0 or > 1 || fy is < 0 or > 1) return false;
        nx = (ushort)Math.Round(fx * 65535);
        ny = (ushort)Math.Round(fy * 65535);
        return true;
    }

    void Tela_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_controlando || !PosicaoNormalizada(e.GetPosition(Tela), out var nx, out var ny)) return;
        long agora = Stopwatch.GetTimestamp();
        if (agora - _ultimoMove < IntervaloMove) return; // limita a frequência
        if (nx == _ultimoNx && ny == _ultimoNy) return;
        _ultimoMove = agora;
        _ultimoNx = nx; _ultimoNy = ny;
        _conexao.EnviarEntrada(Protocolo.MontarEntradaMouseMover(nx, ny));
    }

    void Tela_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_controlando || Botao(e.ChangedButton) is not { } botao) return;
        if (!PosicaoNormalizada(e.GetPosition(Tela), out var nx, out var ny)) return;
        Tela.CaptureMouse();
        _conexao.EnviarEntrada(Protocolo.MontarEntradaMouseBotao(nx, ny, botao, true));
        e.Handled = true;
    }

    void Tela_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_controlando || Botao(e.ChangedButton) is not { } botao) return;
        PosicaoNormalizada(e.GetPosition(Tela), out var nx, out var ny);
        if (Tela.IsMouseCaptured) Tela.ReleaseMouseCapture();
        _conexao.EnviarEntrada(Protocolo.MontarEntradaMouseBotao(nx, ny, botao, false));
        e.Handled = true;
    }

    void Tela_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_controlando || !PosicaoNormalizada(e.GetPosition(Tela), out var nx, out var ny)) return;
        _conexao.EnviarEntrada(Protocolo.MontarEntradaMouseRoda(nx, ny, (short)Math.Clamp(e.Delta, short.MinValue, short.MaxValue)));
        e.Handled = true;
    }

    static BotaoMouse? Botao(MouseButton b) => b switch
    {
        MouseButton.Left => BotaoMouse.Esquerdo,
        MouseButton.Right => BotaoMouse.Direito,
        MouseButton.Middle => BotaoMouse.Meio,
        _ => null,
    };

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_controlando)
        {
            if (e.Key == Key.Escape) Close();
            return;
        }
        if (e.Key == Key.Escape) { BotaoControlar.IsChecked = false; e.Handled = true; return; } // Esc sai do controle
        EnviarTecla(e, pressionado: true);
        e.Handled = true;
    }

    void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_controlando) return;
        if (e.Key != Key.Escape) EnviarTecla(e, pressionado: false);
        e.Handled = true;
    }

    void EnviarTecla(KeyEventArgs e, bool pressionado)
    {
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(tecla);
        if (vk is <= 0 or > 255) return;
        _conexao.EnviarEntrada(Protocolo.MontarEntradaTecla((ushort)vk, pressionado, EhEstendida(tecla)));
    }

    /// <summary>Teclas "estendidas" (setas, navegação, teclas da direita), que precisam da marca correta no Windows.</summary>
    static bool EhEstendida(Key k) => k is Key.Left or Key.Right or Key.Up or Key.Down
        or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Insert or Key.Delete
        or Key.NumLock or Key.PrintScreen or Key.Apps or Key.RightCtrl or Key.RightAlt or Key.Divide;

    void Window_Closed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= AoRenderizar;
        _conexao.Modo = ModoVideo.Miniatura; // volta para a grade leve
    }
}
