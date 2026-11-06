using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Configura a limpeza automática: o que apagar, exceções e a agenda (dias e horário), que pode ser pausada.</summary>
public partial class JanelaLimpeza : Window
{
    /// <summary>A configuração mudou em disco: a janela principal reenvia aos computadores online na hora.</summary>
    public event Action? Salvou;

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
        CampoHora.Text = c.Hora;
        foreach (var b in Dias())
            b.IsChecked = c.Dias.Contains(int.Parse((string)b.Tag));
        MostrarEstado();
    }

    IEnumerable<ToggleButton> Dias() => PainelDias.Children.OfType<ToggleButton>();

    /// <summary>Mostra a situação da agenda salva (não a que está sendo editada) e o que o botão faz agora.</summary>
    void MostrarEstado()
    {
        var c = LimpezaConfig.Carregar();
        bool temAgenda = c.Dias.Count > 0;
        bool ligada = c.Agendada && temAgenda;
        TextoEstado.Text = ligada ? "Ligada" : temAgenda ? "Pausada" : "Sem agenda";
        TextoDetalhe.Text = ligada ? c.ResumoAgenda()
            : temAgenda ? "Nenhum computador será limpo sozinho. A agenda fica guardada."
            : "Marque os dias e o horário abaixo e clique em Salvar.";
        PontoEstado.Fill = (Brush)FindResource(ligada ? "PincelOnline" : temAgenda ? "PincelAviso" : "PincelTextoFraco");
        BotaoPausar.Content = ligada ? "Pausar" : "Retomar";
        BotaoPausar.Visibility = temAgenda ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Pausa ou retoma na hora, sem salvar o que estiver sendo editado nos campos abaixo.</summary>
    void Pausar_Click(object sender, RoutedEventArgs e)
    {
        var c = LimpezaConfig.Carregar();
        c.Agendada = !c.Agendada;
        if (Guardar(c))
            TextoMensagem.Text = c.Agendada
                ? "Limpeza retomada nos computadores online. Os outros recebem ao conectar."
                : "Limpeza pausada nos computadores online. Os outros recebem ao conectar.";
    }

    void Salvar_Click(object sender, RoutedEventArgs e)
    {
        var salva = LimpezaConfig.Carregar();
        var dias = Dias().Where(b => b.IsChecked == true).Select(b => int.Parse((string)b.Tag)).ToList();
        string hora = CampoHora.Text.Trim();
        if (salva.Agendada && dias.Count == 0) { TextoMensagem.Text = "Escolha pelo menos um dia. Para parar a limpeza, clique em Pausar."; return; }
        if (dias.Count > 0 && !LimpezaConfig.HoraValida(hora)) { TextoMensagem.Text = "Horário inválido. Use HH:mm (ex.: 18:00)."; return; }

        // A primeira agenda já liga. Quem pausou continua pausado até clicar em Retomar.
        bool agendada = dias.Count > 0 && (salva.Agendada || salva.Dias.Count == 0);
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
        if (Guardar(c))
            TextoMensagem.Text = dias.Count > 0 && !agendada
                ? "Configuração salva. A limpeza continua pausada."
                : "Configuração salva e enviada aos computadores online. Os outros recebem ao conectar.";
    }

    bool Guardar(LimpezaConfig c)
    {
        try
        {
            c.Salvar();
            Sessao.AtualizarLimpeza();
            Salvou?.Invoke();
            MostrarEstado();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TextoMensagem.Text = $"Não foi possível salvar: {ex.Message}";
            return false;
        }
    }
}
