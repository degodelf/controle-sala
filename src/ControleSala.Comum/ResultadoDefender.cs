using System.Text.Json;

namespace ControleSala.Comum;

public enum EtapaDefender { NaoPedida, EmAndamento, Ok, Falhou, Interrompida }

/// <summary>O que aconteceu na última atualização e verificação do Windows Defender deste PC. O agente guarda e informa ao professor.</summary>
public sealed record ResultadoDefender(EtapaDefender Atualizacao, EtapaDefender Verificacao, DateTimeOffset Inicio,
    DateTimeOffset? Fim = null, bool? AntivirusAtivo = null, DateTimeOffset? Definicoes = null, string Detalhe = "")
{
    const string MarcaAtualizacao = "CS_ATUALIZACAO=", MarcaVerificacao = "CS_VERIFICACAO=", MarcaAtivo = "CS_ATIVO=", MarcaDefinicoes = "CS_DEFINICOES=";
    const string Erro = "erro:";
    const int TamanhoMaximoDetalhe = 160;

    public bool EmAndamento => Atualizacao == EtapaDefender.EmAndamento || Verificacao == EtapaDefender.EmAndamento;

    public static ResultadoDefender Iniciado(bool atualizar, bool verificar, DateTimeOffset agora) => new(
        atualizar ? EtapaDefender.EmAndamento : EtapaDefender.NaoPedida,
        verificar ? EtapaDefender.EmAndamento : EtapaDefender.NaoPedida, agora);

    /// <summary>
    /// Comando do PowerShell. Cada etapa tem o próprio try: a verificação roda mesmo se a atualização falhar (ex.: PC sem internet).
    /// Só usa aspas simples, porque ele vai entre aspas duplas na linha de comando.
    /// </summary>
    public static string Script(bool atualizar, bool verificar, bool varreduraCompleta)
    {
        var passos = new List<string> { "[Console]::OutputEncoding=[Text.Encoding]::UTF8" };
        if (atualizar) passos.Add(Etapa("Update-MpSignature -ErrorAction Stop", MarcaAtualizacao));
        if (verificar) passos.Add(Etapa($"Start-MpScan -ScanType {(varreduraCompleta ? "FullScan" : "QuickScan")} -ErrorAction Stop", MarcaVerificacao));
        passos.Add($"try {{ $s = Get-MpComputerStatus -ErrorAction Stop; '{MarcaAtivo}' + $s.AntivirusEnabled; " +
                   $"'{MarcaDefinicoes}' + $s.AntivirusSignatureLastUpdated.ToUniversalTime().ToString('o') }} catch {{ }}");
        return string.Join("; ", passos);
    }

    static string Etapa(string comando, string marca) =>
        $"try {{ {comando}; '{marca}ok' }} catch {{ '{marca}{Erro}' + $_.Exception.Message }}";

    /// <summary>Lê a saída do script. Etapa pedida que não respondeu conta como falha: o PowerShell pode ter sido encerrado no meio.</summary>
    public ResultadoDefender Concluido(string saida, DateTimeOffset agora)
    {
        var r = this with
        {
            Fim = agora,
            Atualizacao = Atualizacao == EtapaDefender.NaoPedida ? EtapaDefender.NaoPedida : EtapaDefender.Falhou,
            Verificacao = Verificacao == EtapaDefender.NaoPedida ? EtapaDefender.NaoPedida : EtapaDefender.Falhou,
        };
        var erros = new List<string>();
        foreach (string bruta in saida.Split('\n'))
        {
            string linha = bruta.Trim();
            if (linha.StartsWith(MarcaAtualizacao, StringComparison.Ordinal))
                r = r with { Atualizacao = Estado(linha[MarcaAtualizacao.Length..], "Atualização", erros) };
            else if (linha.StartsWith(MarcaVerificacao, StringComparison.Ordinal))
                r = r with { Verificacao = Estado(linha[MarcaVerificacao.Length..], "Verificação", erros) };
            else if (linha.StartsWith(MarcaAtivo, StringComparison.Ordinal))
                r = r with { AntivirusAtivo = bool.TryParse(linha[MarcaAtivo.Length..], out bool ativo) ? ativo : null };
            else if (linha.StartsWith(MarcaDefinicoes, StringComparison.Ordinal))
                r = r with { Definicoes = DateTimeOffset.TryParse(linha[MarcaDefinicoes.Length..], out var data) ? data : null };
        }
        string detalhe = string.Join(" ", erros);
        return r with { Detalhe = detalhe.Length <= TamanhoMaximoDetalhe ? detalhe : detalhe[..TamanhoMaximoDetalhe] + "..." };
    }

    static EtapaDefender Estado(string valor, string nome, List<string> erros)
    {
        if (!valor.StartsWith(Erro, StringComparison.Ordinal)) return EtapaDefender.Ok;
        erros.Add($"{nome}: {valor[Erro.Length..].Trim()}");
        return EtapaDefender.Falhou;
    }

    /// <summary>Não deu para rodar o PowerShell: as etapas pedidas falharam.</summary>
    public ResultadoDefender Falhou(string motivo, DateTimeOffset agora) => this with
    {
        Fim = agora,
        Detalhe = motivo,
        Atualizacao = Atualizacao == EtapaDefender.NaoPedida ? EtapaDefender.NaoPedida : EtapaDefender.Falhou,
        Verificacao = Verificacao == EtapaDefender.NaoPedida ? EtapaDefender.NaoPedida : EtapaDefender.Falhou,
    };

    /// <summary>O agente reabriu com o Defender "em andamento": o PC foi desligado ou reiniciado no meio.</summary>
    public ResultadoDefender Interrompido(DateTimeOffset agora) => EmAndamento ? this with
    {
        Fim = agora,
        Atualizacao = Atualizacao == EtapaDefender.EmAndamento ? EtapaDefender.Interrompida : Atualizacao,
        Verificacao = Verificacao == EtapaDefender.EmAndamento ? EtapaDefender.Interrompida : Verificacao,
    } : this;

    public static string TextoDaEtapa(EtapaDefender etapa) => etapa switch
    {
        EtapaDefender.NaoPedida => "não pedida",
        EtapaDefender.EmAndamento => "em andamento",
        EtapaDefender.Ok => "ok",
        EtapaDefender.Falhou => "falhou",
        _ => "interrompida",
    };

    /// <summary>Uma frase para o registro do PC e para a janela do professor.</summary>
    public string Resumo()
    {
        string texto = $"atualização {TextoDaEtapa(Atualizacao)}, verificação {TextoDaEtapa(Verificacao)}";
        if (AntivirusAtivo == false) texto += ", Defender desativado neste PC";
        return Detalhe.Length == 0 ? texto : $"{texto}. {Detalhe}";
    }

    public string Serializar() => JsonSerializer.Serialize(this);

    public static ResultadoDefender? Desserializar(string json)
    {
        try { return JsonSerializer.Deserialize<ResultadoDefender>(json); }
        catch (JsonException) { return null; }
    }
}
