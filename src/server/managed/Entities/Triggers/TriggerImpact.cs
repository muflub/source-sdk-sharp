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
/// One shove to everything in the volume, when the map asks for it: a blast
/// door opening, a train going past, an explosion somewhere else.
///
/// The same shape as trigger_apply_impulse — an impulse on an input rather than
/// on touch — differing in two ways that both matter. It pushes with a force
/// instead of setting a velocity, so a heavy crate moves less than a can; and it
/// scatters the direction, so a stack does not leave in formation.
/// </summary>
[ScriptedEntity(EntityNames.TriggerImpact, ScriptedEntityKind.Brush)]
public sealed class TriggerImpact : TriggerVolume
{
    public TriggerImpact(IGameClock clock, IEngineHost host, IEngineHud hud, IEngineEntityPhysics physics)
        : base(clock)
    {
        Host = host;
        Hud = hud;
        Physics = physics;
    }

    /// <summary>the process and the match.</summary>
    private readonly IEngineHost Host;

    /// <summary>drawing, and the local player it is drawn for.</summary>
    private readonly IEngineHud Hud;

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;
    /// <summary>The shove as a vector, for a map that wants to do something else with the same number.</summary>
    public readonly Output ImpactForce = new();

    /// <summary>What a map may set on this entity. See <see cref="TriggerImpactVars"/>.</summary>
    [ScriptVars] public TriggerImpactVars TriggerImpactVars = new();

    private const float ViewkickScale = 0.1f;

    /// <summary>
    /// How long an impact keeps reaching things. Stock's tenth of a second, and
    /// stock's reason: it turns its trigger flag on for that long so the engine
    /// re-reports whatever is inside, which is how it finds anything at all.
    /// This volume already knows who is inside, so the window only decides who
    /// still counts if they ARRIVE in the moment the impact happens.
    /// </summary>
    private const float Window = 0.1f;

    /// <summary>
    /// The shove direction, computed ONCE in <see cref="OnSpawn"/> from the
    /// brush's spawn angles, which a hot reload does not run again -- so it is
    /// <c>[SaveField]</c>. Uncarried, a rebuilt trigger would shove everything
    /// along <c>Vector.Zero</c> (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private Vector _direction;
    private float _armedUntil;

    // A trigger_impact that only noticed players would have nothing to throw.
    public override bool TouchedByAnything => true;

    public override void OnSpawn()
    {
        base.OnSpawn();

        // Where the mapper pointed the brush.
        _direction = Angles.Forward(SpawnAngles);

        // A map is free to write nonsense, and a noise above one would point the
        // shove backwards as often as forwards.
        TriggerImpactVars.Noise = Math.Clamp(TriggerImpactVars.Noise, 0f, 1f);
        TriggerImpactVars.Viewkick = Math.Clamp(TriggerImpactVars.Viewkick, 0f, 1f);

        // Hammer never offered this one a "wait", and the base's default would
        // throttle it: a stack of crates has to leave on one impact rather than
        // one crate every fifth of a second.
        Vars.Wait = 0f;
    }

    [ScriptedEvent("Impact")]
    private void OnImpactRequested()
    {
        ImpactForce.Fire(Text(_direction * TriggerImpactVars.Magnitude));

        _armedUntil = Clock.Now + Window;

        foreach (var toucher in Inside)
        {
            if (Entities.IsValid(toucher))
                Shove(toucher);
        }
    }

    [ScriptedEvent("SetMagnitude")]
    private void OnMagnitudeSet(float magnitude) => TriggerImpactVars.Magnitude = magnitude;

    // Arriving during the window counts as having been there for it.
    protected override void Fire(EntityHandle toucher)
    {
        if (Clock.Now < _armedUntil)
            Shove(toucher);
    }

    /// <summary>
    /// The force as three numbers, which is what the output carries.
    ///
    /// No brackets, because the engine hands an output's value on as a string and
    /// converts it at the far end, and three bare numbers is what its
    /// string-to-vector parser reads. The added zeroes are not decoration either:
    /// Source's angle conversion negates the pitch, so a flat direction carries a
    /// NEGATIVE zero and .NET writes that as "-0" — harmless to the parser and a
    /// puzzle to a mapper reading it. Adding a positive zero is what makes IEEE
    /// forget the sign.
    /// </summary>
    private static string Text(Vector force)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture,
                         $"{force.X + 0f} {force.Y + 0f} {force.Z + 0f}");

    private void Shove(EntityHandle toucher)
    {
        // The noise is added to a unit vector rather than scaling it, so a noise
        // of one really is any direction at all — which is what the key promises.
        Vector direction = _direction + new Vector(
            Host.RandomFloat(-TriggerImpactVars.Noise, TriggerImpactVars.Noise),
            Host.RandomFloat(-TriggerImpactVars.Noise, TriggerImpactVars.Noise),
            Host.RandomFloat(-TriggerImpactVars.Noise, TriggerImpactVars.Noise));

        Physics.ApplyForceCenter(toucher, direction * TriggerImpactVars.Magnitude);

        if (TriggerImpactVars.Magnitude == 0f || !Entities.IsPlayer(toucher))
            return;

        // Not the scattered direction: the jolt is meant to read as the same
        // event everyone in the volume felt.
        float punch = -TriggerImpactVars.Viewkick * TriggerImpactVars.Magnitude * ViewkickScale;

        // Centred on the player, with barely any radius. The ABI kicks everyone
        // within reach of a point and the only one owed this is the one stood in
        // the volume; at no distance at all the falloff is one, so they get the
        // whole of it. In the air as well as on the ground, because being thrown
        // is when it matters most.
        Hud.ViewPunch(Entities.GetOrigin(toucher), new Vector(_direction.Y * punch, 0f, _direction.X * punch),
                         radius: 1f, inAir: true);
    }
}

/// <summary>
/// The map keys <see cref="TriggerImpact"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerImpactVars
{
    /// <summary>Strength of the shove. Negative reverses it.</summary>
    public float Magnitude { get; set; } = 200f;

    /// <summary>How scattered the direction is, 0 to 1. One is a random direction.</summary>
    public float Noise { get; set; } = 0.1f;

    /// <summary>How hard a player inside is jolted, as a fraction of the magnitude.</summary>
    public float Viewkick { get; set; } = 0.05f;
}
