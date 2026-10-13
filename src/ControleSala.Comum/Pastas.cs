namespace ControleSala.Comum;

public static class Pastas
{
    /// <summary>Onde ficam as configurações (perfis, papel de parede...). Pode ser trocada com <c>--dados</c>.</summary>
    public static string Dados { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ControleSala");
}
