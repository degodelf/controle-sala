using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>O que o aluno vê escolher na tela de aviso da limpeza.</summary>
enum ResultadoAviso { Proceder, Adiar }

/// <summary>Janelas que aparecem na tela do aluno: mensagem do professor, bloqueio, aviso e carregamento da limpeza.</summary>
static class JanelasAluno
{
    static BloqueioForm? _bloqueio;

    /// <summary>Mostra um aviso do professor, no canto da tela, que some sozinho em ~15 s ou quando o aluno clica em OK.</summary>
    public static void MostrarMensagem(string texto, string? professor)
    {
        var form = new MensagemForm(texto, professor);
        form.Show();
    }

    /// <summary>Cobre a tela do aluno com um aviso, até o professor desbloquear.</summary>
    public static void Bloquear(string texto)
    {
        _bloqueio?.Fechar();
        _bloqueio = new BloqueioForm(texto);
        _bloqueio.Show();
    }

    public static void Desbloquear()
    {
        _bloqueio?.Fechar();
        _bloqueio = null;
    }

    static LimpezaForm? _limpeza;

    /// <summary>Cobre a tela com a tela de carregamento ("Limpando, aguarde...") durante a limpeza.</summary>
    public static void MostrarLimpeza(string? imagem)
    {
        _limpeza?.Fechar();
        _limpeza = new LimpezaForm(imagem);
        _limpeza.Show();
    }

    public static void EsconderLimpeza()
    {
        _limpeza?.Fechar();
        _limpeza = null;
    }

    /// <summary>
    /// Mostra o aviso de limpeza automática (contagem regressiva + "Limpar agora" / "Adiar 1 hora") e espera a escolha.
    /// Chame de uma thread fora da interface: bloqueia até o aluno decidir (ou o tempo acabar = Proceder).
    /// </summary>
    public static ResultadoAviso PerguntarLimpeza(int segundos, string? imagem)
    {
        var escolha = ResultadoAviso.Proceder;
        InterfaceAgente.NaInterface(() =>
        {
            using var f = new AvisoLimpezaForm(segundos, imagem);
            f.ShowDialog();
            escolha = f.Resultado;
        });
        return escolha;
    }
}

/// <summary>Base das telas de tela cheia escura (aviso e carregamento da limpeza): cobre todos os monitores, fica por cima.</summary>
abstract class TelaCheiaEscura : Form
{
    protected static readonly Color CorFundo = Color.FromArgb(0x10, 0x14, 0x20);
    protected static readonly Color CorCartao = Color.FromArgb(0x1B, 0x21, 0x30);
    protected static readonly Color CorBorda = Color.FromArgb(0x33, 0x3C, 0x52);
    protected static readonly Color CorRealce = Color.FromArgb(0x2D, 0x6C, 0xDF);
    protected static readonly Color CorTexto = Color.White;
    protected static readonly Color CorTextoFraco = Color.FromArgb(0xA6, 0xAE, 0xBD);

    protected TelaCheiaEscura()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = CorFundo;

        var total = Rectangle.Empty;
        foreach (var tela in Screen.AllScreens) total = Rectangle.Union(total, tela.Bounds);
        if (total.IsEmpty) total = new Rectangle(0, 0, 1366, 768);
        Bounds = total;
    }

    /// <summary>Em prévia (geração de imagem de teste), desenha tudo relativo à janela, não aos monitores reais.</summary>
    protected bool ModoPrevia;

    /// <summary>Retângulo do monitor principal dentro desta janela (que cobre todos os monitores).</summary>
    protected Rectangle AreaPrincipal()
    {
        if (ModoPrevia) return new Rectangle(0, 0, Width, Height);
        var p = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, Width, Height);
        return new Rectangle(p.X - Left, p.Y - Top, p.Width, p.Height);
    }

    internal static GraphicsPath Arredondado(Rectangle r, int raio)
    {
        var p = new GraphicsPath();
        int d = raio * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Desenha o crédito discreto, centralizado embaixo do monitor principal.</summary>
    protected void DesenharCredito(Graphics g, Rectangle area)
    {
        using var fonte = new Font("Segoe UI", 9f);
        using var pincel = new SolidBrush(Color.FromArgb(0x80, 0x88, 0x96));
        using var centro = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(Creditos.Linha, fonte, pincel, new RectangleF(area.X, area.Bottom - 40, area.Width, 24), centro);
    }

    /// <summary>Desenha a imagem do professor (logo da escola) centralizada no topo da área indicada. Retorna a altura usada.</summary>
    protected static int DesenharImagem(Graphics g, Image? img, Rectangle area, int topo, int alturaMax)
    {
        if (img == null) return 0;
        double escala = Math.Min((double)alturaMax / img.Height, (double)(area.Width * 0.5) / img.Width);
        int w = (int)(img.Width * escala), h = (int)(img.Height * escala);
        g.DrawImage(img, area.X + (area.Width - w) / 2, topo, w, h);
        return h;
    }

    protected override bool ShowWithoutActivation => false;
    protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); BringToFront(); }

    protected static Image? CarregarImagem(string? caminho)
    {
        if (string.IsNullOrEmpty(caminho) || !File.Exists(caminho)) return null;
        try { using var fs = File.OpenRead(caminho); return Image.FromStream(fs); }
        catch (Exception) { return null; }
    }

    /// <summary>Gera uma imagem da tela (só para conferir o visual sem precisar de um aluno logado). Centraliza o cartão.</summary>
    internal Bitmap GerarPrevia(Panel cartao, int w, int h)
    {
        ModoPrevia = true;
        TopMost = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(-30000, 0, w, h); // bem fora da tela: ninguém vê
        Show();
        cartao.Location = new Point((w - cartao.Width) / 2, (h - cartao.Height) / 2);
        for (int i = 0; i < 3; i++) Application.DoEvents();
        var bmp = new Bitmap(w, h);
        DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
        Hide();
        return bmp;
    }
}

/// <summary>Aviso antes da limpeza automática: contagem regressiva, "Limpar agora" e "Adiar 1 hora".</summary>
sealed class AvisoLimpezaForm : TelaCheiaEscura
{
    public ResultadoAviso Resultado { get; private set; } = ResultadoAviso.Proceder;

    readonly Image? _imagem;
    readonly System.Windows.Forms.Timer _relogio = new() { Interval = 1000 };
    int _restante;
    readonly Label _contagem;
    readonly Panel _cartao;

    public AvisoLimpezaForm(int segundos, string? caminhoImagem)
    {
        _restante = Math.Max(5, segundos);
        _imagem = CarregarImagem(caminhoImagem);

        int lw = 620, lh = _imagem != null ? 410 : 300;
        _cartao = new Panel { Size = new Size(lw, lh), BackColor = CorCartao };
        _cartao.Paint += DesenharCartao;

        int y = _imagem != null ? 150 : 40;
        var titulo = Rotulo("Limpeza automática", 21f, FontStyle.Bold, CorRealce, y, lw); _cartao.Controls.Add(titulo);
        y += 48;
        var msg = Rotulo("A limpeza automática deste computador vai começar.\nSeus arquivos pessoais serão apagados. Salve o que precisar.",
            12f, FontStyle.Regular, CorTexto, y, lw, 60); _cartao.Controls.Add(msg);
        y += 74;
        _contagem = Rotulo("", 15f, FontStyle.Bold, CorTextoFraco, y, lw); _cartao.Controls.Add(_contagem);
        y += 48;

        var limpar = Botao("Limpar agora", CorRealce, Color.White);
        limpar.Click += (_, _) => Escolher(ResultadoAviso.Proceder);
        var adiar = Botao("Adiar 1 hora", Color.FromArgb(0x33, 0x33, 0x37), CorTexto);
        adiar.Click += (_, _) => Escolher(ResultadoAviso.Adiar);
        int bw = 170, bh = 44, gap = 18, totalBtn = bw * 2 + gap;
        limpar.SetBounds((lw - totalBtn) / 2, y, bw, bh);
        adiar.SetBounds((lw - totalBtn) / 2 + bw + gap, y, bw, bh);
        _cartao.Controls.Add(limpar);
        _cartao.Controls.Add(adiar);

        Controls.Add(_cartao);
        AtualizarContagem();
        _relogio.Tick += AoTique;
    }

    Label Rotulo(string texto, float tam, FontStyle estilo, Color cor, int y, int largura, int altura = 36) => new()
    {
        Text = texto, Font = new Font("Segoe UI", tam, estilo), ForeColor = cor, BackColor = CorCartao,
        AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(20, y), Size = new Size(largura - 40, altura),
    };

    static Button Botao(string texto, Color fundo, Color cor)
    {
        var b = new Button
        {
            Text = texto, Font = new Font("Segoe UI Semibold", 11f), FlatStyle = FlatStyle.Flat,
            BackColor = fundo, ForeColor = cor, Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    void DesenharCartao(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, _cartao.Width - 1, _cartao.Height - 1);
        using var caminho = Arredondado(r, 16);
        _cartao.Region = new Region(caminho);
        using var borda = new Pen(CorBorda);
        e.Graphics.DrawPath(borda, caminho);
        if (_imagem != null)
            DesenharImagem(e.Graphics, _imagem, new Rectangle(0, 0, _cartao.Width, _cartao.Height), 36, 90);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DesenharCredito(e.Graphics, AreaPrincipal());
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var a = AreaPrincipal();
        _cartao.Location = new Point(a.X + (a.Width - _cartao.Width) / 2, a.Y + (a.Height - _cartao.Height) / 2);
        _relogio.Start();
    }

    void AoTique(object? sender, EventArgs e)
    {
        _restante--;
        if (_restante <= 0) { Escolher(ResultadoAviso.Proceder); return; }
        AtualizarContagem();
    }

    internal Bitmap Previa(int w, int h) => GerarPrevia(_cartao, w, h);

    void AtualizarContagem() => _contagem.Text = $"A limpeza começa em {_restante / 60}:{_restante % 60:00}";

    void Escolher(ResultadoAviso r)
    {
        Resultado = r;
        _relogio.Stop();
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _relogio.Dispose();
        _imagem?.Dispose();
        base.OnFormClosed(e);
    }
}

/// <summary>Tela de carregamento mostrada durante a limpeza: logo do professor (se houver), "Limpando..." e uma barra.</summary>
sealed class LimpezaForm : TelaCheiaEscura
{
    bool _permitirFechar;
    readonly Image? _imagem;
    readonly Panel _cartao;

    public LimpezaForm(string? caminhoImagem)
    {
        _imagem = CarregarImagem(caminhoImagem);

        int lw = 560, lh = _imagem != null ? 330 : 220;
        _cartao = new Panel { Size = new Size(lw, lh), BackColor = CorCartao };
        _cartao.Paint += DesenharCartao;

        int y = _imagem != null ? 150 : 46;
        _cartao.Controls.Add(new Label
        {
            Text = "Limpando o computador", Font = new Font("Segoe UI", 20f, FontStyle.Bold), ForeColor = CorTexto,
            BackColor = CorCartao, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, y), Size = new Size(lw - 40, 36),
        });
        y += 44;
        _cartao.Controls.Add(new Label
        {
            Text = "Aguarde, isso leva alguns instantes.", Font = new Font("Segoe UI", 11f), ForeColor = CorTextoFraco,
            BackColor = CorCartao, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, y), Size = new Size(lw - 40, 26),
        });
        y += 46;
        _cartao.Controls.Add(new BarraProgresso
        {
            BackColor = CorCartao, Location = new Point(60, y), Size = new Size(lw - 120, 12),
        });

        Controls.Add(_cartao);
    }

    internal Bitmap Previa(int w, int h) => GerarPrevia(_cartao, w, h);

    void DesenharCartao(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, _cartao.Width - 1, _cartao.Height - 1);
        using var caminho = Arredondado(r, 16);
        _cartao.Region = new Region(caminho);
        using var borda = new Pen(CorBorda);
        e.Graphics.DrawPath(borda, caminho);
        if (_imagem != null)
            DesenharImagem(e.Graphics, _imagem, new Rectangle(0, 0, _cartao.Width, _cartao.Height), 36, 90);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DesenharCredito(e.Graphics, AreaPrincipal());
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var a = AreaPrincipal();
        _cartao.Location = new Point(a.X + (a.Width - _cartao.Width) / 2, a.Y + (a.Height - _cartao.Height) / 2);
    }

    // Enquanto limpa, Alt+F4 e afins não fecham: só o próprio agente, ao terminar.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_permitirFechar) { e.Cancel = true; return; }
        base.OnFormClosing(e);
    }

    public void Fechar()
    {
        _permitirFechar = true;
        _imagem?.Dispose();
        if (!IsDisposed) Close();
    }
}

/// <summary>Barra de progresso indeterminada azul (do tema): um segmento desliza de um lado a outro.</summary>
sealed class BarraProgresso : Control
{
    static readonly Color CorTrilho = Color.FromArgb(0x2A, 0x31, 0x42);
    static readonly Color CorBarra = Color.FromArgb(0x2D, 0x6C, 0xDF);
    readonly System.Windows.Forms.Timer _relogio = new() { Interval = 16 };
    float _pos = 0.3f;

    public BarraProgresso()
    {
        DoubleBuffered = true;
        _relogio.Tick += (_, _) => { _pos += 0.012f; if (_pos > 1.2f) _pos = -0.2f; Invalidate(); };
        _relogio.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var trilho = new Rectangle(0, 0, Width - 1, Height - 1);
        int raio = Height / 2;
        using (var caminho = TelaCheiaEscura.Arredondado(trilho, raio))
        {
            using var fundo = new SolidBrush(CorTrilho);
            e.Graphics.FillPath(fundo, caminho);
            e.Graphics.SetClip(caminho);
            int segW = (int)(Width * 0.35);
            var seg = new Rectangle((int)(_pos * Width) - segW / 2, 0, segW, Height);
            using var caminhoSeg = TelaCheiaEscura.Arredondado(seg, raio);
            using var barra = new SolidBrush(CorBarra);
            e.Graphics.FillPath(barra, caminhoSeg);
            e.Graphics.ResetClip();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _relogio.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Aviso do professor: janela pequena, sem roubar o foco do que o aluno está fazendo.</summary>
sealed class MensagemForm : Form
{
    readonly System.Windows.Forms.Timer _relogio = new() { Interval = 15000 };

    public MensagemForm(string texto, string? professor)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(420, 150);
        BackColor = Color.FromArgb(0x1E, 0x1E, 0x1E);
        ForeColor = Color.White;

        Controls.Add(new Label
        {
            Text = string.IsNullOrEmpty(professor) ? "Mensagem do professor" : $"Mensagem de {professor}",
            Font = new Font("Segoe UI Semibold", 11f), ForeColor = Color.FromArgb(0x5A, 0x9B, 0xFF),
            AutoSize = false, Location = new Point(16, 12), Size = new Size(388, 24),
        });
        Controls.Add(new Label
        {
            Text = texto, Font = new Font("Segoe UI", 11f),
            AutoSize = false, Location = new Point(16, 40), Size = new Size(388, 70),
        });
        var ok = new Button
        {
            Text = "OK", Size = new Size(80, 28), Location = new Point(324, 112),
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0x2D, 0x6C, 0xDF), ForeColor = Color.White,
        };
        ok.FlatAppearance.BorderSize = 0;
        ok.Click += (_, _) => Close();
        Controls.Add(ok);

        _relogio.Tick += (_, _) => Close();
        _relogio.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 768);
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _relogio.Dispose();
        base.OnFormClosed(e);
    }
}

/// <summary>Tela cheia escura com um recado; cobre todos os monitores e fica por cima de tudo até o professor desbloquear.</summary>
sealed class BloqueioForm : Form
{
    bool _permitirFechar;
    readonly string _texto;

    public BloqueioForm(string texto)
    {
        _texto = string.IsNullOrWhiteSpace(texto) ? "Atenção ao professor" : texto;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(0x10, 0x14, 0x20);

        // Cobre a área de todos os monitores juntos.
        var total = Rectangle.Empty;
        foreach (var tela in Screen.AllScreens) total = Rectangle.Union(total, tela.Bounds);
        if (total.IsEmpty) total = new Rectangle(0, 0, 1366, 768);
        Bounds = total;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using var fonteGrande = new Font("Segoe UI", 30f, FontStyle.Bold);
        using var fontePequena = new Font("Segoe UI", 12f);
        using var branco = new SolidBrush(Color.White);
        using var cinza = new SolidBrush(Color.FromArgb(0xAA, 0xAA, 0xAA));
        var centro = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        // Centraliza no monitor principal (não no retângulo de todos juntos).
        var principal = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, Width, Height);
        var area = new RectangleF(principal.X - Left, principal.Y - Top, principal.Width, principal.Height);
        e.Graphics.DrawString(_texto, fonteGrande, branco, area, centro);
        var rodape = new RectangleF(area.X, area.Bottom - 70, area.Width, 50);
        e.Graphics.DrawString("Tela bloqueada pelo professor", fontePequena, cinza, rodape, centro);
    }

    protected override bool ShowWithoutActivation => false;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        BringToFront();
    }

    // Enquanto bloqueada, Alt+F4 e afins não fecham a janela: só o comando do professor (que liga _permitirFechar).
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_permitirFechar)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    public void Fechar()
    {
        _permitirFechar = true;
        if (!IsDisposed) Close();
    }
}
