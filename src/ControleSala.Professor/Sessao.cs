using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Quem está usando o programa agora (usado nos avisos e no histórico que virão).</summary>
static class Sessao
{
    public static RepositorioPerfis Repositorio { get; private set; } = new();
    public static PerfilProfessor? Atual { get; private set; }

    /// <summary>A sala (escola, nome e chave) configurada neste PC; null até o professor criar a sala.</summary>
    public static ConfigSala? Sala { get; private set; }

    public static void CarregarSala() => Sala = ConfigProfessor.Carregar();

    public static void DefinirSala(ConfigSala sala)
    {
        ConfigProfessor.Salvar(sala);
        Sala = sala;
    }

    public static void Iniciar(RepositorioPerfis repositorio, PerfilProfessor professor)
    {
        Repositorio = repositorio;
        Atual = professor;
    }

    // Criado na primeira vez que é usado: a pasta de dados pode ser trocada por --dados ao abrir o programa.
    static readonly Lazy<RegistroAtividades> _atividades = new(() => new RegistroAtividades());
    public static RegistroAtividades Atividades => _atividades.Value;

    /// <summary>Anota no registro de atividades o que o professor atual fez.</summary>
    public static void Registrar(string acao, string computador = "", string detalhe = "") =>
        Atividades.Registrar(Atual?.Nome ?? "(sem professor)", acao, computador, detalhe);

    // ---- limpeza: configuração + imagens enviadas aos agentes ----

    /// <param name="Json">Null se este PC ainda não tem configuração (ex.: formatado): os alunos continuam com a deles.</param>
    public sealed record PacoteLimpezaDados(string? Json, byte[]? PapelDeParede, byte[]? ImagemLimpeza);

    /// <summary>Caminho do papel de parede pronto que vai para os alunos.</summary>
    public static string CaminhoPapelComposto => System.IO.Path.Combine(Pastas.Dados, "papel-de-parede.png");

    public static PacoteLimpezaDados? PacoteLimpeza { get; private set; }

    /// <summary>Remonta o pacote de limpeza a partir da configuração e das imagens salvas. Chamar ao iniciar e ao salvar a limpeza.</summary>
    public static void AtualizarLimpeza()
    {
        var config = LimpezaConfig.Carregar();
        byte[]? papel = config.RestaurarPapelDeParede ? LerSeExiste(CaminhoPapelComposto) : null;
        // Sem logotipo: o papel de parede é só a imagem escolhida, e a tela de limpeza usa o ícone do programa.
        PacoteLimpeza = new PacoteLimpezaDados(LimpezaConfig.Existe() ? config.Serializar() : null, papel, null);
    }

    static byte[]? LerSeExiste(string caminho)
    {
        try { return System.IO.File.Exists(caminho) ? System.IO.File.ReadAllBytes(caminho) : null; }
        catch (System.IO.IOException) { return null; }
    }
}
