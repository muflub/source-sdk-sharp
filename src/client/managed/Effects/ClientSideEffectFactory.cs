//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// The client's <c>FX_Add*</c> and tracer helpers (clientsideeffects_test.cpp,
/// which despite its name is where stock keeps them): what each one puts on the
/// effect list, the tracer's random length and width, the player's own tracer,
/// and the near-miss whiz with its shared timer (<c>CBulletWhizTimer</c>).
/// Replayed against stock's recorded calls (ClientEffectListTests).
/// </summary>
public sealed class ClientSideEffectFactory : IFxQuadSink, IFxLineSink, IFxDiscreetLineSink
{
    /// <summary><c>TRACER_TYPE_*</c> (shareddefs.h).</summary>
    public const int TracerTypeDefault = 0x01, TracerTypeGunship = 0x02, TracerTypeStrider = 0x04,
                     TracerTypeGauss = 0x08, TracerTypeWaterBullet = 0x10;

    private const float TracerBaseOffset = 8;
    private const float TracerMaxHearDist = 6 * 12;
    private const float TracerSoundTimeMin = 0.1f;
    private const float TracerSoundTimeMax = 0.1f;
    private const float ListenerHeight = 24;

    private readonly IFxRender _render;
    private readonly ClientEffectList _list;
    private readonly IEngineHost _host;
    private readonly Func<bool> _tracerExtra;

    // CBulletWhizTimer::m_nextWhizTime: the client's one near-miss clock.
    private float _nextWhizTime;

    /// <param name="tracerExtra">Reads <c>tracer_extra</c> each time a discreet line draws.</param>
    public ClientSideEffectFactory(IFxRender render, ClientEffectList list, IEngineHost host, Func<bool> tracerExtra)
    {
        _render = render;
        _list = list;
        _host = host;
        _tracerExtra = tracerExtra;
    }

    /// <summary>When the next near-miss may sound (<c>CBulletWhizTimer::m_nextWhizTime</c>).</summary>
    public float NextWhizTime
    {
        get => _nextWhizTime;
        set => _nextWhizTime = value;
    }

    /// <summary>Near-miss sounds emitted, for the effects route.</summary>
    public int WhizSounds { get; private set; }

    /// <summary>
    /// The last near-miss test that got past the timer: where the listener was
    /// taken to be, the squared distance to the shot and the radius it was held
    /// to. The replay's view of the decision; null before the first.
    /// </summary>
    internal (Vector Listener, float DistanceSqr, float WhizDist)? LastNearMissTest { get; private set; }

    /// <summary><c>CBulletWhizTimer::LevelInitPreEntity</c>.</summary>
    public void ResetWhizTimer() => _nextWhizTime = 0;

    /// <summary><c>FX_AddLine( data )</c>; the line takes its own reference on the data's material.</summary>
    public void AddLine(in FxLineData data) => _list.Add(new FxLine("Line", data, _render));

    /// <summary>
    /// <c>FX_AddLine</c> of data whose material stock found with
    /// <c>materials-&gt;FindMaterial</c> and no reference: the line ends up
    /// holding the one reference it takes, as stock's did.
    /// </summary>
    public void AddLine(FxLineData data, string material)
    {
        int id = _render.MaterialFind(material);
        data.Material = id;
        AddLine(data);
        if (id != 0)
            _render.MaterialRelease(id);
    }

    /// <summary><c>FX_AddStaticLine</c>.</summary>
    public void AddStaticLine(Vector start, Vector end, float scale, float life, string material, FxStaticLineFlags flags)
        => _list.Add(new FxStaticLine("StaticLine", start, end, scale, life, _render.MaterialFind(material), flags));

    /// <summary><c>FX_AddDiscreetLine</c>.</summary>
    public void AddDiscreetLine(Vector start, Vector direction, float velocity, float length, float clipLength,
                                float scale, float life, string material)
        => _list.Add(new FxDiscreetLine("Line", start, direction, velocity, length, clipLength, scale, life,
                                        _render.MaterialFind(material), _tracerExtra));

    /// <summary><c>FX_AddQuad( data )</c>: the data's material reference becomes the quad's.</summary>
    public void AddQuad(in FxQuadData data) => _list.Add(new FxQuad(data, _render));

    /// <summary><c>FX_AddQuad( origin, normal, ... )</c>: builds the data as stock's setters do.</summary>
    public void AddQuad(Vector origin, Vector normal, float startSize, float endSize, float sizeBias,
                        float startAlpha, float endAlpha, float alphaBias, float yaw, float deltaYaw,
                        Vector color, float lifeTime, string material, FxQuadFlags flags)
    {
        var data = new FxQuadData
        {
            LifeTime = 0.0f,
            StartAlpha = startAlpha,
            EndAlpha = endAlpha,
            StartScale = startSize,
            EndScale = endSize,
            Flags = flags,
            Material = _render.MaterialFind(material),
            Normal = normal,
            Origin = origin,
            DieTime = lifeTime,
            Color = color,
            ScaleBias = sizeBias,
            AlphaBias = alphaBias,
            Yaw = yaw,
            DeltaYaw = deltaYaw,
        };
        AddQuad(data);
    }

    /// <summary>
    /// <c>FX_PlayerTracer</c>: the local player's own shot, a short static streak
    /// somewhere along the first half of a long enough shot.
    /// </summary>
    public void PlayerTracer(Vector start, Vector end)
    {
        Vector shotDir = FxMath.Sub(end, start);
        float length = FxMath.Normalize(ref shotDir);

        if (length < 256)
            return;

        Vector dStart = FxMath.MA(start, TracerBaseOffset + _host.RandomFloat(-24.0f, 64.0f), shotDir);
        Vector dEnd = FxMath.MA(dStart, length * _host.RandomFloat(0.1f, 0.6f), shotDir);

        _list.Add(new FxStaticLine("Tracer", dStart, dEnd, _host.RandomFloat(0.5f, 0.75f), 0.01f,
                                   _render.MaterialFind("effects/spark"), FxStaticLineFlags.None));
    }

    /// <summary>
    /// <c>FX_Tracer</c>: a streak running from start to end at the bullet's speed
    /// when the shot is long enough to show one, and the near-miss whiz if asked.
    /// </summary>
    public void Tracer(Vector start, Vector end, int velocity, bool makeWhiz)
    {
        Vector dir = FxMath.Sub(end, start);
        float dist = FxMath.Normalize(ref dir);

        if (dist >= 256)
        {
            float length = _host.RandomFloat(64.0f, 128.0f);
            float life = (dist + length) / velocity;
            AddDiscreetLine(start, dir, velocity, length, dist, _host.RandomFloat(0.75f, 0.9f), life, "effects/spark");
        }

        if (makeWhiz)
            TracerSound(start, end, TracerTypeDefault);
    }

    /// <summary>
    /// <c>FX_TracerSound</c>: the near-miss for a tracer type, when the listener is
    /// close enough to the shot's line and the shared timer allows it.
    /// </summary>
    public void TracerSound(Vector start, Vector end, int tracerType)
    {
        string soundName;
        float whizDist = TracerMaxHearDist;
        float minWhizTime = TracerSoundTimeMin;
        float maxWhizTime = TracerSoundTimeMax;
        Vector listen = _render.MainViewOrigin();

        switch (tracerType)
        {
            case TracerTypeDefault:
            {
                soundName = "Bullets.DefaultNearmiss";
                whizDist = 24;

                // Where along a short vertical ray below the eye the bullet
                // passes closest: stock's IntersectRayWithRay(bullet, listener, s, t)
                // fills its `t` with the LISTENER ray's parameter.
                Vector lower = listen;
                lower.Z -= ListenerHeight;
                FxMath.IntersectRayWithRay(start, end, listen, lower, out _, out float t);
                t = FxMath.Clamp(t, 0f, 1f);
                listen.Z -= t * ListenerHeight;
                break;
            }
            case TracerTypeGunship:
                soundName = "Bullets.GunshipNearmiss";
                break;
            case TracerTypeStrider:
                soundName = "Bullets.StriderNearmiss";
                break;
            case TracerTypeWaterBullet:
                soundName = "Underwater.BulletImpact";
                whizDist = 48;
                minWhizTime = 0.3f;
                maxWhizTime = 0.6f;
                break;
            default:
                return;
        }

        float now = _host.CurrentTime;
        float dt = _nextWhizTime - now;
        if (dt > 0)
            return;

        float vDist = FxMath.DistanceSqrToLineSegment(listen, start, end);
        LastNearMissTest = (listen, vDist, whizDist);
        if (vDist >= (whizDist * whizDist))
            return;

        Vector shotDir = FxMath.Sub(end, start);
        FxMath.Normalize(ref shotDir);
        if (_render.EmitTracerSound(soundName, start, shotDir))
            WhizSounds++;

        _nextWhizTime = now + _host.RandomFloat(minWhizTime, maxWhizTime);
    }
}
