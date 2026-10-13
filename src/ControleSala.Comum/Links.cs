namespace ControleSala.Comum;

/// <summary>
/// Confere um link antes de abri-lo no PC do aluno. Só http/https: nada de abrir arquivo local (file:),
/// programa, ou outros esquemas que poderiam rodar algo. Vale nas duas pontas (professor e agente).
/// </summary>
public static class Links
{
    public const int TamanhoMaximo = 2048;

    /// <summary>Mensagem de erro para o professor, ou null se o link pode ser aberto. Devolve a URL já normalizada em <paramref name="normalizado"/>.</summary>
    public static string? Validar(string? link, out string normalizado)
    {
        normalizado = "";
        link = link?.Trim() ?? "";
        if (link.Length == 0) return "Digite um endereço (link).";
        if (link.Length > TamanhoMaximo) return "O link é longo demais.";

        // Sem nenhum esquema (http:, mailto:, file:...): assume https (o professor digita "escola.edu.br").
        // Se já tiver um esquema, não mexe — um esquema que não seja http/https é recusado logo abaixo.
        if (!System.Text.RegularExpressions.Regex.IsMatch(link, @"^[a-zA-Z][a-zA-Z0-9+.\-]*:"))
            link = "https://" + link;

        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            return "Esse link não é válido.";
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return "Só é possível abrir links que começam com http:// ou https://.";
        if (string.IsNullOrEmpty(uri.Host))
            return "O link não tem um endereço de site.";

        normalizado = uri.AbsoluteUri;
        return null;
    }

    public static bool EhValido(string? link) => Validar(link, out _) == null;
}
