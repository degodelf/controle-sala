using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Mostra a versão nova encontrada no GitHub e deixa o professor baixar e escolher: atualizar o próprio PC e/ou enviar aos alunos.</summary>
public partial class JanelaNovaVersao : Window
{
    readonly AtualizacaoDisponivel _nova;
    readonly List<ConexaoComputador> _computadores;
    readonly AutoAtualizadorAlunos? _auto;
    bool _baixado;

    internal JanelaNovaVersao(AtualizacaoDisponivel nova, List<ConexaoComputador> computadores, AutoAtualizadorAlunos? auto = null)
    {
        _nova = nova;
        _computadores = computadores;
        _auto = auto;
        InitializeComponent();
        Title = $"Nova versão {nova.Versao}";
        TextoTitulo.Text = $"Nova versão disponível: {nova.Versao}";
        TextoSub.Text = $"Você tem a versão {Versoes.Atual}. \"Atualizar tudo\" atualiza os alunos e este PC; só aparece uma permissão do Windows. A sala e as configurações continuam.";
        TextoNotas.Text = string.IsNullOrWhiteSpace(nova.Notas) ? "(sem descrição)" : nova.Notas;
        BotaoAlunos.IsEnabled = _computadores.Count > 0;
    }

    /// <summary>
    /// Baixa o instalador do GitHub (se ainda não baixou). Para atualizar o PRÓPRIO PC do professor basta isto:
    /// o download vem por HTTPS do repositório oficial. A assinatura do autor só é exigida para ENVIAR aos alunos
    /// (a janela de atualização dos alunos confere, e cada agente confere de novo ao receber).
    /// </summary>
    async Task GarantirBaixadoAsync()
    {
        if (_baixado) return;
        Barra.Visibility = Visibility.Visible;
        Barra.IsIndeterminate = false;
        Barra.Value = 0;
        try
        {
            TextoMensagem.Foreground = (System.Windows.Media.Brush)FindResource("PincelTextoFraco");
            TextoMensagem.Text = "Baixando a atualização...";
            Directory.CreateDirectory(Path.GetDirectoryName(Atualizacoes.CaminhoBaixado)!);
            await BaixarAsync(_nova.UrlInstalador, Atualizacoes.CaminhoBaixado);
            if (!ArquivoEhInstalador(Atualizacoes.CaminhoBaixado))
                throw new ArgumentException("O arquivo baixado não parece um instalador válido. Tente novamente.");
            _baixado = true;
            TextoMensagem.Foreground = (System.Windows.Media.Brush)FindResource("PincelOnline");
            TextoMensagem.Text = $"Versão {_nova.Versao} baixada do GitHub.";
        }
        finally
        {
            Barra.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Confere rapidamente que o download é um programa do Windows ("MZ") e não uma página de erro.</summary>
    static bool ArquivoEhInstalador(string caminho)
    {
        try
        {
            using var f = File.OpenRead(caminho);
            return f.Length > 100_000 && f.ReadByte() == 'M' && f.ReadByte() == 'Z';
        }
        catch (IOException) { return false; }
    }

    async void AtualizarAgora_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "Atualizar só este computador agora? O ControleSala baixa a versão nova, fecha e instala por cima " +
                "(a sala, os professores e as configurações continuam). Aparece uma permissão do Windows e, depois, " +
                "uma barra de carregamento; o programa reabre sozinho ao terminar.",
                "Atualizar este computador", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        Bloquear(true);
        try
        {
            await GarantirBaixadoAsync();
            InstalarEFechar();
        }
        catch (ArgumentException ex)   // assinatura inválida / instalador não encontrado
        {
            Erro(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            Erro($"Não foi possível baixar: {ex.Message}");
        }
    }

    void Bloquear(bool ocupado)
    {
        BotaoTudo.IsEnabled = BotaoAtualizar.IsEnabled = !ocupado;
        BotaoAlunos.IsEnabled = !ocupado && _computadores.Count > 0;
    }

    void InstalarEFechar()
    {
        try
        {
            // Modo silencioso: sem telas de "Avançar" — só a permissão do Windows e uma barra. O instalador
            // reabre o programa ao terminar (ver o [Run] no .iss). Anotamos a versão para mostrar "concluído" ao reabrir.
            Atualizacoes.MarcarAtualizando(_nova.Versao);
            Process.Start(new ProcessStartInfo(Atualizacoes.CaminhoBaixado)
            {
                UseShellExecute = true,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART",
            });
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Erro($"Não foi possível abrir o instalador: {ex.Message}");
        }
    }

    /// <summary>Atualiza os alunos online desatualizados, liga o auto-update para os que estão desligados, e por fim este PC.</summary>
    async void AtualizarTudo_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "Atualizar tudo agora?\n\n• Os computadores dos alunos que estão ligados se atualizam sozinhos (sem clique neles).\n" +
                "• Os que estiverem desligados serão atualizados assim que ligarem.\n" +
                "• Por último, este computador: aparece uma permissão do Windows, uma barra de carregamento, e o programa reabre sozinho.",
                "Atualizar tudo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        Bloquear(true);
        try
        {
            await GarantirBaixadoAsync();

            // Para enviar aos alunos o instalador precisa estar assinado por você. Se não estiver, atualiza só este PC.
            InstaladorParaAlunos inst;
            try
            {
                inst = await Atualizacoes.PrepararAsync(Atualizacoes.CaminhoBaixado, CancellationToken.None);
            }
            catch (ArgumentException)
            {
                TextoMensagem.Foreground = (System.Windows.Media.Brush)FindResource("PincelAviso");
                TextoMensagem.Text = "O instalador baixado não está assinado: atualizo só este computador. Para os alunos, use um instalador assinado.";
                await Task.Delay(2500);
                InstalarEFechar();
                return;
            }

            var alvos = _computadores.Where(c => Atualizacoes.Desatualizado(c, inst.Versao)).ToList();
            if (alvos.Count > 0)
            {
                Barra.Visibility = Visibility.Visible;
                Barra.IsIndeterminate = true;
                int prontos = 0;
                var progresso = new Progress<ProgressoAtualizacao>(p =>
                {
                    if (p.Ok != null && ++prontos <= alvos.Count)
                        TextoMensagem.Text = $"Enviando aos alunos... {prontos}/{alvos.Count}";
                });
                TextoMensagem.Foreground = (System.Windows.Media.Brush)FindResource("PincelTextoFraco");
                TextoMensagem.Text = $"Enviando aos alunos... 0/{alvos.Count}";
                await Atualizacoes.EnviarAsync(inst, alvos, progresso, CancellationToken.None);
                Barra.IsIndeterminate = false;
                Barra.Visibility = Visibility.Collapsed;
            }

            // Liga o auto-update: pega os desligados quando voltarem (e reinicia junto com o professor).
            _auto?.Ativar(inst);

            TextoMensagem.Foreground = (System.Windows.Media.Brush)FindResource("PincelOnline");
            TextoMensagem.Text = "Alunos atualizando. Agora este computador...";
            await Task.Delay(1200);
            InstalarEFechar();
        }
        catch (ArgumentException ex)
        {
            Erro(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            Erro($"Não foi possível baixar: {ex.Message}");
        }
    }

    void Erro(string texto)
    {
        TextoMensagem.Foreground = (System.Windows.Media.Brush)FindResource("PincelOffline");
        TextoMensagem.Text = texto;
        Bloquear(false);
    }

    async Task BaixarAsync(string url, string destino)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;
        await using var origem = await resp.Content.ReadAsStreamAsync();
        await using var arquivo = new FileStream(destino, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 16];
        long lidos = 0;
        int n;
        while ((n = await origem.ReadAsync(buffer)) > 0)
        {
            await arquivo.WriteAsync(buffer.AsMemory(0, n));
            lidos += n;
            if (total is > 0) Barra.Value = 100.0 * lidos / total.Value;
        }
    }

    async void EnviarAlunos_Click(object sender, RoutedEventArgs e)
    {
        Bloquear(true);
        try
        {
            await GarantirBaixadoAsync();
        }
        catch (ArgumentException ex)
        {
            Erro(ex.Message);
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            Erro($"Não foi possível baixar: {ex.Message}");
            return;
        }
        // Abre a janela dos alunos já apontando para o instalador que acabamos de baixar (ela confere a assinatura).
        new JanelaAtualizarAlunos(_computadores, Atualizacoes.CaminhoBaixado) { Owner = Owner }.ShowDialog();
        Close();
    }
}
