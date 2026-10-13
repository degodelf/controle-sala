using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

public partial class JanelaEditarComputador : Window
{
    readonly RepositorioSala _sala;
    readonly ComputadorSala? _existente;

    public ComputadorSala? Resultado { get; private set; }

    public JanelaEditarComputador(RepositorioSala sala, ComputadorSala? existente = null)
    {
        _sala = sala;
        _existente = existente;
        InitializeComponent();
        Title = existente == null ? "Adicionar computador" : "Editar computador";
        TextoTitulo.Text = Title;
        CampoNome.Text = existente?.Nome ?? "";
        CampoEndereco.Text = existente?.Endereco ?? "";
        CampoPorta.Text = (existente?.Porta ?? Protocolo.PortaPadrao).ToString();
        Loaded += (_, _) => CampoNome.Focus();
    }

    void Salvar_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CampoPorta.Text.Trim(), out int porta))
        {
            TextoErro.Text = "A porta deve ser um número (o padrão é 47810).";
            return;
        }
        try
        {
            Resultado = _existente == null
                ? _sala.Adicionar(CampoNome.Text, CampoEndereco.Text, porta)
                : _sala.Atualizar(_existente, CampoNome.Text, CampoEndereco.Text, porta);
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            TextoErro.Text = ex.Message;
        }
    }
}
