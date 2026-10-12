//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>What a train near a track changer means for whether it may move.</summary>
public enum TrainCode
{
    /// <summary>Not on this changer, or not on this track at all. Move freely.</summary>
    Safe,

    /// <summary>On it and still running, or half on it. Do not move.</summary>
    Blocking,

    /// <summary>Parked on it. Move, and take the train along.</summary>
    Following,
}

/// <summary>
/// A turntable: a platform big enough to hold a train, which swings between two
/// tracks and takes the train with it.
///
/// It is the piece that makes a branching track usable, and the mechanism is
/// simpler than it looks. Only ONE of the two tracks it names is switched on at a
/// time, and which one is whichever end the platform is at — so a train running
/// off the changer has exactly one way to go, and the changer decides which by
/// being somewhere. Everything else is safety: it will not move while a train is
/// half on it, and it will not move at all while it is already moving.
///
/// <para>
/// The train is carried by hand. There is no parenting on this hierarchy, so
/// instead the train's place on the platform is measured once at departure and
/// reproduced every frame from wherever the platform has got to — position and
/// facing both, so a train on a turntable turns with it. That is the one thing
/// here that needs a frame-by-frame think, and it is why
/// <see cref="TicksEveryFrame"/> exists.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncTrackchange, ScriptedEntityKind.Brush)]
public class FuncTrackChange : FuncPlatRot
{
    public FuncTrackChange(IGameClock clock, IEngineDamage damage, IEnginePrecache precache, IEngineSound sound)
        : base(clock, damage, precache, sound)
    {
    }

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="FuncTrackChangeVars"/>.</summary>
    [ScriptVars] public FuncTrackChangeVars FuncTrackChangeVars = new();

    /// <summary>The node at each end, narrowed to the one nearest the platform.</summary>
    protected PathTrack? TopTrack;
    protected PathTrack? BottomTrack;

    protected FuncTrackTrain? Train;

    /// <summary>Which end the changer is trying to be at. Only the auto version moves it.</summary>
    protected PlatformState TargetState = PlatformState.AtBottom;

    private TrainCode _code = TrainCode.Safe;

    /// <summary>
    /// Whether a player may still call the changer. Raised ONCE, in
    /// <see cref="OnSpawn"/>, and moved afterwards only by the inputs and the
    /// arrival, none of which a hot reload replays -- so it is
    /// <c>[SaveField]</c>. Uncarried, the rebuilt instance read the default and
    /// a changer mid-journey with its use disabled became callable again after
    /// a <c>cs_sv_reload</c> (OnSpawnFieldReloadTests). The auto-changer's loss
    /// is this same declaration, inherited.
    /// </summary>
    [SaveField] private bool _useEnabled;
    private bool _found;

    //-------------------------------------------------------------------------
    // The train's place on the platform, measured at departure.
    //
    // Held in the PLATFORM's frame, which is the whole of carrying it: a
    // position in the platform's frame is a position that turns when the platform
    // turns, and reproducing it every frame is what parenting would have done.
    //-------------------------------------------------------------------------
    private Vector _trainLocalOffset;
    private Vector _trainLocalAngles;

    /// <summary>
    /// func_trackchange's own spawnflags.
    ///
    /// <para>
    /// The numbers are why this is a type rather than a pair of ints: 0x0008 is
    /// "start at the bottom" on a track changer and "passable" on the func_train
    /// and func_tracktrain running along the same track. Nothing in the number says
    /// which brush is being asked.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        StartBottom = 0b0000_1000,
        DontMove = 0b0001_0000,
    }

    /// <summary>
    /// Always, whatever the map's flags say. A changer that wandered back down
    /// after swinging a train into place would take the track away from under it.
    /// </summary>
    protected override bool IsTogglePlat => true;

    /// <summary>Only while it is actually dragging a train. See the note on the class.</summary>
    protected override bool TicksEveryFrame => _code == TrainCode.Following;

    public PathTrack? TopNode => TopTrack;

    public PathTrack? BottomNode => BottomTrack;

    public TrainCode Code => _code;

    public override void OnSpawn()
    {
        SetSolidity(BrushSolidity.Solid);
        SetVisible(true);

        Setup();

        // Bolted to the floor: it turns and does not climb, so both ends are the
        // same place and the journey's duration comes from the turn.
        if (Flags.HasFlag(SpawnFlag.DontMove))
            RotateOnly = true;

        SetupRotation();

        // Unlike a platform, which end it starts at is a spawnflag rather than
        // whether the mapper named it: a changer is always named, being the thing
        // a map's buttons talk to.
        bool startBottom = Flags.HasFlag(SpawnFlag.StartBottom);

        Origin = startBottom ? BottomPosition : TopPosition;
        SetAngles(startBottom ? StartAngles : EndAngles);
        SetPlatformState(startBottom ? PlatformState.AtBottom : PlatformState.AtTop);

        TargetState = startBottom ? PlatformState.AtTop : PlatformState.AtBottom;

        _useEnabled = true;

        PrecacheSounds();
        Precache.PrecacheSound("FuncTrackChange.Blocking");

        // Stock waits two whole seconds before looking for its tracks, and the
        // reason is the same one every deferred start here has — except that this
        // one has to wait for the TRAIN to have found its own track first, so that
        // the nodes it compares against are the ones the train is really on.
        SetNextThink(2f);
    }

    public override void OnThink()
    {
        if (!_found)
        {
            _found = true;
            Find();
            return;
        }

        base.OnThink();
    }

    /// <summary>
    /// Finds the two tracks and the train, and narrows each track to the node
    /// nearest the platform.
    ///
    /// The narrowing is the important half: a map names any node on each branch
    /// and means "the branch", so the changer walks each chain for the node
    /// actually sitting on it. Everything after this compares node identities, and
    /// comparing against the wrong end of a branch would make a train that is
    /// parked look like a train that is elsewhere.
    /// </summary>
    private void Find()
    {
        TopTrack = FindByName<PathTrack>(FuncTrackChangeVars.TopTrackName);

        if (TopTrack is null)
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': " +
                           $"no top track named '{FuncTrackChangeVars.TopTrackName}'\n");
            return;
        }

        BottomTrack = FindByName<PathTrack>(FuncTrackChangeVars.BottomTrackName);

        if (BottomTrack is null)
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': " +
                           $"no bottom track named '{FuncTrackChangeVars.BottomTrackName}'\n");
            return;
        }

        Train = FindByName<FuncTrackTrain>(FuncTrackChangeVars.TrainName);

        if (Train is null)
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': " +
                           $"no func_tracktrain named '{FuncTrackChangeVars.TrainName}'\n");
            return;
        }

        Vector centre = Entities.GetWorldSpaceCenter(Handle);

        TopTrack.LinkChain();
        BottomTrack.LinkChain();

        TopTrack = TopTrack.Nearest(centre);
        BottomTrack = BottomTrack.Nearest(centre);

        UpdateAutoTargets(State);
    }

    //-------------------------------------------------------------------------
    // Deciding whether it is safe to move.
    //-------------------------------------------------------------------------

    /// <summary>
    /// What the train is doing relative to the node at this end.
    ///
    /// The three answers are stock's, and the distances are stock's too — under
    /// the train's own length means parked ON the changer, past a hundred and
    /// fifty plus its length means well clear, and the band between the two is
    /// "half on", which is the case that must not move. A train still under power
    /// is blocking whatever its position, because it is about to be somewhere
    /// else.
    ///
    /// <para>
    /// A changer with no train to switch is not broken — stock says so in a
    /// comment — it is a lift, and it says Safe so that it works as one.
    /// </para>
    /// </summary>
    protected TrainCode EvaluateTrain(PathTrack? here)
    {
        if (here is null || Train is null)
            return TrainCode.Safe;

        PathTrack? on = Train.CurrentNode;

        bool onThisEnd = ReferenceEquals(on, here)
                      || (here.GetPrevious() is not null && ReferenceEquals(on, here.GetPrevious()))
                      || (here.GetNext() is not null && ReferenceEquals(on, here.GetNext()));

        if (!onThisEnd)
            return TrainCode.Safe;

        if (Train.CurrentSpeed != 0f)
            return TrainCode.Blocking;

        Vector gap = Entities.GetOrigin(Train.Handle) - Origin;
        float flat = new Vector(gap.X, gap.Y, 0f).Length;

        if (flat < Train.WheelBase)
            return TrainCode.Following;

        if (flat > 150f + Train.WheelBase)
            return TrainCode.Safe;

        return TrainCode.Blocking;
    }

    /// <summary>
    /// The only thing a map says to a changer.
    ///
    /// Refused outright while it is between ends, and refused with an alarm while
    /// the train is half on it — which is the one piece of feedback the entity
    /// gives, and the reason it precaches a sound at all.
    /// </summary>
    protected override void PlatUse()
    {
        if (State != PlatformState.AtTop && State != PlatformState.AtBottom)
            return;

        _code = EvaluateTrain(State == PlatformState.AtTop ? TopTrack : BottomTrack);

        if (_code == TrainCode.Blocking)
        {
            Sound.EntityEmitSound(Handle, "FuncTrackChange.Blocking");
            return;
        }

        _useEnabled = false;

        if (State == PlatformState.AtTop)
            GoDown();
        else
            GoUp();
    }

    protected bool UseEnabled => _useEnabled;

    protected void EnableUse() => _useEnabled = true;

    protected void DisableUse() => _useEnabled = false;

    //-------------------------------------------------------------------------
    // Moving, with or without a train aboard.
    //-------------------------------------------------------------------------
    protected override void GoDown()
    {
        if (_code == TrainCode.Blocking)
            return;

        // Before the journey, not after: a changer that is bolted down finishes in
        // the same frame it starts, so the nodes have to be switched first.
        UpdateAutoTargets(PlatformState.GoingDown);

        if (_code == TrainCode.Following)
            TakeTrainAboard();

        base.GoDown();
    }

    protected override void GoUp()
    {
        if (_code == TrainCode.Blocking)
            return;

        UpdateAutoTargets(PlatformState.GoingUp);

        if (_code == TrainCode.Following)
            TakeTrainAboard();

        base.GoUp();
    }

    protected override void HitTop()
    {
        base.HitTop();

        HandOverTrack(TopTrack);
    }

    protected override void HitBottom()
    {
        base.HitBottom();

        HandOverTrack(BottomTrack);
    }

    private void HandOverTrack(PathTrack? track)
    {
        // One last carry, so that the train lands exactly where the platform did
        // rather than a frame behind it.
        if (_code == TrainCode.Following)
        {
            CarryTrain();

            if (Train is not null && track is not null)
                Train.SetTrack(track);
        }

        _code = TrainCode.Safe;

        UpdateAutoTargets(State);

        EnableUse();
    }

    /// <summary>
    /// Measures where the train is sitting, and takes its track away.
    ///
    /// The track has to go: while the platform is swinging, the train is not on
    /// any track at all, and a train left holding a node would carry on running
    /// along it in mid-air. Stock nulls the same pointer for the same reason, and
    /// the node comes back at the far end.
    /// </summary>
    private void TakeTrainAboard()
    {
        if (Train is null)
            return;

        Vector platformAngles = Entities.GetAngles(Handle);

        _trainLocalOffset = FuncTrackTrain.ToLocal(platformAngles,
                                                   Entities.GetOrigin(Train.Handle) - Origin);

        Vector trainAngles = Entities.GetAngles(Train.Handle);

        _trainLocalAngles = new Vector(trainAngles.X - platformAngles.X,
                                       trainAngles.Y - platformAngles.Y,
                                       trainAngles.Z - platformAngles.Z);

        Train.LosePath();
    }

    protected override void WhileTravelling() => CarryTrain();

    /// <summary>
    /// Puts the train back where it was sitting, relative to a platform that has
    /// since moved and turned.
    ///
    /// The inverse of the measurement above: a local offset rotated back out by
    /// the platform's current facing. This is what parenting does, done by hand
    /// once a frame, and it is the only way a train rides a turntable here.
    /// </summary>
    private void CarryTrain()
    {
        if (Train is null || _code != TrainCode.Following)
            return;

        Vector platformAngles = Entities.GetAngles(Handle);
        var basis = Angles.Vectors(platformAngles);

        // Forward, LEFT and up, matching ToLocal — see the note there about why
        // the Y axis is the left one.
        Vector world = basis.Forward * _trainLocalOffset.X
                     + basis.Right * -_trainLocalOffset.Y
                     + basis.Up * _trainLocalOffset.Z;

        Entities.Teleport(Train.Handle, Origin + world,
                              new Vector(platformAngles.X + _trainLocalAngles.X,
                                         platformAngles.Y + _trainLocalAngles.Y,
                                         platformAngles.Z + _trainLocalAngles.Z),
                              zeroVelocity: true);
    }

    //-------------------------------------------------------------------------
    // Which branch is live.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Switches on the node at whichever end the platform is, and switches off the
    /// other.
    ///
    /// Both off while it is in transit, which falls out of the arithmetic rather
    /// than being a case: neither end matches GoingUp or GoingDown. That is what
    /// stops a train from setting off onto a branch that has swung away.
    /// </summary>
    protected virtual void UpdateAutoTargets(PlatformState state)
    {
        if (TopTrack is null || BottomTrack is null)
            return;

        TopTrack.SetDisabled(state != PlatformState.AtTop);
        BottomTrack.SetDisabled(state != PlatformState.AtBottom);
    }

    /// <summary>
    /// Within the assembly so a test can hand a changer its two nodes and its
    /// train, the lookups above being the only part that needs a map.
    /// </summary>
    internal void Wire(PathTrack top, PathTrack bottom, FuncTrackTrain train)
    {
        TopTrack = top;
        BottomTrack = bottom;
        Train = train;

        UpdateAutoTargets(State);
    }

    /// <summary>The same, for a changer placed at an end with no engine to travel in.</summary>
    internal void PlaceAt(PlatformState state) => SetPlatformState(state);

    /// <summary>What the map's Use press does, reachable from a test.</summary>
    internal void Pressed() => PlatUse();
}

/// <summary>
/// A track changer that a train triggers by arriving, rather than one a map's
/// button presses.
///
/// The difference is which node it switches. An ordinary changer switches the two
/// nodes ON its ends, so a train has one way off it; this one switches the nodes
/// AFTER those, so a train running toward the changer already knows which way it
/// will leave before it gets there. That is what makes it automatic — there is no
/// moment where the train is waiting for the platform to decide.
///
/// <para>
/// It also starts a stopped train, which the ordinary one does not: having
/// switched the branch, the node beyond may carry a speed, and handing that to a
/// parked train is how a map makes a train continue after a change without wiring
/// anything.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncTrackautochange, ScriptedEntityKind.Brush)]
public sealed class FuncTrackAuto : FuncTrackChange
{
    public FuncTrackAuto(IGameClock clock, IEngineDamage damage, IEngineNetworking net,
                         IEnginePrecache precache, IEngineSound sound, IEngineWorldQuery world)
        : base(clock, damage, precache, sound)
    {
    }

    /// <summary>
    /// Switches the nodes one PAST each end, and gives a parked train the speed
    /// the newly live node asks for.
    /// </summary>
    protected override void UpdateAutoTargets(PlatformState state)
    {
        if (TopNode is null || BottomNode is null)
            return;

        bool wantTop = TargetState == PlatformState.AtTop;

        PathTrack? live = (wantTop ? TopNode : BottomNode).GetNext();
        PathTrack? dead = (wantTop ? BottomNode : TopNode).GetNext();

        if (live is not null)
        {
            live.SetDisabled(false);

            if (Code == TrainCode.Following && Train is not null && Train.CurrentSpeed == 0f)
            {
                Train.SetSpeed(live.NodeSpeed);

                // Stock's nudge, with a value of zero: it asks the train to take
                // up the throttle notch it has just been given rather than naming
                // a new one.
                Train.OnUse(0f);
            }
        }

        dead?.SetDisabled(true);
    }

    /// <summary>
    /// A train arriving is what makes this move; anything else only flips which
    /// end it is aiming for.
    ///
    /// The split is on WHO fired it, which is the one place in this batch where a
    /// managed entity has to ask what an activator is. A train announcing itself
    /// means "I am here, swing if you are going to"; a button means "swap which
    /// way the branch points, and let the next train find it that way".
    /// </summary>
    [ScriptedEvent("Trigger")]
    private void OnTrigger(InputData data) => Triggered(data.Activator);

    private void Triggered(EntityHandle activator)
    {
        if (!UseEnabled)
            return;

        PathTrack? here = State switch
        {
            PlatformState.AtTop => TopNode,
            PlatformState.AtBottom => BottomNode,
            _ => null,
        };

        if (Entities.GetClassName(activator) == EntityNames.FuncTracktrain)
        {
            if (EvaluateTrain(here) != TrainCode.Following || State == TargetState)
                return;

            DisableUse();

            if (State == PlatformState.AtTop)
                GoDown();
            else
                GoUp();

            return;
        }

        PathTrack? beyond = here?.GetNext();

        // Only flip when the train is not already committed to the branch that
        // would become live, which is stock's guard: swapping under a train that
        // is heading down the new branch would disable the node it is running to.
        if (beyond is not null && !ReferenceEquals(Train?.CurrentNode, beyond))
        {
            TargetState = TargetState == PlatformState.AtTop ? PlatformState.AtBottom
                                                             : PlatformState.AtTop;
        }

        UpdateAutoTargets(TargetState);
    }
}

/// <summary>
/// The map keys <see cref="FuncTrackChange"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncTrackChangeVars
{
    [VarName("toptrack")]
    public string TopTrackName { get; set; } = string.Empty;

    [VarName("bottomtrack")]
    public string BottomTrackName { get; set; } = string.Empty;

    [VarName("train")]
    public string TrainName { get; set; } = string.Empty;
}
