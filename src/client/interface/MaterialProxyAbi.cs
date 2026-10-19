using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The bind argument resolved as stock's entity proxies resolve it
/// (<c>SourceSharpProxyEntity_t</c>): the renderable's IClientUnknown, its
/// IClientEntity's index and abs origin, and the two engine-block facts the
/// fork's edited proxies read (render alpha, FL_NPC).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ProxyEntityRaw
{
    /// <summary>1 when the renderable answered an IClientUnknown.</summary>
    public int HasUnknown;

    /// <summary>The IClientEntity's index, or -1 when the unknown is not an entity.</summary>
    public int EntIndex;

    public Vector Origin;

    /// <summary>The engine block's render alpha, or -1 when nothing answered.</summary>
    public int RenderAlpha;

    /// <summary>1 or 0 from the flags word, or -1 when nothing answered.</summary>
    public int IsNpc;

    public int Reserved;
}

/// <summary>
/// What a managed proxy reads through the engine, by its handle
/// (IShimMaterialProxyClient): the lookups Init makes, the texture and entity
/// reads a bind makes, and the not-shipped row a refusal raises.
/// </summary>
public interface IClientMaterialProxyExecutor
{
    int FindVar(int handle, string name, bool complain, out bool found);

    int FindTexture(int handle, string name, string group, bool complain, out bool isError);

    /// <summary>The slot's <c>GetTextureValue</c>: its frame count (-1 for none) and name.</summary>
    int VarTexture(int handle, int slot, out string? name);

    ProxyEntityRaw BindEntity(int handle);

    /// <summary>0 the view origin (3 floats), 1 the water LOD params (2), 2 the world's mins and maxs (6).</summary>
    bool ReadView(int kind, Span<float> values);

    bool PlayerCustomFile(int player, out uint crc);

    void NotShipped(string capability, string reason);
}
