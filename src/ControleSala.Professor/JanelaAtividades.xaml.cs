using System.IO;
using System.Windows;
using System.Windows.Controls;
using ControleSala.Comum;
using Microsoft.Win32;

namespace ControleSala.Professor;

/// <summary>Mostra quem fez o quê: serve para a direção prestar contas e para responder a uma família que pergunte.</summary>
public partial class JanelaAtividades : Window
{
    sealed record Linha(Atividade Origem)
    {
        public string Data => Origem.Quando.ToString("dd/MM/yyyy HH:mm");
        public string Professor => Origem.Professor;
        public string Acao => Origem.Acao;
        public string Computador => Origem.Computador;
        public string Detalhe => Origem.Detalhe;
    }

    readonly List<Linha> _todas;

    public JanelaAtividades()
    {
        InitializeComponent();
        _todas = Sessao.Atividades.Ler().Select(a => new Linha(a)).ToList();
        TextoResumo.Text = $"Guardado por {RegistroAtividades.MesesGuardados} meses, só neste computador. Não contém imagens de tela nem texto de mensagens.";
        Filtrar();
    }

    IEnumerable<Linha> Filtradas()
    {
        string busca = CampoBusca.Text.Trim();
        return busca.Length == 0 ? _todas : _todas.Where(l =>
            new[] { l.Data, l.Professor, l.Acao, l.Computador, l.Detalhe }.Any(c => c.Contains(busca, StringComparison.CurrentCultureIgnoreCase)));
    }

    void Filtrar() => Lista.ItemsSource = Filtradas().ToList();

    void Busca_Alterada(object sender, TextChangedEventArgs e) => Filtrar();

    void Exportar_Click(object sender, RoutedEventArgs e)
    {
        var salvar = new SaveFileDialog
        {
            Title = "Exportar o registro de atividades",
            Filter = "Planilha (CSV)|*.csv",
            FileName = $"registro-de-atividades-{DateTime.Now:yyyy-MM-dd}.csv",
        };
        if (salvar.ShowDialog(this) != true) return;
        try
        {
            RegistroAtividades.ExportarCsv(Filtradas().Select(l => l.Origem), salvar.FileName);
            Sessao.Registrar("Exportou o registro de atividades");
            TextoMensagem.Text = "Planilha salva. Guarde em lugar seguro: ela mostra o que cada professor fez.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TextoMensagem.Text = $"Não foi possível salvar: {ex.Message}";
        }
    }
}
