using System.Runtime.InteropServices;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Detail;

/// <summary>
/// The detail props as native's client system asks them
/// (shell_detail_props.cpp), each answer one length-prefixed buffer of int32
/// words the service owns and keeps pinned until its next answer
/// (<see cref="DetailPropsWire"/> says each layout).
/// </summary>
public interface IClientDetailProps
{
    void Init();

    nint LevelInitPreEntity(int materialWidth, int materialHeight, bool hdr);

    string LevelInitPostEntity(string? worldMaterial);

    void LevelShutdownPreEntity();

    nint BuildLists(Vector viewOrigin, Vector currentViewOrigin, bool clientModeDraws, int drawDetailProps, int fullbright);

    void BeginTranslucent();

    nint Prepare(int kind, in DetailView view, ReadOnlySpan<int> leaves, Vector? closest, DetailSwitches switches);

    nint Emit(int maxQuads);
}

/// <summary>
/// The int32 layouts native reads (shell_detail_props.cpp keeps the same
/// numbers). Word 0 of every buffer is its length in words; a model is a
/// pointer in two words, low first; a float is its bits.
/// </summary>
public static class DetailPropsWire
{
    /// <summary>LevelInitPreEntity: [flags] [objects] objects × (model lo, model hi, origin xyz, angles xyz) [runs] runs × (leaf, first, count, fast).</summary>
    public const int LevelPrecache = 1;

    /// <summary>BuildLists: [build] [radius bits] [proxies] proxies × (lo, hi) [models] models × (alpha | setAngles &lt;&lt; 8, angles xyz, colour xyz).</summary>
    public const int ModelSetAngles = 1 << 8;

    /// <summary>Prepare: [material] [alphas] alphas × (object, alpha).</summary>
    public const int MaterialNone = 0;

    /// <summary>Emit: commands until <see cref="End"/>.</summary>
    public const int End = 0;

    /// <summary>[Begin] quads.</summary>
    public const int Begin = 1;

    /// <summary>[Vertices] count, then count × (x, y, z, rgba as r | g &lt;&lt; 8 | b &lt;&lt; 16 | a &lt;&lt; 24, s, t).</summary>
    public const int Vertices = 2;

    /// <summary>[EndDraw]: CMeshBuilder::End, then IMesh::Draw.</summary>
    public const int EndDraw = 3;

    /// <summary>[ModelAlpha] object, alpha.</summary>
    public const int ModelAlpha = 4;

    /// <summary>The words one vertex takes.</summary>
    public const int VertexWords = 6;
}

/// <summary>
/// IManagedDetailPropsClient (C7 cluster 10): what shell_detail_props.cpp's
/// client system forwards to, so game/client/detailobjectsystem.cpp leaves
/// client.so. Each body counts its own crossing first; a failure answers what
/// an empty level would (no buffer, no draw).
/// </summary>
public sealed unsafe class DetailPropsClientBinding(Crossings crossings, ILog log, IClientDetailProps props) : IManagedDetailPropsClient
{
    public void DetailInit()
    {
        crossings.Count(CrossingOp.ClientDetailInit);
        try { props.Init(); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedDetailPropsClient.DetailInit", e); }
    }

    public void* DetailLevelInitPreEntity(int a0, int a1, int a2)
    {
        crossings.Count(CrossingOp.ClientDetailLevelInitPreEntity);
        try { return (void*)props.LevelInitPreEntity(a0, a1, a2 != 0); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedDetailPropsClient.DetailLevelInitPreEntity", e); return null; }
    }

    public int DetailLevelInitPostEntity(byte* a0, byte* a1, int a2)
    {
        crossings.Count(CrossingOp.ClientDetailLevelInitPostEntity);
        try
        {
            if (a1 == null || a2 <= 0)
                return -1;
            string? world = a0 == null ? null : Marshal.PtrToStringUTF8((nint)a0);
            return DecalsClientBinding.Write(props.LevelInitPostEntity(world), new Span<byte>(a1, a2));
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedDetailPropsClient.DetailLevelInitPostEntity", e); return -1; }
    }

    public void DetailLevelShutdownPreEntity()
    {
        crossings.Count(CrossingOp.ClientDetailLevelShutdownPreEntity);
        try { props.LevelShutdownPreEntity(); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedDetailPropsClient.DetailLevelShutdownPreEntity", e); }
    }

    public void* DetailBuildLists(void* a0, void* a1, int a2, int a3, int a4)
    {
        crossings.Count(CrossingOp.ClientDetailBuildLists);
        try
        {
            if (a0 == null || a1 == null)
                return null;
            return (void*)props.BuildLists(*(Vector*)a0, *(Vector*)a1, a2 != 0, a3, a4);
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedDetailPropsClient.DetailBuildLists", e); return null; }
    }

    public void DetailBeginTranslucent()
    {
        crossings.Count(CrossingOp.ClientDetailBeginTranslucent);
        try { props.BeginTranslucent(); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedDetailPropsClient.DetailBeginTranslucent", e); }
    }

    public void* DetailPrepare(int a0, void* a1, void* a2, int a3, void* a4, int a5, int a6, int a7)
    {
        crossings.Count(CrossingOp.ClientDetailPrepare);
        try
        {
            if (a1 == null || (a2 == null && a3 > 0))
                return null;
            Vector* v = (Vector*)a1;
            var view = new DetailView(v[0], v[1], v[2], v[3], v[4]);
            var leaves = new ReadOnlySpan<int>(a2, Math.Max(a3, 0));
            Vector? closest = a4 == null ? null : *(Vector*)a4;
            return (void*)props.Prepare(a0, view, leaves, closest, new DetailSwitches(a5, a6 != 0, a7));
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedDetailPropsClient.DetailPrepare", e); return null; }
    }

    public void* DetailEmit(int a0)
    {
        crossings.Count(CrossingOp.ClientDetailEmit);
        try { return (void*)props.Emit(a0); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedDetailPropsClient.DetailEmit", e); return null; }
    }
}
