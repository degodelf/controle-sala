using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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

    // ---- limpeza: configuração + imagens enviadas aos agentes ----

    public sealed record PacoteLimpezaDados(string Json, byte[]? PapelDeParede, byte[]? ImagemLimpeza);

    /// <summary>Caminho do papel de parede composto (fundo + logo) que vai para os alunos.</summary>
    public static string CaminhoPapelComposto => System.IO.Path.Combine(Pastas.Dados, "papel-de-parede.png");
    /// <summary>Caminho da imagem mostrada durante a limpeza.</summary>
    public static string CaminhoImagemLimpeza => System.IO.Path.Combine(Pastas.Dados, "imagem-limpeza.png");

    public static PacoteLimpezaDados? PacoteLimpeza { get; private set; }

    /// <summary>Remonta o pacote de limpeza a partir da configuração e das imagens salvas. Chamar ao iniciar e ao salvar a limpeza.</summary>
    public static void AtualizarLimpeza()
    {
        var config = LimpezaConfig.Carregar();
        byte[]? papel = config.RestaurarPapelDeParede ? LerSeExiste(CaminhoPapelComposto) : null;
        byte[]? imagem = LerSeExiste(CaminhoImagemLimpeza);
        PacoteLimpeza = new PacoteLimpezaDados(config.Serializar(), papel, imagem);
    }

    static byte[]? LerSeExiste(string caminho)
    {
        try { return System.IO.File.Exists(caminho) ? System.IO.File.ReadAllBytes(caminho) : null; }
        catch (System.IO.IOException) { return null; }
    }
}

static class CaixaDePin
{
    /// <summary>Deixa digitar só números (o PIN tem de 4 a 8 dígitos).</summary>
    public static void SoNumeros(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);
}

/// <summary>Mostra "PIN incorreto"/"bloqueado" e, no bloqueio, desabilita o botão contando os segundos.</summary>
sealed class AvisoDePin
{
    readonly TextBlock _texto;
    readonly Button _botao;
    readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromSeconds(1) };
    DateTime _fim;

    public AvisoDePin(TextBlock texto, Button botao)
    {
        _texto = texto;
        _botao = botao;
        _relogio.Tick += (_, _) => Atualizar();
    }

    public void Limpar()
    {
        _relogio.Stop();
        _texto.Text = "";
        _botao.IsEnabled = true;
    }

    public void Mostrar(ResultadoPin resultado, PerfilProfessor perfil, TimeSpan espera)
    {
        switch (resultado)
        {
            case ResultadoPin.Incorreto:
                int restam = RepositorioPerfis.FalhasAteBloquear - perfil.Falhas;
                _texto.Text = $"PIN incorreto. Mais {restam} tentativa{(restam == 1 ? "" : "s")} antes do bloqueio.";
                break;
            case ResultadoPin.Bloqueado:
                _fim = DateTime.UtcNow + espera;
                _botao.IsEnabled = false;
                Atualizar();
                _relogio.Start();
                break;
        }
    }

    void Atualizar()
    {
        int s = (int)Math.Ceiling((_fim - DateTime.UtcNow).TotalSeconds);
        if (s <= 0)
        {
            Limpar();
            return;
        }
        _texto.Text = $"Muitas tentativas erradas. Tente de novo em {s} s.";
    }
}
