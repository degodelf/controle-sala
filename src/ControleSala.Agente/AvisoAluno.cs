using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ControleSala.Agente;

/// <summary>
/// Transparência com o aluno: ao entrar no Windows, um cartão explica em linguagem simples o que o professor pode ver.
/// Não rouba o foco de quem está trabalhando e some sozinho.
/// </summary>
sealed class AvisoAlunoForm : Form
{
    const int SegundosNaTela = 25;
    const string Texto =
        "Durante a aula, o professor pode ver esta tela e ajudar usando o mouse e o teclado. Nada é gravado.\n\n" +
        "Não entre em contas pessoais e não digite senhas de banco aqui.\n\n" +
        "Quando o ícone perto do relógio fica vermelho, a sua tela está sendo vista. Dúvidas? Fale com o professor.";

    readonly System.Windows.Forms.Timer _relogio = new() { Interval = SegundosNaTela * 1000 };

    public AvisoAlunoForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(400, 250);
        BackColor = Color.FromArgb(0x1E, 0x1E, 0x1E);
        ForeColor = Color.FromArgb(0xDD, 0xDD, 0xDD);
        Font = new Font("Segoe UI", 10f);

        Controls.Add(new Label
        {
            Text = "Este computador é da escola", Font = new Font("Segoe UI Semibold", 14f), AutoSize = true, Location = new Point(18, 14),
        });
        Controls.Add(new Label { Text = Texto, AutoSize = false, Location = new Point(18, 50), Size = new Size(364, 150) });

        var entendi = new Button
        {
            Text = "Entendi", FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0x2D, 0x6C, 0xDF), ForeColor = Color.White,
            UseVisualStyleBackColor = false, Cursor = Cursors.Hand, Location = new Point(282, 206), Size = new Size(100, 32),
        };
        entendi.FlatAppearance.BorderSize = 0;
        entendi.Click += (_, _) => Esconder();
        Controls.Add(entendi);
        _relogio.Tick += (_, _) => Esconder();
    }

    protected override bool ShowWithoutActivation => true;

    public void Mostrar()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 768);
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        Show();
        _relogio.Stop();
        _relogio.Start();
    }

    void Esconder()
    {
        _relogio.Stop();
        Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var borda = new Pen(Color.FromArgb(0x55, 0x55, 0x55));
        e.Graphics.DrawRectangle(borda, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _relogio.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>Desenha o cartão numa imagem, fora da tela (para conferir o visual).</summary>
    public void SalvarImagem(string caminho)
    {
        Location = new Point(-10000, -10000);
        Show();
        Application.DoEvents();
        using var bmp = new Bitmap(Width, Height);
        DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
        bmp.Save(caminho, ImageFormat.Png);
        Hide();
    }
}
