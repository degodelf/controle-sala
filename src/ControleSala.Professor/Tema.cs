using System.IO;
using System.Windows;
using System.Windows.Media;
using ControleSala.Comum;

namespace ControleSala.Professor;

public enum ModoTema { Claro, Escuro }

/// <summary>
/// Troca as cores de <c>Cores.xaml</c> entre claro e escuro, em todas as janelas abertas, e lembra a escolha.
/// As janelas usam os nomes das cores (DynamicResource), então a troca aparece na hora, sem reabrir nada.
/// </summary>
static class Tema
{
    static string Arquivo => Path.Combine(Pastas.Dados, "tema.txt");

    public static ModoTema Atual { get; private set; } = ModoTema.Escuro;

    // Cada nome de cor com a versão clara e a escura. A escura é igual ao que está em Cores.xaml.
    static readonly (string Nome, string Claro, string Escuro)[] Paleta =
    [
        ("CorFundo",        "#FFF3F4F6", "#FF1E1E1E"),
        ("CorSuperficie",   "#FFFFFFFF", "#FF252526"),
        ("CorSuperficie2",  "#FFEAECEF", "#FF2D2D30"),
        ("CorBorda",        "#FFD0D3D8", "#FF3C3C3C"),
        ("CorTexto",        "#FF1B1F24", "#FFEDEDED"),
        ("CorTextoFraco",   "#FF6B7078", "#FF9A9A9A"),
        ("CorRealce",       "#FF2D6CDF", "#FF2D6CDF"),
        ("CorRealceTexto",  "#FFFFFFFF", "#FFFFFFFF"),
        ("CorBotao",        "#FFE6E8EB", "#FF333337"),
        ("CorBotaoSobre",   "#FFD8DBDF", "#FF3E3E42"),
        ("CorCampo",        "#FFFFFFFF", "#FF1B1B1C"),
        ("CorTela",         "#FF0B0B0C", "#FF101012"),
        ("CorOnline",       "#FF2E9E43", "#FF4CAF50"),
        ("CorConectando",   "#FFC98A00", "#FFFFC107"),
        ("CorOffline",      "#FFD33A32", "#FFE53935"),
        ("CorAviso",        "#FFB4702A", "#FFE0A060"),
    ];

    public static void Iniciar()
    {
        Aplicar(Ler(), salvar: false);
    }

    public static void Alternar() => Aplicar(Atual == ModoTema.Escuro ? ModoTema.Claro : ModoTema.Escuro, salvar: true);

    public static void Aplicar(ModoTema modo, bool salvar = true)
    {
        Atual = modo;
        var recursos = Application.Current.Resources;
        foreach (var (nome, claro, escuro) in Paleta)
            recursos[nome] = (Color)ColorConverter.ConvertFromString(modo == ModoTema.Claro ? claro : escuro)!;
        if (salvar) Salvar(modo);
    }

    static ModoTema Ler()
    {
        try
        {
            if (File.Exists(Arquivo) && File.ReadAllText(Arquivo).Trim().Equals("claro", StringComparison.OrdinalIgnoreCase))
                return ModoTema.Claro;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // sem acesso ao arquivo: fica no tema padrão
        }
        return ModoTema.Escuro;
    }

    static void Salvar(ModoTema modo)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
            File.WriteAllText(Arquivo, modo == ModoTema.Claro ? "claro" : "escuro");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // não conseguiu guardar a preferência: não é motivo para falhar
        }
    }
}
