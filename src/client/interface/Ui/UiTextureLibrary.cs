namespace SourceSharp.Client.Ui;

/// <summary>
/// The HUD art a screen draws with (user ruling 2026-10-26: the TF2 UI gets
/// the full TF2 look, which needs TF2's HUD art). A material name such as
/// <c>hud/objectives_flagpanel_bg_left</c> becomes a texture handle a
/// <see cref="UiTexturedQuad"/> takes. Data only: the reading, decoding and
/// upload live behind this, in the UI host in game and in the preview tool
/// offline. A screen may ask every frame; the answers are cached behind the
/// implementation.
/// </summary>
public interface IUiTextureLibrary
{
    /// <summary>A handle for the named HUD material, or 0 when there is none (a screen draws its fallback).</summary>
    int Find(string name);

    /// <summary>What a loaded texture is, or null when the name loads none.</summary>
    UiTextureInfo? About(string name);
}

/// <summary>One loaded texture: its handle, its size, its VTF format.</summary>
public readonly record struct UiTextureInfo(int Handle, int Wide, int Tall, string Format);
