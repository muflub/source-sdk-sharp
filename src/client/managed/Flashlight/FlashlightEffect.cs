//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Client.Flashlight;

/// <summary>
/// What the flashlight asks of the world and tells it: the traces, the client
/// shadow manager's flashlight calls and texture, the engine's DX level and
/// debug overlay (<see cref="IFlashlightSlots"/>), and the dynamic-light pool
/// the old flashlight lights.
/// </summary>
public interface IFlashlightWorld : IFlashlightSlots
{
    /// <summary>The engine's dynamic light, keyed: asking again moves it.</summary>
    void DynamicLightUpdate(in DynamicLight light);

    /// <summary>Puts the keyed dynamic light out now.</summary>
    void DynamicLightRelease(int key);
}

/// <summary>The ConVars the flashlight reads, by stock's meaning.</summary>
public interface IFlashlightSettings
{
    /// <summary>r_newflashlight: the projected texture (true) or the old dynamic light.</summary>
    bool NewFlashlight { get; }

    /// <summary>r_newflashlight.SetValue( 0 ), which stock does on a card below DX7.</summary>
    void DisableNewFlashlight();

    bool SwingFlashlight { get; }        // r_swingflashlight
    bool LockPosition { get; }           // r_flashlightlockposition
    float Fov { get; }                   // r_flashlightfov
    float OffsetY { get; }               // r_flashlightoffsety
    float Near { get; }                  // r_flashlightnear
    float Far { get; }                   // r_flashlightfar
    float ConstantAtten { get; }         // r_flashlightconstant
    float LinearAtten { get; }           // r_flashlightlinear
    float QuadraticAtten { get; }        // r_flashlightquadratic
    bool VisualizeTrace { get; }         // r_flashlightvisualizetrace
    float Ambient { get; }               // r_flashlightambient
    float ShadowAtten { get; }           // r_flashlightshadowatten
    float LadderDist { get; }            // r_flashlightladderdist
    float SlopeScaleDepthBias { get; }   // mat_slopescaledepthbias_shadowmap (the material system's)
    float DepthBias { get; }             // mat_depthbias_shadowmap (the material system's)
    bool DepthTexture { get; }           // r_flashlightdepthtexture (clientshadowmgr.cpp's)
    int DepthResolution { get; }         // r_flashlightdepthres (clientshadowmgr.cpp's)
}

/// <summary>
/// One player's flashlight: stock's CFlashlightEffect (flashlighteffect.cpp),
/// ported whole. The new flashlight is a projected texture handed to the
/// client shadow manager; the old one (r_newflashlight 0) a dynamic light at
/// the end of a line trace. Every decision is here; the world carries them out.
///
/// <para>
/// Held to stock by FlashlightOracleTests, which replays the recording the
/// pristine file made (Fixtures/flashlight-stock.tsv) bit for bit.
/// </para>
///
/// <para>
/// Divergences, each a part of stock this build has no host for: the tool
/// framework's recording-mode KeyValues messages are not posted (SFM's); the
/// old flashlight's dynamic light is rewritten through the keyed pool every
/// frame rather than kept as a pointer, so render->TouchLight is not called
/// (the pool marks a light it hands out changed); and HL2_EPISODIC's
/// battery flicker is not compiled in this game, as in stock's.
/// </para>
/// </summary>
public sealed class FlashlightEffect
{
    /// <summary>CLIENTSHADOW_INVALID_HANDLE, as the executor answers it.</summary>
    public const int InvalidHandle = -1;

    /// <summary>The texture stock picks where the card supports a border colour.</summary>
    public const string BorderTexture = "effects/flashlight_border";

    /// <summary>The texture stock picks where it does not.</summary>
    public const string PlainTexture = "effects/flashlight001";

    // UpdateLightNew's constants, as stock declares them.
    private const float Epsilon = 0.1f;       // flEpsilon
    private const float DistCutoff = 128.0f;  // flDistCutoff
    private const float DistDrag = 0.2f;      // flDistDrag = 0.2 (a double literal, stored as a float)

    // The old flashlight's light: stock's UpdateLightOld.
    private const float PointLightRadius = 80f;
    private const float PointLightLife = 0.2f;

    private static readonly Vector HullMins = new(-4f, -4f, -4f);
    private static readonly Vector HullMaxs = new(4f, 4f, 4f);

    private readonly IFlashlightWorld _world;
    private readonly IFlashlightSettings _settings;
    private readonly int _texture;
    private bool _pointLightLit;

    public FlashlightEffect(int entIndex, IFlashlightWorld world, IFlashlightSettings settings)
    {
        _world = world;
        _settings = settings;
        EntIndex = entIndex;
        Handle = InvalidHandle;

        // Stock's constructor: a card below DX7 gets the old flashlight.
        if (world.DXSupportLevel() is int dx && dx < 70)
            settings.DisableNewFlashlight();

        TextureName = world.SupportsBorderColor() ? BorderTexture : PlainTexture;
        _texture = world.Texture(TextureName);
    }

    /// <summary>m_nEntIndex: the player it lights for, which keys its dynamic light.</summary>
    public int EntIndex { get; }

    /// <summary>m_bIsOn.</summary>
    public bool IsOn { get; private set; }

    /// <summary>m_FlashlightHandle: the projected texture's shadow handle, or <see cref="InvalidHandle"/>.</summary>
    public int Handle { get; private set; }

    /// <summary>m_flDistMod: how far the light is pulled back from where it would be.</summary>
    public float DistMod { get; private set; }

    /// <summary>The texture the constructor chose.</summary>
    public string TextureName { get; }

    /// <summary>The last state handed to the shadow manager, for the dev API.</summary>
    public FlashlightState? LastState { get; private set; }

    public void TurnOn()
    {
        IsOn = true;
        DistMod = 1.0f;
    }

    public void TurnOff()
    {
        if (IsOn)
        {
            IsOn = false;
            LightOff();
        }
    }

    /// <summary>
    /// UpdateLight: the new flashlight or the old, if it is on. <paramref name="onLadder"/>
    /// is stock's local-player read (MOVETYPE_LADDER), taken by the caller.
    /// </summary>
    public void UpdateLight(Vector vecPos, Vector vecDir, Vector vecRight, Vector vecUp, int distance, bool onLadder)
    {
        if (!IsOn)
            return;
        if (_settings.NewFlashlight)
            UpdateLightNew(vecPos, vecDir, vecRight, vecUp, onLadder);
        else
            UpdateLightOld(vecPos, vecDir, distance);
    }

    /// <summary>The destructor: the light goes out, both kinds.</summary>
    public void Release() => LightOff();

    /// <summary>
    /// Forgets the projected texture without destroying it: at level shutdown the
    /// shadow manager destroys every shadow itself, so a handle kept past it
    /// names nothing.
    /// </summary>
    public void ForgetHandle()
    {
        Handle = InvalidHandle;
        _pointLightLit = false;
    }

    private void UpdateLightNew(Vector vecPos, Vector vecForward, Vector vecRight, Vector vecUp, bool bPlayerOnLadder)
    {
        float flOffsetY = _settings.OffsetY;

        if (_settings.SwingFlashlight)
        {
            // Projects the view direction backwards, raising the vertical offset
            // only when the player is looking down.
            Vector vecSwingLight = vecPos + vecForward * -12.0f;
            if (vecSwingLight.Z > vecPos.Z)
                flOffsetY += vecSwingLight.Z - vecPos.Z;
        }

        Vector vOrigin = vecPos + flOffsetY * vecUp;

        // Not on a ladder: trace a hull to the offset origin.
        if (!bPlayerOnLadder)
        {
            FlashlightTrace pmOriginTrace = _world.TraceHull(vecPos, vOrigin, HullMins, HullMaxs, TraceMask.Solid);
            if (pmOriginTrace.Hit)
                vOrigin = vecPos;
        }
        else
        {
            vOrigin = vecPos;
        }

        Vector vTarget = vecPos + vecForward * _settings.Far;

        Vector vDir = vTarget - vOrigin;
        Vector vRight = vecRight;
        Vector vUp = vecUp;
        MoveVec.Normalize(ref vDir);
        MoveVec.Normalize(ref vRight);
        MoveVec.Normalize(ref vUp);

        // Orthonormalise the basis, which the texture projection requires.
        vUp -= Dot(vDir, vUp) * vDir;
        MoveVec.Normalize(ref vUp);
        vRight -= Dot(vDir, vRight) * vDir;
        MoveVec.Normalize(ref vRight);
        vRight -= Dot(vUp, vRight) * vUp;
        MoveVec.Normalize(ref vRight);

        FlashlightTrace pmDirectionTrace = _world.TraceHull(vOrigin, vTarget, HullMins, HullMaxs, TraceMask.OpaqueAndNpcsWindow);

        if (_settings.VisualizeTrace)
        {
            _world.BoxOverlay(pmDirectionTrace.End, HullMins, HullMaxs, QAngle.Zero, 0, 0, 255, 16, 0f);
            _world.LineOverlay(vOrigin, pmDirectionTrace.End, 255, 0, 0, false, 0f);
        }

        float flDist = Length(pmDirectionTrace.End - vOrigin);
        if (flDist < DistCutoff)
        {
            // Within the cutoff: pull back, and trace to see that there is room.
            float flPullBackDist = bPlayerOnLadder ? _settings.LadderDist : DistCutoff - flDist;
            DistMod = Lerp(DistDrag, DistMod, flPullBackDist);

            if (!bPlayerOnLadder)
            {
                FlashlightTrace pmBackTrace = _world.TraceHull(vOrigin, vOrigin - vDir * (flPullBackDist - Epsilon),
                                                               HullMins, HullMaxs, TraceMask.OpaqueAndNpcsWindow);
                if (pmBackTrace.Hit)
                {
                    float flMaxDist = Length(pmBackTrace.End - vOrigin) - Epsilon;
                    if (DistMod > flMaxDist)
                        DistMod = flMaxDist;
                }
            }
        }
        else
        {
            DistMod = Lerp(DistDrag, DistMod, 0.0f);
        }
        vOrigin = vOrigin - vDir * DistMod;

        System.Numerics.Quaternion q = BasisToQuaternion(vDir, vRight, vUp);
        float fov = _settings.Fov;

        var state = new FlashlightState
        {
            OriginX = vOrigin.X,
            OriginY = vOrigin.Y,
            OriginZ = vOrigin.Z,
            OrientationX = q.X,
            OrientationY = q.Y,
            OrientationZ = q.Z,
            OrientationW = q.W,
            QuadraticAtten = _settings.QuadraticAtten,
            LinearAtten = _settings.LinearAtten,
            HorizontalFovDegrees = fov,
            VerticalFovDegrees = fov,
            ConstantAtten = _settings.ConstantAtten,
            ColorR = 1.0f,
            ColorG = 1.0f,
            ColorB = 1.0f,
            ColorA = _settings.Ambient,
            // Pushed out so the world is not clipped when the light pulls back.
            NearZ = _settings.Near + DistMod,
            FarZ = _settings.Far,
            EnableShadows = _settings.DepthTexture ? 1 : 0,
            ShadowMapResolution = _settings.DepthResolution,
            Texture = _texture,
            TextureFrame = 0,
            ShadowAtten = _settings.ShadowAtten,
            ShadowSlopeScaleDepthBias = _settings.SlopeScaleDepthBias,
            ShadowDepthBias = _settings.DepthBias,
        };
        LastState = state;

        if (Handle == InvalidHandle)
            Handle = _world.Create(in state);
        else if (!_settings.LockPosition)
            _world.Update(Handle, in state);

        _world.Project(Handle);

        // The old flashlight goes out if it was lit.
        LightOffOld();
    }

    private void UpdateLightOld(Vector vecPos, Vector vecDir, int nDistance)
    {
        Vector end = vecPos + nDistance * vecDir;

        // A line outward, skipping the player and the view model.
        FlashlightTrace pm = _world.TraceLine(vecPos, end, TraceMask.All);

        float falloff = pm.Fraction * nDistance;
        if (falloff < 500)
            falloff = 1.0f;
        else
            falloff = (float)(500.0 / falloff);
        falloff *= falloff;

        int brightness = (int)(255 * falloff);
        _world.DynamicLightUpdate(new DynamicLight
        {
            Key = EntIndex,
            OriginX = pm.End.X,
            OriginY = pm.End.Y,
            OriginZ = pm.End.Z,
            DirectionX = vecDir.X,
            DirectionY = vecDir.Y,
            DirectionZ = vecDir.Z,
            Radius = PointLightRadius,
            Red = brightness,
            Green = brightness,
            Blue = brightness,
            Exponent = 0,
            LiveFor = PointLightLife,
        });
        _pointLightLit = true;

        // The new flashlight goes out if it was lit.
        LightOffNew();
    }

    private void LightOffNew()
    {
        if (Handle != InvalidHandle)
        {
            _world.Destroy(Handle);
            Handle = InvalidHandle;
        }
    }

    private void LightOffOld()
    {
        if (_pointLightLit)
        {
            _world.DynamicLightRelease(EntIndex);
            _pointLightLit = false;
        }
    }

    private void LightOff()
    {
        LightOffOld();
        LightOffNew();
    }

    // mathlib's DotProduct, VectorLength (FastSqrt is sqrtf) and Lerp, in stock's order.
    private static float Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static float Length(Vector v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    private static float Lerp(float percent, float a, float b) => a + (b - a) * percent;

    /// <summary>
    /// mathlib_base.cpp's BasisToQuaternion, the version it compiles ("go
    /// through angles"): forward, left (right * -1) and up as the matrix's
    /// columns, MatrixAngles, AngleQuaternion.
    /// </summary>
    internal static System.Numerics.Quaternion BasisToQuaternion(Vector forward, Vector right, Vector up)
    {
        var left = new Vector(right.X * -1.0f, right.Y * -1.0f, right.Z * -1.0f);
        var mat = new BoneMatrix3x4
        {
            Row0 = new System.Numerics.Vector4(forward.X, left.X, up.X, 0f),
            Row1 = new System.Numerics.Vector4(forward.Y, left.Y, up.Y, 0f),
            Row2 = new System.Numerics.Vector4(forward.Z, left.Z, up.Z, 0f),
        };
        return BoneSetupMath.AngleQuaternion(BoneSetupMath.MatrixAngles(in mat));
    }
}
