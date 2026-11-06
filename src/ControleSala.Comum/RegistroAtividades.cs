using System.Text;
using System.Text.Json;

namespace ControleSala.Comum;

public sealed record Atividade(DateTimeOffset Quando, string Professor, string Acao, string Computador, string Detalhe);

/// <summary>
/// Quem fez o quê, e em qual computador: a prestação de contas da LGPD (art. 37). Fica só no PC do professor, um arquivo por mês,
/// e nunca guarda imagens de tela, texto de mensagens nem conteúdo de arquivos. Meses além de <see cref="MesesGuardados"/> são apagados.
/// </summary>
public sealed class RegistroAtividades
{
    public const int MesesGuardados = 6;
    const int TamanhoMaximoDetalhe = 120;
    const string Prefixo = "atividades-";

    readonly string _pasta;
    readonly TimeProvider _tempo;
    readonly Lock _trava = new();

    public RegistroAtividades(string? pasta = null, TimeProvider? tempo = null)
    {
        _pasta = pasta ?? Path.Combine(Pastas.Dados, "atividades");
        _tempo = tempo ?? TimeProvider.System;
    }

    string Arquivo(DateTimeOffset quando) => Path.Combine(_pasta, $"{Prefixo}{quando:yyyy-MM}.jsonl");

    /// <summary>Anota uma ação. Sem conseguir gravar, o programa segue: uma falha de disco não pode parar a aula.</summary>
    public void Registrar(string professor, string acao, string computador = "", string detalhe = "")
    {
        var agora = _tempo.GetLocalNow();
        string linha = JsonSerializer.Serialize(new Atividade(agora, professor, acao, computador, Resumir(detalhe)));
        lock (_trava)
        {
            try
            {
                Directory.CreateDirectory(_pasta);
                File.AppendAllText(Arquivo(agora), linha + "\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // segue sem registrar
            }
        }
    }

    static string Resumir(string texto)
    {
        string limpo = string.Concat(texto.Where(c => !char.IsControl(c))).Trim();
        return limpo.Length <= TamanhoMaximoDetalhe ? limpo : limpo[..TamanhoMaximoDetalhe] + "...";
    }

    /// <summary>Tudo o que está guardado, do mais novo para o mais antigo. Linhas danificadas são ignoradas.</summary>
    public List<Atividade> Ler()
    {
        var lista = new List<Atividade>();
        if (!Directory.Exists(_pasta)) return lista;
        lock (_trava)
        {
            foreach (string arquivo in Directory.GetFiles(_pasta, Prefixo + "*.jsonl"))
            {
                foreach (string linha in File.ReadLines(arquivo))
                {
                    try
                    {
                        if (JsonSerializer.Deserialize<Atividade>(linha) is { } a) lista.Add(a);
                    }
                    catch (JsonException)
                    {
                        // linha cortada (ex.: queda de energia): ignora
                    }
                }
            }
        }
        lista.Sort((a, b) => b.Quando.CompareTo(a.Quando));
        return lista;
    }

    /// <summary>Apaga os meses que já passaram do prazo de guarda.</summary>
    public void ApagarAntigos()
    {
        if (!Directory.Exists(_pasta)) return;
        var agora = _tempo.GetLocalNow();
        var limite = new DateTime(agora.Year, agora.Month, 1).AddMonths(-MesesGuardados);
        lock (_trava)
        {
            foreach (string arquivo in Directory.GetFiles(_pasta, Prefixo + "*.jsonl"))
            {
                string mes = Path.GetFileNameWithoutExtension(arquivo)[Prefixo.Length..];
                if (!DateTime.TryParseExact(mes, "yyyy-MM", null, System.Globalization.DateTimeStyles.None, out var data) || data >= limite) continue;
                try { File.Delete(arquivo); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* tenta na próxima abertura */ }
            }
        }
    }

    /// <summary>Grava uma planilha CSV que abre direto no Excel em português (separador ";" e acentos preservados).</summary>
    public static void ExportarCsv(IEnumerable<Atividade> atividades, string caminho)
    {
        var texto = new StringBuilder("Data e hora;Professor;Ação;Computador;Detalhe\r\n");
        foreach (var a in atividades)
            texto.Append(string.Join(';', a.Quando.ToString("dd/MM/yyyy HH:mm:ss"), Celula(a.Professor), Celula(a.Acao), Celula(a.Computador), Celula(a.Detalhe))).Append("\r\n");
        File.WriteAllText(caminho, texto.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>Entre aspas, e um apóstrofo na frente do que o Excel executaria como fórmula.</summary>
    static string Celula(string valor)
    {
        if (valor.Length > 0 && "=+-@".Contains(valor[0])) valor = "'" + valor;
        return $"\"{valor.Replace("\"", "\"\"")}\"";
    }

    public static string NomeDoComando(AcaoComando acao) => acao switch
    {
        AcaoComando.AbrirLink => "Abrir link",
        AcaoComando.Mensagem => "Enviar mensagem",
        AcaoComando.BloquearTela => "Bloquear a tela",
        AcaoComando.DesbloquearTela => "Desbloquear a tela",
        AcaoComando.TransmitirProfessor => "Transmitir a tela do professor",
        AcaoComando.AbrirPrograma => "Abrir programa",
        AcaoComando.FecharPrograma => "Fechar programa",
        AcaoComando.Silenciar => "Silenciar o som",
        AcaoComando.BloquearInternet => "Bloquear a internet",
        AcaoComando.LiberarInternet => "Liberar a internet",
        AcaoComando.Desligar => "Desligar",
        AcaoComando.Reiniciar => "Reiniciar",
        AcaoComando.Logoff => "Encerrar a sessão",
        AcaoComando.Limpeza => "Limpar agora",
        AcaoComando.DefinirPapel => "Aplicar papel de parede",
        AcaoComando.IniciarAplicativo => "Iniciar aplicativo ou comando",
        _ => acao.ToString(),
    };

    /// <summary>O registro diz o que foi feito, não o conteúdo: texto de mensagem e outros argumentos ficam de fora.</summary>
    public static string DetalheDoComando(AcaoComando acao, string argumento) =>
        acao is AcaoComando.AbrirLink or AcaoComando.AbrirPrograma or AcaoComando.FecharPrograma or AcaoComando.IniciarAplicativo
            ? argumento : "";
}
