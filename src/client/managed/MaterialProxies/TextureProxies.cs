//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Interop;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>
/// baseanimatedtextureproxy.cpp <c>CBaseAnimatedTextureProxy</c>: picks the
/// frame of an animated texture from the time since the animation started.
/// The proxy is bound many times a frame, so the frame comes from absolute
/// time, never from a running count.
/// </summary>
public abstract class AnimatedTextureProxyBase : MaterialProxy
{
    protected int TextureVar = -1;
    protected int FrameNumVar = -1;
    private float _frameRate;
    private bool _wrap;

    public override bool Init(IProxyInit init)
    {
        string textureName = init.Keys.GetString("animatedTextureVar");
        TextureVar = init.FindVar(textureName, false, out bool found);
        if (found)
        {
            string frameName = init.Keys.GetString("animatedTextureFrameNumVar");
            FrameNumVar = init.FindVar(frameName, false, out found);
            if (found)
            {
                _frameRate = init.Keys.GetFloat("animatedTextureFrameRate", 15);
                _wrap = init.Keys.GetInt("animationNoWrap", 0) == 0;
                return true;
            }
        }

        TextureVar = -1;
        FrameNumVar = -1;
        return false;
    }

    /// <summary>When the animation began.</summary>
    protected abstract float StartTime(IProxyBind bind);

    public override void Bind(IProxyBind bind)
    {
        if (bind.Var(TextureVar).Type != (int)MaterialVarType.Texture)
            return;

        int numFrames = bind.Texture(TextureVar, out _);
        if (numFrames <= 0)
            return;

        float startTime = StartTime(bind);
        float deltaTime = bind.CurTime - startTime;
        float prevTime = deltaTime - bind.FrameTime;

        if (deltaTime < 0.0f)
            deltaTime = 0.0f;
        if (prevTime < 0.0f)
            prevTime = 0.0f;

        float frame = _frameRate * deltaTime;
        float prevFrame = _frameRate * prevTime;

        int intFrame = (int)frame % numFrames;
        int intPrevFrame = (int)prevFrame % numFrames;

        // The wrap is reported to AnimationWrapped, which nothing on either
        // hierarchy listens to (C_BaseEntity::TextureAnimationWrapped is empty);
        // only the no-wrap clamp has an effect.
        if (intPrevFrame > intFrame && !_wrap)
            intFrame = numFrames - 1;

        bind.SetInt(FrameNumVar, intFrame);
        bind.RecordTools();
    }
}

/// <summary>animatedtextureproxy.cpp ("AnimatedTexture"): the animation starts at time zero.</summary>
public sealed class AnimatedTextureProxy : AnimatedTextureProxyBase
{
    protected override float StartTime(IProxyBind bind) => 0;
}

/// <summary>
/// The fact the entity-timed animated proxies ask for: the entity's spawn time
/// (stock's <c>GetTextureAnimationStartTime</c>, <c>m_flSpawnTime</c>). The
/// engine block has no field for it, so a bind against an entity is a named
/// not-shipped row and the time answers as stock answered an unresolved bind.
/// </summary>
internal static class EntitySpawnTime
{
    public const string Capability = "material proxy texture-animation start time";

    public const string Reason =
        "an entity's spawn time has no engine-block field (plan_entity.md §3.2); the "
        + "animated-texture proxies keep the frame offset they already had for an "
        + "unresolved bind rather than being given a plausible zero";

    /// <summary>The start time, or -1 when nothing could answer.</summary>
    public static float Ask(IProxyBind bind)
    {
        if (bind.Arg == 0 || !bind.Entity().HasUnknown)
            return -1.0f;
        bind.NotShipped(Capability, Reason);
        return -1.0f;
    }
}

/// <summary>animatedentitytextureproxy.cpp ("AnimatedEntityTexture"): the animation starts when the entity spawned.</summary>
public sealed class AnimatedEntityTextureProxy : AnimatedTextureProxyBase
{
    protected override float StartTime(IProxyBind bind)
    {
        if (bind.Arg == 0)
            return 0.0f;
        float start = EntitySpawnTime.Ask(bind);
        return start >= 0.0f ? start : 0.0f;
    }
}

/// <summary>
/// animatedoffsettextureproxy.cpp ("AnimatedOffsetTexture"): the entity's
/// start time, kept between binds, offsets the animation. An unresolved bind
/// keeps the offset it had.
/// </summary>
public sealed class AnimatedOffsetTextureProxy : AnimatedTextureProxyBase
{
    private float _frameOffset;

    protected override float StartTime(IProxyBind bind) => _frameOffset;

    public override void Bind(IProxyBind bind)
    {
        float start = EntitySpawnTime.Ask(bind);
        if (start >= 0.0f)
            _frameOffset = start;
        base.Bind(bind);
    }

    public override void SaveState(ReloadWriter writer) => writer.Write(_frameOffset);

    public override void RestoreState(ReloadReader reader) => _frameOffset = reader.ReadFloat();
}

/// <summary>AnimateSpecificTextureProxy.cpp ("AnimateSpecificTexture"): animates only while the var holds one named texture.</summary>
public sealed class AnimateSpecificTextureProxy : AnimatedTextureProxyBase
{
    private string _onlyOn = string.Empty;

    protected override float StartTime(IProxyBind bind) => 0;

    public override bool Init(IProxyInit init)
    {
        _onlyOn = init.Keys.GetString("onlyAnimateOnTexture");
        return base.Init(init);
    }

    public override void Bind(IProxyBind bind)
    {
        // FStrEq: the same pointer or V_stricmp.
        bind.Texture(TextureVar, out string? name);
        if (string.Equals(name ?? string.Empty, _onlyOn, StringComparison.OrdinalIgnoreCase))
            base.Bind(bind);
    }
}

/// <summary>
/// toggletextureproxy.cpp <c>CBaseToggleTextureProxy</c> ("ToggleTexture"):
/// the frame is the entity's networked texture frame index.
///
/// <para>
/// That index has no reader in this build (<see cref="IProxyBind.TextureFrameIndex"/>),
/// so a bind against an entity sets nothing, which is what the fork's edited
/// arm did; the proxies route counts it unanswered.
/// </para>
/// </summary>
public sealed class ToggleTextureProxy : MaterialProxy
{
    private int _texture = -1;
    private int _frameNum = -1;
    private bool _wrap;

    /// <summary>Binds that reached the frame index and had nothing to read it from.</summary>
    public int Unanswered { get; private set; }

    public override bool Init(IProxyInit init)
    {
        string textureName = init.Keys.GetString("toggleTextureVar");
        _texture = init.FindVar(textureName, false, out bool found);
        if (!found)
            return false;

        string frameName = init.Keys.GetString("toggleTextureFrameNumVar");
        _frameNum = init.FindVar(frameName, false, out found);
        if (!found)
            return false;

        _wrap = init.Keys.GetInt("toggleShouldWrap", 1) != 0;
        return true;
    }

    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg == 0)
            return;
        if (bind.Var(_texture).Type != (int)MaterialVarType.Texture)
            return;
        int numFrames = bind.Texture(_texture, out string? name);
        if (numFrames < 0 && name is null)
            return;
        if (!bind.Entity().IsEntity)
            return;

        if (!bind.TextureFrameIndex(out int f))
        {
            Unanswered++;
            return;
        }

        int intFrame = f % numFrames;
        if (!_wrap && f > numFrames)
            intFrame = numFrames;
        bind.SetInt(_frameNum, intFrame);
        bind.RecordTools();
    }
}

/// <summary>texturescrollmaterialproxy.cpp ("TextureScroll").</summary>
public sealed class TextureScrollProxy : MaterialProxy
{
    private const double Pi = 3.14159265358979323846;

    private int _scroll = -1;
    private ProxyFloatInput _rate;
    private ProxyFloatInput _angle;
    private ProxyFloatInput _scale;

    public override bool Init(IProxyInit init)
    {
        string scrollName = init.Keys.GetString("textureScrollVar");
        _scroll = init.FindVar(scrollName, false, out bool found);
        if (!found)
            return false;

        // Stock ignores what these answer: a missing var still holds its slot.
        _rate.Init(init, "textureScrollRate", 1.0f);
        _angle.Init(init, "textureScrollAngle", 0.0f);
        _scale.Init(init, "textureScale", 1.0f);
        return true;
    }

    public override void Bind(IProxyBind bind)
    {
        float rate = _rate.Get(bind);
        float angle = _angle.Get(bind);
        float scale = _scale.Get(bind);

        // curtime * cos( angle * ( M_PI / 180.0f ) ) * rate, in double.
        float sOffset = (float)(bind.CurTime * Math.Cos(angle * (Pi / 180.0f)) * rate);
        float tOffset = (float)(bind.CurTime * Math.Sin(angle * (Pi / 180.0f)) * rate);

        if (sOffset < 0.0f)
            sOffset += 1.0f + -(int)sOffset;
        if (tOffset < 0.0f)
            tOffset += 1.0f + -(int)tOffset;

        sOffset -= (int)sOffset;
        tOffset -= (int)tOffset;

        if (bind.Var(_scroll).Type == (int)MaterialVarType.Matrix)
        {
            var mat = new VMatrix();
            mat[0, 0] = scale; mat[0, 3] = sOffset;
            mat[1, 1] = scale; mat[1, 3] = tOffset;
            mat[2, 2] = 1.0f;
            mat[3, 3] = 1.0f;
            bind.SetMatrix(_scroll, mat);
        }
        else
        {
            bind.SetVec3(_scroll, sOffset, tOffset, 0.0f);
        }
        bind.RecordTools();
    }
}
