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
/// env_shooter and gibshooter: a machine that throws debris.
///
/// The division is the one this phase keeps arriving at. What a gib IS — a
/// model, a physics object, a fade-out — is an effect and stays native. What
/// this owns is the LOOP: how many, how far apart in time, how widely
/// scattered, which way, and whether it can be asked again.
///
/// <para>
/// One class for both classnames. <c>gibshooter</c> is the same machine with
/// human gibs already chosen, which in stock is a subclass overriding one
/// method; here it is a default for one field.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvShooter)]
[ScriptedEntity(EntityNames.Gibshooter)]
public sealed class EnvShooter : ScriptedEntity
{
    public EnvShooter(IEngineHost host, IEngineEntities entities,
                      IEngineEntityRender render, IEnginePrecache precache,
                      IEntityDirectory directory)
    {
        Host = host;
        Gibs = new Gibs(Log, entities, render, precache, directory);
    }

    /// <summary>the process and the match.</summary>
    private readonly IEngineHost Host;

    /// <summary>
    /// Making one piece of debris — <c>plan_entity.md</c> step 4a.
    ///
    /// <para>
    /// IEngineDamage.ShootGib IS GONE FROM HERE. It crossed to native, which
    /// built a <c>CGib</c>, which was the only thing on the server link line
    /// that still needed <c>game/server/gib.cpp</c>. A gib is a managed entity
    /// now (<see cref="SsGib"/>) and the whole of that crossing with it: this
    /// class makes entities the way every other class does.
    /// </para>
    ///
    /// <para>
    /// Built with <c>new</c> rather than injected, the way
    /// <c>PropPhysics.Breaker</c> builds its <c>PropBreak</c>: it is a helper
    /// over four engine areas and holds only the "said this once already" set,
    /// so a per-shooter instance is what the lifetime should be anyway.
    /// </para>
    /// </summary>
    private readonly Gibs Gibs;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => (SpawnFlag)SpawnFlags;

    /// <summary>What a map may set on this entity. See <see cref="EnvShooterVars"/>.</summary>
    [ScriptVars] public EnvShooterVars Vars = new();

    /// <summary>Stock's default gib model, and the whole of what gibshooter adds.</summary>
    private const string HumanGibs = "models/gibs/hgibs.mdl";

    /// <summary>
    /// What this shooter's gibs bleed, which is the one thing the two classnames
    /// disagree about besides the model.
    ///
    /// <para>
    /// <c>game/server/effects.cpp</c> sets it in BOTH factories and sets it
    /// opposite ways — <c>CGibShooter::CreateGib</c> says
    /// <c>SetBloodColor( BLOOD_COLOR_RED )</c>, <c>CEnvShooter::CreateGib</c>
    /// says <c>SetBloodColor( DONT_BLEED )</c>. That is not an oversight in
    /// either: a gibshooter throws pieces of a body and an env_shooter throws
    /// whatever model the mapper named, which is usually rock or metal.
    /// </para>
    ///
    /// <para>
    /// Neither was being sent, and a <c>CGib</c> has no constructor, so
    /// <c>m_bloodColor</c> read as the zeroed 0 — which is
    /// <see cref="BloodColor.Red"/>. gibshooter was accidentally right and
    /// env_shooter bled: up to five red decals per chunk as it bounced, on a
    /// shooter throwing crates. Only <see cref="GibSimulation.Point"/> shows it,
    /// because <c>CGib::BounceGibTouch</c> returns early on anything the solver
    /// owns.
    /// </para>
    ///
    /// <para>
    /// Decided on the CLASSNAME rather than on whether a model was named. Those
    /// two are not the same question: an env_shooter whose mapper left
    /// <c>shootmodel</c> empty still throws <see cref="HumanGibs"/>, and it is
    /// still an env_shooter — stock would have it not bleed, and reading the
    /// model would have it bleed.
    /// </para>
    /// </summary>
    internal BloodColor Bleeds =>
        ClassName.Equals(EntityNames.Gibshooter, StringComparison.OrdinalIgnoreCase)
            ? BloodColor.Red
            : BloodColor.None;

    /// <summary>How many are left in the current volley.</summary>
    private int _remaining;

    /// <summary>
    /// env_shooter's own spawnflags.
    ///
    /// <para>
    /// THREE ADJACENT BITS, and the middle one is the reason this comment was
    /// rewritten. <c>StrictRemove</c> was 16 here, under a paragraph explaining
    /// the "wide gap between 1 and 16" as an inheritance artefact. There is no
    /// gap. <c>game/server/effects.cpp</c> — the file this class is the port of,
    /// and the last written record of any of these numbers — defines them as
    /// consecutive shifts:
    /// </para>
    ///
    /// <code>
    /// #define SF_GIBSHOOTER_REPEATABLE  (1&lt;&lt;0)   // 1
    /// #define SF_SHOOTER_FLAMING        (1&lt;&lt;1)   // 2
    /// #define SF_SHOOTER_STRICT_REMOVE  (1&lt;&lt;2)   // 4
    /// </code>
    ///
    /// <para>
    /// So a mapper who ticked "Strict Remove" in Hammer set bit 4 and this class
    /// read bit 16, which is a bit nothing defines: strict-remove gibs faded on
    /// stock's normal schedule instead of being taken away while the player was
    /// looking at them, and a map that ticked nothing at all would have got
    /// strict removal the moment some later flag put a 16 in the field.
    /// </para>
    ///
    /// <para>
    /// <see cref="Flaming"/> is recorded here for the same reason the wrong
    /// number survived for as long as it did: the value was written down
    /// nowhere the C# could be checked against, so nothing could disagree with
    /// it out loud. Nothing READS it yet — a burning gib needs a handle to the
    /// gib to ignite, and <c>ShootGib</c> answers whether it threw one rather
    /// than which one. Named, valued and pinned by a test so that when the arm
    /// is written it is written against 2 and not against a guess.
    /// </para>
    /// </summary>
    [Flags]
    internal enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        /// <summary><c>SF_GIBSHOOTER_REPEATABLE</c>: it may be asked again.</summary>
        Repeatable = 0b0000_0001,

        /// <summary><c>SF_SHOOTER_FLAMING</c>: every gib comes out on fire.</summary>
        Flaming = 0b0000_0010,

        /// <summary>
        /// <c>SF_SHOOTER_STRICT_REMOVE</c>: take the gib away when its life is
        /// up even if the player is looking straight at it.
        /// </summary>
        StrictRemove = 0b0000_0100,
    }

    [ScriptedEvent("Shoot")]
    private void OnShoot() => Begin();

    [ScriptedEvent("Use")]
    private void OnUsed() => Begin();

    private void Begin()
    {
        // A volley already in flight is not restarted. Stock has the same
        // effect by way of resetting the think, and saying it out loud is
        // cheaper than reasoning about think scheduling.
        if (_remaining > 0)
            return;

        _remaining = Vars.Count;
        SetNextThink(0f);
    }

    public override void OnThink()
    {
        Throw();

        if (--_remaining > 0)
        {
            SetNextThink(Vars.Delay);
            return;
        }

        // Out of gibs. A repeatable shooter reloads and waits to be asked
        // again; one that is not is finished for good, and stock removes it.
        if (!Flags.HasFlag(SpawnFlag.Repeatable))
            Remove();
    }

    private void Throw()
    {
        var basis = Angles.Vectors(Entities.GetAngles(Handle));

        var shot = new GibShot
        {
            Speed           = Vars.Velocity,
            Simulation      = Vars.Simulation,
            Lifetime        = Vars.Life > 0f ? Vars.Life : 25f,
            AngularVelocity = Vars.AngularVelocity,
            Skin            = Vars.Skin,
            Scale           = Vars.Scale,
            GravityScale    = Vars.GravityScale,
            Material        = (int)MaterialFor(Vars.ShootSounds),
            StrictRemove    = Flags.HasFlag(SpawnFlag.StrictRemove) ? 1 : 0,
            BloodColor      = (int)Bleeds,
        };

        shot.Origin    = Origin;
        shot.Angles    = Vars.GibAngles;
        shot.Direction = Scatter(basis, Vars.Variance);

        LastShot = shot;
        ShotsThrown++;

        Gibs.Shoot(Vars.Model.Length > 0 ? Vars.Model : HumanGibs, shot);
    }

    /// <summary>
    /// The last block this shooter filled in, and how many it has filled.
    ///
    /// <para>
    /// Exposed for tests for the reason <c>SsRagdoll.ThinkArmed</c> gives, and
    /// the reason is sharper here than usual: what a gib IS, is a managed entity
    /// this shooter creates through <see cref="Gibs"/>, and a test's engine
    /// double answers <see cref="EntityHandle.None"/> to
    /// <c>Entities.Create</c> — so nothing downstream of the shot exists to
    /// read. Before step 4a the shot crossed the ABI and the recording engine
    /// caught it; it does not cross any more.
    /// </para>
    ///
    /// <para>
    /// It is the SHOT and not the property behind it, which is the distinction
    /// that made the blood-colour bug findable: <see cref="Bleeds"/> could be
    /// perfectly right and never reach the struct, and that is precisely the
    /// state this shooter was in.
    /// </para>
    /// </summary>
    internal GibShot LastShot { get; private set; }

    /// <inheritdoc cref="LastShot"/>
    internal int ShotsThrown { get; private set; }

    /// <summary>
    /// One scattered direction, drawn per gib so a volley does not fly out in
    /// formation.
    ///
    /// The same arithmetic <c>env_entity_maker</c> uses, bias and all: stock
    /// adds the variance along FORWARD twice and along right and up once each.
    /// It reads like a copy-paste slip in both places and is kept in both, for
    /// the same reason — existing maps were tuned against the spread that
    /// actually comes out.
    /// </summary>
    private Vector Scatter(Angles.Basis basis, float variance)
    {
        if (variance == 0f)
            return basis.Forward;

        Vector shoot = basis.Forward;

        shoot += basis.Right   * Host.RandomFloat(-1f, 1f) * variance;
        shoot += basis.Forward * Host.RandomFloat(-1f, 1f) * variance;
        shoot += basis.Up      * Host.RandomFloat(-1f, 1f) * variance;

        return shoot.Normalized;
    }

    /// <summary>
    /// The map's "shootsounds" number as Source's material letter.
    ///
    /// A lookup rather than a cast: the map writes 0..4 in the order a Hammer
    /// dropdown lists them, which is not the order the engine's material
    /// constants happen to fall in. Static so it can be tested.
    /// </summary>
    internal static SurfaceMaterial MaterialFor(int shootSounds) => shootSounds switch
    {
        0 => SurfaceMaterial.Glass,
        1 => SurfaceMaterial.Wood,
        2 => SurfaceMaterial.Metal,
        3 => SurfaceMaterial.Flesh,
        4 => SurfaceMaterial.Rocks,
        _ => SurfaceMaterial.Silent,   // stock's explicit -1
    };
}

/// <summary>
/// The map keys <see cref="EnvShooter"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvShooterVars
{
    /// <summary>How many to throw, one per interval.</summary>
    [VarName("m_iGibs")]
    public int Count { get; set; } = 3;

    [VarName("m_flVelocity")]
    public float Velocity { get; set; } = 200f;

    /// <summary>How widely the direction is scattered. Zero throws them all the same way.</summary>
    [VarName("m_flVariance")]
    public float Variance { get; set; } = 0.15f;

    [VarName("m_flGibLife")]
    public float Life { get; set; } = 25f;

    /// <summary>Seconds between one and the next. Zero throws the lot in one tick.</summary>
    public float Delay { get; set; }

    /// <summary>Point, physics or ragdoll. See <see cref="GibSimulation"/>.</summary>
    public int Simulation { get; set; }

    public Vector GibAngles { get; set; }

    [VarName("gibanglevelocity")]
    public float AngularVelocity { get; set; }

    /// <summary>
    /// The model to throw. Empty means human gibs, which is what a
    /// <c>gibshooter</c> is.
    /// </summary>
    [VarName("shootmodel")]
    public string Model { get; set; } = string.Empty;

    public int Skin { get; set; }

    public float Scale { get; set; } = 1f;

    [VarName("gibgravityscale")]
    public float GravityScale { get; set; } = 1f;

    /// <summary>Which impact sounds, as stock's numbering. -1 is silent.</summary>
    public int ShootSounds { get; set; } = -1;
}
