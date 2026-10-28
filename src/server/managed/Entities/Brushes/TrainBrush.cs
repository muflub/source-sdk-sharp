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
/// A brush that walks a path: a leg at a time, a pause at each stop, and
/// something to say to whatever gets in the way.
///
/// A sibling of <see cref="MovingBrush"/> rather than a subclass, deliberately.
/// A door is a two-position state machine — closed, open, and the two journeys
/// between them — and every question it asks itself is about which of the two it
/// is at. A train has no positions of its own at all: it has a chain of
/// somewhere-elses, a direction through it and a speed, and "where it is" is
/// only which node it last reached. Bending the door base to fit would mean
/// calling one node Closed and every other node Open.
///
/// What the two DO share is the noise1/noise2 idiom, which is copied here rather
/// than hoisted: a moving loop and an arrival one-shot, precached from OnSpawn
/// because OnSpawn is the only window in which a name from the map can still be
/// precached.
///
/// <para>
/// A leg is driven by a TIMED think and not by arrival. Three reasons, and the
/// third is the one that bites: the shell reports arrival once per axis-set, so
/// a move that both travels and turns reports twice; a rotation to the angles it
/// already holds reports synchronously from inside the call that started it; and
/// the shell stops dead while something is in the way rather than pushing
/// through, so a leg can take longer than the clock says. The last is why
/// <see cref="OnThink"/> asks where the brush actually IS before calling the leg
/// finished.
/// </para>
///
/// What a subclass owns is the chain: what a node is, where the next one is, what
/// a node contributes to the leg leaving it, and when to stop. This class owns
/// only the mechanics of getting from here to a position and back to the
/// subclass afterwards.
/// </summary>
public abstract class TrainBrush : ScriptedEntity
{
    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    public TrainBrush(IGameClock clock, IEngineDamage damage,
                      IEnginePrecache precache, IEngineSound sound)
    {
        Clock = clock;
        Damage = damage;
        Precache = precache;
        Sound = sound;
    }

    /// <summary>what the server thinks the time is.</summary>
    protected readonly IGameClock Clock;

    /// <summary>hurting things.</summary>
    protected readonly IEngineDamage Damage;

    // Movement IS NOT A FIELD HERE ANY MORE -- plan_entity.md step 2g. It is
    // ScriptedEntity's own bound area now, so every mover reaches one object
    // and a class that declared its own would SHADOW the base's.

    /// <summary>asking for what this entity will need before the level runs.</summary>
    protected readonly IEnginePrecache Precache;

    /// <summary>making sounds, and what is heard.</summary>
    protected readonly IEngineSound Sound;

    //-------------------------------------------------------------------------
    // The two numbers a map may leave out, and neither carries its fill-in here.
    //
    // A field initialiser would be indistinguishable from a value the map wrote,
    // so the subclass could never be given a different default: whatever it
    // said, the number would already be a hundred by the time it was asked. Both
    // are left at zero — which is what "the map said nothing" means — and filled
    // in from DefaultSpeed and DefaultBlockDamage at spawn.
    //-------------------------------------------------------------------------

    /// <summary>What a map may set on this entity. See <see cref="TrainBrushVars"/>.</summary>
    [ScriptVars] public TrainBrushVars Vars = new();

    private enum Leg
    {
        /// <summary>Not going anywhere and not counting down.</summary>
        Idle,

        /// <summary>Travelling, with a think armed for the arrival.</summary>
        Travelling,

        /// <summary>Sitting at a node, with a think armed for the departure.</summary>
        Waiting,
    }

    private Leg _leg;
    private Vector _legTarget;
    private float _legSpeed;
    private float _nextBlockTime;

    public override void OnSpawn()
    {
        // Solid and visible: a train is a piece of the map that moves, and a
        // train you can walk through is a train nobody can ride.
        SetSolidity(BrushSolidity.Solid);
        SetVisible(true);

        if (Vars.Speed <= 0f)
            Vars.Speed = DefaultSpeed;

        if (Vars.BlockDamage == 0f)
            Vars.BlockDamage = DefaultBlockDamage;

        (Vars.MoveSound, Vars.ArriveSound) = SoundNames;

        // Here because here is inside the level-load window. It cannot be done
        // at registration either — these names come from a map that does not
        // exist yet then.
        if (Vars.MoveSound.Length > 0)
            Precache.PrecacheSound(Vars.MoveSound);

        if (Vars.ArriveSound.Length > 0)
            Precache.PrecacheSound(Vars.ArriveSound);
    }

    //-------------------------------------------------------------------------
    // What a key the map left out means.
    //
    // Overridable rather than fixed because it is NOT the same fact for every
    // train, and stock says so twice over: a func_train with no speed key has
    // no other way to be told one, so it gets a hundred, while a
    // func_tracktrain reads a missing speed as "stopped until something starts
    // me" — a hundred there would set every train in the map off at load. Same
    // for the crush, which is two on the one and nothing at all on the other.
    //-------------------------------------------------------------------------
    protected virtual float DefaultSpeed => 100f;

    protected virtual float DefaultBlockDamage => 2f;

    /// <summary>
    /// The looping sound while it travels and the one-shot when it stops, by the
    /// time the level load is over.
    ///
    /// Also overridable, and for a sharper reason than the defaults: a track
    /// train's sounds are not under these key names at all — it is not a
    /// platform train and writes MoveSound and StopSound instead — and it has no
    /// fill-in, so a map naming none is silent rather than making lift noises.
    /// Either name coming back empty means there is no such sound, and nothing
    /// is precached or played for it.
    /// </summary>
    protected virtual (string Moving, string Arriving) SoundNames
        => (ValidateSoundName(Vars.MoveSound, "Plat.DefaultMoving"),
            ValidateSoundName(Vars.ArriveSound, "Plat.DefaultArrive"));

    /// <summary>Stock's UTIL_ValidateSoundName: empty, or the literal "0", means unset.</summary>
    protected static string ValidateSoundName(string name, string fallback)
        => (name.Length == 0 || name == "0") ? fallback : name;

    //-------------------------------------------------------------------------
    // The leg clock.
    //
    // Idle is a state a subclass can also be told about, because the deferred
    // start every managed entity needs — a node naming another node cannot be
    // resolved at spawn — arrives as an ordinary think with nothing in flight.
    //-------------------------------------------------------------------------
    public override void OnThink()
    {
        if (_leg == Leg.Travelling)
        {
            float remaining = (_legTarget - Origin).Length;

            // Not there yet. Either the tick is simply not the last one, or
            // something is standing in the way and the stepper has stopped
            // dead against it; both answers are “ask again”. Every frame
            // rather than at the projected arrival, because the journey is
            // also a carry — whoever stands on the train is handed its
            // velocity each tick (see Riders, and Phase 3c's decision).
            if (remaining > ArrivalTolerance)
            {
                // THE CARRY IS NOT HERE ANY MORE -- plan_entity.md step 2g.
                // Riders are carried by the integrator, for every mover, from
                // the step it just committed. The per-frame think STAYS: it is
                // also how a train notices it has arrived, and a train stopped
                // dead against somebody standing on the track has to keep
                // asking.
                SetNextThink(0f);
                return;
            }
        }

        FinishStep();
    }

    // A tenth of a unit. The stepper lands exactly on the destination, so this
    // is only guarding the float comparison — not a slop budget.
    private const float ArrivalTolerance = 0.1f;

    /// <summary>
    /// Ends whatever the train is in the middle of, without asking where it is.
    ///
    /// Split out because it is also the seam a route is walked through in a
    /// test: everything above it is the ARRIVAL CHECK, which reads the entity's
    /// origin, and a test has no engine to move one. Everything below is the
    /// decision, which is the part worth checking.
    /// </summary>
    internal void FinishStep()
    {
        switch (_leg)
        {
            case Leg.Travelling:
                _leg = Leg.Idle;
                StopMoveSound();
                OnLegDone();
                return;

            case Leg.Waiting:
                _leg = Leg.Idle;
                OnWaitDone();
                return;

            default:
                OnIdleThink();
                return;
        }
    }

    /// <summary>A leg finished. Where to go next is the subclass's business.</summary>
    protected virtual void OnLegDone() { }

    /// <summary>A pause at a node ran out.</summary>
    protected virtual void OnWaitDone() { }

    /// <summary>A think that was neither, which is how the deferred start arrives.</summary>
    protected virtual void OnIdleThink() { }

    /// <summary>Sets off for a position, and arms the arrival.</summary>
    protected void TravelTo(Vector destination, float speed)
    {
        _legTarget = destination;
        _legSpeed = speed > 0f ? speed : Vars.Speed;
        _leg = Leg.Travelling;

        PlayMoveSound();
        MoveTo(destination, _legSpeed);

        // Every frame from the first, not once at the projected arrival: the
        // journey is also a carry, and a leg that only thought at its end left
        // its riders standing in the air where the train had been — measured
        // live as a player dropped at the platform while the train ran.
        SetNextThink(0f);
    }

    /// <summary>Arrives without travelling, for a node that says to jump to it.</summary>
    protected void JumpTo(Vector destination)
    {
        Halt();
        Origin = destination;
    }

    /// <summary>Sits still for a while, then <see cref="OnWaitDone"/>.</summary>
    protected void WaitFor(float seconds)
    {
        StopMoveSound();
        _leg = Leg.Waiting;
        SetNextThink(seconds);
    }

    /// <summary>
    /// Stops where it is and stops counting.
    ///
    /// Explicitly cancels the movement as well as the clock, which is the
    /// difference between stopping a train and letting it finish the leg it was
    /// on: stock's Stop clears the move-done function so the brush does NOT
    /// teleport to where it was heading.
    /// </summary>
    protected void Halt()
    {
        _leg = Leg.Idle;
        StopThinking();
        StopMoveSound();
        Movement.StopMoving(Handle);
    }

    //-------------------------------------------------------------------------
    // The noises, played per leg.
    //
    // Virtual because a leg does not mean the same thing to every train. Here a
    // leg is a journey between stops, so restarting the loop at each one is
    // right and is what stock's PlayMovingSound does — it destroys the sound
    // patch and builds a new one. A track train's legs are the gaps between
    // sleepers, and it keeps one sound running across all of them.
    //-------------------------------------------------------------------------
    protected virtual void PlayMoveSound()
    {
        if (Vars.MoveSound.Length > 0)
            Sound.EntityEmitSound(Handle, Vars.MoveSound);
    }

    /// <summary>
    /// Stops the loop. Not optional: noise1 is a looping sound, and a train that
    /// started one and never stopped it grinds for the rest of the map.
    /// </summary>
    protected virtual void StopMoveSound()
    {
        if (Vars.MoveSound.Length > 0)
            Sound.EntityStopSound(Handle, Vars.MoveSound);
    }

    protected void PlayArriveSound()
    {
        if (Vars.ArriveSound.Length > 0)
            Sound.EntityEmitSound(Handle, Vars.ArriveSound);
    }

    //-------------------------------------------------------------------------
    // Something is in the way.
    //
    // A train crushes rather than reversing, which is the opposite of what a
    // door here does and is right for both: a door that killed you would be a
    // surprise, and a train you can stop by standing in front of it would be a
    // toy. The half-second is stock's, and it is what stops a stalled train
    // dealing its damage once per tick.
    //-------------------------------------------------------------------------
    public override void OnBlocked(EntityHandle other)
    {
        if (Clock.Now < _nextBlockTime)
            return;

        _nextBlockTime = Clock.Now + 0.5f;

        Damage.TakeDamage(other, Handle, Vars.BlockDamage, DamageType.Crush);
    }

    /// <summary>
    /// The offset from this brush's origin to the middle of it.
    ///
    /// A path node marks where the middle of the train should end up, not where
    /// its origin should, and for a brush entity the two are far apart: a brush
    /// carries its geometry in world coordinates and its origin is usually at
    /// the map's, so the difference is the whole distance across the map.
    ///
    /// Zero when the collision box is unknown, which is also the right answer
    /// for a train built the tidy way — with an origin brush, the origin IS the
    /// middle.
    ///
    /// <para>
    /// This was zero for every train until SourceSharp_EntityGetBounds learned to
    /// answer for the parallel hierarchy — it resolved a CBaseEntity only, so a
    /// managed brush could not learn where its box sat relative to its origin.
    /// It reads real bounds now.
    /// </para>
    /// </summary>
    protected Vector CentreOffset
    {
        get
        {
            var (mins, maxs) = Entities.GetBounds(Handle);
            return (mins + maxs) * 0.5f;
        }
    }
}

/// <summary>
/// The map keys <see cref="TrainBrush"/> declares.
/// </summary>
[ScriptVars]
public partial class TrainBrushVars
{
    /// <summary>Units per second, unless a node on the path overrides it.</summary>
    public float Speed { get; set; }

    /// <summary>What being crushed by this costs.</summary>
    [VarName("dmg")]
    public float BlockDamage { get; set; }

    //-------------------------------------------------------------------------
    // The noises, under Hammer's own opaque key names: noise1 is the loop while
    // it MOVES and noise2 the one-shot when it ARRIVES.
    //
    // Unlike a door, a train that names neither is not silent — stock fills both
    // in from the platform defaults, so a func_train with no sound keys makes
    // lift noises. "0" counts as unset, which is what a very old map writes.
    //-------------------------------------------------------------------------
    [VarName("noise1")]
    public string MoveSound { get; set; } = string.Empty;

    [VarName("noise2")]
    public string ArriveSound { get; set; } = string.Empty;

    /// <summary>
    /// Declared so a map's value survives being read back, and used by nothing:
    /// the managed sound call plays at the entity's own attenuation and takes no
    /// volume. See docs/divergences.md.
    /// </summary>
    public float Volume { get; set; } = 0.85f;
}
