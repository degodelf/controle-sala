using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using ControleSala.Comum;

namespace ControleSala.Agente;

static partial class Programa
{
    static volatile bool _rodando = true;
    static readonly int[] _pedidosChave = new int[2]; // um por modo de vídeo
    static StreamWriter? _log;
    static readonly Lock _travaLog = new();

    static int PreviaLimpeza(string pasta, string? imagem)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        Directory.CreateDirectory(pasta);
        using (var f = new AvisoLimpezaForm(45, imagem)) { using var b = f.Previa(1200, 760); b.Save(Path.Combine(pasta, "aviso.png")); }
        using (var f = new LimpezaForm(imagem)) { using var b = f.Previa(1200, 760); b.Save(Path.Combine(pasta, "carregando.png")); }
        Console.WriteLine("Prévias geradas em " + pasta);
        return 0;
    }

    [STAThread]
    static int Main(string[] args)
    {
        // Sem isso o Windows pode recusar a Desktop Duplication ou entregar a tela em escala errada.
        SetProcessDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2

        // Dev: gera imagens das telas de limpeza para conferir o visual (--previa-limpeza <pasta> [imagem]).
        if (Array.IndexOf(args, "--previa-limpeza") is int pi && pi >= 0)
            return PreviaLimpeza(args[pi + 1], pi + 2 < args.Length ? args[pi + 2] : null);

        var opcoes = Opcoes.Ler(args);
        if (opcoes == null) return 1;

        if (opcoes.Servico)
        {
            try
            {
                ServiceBase.Run(new ServicoAgente());
                return 0;
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("--servico só funciona quando iniciado pelo Windows (serviço ControleSalaAgente).");
                return 1;
            }
        }

        PrepararConsole();
        if (opcoes.CartaoCaptura != null) return Bandeja.CapturarCartao(opcoes.CartaoCaptura);
        if (opcoes.DefinirPapel != null) { LimpezaExecutor.DefinirPapelDeParede(opcoes.DefinirPapel); return 0; }
        if (opcoes.Limpar)
        {
            InterfaceAgente.Iniciar();
            string cfgLimpeza = Path.Combine(LimpezaExecutor.PastaDados, "limpeza.json");
            var (ok, msg) = LimpezaExecutor.Executar(LimpezaConfig.Carregar(cfgLimpeza));
            Console.WriteLine(msg);
            return ok ? 0 : 1;
        }
        if (opcoes.ImportarSala != null || opcoes.VerificarSala != null) return ComandoSala(opcoes);
        if (opcoes.SalaAtual) return MostrarSalaAtual(opcoes);
        if (opcoes.Desfazer) return Desfazer();

        ConfigSala? sala;
        try
        {
            sala = CarregarSala(opcoes);
        }
        catch (SenhaPacoteIncorretaException)
        {
            Console.WriteLine("Senha do pacote da sala incorreta.");
            return 3;
        }
        catch (Exception ex) when (ex is PacoteInvalidoException or IOException)
        {
            Console.WriteLine($"Não foi possível ler o pacote da sala: {ex.Message}");
            return 4;
        }

        // Aceitar conexões da rede sem chave da sala deixaria qualquer um ver a tela: não é permitido.
        bool rede = opcoes.Rede || opcoes.Sessao;
        if (rede && sala == null)
        {
            Console.WriteLine("""
                Este PC ainda não tem a chave da sala, então só pode aceitar conexões dele mesmo.
                Instale pelo ControleSala-Setup (opção "Aluno") ou use:
                  ControleSala.Agente.exe --importar-sala <arquivo.controlesala> --senha <senha>   (como administrador)
                  ControleSala.Agente.exe --rede --sala <arquivo.controlesala> --senha <senha>     (só para teste)
                """);
            return 2;
        }
        if (opcoes.Sessao) AbrirLogEmArquivo();

        Console.Title = "ControleSala — Agente";
        Log("ControleSala — Agente");
        if (sala == null) Log("Modo de teste: sem chave da sala, aceita só conexões deste PC, sem criptografia.");
        else Log($"Sala: {sala.Sala} — {sala.Escola} | chave {sala.Impressao()} (conexões autenticadas e criptografadas)");
        TentarCor(ConsoleColor.DarkGray, () => Console.WriteLine(Creditos.Linha));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _rodando = false; };

        FFmpegNativo.Inicializar();
        Log($"FFmpeg {FFmpegNativo.Versao}");

        var endereco = rede ? IPAddress.Any : IPAddress.Loopback;
        // Pasta padrão = área de trabalho pública do PC do aluno (todos os usuários veem e podem alterar).
        // --pasta-recebidos serve para testar sem administrador.
        string pastaRecebidos = opcoes.PastaRecebidos ?? PastaRecebidos.Padrao;
        ServidorVideo servidor;
        try
        {
            servidor = new ServidorVideo(endereco, opcoes.Porta, sala?.Chave, pastaRecebidos, liberarParaAlunos: opcoes.PastaRecebidos == null);
        }
        catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.AccessDenied)
        {
            // O Windows (Hyper-V/virtualização) reservou a faixa de portas e bloqueou o bind (erro 10013).
            Log($"ERRO: o Windows bloqueou a porta {opcoes.Porta} (erro 10013). Reinstale pelo ControleSala-Setup novo " +
                "(ele reserva a porta), ou como administrador: 'net stop winnat' → " +
                $"'netsh int ipv4 add excludedportrange protocol=tcp startport={opcoes.Porta} numberofports=1 store=persistent' → 'net start winnat', e reinicie.");
            return 3;
        }
        using var _descarteServidor = servidor;
        string? mac = MacDaPlaca();
        if (mac != null) { servidor.MacMsg = Protocolo.MontarEnderecoMac(mac); Log($"MAC deste PC: {mac}"); }
        Log($"Versão {Versoes.Atual}");
        // O instalador de uma atualização ainda pode estar terminando quando este agente liga: apaga depois.
        _ = Task.Delay(TimeSpan.FromMinutes(3)).ContinueWith(_ => AtualizacaoAgente.LimparRestos());
        servidor.Aviso += m => Log(m);
        servidor.PedidoQuadroChave += modo => Interlocked.Exchange(ref _pedidosChave[(int)modo], 1);
        Log(rede
            ? $"Aguardando o professor na porta {opcoes.Porta} (rede local). Ctrl+C encerra."
            : $"Aguardando o professor em 127.0.0.1:{opcoes.Porta} (só este PC). Ctrl+C encerra.");

        var extras = opcoes.Anunciar.Select(IPAddress.Parse).ToArray();
        using var anunciante = rede || extras.Length > 0
            ? new Anunciante(Environment.MachineName, opcoes.Porta, rede, extras)
            : null;
        if (anunciante != null)
            Log("Anunciando este PC na rede para o professor encontrar.");

        // A thread de interface mostra as janelas no PC do aluno (bandeja, mensagem, bloqueio) e recebe os comandos.
        InterfaceAgente.Iniciar();
        Bandeja? bandeja = null;
        AgendadorLimpeza? agendador = null;
        if (opcoes.Sessao)
        {
            InterfaceAgente.NaInterface(() => bandeja = new Bandeja(sala, servidor));
            agendador = AgendadorLimpeza.Iniciar(); // limpeza agendada: avisa o aluno e limpa, aqui na sessão
        }

        Executar(servidor, opcoes); // bloqueia nesta thread até Ctrl+C / parada do serviço

        agendador?.Dispose();
        if (bandeja != null) InterfaceAgente.NaInterface(bandeja.Dispose);
        Log("Agente encerrado.");
        return 0;
    }

    /// <summary>MAC da placa de rede ativa usada para sair para a rede (a que o professor alcança). Null se não achar.</summary>
    static string? MacDaPlaca()
    {
        try
        {
            using var udp = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
            // "Conecta" (sem enviar) a um endereço externo só para o Windows dizer qual placa sai para a rede.
            udp.Connect("8.8.8.8", 65530);
            var ipLocal = (udp.LocalEndPoint as IPEndPoint)?.Address;

            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                    && n.GetPhysicalAddress().GetAddressBytes().Length == 6).ToList();

            // Prefere a placa que tem o IP de saída; senão, a primeira Ethernet/Wi-Fi.
            var escolhida = interfaces.FirstOrDefault(n => ipLocal != null
                    && n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(ipLocal)))
                ?? interfaces.FirstOrDefault(n => n.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Ethernet
                        or System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                ?? interfaces.FirstOrDefault();

            var bytes = escolhida?.GetPhysicalAddress().GetAddressBytes();
            return bytes is { Length: 6 } ? WakeOnLan.Normalizar(string.Join(":", bytes.Select(x => x.ToString("X2")))) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static void Executar(ServidorVideo servidor, Opcoes opcoes)
    {
        while (_rodando)
        {
            try
            {
                using var captura = new CapturaDxgi();

                // Cria o fluxo completo já na partida: confirma qual encoder funciona neste PC antes de avisar o professor.
                var sondagem = new FluxoVideo(captura, ModoVideo.Completo, captura.Largura, captura.Altura, opcoes.Fps, opcoes.Kbps, opcoes.Encoder);
                string encoder = sondagem.NomeEncoder;
                Log($"Tela {captura.Largura}x{captura.Altura} | GPU: {captura.Adaptador} | encoder: {encoder}");

                var ola = Protocolo.MontarOla(new Ola(captura.Largura, captura.Altura, encoder, Environment.MachineName));
                servidor.Ola = ola;
                servidor.TransmitirControle(ola);

                Transmitir(captura, servidor, opcoes, encoder, sondagem);
            }
            catch (AcessoPerdidoException)
            {
                Log("O Windows interrompeu a captura (bloqueio de tela, UAC ou mudança de resolução). Reiniciando...");
                Thread.Sleep(250);
            }
            catch (Exception ex) when (_rodando)
            {
                Log($"ERRO: {ex.Message}");
                Thread.Sleep(1000);
            }
        }
    }

    static ConfigSala? CarregarSala(Opcoes o)
    {
        if (o.Sala != null)
            return PacoteSala.Importar(File.ReadAllBytes(o.Sala), SenhaDe(o));
        return ConfigAgente.Carregar();
    }

    /// <summary>--importar-sala (grava a sala neste PC) e --verificar-sala (só confere o pacote e a senha). Códigos: 0 ok, 3 senha, 4 pacote, 5 sem permissão.</summary>
    static int ComandoSala(Opcoes o)
    {
        string arquivo = o.ImportarSala ?? o.VerificarSala!;
        try
        {
            var sala = PacoteSala.Importar(File.ReadAllBytes(arquivo), SenhaDe(o));
            if (o.ImportarSala != null) ConfigAgente.Salvar(sala);
            if (o.Saida != null) File.WriteAllText(o.Saida, sala.Escola + "\n" + sala.Sala);
            Console.WriteLine(o.ImportarSala != null
                ? $"Sala \"{sala.Sala}\" ({sala.Escola}) configurada neste computador."
                : $"Pacote da sala \"{sala.Sala}\" ({sala.Escola}) conferido.");
            return 0;
        }
        catch (SenhaPacoteIncorretaException)
        {
            Console.WriteLine("Senha incorreta.");
            return 3;
        }
        catch (PacoteInvalidoException ex)
        {
            Console.WriteLine(ex.Message);
            return 4;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Não foi possível ler/gravar: {ex.Message} (rode como administrador?)");
            return 5;
        }
    }

    /// <summary>--sala-atual: diz se este PC já tem sala gravada (o instalador usa na atualização). Códigos: 0 tem sala, 2 não tem.</summary>
    static int MostrarSalaAtual(Opcoes o)
    {
        var sala = ConfigAgente.Carregar();
        if (sala == null)
        {
            Console.WriteLine("Nenhuma sala configurada neste computador (ou sem permissão para ler).");
            return 2;
        }
        if (o.Saida != null) File.WriteAllText(o.Saida, sala.Escola + "\n" + sala.Sala);
        Console.WriteLine($"Sala \"{sala.Sala}\" ({sala.Escola}).");
        return 0;
    }

    /// <summary>--desfazer: tira o que o agente mudou no Windows (bloqueio de internet/sites e limpeza agendada). O desinstalador chama.</summary>
    static int Desfazer()
    {
        var (ok, msg) = Comandos.LiberarInternet();
        Console.WriteLine(msg);
        ConfigLimpezaAgente.RemoverAgendamento();
        Console.WriteLine("Limpeza agendada removida.");
        return ok ? 0 : 1;
    }

    /// <summary>--senha ou, se não vier, a variável de ambiente CONTROLESALA_SENHA (o instalador usa esta, para a senha não aparecer na linha de comando).</summary>
    static string SenhaDe(Opcoes o) => o.Senha ?? Environment.GetEnvironmentVariable("CONTROLESALA_SENHA") ?? "";

    static FluxoVideo CriarFluxo(CapturaDxgi captura, ModoVideo modo, Opcoes o, string encoder)
    {
        if (modo == ModoVideo.Completo)
            return new FluxoVideo(captura, modo, captura.Largura, captura.Altura, o.Fps, o.Kbps, encoder);

        int largura = Math.Min(o.MiniaturaLargura, captura.Largura) & ~1;
        int altura = (int)Math.Round(largura * (double)captura.Altura / captura.Largura / 2) * 2;
        try
        {
            return new FluxoVideo(captura, modo, largura, altura, o.MiniaturaFps, o.MiniaturaKbps, encoder);
        }
        catch (Exception)
        {
            // O encoder escolhido não aceitou o tamanho pequeno: tenta a lista toda.
            return new FluxoVideo(captura, modo, largura, altura, o.MiniaturaFps, o.MiniaturaKbps, "auto");
        }
    }

    static void Transmitir(CapturaDxgi captura, ServidorVideo servidor, Opcoes opcoes, string encoder, FluxoVideo sondagem)
    {
        // Só existe fluxo para o modo que algum professor está pedindo; fluxo sem uso por 5 s é desligado.
        var fluxos = new Dictionary<ModoVideo, FluxoVideo> { [ModoVideo.Completo] = sondagem };
        long ociosoMax = Stopwatch.Frequency * 5;
        try
        {
            while (_rodando)
            {
                long agora = Stopwatch.GetTimestamp();
                if (servidor.Quantidade == 0)
                {
                    // Ninguém assistindo: não captura nem codifica nada (CPU/GPU em repouso).
                    foreach (var f in fluxos.Values) f.Dispose();
                    fluxos.Clear();
                    servidor.TemProfessores.Wait(500);
                    continue;
                }

                foreach (var modo in new[] { ModoVideo.Completo, ModoVideo.Miniatura })
                {
                    if (servidor.Precisa(modo))
                    {
                        if (!fluxos.TryGetValue(modo, out var fluxo))
                        {
                            fluxo = CriarFluxo(captura, modo, opcoes, encoder);
                            fluxos[modo] = fluxo;
                            fluxo.PedirChave();
                            Log($"Fluxo {Nome(modo)} ligado: {fluxo.Largura}x{fluxo.Altura}");
                        }
                        fluxo.UltimoUso = agora;
                    }
                    else if (fluxos.TryGetValue(modo, out var velho) && agora - velho.UltimoUso > ociosoMax)
                    {
                        velho.Dispose();
                        fluxos.Remove(modo);
                        Log($"Fluxo {Nome(modo)} desligado (ninguém usando).");
                    }
                }

                var ativos = fluxos.Values.Where(f => servidor.Precisa(f.Modo)).ToArray();
                foreach (var f in ativos)
                    if (Interlocked.Exchange(ref _pedidosChave[(int)f.Modo], 0) == 1)
                        f.PedirChave();

                long espera = ativos.Length == 0 ? long.MaxValue : ativos.Min(f => f.TicksAteProximo(agora));
                int esperaMs = espera == long.MaxValue ? 100 : (int)Math.Min(100, espera * 1000 / Stopwatch.Frequency);
                // Só miniaturas: não precisa olhar a tela 60 vezes por segundo, dorme até a hora do próximo envio.
                if (ativos.Length > 0 && ativos.All(f => f.Modo == ModoVideo.Miniatura) && esperaMs > 0)
                {
                    Thread.Sleep(esperaMs);
                    esperaMs = 0;
                }

                if (captura.Capturar(esperaMs))
                    foreach (var f in ativos) f.NovaImagem();

                agora = Stopwatch.GetTimestamp();
                foreach (var f in ativos)
                {
                    f.Processar(captura, servidor, agora);
                    if (f.Estat.Pronta(agora))
                    {
                        if (f.Modo == ModoVideo.Completo)
                            TentarTitulo($"ControleSala — Agente ● TRANSMITINDO para {servidor.Quantidade} professor(es)");
                        Log($"[{Nome(f.Modo)}] {f.Estat.Resumo(agora)} | {servidor.Quantidade} professor(es)");
                    }
                }
            }
        }
        finally
        {
            foreach (var f in fluxos.Values) f.Dispose();
        }
    }

    static string Nome(ModoVideo modo) => modo == ModoVideo.Completo ? "completo" : "miniatura";

    static void PrepararConsole()
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { /* sem console */ }
    }

    static void TentarTitulo(string titulo)
    {
        try { Console.Title = titulo; } catch (IOException) { /* sem console */ }
    }

    static void TentarCor(ConsoleColor cor, Action escrever)
    {
        try { Console.ForegroundColor = cor; escrever(); Console.ResetColor(); }
        catch (IOException) { /* sem console */ }
    }

    static void AbrirLogEmArquivo()
    {
        try
        {
            string pasta = Path.Combine(Path.GetDirectoryName(ConfigAgente.CaminhoPadrao)!, "logs");
            Directory.CreateDirectory(pasta);
            string arquivo = Path.Combine(pasta, "agente.log");
            if (File.Exists(arquivo) && new FileInfo(arquivo).Length > 1_000_000) File.Delete(arquivo);
            _log = new StreamWriter(File.Open(arquivo, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // sem onde gravar: segue só com o console
        }
    }

    static void Log(string mensagem)
    {
        string linha = $"[{DateTime.Now:HH:mm:ss}] {mensagem}";
        try { Console.WriteLine(linha); } catch (IOException) { /* sem console */ }
        lock (_travaLog) _log?.WriteLine(linha);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(nint valor);
}

sealed record Opcoes(int Porta, bool Rede, int Fps, int Kbps, string Encoder,
    int MiniaturaLargura, int MiniaturaFps, int MiniaturaKbps, string[] Anunciar,
    string? Sala, string? Senha, string? ImportarSala, string? VerificarSala, string? Saida,
    bool Servico, bool Sessao, string? CartaoCaptura, string? PastaRecebidos, string? DefinirPapel, bool Limpar,
    bool SalaAtual = false, bool Desfazer = false)
{
    public static Opcoes? Ler(string[] args)
    {
        // 30 fps é suave o bastante para monitorar e controlar a sala, e usa metade da rede/CPU do que 60.
        var o = new Opcoes(Protocolo.PortaPadrao, false, 30, 6000, "auto", 480, 2, 600, [],
            null, null, null, null, null, false, false, null, null, null, false);
        for (int i = 0; i < args.Length; i++)
        {
            string Valor() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Falta o valor de {args[i]}");
            try
            {
                switch (args[i])
                {
                    case "--porta": o = o with { Porta = int.Parse(Valor()) }; break;
                    case "--rede": o = o with { Rede = true }; break;
                    case "--fps": o = o with { Fps = Math.Clamp(int.Parse(Valor()), 1, 120) }; break;
                    case "--kbps": o = o with { Kbps = Math.Clamp(int.Parse(Valor()), 200, 50000) }; break;
                    case "--encoder": o = o with { Encoder = Valor() }; break;
                    case "--miniatura-largura": o = o with { MiniaturaLargura = Math.Clamp(int.Parse(Valor()), 160, 960) }; break;
                    case "--miniatura-fps": o = o with { MiniaturaFps = Math.Clamp(int.Parse(Valor()), 1, 10) }; break;
                    case "--miniatura-kbps": o = o with { MiniaturaKbps = Math.Clamp(int.Parse(Valor()), 100, 5000) }; break;
                    case "--anunciar":
                        string ip = Valor();
                        if (!IPAddress.TryParse(ip, out _)) throw new ArgumentException($"Endereço inválido: {ip}");
                        o = o with { Anunciar = [.. o.Anunciar, ip] };
                        break;
                    case "--sala": o = o with { Sala = Valor() }; break;
                    case "--senha": o = o with { Senha = Valor() }; break;
                    case "--importar-sala": o = o with { ImportarSala = Valor() }; break;
                    case "--verificar-sala": o = o with { VerificarSala = Valor() }; break;
                    case "--saida": o = o with { Saida = Valor() }; break;
                    case "--servico": o = o with { Servico = true }; break;
                    case "--sessao": o = o with { Sessao = true }; break;
                    case "--cartao-captura": o = o with { CartaoCaptura = Valor() }; break;
                    case "--definir-papel": o = o with { DefinirPapel = Valor() }; break;
                    case "--limpar": o = o with { Limpar = true }; break;
                    case "--sala-atual": o = o with { SalaAtual = true }; break;
                    case "--desfazer": o = o with { Desfazer = true }; break;
                    case "--pasta-recebidos": o = o with { PastaRecebidos = Valor() }; break;
                    default: return Ajuda();
                }
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
            {
                Console.WriteLine($"Opção inválida: {ex.Message}\n");
                return Ajuda();
            }
        }
        return o;
    }

    static Opcoes? Ajuda()
    {
        Console.WriteLine("""
            Uso: ControleSala.Agente [--rede] [--porta 47810] [--fps 30] [--kbps 6000]
                                     [--encoder auto|h264_qsv|h264_mf|libopenh264]
                                     [--miniatura-largura 480] [--miniatura-fps 2] [--miniatura-kbps 600]
                                     [--anunciar <ip>]... [--sala <arquivo> --senha <senha>]
              --rede             aceita conexões da rede local e se anuncia (exige a chave da sala)
              --sala/--senha     usa a chave do pacote .controlesala só nesta execução (teste)
              --anunciar         também anuncia para esse endereço (teste local: --anunciar 127.0.0.1)
              --importar-sala    grava a sala neste PC (como administrador): --importar-sala <arquivo> --senha <senha>
              --verificar-sala   só confere o pacote e a senha (opcional: --saida <arquivo> grava escola e sala)
              --sala-atual       diz se este PC já tem sala gravada (opcional: --saida <arquivo>); usado na atualização
              --desfazer         libera internet/sites e remove a limpeza agendada; usado pelo desinstalador
              --pasta-recebidos  onde gravar os arquivos do professor (padrão: "Arquivos do Professor" na área de trabalho pública)
              --servico/--sessao usados pelo serviço do Windows; não rode à mão
            """);
        return null;
    }
}
