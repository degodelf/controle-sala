using FFmpeg.AutoGen;

namespace ControleSala.Comum;

/// <summary>
/// Codificador H.264 de baixa latência. Tenta, em ordem: QuickSync (Intel), Media Foundation
/// (GPU de qualquer marca) e OpenH264 (software, último recurso).
/// Uso: <see cref="PrepararQuadro"/> → preencher NV12 → <see cref="Codificar"/>.
/// </summary>
public sealed unsafe class CodificadorH264 : IDisposable
{
    public static readonly string[] Ordem = ["h264_qsv", "h264_mf", "libopenh264"];

    public delegate void AoPacote(byte* dados, int tamanho, bool chave);

    AVCodecContext* _ctx;
    AVFrame* _quadro;      // NV12, preenchido por quem chama
    AVFrame* _convertido;  // YUV420P, só para encoders que não aceitam NV12
    SwsContext* _sws;
    AVPacket* _pacote;
    long _ultimoPts = -1;

    public string Nome { get; }
    public int Largura { get; }
    public int Altura { get; }

    readonly int? _gop;

    /// <param name="gop">Quadros entre quadros-chave; 1 = todos os quadros são chave (miniaturas).</param>
    public CodificadorH264(int largura, int altura, int fps, int kbps, string? preferido = null, int? gop = null)
    {
        Largura = largura;
        Altura = altura;
        _gop = gop;
        string[] candidatos = string.IsNullOrEmpty(preferido) || preferido == "auto" ? Ordem : [preferido];
        var erros = new List<string>();
        foreach (var nome in candidatos)
        {
            try
            {
                Abrir(nome, fps, kbps);
                Nome = nome;
                return;
            }
            catch (Exception ex)
            {
                erros.Add($"{nome}: {ex.Message}");
                Liberar();
            }
        }
        throw new InvalidOperationException("Nenhum encoder H.264 funcionou:\n  " + string.Join("\n  ", erros));
    }

    void Abrir(string nome, int fps, int kbps)
    {
        var codec = ffmpeg.avcodec_find_encoder_by_name(nome);
        if (codec == null)
            throw new InvalidOperationException("não existe nesta build do FFmpeg");

        _ctx = ffmpeg.avcodec_alloc_context3(codec);
        _ctx->width = Largura;
        _ctx->height = Altura;
        _ctx->time_base = new AVRational { num = 1, den = 1000 }; // pts em milissegundos
        _ctx->framerate = new AVRational { num = fps, den = 1 };
        bool aceitaNv12 = nome != "libopenh264";
        _ctx->pix_fmt = aceitaNv12 ? AVPixelFormat.AV_PIX_FMT_NV12 : AVPixelFormat.AV_PIX_FMT_YUV420P;
        _ctx->gop_size = _gop ?? fps * 10; // quadro-chave a cada ~10 s; fora isso, só quando alguém pede
        _ctx->max_b_frames = 0;      // B-frames atrasam a saída
        _ctx->bit_rate = kbps * 1000L;
        _ctx->rc_max_rate = kbps * 1000L * 3 / 2;
        _ctx->rc_buffer_size = (int)(kbps * 1000L / 2); // ~0,5 s de folga para picos
        // BT.601 "limitado" — o mesmo que o conversor da GPU gera e o decodificador assume.
        _ctx->colorspace = AVColorSpace.AVCOL_SPC_SMPTE170M;
        _ctx->color_primaries = AVColorPrimaries.AVCOL_PRI_SMPTE170M;
        _ctx->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_SMPTE170M;
        _ctx->color_range = AVColorRange.AVCOL_RANGE_MPEG;
        _ctx->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;

        switch (nome)
        {
            case "h264_qsv":
                Opcao("preset", "veryfast");
                Opcao("async_depth", "1"); // devolve cada quadro assim que fica pronto
                Opcao("forced_idr", "1");  // pedido de quadro-chave vira IDR de verdade
                break;
            case "h264_mf":
                Opcao("hw_encoding", "1");
                Opcao("scenario", "display_remoting");
                break;
            case "libopenh264":
                _ctx->thread_count = Math.Min(Environment.ProcessorCount, 4);
                break;
        }

        FFmpegNativo.Verificar(ffmpeg.avcodec_open2(_ctx, codec, null), $"abrir {nome}");

        _quadro = NovoQuadro(AVPixelFormat.AV_PIX_FMT_NV12);
        if (!aceitaNv12)
        {
            _convertido = NovoQuadro(AVPixelFormat.AV_PIX_FMT_YUV420P);
            _sws = ffmpeg.sws_getContext(Largura, Altura, AVPixelFormat.AV_PIX_FMT_NV12,
                Largura, Altura, AVPixelFormat.AV_PIX_FMT_YUV420P, (int)SwsFlags.SWS_POINT, null, null, null);
        }
        _pacote = ffmpeg.av_packet_alloc();

        // Alguns encoders de hardware abrem sem erro e só falham no primeiro quadro:
        // codifica um quadro preto agora para descobrir isso antes de transmitir.
        var q = PrepararQuadro();
        new Span<byte>(q->data[0], q->linesize[0] * Altura).Fill(16);
        new Span<byte>(q->data[1], q->linesize[1] * Altura / 2).Fill(128);
        Codificar(0, true, static (_, _, _) => { });
    }

    void Opcao(string nome, string valor)
    {
        // Opções que não existem nesta versão do encoder são ignoradas.
        ffmpeg.av_opt_set(_ctx->priv_data, nome, valor, 0);
    }

    AVFrame* NovoQuadro(AVPixelFormat formato)
    {
        var q = ffmpeg.av_frame_alloc();
        q->format = (int)formato;
        q->width = Largura;
        q->height = Altura;
        q->color_range = AVColorRange.AVCOL_RANGE_MPEG;
        q->colorspace = AVColorSpace.AVCOL_SPC_SMPTE170M;
        FFmpegNativo.Verificar(ffmpeg.av_frame_get_buffer(q, 32), "av_frame_get_buffer");
        return q;
    }

    /// <summary>Quadro NV12 a ser preenchido antes de <see cref="Codificar"/>.</summary>
    public AVFrame* PrepararQuadro()
    {
        FFmpegNativo.Verificar(ffmpeg.av_frame_make_writable(_quadro), "av_frame_make_writable");
        return _quadro;
    }

    public void Codificar(long ptsMs, bool forcarChave, AoPacote aoPacote)
    {
        var entrada = _quadro;
        if (_sws != null)
        {
            FFmpegNativo.Verificar(ffmpeg.av_frame_make_writable(_convertido), "av_frame_make_writable");
            FFmpegNativo.Verificar(ffmpeg.sws_scale_frame(_sws, _convertido, _quadro), "sws_scale_frame");
            entrada = _convertido;
        }

        _ultimoPts = Math.Max(ptsMs, _ultimoPts + 1);
        entrada->pts = _ultimoPts;
        entrada->pict_type = forcarChave ? AVPictureType.AV_PICTURE_TYPE_I : AVPictureType.AV_PICTURE_TYPE_NONE;
        FFmpegNativo.Verificar(ffmpeg.avcodec_send_frame(_ctx, entrada), "avcodec_send_frame");

        while (true)
        {
            int r = ffmpeg.avcodec_receive_packet(_ctx, _pacote);
            if (r == ffmpeg.AVERROR(ffmpeg.EAGAIN) || r == ffmpeg.AVERROR_EOF)
                return;
            FFmpegNativo.Verificar(r, "avcodec_receive_packet");
            try
            {
                aoPacote(_pacote->data, _pacote->size, (_pacote->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0);
            }
            finally
            {
                ffmpeg.av_packet_unref(_pacote);
            }
        }
    }

    void Liberar()
    {
        var ctx = _ctx; ffmpeg.avcodec_free_context(&ctx); _ctx = null;
        var q = _quadro; ffmpeg.av_frame_free(&q); _quadro = null;
        var c = _convertido; ffmpeg.av_frame_free(&c); _convertido = null;
        var p = _pacote; ffmpeg.av_packet_free(&p); _pacote = null;
        ffmpeg.sws_freeContext(_sws); _sws = null;
        _ultimoPts = -1;
    }

    public void Dispose() => Liberar();
}
