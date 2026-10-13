using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ControleSala.Comum;
using Microsoft.Win32;

namespace ControleSala.Professor;

/// <summary>Envia o instalador assinado aos computadores dos alunos e acompanha até cada um voltar com a versão nova.</summary>
public partial class JanelaAtualizarAlunos : Window
{
    static readonly TimeSpan LimiteParaVoltar = TimeSpan.FromMinutes(5);

    internal sealed class ItemAlvo(ConexaoComputador conexao) : INotifyPropertyChanged
    {
        static readonly Brush Verde = Pincel(0x4C, 0xAF, 0x50), Vermelho = Pincel(0xE5, 0x39, 0x35), Ambar = Pincel(0xFF, 0xC1, 0x07);
        static Brush Pincel(byte r, byte g, byte b) { var p = new SolidColorBrush(Color.FromRgb(r, g, b)); p.Freeze(); return p; }

        bool _marcado, _enviando;
        double _progresso;
        string _status = "";

        public ConexaoComputador Conexao { get; } = conexao;
        public string Nome => Conexao.Dados.Nome;
        public bool Online => Conexao.Estado == EstadoConexao.Online;
        public bool PodeMarcar => Online && !_enviando;
        public Brush CorEstado => Conexao.Estado switch { EstadoConexao.Online => Verde, EstadoConexao.Conectando => Ambar, _ => Vermelho };
        public string Versao => !Online ? "—" : Conexao.VersaoAgente ?? "antiga";
        public bool Marcado { get => _marcado; set { _marcado = value; Mudou(); } }
        public double Progresso { get => _progresso; set { _progresso = value; Mudou(); } }
        public string Status { get => _status; set { _status = value; Mudou(); } }
        /// <summary>O agente aceitou e está instalando: espera ele voltar com a versão nova.</summary>
        public DateTime? InstalandoDesde { get; set; }

        public void DefinirEnviando(bool enviando) { _enviando = enviando; Mudou(nameof(PodeMarcar)); }
        public void AtualizarEstado()
        {
            Mudou(nameof(PodeMarcar));
            Mudou(nameof(CorEstado));
            Mudou(nameof(Versao));
            if (!Online && !_enviando) Marcado = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        void Mudou([CallerMemberName] string? nome = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
    }

    readonly List<ItemAlvo> _alvos;
    readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromSeconds(1) };
    InstaladorParaAlunos? _instalador;
    CancellationTokenSource? _cancelar;

    internal JanelaAtualizarAlunos(IEnumerable<ConexaoComputador> computadores)
    {
        InitializeComponent();
        _alvos = [.. computadores.Select(c => new ItemAlvo(c))];
        ListaComputadores.ItemsSource = _alvos;
        TextoVersaoProfessor.Text = $"Este programa (professor): versão {Versoes.Atual}.";
        _relogio.Tick += (_, _) => Acompanhar();
        _relogio.Start();
        if (_alvos.Count == 0) TextoMensagem.Text = "Não há computadores na sala. Adicione em \"Computadores...\".";
        Loaded += async (_, _) =>
        {
            if (File.Exists(Atualizacoes.CaminhoPadrao))
                await CarregarInstalador(Atualizacoes.CaminhoPadrao);
            else
                TextoInstalador.Text = "Nenhum instalador guardado neste computador. Clique em \"Escolher instalador...\" e escolha o ControleSala-Setup mais novo.";
        };
    }

    bool Enviando => _cancelar != null;

    async Task CarregarInstalador(string caminho)
    {
        _instalador = null;
        BotaoAtualizar.IsEnabled = BotaoEscolher.IsEnabled = false;
        TextoInstalador.Text = "Conferindo a assinatura do instalador...";
        try
        {
            _instalador = await Atualizacoes.PrepararAsync(caminho, CancellationToken.None);
            TextoInstalador.Text = $"Instalador: versão {_instalador.Versao} ({Tamanhos.Formatar(_instalador.Tamanho)}) — assinatura do autor conferida ✓";
            MarcarDesatualizados();
        }
        catch (ArgumentException ex)
        {
            TextoInstalador.Text = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TextoInstalador.Text = $"Não foi possível ler o instalador: {ex.Message}";
        }
        finally
        {
            BotaoAtualizar.IsEnabled = BotaoEscolher.IsEnabled = true;
        }
    }

    async void Escolher_Click(object sender, RoutedEventArgs e)
    {
        var abrir = new OpenFileDialog { Title = "Escolha o instalador do ControleSala", Filter = "Instalador do ControleSala|ControleSala-Setup*.exe|Programas|*.exe" };
        if (abrir.ShowDialog(this) == true) await CarregarInstalador(abrir.FileName);
    }

    void MarcarDesatualizados()
    {
        if (_instalador == null) return;
        foreach (var a in _alvos.Where(a => a.PodeMarcar)) a.Marcado = Atualizacoes.Desatualizado(a.Conexao, _instalador.Versao);
        int n = _alvos.Count(a => a.Marcado);
        int antigos = _alvos.Count(a => a.Online && a.Conexao.VersaoAgente == null);
        TextoMensagem.Text = (n == 0 ? "Nenhum computador online precisa desta atualização." : $"{n} computador{(n == 1 ? "" : "es")} para atualizar.")
            + (antigos > 0 ? $" {antigos} com versão antiga demais (anterior à 0.3.0): atualize esses com o instalador, no próprio computador." : "");
    }

    void Desatualizados_Click(object sender, RoutedEventArgs e) => MarcarDesatualizados();
    void Nenhum_Click(object sender, RoutedEventArgs e) { foreach (var a in _alvos) a.Marcado = false; }

    void Bloquear(bool enviando)
    {
        BotaoEscolher.IsEnabled = BotaoDesatualizados.IsEnabled = BotaoNenhum.IsEnabled = BotaoAtualizar.IsEnabled = !enviando;
        BotaoCancelarEnvio.IsEnabled = enviando;
        foreach (var a in _alvos) a.DefinirEnviando(enviando);
    }

    async void Atualizar_Click(object sender, RoutedEventArgs e)
    {
        if (_instalador is not { } inst) { TextoMensagem.Text = "Escolha um instalador com a assinatura do autor."; return; }
        var escolhidos = _alvos.Where(a => a.Marcado && a.Online).ToList();
        if (escolhidos.Count == 0) { TextoMensagem.Text = "Marque pelo menos um computador que esteja online."; return; }
        if (MessageBox.Show(this, $"Atualizar {escolhidos.Count} computador{(escolhidos.Count == 1 ? "" : "es")} para a versão {inst.Versao}?\n\n" +
                "Cada um sai da grade por cerca de 1 minuto. Se algum aluno estiver no meio de uma atividade, ela não é afetada (só o ControleSala é trocado).",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _cancelar = new CancellationTokenSource();
        Bloquear(true);
        foreach (var a in _alvos) { a.Progresso = 0; a.InstalandoDesde = null; a.Status = escolhidos.Contains(a) ? "Na fila..." : ""; }
        TextoMensagem.Text = "Enviando o instalador...";
        try
        {
            var progresso = new Progress<ProgressoAtualizacao>(p =>
            {
                var alvo = _alvos.FirstOrDefault(a => a.Conexao == p.Alvo);
                if (alvo == null) return;
                alvo.Progresso = p.Percentual;
                alvo.Status = p.Mensagem;
                if (p.Ok == true) alvo.InstalandoDesde = DateTime.UtcNow;
            });
            int aceitos = await Atualizacoes.EnviarAsync(inst, [.. escolhidos.Select(a => a.Conexao)], progresso, _cancelar.Token);
            int falhas = escolhidos.Count - aceitos;
            TextoMensagem.Text = _cancelar.IsCancellationRequested ? "Envio cancelado."
                : falhas == 0 ? "Enviado. Acompanhe ao lado de cada computador até aparecer \"Atualizado\"."
                : $"{aceitos} aceito{(aceitos == 1 ? "" : "s")} e {falhas} com erro (veja ao lado de cada computador).";
        }
        finally
        {
            _cancelar.Dispose();
            _cancelar = null;
            Bloquear(false);
        }
    }

    /// <summary>A cada segundo: estado dos computadores e quem já voltou com a versão nova.</summary>
    void Acompanhar()
    {
        foreach (var a in _alvos)
        {
            a.AtualizarEstado();
            if (a.InstalandoDesde is not { } desde || _instalador == null) continue;
            if (a.Online && a.Conexao.VersaoAgente == _instalador.Versao)
            {
                a.InstalandoDesde = null;
                a.Progresso = 100;
                a.Status = $"Atualizado para a versão {_instalador.Versao} ✓";
            }
            else if (DateTime.UtcNow - desde > LimiteParaVoltar)
            {
                a.InstalandoDesde = null;
                a.Progresso = 0;
                a.Status = "Não voltou com a versão nova em 5 minutos: confira este computador.";
            }
            else if (!a.Online)
            {
                a.Status = "Instalando... (o computador volta sozinho)";
            }
        }
    }

    void CancelarEnvio_Click(object sender, RoutedEventArgs e)
    {
        _cancelar?.Cancel();
        BotaoCancelarEnvio.IsEnabled = false;
        TextoMensagem.Text = "Cancelando...";
    }

    void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (Enviando)
        {
            if (MessageBox.Show(this, "O instalador ainda está sendo enviado. Cancelar o envio e fechar?\n\n(Os computadores que já receberam continuam se atualizando.)",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _cancelar?.Cancel();
        }
        _relogio.Stop();
    }
}
