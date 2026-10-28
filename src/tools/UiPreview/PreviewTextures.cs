using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.UiPreview;

/// <summary>Decoded HUD art by handle: what the preview's backends draw a <see cref="UiTexturedQuad"/> with.</summary>
public sealed class PreviewTextures
{
    private readonly Dictionary<int, (byte[] Rgba, int Wide, int Tall)> _byHandle = [];

    /// <summary>Handles start above the icon atlas's ids, so the two never meet.</summary>
    private int _next = 0x10000;

    public (byte[] Rgba, int Wide, int Tall)? Find(int handle) => _byHandle.TryGetValue(handle, out var art) ? art : null;

    public int Add(byte[] rgba, int wide, int tall)
    {
        int handle = _next++;
        _byHandle[handle] = (rgba, wide, tall);
        return handle;
    }
}

/// <summary>
/// The offline HUD art library (user ruling 2026-10-26): the GAME paths a
/// mod mounts (<see cref="ContentFiles"/>, --mod or --content) stand in for
/// the engine's. No content, or a name it cannot load, answers 0 and the
/// panel draws its fallback.
/// </summary>
public sealed class PreviewUiTextureLibrary(SourceSharp.Abi.IEngineFiles? files, PreviewTextures textures) : IUiTextureLibrary
{
    private readonly Dictionary<string, UiTextureInfo?> _byMaterial = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiTextureInfo> _byTexture = new(StringComparer.OrdinalIgnoreCase);

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
        if (files?.ReadGameFile($"materials/{name}.vmt") is not { } vmt)
            return null;
        if (BaseTexture(vmt) is not { } baseTexture)
            return null;
        string vtfPath = $"materials/{baseTexture.Replace('\\', '/')}.vtf";
        if (_byTexture.TryGetValue(vtfPath, out UiTextureInfo shared))
            return shared;
        if (files.ReadGameFileBytes(vtfPath) is not { } bytes || !VtfImage.TryDecode(bytes, out VtfImage? image, out _))
            return null;

        int handle = textures.Add(image!.Rgba, image.Width, image.Height);
        var info = new UiTextureInfo(handle, image.Width, image.Height, image.Format.ToString());
        _byTexture[vtfPath] = info;
        return info;
    }

    /// <summary>The .vmt's <c>$baseTexture</c>. A tool's read: the first quoted value after the name;
    /// the game parses the file properly (the host's <c>UiTextureLibrary</c>, KeyValuesText).</summary>
    private static string? BaseTexture(string vmt)
    {
        int at = vmt.IndexOf("$basetexture", StringComparison.OrdinalIgnoreCase);
        while (at >= 0)
        {
            // The name is written quoted ("$basetexture"): its closing quote,
            // then the value's two. An unquoted name would read one quote off.
            int q0 = vmt.IndexOf('"', at + "$basetexture".Length);
            int q1 = vmt.IndexOf('"', q0 + 1);
            int q2 = vmt.IndexOf('"', q1 + 1);
            if (q0 >= 0 && q1 > q0 && q2 > q1)
                return vmt[(q1 + 1)..q2];
            at = vmt.IndexOf("$basetexture", at + 1, StringComparison.OrdinalIgnoreCase);
        }
        return null;
    }
}
