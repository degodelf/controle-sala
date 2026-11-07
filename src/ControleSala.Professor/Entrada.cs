using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>
/// Cada professor se identifica pelo nome e aceita as regras de uso uma vez, para o registro de atividades dizer quem fez o quê.
/// Com um só professor no PC, o programa abre direto.
/// </summary>
static class Entrada
{
    /// <summary>Suba este número quando as regras mudarem: todos aceitam de novo.</summary>
    public const int VersaoDasRegras = 1;

    /// <summary>Quem entrou, ou null se a pessoa desistiu.</summary>
    public static PerfilProfessor? Identificar(RepositorioPerfis repositorio, Window? dono = null)
    {
        bool novo = repositorio.Perfis.Count == 0;
        var perfil = novo ? PrimeiroAcesso(repositorio, dono)
            : repositorio.Perfis.Count == 1 ? repositorio.Perfis[0]
            : Entrar(repositorio, dono);
        if (perfil == null || perfil.RegrasAceitas >= VersaoDasRegras) return perfil;

        // Perfil de versão antiga: uma vez só, confirma o nome (o padrão era "Professor").
        if (!novo && Mostrar(new JanelaNovoProfessor(repositorio, ModoPerfil.ConfirmarNome, perfil), dono) != true) return null;

        if (Mostrar(new JanelaRegrasDeUso(pedirAceite: true), dono) != true) return null;
        repositorio.AceitarRegras(perfil, VersaoDasRegras);
        Sessao.Atividades.Registrar(perfil.Nome, "Aceitou as regras de uso", detalhe: $"versão {VersaoDasRegras}");
        return perfil;
    }

    static PerfilProfessor? PrimeiroAcesso(RepositorioPerfis repositorio, Window? dono)
    {
        var novo = new JanelaNovoProfessor(repositorio, ModoPerfil.PrimeiroAcesso);
        return Mostrar(novo, dono) == true ? novo.Resultado : null;
    }

    static PerfilProfessor? Entrar(RepositorioPerfis repositorio, Window? dono)
    {
        var entrar = new JanelaEntrar(repositorio);
        return Mostrar(entrar, dono) == true ? entrar.Escolhido : null;
    }

    static bool? Mostrar(Window janela, Window? dono)
    {
        janela.Owner = dono;
        janela.WindowStartupLocation = dono == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        return janela.ShowDialog();
    }
}
