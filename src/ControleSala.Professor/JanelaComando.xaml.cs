using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>
/// Janela genérica de comando: escolhe computadores e envia uma ação a todos. Serve para qualquer comando —
/// alguns pedem um texto (link, mensagem), outros não (bloquear tela). Reutilizada por todos os comandos.
/// </summary>
public partial class JanelaComando : Window
{
    internal sealed class ItemAlvo(ConexaoComputador conexao, bool marcadoInicial) : INotifyPropertyChanged
    {
        static readonly Brush Verde = Pincel(0x4C, 0xAF, 0x50), Vermelho = Pincel(0xE5, 0x39, 0x35), Ambar = Pincel(0xFF, 0xC1, 0x07), Cinza = Pincel(0x9A, 0x9A, 0x9A);
        static Brush Pincel(byte r, byte g, byte b) { var p = new SolidColorBrush(Color.FromRgb(r, g, b)); p.Freeze(); return p; }

        bool _marcado = marcadoInicial;
        bool _enviando;
        string _status = "";
        Brush _corStatus = Cinza;

        public ConexaoComputador Conexao { get; } = conexao;
        public string Nome => Conexao.Dados.Nome;
        public bool Online => Conexao.Estado == EstadoConexao.Online;
        public bool PodeMarcar => Online && !_enviando;
        public Brush CorEstado => Conexao.Estado switch { EstadoConexao.Online => Verde, EstadoConexao.Conectando => Ambar, _ => Vermelho };
        public bool Marcado { get => _marcado; set { _marcado = value; Mudou(); } }
        public string Status { get => _status; set { _status = value; Mudou(); } }
        public Brush CorStatus { get => _corStatus; set { _corStatus = value; Mudou(); } }

        public void Resultado(bool ok, string msg)
        {
            Status = ok ? "✓ " + msg : "✗ " + msg;
            CorStatus = ok ? Verde : Vermelho;
        }

        public void DefinirEnviando(bool enviando) { _enviando = enviando; Mudou(nameof(PodeMarcar)); }
        public void AtualizarEstado() { Mudou(nameof(PodeMarcar)); Mudou(nameof(CorEstado)); if (!Online && !_enviando) Marcado = false; }

        public event PropertyChangedEventHandler? PropertyChanged;
        void Mudou([CallerMemberName] string? nome = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
    }

    /// <summary>Opção de uma lista suspensa (ex.: um programa): o que o professor vê e o valor enviado ao agente.</summary>
    public sealed record Opcao(string Texto, string Valor);

    readonly List<ItemAlvo> _alvos;
    readonly AcaoComando _acao;
    readonly Func<string, (bool Ok, string ArgumentoOuErro)>? _prepararArgumento;
    readonly bool _usaLista;
    readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromSeconds(1) };
    CancellationTokenSource? _cancelar;

    /// <summary>Se definido, pede confirmação (Sim/Não) com este texto antes de enviar. Para comandos sérios (desligar...).</summary>
    public string? ConfirmacaoAntesDeEnviar { get; init; }

    /// <param name="prepararArgumento">Valida/normaliza o texto digitado (ex.: o link). Null = comando sem texto.</param>
    /// <param name="opcoes">Se dado, mostra uma lista suspensa em vez de caixa de texto (ex.: a lista de programas).</param>
    /// <param name="preMarcar">Quais computadores já vêm marcados (a seleção da grade). Null = todos os online (padrão).</param>
    /// <param name="ocultarLista">Esconde a lista de PCs: o comando já vem mirado nos computadores dados (a seleção da grade).</param>
    internal JanelaComando(IEnumerable<ConexaoComputador> computadores, AcaoComando acao, string titulo,
        string? rotuloArgumento, Func<string, (bool, string)>? prepararArgumento, IReadOnlyList<Opcao>? opcoes = null,
        Func<ConexaoComputador, bool>? preMarcar = null, bool ocultarLista = false)
    {
        InitializeComponent();
        _acao = acao;
        _prepararArgumento = prepararArgumento;
        _usaLista = opcoes != null;
        _alvos = [.. computadores.Select(c => new ItemAlvo(c, c.Estado == EstadoConexao.Online && (preMarcar?.Invoke(c) ?? true)))];
        Title = titulo;
        TextoTitulo.Text = titulo;
        ListaComputadores.ItemsSource = _alvos;

        if (ocultarLista)
        {
            // O comando já vem mirado na seleção da grade: esconde a lista e mostra só um resumo dos alvos.
            PainelLista.Visibility = Visibility.Collapsed;
            ListaComputadores.Visibility = Visibility.Collapsed;
            LinhaLista.Height = new GridLength(0);
            int marcados = _alvos.Count(a => a.Marcado);
            TextoAlvos.Text = $"Enviando para {marcados} computador{(marcados == 1 ? "" : "es")} selecionado{(marcados == 1 ? "" : "s")}.";
            TextoAlvos.Visibility = Visibility.Visible;
            MinHeight = 240;
            Height = 300;
        }

        if (rotuloArgumento == null)
        {
            PainelArgumento.Visibility = Visibility.Collapsed;
        }
        else
        {
            RotuloArgumento.Text = rotuloArgumento;
            if (_usaLista)
            {
                CampoArgumento.Visibility = Visibility.Collapsed;
                ListaOpcoes.Visibility = Visibility.Visible;
                ListaOpcoes.ItemsSource = opcoes;
                ListaOpcoes.SelectedIndex = opcoes!.Count > 0 ? 0 : -1;
            }
        }

        _relogio.Tick += (_, _) => { foreach (var a in _alvos) a.AtualizarEstado(); };
        _relogio.Start();
        if (_alvos.Count == 0) TextoMensagem.Text = "Não há computadores na sala.";
        Loaded += (_, _) => { if (rotuloArgumento != null && !_usaLista) CampoArgumento.Focus(); };
    }

    void Todos_Click(object sender, RoutedEventArgs e) { foreach (var a in _alvos.Where(a => a.PodeMarcar)) a.Marcado = true; }
    void Nenhum_Click(object sender, RoutedEventArgs e) { foreach (var a in _alvos) a.Marcado = false; }

    void Campo_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) Enviar_Click(sender, e);
    }

    async void Enviar_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        string argumento = "";
        if (_prepararArgumento != null)
        {
            if (_usaLista)
            {
                if (ListaOpcoes.SelectedItem is not Opcao op) { TextoMensagem.Text = "Escolha um programa."; return; }
                argumento = op.Valor;
            }
            else
            {
                var (ok, resultado) = _prepararArgumento(CampoArgumento.Text);
                if (!ok) { TextoMensagem.Text = resultado; return; }
                argumento = resultado;
            }
        }

        var escolhidos = _alvos.Where(a => a.Marcado && a.Online).ToList();
        if (escolhidos.Count == 0) { TextoMensagem.Text = "Marque pelo menos um computador que esteja online."; return; }

        if (ConfirmacaoAntesDeEnviar is { } pergunta &&
            MessageBox.Show(this, $"{pergunta}\n\nComputadores selecionados: {escolhidos.Count}.", Title,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        _cancelar = new CancellationTokenSource();
        BotaoEnviar.IsEnabled = false;
        foreach (var a in _alvos) a.DefinirEnviando(true);
        foreach (var a in escolhidos) { a.Status = "Enviando..."; a.CorStatus = (Brush)FindResource("PincelTextoFraco"); }

        try
        {
            await Task.WhenAll(escolhidos.Select(async a =>
            {
                var (ok, msg) = await a.Conexao.EnviarComandoAsync(_acao, argumento, _cancelar.Token);
                a.Resultado(ok, msg);
            }));
            int falhas = escolhidos.Count(a => a.Status.StartsWith('✗'));
            TextoMensagem.Text = falhas == 0 ? "Enviado a todos." : $"Enviado, com {falhas} erro(s) — veja ao lado de cada computador.";
        }
        finally
        {
            _cancelar.Dispose();
            _cancelar = null;
            BotaoEnviar.IsEnabled = true;
            foreach (var a in _alvos) a.DefinirEnviando(false);
        }
    }

    void Window_Closing(object? sender, CancelEventArgs e)
    {
        _cancelar?.Cancel();
        _relogio.Stop();
    }
}
