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
/// A brush that runs a chain of <see cref="PathCorner"/>s, one leg at a time,
/// and crushes what stands in it.
///
/// The oldest mover in the SDK, and it shows in what it does NOT have: no
/// direction to reverse, no banking, no acceleration. It goes forward through the
/// chain forever, so a mapper who wants a round trip lays the corners out and
/// back and joins the last to the first. That is why nothing here holds a
/// direction, and why <see cref="PathCorner"/> has no back-link to hold one
/// against.
///
/// Two things a corner contributes to the leg, and they are asymmetric in a way
/// that reads as a bug until you see the reason: the WAIT belongs to the corner
/// being arrived at, and the SPEED belongs to the corner being left. A corner's
/// speed describes the journey out of it, so the leg toward corner N runs at
/// corner N-1's speed. Stock has it that way and maps were timed against it.
///
/// Being started and stopped is expressed as one bit — the same bit a corner uses
/// to mean the same thing — and the bit is the whole state: a train that is
/// "waiting to be retriggered" has no leg in flight and no clock running, and
/// starting it is asking for the next leg.
/// </summary>
[ScriptedEntity(EntityNames.FuncTrain, ScriptedEntityKind.Brush)]
public sealed class FuncTrain : TrainBrush
{
    public FuncTrain(IGameClock clock, IEngineDamage damage,
                     IEnginePrecache precache, IEngineSound sound)
        : base(clock, damage, precache, sound)
    {
    }

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="FuncTrainVars"/>.</summary>
    [ScriptVars] public FuncTrainVars FuncTrainVars = new();

    /// <summary>The corner most recently set off for, which is where the train is.</summary>
    private PathCorner? _current;

    /// <summary>The corner to set off for next. Held rather than looked up — see Advance.</summary>
    private PathCorner? _pending;

    /// <summary>Seconds to sit at <see cref="_current"/> once it is reached.</summary>
    private float _wait;

    /// <summary>
    /// The speed of the leg in flight, seeded from the map's key in
    /// <see cref="OnSpawn"/> — which a hot reload does not run again — and
    /// retuned only by the corner it is heading for, so it is
    /// <c>[SaveField]</c>. Uncarried, the rebuilt instance braked the leg
    /// mid-journey to zero after a <c>cs_sv_reload</c>
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private float _currentSpeed;

    private bool _stopped;
    private bool _started;

    /// <summary>The corner the train is at, or heading for. Null before it has found its first.</summary>
    public PathCorner? CurrentCorner => _current;

    /// <summary>The corner after that, read one leg ahead. See <see cref="Advance"/>.</summary>
    public PathCorner? PendingCorner => _pending;

    /// <summary>Whether the train is waiting to be sent for.</summary>
    public bool WaitingForTrigger => _stopped;

    /// <summary>The speed of the leg in flight, which is not necessarily the map's.</summary>
    public float LegSpeed => _currentSpeed;

    public override void OnSpawn()
    {
        base.OnSpawn();

        _currentSpeed = Vars.Speed;

        // A train the player walks through. The only use stock names for it is a
        // moving body of water, which has to be entered rather than stood on.
        if (Flags.HasFlag(SpawnFlag.Passable))
            SetSolidity(BrushSolidity.None);

        // Stock's SF_TRAIN_WAIT_RETRIGGER, which is spawnflag 1 on the train and
        // means the same thing spawnflag 1 on a corner does.
        _stopped = Flags.HasFlag(SpawnFlag.WaitRetrigger);

        // Deferred, and this is the only window there is: a train names a corner
        // and managed entities spawn together in no order worth relying on, so
        // at spawn the corner may not exist. Stock has Activate for this; the
        // idiom here is a think armed from spawn, which logic_auto established.
        SetNextThink(0.2f);
    }

    /// <summary>
    /// func_train's own spawnflags.
    ///
    /// <para>
    /// The same 0x0008 that means "start at the bottom" to the func_trackchange on
    /// the same track, and "passable" here and to func_tracktrain. The type is what
    /// says which of the three is being asked.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        WaitRetrigger = 0b0000_0001,
        /// <summary>Stock's SF_TRAIN_PASSABLE, whose comment is "used to make water trains".</summary>
        Passable = 0b0000_1000,
    }

    /// <summary>The deferred start, which is the only reason this think exists.</summary>
    protected override void OnIdleThink() => TakeFirstCorner();

    /// <summary>
    /// What stock does in Activate: find the first corner, jump onto it, and
    /// either set off or wait to be sent for.
    ///
    /// Whether the mapper NAMED the train is what decides which — an unnamed
    /// train has nothing that could ever start it, so it starts itself. Only ever
    /// runs once, because a train that re-activated would jump back to the
    /// beginning of its path.
    ///
    /// Reachable from the Start input as well as from the think, because the
    /// think is a fifth of a second late and a logic_auto firing Start at map
    /// load is not: a train asked to go before it has found its first corner has
    /// to find it then rather than lose the request.
    /// </summary>
    private void TakeFirstCorner()
    {
        if (_started)
            return;

        var first = FindByName<PathCorner>(FuncTrainVars.FirstCornerName);

        if (first is null)
        {
            _started = true;
            Log.Warning($"[SourceSharp] func_train '{Name}': can't find first path corner '{FuncTrainVars.FirstCornerName}'\n");
            return;
        }

        TakeFirstCorner(first);
    }

    /// <summary>
    /// The same, with the corner already in hand.
    ///
    /// Separated from the lookup above rather than merged into it because the
    /// lookup is the only part that needs a map: a test builds the chain itself
    /// and starts the train on it here.
    /// </summary>
    internal void TakeFirstCorner(PathCorner first)
    {
        if (_started)
            return;

        _started = true;

        _current = first;
        _pending = first.Next;

        JumpTo(PositionOf(first));

        if (Name.Length == 0)
            WaitFor(0.1f);
        else
            _stopped = true;
    }

    //-------------------------------------------------------------------------
    // The inputs. Stock exposes only these three, and a player's use key is not
    // one of them: stock's Use does its own thing, toggling the same bit.
    //-------------------------------------------------------------------------
    [ScriptedEvent("Start")] private void OnStartInput() => Start();
    [ScriptedEvent("Stop")] private void OnStopInput() => Stop();

    [ScriptedEvent("Toggle")]
    private void OnToggleInput()
    {
        if (_stopped)
            Start();
        else
            Stop();
    }

    [ScriptedEvent("Use")] private void OnUse() => OnToggleInput();

    private void Start()
    {
        TakeFirstCorner();

        if (!_stopped)
            return;

        _stopped = false;
        Advance();
    }

    /// <summary>
    /// Stops where it is, and rewinds one corner so that starting again finishes
    /// the leg it was on rather than skipping it.
    ///
    /// That rewind is stock's "pop back to last target", and it is the reason the
    /// corner to go to next is held separately from the corner the train is at:
    /// they are the same corner while stopped.
    /// </summary>
    private void Stop()
    {
        if (_stopped)
            return;

        _stopped = true;
        _pending = _current;

        Halt();
        PlayArriveSound();
    }

    /// <summary>The end of a leg. Stock's Wait, which is not only about waiting.</summary>
    protected override void OnLegDone() => Arrive();

    /// <summary>The end of a pause at a corner.</summary>
    protected override void OnWaitDone() => Advance();

    /// <summary>What arriving at a corner means. See <see cref="ArrivalAt"/>.</summary>
    internal enum Arrival
    {
        /// <summary>Straight on to the next corner, with no pause at all.</summary>
        Advance,

        /// <summary>Sit here for the corner's wait, then carry on by itself.</summary>
        Pause,

        /// <summary>Stop, and stay stopped until something sends for the train.</summary>
        AwaitTrigger,

        /// <summary>Stop, without being marked as stopped. See below.</summary>
        Rest,
    }

    /// <summary>
    /// The one decision arriving at a corner involves, as arithmetic.
    ///
    /// A static taking the three things it depends on rather than reading them
    /// off the entity, because both spawnflag reads ask the ENGINE and there is
    /// none in a test — it would see zero, take the wrong branch and pass for the
    /// wrong reason.
    ///
    /// <para>
    /// Rest and AwaitTrigger both stop the train and differ in one bit, and the
    /// difference is visible to a mapper: a train resting on a -1 wait is not
    /// marked as stopped, so a Start input finds nothing to start and does
    /// nothing. Stop first, then Start, and it goes. That is stock's behaviour
    /// and it looks exactly like a broken train until you know.
    /// </para>
    /// </summary>
    internal static Arrival ArrivalAt(bool cornerAwaitsTrigger, bool trainStopped, float wait)
    {
        // Either end of the arrangement can hold the train: a corner marked
        // wait-for-trigger stops any train, and a train already marked stays
        // stopped once it has arrived somewhere.
        if (cornerAwaitsTrigger || trainStopped)
            return Arrival.AwaitTrigger;

        if (wait == 0f)
            return Arrival.Advance;

        return wait < 0f ? Arrival.Rest : Arrival.Pause;
    }

    private void Arrive()
    {
        // Whoever the mapper wired to this corner hears about it first, before
        // any decision about carrying on: a corner that stops the train still
        // fires as it is passed.
        _current?.Pass(Handle);

        switch (ArrivalAt(_current?.WaitForTrigger ?? false, _stopped, _wait))
        {
            case Arrival.Advance:
                Advance();
                return;

            case Arrival.Pause:
                PlayArriveSound();
                WaitFor(_wait);
                return;

            case Arrival.AwaitTrigger:
                _stopped = true;
                Halt();
                PlayArriveSound();
                return;

            default:
                Halt();
                PlayArriveSound();
                return;
        }
    }

    /// <summary>
    /// Sets off for the next corner.
    ///
    /// The pending corner is read and replaced in one go, which is what makes
    /// <c>SetNextPathCorner</c> behave as it does in stock: the chain is followed
    /// one corner ahead of where the train is, so retargeting the corner the
    /// train is currently heading for arrives too late to change this leg.
    /// </summary>
    private void Advance()
    {
        PathCorner? target = _pending;

        // The end of a chain that does not loop. Stock says nothing about it and
        // simply stops, which is what a mapper who left the last corner blank
        // asked for.
        if (target is null)
        {
            Halt();
            PlayArriveSound();
            return;
        }

        _wait = target.Delay;

        // The leg leaving a corner runs at that corner's speed, so this is the
        // corner being LEFT and not the one being approached. Zero means the
        // corner has nothing to say and the current speed carries.
        if (_current is not null && _current.Speed != 0f)
            _currentSpeed = _current.Speed;

        _current = target;
        _pending = target.Next;

        if (target.Teleport)
        {
            JumpTo(PositionOf(target));
            Arrive();
            return;
        }

        TravelTo(PositionOf(target), _currentSpeed);
    }

    /// <summary>
    /// Where the train's ORIGIN has to be for its middle to sit on a corner.
    ///
    /// Stock computes the same offset and calls its own arithmetic busted in a
    /// comment, because it mixes local and world space; for an unrotated brush —
    /// which every func_train is, having no way to turn — the two agree.
    /// </summary>
    private Vector PositionOf(PathCorner corner) => corner.Position - CentreOffset;
}

/// <summary>
/// The map keys <see cref="FuncTrain"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncTrainVars
{
    /// <summary>The name of the first corner. Only read once, at the deferred start.</summary>
    [VarName("target")]
    public string FirstCornerName { get; set; } = string.Empty;
}
