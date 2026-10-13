using FFmpeg.AutoGen;

namespace ControleSala.Comum;

/// <summary>Carrega as DLLs do FFmpeg que ficam ao lado do .exe.</summary>
public static unsafe class FFmpegNativo
{
    static bool _iniciado;

    public static void Inicializar()
    {
        if (_iniciado) return;
        ffmpeg.RootPath = AppContext.BaseDirectory;
        DynamicallyLoadedBindings.Initialize();
        ffmpeg.av_log_set_level(ffmpeg.AV_LOG_WARNING);
        _iniciado = true;
    }

    public static string Versao => ffmpeg.av_version_info();

    public static string Erro(int codigo)
    {
        const int tamanho = 256;
        var buffer = stackalloc byte[tamanho];
        ffmpeg.av_strerror(codigo, buffer, tamanho);
        return new string((sbyte*)buffer);
    }

    public static void Verificar(int codigo, string operacao)
    {
        if (codigo < 0)
            throw new InvalidOperationException($"{operacao} falhou: {Erro(codigo)} ({codigo})");
    }
}
