using System;

namespace SourceSharp.Abi;

/// <summary>
/// The engine's debug overlay, as far as managed asks it: a line of text at a
/// world point. Both sides resolve <c>VDebugOverlay003</c>; a dedicated server
/// has none.
/// </summary>
public interface IFwdDebugOverlay
{
    /// <summary>
    /// <c>AddTextOverlayRGB( origin, line, duration, r, g, b, a, "%s", text )</c>:
    /// the text is formatted here, never by the engine.
    /// </summary>
    void TextAtPosition(Vector origin, int line, float duration, int r, int g, int b, int a, string text);
}

/// <summary>
/// <see cref="IFwdDebugOverlay"/> over DebugOverlayFwd (2026-10-19), where
/// the DrawWorldText shim slot made the same call through overlay_library's
/// Overlay_TextAtPosition.
/// </summary>
public sealed unsafe class EngineDebugOverlay : IFwdDebugOverlay
{
    public const string InterfaceVersion = "VDebugOverlay003";

    private readonly void* _self;
    private readonly DebugOverlayFwd* _fwd;

    private EngineDebugOverlay(void* self, DebugOverlayFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>The side's overlay, or null with the reason (a dedicated server has none).</summary>
    internal static IFwdDebugOverlay? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire<DebugOverlayFwd>(
               factory, SourceSharpFactoryType.AppSystem, InterfaceVersion,
               out void* self, out DebugOverlayFwd* table, out why)
            ? new EngineDebugOverlay(self, table)
            : null;

    /// <inheritdoc/>
    public void TextAtPosition(Vector origin, int line, float duration, int r, int g, int b, int a, string text)
    {
        fixed (byte* p = Vtable.Utf8(text))
            _fwd->AddTextOverlayRGB_2(_self, &origin, line, duration, r, g, b, a, p);
    }
}
