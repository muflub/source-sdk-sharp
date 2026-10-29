using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The HUD art library in game (user ruling 2026-10-26): a material name
/// becomes a texture the surface holds. The .vmt on the engine's GAME paths
/// names its <c>$baseTexture</c>; that .vtf decodes to mip 0 RGBA
/// (<see cref="VtfImage"/>) and uploads once. A name that loads nothing
/// answers 0, said once in the log; a screen can ask every frame.
/// </summary>
public sealed class UiTextureLibrary : IUiTextureLibrary
{
    private readonly IEngineFiles? _files;
    private readonly IUiSurface _surface;
    private readonly ILog _log;

    /// <summary>Per material name, what loaded — null cached for the refusals, so they warn once.</summary>
    private readonly Dictionary<string, UiTextureInfo?> _byMaterial = new(StringComparer.Ordinal);

    /// <summary>Per .vtf path, the uploaded texture: two materials on one image upload it once.</summary>
    private readonly Dictionary<string, UiTextureInfo> _byTexture = new(StringComparer.OrdinalIgnoreCase);

    public UiTextureLibrary(IEngineFiles? files, IUiSurface surface, ILog log) => (_files, _surface, _log) = (files, surface, log);

    public int Find(string name) => Load(name)?.Handle ?? 0;

    public UiTextureInfo? About(string name) => Load(name);

    private UiTextureInfo? Load(string name)
    {
        if (_byMaterial.TryGetValue(name, out UiTextureInfo? cached))
            return cached;
        UiTextureInfo? info = Read(name);
        _byMaterial[name] = info;
        return info;
    }

    private UiTextureInfo? Read(string name)
    {
        if (_files is null)
            return Refuse(name, "no engine file reads on this client");
        string vmtPath = $"materials/{name}.vmt";
        string? vmt = _files.ReadGameFile(vmtPath);
        if (vmt is null)
            return Refuse(name, $"{vmtPath} is on no GAME path");

        string? baseTexture;
        try
        {
            baseTexture = BaseTexture(vmt);
        }
        catch (KeyValuesTextException e)
        {
            return Refuse(name, $"{vmtPath} does not parse: {e.Message}");
        }
        if (baseTexture is null)
            return Refuse(name, $"{vmtPath} names no $baseTexture");

        string vtfPath = $"materials/{baseTexture.Replace('\\', '/')}.vtf";
        if (_byTexture.TryGetValue(vtfPath, out UiTextureInfo shared))
            return shared;
        byte[]? vtf = _files.ReadGameFileBytes(vtfPath);
        if (vtf is null)
            return Refuse(name, $"{vtfPath} is on no GAME path");
        if (!VtfImage.TryDecode(vtf, out VtfImage? image, out string why))
            return Refuse(name, $"{vtfPath}: {why}");

        int handle = _surface.TextureCreate();
        _surface.TextureUpload(handle, image!.Rgba, image.Width, image.Height);
        var info = new UiTextureInfo(handle, image.Width, image.Height, image.Format.ToString());
        _byTexture[vtfPath] = info;
        return info;
    }

    private UiTextureInfo? Refuse(string name, string reason)
    {
        _log.Warning($"[SourceSharp/Ui] hud texture {name}: {reason}; the panels draw their fallback\n");
        return null;
    }

    /// <summary>The .vmt's <c>$baseTexture</c>, wherever in the file's blocks it sits (shaders nest their params).</summary>
    private static string? BaseTexture(string vmt)
    {
        var root = KeyValuesText.Parse(vmt);
        return FindValue(root);
    }

    private static string? FindValue(KeyValuesText node)
    {
        if (node.Find("$baseTexture") is { } hit && !hit.IsBlock)
            return hit.AsString();
        foreach (var child in node.Children)
            if (FindValue(child) is { } found)
                return found;
        return null;
    }
}
