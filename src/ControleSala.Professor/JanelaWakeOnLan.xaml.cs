using System.Net;
using System.Net.Sockets;
using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Liga os computadores pela rede (Wake-on-LAN), usando o MAC que foi descoberto quando eles estavam online.</summary>
public partial class JanelaWakeOnLan : Window
{
    sealed class Item(ComputadorSala computador)
    {
        public ComputadorSala Computador { get; } = computador;
        public string Nome => Computador.Nome;
        public bool TemMac => !string.IsNullOrEmpty(Computador.Mac);
        public bool Marcado { get; set; }
        public string Detalhe => TemMac ? Computador.Mac! : "sem endereço de rede ainda";
    }

    readonly List<Item> _itens;

    internal JanelaWakeOnLan(RepositorioSala sala)
    {
        InitializeComponent();
        _itens = [.. sala.Computadores.Select(c => new Item(c) { Marcado = !string.IsNullOrEmpty(c.Mac) })];
        Lista.ItemsSource = _itens;
        if (_itens.All(i => !i.TemMac))
            TextoMensagem.Text = "Nenhum computador tem o endereço de rede guardado ainda. Deixe os PCs ligarem uma vez com o ControleSala.";
    }

    void Todos_Click(object sender, RoutedEventArgs e) { foreach (var i in _itens.Where(i => i.TemMac)) i.Marcado = true; Lista.Items.Refresh(); }
    void Nenhum_Click(object sender, RoutedEventArgs e) { foreach (var i in _itens) i.Marcado = false; Lista.Items.Refresh(); }

    void Ligar_Click(object sender, RoutedEventArgs e)
    {
        var escolhidos = _itens.Where(i => i.Marcado && i.TemMac).ToList();
        if (escolhidos.Count == 0) { TextoMensagem.Text = "Marque pelo menos um computador com endereço de rede."; return; }

        int enviados = 0;
        foreach (var i in escolhidos)
        {
            try
            {
                // Também manda direto para o IP fixo do aluno, caso a rede bloqueie o broadcast.
                IPAddress.TryParse(i.Computador.Endereco, out var ip);
                WakeOnLan.Enviar(i.Computador.Mac!, ip);
                enviados++;
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                // segue para os próximos
            }
        }
        TextoMensagem.Text = $"Sinal enviado para {enviados} computador(es). Eles podem levar alguns segundos para ligar.";
    }
}
