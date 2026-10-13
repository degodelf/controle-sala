using System.Diagnostics;
using AVFrame = FFmpeg.AutoGen.AVFrame;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ControleSala.Agente;

/// <summary>A captura precisa ser recriada (resolução mudou, tela de bloqueio, UAC...).</summary>
sealed class AcessoPerdidoException() : Exception("A captura de tela foi interrompida pelo Windows.");

/// <summary>
/// Captura o monitor principal com a Desktop Duplication API. Cada imagem nova é copiada (GPU → GPU) para uma
/// textura própria e o quadro do Windows é devolvido na hora; depois, cada <see cref="SaidaNv12"/> converte essa
/// cópia para NV12 no tamanho que precisa (tela cheia, miniatura...), também na GPU.
/// </summary>
sealed class CapturaDxgi : IDisposable
{
    readonly IDXGIOutputDuplication _duplicacao;

    internal ID3D11Device Device { get; }
    internal ID3D11DeviceContext Contexto { get; }
    internal ID3D11VideoDevice VideoDevice { get; }
    internal ID3D11VideoContext VideoContexto { get; }
    /// <summary>Última imagem da área de trabalho (BGRA), no tamanho real da tela.</summary>
    internal ID3D11Texture2D Copia { get; }
    internal int LarguraTela { get; }
    internal int AlturaTela { get; }

    /// <summary>Tamanho da transmissão em tela cheia (sempre par, exigência do NV12).</summary>
    public int Largura { get; }
    public int Altura { get; }
    public string Adaptador { get; }
    public bool TemImagem { get; private set; }
    /// <summary>Momento (Stopwatch) em que a imagem atual foi capturada.</summary>
    public long MomentoCaptura { get; private set; }

    public CapturaDxgi()
    {
        using var fabrica = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var (adaptador, saida) = EncontrarMonitorPrincipal(fabrica);
        using (adaptador)
        using (saida)
        {
            Adaptador = adaptador.Description1.Description;
            D3D11.D3D11CreateDevice(adaptador, DriverType.Unknown,
                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
                out ID3D11Device device, out ID3D11DeviceContext contexto).CheckError();
            Device = device;
            Contexto = contexto;

            var area = saida.Description.DesktopCoordinates;
            LarguraTela = area.Right - area.Left;
            AlturaTela = area.Bottom - area.Top;
            Largura = LarguraTela & ~1;
            Altura = AlturaTela & ~1;

            using var saida1 = saida.QueryInterface<IDXGIOutput1>();
            _duplicacao = saida1.DuplicateOutput(Device);

            VideoDevice = Device.QueryInterface<ID3D11VideoDevice>();
            VideoContexto = Contexto.QueryInterface<ID3D11VideoContext>();

            Copia = Device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)LarguraTela,
                Height = (uint)AlturaTela,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            });
        }
    }

    static (IDXGIAdapter1, IDXGIOutput) EncontrarMonitorPrincipal(IDXGIFactory1 fabrica)
    {
        (IDXGIAdapter1, IDXGIOutput)? primeiro = null;
        for (uint a = 0; fabrica.EnumAdapters1(a, out var adaptador).Success; a++)
        {
            bool usado = false;
            for (uint o = 0; adaptador.EnumOutputs(o, out var saida).Success; o++)
            {
                var d = saida.Description;
                if (d.AttachedToDesktop && d.DesktopCoordinates.Left == 0 && d.DesktopCoordinates.Top == 0)
                {
                    if (primeiro is var (pa, ps)) { pa.Dispose(); ps.Dispose(); }
                    return (adaptador, saida);
                }
                if (d.AttachedToDesktop && primeiro == null)
                {
                    primeiro = (adaptador, saida);
                    usado = true;
                }
                else
                {
                    saida.Dispose();
                }
            }
            if (!usado) adaptador.Dispose();
        }
        return primeiro ?? throw new InvalidOperationException("Nenhum monitor ligado à área de trabalho foi encontrado.");
    }

    /// <summary>
    /// Espera até <paramref name="timeoutMs"/> por uma tela nova e, se veio, guarda a imagem em <see cref="Copia"/>.
    /// Retorna false se nada mudou na tela (ou só o mouse mexeu).
    /// </summary>
    public bool Capturar(int timeoutMs)
    {
        var r = _duplicacao.AcquireNextFrame((uint)timeoutMs, out var info, out var recurso);
        if (r == Vortice.DXGI.ResultCode.WaitTimeout)
            return false;
        if (r == Vortice.DXGI.ResultCode.AccessLost)
            throw new AcessoPerdidoException();
        r.CheckError();

        // O quadro do Windows é liberado logo depois da cópia: enquanto ele fica "preso" aqui,
        // o Windows não atualiza a tela capturada e o FPS cai.
        try
        {
            // LastPresentTime == 0: só o ponteiro do mouse mudou, a imagem é a mesma.
            if (info.LastPresentTime == 0 && TemImagem)
                return false;

            using var textura = recurso!.QueryInterface<ID3D11Texture2D>();
            Contexto.CopyResource(Copia, textura);
        }
        finally
        {
            recurso?.Dispose();
            _duplicacao.ReleaseFrame();
        }

        TemImagem = true;
        MomentoCaptura = Stopwatch.GetTimestamp();
        return true;
    }

    /// <summary>Cria uma saída NV12 (conversão + redimensionamento na GPU) do tamanho pedido.</summary>
    public SaidaNv12 CriarSaida(int largura, int altura, bool qualidade) => new(this, largura, altura, qualidade);

    public void Dispose()
    {
        Copia.Dispose();
        VideoContexto.Dispose();
        VideoDevice.Dispose();
        _duplicacao.Dispose();
        Contexto.Dispose();
        Device.Dispose();
    }
}

/// <summary>Converte a última imagem capturada para NV12 num tamanho fixo e permite ler o resultado na CPU.</summary>
sealed unsafe class SaidaNv12 : IDisposable
{
    readonly CapturaDxgi _dono;
    readonly ID3D11VideoProcessorEnumerator _vpEnum;
    readonly ID3D11VideoProcessor _vp;
    readonly ID3D11Texture2D _nv12;
    readonly ID3D11VideoProcessorOutputView _vpSaida;
    readonly ID3D11VideoProcessorInputView _vpEntrada;
    readonly ID3D11Texture2D _copiaCpu;

    public int Largura { get; }
    public int Altura { get; }

    internal SaidaNv12(CapturaDxgi dono, int largura, int altura, bool qualidade)
    {
        _dono = dono;
        Largura = largura & ~1;
        Altura = altura & ~1;

        var conteudo = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputFrameRate = new Rational(60, 1),
            InputWidth = (uint)dono.LarguraTela,
            InputHeight = (uint)dono.AlturaTela,
            OutputFrameRate = new Rational(60, 1),
            OutputWidth = (uint)Largura,
            OutputHeight = (uint)Altura,
            Usage = qualidade ? VideoUsage.OptimalQuality : VideoUsage.OptimalSpeed,
        };
        _vpEnum = dono.VideoDevice.CreateVideoProcessorEnumerator(conteudo);
        _vp = dono.VideoDevice.CreateVideoProcessor(_vpEnum, 0);
        dono.VideoContexto.VideoProcessorSetStreamFrameFormat(_vp, 0, VideoFrameFormat.Progressive);
        dono.VideoContexto.VideoProcessorSetStreamAutoProcessingMode(_vp, 0, false);

        var desc = new Texture2DDescription
        {
            Width = (uint)Largura,
            Height = (uint)Altura,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.NV12,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
        };
        _nv12 = dono.Device.CreateTexture2D(desc);
        _vpSaida = dono.VideoDevice.CreateVideoProcessorOutputView(_nv12, _vpEnum,
            new VideoProcessorOutputViewDescription { ViewDimension = VideoProcessorOutputViewDimension.Texture2D });
        _vpEntrada = dono.VideoDevice.CreateVideoProcessorInputView(dono.Copia, _vpEnum,
            new VideoProcessorInputViewDescription { ViewDimension = VideoProcessorInputViewDimension.Texture2D });

        desc.Usage = ResourceUsage.Staging;
        desc.BindFlags = BindFlags.None;
        desc.CPUAccessFlags = CpuAccessFlags.Read;
        _copiaCpu = dono.Device.CreateTexture2D(desc);
    }

    /// <summary>Converte/redimensiona a imagem capturada mais recente para NV12 (na GPU).</summary>
    public void Atualizar() =>
        _dono.VideoContexto.VideoProcessorBlt(_vp, _vpSaida, 0, 1,
            [new VideoProcessorStream { Enable = true, InputSurface = _vpEntrada }]);

    /// <summary>Copia o NV12 da GPU para o quadro do encoder.</summary>
    public void CopiarPara(AVFrame* quadro)
    {
        _dono.Contexto.CopyResource(_copiaCpu, _nv12);
        var mapa = _dono.Contexto.Map(_copiaCpu, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var origem = (byte*)mapa.DataPointer;
            int pitch = (int)mapa.RowPitch;
            // NV12: plano Y (Altura linhas) seguido do plano UV intercalado (Altura/2 linhas).
            CopiarPlano(origem, pitch, quadro->data[0], quadro->linesize[0], Largura, Altura);
            CopiarPlano(origem + (long)pitch * Altura, pitch, quadro->data[1], quadro->linesize[1], Largura, Altura / 2);
        }
        finally
        {
            _dono.Contexto.Unmap(_copiaCpu, 0);
        }
    }

    static void CopiarPlano(byte* origem, int pitchOrigem, byte* destino, int pitchDestino, int bytesPorLinha, int linhas)
    {
        if (pitchOrigem == pitchDestino)
        {
            Buffer.MemoryCopy(origem, destino, (long)pitchDestino * linhas, (long)pitchOrigem * linhas);
            return;
        }
        for (int y = 0; y < linhas; y++)
            Buffer.MemoryCopy(origem + (long)y * pitchOrigem, destino + (long)y * pitchDestino, pitchDestino, bytesPorLinha);
    }

    public void Dispose()
    {
        _copiaCpu.Dispose();
        _vpEntrada.Dispose();
        _vpSaida.Dispose();
        _nv12.Dispose();
        _vp.Dispose();
        _vpEnum.Dispose();
    }
}
