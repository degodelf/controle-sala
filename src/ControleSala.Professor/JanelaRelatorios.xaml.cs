using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>As últimas limpezas da sala. Cada uma abre no navegador, sem precisar procurar o arquivo na pasta.</summary>
public partial class JanelaRelatorios : Window
{
    sealed record Linha(RelatorioLimpeza Origem)
    {
        public string Data => Origem.Inicio.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        public string Tipo => Origem.Tipo;
        public string Computadores => $"{Origem.Limpos} de {Origem.Linhas.Count} PCs";
        public string Total => RelatorioLimpeza.EmGb(Origem.Bytes);
    }

    readonly RepositorioRelatorios _relatorios;

    internal JanelaRelatorios(RepositorioRelatorios relatorios)
    {
        _relatorios = relatorios;
        InitializeComponent();
        var linhas = relatorios.Listar().Select(r => new Linha(r)).ToList();
        Lista.ItemsSource = linhas;
        if (linhas.Count > 0) Lista.SelectedIndex = 0;
        BotaoAbrir.IsEnabled = linhas.Count > 0;
        TextoResumo.Text = linhas.Count > 0
            ? $"Ficam guardadas as {RepositorioRelatorios.Guardados} limpezas mais recentes. A mais antiga é apagada quando chega uma nova."
            : "Nenhuma limpeza ainda. O relatório aparece aqui quando os computadores terminarem a primeira.";
    }

    /// <summary>Abre o relatório no navegador padrão do PC.</summary>
    internal static void AbrirNoNavegador(string caminho) => Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true });

    void AbrirSelecionado()
    {
        if (Lista.SelectedItem is Linha linha) AbrirNoNavegador(_relatorios.CaminhoHtml(linha.Origem));
    }

    void Abrir_Click(object sender, RoutedEventArgs e) => AbrirSelecionado();
    void Lista_DuploClique(object sender, MouseButtonEventArgs e) => AbrirSelecionado();

    void AbrirPasta_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_relatorios.Pasta);
        AbrirNoNavegador(_relatorios.Pasta);
    }
}
