using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// One material var's state as native read it before a bind
/// (<c>SourceSharpProxyVar_t</c>, sourcesharp.h): the type, the vector size and
/// the three getters stock's proxies call (<c>GetIntValue</c>,
/// <c>GetFloatValue</c>, <c>GetVecValue( v, 4 )</c>). Texture vars carry no
/// texture here: <c>GetTextureValue</c> has side effects (it loads a string
/// var's texture), so a proxy that reads one asks for it by slot, which is
/// what stock did. Held by MaterialProxyAbiParityTests.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ProxyVar
{
    public int Type;
    public int VecSize;
    public int Int;
    public float Float;
    public float V0;
    public float V1;
    public float V2;
    public float V3;

    public readonly float this[int i] => i switch { 0 => V0, 1 => V1, 2 => V2, 3 => V3, _ => 0f };
}

/// <summary>
/// What native hands a bind besides the vars
/// (<c>SourceSharpProxyBindIn_t</c>): the clock as stock's proxies read it off
/// <c>gpGlobals</c>, the frame (for the per-frame bind counts), and the bind
/// argument, which is opaque: a renderable for most materials, a player index
/// for a spray decal's. Managed never dereferences it; it hands it back to the
/// fact reads that do.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ProxyBindIn
{
    public float CurTime;
    public float FrameTime;
    public int MaxClients;
    public int FrameCount;
    public int VarCount;
    public int Reserved;
    public nint Arg;
}

/// <summary>The setter a <see cref="ProxyOp"/> carries out: one per IMaterialVar setter stock's proxies call.</summary>
public enum ProxyOpKind
{
    /// <summary><c>SetFloatValue( Values[0] )</c>.</summary>
    Float = 1,

    /// <summary><c>SetIntValue( Arg )</c>.</summary>
    Int = 2,

    /// <summary><c>SetVecValue( Values, Arg )</c>.</summary>
    VecN = 3,

    /// <summary><c>SetVecValue( Values[0], Values[1], Values[2] )</c>.</summary>
    Vec3 = 4,

    /// <summary><c>SetVecComponentValue( Values[0], Arg )</c>.</summary>
    VecComponent = 5,

    /// <summary><c>SetMatrixValue( Values[0..15] )</c>, row-major as VMatrix stores it.</summary>
    Matrix = 6,

    /// <summary><c>SetTextureValue</c> of the proxy's texture slot <c>Arg</c>.</summary>
    Texture = 7,

    /// <summary><c>ToolFramework_RecordMaterialParams( material )</c> when tools are on. No slot.</summary>
    RecordTools = 8,
}

/// <summary>
/// One setter call a bind asks native to make (<c>SourceSharpProxyOp_t</c>),
/// in the order stock made them. A proxy that reads a var it wrote later in the
/// same bind would see the old value here; none of stock's does.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ProxyOp
{
    public int Kind;
    public int Slot;
    public int Arg;
    public int Reserved;
    public fixed float Values[16];

    /// <summary><see cref="Values"/>[<paramref name="i"/>], for code with no unsafe context.</summary>
    public readonly float Value(int i) => (uint)i < 16u ? Values[i] : 0f;
}

/// <summary>
/// One key of a proxy's KeyValues block as native read it, RAW
/// (<c>SourceSharpProxyKey_t</c>): <c>KeyValues::GetString</c> rewrites a
/// FLOAT or INT key as a STRING in place, so native reads each key only through
/// the accessor that does not convert it and managed replays the conversions
/// (<c>ProxyKeyValues</c>). For a STRING key native adds what the C library
/// reads from it (<c>sscanf "%f"</c>, <c>atof</c>, <c>atoi</c>), so managed
/// never re-implements a C parse.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ProxyKeyRaw
{
    public byte* Name;
    public byte* String;
    public int Type;
    public float Float;
    public int Int;
    public int ScanCount;
    public float ScanValue;
    public float Atof;
    public int Atoi;
    public int Reserved;
}

/// <summary>One key of a proxy's block, out of <see cref="ProxyKeyRaw"/> and into managed strings.</summary>
public readonly record struct ProxyKeyData(string Name, int Type, string String, float Float, int Int,
                                           int ScanCount, float ScanValue, float Atof, int Atoi);

/// <summary>
/// One bind's native buffers: the bind-in, the vars' state (native refreshes a
/// var a texture read changed, so <see cref="Var"/> always reads the buffer)
/// and the setter calls managed asks for. Valid between <see cref="Begin"/> and
/// <see cref="End"/> only, inside the one ProxyBind call; nothing keeps a pointer
/// past it.
/// </summary>
public sealed unsafe class ProxyBindFrame
{
    private ProxyBindIn* _in;
    private ProxyVar* _vars;
    private ProxyOp* _ops;
    private int _maxOps;

    /// <summary>The setter calls asked for so far this bind.</summary>
    public int OpCount { get; private set; }

    /// <summary>Calls past the buffer: a proxy that asked for more than native carries.</summary>
    public int Dropped { get; private set; }

    public bool Active => _in != null;

    public void Begin(ProxyBindIn* input, ProxyVar* vars, ProxyOp* ops, int maxOps)
    {
        _in = input;
        _vars = vars;
        _ops = ops;
        _maxOps = maxOps;
        OpCount = 0;
        Dropped = 0;
    }

    public void End()
    {
        _in = null;
        _vars = null;
        _ops = null;
        _maxOps = 0;
    }

    public ProxyBindIn In => _in != null ? *_in : default;

    public ProxyVar Var(int slot) => _in != null && (uint)slot < (uint)_in->VarCount ? _vars[slot] : default;

    public ProxyOp Op(int i) => (uint)i < (uint)OpCount ? _ops[i] : default;

    public void Add(ProxyOpKind kind, int slot, int arg, ReadOnlySpan<float> values)
    {
        if (OpCount >= _maxOps || _ops == null)
        {
            Dropped++;
            return;
        }
        ProxyOp* op = &_ops[OpCount++];
        op->Kind = (int)kind;
        op->Slot = slot;
        op->Arg = arg;
        op->Reserved = 0;
        for (int i = 0; i < 16; i++)
            op->Values[i] = i < values.Length ? values[i] : 0f;
    }
}

/// <summary>
/// The material proxies as native's trampoline asks them (lane c5-proxies):
/// implemented by the game assembly's <c>MaterialProxyHost</c>.
/// </summary>
public interface IClientMaterialProxies
{
    /// <summary>A proxy the material system made: the name it asked for, its material and its block. False refuses it.</summary>
    bool Init(int handle, string name, string material, IReadOnlyList<ProxyKeyData> keys);

    /// <summary>One bind: reads the frame's vars and adds the setter calls to make.</summary>
    void Bind(int handle, ProxyBindFrame frame);

    void Release(int handle);

    /// <summary>Whether <c>mat_stub</c> asks the view to swap in the stub material system this frame.</summary>
    bool MatStubEnabled();
}

/// <summary>The real one, over IShimMaterialProxyClient.</summary>
internal sealed unsafe class ClientMaterialProxyExecutor : IClientMaterialProxyExecutor
{
    // The shim area this calls, handed over by the client root once the
    // side's areas are attached (plan_shim_areas_split phase 5).
    private readonly MaterialProxyClientArea _materialProxyClient;

    internal ClientMaterialProxyExecutor(MaterialProxyClientArea materialProxyClient)
    {
        _materialProxyClient = materialProxyClient;
    }

    public int FindVar(int handle, string name, bool complain, out bool found)
    {
        found = false;
        byte[] utf8 = Vtable.Utf8(name);
        int f = 0;
        int slot;
        fixed (byte* p = utf8)
            slot = _materialProxyClient.ProxyFindVar(handle, p, complain ? 1 : 0, &f);
        found = f != 0;
        return slot;
    }

    public int FindTexture(int handle, string name, string group, bool complain, out bool isError)
    {
        isError = false;
        byte[] n = Vtable.Utf8(name);
        byte[] g = Vtable.Utf8(group);
        int e = 0;
        int slot;
        fixed (byte* pn = n)
        fixed (byte* pg = g)
            slot = _materialProxyClient.ProxyFindTexture(handle, pn, pg, complain ? 1 : 0, &e);
        isError = e != 0;
        return slot;
    }

    public int VarTexture(int handle, int slot, out string? name)
    {
        name = null;
        byte* buffer = stackalloc byte[260];
        int frames = _materialProxyClient.ProxyVarTexture(handle, slot, buffer, 260);
        if (frames >= 0)
            name = Marshal.PtrToStringUTF8((nint)buffer);
        return frames;
    }

    public ProxyEntityRaw BindEntity(int handle)
    {
        var raw = new ProxyEntityRaw { EntIndex = -1, RenderAlpha = -1, IsNpc = -1 };
        _materialProxyClient.ProxyBindEntity(handle, &raw);
        return raw;
    }

    public bool ReadView(int kind, Span<float> values)
    {
        fixed (float* p = values)
            return _materialProxyClient.ProxyReadView(kind, p, values.Length) != 0;
    }

    public bool PlayerCustomFile(int player, out uint crc)
    {
        crc = 0;
        uint c = 0;
        int ok = _materialProxyClient.ProxyPlayerCustomFile(player, &c);
        crc = c;
        return ok != 0;
    }

    public void NotShipped(string capability, string reason)
    {
        byte[] c = Vtable.Utf8(capability);
        byte[] r = Vtable.Utf8(reason);
        fixed (byte* pc = c)
        fixed (byte* pr = r)
            _materialProxyClient.ProxyNotShipped(pc, pr);
    }
}
