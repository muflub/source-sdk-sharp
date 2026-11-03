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

    /// <summary>
    /// The named material bound whole — the engine resolves the .vmt and the
    /// draw runs the material's own shader, whatever it is (stock's
    /// DrawSetTextureFile, CHudScope::Init tf_hud_scope.cpp:317-320). This is
    /// the way to draw a shader whose pixels are not a picture to upload — a
    /// Refract quadrant's pixels are the refracted frame buffer. 0 when the
    /// name loads nothing.
    /// </summary>
    int Material(string name);

    /// <summary>What a loaded texture is, or null when the name loads none.</summary>
    UiTextureInfo? About(string name);

    /// <summary>
    /// The named HUD icon — a CIconPanel's <c>"icon"</c> key
    /// (resource/ui/…/scout_red.res) — as the texture handle and the
    /// normalized UV rectangle its pixels make of that texture, or null when
    /// no icon table names it (the screen draws its fallback). Stock resolves
    /// the name through <c>scripts/hud_textures.txt</c> then
    /// <c>scripts/mod_textures.txt</c> (CHud::Init's load order,
    /// hud.cpp:464-465), and the first file to name an icon keeps it
    /// (AddSearchableHudIconToList answers the icon already in the list,
    /// hud.cpp:772-787).
    /// </summary>
    UiTextureIcon? Icon(string name);
}

/// <summary>One loaded texture: its handle, its size, its VTF format.</summary>
public readonly record struct UiTextureInfo(int Handle, int Wide, int Tall, string Format);

/// <summary>One HUD icon: a texture handle, the UV rectangle the icon's
/// x/y/width/height (its table's pixels) make of that texture's size, and
/// that width and height themselves (the whole texture's for an icon with
/// no rect) — stock's EffectiveWidth/Height, which a screen scales keeping
/// the icon's aspect.</summary>
public readonly record struct UiTextureIcon(int Handle, UiRect Uv, int Wide, int Tall);
