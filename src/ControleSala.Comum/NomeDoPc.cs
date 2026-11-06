namespace ControleSala.Comum;

/// <summary>O nome deste PC na sala, escolhido na instalação (ex.: "PC 01"). Só vale no ControleSala: o nome do Windows não muda.</summary>
public static class NomeDoPc
{
    public const int TamanhoMaximo = 40;

    public static string CaminhoPadrao => Path.Combine(Path.GetDirectoryName(ConfigAgente.CaminhoPadrao)!, "nome-pc.txt");

    /// <summary>O nome sem caracteres de controle nem espaços nas pontas; null se ficar vazio ou longo demais.</summary>
    public static string? Validar(string? nome)
    {
        string limpo = string.Concat((nome ?? "").Where(c => !char.IsControl(c))).Trim();
        return limpo.Length is 0 or > TamanhoMaximo ? null : limpo;
    }

    public static string? Ler(string? caminho = null)
    {
        string c = caminho ?? CaminhoPadrao;
        try
        {
            return File.Exists(c) ? Validar(File.ReadAllText(c)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Salvar(string nome, string? caminho = null)
    {
        string c = caminho ?? CaminhoPadrao;
        Directory.CreateDirectory(Path.GetDirectoryName(c)!);
        File.WriteAllText(c, nome);
    }
}
