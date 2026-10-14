using System.Drawing;
using System.Windows.Forms;

namespace ControleSala.Agente;

/// <summary>
/// Aviso de transparência: enquanto o professor estiver controlando (mouse/teclado), aparece uma faixa no topo da
/// tela do aluno ("Prof. X está controlando este computador"). Some sozinha pouco depois do último evento.
/// </summary>
static class IndicadorControle
{
    const long OciosoMs = 2500;
    static long _ultimo;
    static volatile string? _professor;
    static volatile bool _ativo;
    static AvisoControleForm? _form;

    /// <summary>Momento (TickCount64) do último evento de controle recebido.</summary>
    public static long Ultimo => Interlocked.Read(ref _ultimo);

    /// <summary>Chamado a cada evento de controle; liga/renova a faixa de aviso.</summary>
    public static void Tocar(string? professor)
    {
        _professor = professor;
        Interlocked.Exchange(ref _ultimo, Environment.TickCount64);
        if (_ativo) return;
        _ativo = true;
        InterfaceAgente.Postar(Mostrar);
    }

    static void Mostrar()
    {
        _form ??= new AvisoControleForm();
        _form.Atualizar(_professor);
        _form.Mostrar();
    }

    internal static void MarcarInativo() => _ativo = false;
    internal static bool AindaAtivo => Environment.TickCount64 - Ultimo < OciosoMs;
}

/// <summary>Faixa fina no topo da tela avisando que o computador está sendo controlado.</summary>
sealed class AvisoControleForm : Form
{
    readonly Label _texto;
    readonly System.Windows.Forms.Timer _relogio = new() { Interval = 500 };

    public AvisoControleForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(0x2D, 0x6C, 0xDF);
        Size = new Size(520, 36);

        _texto = new Label
        {
            Dock = DockStyle.Fill, ForeColor = Color.White, BackColor = Color.FromArgb(0x2D, 0x6C, 0xDF),
            Font = new Font("Segoe UI Semibold", 10.5f), TextAlign = ContentAlignment.MiddleCenter,
        };
        Controls.Add(_texto);
        _relogio.Tick += (_, _) => { if (!IndicadorControle.AindaAtivo) Esconder(); };
    }

    public void Atualizar(string? professor)
    {
        string quem = string.IsNullOrWhiteSpace(professor) ? "O professor" : professor!;
        _texto.Text = $"🖱  {quem} está controlando este computador";
    }

    public void Mostrar()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 768);
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + 8);
        if (!Visible) Show();
        TopMost = true;
        BringToFront();
        _relogio.Start();
    }

    void Esconder()
    {
        _relogio.Stop();
        IndicadorControle.MarcarInativo();
        Hide();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _relogio.Dispose();
        base.OnFormClosed(e);
    }
}
