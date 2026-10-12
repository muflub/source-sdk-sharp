//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>
/// proxyentity.cpp <c>CEntityMaterialProxy</c>: a bind whose argument resolves
/// to an <c>IClientEntity</c> runs the entity arm and records the material for
/// the tools; anything else (no argument, a renderable that is not an entity)
/// runs nothing.
/// </summary>
public abstract class EntityMaterialProxy : MaterialProxy
{
    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg == 0)
            return;
        ProxyEntity entity = bind.Entity();
        if (!entity.IsEntity)
            return;
        BindEntity(bind, entity);
        bind.RecordTools();
    }

    protected abstract void BindEntity(IProxyBind bind, ProxyEntity entity);
}

/// <summary>entityoriginmaterialproxy.cpp ("EntityOrigin", and "EntityOriginAlyx" 15 units to -x).</summary>
public sealed class EntityOriginProxy : EntityMaterialProxy
{
    private readonly float _xOffset;
    private int _origin = -1;

    public EntityOriginProxy(float xOffset) => _xOffset = xOffset;

    public override bool Init(IProxyInit init)
    {
        _origin = init.FindVar("$entityorigin", true, out bool found);
        return found;
    }

    protected override void BindEntity(IProxyBind bind, ProxyEntity entity)
    {
        Vector o = entity.Origin;
        bind.SetVec3(_origin, _xOffset == 0 ? o.X : o.X - _xOffset, o.Y, o.Z);
    }
}

/// <summary>entityoriginmaterialproxy.cpp <c>CEp1IntroVortRefractMaterialProxy</c> ("Ep1IntroVortRefract").</summary>
public sealed class Ep1IntroVortRefractProxy : EntityMaterialProxy
{
    private int _refract = -1;

    public override bool Init(IProxyInit init)
    {
        _refract = init.FindVar("$refractamount", true, out bool found);
        return found;
    }

    protected override void BindEntity(IProxyBind bind, ProxyEntity entity)
    {
        float tmp = 1.0f - bind.Var(_refract).Float;
        tmp *= tmp;
        tmp *= tmp;
        tmp = (1.0f - tmp) * 0.25f;
        bind.SetFloat(_refract, tmp);
    }
}

/// <summary>
/// alphamaterialproxy.cpp ("Alpha"): the entity's render alpha into
/// <c>$alpha</c>. The alpha is the engine block's <c>RenderColorA</c>; a bind
/// with nothing to answer leaves the var as the material declared it.
/// </summary>
public sealed class AlphaProxy : EntityMaterialProxy
{
    private int _alpha = -1;

    public override bool Init(IProxyInit init)
    {
        _alpha = init.FindVar("$alpha", false, out bool found);
        return found;
    }

    protected override void BindEntity(IProxyBind bind, ProxyEntity entity)
    {
        if (_alpha >= 0 && entity.RenderAlpha >= 0)
            bind.SetFloat(_alpha, entity.RenderAlpha);
    }
}

/// <summary>
/// IsNPCProxy.cpp ("IsNPC"): the scale when the bound entity carries FL_NPC,
/// zero when it does not, nothing when the argument resolves to nothing.
/// </summary>
public sealed class IsNpcProxy : ResultProxy
{
    private ProxyFloatInput _factor;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        return _factor.Init(init, "scale", 1);
    }

    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg == 0)
            return;
        int isNpc = bind.Entity().IsNpc;
        if (isNpc < 0)
            return;
        SetFloatResult(bind, isNpc != 0 ? _factor.Get(bind) : 0.0f);
        bind.RecordTools();
    }
}

/// <summary>
/// ProxyHealth.cpp ("Health"): the entity's health fraction times the scale.
/// Health is managed game state with no engine-block field and no interface on
/// this boundary, so the ask is a declared refusal (the not-shipped row) and
/// the result is left as the .vmt declared it.
/// </summary>
public sealed class HealthProxy : ResultProxy
{
    public const string Capability = "material proxy \"Health\"";

    public const string Reason =
        "an entity's health fraction is managed game state with no engine-block field "
        + "and no interface on this boundary (plan_entity.md §3.2, §6); the material's "
        + "$result is left as the .vmt declared it rather than given a plausible constant";

    private ProxyFloatInput _factor;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        return _factor.Init(init, "scale", 1);
    }

    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg == 0)
            return;

        float fraction = -1.0f;
        if (bind.Entity().HasUnknown)
            bind.NotShipped(Capability, Reason);

        if (fraction >= 0.0f)
            SetFloatResult(bind, fraction * _factor.Get(bind));
        bind.RecordTools();
    }
}

/// <summary>
/// lampbeamproxy.cpp ("lampbeam") and lamphaloproxy.cpp ("lamphalo"): fade a
/// lamp's card out (the beam) or in (the halo) by how steeply the viewer looks
/// at the lamp. Both compute in double where stock's literals are double.
/// </summary>
public sealed class LampProxy : EntityMaterialProxy
{
    private readonly bool _halo;
    private int _fade = -1;

    public LampProxy(bool halo) => _halo = halo;

    public override bool Init(IProxyInit init)
    {
        _fade = init.FindVar("$alpha", true, out bool found);
        return found;
    }

    protected override void BindEntity(IProxyBind bind, ProxyEntity entity)
    {
        if (_fade < 0)
            return;

        Vector local = entity.Origin - bind.ViewOrigin();
        MoveVec.Normalize(ref local);
        float z = MathF.Abs(local.Z);

        float fade;
        if (!_halo)
        {
            fade = (float)(1.0 - z);
        }
        else if (z < 0.25)
        {
            fade = 0.0f;
        }
        else
        {
            // MIN( (fade - 0.25) * 1.35, 1.0f ): a double against a float.
            double scaled = (z - 0.25) * 1.35;
            fade = (float)(scaled < 1.0f ? scaled : 1.0f);
        }
        bind.SetFloat(_fade, fade);
    }
}

/// <summary>
/// proxypupil.cpp ("Pupil"): dilates an eye's pupil with the lighting at the
/// entity's eye, rate-limited between binds through <c>$lighting</c>.
/// </summary>
public sealed class PupilProxy : EntityMaterialProxy
{
    private const float DefaultOpenRate = 0.03f;
    private const float DefaultCloseRate = 0.1f;

    private readonly ILog _log;
    private int _texture = -1;
    private int _frameNum = -1;
    private int _lighting = -1;
    private ProxyFloatInput _closeRate;
    private ProxyFloatInput _openRate;

    public PupilProxy(ILog log) => _log = log;

    /// <summary>Binds against an entity that had no eye lighting to read.</summary>
    public int Unanswered { get; private set; }

    public override bool Init(IProxyInit init)
    {
        string textureName = init.Keys.GetString("TextureVar");
        _texture = init.FindVar(textureName, false, out bool found);
        if (!found)
            return false;

        string frameName = init.Keys.GetString("TextureFrameNumVar");
        _frameNum = init.FindVar(frameName, false, out found);
        if (!found)
            return false;

        _lighting = init.FindVar("$lighting", false, out found);
        if (!found)
        {
            _log.Warning("Materials using the pupil proxy must have a field called $lighting which has a value of 0.5!\n");
            return false;
        }

        _closeRate.Init(init, "PupilCloseRate", DefaultCloseRate);
        _openRate.Init(init, "PupilOpenRate", DefaultOpenRate);
        return true;
    }

    protected override void BindEntity(IProxyBind bind, ProxyEntity entity)
    {
        if (_texture < 0)
            return;
        if (bind.Var(_texture).Type != (int)MaterialVarType.Texture)
            return;

        int frameCount = bind.Texture(_texture, out _);

        if (!bind.EyeLighting(out _, out _, out Vector color))
        {
            Unanswered++;
            return;
        }

        float intensity = (float)((0.299f * color.X + 0.587f * color.Y + 0.114f * color.Z) * 0.5);
        intensity = Math.Clamp(intensity, 0f, 1f);
        float last = bind.Var(_lighting).Float;
        if (intensity > last)
        {
            float maxChange = _closeRate.Get(bind) * bind.FrameTime;
            if (intensity > maxChange + last)
                intensity = last + maxChange;
        }
        else
        {
            float maxChange = _openRate.Get(bind) * bind.FrameTime;
            if (intensity < last - maxChange)
                intensity = last - maxChange;
        }

        int frame = (int)(frameCount * intensity);
        frame = Math.Clamp(frame, 0, frameCount - 1);

        bind.SetInt(_frameNum, frame);
        bind.SetFloat(_lighting, intensity);
    }
}

/// <summary>
/// camomaterialproxy.cpp ("Camo"): stock's Init answers false before it reads
/// anything ("hack! Need to make sure that the TGA loader has a valid
/// filesystem"), so no material ever gets one and its procedural texture code
/// is unreachable in stock itself.
/// </summary>
public sealed class CamoProxy : MaterialProxy
{
    public override bool Init(IProxyInit init) => false;

    public override void Bind(IProxyBind bind)
    {
    }
}
