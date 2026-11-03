using System.IO;
using System.Text.Json;
using System.Windows.Input;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Ações da janela principal que podem ter um atalho de teclado.</summary>
enum AcaoAtalho
{
    AtualizarTelas, SelecionarTodos, BloquearTela, DesbloquearTela,
    Mensagem, AbrirLink, EnviarArquivos, SilenciarSom, LimparAgora, Desligar,
}

/// <summary>Atalhos de teclado do professor (personalizáveis). Arquivo: <c>atalhos.json</c>.</summary>
sealed class RepositorioAtalhos
{
    /// <summary>A ação, o texto que o professor vê e o atalho padrão (vazio = sem atalho).</summary>
    public static readonly (AcaoAtalho Acao, string Rotulo, string Padrao)[] Catalogo =
    [
        (AcaoAtalho.AtualizarTelas,  "Atualizar as telas", "F5"),
        (AcaoAtalho.SelecionarTodos, "Selecionar todos",   "Ctrl+A"),
        (AcaoAtalho.BloquearTela,    "Bloquear a tela",    "Ctrl+B"),
        (AcaoAtalho.DesbloquearTela, "Desbloquear a tela", "Ctrl+D"),
        (AcaoAtalho.Mensagem,        "Enviar mensagem",    "Ctrl+M"),
        (AcaoAtalho.AbrirLink,       "Abrir link",         "Ctrl+L"),
        (AcaoAtalho.EnviarArquivos,  "Enviar arquivos",    "Ctrl+E"),
        (AcaoAtalho.SilenciarSom,    "Silenciar o som",    "Ctrl+Shift+S"),
        (AcaoAtalho.LimparAgora,     "Limpar agora",       ""),
        (AcaoAtalho.Desligar,        "Desligar",           ""),
    ];

    readonly string _caminho;
    readonly Dictionary<AcaoAtalho, string> _mapa = [];

    public RepositorioAtalhos(string? caminho = null)
    {
        _caminho = caminho ?? Path.Combine(Pastas.Dados, "atalhos.json");
        Carregar();
    }

    void Carregar()
    {
        foreach (var it in Catalogo) _mapa[it.Acao] = it.Padrao;   // começa nos padrões
        try
        {
            if (File.Exists(_caminho)
                && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_caminho)) is { } salvos)
                foreach (var (k, v) in salvos)
                    if (Enum.TryParse<AcaoAtalho>(k, out var a)) _mapa[a] = v ?? "";
        }
        catch (Exception e) when (e is IOException or JsonException) { }
    }

    void Salvar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
            var obj = _mapa.ToDictionary(p => p.Key.ToString(), p => p.Value);
            File.WriteAllText(_caminho, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public string Gesto(AcaoAtalho a) => _mapa.GetValueOrDefault(a, "");

    /// <summary>A ação ligada a esta combinação de teclas, ou null. Combinação vazia nunca casa.</summary>
    public AcaoAtalho? Acao(string gesto)
    {
        if (string.IsNullOrEmpty(gesto)) return null;
        foreach (var (a, g) in _mapa)
            if (string.Equals(g, gesto, StringComparison.OrdinalIgnoreCase)) return a;
        return null;
    }

    public void Definir(AcaoAtalho acao, string gesto)
    {
        // Mantém os atalhos únicos: tira a combinação de quem já a tinha.
        if (!string.IsNullOrEmpty(gesto))
            foreach (var a in _mapa.Keys.ToList())
                if (a != acao && string.Equals(_mapa[a], gesto, StringComparison.OrdinalIgnoreCase)) _mapa[a] = "";
        _mapa[acao] = gesto;
        Salvar();
    }

    public void RestaurarPadroes()
    {
        foreach (var it in Catalogo) _mapa[it.Acao] = it.Padrao;
        Salvar();
    }

    /// <summary>A tecla real do evento (resolve o Alt, que chega como System).</summary>
    public static Key TeclaReal(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

    /// <summary>Texto da combinação (ex.: "Ctrl+Shift+B"), ou "" se só há modificador.</summary>
    public static string Formatar(ModifierKeys mod, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.System or Key.None or Key.LWin or Key.RWin)
            return ""; // só modificador: ainda não é um atalho
        var partes = new List<string>();
        if (mod.HasFlag(ModifierKeys.Control)) partes.Add("Ctrl");
        if (mod.HasFlag(ModifierKeys.Shift)) partes.Add("Shift");
        if (mod.HasFlag(ModifierKeys.Alt)) partes.Add("Alt");
        partes.Add(key.ToString());
        return string.Join("+", partes);
    }
}
