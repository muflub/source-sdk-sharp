//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_beam (stock CEnvBeam, EnvBeam.cpp): a beam strung between two named
/// entities, in one of two modes behind one classname.
///
/// <para>
/// With a life of zero and no ring it IS a beam (ServerSide): a beam
/// from its start to its end, kept up to date every tick, burning what it
/// crosses every tenth of a second and telling the map what touches it.
/// With a life it STRIKES: temp-entity beams on a timer (StrikeTime, or a
/// random share of it), between two entities, an entity and a point, round a
/// ring, or zapping at random off the walls in its radius when it has no end.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvBeam)]
public sealed class EnvBeam : BeamEntity
{
    public EnvBeam(IGameClock clock, IEngineHost host, IEngineDamage damage, IEngineEffects effects,
                   IServerDecals decals, IEngineWorldQuery world, IEngineEntityRender render,
                   IEnginePrecache precache, ITempEnts temps)
        : base(clock, host, damage, effects, decals, world, render)
    {
        Precache = precache;
        Temps = temps;
    }

    private readonly IEnginePrecache Precache;
    private readonly ITempEnts Temps;

    /// <summary>What a map may set on this entity. See <see cref="EnvBeamVars"/>.</summary>
    [ScriptVars] public EnvBeamVars Vars = new();

    public readonly Output OnTouchedByEntity = new();

    private const string StrikeContext = "Strike";

    /// <summary>The Touch_t values.</summary>
    public enum TouchType { None = 0, PlayerOnly = 1, NpcOnly = 2, PlayerOrNpc = 3, PlayerOrNpcOrPhysicsProp = 4 }

    [SaveField] private bool _active;
    private int _sprite = -1;

    // The ends ServerSide follows (BEAM_ENTPOINT): an entity, or a point.
    private EntityHandle _start = EntityHandle.None, _end = EntityHandle.None;
    private Vector _startPoint, _endPoint;
    private bool _resolved;

    /// <summary>ServerSide(): a beam that stays, rather than one that strikes.</summary>
    private bool ServerSide => Vars.Life == 0f && !Flags.HasFlag(SpawnFlag.Ring);

    protected override string DecalName => Vars.DecalName;

    public override void OnSpawn()
    {
        if (Vars.Sprite.Length == 0)
        {
            // SetThink( SUB_Remove ): a beam with nothing to draw goes.
            Remove();
            return;
        }

        // Precache: the material name with its .vmt, whatever the map wrote.
        string sprite = Vars.Sprite.Contains(".vmt", StringComparison.OrdinalIgnoreCase)
            ? Vars.Sprite
            : System.IO.Path.ChangeExtension(Vars.Sprite, ".vmt");
        _sprite = Precache.PrecacheModel(sprite);

        Vars.Noise = Math.Min(MaxNoiseAmplitude, Vars.Noise);
        Vars.ScrollRate = Math.Clamp(Vars.ScrollRate, 0, (int)MaxScrollSpeed);

        if (ServerSide)
        {
            if (!CreateBeam() || Beam is null)
                return;

            // Check for tapering; BeamUpdateVars' look.
            float width = Math.Min(MaxWidth, Vars.Width);
            Beam.SetLook(_sprite, width, Flags.HasFlag(SpawnFlag.TaperOut) ? 0f : width, Vars.Noise, Vars.ScrollRate,
                         (byte)Vars.Colour.X, (byte)Vars.Colour.Y, (byte)Vars.Colour.Z, (byte)Vars.Brightness);
            Beam.SetFrame(Vars.FrameStart);
            int flags = Flags.HasFlag(SpawnFlag.ShadeIn) ? BeamNetFlags.ShadeIn
                      : Flags.HasFlag(SpawnFlag.ShadeOut) ? BeamNetFlags.ShadeOut
                      : 0;
            Beam.SetEffects(flags, 0f, 0, 0f, BeamVars.HdrColorScale);

            FireTime = Clock.Now;
            SetNextThink(0f);

            // A named beam not started on waits for an input.
            if (Name.Length > 0)
            {
                if (!Flags.HasFlag(SpawnFlag.StartOn))
                {
                    Beam.Visible = false;
                    _active = false;
                    StopThinking();
                }
                else
                {
                    _active = true;
                    Beam.Visible = true;
                }
            }
            else
            {
                Beam.Visible = true;
            }
        }
        else
        {
            _active = false;
            if (Name.Length == 0 || Flags.HasFlag(SpawnFlag.StartOn))
                SetContextThink(StrikeContext, 1f);
        }
    }

    //-------------------------------------------------------------------------
    // The inputs.
    //-------------------------------------------------------------------------

    [ScriptedEvent("TurnOn")]
    private void OnTurnOn()
    {
        if (!_active)
            TurnOn();
    }

    [ScriptedEvent("TurnOff")]
    private void OnTurnOff()
    {
        if (_active)
            TurnOff();
    }

    [ScriptedEvent("Toggle")]
    private void OnToggle()
    {
        if (_active)
            TurnOff();
        else
            TurnOn();
    }

    /// <summary>InputStrikeOnce: one strike, whatever the interval refiring is doing.</summary>
    [ScriptedEvent("StrikeOnce")]
    private void OnStrikeOnce() => Strike();

    private void TurnOn()
    {
        _active = true;
        if (ServerSide)
        {
            if (Beam is not null)
                Beam.Visible = true;
            Resolve();
            DoSparks(_startPoint, _endPoint);
            FireTime = Clock.Now;
            SetNextThink(0f);
        }
        else
        {
            SetContextThink(StrikeContext, 0f);
        }
    }

    private void TurnOff()
    {
        _active = false;
        if (ServerSide && Beam is not null)
            Beam.Visible = false;

        StopThinking();
        StopThinking(StrikeContext);
    }

    //-------------------------------------------------------------------------
    // The beam that stays (ServerSide).
    //-------------------------------------------------------------------------

    /// <summary>
    /// BeamUpdateVars' ends, from Activate: each a static point (its origin,
    /// once) or an entity the beam follows.
    /// </summary>
    private void Resolve()
    {
        if (_resolved)
        {
            Follow();
            return;
        }

        EntityHandle start = Entities.FindByName(Vars.StartEntity);
        EntityHandle end = Entities.FindByName(Vars.EndEntity);
        if (!Entities.IsValid(start) || !Entities.IsValid(end))
            return;

        _resolved = true;
        _start = IsStaticPoint(start) ? EntityHandle.None : start;
        _end = IsStaticPoint(end) ? EntityHandle.None : end;
        _startPoint = Entities.GetOrigin(start);
        _endPoint = Entities.GetOrigin(end);

        // SetAbsStartPos on a static point start moves the entity's own
        // origin there: stock's env_beam IS its beam.
        if (_start == EntityHandle.None && !Origin.Equals(_startPoint))
            Origin = _startPoint;

        Follow();
    }

    /// <summary>RelinkBeam: the ends where the entities now are.</summary>
    private void Follow()
    {
        if (_start != EntityHandle.None && Entities.IsValid(_start))
            _startPoint = Entities.GetOrigin(_start);
        if (_end != EntityHandle.None && Entities.IsValid(_end))
            _endPoint = Entities.GetOrigin(_end);

        Beam?.SetPoints(_startPoint, _endPoint);
    }

    /// <summary>
    /// UpdateThink: the ends kept up to date; damage every tenth of a second;
    /// and the touch trace, which, when something it wants touches the beam,
    /// fires OnTouchedByEntity and stops the beam updating.
    /// </summary>
    public override void OnThink()
    {
        Resolve();
        if (!_resolved)
        {
            SetNextThink(0f);
            return;
        }

        // Apply damage every 1/10th of a second.
        if (BeamVars.Damage > 0f && Clock.Now >= FireTime + 0.1f)
        {
            var trace = World.TraceLine(_startPoint, _endPoint, TraceMask.Solid);
            BeamDamage(trace, _startPoint);
        }

        if (Vars.Touch != TouchType.None && TouchedBy() is { } toucher && toucher != EntityHandle.None)
        {
            OnTouchedByEntity.Fire(toucher);
            return;
        }

        SetNextThink(0f);
    }

    /// <summary>
    /// The touch trace: an entities-only ray from end to end (stock's
    /// CTraceFilterPlayersNPCs[PhysicsProps], TRACE_ENTITIES_ONLY), the
    /// nearest that passes the touch filters. The world does not stop it.
    /// </summary>
    private EntityHandle? TouchedBy()
    {
        Vector delta = _endPoint - _startPoint;
        float length = delta.Length;
        if (length <= 0f)
            return null;

        Vector mid = _startPoint + delta * 0.5f;
        Span<EntityHandle> near = stackalloc EntityHandle[64];
        int count = World.EntitiesInSphere(mid, length * 0.5f + NearSlack, near);

        EntityHandle best = EntityHandle.None;
        float bestT = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            EntityHandle candidate = near[i];
            if (candidate == Handle || !Wanted(candidate))
                continue;

            if (RayHitsBox(_startPoint, delta, candidate, out float t) && t < bestT)
            {
                bestT = t;
                best = candidate;
            }
        }

        return best != EntityHandle.None && PassesTouchFilters(best) ? best : null;
    }

    /// <summary>How far past the segment an entity's origin may be and still have a box across it.</summary>
    private const float NearSlack = 128f;

    /// <summary>The trace filter's classes: players, NPCs, and prop_physics for the fifth touch type.</summary>
    private bool Wanted(EntityHandle entity)
        => Entities.IsPlayer(entity)
           || (Vars.Touch == TouchType.PlayerOrNpcOrPhysicsProp && Entities.GetClassName(entity) == EntityNames.PropPhysics);

    /// <summary>The ray against the entity's box in the world, as a fraction along it.</summary>
    private bool RayHitsBox(Vector start, Vector delta, EntityHandle entity, out float t)
    {
        var (mins, maxs) = Entities.GetBounds(entity);
        Vector origin = Entities.GetOrigin(entity);
        var basis = Angles.Vectors(Entities.GetAngles(entity));

        // The world box of the turned one.
        Vector lo = new(float.MaxValue, float.MaxValue, float.MaxValue), hi = new(float.MinValue, float.MinValue, float.MinValue);
        for (int c = 0; c < 8; c++)
        {
            float x = (c & 1) != 0 ? maxs.X : mins.X, y = (c & 2) != 0 ? maxs.Y : mins.Y, z = (c & 4) != 0 ? maxs.Z : mins.Z;
            Vector p = origin + basis.Forward * x - basis.Right * y + basis.Up * z;
            lo = new Vector(MathF.Min(lo.X, p.X), MathF.Min(lo.Y, p.Y), MathF.Min(lo.Z, p.Z));
            hi = new Vector(MathF.Max(hi.X, p.X), MathF.Max(hi.Y, p.Y), MathF.Max(hi.Z, p.Z));
        }

        return SlabIntersect(start, delta, lo, hi, out t);
    }

    /// <summary>A segment start + t·delta, t in [0, 1], against an axis-aligned box: the entry fraction.</summary>
    internal static bool SlabIntersect(Vector start, Vector delta, Vector lo, Vector hi, out float t)
    {
        float tMin = 0f, tMax = 1f;
        for (int axis = 0; axis < 3; axis++)
        {
            float s = axis == 0 ? start.X : axis == 1 ? start.Y : start.Z;
            float d = axis == 0 ? delta.X : axis == 1 ? delta.Y : delta.Z;
            float l = axis == 0 ? lo.X : axis == 1 ? lo.Y : lo.Z;
            float h = axis == 0 ? hi.X : axis == 1 ? hi.Y : hi.Z;

            if (MathF.Abs(d) < 1e-6f)
            {
                if (s < l || s > h)
                {
                    t = 0f;
                    return false;
                }

                continue;
            }

            float t1 = (l - s) / d, t2 = (h - s) / d;
            if (t1 > t2)
                (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax)
            {
                t = 0f;
                return false;
            }
        }

        t = tMin;
        return true;
    }

    /// <summary>PassesTouchFilters: the touch type's kinds, then the named filter.</summary>
    private bool PassesTouchFilters(EntityHandle other)
    {
        bool passed = false;
        bool player = Entities.IsPlayer(other);

        // Touched some player or NPC! (No NPCs in this game.)
        if (Vars.Touch != TouchType.NpcOnly && player)
            passed = true;

        if (Vars.Touch == TouchType.PlayerOrNpcOrPhysicsProp && Entities.GetClassName(other) == EntityNames.PropPhysics)
            passed = true;

        if (!passed)
            return false;

        var filter = FindByName<FilterBase>(Vars.FilterName);
        return filter is null || filter.Passes(other);
    }

    //-------------------------------------------------------------------------
    // The beam that strikes.
    //-------------------------------------------------------------------------

    public override void OnThink(string context)
    {
        if (context == StrikeContext)
            StrikeThink();
    }

    /// <summary>StrikeThink: the next strike booked, then this one.</summary>
    private void StrikeThink()
    {
        if (Vars.Life != 0f)
        {
            float wait = Flags.HasFlag(SpawnFlag.Random)
                ? Vars.Life + Host.RandomFloat(0f, Vars.StrikeTime)
                : Vars.Life + Vars.StrikeTime;
            SetContextThink(StrikeContext, wait);
        }

        _active = true;

        if (Vars.EndEntity.Length == 0)
        {
            if (Vars.StartEntity.Length == 0)
            {
                RandomArea();
            }
            else
            {
                EntityHandle start = RandomTargetname(Vars.StartEntity);
                if (start != EntityHandle.None)
                    RandomPoint(Entities.GetOrigin(start));
                else
                    Log.Msg($"env_beam: unknown entity \"{Vars.StartEntity}\"\n");
            }

            return;
        }

        Strike();
    }

    private TeBeamLook Look(float endWidth)
        => new(_sprite, 0, Vars.FrameStart, 0, Vars.Life, Vars.Width, endWidth, 0, Vars.Noise,
               (int)Vars.Colour.X, (int)Vars.Colour.Y, (int)Vars.Colour.Z, Vars.Brightness, Vars.ScrollRate);

    /// <summary>Strike: once, for its life, between a random pick of each end.</summary>
    private void Strike()
    {
        EntityHandle start = RandomTargetname(Vars.StartEntity);
        EntityHandle end = RandomTargetname(Vars.EndEntity);
        if (start == EntityHandle.None || end == EntityHandle.None)
            return;

        Vars.ScrollRate = Math.Clamp(Vars.ScrollRate, 0, (int)MaxScrollSpeed);

        bool pointStart = IsStaticPoint(start), pointEnd = IsStaticPoint(end);
        Vector startAt = Entities.GetOrigin(start), endAt = Entities.GetOrigin(end);

        if (pointStart || pointEnd)
        {
            // don't work
            if (Flags.HasFlag(SpawnFlag.Ring))
                return;

            Temps.BeamEntPoint(TeFilters.Broadcast, 0f,
                               pointStart ? 0 : Entities.GetIndex(start), pointStart ? startAt : Vector.Zero,
                               pointEnd ? 0 : Entities.GetIndex(end), pointEnd ? endAt : Vector.Zero,
                               Look(Vars.Width));
        }
        else if (Flags.HasFlag(SpawnFlag.Ring))
        {
            Temps.BeamRing(TeFilters.Broadcast, 0f, Entities.GetIndex(start), Entities.GetIndex(end), Look(Vars.Width));
        }
        else
        {
            Temps.BeamEnts(TeFilters.Broadcast, 0f, Entities.GetIndex(start), Entities.GetIndex(end), Look(Vars.Width));
        }

        DoSparks(startAt, endAt);

        if (BeamVars.Damage > 0f)
        {
            var trace = World.TraceLine(startAt, endAt, TraceMask.Solid);
            BeamDamageInstant(trace, startAt);
        }
    }

    /// <summary>Zap: a struck beam between two points, and its sparks.</summary>
    private void Zap(Vector from, Vector to)
    {
        Temps.BeamPoints(TeFilters.Broadcast, 0f, from, to, Look(Vars.Width));
        DoSparks(from, to);
    }

    private Vector RandomDirection()
        => new Vector(Host.RandomFloat(-1f, 1f), Host.RandomFloat(-1f, 1f), Host.RandomFloat(-1f, 1f)).Normalized;

    /// <summary>
    /// RandomArea: ten tries to find two walls within the radius, on opposite
    /// sides and in sight of each other, and a zap between them.
    /// </summary>
    private void RandomArea()
    {
        for (int loops = 0; loops < 10; loops++)
        {
            Vector src = Origin;

            Vector dir1 = RandomDirection();
            var tr1 = World.TraceLine(src, src + dir1 * Vars.Radius, TraceMask.Brushes, Handle);
            if (tr1.Fraction == 1f)
                continue;

            Vector dir2;
            do
            {
                dir2 = new Vector(Host.RandomFloat(-1f, 1f), Host.RandomFloat(-1f, 1f), Host.RandomFloat(-1f, 1f));
            }
            while (dir1.Dot(dir2) > 0f);
            dir2 = dir2.Normalized;

            var tr2 = World.TraceLine(src, src + dir2 * Vars.Radius, TraceMask.Brushes, Handle);
            if (tr2.Fraction == 1f)
                continue;

            if ((tr1.End - tr2.End).Length < Vars.Radius * 0.1f)
                continue;

            var between = World.TraceLine(tr1.End, tr2.End, TraceMask.Brushes, Handle);
            if (between.Fraction != 1f)
                continue;

            Zap(tr1.End, tr2.End);
            break;
        }
    }

    /// <summary>RandomPoint: ten tries to find a wall within the radius of a point, and a zap to it.</summary>
    private void RandomPoint(Vector src)
    {
        for (int loops = 0; loops < 10; loops++)
        {
            Vector dir1 = RandomDirection();
            var tr1 = World.TraceLine(src, src + dir1 * Vars.Radius, TraceMask.Brushes, Handle);
            if ((tr1.End - src).Length < Vars.Radius * 0.1f)
                continue;
            if (tr1.Fraction == 1f)
                continue;

            Zap(src, tr1.End);
            break;
        }
    }
}

/// <summary>
/// The map keys <see cref="EnvBeam"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvBeamVars
{
    [VarName("LightningStart")]
    public string StartEntity { get; set; } = string.Empty;

    [VarName("LightningEnd")]
    public string EndEntity { get; set; } = string.Empty;

    /// <summary>How long a strike lasts; zero is a beam that stays.</summary>
    public float Life { get; set; }

    /// <summary>The wait between strikes (a random share of it with SF_BEAM_RANDOM).</summary>
    public float StrikeTime { get; set; }

    [VarName("texture")]
    public string Sprite { get; set; } = string.Empty;

    [VarName("BoltWidth")]
    public float Width { get; set; } = 2f;

    [VarName("NoiseAmplitude")]
    public float Noise { get; set; }

    [VarName("TextureScroll")]
    public int ScrollRate { get; set; } = 35;

    [VarName("framestart")]
    public int FrameStart { get; set; }

    /// <summary>How far a random strike looks for walls.</summary>
    public float Radius { get; set; }

    [VarName("TouchType")]
    public EnvBeam.TouchType Touch { get; set; }

    [VarName("filtername")]
    public string FilterName { get; set; } = string.Empty;

    [VarName("decalname")]
    public string DecalName { get; set; } = string.Empty;

    [VarName("renderamt")]
    public int Brightness { get; set; } = 255;

    [VarName("rendercolor")]
    public Vector Colour { get; set; } = new(255f, 255f, 255f);
}
