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
        TextoSub.Text = $"Você tem a versão {Versoes.Atual}.";
        TextoNotas.Text = string.IsNullOrWhiteSpace(nova.Notas) ? "(sem descrição)" : nova.Notas;
    }

    async void Baixar_Click(object sender, RoutedEventArgs e)
    {
        TextoMensagem.Text = "";
        BotaoBaixar.IsEnabled = false;
        Barra.Visibility = Visibility.Visible;
        Barra.IsIndeterminate = false;
        Barra.Value = 0;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Atualizacoes.CaminhoPadrao)!);
            await BaixarAsync(_nova.UrlInstalador, Atualizacoes.CaminhoPadrao);

            // Confere a assinatura do autor: só um instalador oficial é aceito.
            TextoMensagem.Foreground = (System.Windows.Media.Brush)FindResource("PincelTextoFraco");
            TextoMensagem.Text = "Conferindo a assinatura...";
            var pronto = await Atualizacoes.PrepararAsync(Atualizacoes.CaminhoPadrao, default);

            _baixado = true;
            BotaoConsole.IsEnabled = true;
            BotaoAlunos.IsEnabled = _computadores.Count > 0;
            TextoMensagem.Foreground = (System.Windows.Media.Brush)FindResource("PincelOnline");
            TextoMensagem.Text = $"Instalador {pronto.Versao} baixado e assinatura conferida. Escolha o que fazer.";
        }
        catch (ArgumentException ex)   // assinatura inválida / instalador não encontrado
        {
            TextoMensagem.Foreground = (System.Windows.Media.Brush)FindResource("PincelOffline");
            TextoMensagem.Text = ex.Message;
            BotaoBaixar.IsEnabled = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            TextoMensagem.Foreground = (System.Windows.Media.Brush)FindResource("PincelOffline");
            TextoMensagem.Text = $"Não foi possível baixar: {ex.Message}";
            BotaoBaixar.IsEnabled = true;
        }
        finally
        {
            Barra.Visibility = Visibility.Collapsed;
        }
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

    void EnviarAlunos_Click(object sender, RoutedEventArgs e)
    {
        if (!_baixado) return;
        // A janela de atualização dos alunos carrega sozinha o instalador que acabamos de salvar.
        new JanelaAtualizarAlunos(_computadores) { Owner = Owner }.ShowDialog();
        Close();
    }
}
