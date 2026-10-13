using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>
/// Ícone perto do relógio do PC do aluno. Sempre visível; ao clicar mostra só um cartão informativo
/// (sala, quem está vendo a tela, crédito). Não tem menu nem botões: o aluno não configura nem encerra nada.
/// </summary>
sealed class Bandeja : IDisposable
{
    readonly NotifyIcon _icone;
    readonly CartaoForm _cartao;
    readonly Icon _iconeLivre, _iconeVisto;
    readonly Control _ponte = new();
    readonly string _nomeSala;

    public Bandeja(ConfigSala? sala, ServidorVideo servidor)
    {
        _nomeSala = sala == null ? "ControleSala" : $"ControleSala — {sala.Sala}";
        _iconeLivre = CriarIcone(Color.FromArgb(0x4C, 0xAF, 0x50));
        _iconeVisto = CriarIcone(Color.FromArgb(0xE5, 0x39, 0x35));
        _cartao = new CartaoForm(sala);
        _ = _ponte.Handle; // cria o handle na thread da interface, para poder voltar a ela de outras threads

        _icone = new NotifyIcon { Icon = _iconeLivre, Text = Limitar(_nomeSala), Visible = true };
        _icone.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _cartao.Alternar();
        };
        servidor.ProfessoresMudaram += profs => _ponte.BeginInvoke(() => Atualizar(profs));
        servidor.ArquivoRecebido += (professor, arquivo) => _ponte.BeginInvoke(() =>
            _icone.ShowBalloonTip(6000, "ControleSala",
                $"{professor} enviou: {arquivo}\nEstá na pasta \"{PastaRecebidos.Nome}\", na área de trabalho.", ToolTipIcon.Info));
        Atualizar(servidor.Professores);
    }

    void Atualizar(string[] professores)
    {
        bool visto = professores.Length > 0;
        _icone.Icon = visto ? _iconeVisto : _iconeLivre;
        _icone.Text = Limitar(visto ? $"{_nomeSala} — tela sendo vista por {string.Join(", ", professores)}" : _nomeSala);
        _cartao.Atualizar(professores);
    }

    static string Limitar(string texto) => texto.Length <= 63 ? texto : texto[..60] + "..."; // limite do Windows

    static Icon CriarIcone(Color ponto)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var corpo = new SolidBrush(Color.FromArgb(0x2D, 0x2D, 0x30));
            using var borda = new Pen(Color.FromArgb(0xE0, 0xE0, 0xE0), 2.5f);
            g.FillRectangle(corpo, 3, 5, 26, 17);
            g.DrawRectangle(borda, 3, 5, 26, 17);
            g.DrawLine(borda, 16, 22, 16, 27);
            g.DrawLine(borda, 9, 27, 23, 27);
            using var luz = new SolidBrush(ponto);
            g.FillEllipse(luz, 10, 9, 12, 9);
        }
        var h = bmp.GetHicon();
        return Icon.FromHandle(h);
    }

    /// <summary>Desenha o cartão com dados de exemplo numa imagem (para conferir o visual sem depender da bandeja).</summary>
    public static int CapturarCartao(string saida)
    {
        using var cartao = new CartaoForm(new ConfigSala("Escola Estadual Exemplo", "Laboratório de Informática 1", new byte[ConfigSala.TamanhoChave]));
        cartao.Atualizar(["Prof. Diego"]);
        cartao.SalvarImagem(saida);
        return 0;
    }

    public void Dispose()
    {
        _icone.Visible = false;
        _icone.Dispose();
        _cartao.Dispose();
        _ponte.Dispose();
    }
}

sealed class CartaoForm : Form
{
    readonly Label _estado = new();

    public CartaoForm(ConfigSala? sala)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(360, 176);
        BackColor = Color.FromArgb(0x1E, 0x1E, 0x1E);
        ForeColor = Color.FromArgb(0xDD, 0xDD, 0xDD);
        Font = new Font("Segoe UI", 9.5f);

        Controls.Add(new Label
        {
            Text = "ControleSala", Font = new Font("Segoe UI Semibold", 14f), AutoSize = true, Location = new Point(18, 14),
        });
        Controls.Add(new Label
        {
            Text = sala == null ? "Sala não configurada" : $"{sala.Sala}\n{sala.Escola}",
            ForeColor = Color.FromArgb(0xAA, 0xAA, 0xAA), AutoSize = false, Location = new Point(18, 44), Size = new Size(324, 38),
        });
        _estado.AutoSize = false;
        _estado.Location = new Point(18, 90);
        _estado.Size = new Size(324, 42);
        Controls.Add(_estado);
        Controls.Add(new Label
        {
            Text = Creditos.Linha, ForeColor = Color.FromArgb(0x6E, 0x6E, 0x6E), Font = new Font("Segoe UI", 7.5f),
            AutoSize = false, Location = new Point(18, 148), Size = new Size(324, 18),
        });
        Atualizar([]);

        Deactivate += (_, _) => Hide();
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
    }

    // Não rouba o foco de quem está trabalhando quando aparece.
    protected override bool ShowWithoutActivation => false;

    public void Atualizar(string[] professores)
    {
        if (professores.Length == 0)
        {
            _estado.ForeColor = Color.FromArgb(0xAA, 0xAA, 0xAA);
            _estado.Text = "Ninguém está vendo a sua tela agora.";
        }
        else
        {
            _estado.ForeColor = Color.FromArgb(0xFF, 0x8A, 0x80);
            _estado.Text = $"● A sua tela está sendo vista por:\n{string.Join(", ", professores)}";
        }
    }

    public void Alternar()
    {
        if (Visible) { Hide(); return; }
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 768);
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        Show();
        Activate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var borda = new Pen(Color.FromArgb(0x55, 0x55, 0x55));
        e.Graphics.DrawRectangle(borda, 0, 0, Width - 1, Height - 1);
    }

    public void SalvarImagem(string caminho)
    {
        // Exibe fora da tela (ninguém vê) para os controles serem criados e desenhados.
        Location = new Point(-10000, -10000);
        Show();
        Application.DoEvents();
        using var bmp = new Bitmap(Width, Height);
        DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
        bmp.Save(caminho, ImageFormat.Png);
        Hide();
    }
}
