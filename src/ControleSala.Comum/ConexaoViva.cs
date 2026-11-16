using System.Net.Sockets;

namespace ControleSala.Comum;

/// <summary>
/// Liga o "keepalive" do TCP: se o outro PC sumir sem fechar a conexão (cabo puxado, falta de luz), o Windows percebe em
/// cerca de 20 s e a conexão cai, em vez de ficar parada para sempre mostrando a última imagem.
/// </summary>
public static class ConexaoViva
{
    public const int SegundosParado = 10, SegundosEntreTestes = 2, Testes = 5;

    public static void Ligar(Socket socket)
    {
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, SegundosParado);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, SegundosEntreTestes);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, Testes);
        }
        catch (Exception ex) when (ex is SocketException or PlatformNotSupportedException or ObjectDisposedException)
        {
            // Windows antigo sem esses ajustes: fica sem o aviso de queda, como antes.
        }
    }
}
