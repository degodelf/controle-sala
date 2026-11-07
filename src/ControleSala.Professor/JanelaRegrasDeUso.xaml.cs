using System.Windows;

namespace ControleSala.Professor;

/// <summary>As regras que o professor aceita ao entrar pela primeira vez. Depois ficam em Ajuda, só para consulta.</summary>
public partial class JanelaRegrasDeUso : Window
{
    static readonly string[] Regras =
    [
        "Usar só para a aula: acompanhar, ajudar e manter a turma na atividade.",
        "Não fotografar nem gravar as telas, e não mostrar a tela de um aluno a outras pessoas sem necessidade pedagógica.",
        "Se aparecer algo pessoal (conversa, conta, dado de família), sair daquela tela e não comentar com a turma.",
        "Se vir algo que indique risco a um aluno, avisar a direção da escola.",
        "Avisar a turma antes de limpar: a limpeza apaga arquivos.",
        "Fechar o programa ao sair da sala.",
    ];

    public JanelaRegrasDeUso(bool pedirAceite)
    {
        InitializeComponent();
        ListaRegras.ItemsSource = Regras;
        if (pedirAceite) return;
        BotaoAceitar.Visibility = Visibility.Collapsed;
        BotaoSair.Content = "Fechar";
    }

    void Aceitar_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
