namespace ControleSala.Comum;

/// <summary>
/// Regras do professor para os comandos (separadas da janela para dar para testar): o que fica valendo e é reaplicado
/// quando um PC reconecta, o que pode ser mandado de novo sem efeito dobrado, e o resumo do envio na barra de status.
/// </summary>
public static class RegrasDeComando
{
    /// <summary>Queda de conexão mais curta que isso não aparece na grade: o PC reconecta sozinho.</summary>
    public const long CarenciaDeQuedaMs = 8000;

    /// <summary>A conexão caiu há pouco (<paramref name="caiuEm"/> em TickCount64; 0 = não caiu): ainda mostra como online.</summary>
    public static bool NaCarencia(long caiuEm, long agora) => caiuEm != 0 && agora - caiuEm < CarenciaDeQuedaMs;

    /// <summary>Assunto do comando no estado que o professor quer manter; null se o comando não é desse tipo.</summary>
    public static string? AssuntoDoEstado(AcaoComando acao) => acao switch
    {
        AcaoComando.BloquearTela or AcaoComando.DesbloquearTela => "tela",
        AcaoComando.BloquearInternet or AcaoComando.LiberarInternet => "internet",
        AcaoComando.Silenciar => "som",
        _ => null,
    };

    /// <summary>Comando que restringe o aluno (fica valendo e é reaplicado se o PC reconectar).</summary>
    public static bool Restritivo(AcaoComando acao, string argumento) =>
        acao is AcaoComando.BloquearTela or AcaoComando.BloquearInternet
        || (acao == AcaoComando.Silenciar && argumento.Trim() != "0");

    /// <summary>Comando que pode ser repetido sem efeito dobrado (se a conexão cair antes da resposta, vai de novo).</summary>
    public static bool PodeRepetir(AcaoComando acao) => acao is AcaoComando.BloquearTela or AcaoComando.DesbloquearTela
        or AcaoComando.BloquearInternet or AcaoComando.LiberarInternet or AcaoComando.Silenciar or AcaoComando.DefinirPapel;

    /// <summary>
    /// Atualização automática dos alunos: quanto esperar para mandar o instalador de novo a um PC depois de
    /// <paramref name="falhas"/> envios que não deram certo. 3, 6, 12, 24 e no máximo 30 minutos: um PC com problema
    /// não recebe 105 MB a cada 3 minutos para sempre.
    /// </summary>
    public static TimeSpan EsperaParaReenviarAtualizacao(int falhas) =>
        TimeSpan.FromMinutes(Math.Min(30, 3 * (1 << Math.Clamp(falhas, 0, 4))));

    /// <summary>A conexão cair logo depois deste comando é o próprio comando funcionando (o PC saiu da rede).</summary>
    public static bool SaiDaRede(AcaoComando acao) => acao is AcaoComando.Desligar or AcaoComando.Reiniciar or AcaoComando.Logoff;

    /// <summary>"Bloquear tela: enviado a 24 computadores ✓" ou "Bloquear tela: 22 ok — não chegou em PC-14, PC-23."</summary>
    public static string ResumoDoEnvio(string nomeComando, int total, IEnumerable<string> falharam)
    {
        var nomes = falharam.Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        if (nomes.Count == 0) return $"{nomeComando}: enviado a {total} computador{(total == 1 ? "" : "es")} ✓";
        string lista = nomes.Count <= 6 ? string.Join(", ", nomes) : $"{string.Join(", ", nomes.Take(6))} e mais {nomes.Count - 6}";
        return $"{nomeComando}: {total - nomes.Count} ok — não chegou em {lista}.";
    }
}
