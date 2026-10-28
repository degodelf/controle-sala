using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
        TextoCreditos.Text = Creditos.LinhaComVersao(Versoes.Atual);
        MostrarProfessor();
        MostrarSala();
        AtualizarAgendaLimpeza();
        Grade.ItemsSource = _tiles;
        ReconstruirGrade();
        CompositionTarget.Rendering += AoRenderizar;
        if (!App.Capturando)   // na captura de telas não busca atualização nem mexe no marcador/pendentes
        {
            IniciarAutoAtualizacao();
            MostrarConclusaoAtualizacao();
            _ = VerificarAtualizacaoAsync();
        }
    }

    AtualizacaoDisponivel? _novaVersao;
    bool _avisouVersaoNova;

    // --- Atualização online (aviso central, auto-update dos alunos, aviso de "concluído") ---

    AutoAtualizadorAlunos? _auto;
    DispatcherTimer? _relogioAuto;
    DispatcherTimer? _relogioAviso;

    void IniciarAutoAtualizacao()
    {
        _auto = new AutoAtualizadorAlunos(() => Conexoes, AvisoAtualizacao);
        _ = _auto.RetomarAsync();   // retoma uma atualização pendente (PCs que estavam desligados)
        _relogioAuto = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _relogioAuto.Tick += (_, _) => _auto?.Verificar();
        _relogioAuto.Start();
    }

    /// <summary>Ao reabrir depois de instalar a atualização deste PC: confirma na barra de status.</summary>
    void MostrarConclusaoAtualizacao()
    {
        if (Atualizacoes.ConsumirMarcador() is { } alvo && alvo == Versoes.Atual)
            AvisoAtualizacao($"ControleSala atualizado para a versão {alvo} com sucesso ✓");
    }

    /// <summary>Mostra um aviso curto na barra de status (some sozinho depois de alguns segundos).</summary>
    void AvisoAtualizacao(string texto)
    {
        TextoAviso.Text = texto;
        TextoAviso.Visibility = Visibility.Visible;
        _relogioAviso?.Stop();
        _relogioAviso = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _relogioAviso.Tick += (_, _) =>
        {
            _relogioAviso?.Stop();
            TextoAviso.Visibility = Visibility.Collapsed;
        };
        _relogioAviso.Start();
    }

    /// <summary>Checa o GitHub, sem travar nada, e mostra o aviso se houver versão nova.</summary>
    async Task VerificarAtualizacaoAsync()
    {
        var nova = await VerificadorAtualizacao.VerificarAsync(Versoes.Atual);
        if (nova == null) return;
        _novaVersao = nova;
        TextoBanner.Text = $"Nova versão disponível: {nova.Versao} (você tem a {Versoes.Atual}).";
        BannerAtualizacao.Visibility = Visibility.Visible;
        if (!_avisouVersaoNova)   // aviso no meio da tela, só uma vez por abertura do programa
        {
            _avisouVersaoNova = true;
            AbrirJanelaNovaVersao(nova);
        }
    }

    void AbrirJanelaNovaVersao(AtualizacaoDisponivel nova)
        => new JanelaNovaVersao(nova, Conexoes, _auto) { Owner = this }.ShowDialog();

    /// <summary>Só para a ferramenta de captura: força o aviso de versão nova.</summary>
    internal void MostrarAvisoVersaoParaTeste(AtualizacaoDisponivel nova)
    {
        _novaVersao = nova;
        TextoBanner.Text = $"Nova versão disponível: {nova.Versao} (você tem a {Versoes.Atual}).";
        BannerAtualizacao.Visibility = Visibility.Visible;
    }

    /// <summary>Só para a ferramenta de captura: marca os primeiros computadores, para conferir o visual da seleção.</summary>
    internal void SelecionarParaTeste(int quantidade)
    {
        for (int i = 0; i < _tiles.Count && i < quantidade; i++) _tiles[i].Selecionado = true;
        AtualizarContagemSelecao();
    }

    void BannerAtualizacao_Click(object sender, MouseButtonEventArgs e)
    {
        if (_novaVersao is { } nova) AbrirJanelaNovaVersao(nova);
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
        => new JanelaEnviarArquivos(AlvosArquivos()) { Owner = this }.ShowDialog();

    List<ConexaoComputador> Conexoes => _tiles.Select(t => t.Conexao).ToList();

    void AtualizarAlunos_Click(object sender, RoutedEventArgs e)
        => new JanelaAtualizarAlunos(Conexoes) { Owner = this }.ShowDialog();

    void TextoDesatualizados_Click(object sender, MouseButtonEventArgs e) => AtualizarAlunos_Click(sender, e);

    void ComandoLink_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.AbrirLink,
            "Abrir link nos computadores", "Endereço do site (ex.: www.escola.edu.br)",
            texto => Links.Validar(texto, out var url) is { } erro ? (false, erro) : (true, url), preMarcar: PreMarca()) { Owner = this }.ShowDialog();

    void ComandoMensagem_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.Mensagem,
            "Enviar mensagem aos alunos", "Mensagem que aparece na tela do aluno",
            texto => string.IsNullOrWhiteSpace(texto) ? (false, "Digite a mensagem.") : (true, texto.Trim()), preMarcar: PreMarca()) { Owner = this }.ShowDialog();

    void ComandoBloquear_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.BloquearTela,
            "Bloquear a tela dos alunos", "Recado na tela (opcional — padrão: \"Atenção ao professor\")",
            texto => (true, texto.Trim()), preMarcar: PreMarca()) { Owner = this }.ShowDialog();

    void ComandoDesbloquear_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.DesbloquearTela,
            "Desbloquear a tela dos alunos", null, null, preMarcar: PreMarca()) { Owner = this }.ShowDialog();

    void ComandoAbrirPrograma_Click(object sender, RoutedEventArgs e)
    {
        var lista = Programas.Carregar();
        if (lista.Count == 0) { MessageBox.Show(this, "Cadastre ao menos um programa em \"Programas permitidos...\".", "Abrir programa", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var opcoes = lista.Select(p => new JanelaComando.Opcao(p.Nome, p.Comando)).ToList();
        new JanelaComando(Conexoes, AcaoComando.AbrirPrograma, "Abrir programa nos computadores",
            "Programa", _ => (true, ""), opcoes, PreMarca()) { Owner = this }.ShowDialog();
    }

    void ComandoFecharPrograma_Click(object sender, RoutedEventArgs e)
    {
        var lista = Programas.Carregar();
        if (lista.Count == 0) { MessageBox.Show(this, "Cadastre ao menos um programa em \"Programas permitidos...\".", "Fechar programa", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var opcoes = lista.Select(p => new JanelaComando.Opcao(p.Nome, p.NomeProcesso)).ToList();
        new JanelaComando(Conexoes, AcaoComando.FecharPrograma, "Fechar programa nos computadores",
            "Programa", _ => (true, ""), opcoes, PreMarca()) { Owner = this }.ShowDialog();
    }

    void GerenciarProgramas_Click(object sender, RoutedEventArgs e)
        => new JanelaProgramas { Owner = this }.ShowDialog();

    void ComandoDesligar_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.Desligar, "Desligar os computadores", null, null, preMarcar: PreMarca())
        { Owner = this, ConfirmacaoAntesDeEnviar = "Desligar os computadores selecionados? Os alunos verão um aviso de 20 segundos antes." }.ShowDialog();

    void ComandoReiniciar_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.Reiniciar, "Reiniciar os computadores", null, null, preMarcar: PreMarca())
        { Owner = this, ConfirmacaoAntesDeEnviar = "Reiniciar os computadores selecionados? Os alunos verão um aviso de 20 segundos antes." }.ShowDialog();

    void ComandoLogoff_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.Logoff, "Encerrar a sessão dos alunos (logoff)", null, null, preMarcar: PreMarca())
        { Owner = this, ConfirmacaoAntesDeEnviar = "Encerrar a sessão dos computadores selecionados? Os alunos verão um aviso de 20 segundos antes." }.ShowDialog();

    void ComandoSilenciar_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.Silenciar, "Silenciar o som dos alunos", null, _ => (true, "1"), preMarcar: PreMarca()) { Owner = this }.ShowDialog();

    void ComandoReativarSom_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.Silenciar, "Reativar o som dos alunos", null, _ => (true, "0"), preMarcar: PreMarca()) { Owner = this }.ShowDialog();

    void WakeOnLan_Click(object sender, RoutedEventArgs e)
        => new JanelaWakeOnLan(_sala) { Owner = this }.ShowDialog();

    void BloquearInternet_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.BloquearInternet, "Bloquear TODA a internet", null, _ => (true, ""), preMarcar: PreMarca())
        { Owner = this, ConfirmacaoAntesDeEnviar = "Bloquear toda a internet nos computadores selecionados? A rede local (e o ControleSala) continua funcionando. Use \"Liberar a internet\" para desfazer." }.ShowDialog();

    void BloquearSites_Click(object sender, RoutedEventArgs e)
    {
        var sites = Sites.Carregar();
        if (sites.Count == 0) { MessageBox.Show(this, "Cadastre os sites em \"Sites bloqueados...\" primeiro.", "Bloquear sites", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        new JanelaComando(Conexoes, AcaoComando.BloquearInternet, $"Bloquear {sites.Count} site(s)", null, _ => (true, string.Join(",", sites)), preMarcar: PreMarca())
        { Owner = this }.ShowDialog();
    }

    void LiberarInternet_Click(object sender, RoutedEventArgs e)
        => new JanelaComando(Conexoes, AcaoComando.LiberarInternet, "Liberar a internet", null, null, preMarcar: PreMarca()) { Owner = this }.ShowDialog();

    void GerenciarSites_Click(object sender, RoutedEventArgs e)
        => new JanelaSites { Owner = this }.ShowDialog();

    void Limpeza_Click(object sender, RoutedEventArgs e)
    {
        var janela = new JanelaLimpeza { Owner = this };
        janela.ShowDialog();
        if (janela.Salvou)
            foreach (var t in _tiles.Where(t => t.Conexao.Estado == EstadoConexao.Online))
                t.Conexao.ReenviarConfigLimpeza();
        AtualizarAgendaLimpeza();
    }

    void TextoLimpeza_Click(object sender, MouseButtonEventArgs e) => Limpeza_Click(sender, e);

    /// <summary>Mostra na barra de status quando a limpeza automática roda (ou "desligada"), sempre visível.</summary>
    void AtualizarAgendaLimpeza()
    {
        var c = LimpezaConfig.Carregar();
        bool ligada = c.Agendada && c.Dias.Count > 0;
        TextoLimpeza.Text = ligada ? $"🧹 Limpeza: {c.ResumoAgenda()}" : "🧹 Limpeza: desligada";
        TextoLimpeza.Foreground = (Brush)FindResource(ligada ? "PincelTexto" : "PincelTextoFraco");
    }

    void LimparAgora_Click(object sender, RoutedEventArgs e)
    {
        var config = LimpezaConfig.Carregar().Serializar();
        new JanelaComando(Conexoes, AcaoComando.Limpeza, "Limpar agora", null, _ => (true, config), preMarcar: PreMarca())
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

    // --- Seleção de computadores na grade (clique, Ctrl+clique, Shift+clique, Ctrl+A, Esc e arrasto do mouse) ---

    int _ancora = -1; // índice do último clicado, base do Shift+clique

    Point _inicioArrasto;                 // onde o arrasto começou (coordenadas de AreaGrade)
    bool _candidatoArrasto;               // botão esquerdo desceu numa área vazia
    bool _arrastando;                     // já passou do limiar e está desenhando a caixa
    bool _aditivoArrasto;                 // Ctrl segurado ao começar: soma à seleção que já existia
    readonly HashSet<TileVm> _selecaoBase = []; // seleção no instante em que o arrasto começou

    void Tile_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not TileVm tile) return;
        if (e.ClickCount == 2) { AbrirTela(tile); return; }

        int idx = _tiles.IndexOf(tile);
        var mod = Keyboard.Modifiers;
        if ((mod & ModifierKeys.Shift) != 0 && _ancora >= 0 && _ancora < _tiles.Count)
        {
            if ((mod & ModifierKeys.Control) == 0)
                foreach (var t in _tiles) t.Selecionado = false;
            int a = Math.Min(_ancora, idx), b = Math.Max(_ancora, idx);
            for (int i = a; i <= b; i++) _tiles[i].Selecionado = true;
        }
        else if ((mod & ModifierKeys.Control) != 0)
        {
            tile.Selecionado = !tile.Selecionado;
            _ancora = idx;
        }
        else
        {
            foreach (var t in _tiles) t.Selecionado = (t == tile);
            _ancora = idx;
        }
        AtualizarContagemSelecao();
    }

    void Tile_MouseRightDown(object sender, MouseButtonEventArgs e)
    {
        // Botão direito num computador que não está marcado: seleciona só ele antes de abrir o menu.
        if (((FrameworkElement)sender).DataContext is not TileVm tile || tile.Selecionado) return;
        foreach (var t in _tiles) t.Selecionado = (t == tile);
        _ancora = _tiles.IndexOf(tile);
        AtualizarContagemSelecao();
    }

    void Fundo_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Botão esquerdo numa área vazia (fora dos computadores): começa a seleção por arrasto.
        // Sem arrastar, vira um clique simples que limpa a seleção (ao soltar).
        if (AcharTile(e.OriginalSource as DependencyObject) != null) return;

        _aditivoArrasto = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        _selecaoBase.Clear();
        if (_aditivoArrasto)
            foreach (var t in _tiles.Where(t => t.Selecionado)) _selecaoBase.Add(t);
        else
            foreach (var t in _tiles) t.Selecionado = false;

        _inicioArrasto = e.GetPosition(AreaGrade);
        _candidatoArrasto = true;
        _arrastando = false;
        AreaGrade.CaptureMouse();
        AtualizarContagemSelecao();
    }

    void Area_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_candidatoArrasto || e.LeftButton != MouseButtonState.Pressed) return;

        var p = e.GetPosition(AreaGrade);
        p.X = Math.Max(0, Math.Min(p.X, AreaGrade.ActualWidth));
        p.Y = Math.Max(0, Math.Min(p.Y, AreaGrade.ActualHeight));

        if (!_arrastando)
        {
            // Só desenha depois de um pequeno movimento, para não confundir com um clique.
            if (Math.Abs(p.X - _inicioArrasto.X) < 4 && Math.Abs(p.Y - _inicioArrasto.Y) < 4) return;
            _arrastando = true;
            CaixaSelecao.Visibility = Visibility.Visible;
        }

        double x = Math.Min(p.X, _inicioArrasto.X), y = Math.Min(p.Y, _inicioArrasto.Y);
        double w = Math.Abs(p.X - _inicioArrasto.X), h = Math.Abs(p.Y - _inicioArrasto.Y);
        System.Windows.Controls.Canvas.SetLeft(CaixaSelecao, x);
        System.Windows.Controls.Canvas.SetTop(CaixaSelecao, y);
        CaixaSelecao.Width = w;
        CaixaSelecao.Height = h;

        var caixa = new Rect(x, y, w, h);
        foreach (var t in _tiles)
        {
            var r = RetanguloDoTile(t);
            bool dentro = r.HasValue && caixa.IntersectsWith(r.Value);
            t.Selecionado = dentro || (_aditivoArrasto && _selecaoBase.Contains(t));
        }
        AtualizarContagemSelecao();
    }

    void Area_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_candidatoArrasto) return;
        _candidatoArrasto = false;
        AreaGrade.ReleaseMouseCapture();
        if (_arrastando)
        {
            _arrastando = false;
            CaixaSelecao.Visibility = Visibility.Collapsed;
            _ancora = PrimeiroSelecionado(); // base para um Shift+clique seguinte
        }
        _selecaoBase.Clear();
        AtualizarContagemSelecao();
    }

    int PrimeiroSelecionado()
    {
        for (int i = 0; i < _tiles.Count; i++)
            if (_tiles[i].Selecionado) return i;
        return -1;
    }

    /// <summary>Retângulo que o computador ocupa na tela, em coordenadas de AreaGrade (ou null se ainda não há caixa).</summary>
    Rect? RetanguloDoTile(TileVm tile)
    {
        if (Grade.ItemContainerGenerator.ContainerFromItem(tile) is not FrameworkElement c) return null;
        if (c.ActualWidth <= 0 || c.ActualHeight <= 0) return null;
        try
        {
            return c.TransformToVisual(AreaGrade).TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight));
        }
        catch { return null; }
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            foreach (var t in _tiles) t.Selecionado = true;
            _ancora = _tiles.Count > 0 ? 0 : -1;
            AtualizarContagemSelecao();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            foreach (var t in _tiles) t.Selecionado = false;
            AtualizarContagemSelecao();
        }
    }

    static TileVm? AcharTile(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is FrameworkElement fe && fe.DataContext is TileVm t) return t;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    void AtualizarContagemSelecao()
    {
        int n = _tiles.Count(t => t.Selecionado);
        TextoSelecao.Text = n == 0 ? "" : $"{n} selecionado{(n == 1 ? "" : "s")}";
        TextoSelecao.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Computadores marcados na grade (ou null se nenhum está marcado — aí o comando vale para todos).</summary>
    Func<ConexaoComputador, bool>? PreMarca()
    {
        var marcados = _tiles.Where(t => t.Selecionado).Select(t => t.Conexao).ToHashSet();
        return marcados.Count == 0 ? null : marcados.Contains;
    }

    /// <summary>Conexões para a janela de enviar arquivos: só as marcadas, ou todas se nada estiver marcado.</summary>
    List<ConexaoComputador> AlvosArquivos()
    {
        var marcados = _tiles.Where(t => t.Selecionado).Select(t => t.Conexao).ToList();
        return marcados.Count > 0 ? marcados : Conexoes;
    }

    void TileVerTela_Click(object sender, RoutedEventArgs e)
    {
        // O menu herda o contexto do computador em que foi aberto.
        if ((sender as FrameworkElement)?.DataContext is TileVm tile) AbrirTela(tile);
    }

    void TileReconectar_Click(object sender, RoutedEventArgs e)
    {
        var alvos = _tiles.Where(t => t.Selecionado).ToList();
        if (alvos.Count == 0 && (sender as FrameworkElement)?.DataContext is TileVm tile) alvos.Add(tile);
        foreach (var t in alvos) t.Conexao.Reconectar();
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
