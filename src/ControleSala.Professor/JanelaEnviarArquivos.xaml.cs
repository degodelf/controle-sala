using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ControleSala.Comum;
using Microsoft.Win32;

namespace ControleSala.Professor;

/// <summary>Escolhe arquivos e computadores e envia, mostrando o andamento de cada computador.</summary>
public partial class JanelaEnviarArquivos : Window
{
    sealed class ItemArquivo(string caminho, string nome, long tamanho)
    {
        public string Caminho { get; } = caminho;
        public string Nome { get; } = nome;
        public string Texto => $"{Nome}   ({Tamanhos.Formatar(tamanho)})";
    }

    internal sealed class ItemAlvo(ConexaoComputador conexao) : INotifyPropertyChanged
    {
        static readonly Brush Verde = Pincel(0x4C, 0xAF, 0x50), Vermelho = Pincel(0xE5, 0x39, 0x35), Ambar = Pincel(0xFF, 0xC1, 0x07);
        static Brush Pincel(byte r, byte g, byte b) { var p = new SolidColorBrush(Color.FromRgb(r, g, b)); p.Freeze(); return p; }

        bool _marcado = conexao.Estado == EstadoConexao.Online;
        bool _enviando;
        double _progresso;
        string _status = "";

        public ConexaoComputador Conexao { get; } = conexao;
        public string Nome => Conexao.Dados.Nome;
        public bool Online => Conexao.Estado == EstadoConexao.Online;
        public bool PodeMarcar => Online && !_enviando;
        public Brush CorEstado => Conexao.Estado switch { EstadoConexao.Online => Verde, EstadoConexao.Conectando => Ambar, _ => Vermelho };
        public bool Marcado { get => _marcado; set { _marcado = value; Mudou(); } }
        public double Progresso { get => _progresso; set { _progresso = value; Mudou(); } }
        public string Status { get => _status; set { _status = value; Mudou(); } }

        public void DefinirEnviando(bool enviando) { _enviando = enviando; Mudou(nameof(PodeMarcar)); }
        public void AtualizarEstado()
        {
            Mudou(nameof(PodeMarcar));
            Mudou(nameof(CorEstado));
            if (!Online && !_enviando) Marcado = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        void Mudou([CallerMemberName] string? nome = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
    }

    readonly ObservableCollection<ItemArquivo> _arquivos = [];
    readonly List<ItemAlvo> _alvos;
    readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromSeconds(1) };
    CancellationTokenSource? _cancelar;

    internal JanelaEnviarArquivos(IEnumerable<ConexaoComputador> computadores)
    {
        InitializeComponent();
        _alvos = [.. computadores.Select(c => new ItemAlvo(c))];
        ListaArquivos.ItemsSource = _arquivos;
        ListaComputadores.ItemsSource = _alvos;
        _relogio.Tick += (_, _) => { foreach (var a in _alvos) a.AtualizarEstado(); };
        _relogio.Start();
        if (_alvos.Count == 0) TextoMensagem.Text = "Não há computadores na sala. Adicione em \"Computadores...\".";
    }

    bool Enviando => _cancelar != null;

    void Adicionar_Click(object sender, RoutedEventArgs e)
    {
        var abrir = new OpenFileDialog { Title = "Escolha os arquivos para enviar", Multiselect = true, Filter = "Todos os arquivos|*.*" };
        if (abrir.ShowDialog(this) != true) return;

        var recusados = new List<string>();
        foreach (string caminho in abrir.FileNames)
        {
            var info = new FileInfo(caminho);
            if (NomesDeArquivo.Validar(info.Name) is { } erro) { recusados.Add($"{info.Name}: {erro}"); continue; }
            if (info.Length > RecebedorArquivos.TamanhoMaximo) { recusados.Add($"{info.Name}: grande demais."); continue; }
            if (_arquivos.Any(a => string.Equals(a.Caminho, info.FullName, StringComparison.OrdinalIgnoreCase))) continue;
            if (_arquivos.Any(a => string.Equals(a.Nome, info.Name, StringComparison.OrdinalIgnoreCase)))
            {
                recusados.Add($"{info.Name}: já existe um arquivo com esse nome na lista.");
                continue;
            }
            _arquivos.Add(new ItemArquivo(info.FullName, info.Name, info.Length));
        }
        TextoMensagem.Text = "";
        if (recusados.Count > 0)
            MessageBox.Show(this, "Estes arquivos não foram adicionados:\n\n" + string.Join("\n", recusados), "Alguns arquivos não podem ser enviados",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    void Remover_Click(object sender, RoutedEventArgs e)
    {
        foreach (var a in ListaArquivos.SelectedItems.Cast<ItemArquivo>().ToList()) _arquivos.Remove(a);
    }

    void Todos_Click(object sender, RoutedEventArgs e) { foreach (var a in _alvos.Where(a => a.PodeMarcar)) a.Marcado = true; }
    void Nenhum_Click(object sender, RoutedEventArgs e) { foreach (var a in _alvos) a.Marcado = false; }

    void Bloquear(bool enviando)
    {
        BotaoAdicionar.IsEnabled = BotaoRemover.IsEnabled = BotaoTodos.IsEnabled = BotaoNenhum.IsEnabled = BotaoEnviar.IsEnabled = !enviando;
        BotaoCancelarEnvio.IsEnabled = enviando;
        foreach (var a in _alvos) a.DefinirEnviando(enviando);
    }

    async void Enviar_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        var escolhidos = _alvos.Where(a => a.Marcado && a.Online).ToList();
        if (_arquivos.Count == 0) { TextoMensagem.Text = "Adicione pelo menos um arquivo."; return; }
        if (escolhidos.Count == 0) { TextoMensagem.Text = "Marque pelo menos um computador que esteja online."; return; }

        _cancelar = new CancellationTokenSource();
        var ct = _cancelar.Token;
        Bloquear(true);
        foreach (var a in _alvos) { a.Progresso = 0; a.Status = a.Marcado ? "Preparando..." : ""; }
        try
        {
            TextoMensagem.Text = "Preparando os arquivos...";
            var prontos = await EnvioDeArquivos.PrepararAsync(_arquivos.Select(a => a.Caminho), ct);

            foreach (var a in escolhidos) a.Status = "Enviando...";
            TextoMensagem.Text = "Enviando...";
            var progresso = new Progress<ProgressoEnvio>(p => Atualizar(p));
            var resultados = await EnvioDeArquivos.EnviarAsync(prontos, [.. escolhidos.Select(a => a.Conexao)], progresso, ct);

            int ok = resultados.Count(r => r.Ok), falhas = resultados.Count(r => !r.Ok);
            if (ct.IsCancellationRequested)
                TextoMensagem.Text = "Envio cancelado.";
            else
                TextoMensagem.Text = falhas == 0
                    ? $"Pronto: {ok} envio{(ok == 1 ? "" : "s")} concluído{(ok == 1 ? "" : "s")}."
                    : $"{ok} envio{(ok == 1 ? "" : "s")} concluído{(ok == 1 ? "" : "s")} e {falhas} com erro (veja ao lado de cada computador).";
        }
        catch (ArgumentException ex)
        {
            TextoMensagem.Text = ex.Message;
            foreach (var a in _alvos) a.Status = "";
        }
        catch (OperationCanceledException)
        {
            TextoMensagem.Text = "Envio cancelado.";
        }
        finally
        {
            _cancelar.Dispose();
            _cancelar = null;
            Bloquear(false);
        }
    }

    void Atualizar(ProgressoEnvio p)
    {
        var alvo = _alvos.FirstOrDefault(a => a.Conexao == p.Alvo);
        if (alvo == null) return;
        if (p.Final != null)
        {
            alvo.Status = p.Final;
            if (p.Sucesso) alvo.Progresso = 100;
            return;
        }
        double pct = p.TamanhoDoArquivo == 0 ? 100 : 100.0 * p.Bytes / p.TamanhoDoArquivo;
        alvo.Progresso = pct;
        alvo.Status = p.TotalDeArquivos == 1 ? $"{pct:0}%" : $"Arquivo {p.ArquivoAtual} de {p.TotalDeArquivos} — {pct:0}%";
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
            if (MessageBox.Show(this, "Há um envio em andamento. Cancelar o envio e fechar?", "Enviar arquivos",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _cancelar?.Cancel();
        }
        _relogio.Stop();
    }
}

static class Tamanhos
{
    public static string Formatar(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} bytes",
    };
}
