using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using ControleSala.Comum;
using Microsoft.Win32;

namespace ControleSala.Professor;

/// <summary>Configura a limpeza automática: o que apagar, exceções, imagem mostrada e a agenda (dias e horário).</summary>
public partial class JanelaLimpeza : Window
{
    /// <summary>Chamado ao salvar, para a janela principal reenviar a configuração aos computadores online.</summary>
    public bool Salvou { get; private set; }

    public JanelaLimpeza()
    {
        InitializeComponent();
        var c = LimpezaConfig.Carregar();
        CxDownloads.IsChecked = c.Downloads;
        CxAreaTrabalho.IsChecked = c.AreaTrabalho;
        CxDocumentos.IsChecked = c.Documentos;
        CxNavegadores.IsChecked = c.Navegadores;
        CxLixeira.IsChecked = c.Lixeira;
        CxTemporarios.IsChecked = c.Temporarios;
        CxPapel.IsChecked = c.RestaurarPapelDeParede;
        CxDefenderAtualizar.IsChecked = c.AtualizarDefender;
        CxDefenderVerificar.IsChecked = c.VerificarDefender;
        RbCompleta.IsChecked = c.DefenderVarreduraCompleta;
        RbRapida.IsChecked = !c.DefenderVarreduraCompleta;
        CampoExcecoes.Text = string.Join("\n", c.Excecoes);
        CxAgendada.IsChecked = c.Agendada;
        CampoHora.Text = c.Hora;
        foreach (var b in Dias())
            b.IsChecked = c.Dias.Contains(int.Parse((string)b.Tag));
        MostrarImagem();
    }

    IEnumerable<ToggleButton> Dias() => PainelDias.Children.OfType<ToggleButton>();

    void MostrarImagem() =>
        TextoImagem.Text = File.Exists(Sessao.CaminhoImagemLimpeza) ? "imagem escolhida" : "nenhuma (mostra só o texto)";

    void EscolherImagem_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog { Title = "Imagem da limpeza", Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialogo.ShowDialog(this) != true) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Sessao.CaminhoImagemLimpeza)!);
            File.Copy(dialogo.FileName, Sessao.CaminhoImagemLimpeza, overwrite: true);
            MostrarImagem();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TextoMensagem.Text = $"Não foi possível usar a imagem: {ex.Message}";
        }
    }

    void RemoverImagem_Click(object sender, RoutedEventArgs e)
    {
        try { if (File.Exists(Sessao.CaminhoImagemLimpeza)) File.Delete(Sessao.CaminhoImagemLimpeza); }
        catch (IOException) { }
        MostrarImagem();
    }

    void Salvar_Click(object sender, RoutedEventArgs e)
    {
        bool agendada = CxAgendada.IsChecked == true;
        var dias = Dias().Where(b => b.IsChecked == true).Select(b => int.Parse((string)b.Tag)).ToList();
        string hora = CampoHora.Text.Trim();
        if (agendada && dias.Count == 0) { TextoMensagem.Text = "Escolha pelo menos um dia para a limpeza agendada."; return; }
        if (agendada && !LimpezaConfig.HoraValida(hora)) { TextoMensagem.Text = "Horário inválido. Use HH:mm (ex.: 18:00)."; return; }

        var c = new LimpezaConfig
        {
            Downloads = CxDownloads.IsChecked == true,
            AreaTrabalho = CxAreaTrabalho.IsChecked == true,
            Documentos = CxDocumentos.IsChecked == true,
            Navegadores = CxNavegadores.IsChecked == true,
            Lixeira = CxLixeira.IsChecked == true,
            Temporarios = CxTemporarios.IsChecked == true,
            RestaurarPapelDeParede = CxPapel.IsChecked == true,
            AtualizarDefender = CxDefenderAtualizar.IsChecked == true,
            VerificarDefender = CxDefenderVerificar.IsChecked == true,
            DefenderVarreduraCompleta = RbCompleta.IsChecked == true,
            Excecoes = CampoExcecoes.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Agendada = agendada,
            Dias = dias,
            Hora = LimpezaConfig.HoraValida(hora) ? hora : "18:00",
        };
        try
        {
            c.Salvar();
            Sessao.AtualizarLimpeza();
            Salvou = true;
            TextoMensagem.Text = "Configuração salva. Enviada aos computadores que estão online.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TextoMensagem.Text = $"Não foi possível salvar: {ex.Message}";
        }
    }
}
