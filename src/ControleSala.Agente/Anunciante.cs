using System.Net;
using System.Net.Sockets;
using ControleSala.Comum;

namespace ControleSala.Agente;

/// <summary>Avisa a cada poucos segundos, por UDP, "estou aqui" (nome do PC e porta), para o professor encontrar este PC.</summary>
sealed class Anunciante : IDisposable
{
    readonly UdpClient _udp = new() { EnableBroadcast = true };
    readonly List<IPEndPoint> _destinos = [];
    readonly byte[] _anuncio;
    readonly System.Threading.Timer _relogio;

    /// <param name="broadcast">Anuncia para toda a rede local (use com --rede).</param>
    /// <param name="extras">Endereços fixos que também recebem o anúncio (útil em testes e em redes que bloqueiam broadcast).</param>
    public Anunciante(string nome, int portaVideo, bool broadcast, IEnumerable<IPAddress> extras)
    {
        _anuncio = Descoberta.MontarAnuncio(nome, portaVideo);
        if (broadcast) _destinos.Add(new IPEndPoint(IPAddress.Broadcast, Descoberta.Porta));
        _destinos.AddRange(extras.Select(ip => new IPEndPoint(ip, Descoberta.Porta)));
        _relogio = new System.Threading.Timer(_ => Enviar(), null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    void Enviar()
    {
        foreach (var destino in _destinos)
        {
            try
            {
                _udp.Send(_anuncio, _anuncio.Length, destino);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                // Sem rede agora: tenta de novo no próximo aviso.
            }
        }
    }

    public void Dispose()
    {
        _relogio.Dispose();
        _udp.Dispose();
    }
}
