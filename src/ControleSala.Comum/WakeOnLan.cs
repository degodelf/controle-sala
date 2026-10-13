using System.Net;
using System.Net.Sockets;

namespace ControleSala.Comum;

/// <summary>
/// Liga um computador pela rede (Wake-on-LAN). É enviado pelo professor, por UDP, mesmo com o PC desligado:
/// um "pacote mágico" com o endereço de rede (MAC) da placa do aluno acorda o PC, se ele estiver configurado para isso.
/// </summary>
public static class WakeOnLan
{
    /// <summary>Deixa o MAC num formato padrão "AA:BB:CC:DD:EE:FF", ou null se não for um MAC válido.</summary>
    public static string? Normalizar(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        var bytes = ParaBytes(mac);
        return bytes == null ? null : string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    public static bool EhValido(string? mac) => ParaBytes(mac) != null;

    /// <summary>Monta o pacote mágico: 6 bytes 0xFF + o MAC repetido 16 vezes (102 bytes).</summary>
    public static byte[] MontarPacote(string mac)
    {
        var endereco = ParaBytes(mac) ?? throw new ArgumentException("Endereço MAC inválido.");
        var pacote = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) pacote[i] = 0xFF;
        for (int r = 0; r < 16; r++) endereco.CopyTo(pacote, 6 + r * 6);
        return pacote;
    }

    /// <summary>Envia o pacote mágico por broadcast (e, se dado, também direto para o IP fixo do aluno).</summary>
    public static void Enviar(string mac, IPAddress? ipDireto = null)
    {
        var pacote = MontarPacote(mac);
        using var udp = new UdpClient { EnableBroadcast = true };
        // Portas comuns de Wake-on-LAN: 9 (e 7).
        foreach (int porta in new[] { 9, 7 })
        {
            udp.Send(pacote, pacote.Length, new IPEndPoint(IPAddress.Broadcast, porta));
            if (ipDireto != null)
                udp.Send(pacote, pacote.Length, new IPEndPoint(ipDireto, porta));
        }
    }

    static byte[]? ParaBytes(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        var limpo = mac.Replace(":", "").Replace("-", "").Replace(".", "").Replace(" ", "").Trim();
        if (limpo.Length != 12) return null;
        var bytes = new byte[6];
        for (int i = 0; i < 6; i++)
            if (!byte.TryParse(limpo.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                return null;
        return bytes;
    }
}
