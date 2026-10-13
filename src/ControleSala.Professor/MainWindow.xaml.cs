using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ControleSala.Comum;

namespace ControleSala.Professor;

public partial class MainWindow : Window
{
    readonly RepositorioSala _sala = new();
    readonly ObservableCollection<TileVm> _tiles = [];
    readonly Dictionary<ConexaoComputador, JanelaTelaCheia> _telasAbertas = [];
    string _ultimoTextoEstado = "";

    public MainWindow()
    {
        InitializeComponent();
        TextoCreditos.Text = Creditos.Linha;
        MostrarProfessor();
        MostrarSala();
        Grade.ItemsSource = _tiles;
        ReconstruirGrade();
        CompositionTarget.Rendering += AoRenderizar;
        _ = VerificarAtualizacaoAsync();
    }

    AtualizacaoDisponivel? _novaVersao;

    /// <summary>Checa o GitHub, sem travar nada, e mostra o aviso se houver versão nova.</summary>
    async Task VerificarAtualizacaoAsync()
    {
        var nova = await VerificadorAtualizacao.VerificarAsync(Versoes.Atual);
        if (nova == null) return;
        _novaVersao = nova;
        TextoBanner.Text = $"Nova versão disponível: {nova.Versao} (você tem a {Versoes.Atual}).";
        BannerAtualizacao.Visibility = Visibility.Visible;
    }

    /// <summary>Só para a ferramenta de captura: força o aviso de versão nova.</summary>
    internal void MostrarAvisoVersaoParaTeste(AtualizacaoDisponivel nova)
    {
        _novaVersao = nova;
        TextoBanner.Text = $"Nova versão disponível: {nova.Versao} (você tem a {Versoes.Atual}).";
        BannerAtualizacao.Visibility = Visibility.Visible;
    }

    void BannerAtualizacao_Click(object sender, MouseButtonEventArgs e)
    {
        if (_novaVersao is { } nova)
            new JanelaNovaVersao(nova, Conexoes) { Owner = this }.ShowDialog();
    }

    void DispensarBanner_Click(object sender, RoutedEventArgs e)
    {
        BannerAtualizacao.Visibility = Visibility.Collapsed;
        e.Handled = true; // não deixa o clique abrir a janela também
    }

    /// <summary>Faz a grade refletir a lista da sala: conexões novas para quem chegou, fecha as de quem saiu.</summary>
    void ReconstruirGrade()
    {
        var porDados = _tiles.ToDictionary(t => t.Conexao.Dados);
        var novos = new List<TileVm>();
        foreach (var c in _sala.Computadores)
        {
            if (porDados.Remove(c, out var existente))
                novos.Add(existente);
            else
                novos.Add(new TileVm(new ConexaoComputador(c, Sessao.Sala?.Chave)) { Largura = DeslizeTamanho.Value });
        }
        foreach (var sobrou in porDados.Values)
        {
            if (_telasAbertas.TryGetValue(sobrou.Conexao, out var tela)) tela.Close();
            sobrou.Conexao.Dispose();
        }
        _tiles.Clear();
        foreach (var t in novos) _tiles.Add(t);
        TextoVazio.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TextoVazio.Text = Sessao.Sala == null
            ? "A sala ainda não foi criada.\nClique em \"Sala...\" para criar a chave e gerar o pacote dos alunos."
            : "Nenhum computador na sala ainda.\nClique em \"Computadores...\" para adicionar ou procurar na rede.";
    }

    /// <summary>Abre a janela da sala e, se a chave mudou, reconecta tudo com a chave nova.</summary>
    public void AbrirSala()
    {
        string? antes = Sessao.Sala?.Impressao();
        new JanelaSala { Owner = this }.ShowDialog();
        MostrarSala();
        if (Sessao.Sala?.Impressao() == antes) return;
        foreach (var t in _tiles) { if (_telasAbertas.TryGetValue(t.Conexao, out var tela)) tela.Close(); t.Conexao.Dispose(); }
        _tiles.Clear();
        ReconstruirGrade();
    }

    void MostrarSala() =>
        TextoSala.Text = Sessao.Sala is { } s ? $"{s.Sala} — {s.Escola}" : "Sala não criada";

    void BotaoSala_Click(object sender, RoutedEventArgs e) => AbrirSala();

    void MenuSair_Click(object sender, RoutedEventArgs e) => Close();

    void MenuProcurar_Click(object sender, RoutedEventArgs e)
    {
        new JanelaComputadores(_sala, abrirProcurar: true) { Owner = this }.ShowDialog();
        ReconstruirGrade();
    }

    void BotaoTema_Click(object sender, RoutedEventArgs e) => Tema.Alternar();

    void BotaoEnviarArquivos_Click(object sender, RoutedEventArgs e)
        => new JanelaEnviarArquivos(_tiles.Select(t => t.Conexao).ToList()) { Owner = this }.ShowDialog();

    List<ConexaoComputador> Conexoes => _tiles.Select(t => t.Conexao).ToList();

    void AtualizarAlunos_Click(object sender, RoutedEventArgs e)
        => new JanelaAtualizarAlunos(Conexoes) { Owner = this }.ShowDialog();

    void TextoDesatualizados_Click(object sender, MouseButtonEventArgs e) => AtualizarAlunos_Click(sender, e);

    void ComandoLink_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.AbrirLink,
            "Abrir link nos computadores", "Endereço do site (ex.: www.escola.edu.br)",
            texto => Links.Validar(texto, out var url) is { } erro ? (false, erro) : (true, url)) { Owner = this }.ShowDialog();

    void ComandoMensagem_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.Mensagem,
            "Enviar mensagem aos alunos", "Mensagem que aparece na tela do aluno",
            texto => string.IsNullOrWhiteSpace(texto) ? (false, "Digite a mensagem.") : (true, texto.Trim())) { Owner = this }.ShowDialog();

    void ComandoBloquear_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.BloquearTela,
            "Bloquear a tela dos alunos", "Recado na tela (opcional — padrão: \"Atenção ao professor\")",
            texto => (true, texto.Trim())) { Owner = this }.ShowDialog();

    void ComandoDesbloquear_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.DesbloquearTela,
            "Desbloquear a tela dos alunos", null, null) { Owner = this }.ShowDialog();

    void ComandoAbrirPrograma_Click(object sender, RoutedEventArgs e)
    {
        var lista = Programas.Carregar();
        if (lista.Count == 0) { MessageBox.Show(this, "Cadastre ao menos um programa em \"Programas permitidos...\".", "Abrir programa", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var opcoes = lista.Select(p => new JanelaComando.Opcao(p.Nome, p.Comando)).ToList();
        new JanelaComando(Conexoes, AcaoComando.AbrirPrograma, "Abrir programa nos computadores",
            "Programa", _ => (true, ""), opcoes) { Owner = this }.ShowDialog();
    }

    void ComandoFecharPrograma_Click(object sender, RoutedEventArgs e)
    {
        var lista = Programas.Carregar();
        if (lista.Count == 0) { MessageBox.Show(this, "Cadastre ao menos um programa em \"Programas permitidos...\".", "Fechar programa", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var opcoes = lista.Select(p => new JanelaComando.Opcao(p.Nome, p.NomeProcesso)).ToList();
        new JanelaComando(Conexoes, AcaoComando.FecharPrograma, "Fechar programa nos computadores",
            "Programa", _ => (true, ""), opcoes) { Owner = this }.ShowDialog();
    }

    void GerenciarProgramas_Click(object sender, RoutedEventArgs e)
        => new JanelaProgramas { Owner = this }.ShowDialog();

    void ComandoDesligar_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.Desligar, "Desligar os computadores", null, null)
        { Owner = this, ConfirmacaoAntesDeEnviar = "Desligar os computadores selecionados? Os alunos verão um aviso de 20 segundos antes." }.ShowDialog();

    void ComandoReiniciar_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.Reiniciar, "Reiniciar os computadores", null, null)
        { Owner = this, ConfirmacaoAntesDeEnviar = "Reiniciar os computadores selecionados? Os alunos verão um aviso de 20 segundos antes." }.ShowDialog();

    void ComandoLogoff_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.Logoff, "Encerrar a sessão dos alunos (logoff)", null, null)
        { Owner = this, ConfirmacaoAntesDeEnviar = "Encerrar a sessão dos computadores selecionados? Os alunos verão um aviso de 20 segundos antes." }.ShowDialog();

    void ComandoSilenciar_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.Silenciar, "Silenciar o som dos alunos", null, _ => (true, "1")) { Owner = this }.ShowDialog();

    void ComandoReativarSom_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.Silenciar, "Reativar o som dos alunos", null, _ => (true, "0")) { Owner = this }.ShowDialog();

    void WakeOnLan_Click(object sender, RoutedEventArgs e)
        => new JanelaWakeOnLan(_sala) { Owner = this }.ShowDialog();

    void BloquearInternet_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.BloquearInternet, "Bloquear TODA a internet", null, _ => (true, ""))
        { Owner = this, ConfirmacaoAntesDeEnviar = "Bloquear toda a internet nos computadores selecionados? A rede local (e o ControleSala) continua funcionando. Use \"Liberar a internet\" para desfazer." }.ShowDialog();

    void BloquearSites_Click(object sender, RoutedEventArgs e)
    {
        var sites = Sites.Carregar();
        if (sites.Count == 0) { MessageBox.Show(this, "Cadastre os sites em \"Sites bloqueados...\" primeiro.", "Bloquear sites", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        new JanelaComando(Conexoes, AcaoComando.BloquearInternet, $"Bloquear {sites.Count} site(s)", null, _ => (true, string.Join(",", sites)))
        { Owner = this }.ShowDialog();
    }

    void LiberarInternet_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.LiberarInternet, "Liberar a internet", null, null) { Owner = this }.ShowDialog();

    void GerenciarSites_Click(object sender, RoutedEventArgs e)
        => new JanelaSites { Owner = this }.ShowDialog();

    void Limpeza_Click(object sender, RoutedEventArgs e)
    {
        var janela = new JanelaLimpeza { Owner = this };
        janela.ShowDialog();
        if (janela.Salvou)
            foreach (var t in _tiles.Where(t => t.Conexao.Estado == EstadoConexao.Online))
                t.Conexao.ReenviarConfigLimpeza();
    }

    void LimparAgora_Click(object sender, RoutedEventArgs e)
    {
        var config = LimpezaConfig.Carregar().Serializar();
        new JanelaComando(Conexoes, AcaoComando.Limpeza, "Limpar agora", null, _ => (true, config))
        { Owner = this, ConfirmacaoAntesDeEnviar = "Limpar agora os computadores selecionados? Serão apagados os arquivos marcados na configuração da limpeza (a conta de administrador não é tocada)." }.ShowDialog();
    }

    void AoRenderizar(object? sender, EventArgs e)
    {
        int online = 0;
        foreach (var t in _tiles)
        {
            // Com a tela aberta em tamanho grande, quem desenha é a janela dela.
            if (t.Conexao.Modo == ModoVideo.Miniatura)
                t.AtualizarImagem();
            t.AtualizarEstado();
            if (t.Conexao.Estado == EstadoConexao.Online) online++;
            if (t.Conexao.Mac is { } mac) _sala.DefinirMac(t.Conexao.Dados, mac); // guarda o MAC assim que chega (para Wake-on-LAN)
        }
        string texto = _tiles.Count == 0 ? "" : $"{online} de {_tiles.Count} online";
        if (texto != _ultimoTextoEstado)
            TextoEstado.Text = _ultimoTextoEstado = texto;

        // Alunos com versão mais antiga que a deste programa: avisa e leva à janela de atualização.
        int antigos = _tiles.Count(t => Atualizacoes.Desatualizado(t.Conexao, Versoes.Atual));
        string aviso = antigos == 0 ? "" : $"⚠ {antigos} computador{(antigos == 1 ? "" : "es")} com versão antiga — Atualizar";
        if (aviso != TextoDesatualizados.Text)
        {
            TextoDesatualizados.Text = aviso;
            TextoDesatualizados.Visibility = antigos == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    void Tamanho_Alterado(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_tiles == null) return; // evento disparado durante o InitializeComponent
        foreach (var t in _tiles) t.Largura = e.NewValue;
    }

    void Tile_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && ((FrameworkElement)sender).DataContext is TileVm tile)
            AbrirTela(tile);
    }

    void TileVerTela_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TileVm tile) AbrirTela(tile);
    }

    void TileReconectar_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TileVm tile) tile.Conexao.Reconectar();
    }

    void AbrirTela(TileVm tile)
    {
        if (_telasAbertas.TryGetValue(tile.Conexao, out var aberta))
        {
            aberta.Activate();
            return;
        }
        var tela = new JanelaTelaCheia(tile.Conexao) { Owner = this };
        _telasAbertas[tile.Conexao] = tela;
        tela.Closed += (_, _) => _telasAbertas.Remove(tile.Conexao);
        tela.Show();
    }

    void BotaoComputadores_Click(object sender, RoutedEventArgs e)
    {
        new JanelaComputadores(_sala) { Owner = this }.ShowDialog();
        ReconstruirGrade();
    }

    void MostrarProfessor() =>
        TextoProfessor.Text = Sessao.Atual is { } p ? $"Professor: {p.Nome}" : "";

    void BotaoTrocar_Click(object sender, RoutedEventArgs e)
    {
        var entrar = new JanelaEntrar(Sessao.Repositorio) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (entrar.ShowDialog() == true && entrar.Escolhido != null)
        {
            Sessao.Iniciar(Sessao.Repositorio, entrar.Escolhido);
            MostrarProfessor();
        }
    }

    void BotaoProfessores_Click(object sender, RoutedEventArgs e)
    {
        if (Sessao.Atual != null)
            new JanelaProfessores(Sessao.Repositorio, Sessao.Atual) { Owner = this }.ShowDialog();
    }

    void BotaoSobre_Click(object sender, RoutedEventArgs e) => new JanelaSobre { Owner = this }.ShowDialog();

    void BotaoPapelDeParede_Click(object sender, RoutedEventArgs e)
        => new JanelaPapelDeParede { Owner = this }.ShowDialog();

    void Window_Closed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= AoRenderizar;
        foreach (var t in _tiles) t.Conexao.Dispose();
        _tiles.Clear();
    }
}
