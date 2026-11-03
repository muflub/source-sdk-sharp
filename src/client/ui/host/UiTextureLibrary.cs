using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The HUD art library in game (user ruling 2026-10-26): a material name is
/// <em>bound</em>, not decoded — the surface's <c>DrawSetTextureFile</c>
/// resolves the .vmt and every draw of the handle runs that material's own
/// shader, its $translucent / $alphatest / $vertexalpha included (integrator
/// ruling 2026-10-31; stock's CHudTexture::Precache, hud.cpp). The pixel size
/// comes from the .vmt's $baseTexture VTF header alone, for the icon
/// rectangles that normalise their UVs by it; the pixels are never read here
/// (the offline preview decodes, in <c>src/tools/UiPreview</c>). A name that
/// binds nothing answers 0, said once in the log; a screen can ask every frame.
/// </summary>
public sealed class UiTextureLibrary : IUiTextureLibrary
{
    private readonly IEngineFiles? _files;
    private readonly IUiSurface _surface;
    private readonly ILog _log;

    /// <summary>Per material name, what the surface bound — null cached for the refusals, so they warn once.</summary>
    private readonly Dictionary<string, UiTextureInfo?> _byMaterial = new(StringComparer.Ordinal);

    /// <summary>The icon tables, read once over both files (hud_textures
    /// then mod_textures, hud.cpp:464-465); null until the first ask.</summary>
    private HudIconTable? _icons;

    /// <summary>Per icon name, what resolved — null cached, as a material miss is.</summary>
    private readonly Dictionary<string, UiTextureIcon?> _byIcon = new(StringComparer.Ordinal);

    public UiTextureLibrary(IEngineFiles? files, IUiSurface surface, ILog log) => (_files, _surface, _log) = (files, surface, log);

    public int Find(string name) => Load(name)?.Handle ?? 0;

    public int Material(string name) => Load(name)?.Handle ?? 0;

    public UiTextureInfo? About(string name) => Load(name);

    public UiTextureIcon? Icon(string name)
    {
        if (_byIcon.TryGetValue(name, out UiTextureIcon? cached))
            return cached;
        UiTextureIcon? icon = ReadIcon(name);
        _byIcon[name] = icon;
        return icon;
    }

    /// <summary>The icon's entry binds its file like any material, and the
    /// rect normalizes over that texture's size. A miss is silent:
    /// stock's GetIcon is a plain find answering NULL (hud.cpp:792-799).</summary>
    private UiTextureIcon? ReadIcon(string name)
    {
        if (IconTable()?.Entry(name) is not { } entry)
            return null;
        if (Load(entry.File) is not { } info || info.Wide <= 0 || info.Tall <= 0)
            return null;
        return entry.Width < 0
            ? new UiTextureIcon(info.Handle, new UiRect(0f, 0f, 1f, 1f), info.Wide, info.Tall)
            : new UiTextureIcon(info.Handle, new UiRect(
                entry.X / (float)info.Wide, entry.Y / (float)info.Tall,
                entry.Width / (float)info.Wide, entry.Height / (float)info.Tall), entry.Width, entry.Height);
    }

    private HudIconTable IconTable()
    {
        _icons ??= HudIconTable.Chain(ReadTable("scripts/hud_textures.txt"), ReadTable("scripts/mod_textures.txt"));
        return _icons;
    }

    private HudIconTable ReadTable(string path)
        => _files?.ReadGameFileBytes(path) is { } bytes ? HudIconTable.Parse(bytes) : new HudIconTable();

    private UiTextureInfo? Load(string name)
    {
        if (_byMaterial.TryGetValue(name, out UiTextureInfo? cached))
            return cached;
        UiTextureInfo? info = Bind(name);
        _byMaterial[name] = info;
        return info;
    }

    private UiTextureInfo? Bind(string name)
    {
        int handle = _surface.MaterialTexture(name);
        if (handle == 0)
        {
            _log.Warning($"[SourceSharp/Ui] hud material {name}: the surface bound nothing; the panels draw their fallback\n");
            return null;
        }

        (int Wide, int Tall, string Format) size = Extents(name);
        return new UiTextureInfo(handle, size.Wide, size.Tall, size.Format);
    }

    /// <summary>The bound texture's pixel size, from the .vmt's $baseTexture
    /// VTF header: the icon rectangles normalise their UVs by it. A header
    /// that cannot be read leaves the size zero — the art still draws (the
    /// engine holds the true extent), an icon table entry over it stands down
    /// rather than normalise by zero.</summary>
    private (int Wide, int Tall, string Format) Extents(string name)
    {
        string? vmt = _files?.ReadGameFile($"materials/{name}.vmt");
        if (vmt is null)
            return (0, 0, "");

        string? baseTexture;
        try
        {
            baseTexture = BaseTexture(vmt);
        }
        catch (KeyValuesTextException)
        {
            return (0, 0, "");
        }
        if (baseTexture is null || _files!.ReadGameFileBytes(VtfOf(baseTexture)) is not { } vtf
            || !VtfImage.TryReadExtents(vtf, out int wide, out int tall, out string? format, out _))
            return (0, 0, "");

        return (wide, tall, format ?? "");
    }

    private static string VtfOf(string baseTexture) => $"materials/{baseTexture.Replace('\\', '/')}.vtf";

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
