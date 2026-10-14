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
    bool _baixado;

    internal JanelaNovaVersao(AtualizacaoDisponivel nova, List<ConexaoComputador> computadores)
    {
        _nova = nova;
        _computadores = computadores;
        InitializeComponent();
        Title = $"Nova versão {nova.Versao}";
        TextoTitulo.Text = $"Nova versão disponível: {nova.Versao}";
        TextoSub.Text = $"Você tem a versão {Versoes.Atual}. Atualizar mantém a sala, os professores e as configurações.";
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
            Directory.CreateDirectory(Path.GetDirectoryName(Atualizacoes.CaminhoPadrao)!);
            await BaixarAsync(_nova.UrlInstalador, Atualizacoes.CaminhoPadrao);
            if (!ArquivoEhInstalador(Atualizacoes.CaminhoPadrao))
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
                "Atualizar este computador agora? O ControleSala vai baixar a versão nova, fechar e instalar por cima " +
                "(a sala, os professores e as configurações continuam). Só vai aparecer o aviso de permissão do Windows.",
                "Atualizar agora", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        BotaoAtualizar.IsEnabled = false;
        BotaoAlunos.IsEnabled = false;
        try
        {
            await GarantirBaixadoAsync();
            InstalarEFechar();
        }
        catch (ArgumentException ex)   // assinatura inválida / instalador não encontrado
        {
            Erro(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            Erro($"Não foi possível baixar: {ex.Message}");
        }
    }

    void InstalarEFechar()
    {
        try
        {
            // O instalador pede administrador (abre a janela de permissão do Windows) e instala por cima.
            Process.Start(new ProcessStartInfo(Atualizacoes.CaminhoPadrao) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Erro($"Não foi possível abrir o instalador: {ex.Message}");
        }
    }

    void Erro(string texto)
    {
        TextoMensagem.Foreground = (System.Windows.Media.Brush)FindResource("PincelOffline");
        TextoMensagem.Text = texto;
        BotaoAtualizar.IsEnabled = true;
        BotaoAlunos.IsEnabled = _computadores.Count > 0;
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

    void AtualizarConsole_Click(object sender, RoutedEventArgs e)
    {
        if (!_baixado) return;
        if (MessageBox.Show(this, "Atualizar este computador agora? O ControleSala vai fechar e o instalador vai abrir (ele mantém a sala, os professores e as configurações).",
                "Atualizar este computador", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            // O instalador pede administrador (abre a janela de permissão do Windows) e instala por cima.
            Process.Start(new ProcessStartInfo(Atualizacoes.CaminhoPadrao) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            TextoMensagem.Foreground = (System.Windows.Media.Brush)FindResource("PincelOffline");
            TextoMensagem.Text = $"Não foi possível abrir o instalador: {ex.Message}";
        }
    }

    async void EnviarAlunos_Click(object sender, RoutedEventArgs e)
    {
        BotaoAtualizar.IsEnabled = false;
        BotaoAlunos.IsEnabled = false;
        try
        {
            await GarantirBaixadoAsync();
        }
        catch (ArgumentException ex)
        {
            Erro(ex.Message);
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            Erro($"Não foi possível baixar: {ex.Message}");
            return;
        }
        // A janela de atualização dos alunos carrega sozinha o instalador que acabamos de salvar.
        new JanelaAtualizarAlunos(_computadores) { Owner = Owner }.ShowDialog();
        Close();
    }
}
