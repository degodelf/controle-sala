namespace ControleSala.Comum;

/// <summary>Créditos exibidos (de forma discreta) no Professor, no Agente e nos avisos ao aluno.</summary>
public static class Creditos
{
    public const string Autor = "Prof. Diego Sabino Delfino";
    public const string Linha = "ControleSala · criado pelo professor Diego Sabino Delfino";

    /// <summary>O crédito com a versão embutida, para mostrar de forma discreta (ex.: "ControleSala 0.3.0 · criado...").</summary>
    public static string LinhaComVersao(string versao) => Linha.Replace("ControleSala ", $"ControleSala {versao} ");
}
