using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ControleSala.Comum;

/// <summary>O que aconteceu na última limpeza de um PC. O agente guarda e informa ao professor quando ele conectar.</summary>
public sealed record ResultadoLimpeza(DateTimeOffset Inicio, DateTimeOffset Fim, bool Automatica, string Pedida, bool Ok, long Bytes, string Detalhe = "")
{
    public string Serializar() => JsonSerializer.Serialize(this);

    public static ResultadoLimpeza? Desserializar(string json)
    {
        try { return JsonSerializer.Deserialize<ResultadoLimpeza>(json); }
        catch (JsonException) { return null; }
    }
}

public sealed record LinhaRelatorio(string Computador, ResultadoLimpeza Resultado);

/// <summary>Uma limpeza da sala: os computadores que limparam em até <see cref="RepositorioRelatorios.JanelaDeUmaLimpeza"/> entram juntos.</summary>
public sealed class RelatorioLimpeza
{
    public string Id { get; set; } = "";
    public DateTimeOffset Inicio { get; set; }
    public string Escola { get; set; } = "";
    public string Sala { get; set; } = "";
    public List<LinhaRelatorio> Linhas { get; set; } = [];

    public int Limpos => Linhas.Count(l => l.Resultado.Ok);
    public long Bytes => Linhas.Sum(l => l.Resultado.Bytes);
    public int ComProblema => Linhas.Count(l => !l.Resultado.Ok);

    public string Tipo => Linhas.FirstOrDefault()?.Resultado is { } r
        ? r.Automatica ? "Limpeza agendada" : r.Pedida.Length > 0 ? $"Limpar agora, por {r.Pedida}" : "Limpar agora"
        : "Limpeza";

    /// <summary>Em GB, como o Windows mostra (1 GB = 1024 MB), com vírgula.</summary>
    public static string EmGb(long bytes)
    {
        double gb = bytes / (1024.0 * 1024 * 1024);
        var br = CultureInfo.GetCultureInfo("pt-BR");
        return bytes > 0 && gb < 0.01 ? $"menos de {0.01.ToString("0.00", br)} GB" : $"{gb.ToString("0.00", br)} GB";
    }

    /// <summary>Página única, sem nada externo, que abre em qualquer navegador: uma barra em GB por computador e o total no final.</summary>
    public string GerarHtml()
    {
        static string E(string texto) => WebUtility.HtmlEncode(texto);
        string local = string.Join(" · ", new[] { Escola, Sala }.Where(t => t.Length > 0));
        long maior = Math.Max(1, Linhas.Select(l => l.Resultado.Bytes).DefaultIfEmpty(0).Max());

        var h = new StringBuilder();
        h.Append("""
            <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Relatório de limpeza</title><style>
            :root{--fg:#17222b;--fraco:#5b6b78;--linha:#e3e9ee;--trilho:#eef2f5;--barra:#0b5f6b;--erro:#a32a22}
            body{margin:0;background:#fff;color:var(--fg);font:15px/1.5 "Segoe UI",Arial,sans-serif}
            main{max-width:860px;margin:0 auto;padding:32px 24px}
            h1{margin:0;font-size:28px;font-weight:600}.sub{color:var(--fraco);margin-top:2px}
            header{border-bottom:3px solid var(--barra);padding-bottom:12px;margin-bottom:18px}
            .pc{display:grid;grid-template-columns:110px 1fr 130px;gap:12px;align-items:center;padding:6px 0}
            .trilho{height:14px;background:var(--trilho);border-radius:0 4px 4px 0}
            .barra{height:14px;background:var(--barra);border-radius:0 4px 4px 0;min-width:2px}
            .valor{text-align:right;font-variant-numeric:tabular-nums}.falhou{color:var(--erro);font-weight:600}
            .total{display:flex;justify-content:space-between;align-items:baseline;gap:16px;border-top:2px solid var(--fg);margin-top:12px;padding-top:12px}
            .total b{font-size:26px;font-weight:600;font-variant-numeric:tabular-nums}
            footer{color:var(--fraco);font-size:12px;margin-top:22px}@media print{main{max-width:none;padding:0}.barra,.trilho{print-color-adjust:exact;-webkit-print-color-adjust:exact}}
            </style></head><body><main>
            """);
        h.Append($"<header><h1>Relatório de limpeza</h1><div class=\"sub\">{E(local)}</div>");
        h.Append($"<div class=\"sub\">{E(Tipo)} · {Inicio.ToLocalTime():dd/MM/yyyy} às {Inicio.ToLocalTime():HH:mm}</div></header>");
        foreach (var l in Linhas.OrderBy(l => l.Computador, StringComparer.CurrentCultureIgnoreCase))
        {
            var r = l.Resultado;
            h.Append($"<div class=\"pc\"><div>{E(l.Computador)}</div>");
            if (r.Ok)
            {
                double largura = 100.0 * r.Bytes / maior;
                string texto = EmGb(r.Bytes);
                h.Append($"<div class=\"trilho\" title=\"{E(l.Computador)}: {E(texto)}\"><div class=\"barra\" style=\"width:{largura.ToString("0.0", CultureInfo.InvariantCulture)}%\"></div></div>");
                h.Append($"<div class=\"valor\">{E(texto)}</div></div>");
            }
            else
            {
                h.Append($"<div class=\"falhou\">⚠ Não limpou: {E(r.Detalhe)}</div><div class=\"valor\">—</div></div>");
            }
        }
        h.Append($"<div class=\"total\"><span>Total em {Limpos} de {Linhas.Count} computadores</span><b>{E(EmGb(Bytes))}</b></div>");
        h.Append($"<footer>Só aparecem computadores que já informaram o resultado. {E(Creditos.Linha)}</footer></main></body></html>");
        return h.ToString();
    }
}

/// <summary>Os relatórios no PC do professor: um .html para abrir e um .json para acrescentar os computadores que chegarem depois.</summary>
public sealed class RepositorioRelatorios(string? pasta = null)
{
    public const int Guardados = 5;
    public static readonly TimeSpan JanelaDeUmaLimpeza = TimeSpan.FromHours(2);
    const string Prefixo = "limpeza-";

    public string Pasta { get; } = pasta ?? Path.Combine(Pastas.Dados, "relatorios");

    string Caminho(string id, string extensao) => Path.Combine(Pasta, $"{Prefixo}{id}.{extensao}");
    public string CaminhoHtml(RelatorioLimpeza r) => Caminho(r.Id, "html");

    /// <summary>Os relatórios guardados, do mais novo para o mais antigo.</summary>
    public List<RelatorioLimpeza> Listar()
    {
        var lista = new List<RelatorioLimpeza>();
        if (!Directory.Exists(Pasta)) return lista;
        foreach (string arquivo in Directory.GetFiles(Pasta, Prefixo + "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<RelatorioLimpeza>(File.ReadAllText(arquivo)) is { } r) lista.Add(r);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // arquivo danificado: ignora
            }
        }
        lista.Sort((a, b) => b.Inicio.CompareTo(a.Inicio));
        return lista;
    }

    /// <summary>Junta o resultado de um PC ao relatório daquela limpeza. Null se nada mudou: o agente reenvia o último resultado a cada conexão.</summary>
    public RelatorioLimpeza? Registrar(string computador, ResultadoLimpeza resultado, string escola, string sala)
    {
        var lista = Listar();
        var relatorio = lista.FirstOrDefault(r => (r.Inicio - resultado.Inicio).Duration() <= JanelaDeUmaLimpeza);
        if (relatorio == null)
        {
            // Resultado antigo de um PC que ficou tempo sem conectar: não ressuscita um relatório já apagado.
            if (lista.Count >= Guardados && resultado.Inicio < lista[^1].Inicio) return null;
            relatorio = new RelatorioLimpeza { Id = $"{resultado.Inicio.ToLocalTime():yyyy-MM-dd-HHmmss}", Inicio = resultado.Inicio, Escola = escola, Sala = sala };
            lista.Insert(0, relatorio);
        }

        var linha = new LinhaRelatorio(computador, resultado);
        int i = relatorio.Linhas.FindIndex(l => l.Computador == computador);
        if (i >= 0 && relatorio.Linhas[i] == linha) return null;
        if (i >= 0) relatorio.Linhas[i] = linha;
        else relatorio.Linhas.Add(linha);

        Directory.CreateDirectory(Pasta);
        File.WriteAllText(Caminho(relatorio.Id, "json"), JsonSerializer.Serialize(relatorio));
        File.WriteAllText(CaminhoHtml(relatorio), relatorio.GerarHtml());
        ApagarExcedentes();
        return relatorio;
    }

    void ApagarExcedentes()
    {
        foreach (var antigo in Listar().Skip(Guardados))
        {
            File.Delete(Caminho(antigo.Id, "json"));
            File.Delete(Caminho(antigo.Id, "html"));
        }
    }
}
