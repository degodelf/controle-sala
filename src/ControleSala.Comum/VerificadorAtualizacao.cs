using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ControleSala.Comum;

/// <summary>Uma versão nova encontrada no GitHub: a versão, o endereço do instalador e as notas.</summary>
public sealed record AtualizacaoDisponivel(string Versao, string UrlInstalador, string Notas);

/// <summary>
/// Checa no GitHub se saiu uma versão mais nova do ControleSala (Releases do repositório do autor) e avisa o professor.
/// Só lê informação pública; não baixa nada sozinho. A parte que interpreta o JSON é separada, para dar para testar.
/// </summary>
public static class VerificadorAtualizacao
{
    public const string Repositorio = "degodelf/controle-sala";
    static readonly string Url = $"https://api.github.com/repos/{Repositorio}/releases/latest";

    /// <summary>
    /// Interpreta a resposta do GitHub. Devolve a atualização se a versão for mais nova que <paramref name="versaoAtual"/>
    /// e houver um instalador anexado; senão, null.
    /// </summary>
    public static AtualizacaoDisponivel? Analisar(string json, string? versaoAtual)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return null;
            if (raiz.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
            if (raiz.TryGetProperty("prerelease", out var pr) && pr.ValueKind == JsonValueKind.True) return null;

            string tag = raiz.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string versao = tag.TrimStart('v', 'V');
            if (!Versoes.MaisNova(versao, versaoAtual)) return null;

            // Acha o instalador anexado (ControleSala-Setup-*.exe).
            string? urlInstalador = null;
            if (raiz.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                {
                    string nome = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (nome.StartsWith("ControleSala-Setup", StringComparison.OrdinalIgnoreCase)
                        && nome.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        && a.TryGetProperty("browser_download_url", out var u))
                    {
                        urlInstalador = u.GetString();
                        break;
                    }
                }
            if (string.IsNullOrEmpty(urlInstalador)) return null;

            string notas = raiz.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            return new AtualizacaoDisponivel(versao, urlInstalador, notas);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Pergunta ao GitHub e devolve a atualização (ou null se não houver, ou se a internet falhar).</summary>
    public static async Task<AtualizacaoDisponivel?> VerificarAsync(string? versaoAtual, HttpClient? http = null, CancellationToken ct = default)
    {
        var cliente = http ?? Padrao.Value;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Url);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("ControleSala", versaoAtual ?? "0"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var resp = await cliente.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return Analisar(await resp.Content.ReadAsStringAsync(ct), versaoAtual);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null; // sem internet / demorou: não avisa nada
        }
    }

    static readonly Lazy<HttpClient> Padrao = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
}
