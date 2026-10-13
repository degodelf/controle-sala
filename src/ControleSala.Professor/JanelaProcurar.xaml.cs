using System.ComponentModel;
using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

public partial class JanelaProcurar : Window
{
    static readonly TimeSpan Duracao = TimeSpan.FromSeconds(6);

    sealed class Achado(AgenteEncontrado dados) : INotifyPropertyChanged
    {
        bool _marcado = true;
        public AgenteEncontrado Dados { get; } = dados;
        public string Texto => $"{Dados.Nome}   ({Dados.Endereco}:{Dados.Porta})";
        public bool Marcado
        {
            get => _marcado;
            set { _marcado = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Marcado))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    readonly RepositorioSala _sala;
    CancellationTokenSource? _busca;

    public int Adicionados { get; private set; }

    public JanelaProcurar(RepositorioSala sala)
    {
        _sala = sala;
        InitializeComponent();
    }

    void Window_Loaded(object sender, RoutedEventArgs e) => _ = ProcurarAsync();
    void Procurar_Click(object sender, RoutedEventArgs e) => _ = ProcurarAsync();

    void Window_Closing(object? sender, CancelEventArgs e) => _busca?.Cancel();

    async Task ProcurarAsync()
    {
        BotaoProcurar.IsEnabled = BotaoAdicionar.IsEnabled = false;
        Lista.ItemsSource = null;
        TextoEstado.Text = $"Procurando computadores com o agente aberto... ({Duracao.TotalSeconds:0} segundos)";
        _busca?.Cancel();
        var busca = _busca = new CancellationTokenSource();
        try
        {
            var encontrados = await Descoberta.ProcurarAsync(Duracao, ct: busca.Token);
            if (busca.IsCancellationRequested) return;

            var novos = encontrados
                .Where(a => !_sala.Computadores.Any(c => c.Porta == a.Porta
                    && string.Equals(c.Endereco, a.Endereco, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(a => a.Nome, StringComparer.CurrentCultureIgnoreCase)
                .Select(a => new Achado(a))
                .ToList();
            Lista.ItemsSource = novos;
            BotaoAdicionar.IsEnabled = novos.Count > 0;
            TextoEstado.Text = novos.Count > 0
                ? $"{novos.Count} computador{(novos.Count == 1 ? "" : "es")} novo{(novos.Count == 1 ? "" : "s")} encontrado{(novos.Count == 1 ? "" : "s")}. Desmarque os que não quer."
                : encontrados.Count > 0
                    ? "Todos os computadores encontrados já estão na sala."
                    : "Nenhum computador encontrado. Confira se o agente está aberto nos PCs dos alunos e na mesma rede.";
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException)
        {
            TextoEstado.Text = ex is System.Net.Sockets.SocketException
                ? $"Não foi possível escutar a rede: {ex.Message}"
                : "";
        }
        finally
        {
            if (!busca.IsCancellationRequested) BotaoProcurar.IsEnabled = true;
        }
    }

    void Adicionar_Click(object sender, RoutedEventArgs e)
    {
        var marcados = (Lista.ItemsSource as IEnumerable<Achado>)?.Where(a => a.Marcado).ToList() ?? [];
        var problemas = new List<string>();
        foreach (var a in marcados)
        {
            try
            {
                _sala.Adicionar(a.Dados.Nome, a.Dados.Endereco, a.Dados.Porta);
                Adicionados++;
            }
            catch (ArgumentException ex)
            {
                problemas.Add($"{a.Dados.Nome}: {ex.Message}");
            }
        }
        if (problemas.Count > 0)
            MessageBox.Show(this, string.Join("\n", problemas), "Alguns não foram adicionados", MessageBoxButton.OK, MessageBoxImage.Warning);
        Close();
    }
}
