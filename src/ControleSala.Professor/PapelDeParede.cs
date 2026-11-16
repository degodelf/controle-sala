using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ControleSala.Comum;

namespace ControleSala.Professor;

/// <summary>Escolhas do professor para o papel de parede padrão da sala.</summary>
public sealed class ConfigPapelDeParede
{
    public string? Fundo { get; set; }
    public int Largura { get; set; } = 1920;
    public int Altura { get; set; } = 1080;
    public bool MostrarCredito { get; set; } = true;

    public static string CaminhoPadrao => Path.Combine(Pastas.Dados, "papel-de-parede.json");

    public static ConfigPapelDeParede Carregar(string? caminho = null)
    {
        try
        {
            string c = caminho ?? CaminhoPadrao;
            if (File.Exists(c))
                return JsonSerializer.Deserialize<ConfigPapelDeParede>(File.ReadAllText(c)) ?? new();
        }
        catch (Exception)
        {
            // Arquivo corrompido: volta para o padrão em vez de impedir o programa de abrir.
        }
        return new();
    }

    public void Salvar(string? caminho = null)
    {
        string c = caminho ?? CaminhoPadrao;
        Directory.CreateDirectory(Path.GetDirectoryName(c)!);
        File.WriteAllText(c, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class PapelDeParede
{
    static readonly Color CorDeFundo = Color.FromRgb(0x1F, 0x3A, 0x5F);

    public static BitmapImage CarregarImagem(string caminho)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad; // não trava o arquivo no disco
        img.UriSource = new Uri(caminho, UriKind.Absolute);
        img.EndInit();
        img.Freeze();
        return img;
    }

    /// <summary>A imagem de fundo (+ crédito discreto) no tamanho configurado.</summary>
    public static BitmapSource Compor(ConfigPapelDeParede c, BitmapSource? fundo)
    {
        int w = c.Largura, h = c.Altura;
        var desenho = new DrawingVisual();
        using (var dc = desenho.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(CorDeFundo), null, new Rect(0, 0, w, h));

            if (fundo != null)
            {
                // Preenche a tela inteira sem distorcer (corta o excesso).
                double escala = Math.Max((double)w / fundo.PixelWidth, (double)h / fundo.PixelHeight);
                double dw = fundo.PixelWidth * escala, dh = fundo.PixelHeight * escala;
                dc.DrawImage(fundo, new Rect((w - dw) / 2, (h - dh) / 2, dw, dh));
            }

            if (c.MostrarCredito)
            {
                var texto = new FormattedText(Creditos.Linha, CultureInfo.GetCultureInfo("pt-BR"),
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(10, h / 90.0),
                    new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), 1.0);
                // Canto inferior esquerdo, pequeno e translúcido.
                dc.DrawText(texto, new Point(w * 0.012, h - texto.Height - h * 0.012));
            }
        }

        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(desenho);
        bitmap.Freeze();
        return bitmap;
    }

    public static void SalvarPng(BitmapSource imagem, string caminho)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(imagem));
        using var arquivo = File.Create(caminho);
        encoder.Save(arquivo);
    }

}
