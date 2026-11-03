using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>One of the engine's video modes (modes.h's vmode_t, read by layout).</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct VideoMode(int Width, int Height, int Bpp, int RefreshRate);

/// <summary>
/// The engine's answers to its GameUI that the video options need
/// (plan_gameui_managed.md G2): the mode list and the desktop's size.
/// </summary>
public interface IFwdGameUIFuncs
{
    /// <summary>The engine's video modes, in its order.</summary>
    VideoMode[] VideoModes();

    /// <summary>The desktop's size, in pixels.</summary>
    (int Width, int Height) DesktopResolution();
}

/// <summary>The video config's mode and whether it is windowed (MaterialSystem_Config_t's head and flags).</summary>
public readonly record struct VideoConfig(int Width, int Height, bool Windowed);

/// <summary>
/// The material system's answers the video options need (plan_gameui_managed.md
/// G2): the current video config, and which antialiasing modes the card does.
/// </summary>
public interface IFwdMaterialSystem
{
    VideoConfig CurrentVideoConfig();

    bool SupportsMsaa(int samples);

    bool SupportsCsaa(int samples, int quality);
}

/// <summary>
/// The offsets of MaterialSystem_Config_t that <see cref="MaterialSystem"/>
/// reads: video_config_layout_test holds them to the C++ declaration, and
/// VideoInterfacesTests pins the same numbers here.
/// </summary>
public static class VideoConfigLayout
{
    public const int Width = 0, Height = 4, Flags = 52;

    /// <summary>MATSYS_VIDCFG_FLAGS_WINDOWED.</summary>
    public const uint WindowedFlag = 1u << 0;
}

/// <summary>IGameUIFuncs through its forwarders (GameUIFuncsFwd, generated). Client only.</summary>
public sealed unsafe class GameUIFuncs : IFwdGameUIFuncs
{
    public const string InterfaceVersion = "VENGINE_GAMEUIFUNCS_VERSION005";

    private readonly void* _self;
    private readonly GameUIFuncsFwd* _fwd;

    private GameUIFuncs(void* self, GameUIFuncsFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>The engine's GameUI funcs, or null with the reason, acquired through the side's factory.</summary>
    internal static IFwdGameUIFuncs? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire<GameUIFuncsFwd>(
               factory, SourceSharpFactoryType.AppSystem,
               InterfaceVersion, out void* self, out GameUIFuncsFwd* table, out why)
            ? new GameUIFuncs(self, table)
            : null;

    /// <inheritdoc/>
    public VideoMode[] VideoModes()
    {
        void* list = null;
        int count = 0;
        _fwd->GetVideoModes(_self, &list, &count);
        if (list is null || count <= 0)
            return [];
        return new ReadOnlySpan<VideoMode>(list, count).ToArray();
    }

    /// <inheritdoc/>
    public (int Width, int Height) DesktopResolution()
    {
        int w = 0, h = 0;
        _fwd->GetDesktopResolution(_self, &w, &h);
        return (w, h);
    }
}

/// <summary>IMaterialSystem through its forwarders (MaterialSystemFwd, generated): the video options' three. Client only.</summary>
public sealed unsafe class MaterialSystem : IFwdMaterialSystem
{
    public const string InterfaceVersion = "VMaterialSystem080";

    private readonly void* _self;
    private readonly MaterialSystemFwd* _fwd;

    private MaterialSystem(void* self, MaterialSystemFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>The material system, or null with the reason, acquired through the side's factory.</summary>
    internal static IFwdMaterialSystem? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire<MaterialSystemFwd>(
               factory, SourceSharpFactoryType.AppSystem,
               InterfaceVersion, out void* self, out MaterialSystemFwd* table, out why)
            ? new MaterialSystem(self, table)
            : null;

    /// <inheritdoc/>
    public VideoConfig CurrentVideoConfig()
    {
        byte* config = (byte*)_fwd->GetCurrentConfigForVideoCard(_self);
        return new VideoConfig(*(int*)(config + VideoConfigLayout.Width), *(int*)(config + VideoConfigLayout.Height),
                               (*(uint*)(config + VideoConfigLayout.Flags) & VideoConfigLayout.WindowedFlag) != 0);
    }

    /// <inheritdoc/>
    public bool SupportsMsaa(int samples) => _fwd->SupportsMSAAMode(_self, samples) != 0;

    /// <inheritdoc/>
    public bool SupportsCsaa(int samples, int quality) => _fwd->SupportsCSAAMode(_self, samples, quality) != 0;
}

/// <summary>ivoicetweak.h's IVoiceTweak, a struct of C function pointers (no vtable): one layout on every ABI.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VoiceTweakTable
{
    public delegate* unmanaged<int> StartVoiceTweakMode;
    public delegate* unmanaged<void> EndVoiceTweakMode;
    public delegate* unmanaged<int, float, void> SetControlFloat;
    public delegate* unmanaged<int, float> GetControlFloat;
    public delegate* unmanaged<byte> IsStillTweaking;
}
