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
/// Entities that watch something else move and report on it.
///
/// These became possible with the physics reads the constraints needed: angular
/// velocity had a setter and no getter, so an entity whose whole job is to
/// observe rotation had nothing to observe with.
/// </summary>
[ScriptedEntity(EntityNames.PointAngularvelocitysensor)]
public sealed class PointAngularVelocitySensor : ScriptedEntity
{
    public PointAngularVelocitySensor(IGameClock clock)
    {
        Clock = clock;
    }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;
    /// <summary>What a map may set on this entity. See <see cref="PointAngularVelocitySensorVars"/>.</summary>
    [ScriptVars] public PointAngularVelocitySensorVars Vars = new();

    public readonly Output AngularVelocity = new();
    public readonly Output OnLessThan = new();
    public readonly Output OnLessThanOrEqualTo = new();
    public readonly Output OnGreaterThan = new();
    public readonly Output OnGreaterThanOrEqualTo = new();
    public readonly Output OnEqualTo = new();

    private EntityHandle _watched = EntityHandle.None;
    private float _lastReported = float.NaN;

    private const int NoResultYet = -2;
    private int _lastCompare = NoResultYet;
    private int _lastFired = NoResultYet;
    private float _fireAt;

    /// <summary>
    /// Resolved on the first think rather than at spawn: the target is named,
    /// and half the map does not exist yet while the map is being parsed.
    /// </summary>
    public override void OnSpawn() => SetNextThink(0f);

    public override void OnThink()
    {
        if (!Entities.IsValid(_watched))
        {
            _watched = Entities.FindByName(Vars.Target);

            if (!Entities.IsValid(_watched))
            {
                // Reported once. A sensor watching nothing will never fire, and
                // silence would make that look like the fan simply never spun.
                Log.Warning($"[SourceSharp] point_angularvelocitysensor '{Name}': no entity named '{Vars.Target}'\n");
                return;
            }
        }

        Observe(Sample(), Clock.Now);
        SetNextThink(0f);
    }

    /// <summary>
    /// The debounce, given a reading and the time it was taken.
    ///
    /// Separated from the reading so it can be driven directly by a test: the
    /// interesting behaviour is entirely in when it decides to believe a change,
    /// and none of that needs a physics object to exist.
    /// </summary>
    internal void Observe(float speed, float now)
    {
        if (speed != _lastReported)
        {
            AngularVelocity.Fire(speed);
            _lastReported = speed;
        }

        int compare = Classify(speed);

        if (compare != _lastCompare)
        {
            // Oscillating back to what was last fired is not a change worth
            // announcing — the world never saw the excursion.
            if (compare == _lastFired)
                _fireAt = 0f;
            else if (_lastCompare != NoResultYet)
                _fireAt = now + Vars.FireInterval;

            _lastCompare = compare;
        }
        else if (_fireAt != 0f && now >= _fireAt)
        {
            FireComparison(compare);
            _lastFired = compare;
            _fireAt = 0f;
        }
    }

    /// <summary>Configures the thresholds a test drives it with.</summary>
    internal void Configure(float threshold, float fireInterval)
    {
        Vars.Threshold = threshold;
        Vars.FireInterval = fireInterval;
    }

    /// <summary>Tests once, immediately, without waiting out the interval.</summary>
    [ScriptedEvent("Test")]
    private void OnTest() => FireComparison(Classify(Sample()));

    /// <summary>Tests, but lets the interval have its say.</summary>
    [ScriptedEvent("TestWithInterval")]
    private void OnTestWithInterval()
    {
        int compare = Classify(Sample());

        if (compare != _lastCompare)
        {
            _fireAt = Clock.Now + Vars.FireInterval;
            _lastCompare = compare;
        }
    }

    /// <summary>-1 below the threshold, 0 exactly on it, 1 above.</summary>
    internal int Classify(float speed)
        => speed > Vars.Threshold ? 1 : speed == Vars.Threshold ? 0 : -1;

    private float Sample()
    {
        Vector angular = Entities.GetAngularVelocity(_watched);

        if (!Vars.UseHelper)
            return angular.Length;

        // The key is a POINT — Hammer draws the axis as a line from here to
        // there — so the direction is the difference, and the component along
        // it is what the map wants measured.
        Vector along = (Vars.Axis - Origin).Normalized;
        return angular.Dot(along);
    }

    private void FireComparison(int compare)
    {
        switch (compare)
        {
            case -1:
                OnLessThan.Fire();
                OnLessThanOrEqualTo.Fire();
                break;

            case 0:
                OnEqualTo.Fire();
                OnLessThanOrEqualTo.Fire();
                OnGreaterThanOrEqualTo.Fire();
                break;

            default:
                OnGreaterThan.Fire();
                OnGreaterThanOrEqualTo.Fire();
                break;
        }
    }
}

/// <summary>
/// The map keys <see cref="PointAngularVelocitySensor"/> declares.
/// </summary>
[ScriptVars]
public partial class PointAngularVelocitySensorVars
{
    /// <summary>Name of the entity to watch.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>The speed the outputs compare against, in degrees per second.</summary>
    public float Threshold { get; set; }

    /// <summary>
    /// How long a comparison has to hold before it is believed.
    ///
    /// Not a rate limit — a debounce. A fan slowing past the threshold wobbles
    /// across it several times, and without this every wobble would fire the
    /// whole output chain.
    /// </summary>
    public float FireInterval { get; set; }

    /// <summary>
    /// A world point defining the axis to measure along, when
    /// <see cref="PointAngularVelocitySensorVars.UseHelper"/> is set. Without it the magnitude is used,
    /// whatever direction the spin is in.
    /// </summary>
    public Vector Axis { get; set; }

    public bool UseHelper { get; set; }
}
