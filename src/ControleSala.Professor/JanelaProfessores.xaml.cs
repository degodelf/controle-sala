using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

public partial class JanelaProfessores : Window
{
    sealed record Item(PerfilProfessor Perfil, string Texto);

    readonly RepositorioPerfis _repositorio;
    readonly PerfilProfessor _atual;

    public JanelaProfessores(RepositorioPerfis repositorio, PerfilProfessor atual)
    {
        _repositorio = repositorio;
        _atual = atual;
        InitializeComponent();
        Atualizar();
    }

    void Atualizar()
    {
        Lista.ItemsSource = _repositorio.Perfis
            .Select(p => new Item(p, Rotulo(p)))
            .ToList();
    }

    string Rotulo(PerfilProfessor p)
    {
        string selo = RepositorioPerfis.TemPin(p) ? "com senha" : "sem senha";
        return p == _atual ? $"{p.Nome}   (em uso · {selo})" : $"{p.Nome}   ({selo})";
    }

    PerfilProfessor? Selecionado => (Lista.SelectedItem as Item)?.Perfil;

    /// <summary>Ações sensíveis pedem o PIN de quem está usando, para não valerem num programa deixado aberto.
    /// Se quem está usando não tem senha, não há o que confirmar.</summary>
    bool ConfirmarQuemEstaUsando(string pergunta) =>
        !RepositorioPerfis.TemPin(_atual) ||
        new JanelaConfirmarPin(_repositorio, _atual, pergunta) { Owner = this }.ShowDialog() == true;

    void Adicionar_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        if (!ConfirmarQuemEstaUsando($"{_atual.Nome}, digite o seu PIN para adicionar um professor:")) return;
        if (new JanelaNovoProfessor(_repositorio, ModoPerfil.Novo) { Owner = this }.ShowDialog() == true)
            Atualizar();
    }

    void TrocarPin_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        if (Selecionado is not { } perfil)
        {
            TextoMensagem.Text = "Escolha um professor na lista.";
            return;
        }
        if (new JanelaNovoProfessor(_repositorio, ModoPerfil.TrocarPin, perfil) { Owner = this }.ShowDialog() == true)
        {
            Atualizar();
            TextoMensagem.Text = $"Senha de {perfil.Nome} definida.";
        }
    }

    void RemoverSenha_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        if (Selecionado is not { } perfil)
        {
            TextoMensagem.Text = "Escolha um professor na lista.";
            return;
        }
        if (!RepositorioPerfis.TemPin(perfil))
        {
            TextoMensagem.Text = $"{perfil.Nome} já entra sem senha.";
            return;
        }
        if (!ConfirmarQuemEstaUsando($"{_atual.Nome}, digite o seu PIN para remover a senha de {perfil.Nome}:")) return;
        _repositorio.RemoverPin(perfil);
        Atualizar();
        TextoMensagem.Text = $"{perfil.Nome} agora entra sem senha.";
    }

    void Remover_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        if (Selecionado is not { } perfil)
        {
            TextoMensagem.Text = "Escolha um professor na lista.";
            return;
        }
        if (perfil == _atual)
        {
            TextoMensagem.Text = "Você não pode remover quem está usando o programa agora.";
            return;
        }
        if (_repositorio.Perfis.Count <= 1)
        {
            TextoMensagem.Text = "É preciso manter pelo menos um professor.";
            return;
        }
        if (!ConfirmarQuemEstaUsando($"{_atual.Nome}, digite o seu PIN para remover {perfil.Nome}:")) return;
        if (MessageBox.Show(this, $"Remover o professor {perfil.Nome}?", "Remover professor",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (_repositorio.Remover(perfil))
        {
            Atualizar();
            TextoMensagem.Text = $"{perfil.Nome} foi removido.";
        }
    }
}
