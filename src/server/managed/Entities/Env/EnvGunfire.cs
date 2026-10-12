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
/// env_gunfire: something offscreen shooting at something else. A firefight the
/// player hears and sees the tracers of without either side existing.
///
/// <para>
/// It is the one entity in effects.cpp that is a decision all the way down
/// rather than a dispatcher with keys. What it owns is the shape of a firefight:
/// how many rounds are in a burst, how long the lull between bursts, how fast
/// the rounds come, how wide the group is and how it is DISTRIBUTED across that
/// width, whether the rounds stop at what is in the way or carry to the
/// distance, and where the aim point actually is. None of that is presentation;
/// only the tracer is, and a tracer is an ss_beam the same way env_laser's is.
/// </para>
///
/// <para>
/// The spread is worth reading rather than skipping. A cone alone gives a flat
/// group — every round equally likely anywhere in the circle — which reads as a
/// spray rather than as somebody aiming. The bias reshapes it: at 1 the rounds
/// bunch towards the middle, at 0 they are flat across the circle, and below 0
/// they are pushed towards the EDGE, which is what makes suppressing fire look
/// like it is going past rather than at. That is the whole of what "bias" buys
/// and it is the reason the arithmetic came over verbatim rather than being
/// approximated by one random call.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvGunfire)]
public sealed class EnvGunfire : ScriptedEntity
{
    public EnvGunfire(IEngineConsole console, IEngineHost host, IEnginePrecache precache, IEngineSound sound, IEngineWorldQuery world)
    {
        Console = console;
        Host = host;
        Precache = precache;
        Sound = sound;
        World = world;
    }

    /// <summary>the console: convars, and commands run on the server or a client.</summary>
    private readonly IEngineConsole Console;

    /// <summary>the process and the match.</summary>
    private readonly IEngineHost Host;

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>making sounds, and what is heard.</summary>
    private readonly IEngineSound Sound;

    /// <summary>asking the world what is there.</summary>
    private readonly IEngineWorldQuery World;
    /// <summary>What a map may set on this entity. See <see cref="EnvGunfireVars"/>.</summary>
    [ScriptVars] public EnvGunfireVars Vars = new();

    /// <summary>
    /// The tracer sprite. The same one this mod's bullets use, so a shot from an
    /// env_gunfire and a shot from a player look alike — which is the point of
    /// the entity.
    /// </summary>
    private const string TracerSprite = "sprites/laserbeam.vmt";

    /// <summary>
    /// The four cones stock will accept, as the tangent of the half-angle —
    /// Source's own numbers from basecombatweapon_shared.h. A spread key that is
    /// none of them means no spread at all, which is stock's default branch and
    /// not a mistake: 0 is the documented way to ask for a straight line.
    /// </summary>
    internal static float Cone(int degrees) => degrees switch
    {
        1  => 0.00873f,
        5  => 0.04362f,
        10 => 0.08716f,
        15 => 0.13053f,
        _  => 0f,
    };

    /// <summary>
    /// The spread cone and the seconds between rounds. Computed ONCE in
    /// <see cref="OnSpawn"/>, which a hot reload does not run again -- so they
    /// are <c>[SaveField]</c>. Uncarried the cone would be zero and the
    /// interval zero, so a burst after a <c>cs_sv_reload</c> would put every
    /// round into one frame (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private float _cone;
    [SaveField] private float _interval;

    /// <summary>
    /// The rounds left in the current burst, rolled by
    /// <see cref="StartBurst"/> -- which <see cref="OnSpawn"/> calls and a hot
    /// reload does not run again -- so it is <c>[SaveField]</c>
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private int _shotsLeft;

    /// <summary>Where the target was, and how far, cached until it moves.</summary>
    private Vector _aim;
    private float _range;

    private EntityHandle _tracer = EntityHandle.None;
    private SsBeam? _managedTracer;

    /// <summary>The range of shot bias, which is engine tuning rather than ours.</summary>
    private float _biasMin = -1f;
    private float _biasMax = 1f;

    public override void OnSpawn()
    {
        if (Vars.ShootSound.Length > 0)
            Precache.PrecacheSound(Vars.ShootSound);

        _cone = Cone(Vars.Spread);

        // Stock inverts the keyvalue in place and calls the field "rate of fire"
        // afterwards. Kept as a rate here and inverted where it is used, because
        // a field whose name stops being true halfway through Spawn is how a
        // second reader of it gets the wrong number.
        _interval = Vars.RateOfFire > 0f ? 1f / Vars.RateOfFire : 0.1f;

        // The engine's own bounds on how far the bias may reshape a group. Read
        // once: nothing in this mod moves them, and a lookup per round would be
        // a string compare per bullet.
        _biasMin = ParseFloat(Console.FindConVarValue("ai_shot_bias_min") ?? string.Empty, -1f);
        _biasMax = ParseFloat(Console.FindConVarValue("ai_shot_bias_max") ?? string.Empty, 1f);

        int sprite = Precache.PrecacheModel(TracerSprite);

        if (sprite >= 0)
        {
            _tracer = Entities.Create(EntityNames.SsBeam);

            if (Entities.IsValid(_tracer))
            {
                Entities.Spawn(_tracer);
                _managedTracer = FindByHandle<SsBeam>(_tracer);

                // Thin, bright, and not scrolling: a round in flight, not a
                // laser. One beam is re-aimed per round rather than one being
                // created per round — a burst at ten rounds a second would
                // otherwise be ten entities a second, spawned and removed.
                _managedTracer?.SetLook(sprite, 2f, 2f, 0f, 0f, 255, 200, 120, 255);

                if (_managedTracer is not null)
                    _managedTracer.Visible = false;
            }
        }

        if (!Vars.Disabled)
            StartBurst();
    }

    public override void OnRemoved()
    {
        // Ours, and nothing else knows it belongs to us.
        if (Entities.IsValid(_tracer))
            Entities.Remove(_tracer);
    }

    [ScriptedEvent]
    private void OnEnable()
    {
        Vars.Disabled = false;
        StartBurst();
    }

    [ScriptedEvent]
    private void OnDisable()
    {
        Vars.Disabled = true;
        Stop();
    }

    public override void OnThink()
    {
        if (Vars.Disabled)
            return;

        var target = Vars.Target.Length > 0 ? Entities.FindByName(Vars.Target) : EntityHandle.None;

        // Nothing to shoot at is a stop rather than a shot into nowhere. Stock
        // stops its think here and then fires one more round anyway, at
        // whatever position it last cached — the world origin, if it never
        // found a target at all. See docs/divergences.md.
        if (!Entities.IsValid(target))
        {
            Stop();
            return;
        }

        Fire(target);

        _shotsLeft--;

        if (_shotsLeft > 0)
        {
            SetNextThink(_interval);
            return;
        }

        // The burst is over: roll the next one and wait out the lull.
        StartBurst(Host.RandomFloat(Vars.MinBurstDelay, Vars.MaxBurstDelay));
    }

    /// <summary>
    /// Rolls a burst length and starts it. The length is decided up front rather
    /// than per round because a burst is a decision somebody made before they
    /// pulled the trigger.
    /// </summary>
    private void StartBurst(float delay = 0f)
    {
        _shotsLeft = BurstSize(Vars.MinBurst, Vars.MaxBurst, Host.RandomFloat);

        // Nothing is in flight during a lull. The tracer is one beam that is
        // re-aimed rather than a projectile that expires, so it has to be put
        // away explicitly or it hangs in the air between bursts.
        if (delay > 0f && _managedTracer is not null)
            _managedTracer.Visible = false;

        SetNextThink(delay);
    }

    /// <summary>
    /// How many rounds are in a burst, inclusive of both ends.
    ///
    /// Floor of a float draw across a range one wider, rather than rounding one
    /// across the range itself: rounding gives the two end values half the
    /// weight of every other, so a burst keyed 2 to 3 would come out 2 or 3 a
    /// quarter of the time each and 2.5 never.
    /// </summary>
    internal static int BurstSize(int min, int max, Func<float, float, float> random)
    {
        if (max < min)
            max = min;

        int size = min + (int)MathF.Floor(random(0f, max - min + 1));

        return Math.Clamp(size, Math.Max(min, 1), Math.Max(max, 1));
    }

    private void Stop()
    {
        if (_managedTracer is not null)
            _managedTracer.Visible = false;

        StopThinking();
    }

    /// <summary>One round: where it goes, what it hits, and what is seen and heard.</summary>
    private void Fire(EntityHandle target)
    {
        Vector from = Origin;
        Vector centre = Entities.GetWorldSpaceCenter(target);

        // Re-measured only when the target has moved, which is stock's rule and
        // matters for the range: a burst fired at a moving target should not
        // change length round by round while the target stands still.
        if (centre.X != _aim.X || centre.Y != _aim.Y || centre.Z != _aim.Z)
        {
            _aim = centre;
            _range = (centre - from).Length;
        }

        Vector direction = Spread((_aim - from).Normalized);
        Vector to;

        if (Vars.Collide)
        {
            // MASK_SHOT, so a round is stopped by what a bullet would be stopped
            // by rather than only by walls.
            var hit = World.TraceLine(from, from + direction * 8192f, TraceMask.Shot, Handle);
            to = hit.End;
        }
        else
        {
            // As far as the target is, and no further: a round that missed
            // should fly PAST the target, not to the far wall of the map.
            to = from + direction * _range;
        }

        if (_managedTracer is not null)
        {
            _managedTracer.SetPoints(from, to);
            _managedTracer.Visible = true;
        }

        // At the muzzle rather than on the entity: a logical entity has no edict
        // for the engine to attach a sound to.
        if (Vars.ShootSound.Length > 0)
            Sound.EmitSoundAtPosition(Vars.ShootSound, from);
    }

    /// <summary>
    /// Where one round actually goes, given where it was aimed.
    ///
    /// The basis is the engine's, from the aim direction. Which way round the
    /// circle it is rotated does not matter — the cone is the same width in both
    /// axes, so rotating the basis about the shot rotates the group about its own
    /// centre and nothing about it changes.
    /// </summary>
    private Vector Spread(Vector direction)
    {
        if (_cone == 0f)
            return direction;

        var (right, up) = World.AngleVectorsFromForward(direction);

        var (x, y) = Scatter(Vars.Bias, _biasMin, _biasMax, Host.RandomFloat);

        return (direction + right * (x * _cone) + up * (y * _cone)).Normalized;
    }

    /// <summary>
    /// A point in the unit circle, distributed the way the bias asks for.
    ///
    /// <para>
    /// Two uniform draws mixed in a proportion the bias sets: all of the first
    /// gives a flat disc, all of the second gives the sum of two uniforms, which
    /// is a triangle peaked in the middle — the "gaussian" of the original. A
    /// negative bias then reflects each axis outward, turning the peak into a
    /// hole and pushing the group to the edge.
    /// </para>
    ///
    /// <para>
    /// The rejection loop is what makes it a circle rather than a square, and it
    /// is bounded in practice — the accepted region is most of the square — but
    /// it is capped anyway, because a loop that draws from the engine's random
    /// stream is a loop that can consume an unbounded number of numbers a demo
    /// would have to reproduce.
    /// </para>
    ///
    /// <para>
    /// Takes its random source as an argument so the tests can drive it with a
    /// known sequence; the entity always passes the engine's, because server
    /// randomness has to replay identically in a recording.
    /// </para>
    /// </summary>
    internal static (float X, float Y) Scatter(float bias, float biasMin, float biasMax,
                                               Func<float, float, float> random)
    {
        bias = Math.Clamp(bias, 0f, 1f);

        float shotBias = (biasMax - biasMin) * bias + biasMin;
        float flatness = MathF.Abs(shotBias) * 0.5f;

        float x = 0f, y = 0f;

        for (int attempt = 0; attempt < 16; attempt++)
        {
            x = random(-1f, 1f) * flatness + random(-1f, 1f) * (1f - flatness);
            y = random(-1f, 1f) * flatness + random(-1f, 1f) * (1f - flatness);

            if (shotBias < 0f)
            {
                x = x >= 0f ? 1f - x : -1f - x;
                y = y >= 0f ? 1f - y : -1f - y;
            }

            if (x * x + y * y <= 1f)
                return (x, y);
        }

        // Sixteen misses in a row is not a shot worth taking a seventeenth draw
        // for: fall back to the middle of the group, which is where an unbiased
        // round is most likely to have gone anyway.
        return (0f, 0f);
    }
}

/// <summary>
/// The map keys <see cref="EnvGunfire"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvGunfireVars
{
    /// <summary>What it shoots at, by name. The aim point is that entity's centre.</summary>
    public string Target { get; set; } = string.Empty;

    [VarName("minburstsize")]
    public int MinBurst { get; set; } = 2;

    [VarName("maxburstsize")]
    public int MaxBurst { get; set; } = 7;

    public float MinBurstDelay { get; set; } = 2f;

    public float MaxBurstDelay { get; set; } = 5f;

    /// <summary>Rounds per second, which is the reciprocal of the interval between them.</summary>
    public float RateOfFire { get; set; } = 10f;

    public string ShootSound { get; set; } = string.Empty;

    /// <summary>The width of the group, in degrees, and only the four stock offers.</summary>
    public int Spread { get; set; } = 5;

    /// <summary>How the group is distributed across that width. See the class note.</summary>
    public float Bias { get; set; } = 1f;

    /// <summary>Whether rounds stop at what is in the way, or carry to the target's range.</summary>
    [VarName("collisions")]
    public bool Collide { get; set; }

    [VarName("startdisabled")]
    public bool Disabled { get; set; }
}
