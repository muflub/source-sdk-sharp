//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.MaterialProxies;

/// <summary><c>MaterialVarType_t</c>.</summary>
public enum MaterialVarType
{
    Float = 0,
    String = 1,
    Vector = 2,
    Texture = 3,
    Int = 4,
    FourCC = 5,
    Undefined = 6,
    Matrix = 7,
    Material = 8,
}

/// <summary>
/// What a proxy's Init can ask of the material it is on: the material system's
/// <c>IMaterial::FindVar</c> and <c>IMaterialSystem::FindTexture</c>. Native
/// keeps what they answer (the var and texture pointers) and hands managed a
/// slot number for each, in the order asked.
/// </summary>
public interface IProxyInit
{
    string MaterialName { get; }

    ProxyKeyValues Keys { get; }

    /// <summary><c>IMaterial::FindVar( name, &amp;found, complain )</c>: the var's slot, or -1 for none.</summary>
    int FindVar(string name, bool complain, out bool found);

    /// <summary>
    /// <c>materials-&gt;FindTexture( name, group, complain )</c>: the texture's
    /// slot (native holds a reference for the proxy's life), or -1 for NULL;
    /// <paramref name="isError"/> is <c>IsErrorTexture</c>.
    /// </summary>
    int FindTexture(string name, string group, bool complain, out bool isError);
}

/// <summary>
/// The bind argument resolved as stock resolves it: <c>GetIClientUnknown</c>,
/// then <c>GetIClientEntity</c>, and the engine block's two facts the edited
/// fork proxies read.
/// </summary>
public readonly record struct ProxyEntity(bool HasArg, bool HasUnknown, int EntIndex, Vector Origin, int RenderAlpha, int IsNpc)
{
    /// <summary>An IClientEntity answered (stock's CEntityMaterialProxy arm).</summary>
    public bool IsEntity => EntIndex >= 0;

    public static readonly ProxyEntity None = new(false, false, -1, Vector.Zero, -1, -1);
}

/// <summary>
/// One bind, as a proxy sees it: the clock, the bind argument, its vars' state
/// before the bind, the reads stock made through engine interfaces, and the
/// setter calls it makes, in order. Native makes the setter calls after the
/// proxy returns, so a read never sees this bind's write (no stock proxy reads
/// what it wrote).
/// </summary>
public interface IProxyBind
{
    float CurTime { get; }

    float FrameTime { get; }

    int MaxClients { get; }

    /// <summary>The raw bind argument: a renderable for most materials, a player index for a spray decal's.</summary>
    nint Arg { get; }

    /// <summary>The var in <paramref name="slot"/> as it stood before this bind.</summary>
    ProxyVar Var(int slot);

    /// <summary>
    /// <c>var-&gt;GetTextureValue()</c> and its <c>GetNumAnimationFrames</c>:
    /// -1 frames and a null name for a NULL texture. A read with stock's side
    /// effect (a string var's texture is loaded and the var becomes a texture).
    /// </summary>
    int Texture(int slot, out string? name);

    /// <summary>The bind argument as a renderable (never call it for a player index).</summary>
    ProxyEntity Entity();

    /// <summary><c>CurrentViewOrigin()</c>.</summary>
    Vector ViewOrigin();

    /// <summary><c>view-&gt;GetWaterLODParams</c>.</summary>
    void WaterLod(out float start, out float end);

    /// <summary><c>GetClientWorldEntity()</c>'s <c>GetWorldMins/Maxs</c>; false with no world.</summary>
    bool WorldBounds(out Vector mins, out Vector maxs);

    /// <summary>
    /// The particle manager's directional light and model-view matrix
    /// (<c>CParticleMgr::GetDirectionalLightInfo</c>, <c>GetModelView</c>):
    /// what a lit particle material's bind reads off the manager its bind
    /// argument is.
    /// </summary>
    ParticleBindLight ParticleLight();

    /// <summary><c>engine-&gt;GetPlayerInfo</c>: false when it fails; else the player's <c>customFiles[0]</c>.</summary>
    bool PlayerCustomFile(int player, out uint crc);

    /// <summary>
    /// <c>materials-&gt;FindTexture( name, group, complain )</c> during a bind:
    /// the texture's slot (native holds a reference until the proxy goes), or
    /// -1 for NULL.
    /// </summary>
    int FindTexture(string name, string group, bool complain);

    /// <summary><c>random-&gt;RandomFloat</c>: the engine's uniform stream.</summary>
    float RandomFloat(float lo, float hi);

    /// <summary><c>randomgaussian-&gt;RandomFloat</c>.</summary>
    float RandomGaussian(float mean, float stdDev);

    /// <summary>Reaching a path this build does not ship (the client's not-shipped ledger).</summary>
    void NotShipped(string capability, string reason);

    /// <summary>
    /// The bound entity's <c>m_iTextureFrameIndex</c> (stock's
    /// <c>GetTextureFrameIndex</c>), false when nothing can answer: the engine
    /// block has no word for it and no managed class declares it
    /// (ContractBaseline's <c>key|texframeindex</c> row), so in this build it
    /// never answers.
    /// </summary>
    bool TextureFrameIndex(out int frame);

    /// <summary>
    /// What the pupil proxy reads off the bound entity: its <c>EyePosition</c>,
    /// its forward vector (<c>GetVectors</c>) and
    /// <c>engine-&gt;ComputeLighting</c> there. Actor concepts no interface on
    /// this boundary carries (igameentity.h's verdict), so in this build it
    /// never answers.
    /// </summary>
    bool EyeLighting(out Vector eye, out Vector forward, out Vector lighting);

    void SetFloat(int slot, float value);

    void SetInt(int slot, int value);

    /// <summary><c>SetVecValue( v, n )</c>.</summary>
    void SetVec(int slot, ReadOnlySpan<float> values, int count);

    /// <summary><c>SetVecValue( x, y, z )</c>, a different setter from the array one.</summary>
    void SetVec3(int slot, float x, float y, float z);

    void SetVecComponent(int slot, float value, int component);

    void SetMatrix(int slot, in VMatrix matrix);

    void SetTexture(int slot, int textureSlot);

    /// <summary><c>if ( ToolsEnabled() ) ToolFramework_RecordMaterialParams( GetMaterial() )</c>.</summary>
    void RecordTools();
}

/// <summary>
/// A material proxy's behaviour: what stock's <c>IMaterialProxy</c>
/// subclass decided in Init and OnBind. One instance per proxy the material
/// system made (native's trampoline holds the engine's object and asks this).
/// </summary>
public abstract class MaterialProxy
{
    /// <summary>Stock's <c>Init</c>: false refuses the proxy (the material system then releases it).</summary>
    public abstract bool Init(IProxyInit init);

    /// <summary>Stock's <c>OnBind</c>.</summary>
    public abstract void Bind(IProxyBind bind);

    /// <summary>State a bind changes that a reload must carry (the frame offset of AnimatedOffsetTexture, PlayerLogo's spray cache).</summary>
    public virtual void SaveState(ReloadWriter writer)
    {
    }

    public virtual void RestoreState(ReloadReader reader)
    {
    }
}

/// <summary>
/// A var name with an optional <c>[n]</c> component, as <c>CResultProxy::Init</c>
/// and <c>CFloatInput::Init</c> split it: copied into a 256-byte buffer
/// (<c>Q_strncpy</c>), cut at the first '[', and the component read with
/// <c>strtol( after, NULL, 10 )</c>.
/// </summary>
public static class ProxyVarName
{
    public static string Split(string spec, out int component)
    {
        if (spec.IndexOf('[') < 0)
        {
            component = -1;
            return spec;
        }

        string copy = spec.Length > 255 ? spec[..255] : spec;
        int bracket = copy.IndexOf('[');
        if (bracket < 0)
        {
            // A '[' past the 255 characters the copy keeps: stock writes
            // through the NULL strchr returns. No var name is that long.
            component = -1;
            return copy;
        }
        component = StrToL(copy.AsSpan(bracket + 1));
        return copy[..bracket];
    }

    /// <summary><c>strtol( s, NULL, 10 )</c>, clamped as glibc clamps, narrowed to int as stock assigns it.</summary>
    public static int StrToL(ReadOnlySpan<char> s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r')))
            i++;
        bool negative = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            negative = s[i] == '-';
            i++;
        }
        long value = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            value = value * 10 + (s[i] - '0');
            if (value > long.MaxValue / 10)
                break;
            i++;
        }
        return unchecked((int)(negative ? -value : value));
    }
}

/// <summary>
/// <c>CFloatInput</c> (functionproxy.cpp): a number from the block, or a var
/// (with an optional component) read at each bind.
/// </summary>
public struct ProxyFloatInput
{
    private float _value;
    private int _var;
    private int _component;

    /// <summary><c>CFloatInput::Init</c>. A var that is not found still holds its slot (stock keeps the pointer it got) and answers false.</summary>
    public bool Init(IProxyInit init, string keyName, float defaultValue = 0f)
    {
        _var = -1;
        ProxyKey? section = init.Keys.Find(keyName);
        if (section is null)
        {
            _value = defaultValue;
            return true;
        }

        if (section.Type != ProxyKeyType.String)
        {
            _value = section.GetFloat();
            return true;
        }

        string varName = section.GetString();
        if (section.ScanCount == 1)
        {
            _value = section.ScanValue;
            return true;
        }

        varName = ProxyVarName.Split(varName, out _component);
        _var = init.FindVar(varName, true, out bool found);
        return found;
    }

    /// <summary><c>CFloatInput::GetFloat</c>.</summary>
    public readonly float Get(IProxyBind bind)
    {
        if (_var < 0)
            return _value;

        ProxyVar v = bind.Var(_var);
        if (_component < 0)
            return v.Float;
        if (_component >= v.VecSize)
            return 0f;
        return v[_component];
    }
}

/// <summary><c>CResultProxy</c>: a result var with an optional component.</summary>
public abstract class ResultProxy : MaterialProxy
{
    protected int Result = -1;
    protected int ResultComponent = -1;

    public override bool Init(IProxyInit init)
    {
        string result = init.Keys.GetString("resultVar");
        result = ProxyVarName.Split(result, out ResultComponent);
        Result = init.FindVar(result, true, out bool found);
        return found;
    }

    /// <summary><c>CResultProxy::SetFloatResult</c>.</summary>
    protected void SetFloatResult(IProxyBind bind, float result)
    {
        ProxyVar r = bind.Var(Result);
        if (r.Type == (int)MaterialVarType.Vector)
        {
            if (ResultComponent >= 0)
            {
                bind.SetVecComponent(Result, result, ResultComponent);
            }
            else
            {
                Span<float> v = stackalloc float[4];
                int vecSize = r.VecSize;
                for (int i = 0; i < vecSize && i < 4; ++i)
                    v[i] = result;
                bind.SetVec(Result, v, vecSize);
            }
        }
        else
        {
            bind.SetFloat(Result, result);
        }
    }
}

/// <summary><c>CFunctionProxy</c>: one or two sources and a result.</summary>
public abstract class FunctionProxy : ResultProxy
{
    protected int Src1 = -1;
    protected int Src2 = -1;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;

        string src1 = init.Keys.GetString("srcVar1");
        Src1 = init.FindVar(src1, true, out bool found);
        if (!found)
            return false;

        string src2 = init.Keys.GetString("srcVar2");
        if (src2.Length > 0)
        {
            Src2 = init.FindVar(src2, true, out found);
            if (!found)
                return false;
        }
        else
        {
            Src2 = -1;
        }
        return true;
    }

    /// <summary><c>CFunctionProxy::ComputeResultType</c>. The size is unset (0) where stock leaves it uninitialised and never reads it.</summary>
    protected MaterialVarType ComputeResultType(IProxyBind bind, out int vecSize)
    {
        vecSize = 0;
        ProxyVar r = bind.Var(Result);
        var resultType = (MaterialVarType)r.Type;
        if (resultType == MaterialVarType.Vector)
        {
            if (ResultComponent >= 0)
                resultType = MaterialVarType.Float;
            vecSize = r.VecSize;
        }
        else if (resultType == MaterialVarType.Undefined)
        {
            ProxyVar s1 = bind.Var(Src1);
            resultType = (MaterialVarType)s1.Type;
            if (resultType == MaterialVarType.Vector)
            {
                vecSize = s1.VecSize;
            }
            else if (resultType == MaterialVarType.Undefined && Src2 >= 0)
            {
                ProxyVar s2 = bind.Var(Src2);
                resultType = (MaterialVarType)s2.Type;
                if (resultType == MaterialVarType.Vector)
                    vecSize = s2.VecSize;
            }
        }
        return resultType;
    }
}
