using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The keyframed path, and the measurement that lives beside it.
///
/// Two things worth checking without a game. A keyframe chain is a set of names
/// resolved into links, and a link that goes the wrong way does not fail — the
/// mover simply travels somewhere else, which reads as a mapper's mistake rather
/// than a bug here. And logic_measure_movement is a change of frame done twice,
/// where a sign error puts the target somewhere plausible and wrong; the two
/// invariants below pin it, because both have answers that can be known without
/// doing the arithmetic the code does.
/// </summary>
public class PathTests
{

    private static ScriptedEntityRegistry NewRegistry()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(KeyframeTrack).Assembly);
        registry.Services = Ignoring("Clock.Now", "Entities.GetAngles", "Entities.GetBounds", "Entities.GetBoundsSize", "Entities.GetOrigin", "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles", "Entities.IsValid");
        return registry;
    }

    /// <summary>
    /// A key at a place, with a speed.
    ///
    /// The speed goes in as a map key, which is the real path; the position does
    /// not, because a key reads its own position from where the mapper drew it
    /// and there is no map here to have drawn it.
    /// </summary>
    private static T Key<T>(ScriptedEntityRegistry registry, uint handle, string className,
                            Vector origin, Vector angles, string speed) where T : KeyframeTrack
    {
        var h = new EntityHandle(handle);

        registry.KeyValue(h, className, "MoveSpeed", speed);
        registry.Spawn(h, className);

        var key = (T)registry.Get(h)!;
        key.KeyOrigin = origin;
        key.KeyAngles = angles;

        return key;
    }

    private static KeyframeTrack Key(ScriptedEntityRegistry registry, uint handle,
                                     Vector origin, Vector angles, string speed)
        => Key<KeyframeTrack>(registry, handle, EntityNames.KeyframeTrack, origin, angles, speed);

    [Fact]
    public void KeyDurations()
    {
        var registry = NewRegistry();

        var first = Key(registry, 0x0041_0001u, new Vector(0f, 0f, 0f), Vector.Zero, "100");
        var second = Key(registry, 0x0041_0002u, new Vector(200f, 0f, 0f), Vector.Zero, "100");

        Assert.True(first.TimeToNext == 0f, "a key with nothing after it takes no time to leave");

        first.LinkTo(second);

        Assert.True(Near(first.TimeToNext, 2f), "distance over speed is the leg's duration");

        // Two keys in the same place, differing only in facing: the speed is read
        // as degrees per second instead.
        var turnFrom = Key(registry, 0x0041_0003u, Vector.Zero, new Vector(0f, 0f, 0f), "45");
        var turnTo = Key(registry, 0x0041_0004u, Vector.Zero, new Vector(0f, 90f, 0f), "45");

        turnFrom.LinkTo(turnTo);

        // 90 degrees at 45 a second would be two, and it is six: the delta is
        // -90, which stock wraps to 270 before measuring it. Pinned deliberately
        // — see the note on TimeToNext — because a map's timings were built
        // against the version that goes the long way round.
        Assert.True(Near(turnFrom.TimeToNext, 6f), "a turn one way measures the long way round");

        var backFrom = Key(registry, 0x0041_0005u, Vector.Zero, new Vector(0f, 90f, 0f), "45");
        var backTo = Key(registry, 0x0041_0006u, Vector.Zero, new Vector(0f, 0f, 0f), "45");

        backFrom.LinkTo(backTo);

        Assert.True(Near(backFrom.TimeToNext, 2f), "and the same turn the other way measures the short way");

        var noSpeed = Key(registry, 0x0041_0007u, Vector.Zero, Vector.Zero, "0");
        noSpeed.LinkTo(second);

        Assert.True(noSpeed.TimeToNext == 0f, "a key with no speed has no duration");
    }

    [Fact]
    public void ChainLinking()
    {
        var registry = NewRegistry();

        var a = Key(registry, 0x0042_0001u, Vector.Zero, Vector.Zero, "100");
        var b = Key(registry, 0x0042_0002u, new Vector(100f, 0f, 0f), Vector.Zero, "100");
        var c = Key(registry, 0x0042_0003u, new Vector(200f, 0f, 0f), Vector.Zero, "100");

        a.LinkTo(b);
        b.LinkTo(c);

        Assert.True(ReferenceEquals(b.NextKey(1), c), "a key points at the one it names");

        // The back-link is the half a map cannot express, so it is the half worth
        // checking: nothing in a map says what comes before a key.
        Assert.True(ReferenceEquals(b.NextKey(-1), a), "and back at whoever named it");
        Assert.True(ReferenceEquals(b.PrevKey(1), a), "PrevKey reads the same link the other way up");
        Assert.True(ReferenceEquals(b.PrevKey(-1), c), "and negating the direction swaps them");

        Assert.True(ReferenceEquals(b.NextKey(0), b), "direction zero is this key, which is stock's answer for not moving");

        Assert.True(c.NextKey(1) is null, "the end of a chain answers nothing");
    }

    [Fact]
    public void MoverRoundTrip()
    {
        var registry = NewRegistry();

        var mover = Key<MoveKeyframed>(registry, 0x0043_0001u, EntityNames.MoveKeyframed,
                                       Vector.Zero, Vector.Zero, "100");
        var middle = Key(registry, 0x0043_0002u, new Vector(100f, 0f, 0f), Vector.Zero, "100");
        var last = Key(registry, 0x0043_0003u, new Vector(200f, 0f, 0f), Vector.Zero, "100");

        mover.LinkTo(middle);
        middle.LinkTo(last);

        Assert.True(ReferenceEquals(mover.CurrentKey, mover), "a mover is its own first key");
        Assert.True(mover.Direction == 0, "and is at rest until it has looked at the chain");

        mover.OnThink();

        Assert.True(mover.Direction == 1 && ReferenceEquals(mover.CurrentKey, mover)
                                 && ReferenceEquals(mover.TargetKey, middle), "the first leg runs forward off the mover itself");

        mover.OnThink();

        Assert.True(ReferenceEquals(mover.CurrentKey, middle) && ReferenceEquals(mover.TargetKey, last), "and then to the next pair");

        // The end of the chain: the leg forward cannot be made, so the opposite
        // one is tried. Turning round rather than stopping is what makes this a
        // round trip, and it is the one place the direction can change.
        mover.OnThink();

        Assert.True(mover.Direction == -1 && ReferenceEquals(mover.CurrentKey, last)
                                  && ReferenceEquals(mover.TargetKey, middle), "the end of the chain turns it round");

        mover.OnThink();

        Assert.True(ReferenceEquals(mover.CurrentKey, middle) && ReferenceEquals(mover.TargetKey, mover), "and it walks back down the same links");

        mover.OnThink();

        Assert.True(mover.Direction == 1 && ReferenceEquals(mover.CurrentKey, mover)
                                 && ReferenceEquals(mover.TargetKey, middle), "the other end turns it round again");
    }

    [Fact]
    public void MoverWithNoChain()
    {
        var registry = NewRegistry();

        var alone = Key<MoveKeyframed>(registry, 0x0044_0001u, EntityNames.MoveKeyframed,
                                       Vector.Zero, Vector.Zero, "100");
        alone.LinkTo(null);

        // Neither direction has anywhere to go, and the reversal must not become
        // a loop between two failures.
        alone.OnThink();

        Assert.True(alone.Direction == 0 && alone.TargetKey is null, "a mover named nothing comes to rest");
        Assert.True(ReferenceEquals(alone.CurrentKey, alone), "and still knows where it is");
    }

    [Fact]
    public void MeasuredPlacement()
    {
        // The strongest invariant available, because the answer is known without
        // doing the arithmetic: measure something against a reference and then
        // hand back the SAME reference to place against, and whatever comes out
        // has to be exactly where the measured thing is.
        var (origin, angles) = LogicMeasureMovement.Place(
            measureOrigin: new Vector(10f, 20f, 30f), measureAngles: new Vector(20f, 45f, 0f),
            measureReferenceOrigin: new Vector(100f, -50f, 0f), measureReferenceAngles: new Vector(0f, -30f, 0f),
            targetReferenceOrigin: new Vector(100f, -50f, 0f), targetReferenceAngles: new Vector(0f, -30f, 0f),
            scale: 1f);

        Assert.True(Near(origin.X, 10f) && Near(origin.Y, 20f) && Near(origin.Z, 30f), "one reference used twice places the target on the measured entity");

        Assert.True(Near(angles.X, 20f) && Near(angles.Y, 45f) && Near(angles.Z, 0f), "with the measured entity's own facing");

        // No rotation anywhere, so the only thing left is the offset: a hundred
        // units behind its own reference becomes a hundred units behind the
        // other one.
        var (offsetOrigin, _) = LogicMeasureMovement.Place(
            measureOrigin: Vector.Zero, measureAngles: Vector.Zero,
            measureReferenceOrigin: new Vector(100f, 0f, 0f), measureReferenceAngles: Vector.Zero,
            targetReferenceOrigin: new Vector(500f, 0f, 0f), targetReferenceAngles: Vector.Zero,
            scale: 1f);

        Assert.True(Near(offsetOrigin.X, 400f) && Near(offsetOrigin.Y, 0f) && Near(offsetOrigin.Z, 0f), "an offset from one reference is reproduced from the other");

        var (scaledOrigin, scaledAngles) = LogicMeasureMovement.Place(
            measureOrigin: Vector.Zero, measureAngles: Vector.Zero,
            measureReferenceOrigin: new Vector(100f, 0f, 0f), measureReferenceAngles: Vector.Zero,
            targetReferenceOrigin: new Vector(500f, 0f, 0f), targetReferenceAngles: Vector.Zero,
            scale: 2f);

        Assert.True(Near(scaledOrigin.X, 450f), "a scale of two halves the offset");

        // The scale divides the offset and nothing else. A half-size copy of a
        // room is still the same way up.
        Assert.True(Near(scaledAngles.Y, 0f), "and leaves the facing alone");
    }

    //-------------------------------------------------------------------------
    // path_corner and the trains that walk it.
    //
    // A corner chain is the other half of the same idea as a keyframe chain and
    // gets its links a different way on purpose — forward only, resolved on
    // demand — so the things worth pinning here are the differences: that a
    // retarget mid-journey is actually seen, and that a train reads the wait off
    // one corner and the speed off the one behind it, which is the asymmetry
    // most likely to be quietly "corrected" by somebody tidying up.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A corner with a wait and a speed, and no position.
    ///
    /// Unlike a keyframe, a corner has no position to be given: it reads its own
    /// live, which is what makes what follows testable without an engine — every
    /// assertion below is about which corner comes next and what it contributes,
    /// and none of it is about geometry.
    /// </summary>
    private static PathCorner Corner(ScriptedEntityRegistry registry, uint handle,
                                     string wait = "0", string speed = "0")
    {
        var h = new EntityHandle(handle);

        registry.KeyValue(h, EntityNames.PathCorner, "wait", wait);
        registry.KeyValue(h, EntityNames.PathCorner, "speed", speed);
        registry.Spawn(h, EntityNames.PathCorner);

        return (PathCorner)registry.Get(h)!;
    }

    private static FuncTrain Train(ScriptedEntityRegistry registry, uint handle, string speed = "100")
    {
        var h = new EntityHandle(handle);

        registry.KeyValue(h, EntityNames.FuncTrain, "speed", speed);
        registry.Spawn(h, EntityNames.FuncTrain);

        return (FuncTrain)registry.Get(h)!;
    }

    [Fact]
    public void CornerChain()
    {
        var registry = NewRegistry();

        var a = Corner(registry, 0x0051_0001u, wait: "3", speed: "250");
        var b = Corner(registry, 0x0051_0002u);

        Assert.True(Near(a.Delay, 3f), "a corner keeps the wait the map gave it");
        Assert.True(Near(a.Speed, 250f), "and the speed, which is a CBaseEntity key and not one of its own");

        Assert.True(b.Next is null, "a corner naming nothing ends the chain");

        a.LinkTo(b);

        Assert.True(ReferenceEquals(a.Next, b), "a corner points at the one it names");

        // A corner chain is one-way. Nothing that walks corners turns round —
        // the entity that does walks path_track — so there is no link to check
        // the other way and that is the design rather than a gap.
        Assert.True(b.Next is null, "and nothing points back");

        // Both the input a map can fire and the direct call a managed mover
        // makes reach the same output, because a corner that stops firing when
        // the mover changes is a broken map that looks like a broken train.
        registry.Input(new EntityHandle(0x0051_0001u), "InPass", "", 0f, default);
        Assert.True(a.OnPass.FireCount == 1, "the InPass input fires OnPass");
    }

    [Fact]
    public void CornerRetargeting()
    {
        var registry = NewRegistry();

        var a = Corner(registry, 0x0052_0001u);
        var b = Corner(registry, 0x0052_0002u);

        a.LinkTo(b);

        registry.Input(new EntityHandle(0x0052_0001u), "SetNextPathCorner", "somewhere_else", 0f, default);

        // The whole reason a corner resolves its link on demand rather than
        // linking the chain once: this input exists, and a link cached at spawn
        // would outlive the name that produced it. There is no engine here to
        // resolve the new name against, so what is checked is that the OLD
        // answer is gone.
        Assert.True(a.Next is null, "retargeting a corner drops the link it had");
    }

    [Fact]
    public void TrainWalksItsCorners()
    {
        var registry = NewRegistry();

        var first = Corner(registry, 0x0053_0001u);
        var second = Corner(registry, 0x0053_0002u);
        var third = Corner(registry, 0x0053_0003u);

        first.LinkTo(second);
        second.LinkTo(third);

        var train = Train(registry, 0x0053_0010u);

        Assert.True(train.CurrentCorner is null, "a train has no corner until it has looked for one");

        train.TakeFirstCorner(first);

        // Unnamed, because there is no engine to have given it a name, which is
        // also the case a mapper gets when they do not name one: nothing could
        // ever send for it, so it sends for itself.
        Assert.True(ReferenceEquals(train.CurrentCorner, first), "a train starts on its first corner");
        Assert.True(ReferenceEquals(train.PendingCorner, second), "and already knows the one after");
        Assert.True(!train.WaitingForTrigger, "an unnamed train does not wait to be sent for");

        // The 0.1s stock waits between arriving on the first corner and setting
        // off from it.
        train.FinishStep();

        Assert.True(ReferenceEquals(train.CurrentCorner, second) && ReferenceEquals(train.PendingCorner, third), "the first leg runs to the second corner");

        train.FinishStep();

        Assert.True(second.OnPass.FireCount == 1, "arriving fires the corner's OnPass");
        Assert.True(ReferenceEquals(train.CurrentCorner, third) && train.PendingCorner is null, "and with no wait it carries straight on");

        train.FinishStep();

        Assert.True(third.OnPass.FireCount == 1, "the last corner fires as it is passed");
        Assert.True(ReferenceEquals(train.CurrentCorner, third), "and the train stays on it");
    }

    [Fact]
    public void TrainSpeedComesFromTheCornerBehind()
    {
        var registry = NewRegistry();

        // Only the FIRST corner names a speed. If the leg took its speed from
        // the corner it is heading for, this would never be read at all.
        var first = Corner(registry, 0x0054_0001u, speed: "400");
        var second = Corner(registry, 0x0054_0002u);

        first.LinkTo(second);

        var train = Train(registry, 0x0054_0010u, speed: "100");

        train.TakeFirstCorner(first);
        Assert.True(Near(train.LegSpeed, 100f), "a train sets off at the speed the map gave it");

        train.FinishStep();

        Assert.True(Near(train.LegSpeed, 400f), "and the leg leaving a corner runs at THAT corner's speed");

        train.FinishStep();

        // The second corner names no speed, so nothing overrides it and the
        // train keeps going at what the first corner set. A zero means "nothing
        // to say", not "stop".
        Assert.True(Near(train.LegSpeed, 400f), "a corner with no speed leaves the train's alone");
    }

    [Fact]
    public void TrainArrivalRules()
    {
        Assert.True(FuncTrain.ArrivalAt(cornerAwaitsTrigger: false, trainStopped: false, wait: 0f)
                == FuncTrain.Arrival.Advance, "no wait and nothing holding it means straight on");

        Assert.True(FuncTrain.ArrivalAt(false, false, 4f) == FuncTrain.Arrival.Pause, "a positive wait is a pause it comes out of by itself");

        // -1 is stock's "wait forever", and it is NOT the same as being stopped:
        // the train is not marked, so a Start input finds nothing to start. A
        // Stop then a Start is what moves it, which reads as a bug and is not.
        Assert.True(FuncTrain.ArrivalAt(false, false, -1f) == FuncTrain.Arrival.Rest, "a negative wait rests without being marked as stopped");

        Assert.True(FuncTrain.ArrivalAt(true, false, 0f) == FuncTrain.Arrival.AwaitTrigger, "a corner marked wait-for-trigger holds any train");

        // The same bit on the train itself, which is how stock stops one: it is
        // set while the train is in flight and only acted on when it arrives.
        Assert.True(FuncTrain.ArrivalAt(false, true, 0f) == FuncTrain.Arrival.AwaitTrigger, "and a train already marked stays marked when it arrives");

        Assert.True(FuncTrain.ArrivalAt(true, false, 4f) == FuncTrain.Arrival.AwaitTrigger, "being held beats a wait that would have been served");
    }

    [Fact]
    public void TrainStopRewindsOneCorner()
    {
        var registry = NewRegistry();

        var first = Corner(registry, 0x0055_0001u);
        var second = Corner(registry, 0x0055_0002u);
        var third = Corner(registry, 0x0055_0003u);

        first.LinkTo(second);
        second.LinkTo(third);

        var train = Train(registry, 0x0055_0010u);
        var handle = new EntityHandle(0x0055_0010u);

        train.TakeFirstCorner(first);
        train.FinishStep();

        Assert.True(ReferenceEquals(train.CurrentCorner, second), "the train is on its way to the second corner");

        registry.Input(handle, "Stop", "", 0f, default);

        Assert.True(train.WaitingForTrigger, "Stop marks the train");

        // The rewind. Without it, starting again would skip the corner the train
        // was already most of the way to — so a stopped train would jump a leg
        // every time a mapper's button was pressed.
        Assert.True(ReferenceEquals(train.PendingCorner, second), "and rewinds the pending corner onto the one it was heading for");

        registry.Input(handle, "Start", "", 0f, default);

        Assert.True(!train.WaitingForTrigger, "Start clears the mark");
        Assert.True(ReferenceEquals(train.CurrentCorner, second) && ReferenceEquals(train.PendingCorner, third), "and finishes the leg it was on rather than skipping it");
    }

    //-------------------------------------------------------------------------
    // path_track, and the train and the turntable that run it.
    //
    // The corner chain above is a one-way walk with nothing to get wrong but the
    // order. A track is doubly linked, can fork, and its nodes can be switched
    // off under a train that is heading for them — so the things pinned below are
    // the ones where a mistake gives a plausible wrong answer rather than a
    // crash: which link a fork stands in for, what "past this node" means when
    // the distance runs out exactly on it, and which of two branches is live.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A node at a place, with the one key a train reads off it.
    ///
    /// The position has to be given rather than read, for the reason on
    /// PathTrack.PlaceAt: everything geometric below is about where nodes are, and
    /// there is no map here to have drawn them.
    /// </summary>
    private static PathTrack Node(ScriptedEntityRegistry registry, uint handle,
                                  Vector position, string speed = "0")
    {
        var h = new EntityHandle(handle);

        registry.KeyValue(h, EntityNames.PathTrack, "speed", speed);
        registry.Spawn(h, EntityNames.PathTrack);

        var node = (PathTrack)registry.Get(h)!;
        node.PlaceAt(position);

        return node;
    }

    private static FuncTrackTrain TrackTrain(ScriptedEntityRegistry registry, uint handle,
                                             string speed = "100", string wheels = "0")
    {
        var h = new EntityHandle(handle);

        registry.KeyValue(h, EntityNames.FuncTracktrain, "speed", speed);
        registry.KeyValue(h, EntityNames.FuncTracktrain, "wheels", wheels);
        registry.Spawn(h, EntityNames.FuncTracktrain);

        return (FuncTrackTrain)registry.Get(h)!;
    }

    /// <summary>
    /// SF_TRACKTRAIN_UNBLOCKABLE_BY_PLAYER (0x200) is FL_UNBLOCKABLE_BY_PLAYER
    /// (trains.cpp, CFuncTrackTrain::Spawn): a player in the train's way is
    /// pushed where the push says rather than blocking it.
    /// </summary>
    [Fact]
    public void ATrackTrainTickedUnblockableByPlayerIsUnblockableByPlayer()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(KeyframeTrack).Assembly);
        registry.Services = Services(out RecordingEngine engine);
        engine.Ignores("Clock.Now", "Entities.GetAngles", "Entities.GetBounds", "Entities.GetBoundsSize",
                       "Entities.GetOrigin", "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles",
                       "Entities.IsValid");
        engine.Returns("Entities.GetSpawnFlags", 0x200);

        FuncTrackTrain train = TrackTrain(registry, 0x0071_0001u);

        Assert.True(train.UnblockableByPlayer);

        engine.Returns("Entities.GetSpawnFlags", 0x100);
        Assert.False(train.UnblockableByPlayer);
    }

    [Fact]
    public void TrackChainLinking()
    {
        var registry = NewRegistry();

        var a = Node(registry, 0x0061_0001u, Vector.Zero);
        var b = Node(registry, 0x0061_0002u, new Vector(100f, 0f, 0f));
        var c = Node(registry, 0x0061_0003u, new Vector(300f, 0f, 0f));

        a.LinkTo(b);
        b.LinkTo(c);

        Assert.True(ReferenceEquals(b.GetNext(), c), "a node points at the one it names");

        // The half a map cannot express. Unlike a path_corner, which has no such
        // link at all, this is the one a train needs in order to reverse.
        Assert.True(ReferenceEquals(b.GetPrevious(), a), "and back at whoever named it");

        Assert.True(ReferenceEquals(b.NextKey(1), c), "a direction of 1 is forward");
        Assert.True(ReferenceEquals(b.NextKey(-1), a), "and -1 is back");
        Assert.True(ReferenceEquals(b.NextKey(0), b), "and zero is this node, which is stock's answer for not moving");

        Assert.True(c.GetNext() is null, "the end of a track answers nothing");
        Assert.True(a.GetPrevious() is null, "and so does the start, the other way");
    }

    [Fact]
    public void TrackForks()
    {
        var registry = NewRegistry();

        var junction = Node(registry, 0x0062_0001u, Vector.Zero);
        var straight = Node(registry, 0x0062_0002u, new Vector(100f, 0f, 0f));
        var branch = Node(registry, 0x0062_0003u, new Vector(0f, 100f, 0f));

        junction.LinkTo(straight);
        junction.LinkAlternate(branch);

        Assert.True(ReferenceEquals(junction.GetNext(), straight), "a node with a fork it has not been told to take goes straight on");

        Assert.True(junction.HasAlternatePath, "but it knows the fork is there");

        registry.Input(new EntityHandle(0x0062_0001u), "EnableAlternatePath", "", 0f, default);

        Assert.True(ReferenceEquals(junction.GetNext(), branch), "armed, the fork stands in for the way forward");

        // The way BACK is untouched, which is the default and is why there is a
        // spawnflag to swap which link the fork replaces: one junction then serves
        // a train going either way round a loop.
        Assert.True(junction.GetPrevious() is null, "and leaves the way back alone");

        registry.Input(new EntityHandle(0x0062_0001u), "DisableAlternatePath", "", 0f, default);

        Assert.True(ReferenceEquals(junction.GetNext(), straight), "disarmed, it goes straight on again");

        // Both ends of a fork are back-linked to the junction that named them,
        // because a map only ever names forward and a train has to be able to come
        // back off either.
        Assert.True(ReferenceEquals(branch.GetPrevious(), junction), "a fork back-links as an ordinary link does");

        var lonely = Node(registry, 0x0062_0004u, Vector.Zero);
        registry.Input(new EntityHandle(0x0062_0004u), "EnableAlternatePath", "", 0f, default);

        // Stock's guard, and it matters: without it a node could claim to be
        // forked while GetNext answered the ordinary link.
        Assert.True(!lonely.HasAlternatePath, "arming a fork that does not exist does nothing");
    }

    [Fact]
    public void TrackDisabling()
    {
        var registry = NewRegistry();

        var a = Node(registry, 0x0063_0001u, Vector.Zero);
        var b = Node(registry, 0x0063_0002u, new Vector(100f, 0f, 0f));

        a.LinkTo(b);

        Assert.True(!b.IsDisabled, "a node is on to begin with");
        Assert.True(ReferenceEquals(PathTrack.ValidPath(b), b), "and a valid path is itself");

        registry.Input(new EntityHandle(0x0063_0002u), "DisablePath", "", 0f, default);

        Assert.True(PathTrack.ValidPath(b) is null, "switched off, it is no longer a valid path");

        // The LINK is still there, and that distinction is what makes a track
        // change work: the node has not gone, it is closed, and something
        // measuring rather than travelling still has to reach it.
        Assert.True(ReferenceEquals(a.GetNext(), b), "but the link to it has not gone");
        Assert.True(ReferenceEquals(PathTrack.ValidPath(b, testDisabled: false), b), "and a walk that ignores the switch still finds it");

        registry.Input(new EntityHandle(0x0063_0002u), "TogglePath", "", 0f, default);

        Assert.True(!b.IsDisabled, "toggling brings it back");

        Assert.True(PathTrack.ValidPath(null) is null, "nothing is never a valid path");
    }

    [Fact]
    public void TrackLookAhead()
    {
        var registry = NewRegistry();

        var a = Node(registry, 0x0064_0001u, Vector.Zero);
        var b = Node(registry, 0x0064_0002u, new Vector(100f, 0f, 0f));
        var c = Node(registry, 0x0064_0003u, new Vector(300f, 0f, 0f));

        a.LinkTo(b);
        b.LinkTo(c);

        var shortHop = a.LookAheadFrom(a.Position, 50f, respectDisabled: true);

        Assert.True(ReferenceEquals(shortHop.Node, a) && Near(shortHop.Origin.X, 50f), "stopping short of the next node leaves you past this one");
        Assert.True(ReferenceEquals(shortHop.NextNext, b), "and one further on is the node you were heading for");

        // Landing exactly on a node is the case worth pinning, because "past it"
        // and "at it" are the same place and the two readings differ by one node.
        var exact = a.LookAheadFrom(a.Position, 100f, respectDisabled: true);

        Assert.True(ReferenceEquals(exact.Node, b) && Near(exact.Origin.X, 100f), "a distance that runs out exactly on a node is past THAT node");
        Assert.True(ReferenceEquals(exact.NextNext, c), "and the one beyond is the one after that");

        var acrossTwo = a.LookAheadFrom(a.Position, 150f, respectDisabled: true);

        Assert.True(ReferenceEquals(acrossTwo.Node, b) && Near(acrossTwo.Origin.X, 150f), "a look that crosses a node measures the rest along the next segment");

        // Backward, from the far end. A negative distance is the whole of looking
        // the other way, which is what lets one method serve a train in reverse.
        var backward = c.LookAheadFrom(c.Position, -150f, respectDisabled: true);

        Assert.True(ReferenceEquals(backward.Node, c) && Near(backward.Origin.X, 150f), "a negative distance walks the track the other way");
        Assert.True(ReferenceEquals(backward.NextNext, b), "and reports the node it was heading back to");

        // Past the end, two ways. A train that is MOVING wants to be told it has
        // run out; one that is measuring where its nose will be wants the position
        // extended along the last segment, or it would believe its front was stuck
        // at the last node forever.
        var ranOut = a.LookAheadFrom(a.Position, 1000f, respectDisabled: true);

        Assert.True(ranOut.Node is null, "running out of track answers no node");
        Assert.True(Near(ranOut.Origin.X, 300f), "and stops at the last one");

        var projected = a.LookAheadFrom(a.Position, 1000f, respectDisabled: false);

        Assert.True(projected.Node is null && Near(projected.Origin.X, 1000f), "a measuring look projects past the end instead");

        // A node switched off ends the walk exactly as the end of the track does,
        // which is how a track change stops a train short of a branch that has
        // swung away.
        b.SetDisabled(true);

        var blocked = a.LookAheadFrom(a.Position, 150f, respectDisabled: true);

        Assert.True(blocked.Node is null, "a switched-off node ends the walk");
    }

    /// <summary>A node the map names and points onward by name, as a map does.</summary>
    private static PathTrack Named(ScriptedEntityRegistry registry, uint handle, string name, string next)
    {
        var h = new EntityHandle(handle);

        registry.KeyValue(h, EntityNames.PathTrack, "targetname", name);

        if (next.Length > 0)
            registry.KeyValue(h, EntityNames.PathTrack, "target", next);

        registry.Spawn(h, EntityNames.PathTrack);
        return (PathTrack)registry.Get(h)!;
    }

    /// <summary>The warnings a link walk from <paramref name="head"/> printed.</summary>
    private static List<string> WalkWarnings(PathTrack head)
    {
        // The registry's console lines (statics to zero), which its entities print to.
        var lines = new List<string>();
        var console = head.Owner!.Lines;
        console.Tap = (text, warning) => { if (warning) lines.Add(text); };

        try
        {
            head.LinkChain();
        }
        finally
        {
            console.Tap = null;
        }

        return lines;
    }

    [Fact]
    public void OneLinkWalkReachesEveryNodeOfTheChain()
    {
        // The walk from the head resolves every node it reaches, not only the
        // first. Seen through the one thing a node says when it resolves: with
        // no engine a name finds nothing, so the SECOND node reports its dead
        // end -- which it can only do if the head's walk reached it.
        var registry = NewRegistry();

        var a = Named(registry, 0x0066_0001u, "track_a", "");
        var b = Named(registry, 0x0066_0002u, "track_b", "track_missing");
        a.LinkTo(b);

        var warnings = WalkWarnings(a);

        Assert.Contains(warnings, w => w.Contains("path_track 'track_b': dead end link 'track_missing'"));
    }

    [Fact]
    public void ALinkWalkRoundACircularChainTerminates()
    {
        // The walk stops at a node it has already seen in THIS walk, which is
        // what ends a loop: it arrives back at where it started. A walk that
        // did not stop would never return from LinkChain.
        var registry = NewRegistry();

        var a = Named(registry, 0x0067_0001u, "loop_a", "");
        var b = Named(registry, 0x0067_0002u, "loop_b", "loop_missing");
        a.LinkTo(b);
        b.LinkTo(a);

        var warnings = WalkWarnings(a);

        Assert.Empty(warnings);
        Assert.True(ReferenceEquals(a.NextKey(1), b) && ReferenceEquals(b.NextKey(1), a),
                    "the loop is intact and the walk came back");
    }

    [Fact]
    public void TrackNearest()
    {
        var registry = NewRegistry();

        var a = Node(registry, 0x0065_0001u, Vector.Zero);
        var b = Node(registry, 0x0065_0002u, new Vector(100f, 0f, 0f));
        var c = Node(registry, 0x0065_0003u, new Vector(300f, 0f, 0f));

        a.LinkTo(b);
        b.LinkTo(c);

        Assert.True(ReferenceEquals(a.Nearest(new Vector(90f, 0f, 0f)), b), "the nearest node to a place on the track is the one beside it");

        Assert.True(ReferenceEquals(a.Nearest(Vector.Zero), a), "and from the start of the track, the start");

        // Height is dropped before measuring, and it has to be: the thing asking
        // is a train, which sits its own height above the node it is on.
        Assert.True(ReferenceEquals(a.Nearest(new Vector(100f, 0f, 500f)), b), "height does not count, because a train rides above its track");

        // A circular track has to terminate, and it does because the walk stops
        // when it arrives back where it started.
        c.LinkTo(a);

        Assert.True(ReferenceEquals(a.Nearest(new Vector(295f, 0f, 0f)), c), "a track that loops still answers");
    }

    [Fact]
    public void TrackTrainRunsItsTrack()
    {
        var registry = NewRegistry();

        var first = Node(registry, 0x0066_0001u, Vector.Zero);
        var second = Node(registry, 0x0066_0002u, new Vector(200f, 0f, 0f));
        var third = Node(registry, 0x0066_0003u, new Vector(400f, 0f, 0f));

        first.LinkTo(second);
        second.LinkTo(third);

        var train = TrackTrain(registry, 0x0066_0010u);

        Assert.True(train.CurrentNode is null, "a train has no node until it has looked for one");

        train.StartOn(first);

        Assert.True(ReferenceEquals(train.CurrentNode, first), "a train starts on the node it names");
        Assert.True(ReferenceEquals(train.TargetNode, second), "and sets off for the next one");
        Assert.True(first.OnPass.FireCount == 1, "arriving on the first node fires its OnPass");
        Assert.True(train.IsDirForward, "and it runs forward to begin with");

        train.FinishStep();

        Assert.True(ReferenceEquals(train.CurrentNode, second) && ReferenceEquals(train.TargetNode, third), "the leg ends on the node it was aimed at");
        Assert.True(second.OnPass.FireCount == 1, "which fires that node too");

        train.FinishStep();

        Assert.True(Near(train.CurrentSpeed, 0f), "the end of the track stops the train");
        Assert.True(ReferenceEquals(train.CurrentNode, third) && train.TargetNode is null, "and it comes to rest on the last node");

        // Twice, not once. Stock announces the terminal node on arrival and again
        // when it works out that it is at a dead end, and a map counting passes
        // was built against that. Pinned so a tidy-up does not quietly halve it.
        Assert.True(third.OnPass.FireCount == 2, "the terminal node is announced twice, as stock does");

        registry.Input(new EntityHandle(0x0066_0010u), "Resume", "", 0f, default);

        // Nowhere to go: Resume restores the speed the train had, and the next leg
        // finds the same dead end.
        Assert.True(ReferenceEquals(train.CurrentNode, third) && train.TargetNode is null, "resuming at a dead end does not move it");
    }

    [Fact]
    public void TrackTrainReverses()
    {
        var registry = NewRegistry();

        var first = Node(registry, 0x0067_0001u, Vector.Zero);
        var second = Node(registry, 0x0067_0002u, new Vector(200f, 0f, 0f));
        var third = Node(registry, 0x0067_0003u, new Vector(400f, 0f, 0f));

        first.LinkTo(second);
        second.LinkTo(third);

        var train = TrackTrain(registry, 0x0067_0010u);
        var handle = new EntityHandle(0x0067_0010u);

        train.StartOn(first);
        train.FinishStep();

        Assert.True(ReferenceEquals(train.CurrentNode, second) && ReferenceEquals(train.TargetNode, third), "the train is on the middle node heading for the last");

        registry.Input(handle, "Reverse", "", 0f, default);

        // The swap is the whole of the reversal, and it is the part stock only
        // approximates: the node ahead becomes the node behind, so the train
        // finishes the leg it was on backwards rather than skipping to a
        // neighbour of where it came from.
        Assert.True(!train.IsDirForward, "reversing turns it round");
        Assert.True(ReferenceEquals(train.CurrentNode, third) && ReferenceEquals(train.TargetNode, second), "and the node ahead becomes the node behind");
        Assert.True(train.CurrentSpeed < 0f, "with the speed signed the other way");

        train.FinishStep();

        Assert.True(ReferenceEquals(train.CurrentNode, second) && ReferenceEquals(train.TargetNode, first), "and it walks back down the same links");

        registry.Input(handle, "Stop", "", 0f, default);

        Assert.True(Near(train.CurrentSpeed, 0f), "Stop takes the speed away");
        Assert.True(train.TargetNode is null, "and there is no leg in flight");

        registry.Input(handle, "StartForward", "", 0f, default);

        Assert.True(train.IsDirForward, "StartForward turns it round again");
        Assert.True(Near(train.CurrentSpeed, 100f), "and sets off at the maximum the map gave it");
    }

    [Fact]
    public void TrackTrainTakesNodeSpeed()
    {
        var registry = NewRegistry();

        var first = Node(registry, 0x0068_0001u, Vector.Zero);
        var slow = Node(registry, 0x0068_0002u, new Vector(200f, 0f, 0f), speed: "40");
        var quiet = Node(registry, 0x0068_0003u, new Vector(400f, 0f, 0f));
        var last = Node(registry, 0x0068_0004u, new Vector(600f, 0f, 0f));

        first.LinkTo(slow);
        slow.LinkTo(quiet);
        quiet.LinkTo(last);

        // Two trains on one track, because the thing being separated is a
        // property of the TRAIN and not of the node: the same node says forty to
        // one of them and nothing to the other.
        var drivable = TrackTrain(registry, 0x0068_0010u, speed: "100");

        drivable.StartOn(first);

        Assert.True(Near(drivable.CurrentSpeed, 100f), "a train sets off at the speed the map gave it");

        drivable.FinishStep();

        // Nothing has happened, and that is stock: a node's speed is read only
        // when the train is not under a player's hand, so a train the map left
        // drivable ignores its track's speeds entirely.
        Assert.True(Near(drivable.CurrentSpeed, 100f), "a drivable train ignores the speed of the node it arrives on");

        var governed = TrackTrain(registry, 0x0068_0020u, speed: "100");
        var handle = new EntityHandle(0x0068_0020u);

        governed.TakeOutOfPlayersHands();
        governed.StartOn(first);

        Assert.True(Near(governed.CurrentSpeed, 100f), "and so does the other one, until it reaches a node with something to say");

        governed.FinishStep();

        Assert.True(Near(governed.CurrentSpeed, 40f), "a train taken out of the player's hands takes the node's speed");

        governed.FinishStep();

        // A zero on a node means "nothing to say", not "stop", so the speed
        // carries across it.
        Assert.True(Near(governed.CurrentSpeed, 40f), "and a node with no speed leaves it alone");
        Assert.True(ReferenceEquals(governed.TargetNode, last), "with the train still going somewhere");

        registry.Input(handle, "SetSpeed", "0.5", 0.5f, default);

        Assert.True(Near(governed.CurrentSpeed, 50f), "a scaled input is a fraction of the maximum");

        registry.Input(handle, "SetSpeedReal", "70", 70f, default);

        Assert.True(Near(governed.CurrentSpeed, 70f), "and a real one is units per second");

        registry.Input(handle, "SetSpeedReal", "9999", 9999f, default);

        Assert.True(Near(governed.CurrentSpeed, 100f), "clamped to the maximum");
    }

    [Fact]
    public void TrackChangerSwitchesBranches()
    {
        var registry = NewRegistry();

        // Two branches, and the changer's whole job is that exactly one of them is
        // reachable at a time.
        var upper = Node(registry, 0x0069_0001u, new Vector(0f, 0f, 128f));
        var lower = Node(registry, 0x0069_0002u, Vector.Zero);

        // A train parked on the upper branch. The wheelbase matters: it is the
        // distance inside which the changer counts the train as aboard rather than
        // merely nearby.
        var train = TrackTrain(registry, 0x0069_0010u, speed: "0", wheels: "64");
        train.StartOn(upper);

        var changer = Changer(registry, 0x0069_0020u);

        Assert.True(changer.State == PlatformState.AtTop, "a changer starts at the top unless the map says otherwise");

        changer.Wire(upper, lower, train);

        Assert.True(!upper.IsDisabled, "the branch under the changer is the live one");
        Assert.True(lower.IsDisabled, "and the other one is closed");

        changer.Pressed();

        // The train is stopped and sitting on the changer, so it comes along — and
        // while it is in transit it holds no node at all, because it is not on any
        // track.
        Assert.True(changer.Code == TrainCode.Following, "a parked train is taken aboard");
        Assert.True(train.CurrentNode is null, "and loses its track while it is in the air");
        Assert.True(upper.IsDisabled && lower.IsDisabled, "with neither branch reachable in transit");

        changer.FinishJourney();

        Assert.True(changer.State == PlatformState.AtBottom, "the changer arrives at the other end");
        Assert.True(ReferenceEquals(train.CurrentNode, lower), "and hands the train the branch it swung to");
        Assert.True(!lower.IsDisabled && upper.IsDisabled, "which is now the live one");

        // A changer between ends refuses outright, which is what stops a second
        // press half way through from stranding a train.
        changer.PlaceAt(PlatformState.GoingUp);
        TrainCode before = changer.Code;
        changer.Pressed();

        Assert.True(changer.Code == before, "a changer in transit ignores being pressed");
    }

    /// <summary>A changer with no keys: everything it does comes from being wired up.</summary>
    private static FuncTrackChange Changer(ScriptedEntityRegistry registry, uint handle)
    {
        var h = new EntityHandle(handle);

        registry.Spawn(h, EntityNames.FuncTrackchange);

        return (FuncTrackChange)registry.Get(h)!;
    }

    private static bool Near(float a, float b, float tolerance = 0.01f) => MathF.Abs(a - b) <= tolerance;
}
