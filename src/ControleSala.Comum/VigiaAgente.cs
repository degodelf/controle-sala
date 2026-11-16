namespace ControleSala.Comum;

/// <summary>Regras do serviço que mantém o agente de pé no PC do aluno. Ficam aqui para dar para testar.</summary>
public static class VigiaAgente
{
    /// <summary>Quem cai antes disso conta como "não conseguiu subir".</summary>
    public static readonly TimeSpan TempoMinimoVivo = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A porta do agente aceita conexão agora? Conecta de verdade em 127.0.0.1 (e solta na hora). É a confirmação antes de
    /// reiniciar um agente: a lista de portas do Windows às vezes não mostra o agente mesmo ele funcionando (visto em 8 PCs
    /// com o Windows 11 build 26300, que reiniciavam o agente a cada 1 a 6 minutos sem motivo).
    /// </summary>
    public static bool AceitaConexao(int porta, TimeSpan limite)
    {
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            var tentativa = tcp.ConnectAsync(System.Net.IPAddress.Loopback, porta);
            return tentativa.Wait(limite) && tcp.Connected;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or AggregateException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Quanto esperar para religar depois de <paramref name="quedas"/> quedas seguidas: nada, 2, 4, 8, 16 e no máximo 30 s.</summary>
    public static TimeSpan EsperaAposQuedas(int quedas) =>
        quedas <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(quedas, 5)));
}
