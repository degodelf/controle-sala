using System.Runtime.InteropServices;

namespace ControleSala.Agente;

/// <summary>Liga/desliga o mudo do alto-falante padrão do PC do aluno, pela API de áudio do Windows (Core Audio).</summary>
static class Audio
{
    public static void DefinirMudo(bool mudo)
    {
        var enumerador = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        enumerador.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out IMMDevice dispositivo);
        var iid = typeof(IAudioEndpointVolume).GUID;
        dispositivo.Activate(ref iid, 23 /* CLSCTX_ALL */, 0, out object obj);
        var volume = (IAudioEndpointVolume)obj;
        var vazio = Guid.Empty;
        volume.SetMute(mudo, ref vazio);
        Marshal.ReleaseComObject(volume);
        Marshal.ReleaseComObject(dispositivo);
        Marshal.ReleaseComObject(enumerador);
    }

    enum EDataFlow { Render = 0 }
    enum ERole { Console = 0 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int _EnumAudioEndpoints(); // slot 0, não usado
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow fluxo, ERole papel, out IMMDevice dispositivo);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint contexto, nint parametros,
            [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int _RegisterControlChangeNotify();   // 0
        [PreserveSig] int _UnregisterControlChangeNotify(); // 1
        [PreserveSig] int _GetChannelCount();               // 2
        [PreserveSig] int _SetMasterVolumeLevel();          // 3
        [PreserveSig] int _SetMasterVolumeLevelScalar();    // 4
        [PreserveSig] int _GetMasterVolumeLevel();          // 5
        [PreserveSig] int _GetMasterVolumeLevelScalar();    // 6
        [PreserveSig] int _SetChannelVolumeLevel();         // 7
        [PreserveSig] int _SetChannelVolumeLevelScalar();   // 8
        [PreserveSig] int _GetChannelVolumeLevel();         // 9
        [PreserveSig] int _GetChannelVolumeLevelScalar();   // 10
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mudo, ref Guid contexto); // 11
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mudo);                // 12
    }
}
