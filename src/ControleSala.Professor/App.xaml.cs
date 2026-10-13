using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ControleSala.Comum;

namespace ControleSala.Professor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            FFmpegNativo.Inicializar();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível carregar o FFmpeg:\n{ex.Message}", "ControleSala", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        int d = Array.IndexOf(e.Args, "--dados");
        if (d >= 0 && d + 1 < e.Args.Length)
            Pastas.Dados = e.Args[d + 1];

        // --tema claro|escuro força o tema (usado na captura das janelas); sem isso, usa a preferência salva.
        int t = Array.IndexOf(e.Args, "--tema");
        if (t >= 0 && t + 1 < e.Args.Length)
            Tema.Aplicar(e.Args[t + 1].Equals("claro", StringComparison.OrdinalIgnoreCase) ? ModoTema.Claro : ModoTema.Escuro, salvar: false);
        else
            Tema.Iniciar();

        // Só para conferir o visual sem fotografar a tela: --capturar <janela> <saida.png>
        int k = Array.IndexOf(e.Args, "--capturar");
        if (k >= 0 && k + 2 < e.Args.Length)
        {
            int w = Array.IndexOf(e.Args, "--esperar");
            double esperar = w >= 0 && w + 1 < e.Args.Length ? double.Parse(e.Args[w + 1], System.Globalization.CultureInfo.InvariantCulture) : 0;
            CapturarJanela(e.Args[k + 1], e.Args[k + 2], esperar);
            Shutdown(0);
            return;
        }

        // Sem janela: cria a sala (se ainda não existe) e grava o pacote para os alunos.
        // --gerar-pacote <saida.controlesala> --senha <s> [--escola <e> --sala <s>]
        int g = Array.IndexOf(e.Args, "--gerar-pacote");
        if (g >= 0 && g + 1 < e.Args.Length)
        {
            string Arg(string nome) { int j = Array.IndexOf(e.Args, nome); return j >= 0 && j + 1 < e.Args.Length ? e.Args[j + 1] : ""; }
            var salaExistente = ConfigProfessor.Carregar();
            if (salaExistente == null)
            {
                if (Arg("--escola").Length == 0 || Arg("--sala").Length == 0) { Shutdown(2); return; }
                salaExistente = ConfigSala.Nova(Arg("--escola"), Arg("--sala"));
                ConfigProfessor.Salvar(salaExistente);
            }
            try
            {
                File.WriteAllBytes(e.Args[g + 1], PacoteSala.Exportar(salaExistente, Arg("--senha")));
                Shutdown(0);
            }
            catch (ArgumentException)
            {
                Shutdown(3); // senha curta demais
            }
            return;
        }

        // Sem janela: envia arquivos para um computador. Serve para testes e para automatizar.
        // --enviar <arquivo>... --para <host:porta> [--resultado <arquivo>] [--cancelar-apos-ms <n>]
        int en = Array.IndexOf(e.Args, "--enviar");
        if (en >= 0)
        {
            Shutdown(EnviarPelaLinhaDeComando(e.Args, en));
            return;
        }

        // Sem janela: atualiza um computador. --atualizar <Setup.exe> --para <host:porta> [--resultado <arquivo>] [--esperar-versao <s>]
        int at = Array.IndexOf(e.Args, "--atualizar");
        if (at >= 0)
        {
            Shutdown(AtualizarPelaLinhaDeComando(e.Args, at));
            return;
        }

        // Sem janela: manda um comando a um computador. --comando <acao> [argumento] --para <host:porta>
        int cm = Array.IndexOf(e.Args, "--comando");
        if (cm >= 0)
        {
            Shutdown(ComandoPelaLinhaDeComando(e.Args, cm));
            return;
        }

        int i = Array.IndexOf(e.Args, "--gerar-papel-de-parede");
        if (i >= 0 && i + 1 < e.Args.Length)
        {
            int c = Array.IndexOf(e.Args, "--config");
            var config = ConfigPapelDeParede.Carregar(c >= 0 && c + 1 < e.Args.Length ? e.Args[c + 1] : null);
            var fundo = string.IsNullOrEmpty(config.Fundo) ? null : PapelDeParede.CarregarImagem(config.Fundo);
            var logo = string.IsNullOrEmpty(config.Logo) ? null : PapelDeParede.CarregarImagem(config.Logo);
            PapelDeParede.SalvarPng(PapelDeParede.Compor(config, fundo, logo), e.Args[i + 1]);
            Shutdown(0);
            return;
        }
        base.OnStartup(e);

        // Enquanto as telas de entrada estão abertas não existe janela principal: não encerrar sozinho.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var repositorio = new RepositorioPerfis();
        PerfilProfessor? professor;
        if (repositorio.Perfis.Count == 0)
        {
            var primeiro = new JanelaNovoProfessor(repositorio, ModoPerfil.PrimeiroAcesso);
            primeiro.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            professor = primeiro.ShowDialog() == true ? primeiro.Resultado : null;
        }
        else
        {
            var entrar = new JanelaEntrar(repositorio);
            professor = entrar.ShowDialog() == true ? entrar.Escolhido : null;
        }
        if (professor == null)
        {
            Shutdown(0);
            return;
        }

        Sessao.Iniciar(repositorio, professor);
        Sessao.CarregarSala();
        Sessao.AtualizarLimpeza();
        var principal = new MainWindow();
        MainWindow = principal;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        principal.Show();
        if (Sessao.Sala == null)
            principal.AbrirSala(); // primeira vez: criar a sala
    }

    static int ComandoPelaLinhaDeComando(string[] args, int posicao)
    {
        string Valor(string nome) { int j = Array.IndexOf(args, nome); return j >= 0 && j + 1 < args.Length ? args[j + 1] : ""; }
        if (!Enum.TryParse<AcaoComando>(args.ElementAtOrDefault(posicao + 1), ignoreCase: true, out var acao)) return 2;
        string argumento = args.ElementAtOrDefault(posicao + 2) is { } a && !a.StartsWith("--") ? a : "";
        string para = Valor("--para");
        int partes = para.LastIndexOf(':');
        if (partes <= 0 || !int.TryParse(para[(partes + 1)..], out int porta)) return 2;

        try
        {
            Sessao.CarregarSala();
            var computador = new ComputadorSala { Nome = para, Endereco = para[..partes], Porta = porta };
            using var conexao = new ConexaoComputador(computador, Sessao.Sala?.Chave);
            var limite = DateTime.UtcNow.AddSeconds(15);
            while (conexao.Estado != EstadoConexao.Online && DateTime.UtcNow < limite) Thread.Sleep(100);
            if (conexao.Estado != EstadoConexao.Online)
            {
                Console.WriteLine($"ERRO: não conectou a {para}: {conexao.Detalhe}");
                return 1;
            }
            var (ok, msg) = Task.Run(() => conexao.EnviarComandoAsync(acao, argumento, CancellationToken.None)).GetAwaiter().GetResult();
            Console.WriteLine($"{(ok ? "OK" : "ERRO")}: {acao}: {msg}");
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERRO: {ex.Message}");
            return 1;
        }
    }

    static int EnviarPelaLinhaDeComando(string[] args, int posicao)
    {
        string Valor(string nome) { int j = Array.IndexOf(args, nome); return j >= 0 && j + 1 < args.Length ? args[j + 1] : ""; }
        var arquivos = args.Skip(posicao + 1).TakeWhile(a => !a.StartsWith("--")).ToList();
        string para = Valor("--para"), resultado = Valor("--resultado");
        int partes = para.LastIndexOf(':');
        if (arquivos.Count == 0 || partes <= 0 || !int.TryParse(para[(partes + 1)..], out int porta)) return 2;

        var linhas = new List<string>();
        int codigo = 1;
        try
        {
            Sessao.CarregarSala();
            var computador = new ComputadorSala { Nome = para, Endereco = para[..partes], Porta = porta };
            using var conexao = new ConexaoComputador(computador, Sessao.Sala?.Chave);
            var limite = DateTime.UtcNow.AddSeconds(15);
            while (conexao.Estado != EstadoConexao.Online && DateTime.UtcNow < limite) Thread.Sleep(100);
            if (conexao.Estado != EstadoConexao.Online)
            {
                linhas.Add($"ERRO: não conectou a {para}: {conexao.Detalhe}");
            }
            else
            {
                var cancelar = new CancellationTokenSource();
                // Task.Run + GetResult: a thread da interface não pode ficar presa em await (Progress<T> a usaria).
                var res = Task.Run(async () =>
                {
                    var prontos = await EnvioDeArquivos.PrepararAsync(arquivos, CancellationToken.None);
                    // --cancelar-apos-ms conta a partir do começo do envio (como o professor apertando "Cancelar envio").
                    if (int.TryParse(Valor("--cancelar-apos-ms"), out int ms)) cancelar.CancelAfter(ms);
                    return await EnvioDeArquivos.EnviarAsync(prontos, [conexao], null, cancelar.Token);
                }).GetAwaiter().GetResult();
                foreach (var r in res) linhas.Add($"{(r.Ok ? "OK" : "ERRO")}: {r.Arquivo}: {r.Mensagem}");
                codigo = res.Count > 0 && res.All(r => r.Ok) ? 0 : 1;
            }
        }
        catch (ArgumentException ex)
        {
            linhas.Add($"ERRO: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            linhas.Add("ERRO: cancelado");
        }
        if (resultado.Length > 0) File.WriteAllLines(resultado, linhas);
        return codigo;
    }

    static int AtualizarPelaLinhaDeComando(string[] args, int posicao)
    {
        string Valor(string nome) { int j = Array.IndexOf(args, nome); return j >= 0 && j + 1 < args.Length ? args[j + 1] : ""; }
        string setup = args.ElementAtOrDefault(posicao + 1) ?? "", para = Valor("--para"), resultado = Valor("--resultado");
        int partes = para.LastIndexOf(':');
        if (setup.Length == 0 || partes <= 0 || !int.TryParse(para[(partes + 1)..], out int porta)) return 2;

        var linhas = new List<string>();
        int codigo = 1;
        try
        {
            Sessao.CarregarSala();
            var inst = Task.Run(() => Atualizacoes.PrepararAsync(setup, CancellationToken.None)).GetAwaiter().GetResult();
            linhas.Add($"Instalador: versão {inst.Versao}, assinatura conferida.");
            var computador = new ComputadorSala { Nome = para, Endereco = para[..partes], Porta = porta };
            using var conexao = new ConexaoComputador(computador, Sessao.Sala?.Chave);
            var limite = DateTime.UtcNow.AddSeconds(15);
            while ((conexao.Estado != EstadoConexao.Online || conexao.VersaoAgente == null) && DateTime.UtcNow < limite) Thread.Sleep(100);
            if (conexao.Estado != EstadoConexao.Online)
            {
                linhas.Add($"ERRO: não conectou a {para}: {conexao.Detalhe}");
            }
            else
            {
                linhas.Add($"Agente: versão {conexao.VersaoAgente ?? "desconhecida"}.");
                int aceitos = Task.Run(async () =>
                {
                    var p = new Progress<ProgressoAtualizacao>(r => { if (r.Ok != null) lock (linhas) linhas.Add($"{(r.Ok == true ? "OK" : "ERRO")}: {r.Mensagem}"); });
                    int n = await Atualizacoes.EnviarAsync(inst, [conexao], p, CancellationToken.None);
                    await Task.Delay(300); // deixa o Progress entregar a última mensagem
                    return n;
                }).GetAwaiter().GetResult();
                codigo = aceitos == 1 ? 0 : 1;
                // Teste completo (agente instalado como serviço): espera ele voltar com a versão nova.
                if (aceitos == 1 && int.TryParse(Valor("--esperar-versao"), out int segundos))
                {
                    var fim = DateTime.UtcNow.AddSeconds(segundos);
                    while (!(conexao.Estado == EstadoConexao.Online && conexao.VersaoAgente == inst.Versao) && DateTime.UtcNow < fim) Thread.Sleep(500);
                    bool voltou = conexao.VersaoAgente == inst.Versao;
                    linhas.Add(voltou ? $"OK: o agente voltou na versão {inst.Versao}." : $"ERRO: o agente não voltou na versão {inst.Versao} em {segundos} s (está em {conexao.VersaoAgente ?? "?"}).");
                    codigo = voltou ? 0 : 1;
                }
            }
        }
        catch (ArgumentException ex)
        {
            linhas.Add($"ERRO: {ex.Message}");
        }
        if (resultado.Length > 0) File.WriteAllLines(resultado, linhas);
        return codigo;
    }

    static MainWindow MostrarBannerDeTeste(MainWindow w)
    {
        // Só para a captura: mostra a faixa de aviso de versão nova.
        w.Loaded += (_, _) => w.MostrarAvisoVersaoParaTeste(new ControleSala.Comum.AtualizacaoDisponivel("0.4.0", "https://x/Setup.exe", ""));
        return w;
    }

    static void CapturarJanela(string nome, string saida, double esperar)
    {
        // Dados de exemplo (só na pasta indicada por --dados) para as telas terem o que mostrar.
        var repositorio = new RepositorioPerfis(iteracoes: 1000);
        if (repositorio.Perfis.Count == 0)
        {
            repositorio.Adicionar("Diego", "1234");
            repositorio.Adicionar("Maria", "5678");
        }
        Sessao.Iniciar(repositorio, repositorio.Perfis[0]);
        Sessao.CarregarSala();
        Sessao.AtualizarLimpeza();

        var sala = new RepositorioSala();
        ConexaoComputador? conexao = nome == "tela" && sala.Computadores.Count > 0 ? new ConexaoComputador(sala.Computadores[0], Sessao.Sala?.Chave) : null;

        Window janela = nome switch
        {
            "principal" => MostrarBannerDeTeste(new MainWindow()),
            "papel" => new JanelaPapelDeParede(),
            "entrar" => new JanelaEntrar(repositorio),
            "primeiro" => new JanelaNovoProfessor(new RepositorioPerfis(Path.GetTempFileName() + ".json"), ModoPerfil.PrimeiroAcesso),
            "novo" => new JanelaNovoProfessor(repositorio, ModoPerfil.Novo),
            "trocarpin" => new JanelaNovoProfessor(repositorio, ModoPerfil.TrocarPin, repositorio.Perfis[0]),
            "confirmar" => new JanelaConfirmarPin(repositorio, repositorio.Perfis[0], "Diego, digite o seu PIN para adicionar um professor:"),
            "professores" => new JanelaProfessores(repositorio, repositorio.Perfis[0]),
            "sobre" => new JanelaSobre(),
            "sala" => new JanelaSala(),
            "enviar" => new JanelaEnviarArquivos(sala.Computadores.Select(c => new ConexaoComputador(c, Sessao.Sala?.Chave)).ToList()),
            "atualizar" => new JanelaAtualizarAlunos(sala.Computadores.Select(c => new ConexaoComputador(c, Sessao.Sala?.Chave)).ToList()),
            "programas" => new JanelaProgramas(),
            "editarprograma" => new JanelaEditarPrograma(null),
            "abrirprograma" => new JanelaComando(sala.Computadores.Select(c => new ConexaoComputador(c, Sessao.Sala?.Chave)).ToList(),
                AcaoComando.AbrirPrograma, "Abrir programa nos computadores", "Programa", _ => (true, ""),
                Programas.Carregar().Select(pr => new JanelaComando.Opcao(pr.Nome, pr.Comando)).ToList()),
            "wol" => new JanelaWakeOnLan(sala),
            "novaversao" => new JanelaNovaVersao(new ControleSala.Comum.AtualizacaoDisponivel("0.4.0", "https://x/Setup.exe", "- Correções e melhorias\n- Novo comando X\n- Mais estável"), sala.Computadores.Select(c => new ConexaoComputador(c, Sessao.Sala?.Chave)).ToList()),
            "limpeza" => new JanelaLimpeza(),
            "sites" => new JanelaSites(),
            "comandomsg" => new JanelaComando(sala.Computadores.Select(c => new ConexaoComputador(c, Sessao.Sala?.Chave)).ToList(),
                AcaoComando.Mensagem, "Enviar mensagem aos alunos", "Mensagem que aparece na tela do aluno",
                texto => string.IsNullOrWhiteSpace(texto) ? (false, "Digite a mensagem.") : (true, texto.Trim())),
            "comandolink" => new JanelaComando(sala.Computadores.Select(c => new ConexaoComputador(c, Sessao.Sala?.Chave)).ToList(),
                AcaoComando.AbrirLink, "Abrir link nos computadores", "Endereço do site (ex.: www.escola.edu.br)",
                texto => Links.Validar(texto, out var url) is { } erro ? (false, erro) : (true, url)),
            "senha" => new JanelaSenhaPacote("Senha do pacote", "Escolha uma senha para proteger o pacote da sala.", confirmar: true),
            "computadores" => new JanelaComputadores(sala),
            "editar" => new JanelaEditarComputador(sala),
            "procurar" => new JanelaProcurar(sala),
            "tela" => new JanelaTelaCheia(conexao ?? throw new ArgumentException("A sala de teste está vazia.")),
            _ => throw new ArgumentException($"Janela desconhecida: {nome}"),
        };
        janela.WindowStartupLocation = WindowStartupLocation.Manual;
        janela.Left = -20000; // fora da tela: ninguém vê, e nada da área de trabalho entra na imagem
        janela.Top = 0;
        janela.ShowActivated = false;
        janela.Show();
        if (esperar > 0)
        {
            // Deixa a janela viva por um tempo (conexões, miniaturas, buscas) antes de desenhar.
            var quadro = new System.Windows.Threading.DispatcherFrame();
            var relogio = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(esperar) };
            relogio.Tick += (_, _) => { relogio.Stop(); quadro.Continue = false; };
            relogio.Start();
            System.Windows.Threading.Dispatcher.PushFrame(quadro);
        }
        janela.UpdateLayout();

        // A raiz visual da janela é a área de cliente inteira (com margens e fundo), sem a barra de título.
        var raiz = (FrameworkElement)VisualTreeHelper.GetChild(janela, 0);
        int w = (int)Math.Ceiling(raiz.ActualWidth), h = (int)Math.Ceiling(raiz.ActualHeight);
        var imagem = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        imagem.Render(raiz);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(imagem));
        using (var arquivo = File.Create(saida))
            encoder.Save(arquivo);
        if (janela is JanelaTelaCheia tela)
            File.WriteAllText(saida + ".txt", tela.Resumo);
        janela.Close();
        conexao?.Dispose();
    }
}
