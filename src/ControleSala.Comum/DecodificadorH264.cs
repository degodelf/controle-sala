using FFmpeg.AutoGen;

namespace ControleSala.Comum;

/// <summary>Decodifica H.264 e converte cada quadro para BGRA num buffer fornecido por quem chama.</summary>
public sealed unsafe class DecodificadorH264 : IDisposable
{
    /// <summary>Devolve o destino BGRA (e o stride) para um quadro do tamanho dado.</summary>
    public delegate byte* ObterDestino(int largura, int altura, out int stride);

    AVCodecContext* _ctx;
    AVFrame* _quadro;
    AVPacket* _pacote;
    SwsContext* _sws;
    int _swsLargura, _swsAltura;
    AVPixelFormat _swsFormato;

    public DecodificadorH264()
    {
        var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
        _ctx = ffmpeg.avcodec_alloc_context3(codec);
        _ctx->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
        // Threads por quadro adicionariam um quadro de atraso por thread; por fatia não.
        _ctx->thread_type = ffmpeg.FF_THREAD_SLICE;
        _ctx->thread_count = Math.Min(Environment.ProcessorCount, 4);
        FFmpegNativo.Verificar(ffmpeg.avcodec_open2(_ctx, codec, null), "abrir decodificador H.264");
        _quadro = ffmpeg.av_frame_alloc();
        _pacote = ffmpeg.av_packet_alloc();
    }

    /// <summary>
    /// Decodifica um pacote. Retorna quantos quadros saíram (0 é normal no começo).
    /// Lança exceção se os dados estiverem corrompidos — nesse caso, peça um quadro-chave.
    /// </summary>
    public int Decodificar(byte* dados, int tamanho, ObterDestino obterDestino)
    {
        _pacote->data = dados;
        _pacote->size = tamanho;
        int r = ffmpeg.avcodec_send_packet(_ctx, _pacote);
        _pacote->data = null;
        _pacote->size = 0;
        FFmpegNativo.Verificar(r, "avcodec_send_packet");

        int n = 0;
        while (true)
        {
            r = ffmpeg.avcodec_receive_frame(_ctx, _quadro);
            if (r == ffmpeg.AVERROR(ffmpeg.EAGAIN) || r == ffmpeg.AVERROR_EOF)
                return n;
            FFmpegNativo.Verificar(r, "avcodec_receive_frame");
            try
            {
                int w = _quadro->width, h = _quadro->height;
                var formato = (AVPixelFormat)_quadro->format;
                if (_sws == null || w != _swsLargura || h != _swsAltura || formato != _swsFormato)
                {
                    ffmpeg.sws_freeContext(_sws);
                    _sws = ffmpeg.sws_getContext(w, h, formato, w, h, AVPixelFormat.AV_PIX_FMT_BGRA,
                        (int)SwsFlags.SWS_BILINEAR, null, null, null);
                    (_swsLargura, _swsAltura, _swsFormato) = (w, h, formato);
                }

                byte* destino = obterDestino(w, h, out int stride);
                var dst = new byte_ptrArray4();
                dst[0] = destino;
                var dstStride = new int_array4();
                dstStride[0] = stride;
                ffmpeg.sws_scale(_sws, _quadro->data, _quadro->linesize, 0, h, dst, dstStride);
                n++;
            }
            finally
            {
                ffmpeg.av_frame_unref(_quadro);
            }
        }
    }

    public void Dispose()
    {
        var ctx = _ctx; ffmpeg.avcodec_free_context(&ctx); _ctx = null;
        var q = _quadro; ffmpeg.av_frame_free(&q); _quadro = null;
        var p = _pacote; ffmpeg.av_packet_free(&p); _pacote = null;
        ffmpeg.sws_freeContext(_sws); _sws = null;
    }
}
