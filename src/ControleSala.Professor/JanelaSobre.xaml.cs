using System.Reflection;
using System.Windows;
using ControleSala.Comum;

namespace ControleSala.Professor;

public partial class JanelaSobre : Window
{
    public JanelaSobre()
    {
        InitializeComponent();
        string versao = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "?";
        TextoVersao.Text = $"Versão {versao.Split('+')[0]} (prova de conceito)";
        TextoAutor.Text = Creditos.Autor;
        TextoComponentes.Text =
            $"FFmpeg {FFmpegNativo.Versao} — LGPL v3 (texto em LICENSE-FFmpeg-LGPL.txt, na pasta do programa)\n" +
            "FFmpeg.AutoGen — MIT";
    }
}
