using System.Drawing;
using System.Windows.Forms;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>Janelas que aparecem na tela do aluno: a mensagem do professor e a tela de bloqueio ("atenção ao professor").</summary>
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

    /// <summary>Cobre a tela com a imagem do professor (se houver) e "Limpando, aguarde..." durante a limpeza.</summary>
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
}

/// <summary>Tela cheia mostrada durante a limpeza: a imagem do professor (se houver) e "Limpando, aguarde...".</summary>
sealed class LimpezaForm : Form
{
    bool _permitirFechar;
    readonly Image? _imagem;

    public LimpezaForm(string? caminhoImagem)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(0x10, 0x14, 0x20);

        var total = Rectangle.Empty;
        foreach (var tela in Screen.AllScreens) total = Rectangle.Union(total, tela.Bounds);
        if (total.IsEmpty) total = new Rectangle(0, 0, 1366, 768);
        Bounds = total;

        if (!string.IsNullOrEmpty(caminhoImagem) && File.Exists(caminhoImagem))
        {
            try { using var fs = File.OpenRead(caminhoImagem); _imagem = Image.FromStream(fs); }
            catch (Exception) { _imagem = null; }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var principal = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, Width, Height);
        var area = new Rectangle(principal.X - Left, principal.Y - Top, principal.Width, principal.Height);

        if (_imagem != null)
        {
            // Encaixa a imagem no centro sem distorcer.
            double escala = Math.Min((double)area.Width / _imagem.Width, (double)area.Height / _imagem.Height) * 0.6;
            int w = (int)(_imagem.Width * escala), h = (int)(_imagem.Height * escala);
            e.Graphics.DrawImage(_imagem, area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2 - 40, w, h);
        }

        using var fonte = new Font("Segoe UI", 22f, FontStyle.Bold);
        using var branco = new SolidBrush(Color.White);
        var centro = new StringFormat { Alignment = StringAlignment.Center };
        e.Graphics.DrawString("Limpando o computador, aguarde...", fonte, branco,
            new RectangleF(area.X, area.Bottom - 120, area.Width, 50), centro);
    }

    protected override bool ShowWithoutActivation => false;
    protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); BringToFront(); }

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
