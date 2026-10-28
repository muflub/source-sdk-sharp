using System;
using System.Collections.Generic;
using System.IO;

using SourceSharp.Abi;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// An entity whose whole job is to have an output, an input and a name.
///
/// <para>
/// Declared here rather than reusing a game entity so that a failure points at
/// the I/O half and not at a change in what a <c>logic_relay</c> does.
/// </para>
/// </summary>
[ScriptedEntity("test_io_relay")]
public sealed class TestIoRelay : ScriptedEntity
{
    public readonly Output OnTrigger = new();
    public readonly Output OnSecond = new();

    /// <summary>Every input this entity was handed, in the order it arrived.</summary>
    public readonly List<string> Inputs = new();

    /// <summary>Every value that came with one.</summary>
    public readonly List<string> Values = new();

    /// <summary>Who each input said had caused it.</summary>
    public readonly List<EntityHandle> Activators = new();

    /// <summary>Set by <see cref="OnTrip"/>, which fires OnTrigger from inside an input.</summary>
    public bool Chain;

    /// <summary>The input nesting depth seen inside a handler. 1 means "not nested".</summary>
    public int DepthSeen;

    public override bool OnInput(string name, string value, float number, EntityHandle activator)
    {
        Inputs.Add(name);
        Values.Add(value);
        Activators.Add(activator);
        DepthSeen = Owner?.InputDepth ?? -1;

        if (Chain && string.Equals(name, "Trip", StringComparison.Ordinal))
            OnTrigger.Fire();

        return true;
    }
}

/// <summary>A second class, so a classname fallback has something to find.</summary>
[ScriptedEntity("test_io_sink")]
public sealed class TestIoSink : ScriptedEntity
{
    public readonly List<string> Inputs = new();

    public override bool OnInput(string name, string value, float number, EntityHandle activator)
    {
        Inputs.Add(name);
        return true;
    }
}

/// <summary>
/// THE I/O HALF — <c>plan_entity.md</c> step 2a. Connections parsed in managed
/// keyvalue handling, delays queued in the directory, the procedural names
/// resolved there, and the whole lot in the reload record.
/// </summary>
public class EntityIoTests
{
    // This class's own side state (statics to zero): xUnit builds one
    // instance per fact, so no fact sees another's.
    private readonly ReloadStore _reload = new(Side.Server);
    private readonly EventIdStamps _stamps = new();

    private static readonly EntityHandle A = new(0x0001_0001u);
    private static readonly EntityHandle B = new(0x0002_0002u);
    private static readonly EntityHandle C = new(0x0003_0003u);
    private static readonly EntityHandle Player = new(0x0004_0004u);

    private static ScriptedEntityRegistry Fresh()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EntityIoTests).Assembly);
        registry.Services = Services();

        // The engine is not up in a test, so !player would answer None and the
        // fact that reads it could not fail. A provider naming a real fixture
        // is what makes that check a check.
        registry.Directory.PlayerProvider = () => Player;

        return registry;
    }

    private static TestIoRelay Relay(ScriptedEntityRegistry registry, EntityHandle handle,
                                     string? name = null)
    {
        if (name is not null)
            registry.KeyValue(handle, "test_io_relay", "targetname", name);

        registry.Spawn(handle, "test_io_relay");
        return (TestIoRelay)registry.Get(handle)!;
    }

    //=========================================================================
    // Parsing a map's connection string — CEventAction::CEventAction.
    //=========================================================================

    [Fact]
    public void AConnectionCarriesEveryFieldTheMapWrote()
    {
        var action = EventAction.Parse("plug2,EnableMotion,onoff,1.5,3", _stamps);

        Assert.Equal("plug2", action.Target);
        Assert.Equal("EnableMotion", action.TargetInput);
        Assert.Equal("onoff", action.Parameter);
        Assert.Equal(1.5f, action.Delay);
        Assert.Equal(3, action.TimesToFire);
    }

    [Fact]
    public void AConnectionWithNoInputNameFiresUse()
    {
        // CEventAction's own default, and not a guess: a blank input is "Use".
        Assert.Equal("Use", EventAction.Parse("plug2", _stamps).TargetInput);
        Assert.Equal("Use", EventAction.Parse("plug2,,,0,-1", _stamps).TargetInput);
    }

    [Fact]
    public void AMissingParameterIsNullAndNotEmpty()
    {
        // Stock's NULL_STRING against a written-but-empty override. The first
        // passes the fired value through; the second replaces it.
        Assert.Null(EventAction.Parse("plug2,Use,,0,-1", _stamps).Parameter);
        Assert.Equal("x", EventAction.Parse("plug2,Use,x,0,-1", _stamps).Parameter);
    }

    [Fact]
    public void AnEscDelimiterLetsAParameterCarryACommas()
    {
        // VMF_IOPARAM_STRING_DELIMITER exists for exactly this, and choosing
        // the comma unconditionally is the difference between a game_text
        // receiving "Hello, world" and receiving "Hello".
        const char Esc = EventAction.VmfDelimiter;

        var action = EventAction.Parse($"sign{Esc}Display{Esc}Hello, world{Esc}0{Esc}-1", _stamps);

        Assert.Equal("Hello, world", action.Parameter);
        Assert.Equal(Esc, EventAction.DelimiterFor($"a{Esc}b"));
        Assert.Equal(',', EventAction.DelimiterFor("a,b"));
    }

    [Fact]
    public void ATimesToFireOfZeroMeansAlways()
    {
        // `if (m_nTimesToFire == 0) m_nTimesToFire = EVENT_FIRE_ALWAYS`. A map
        // that writes 0 means "no limit", not "never".
        Assert.Equal(EventAction.FireAlways, EventAction.Parse("plug,Use,,0,0", _stamps).TimesToFire);
        Assert.Equal(EventAction.FireAlways, EventAction.Parse("plug,Use,,0", _stamps).TimesToFire);
    }

    [Fact]
    public void ADelayIsReadTheWayAtofReadsIt()
    {
        // TryParse refuses both of these and would leave the delay at zero,
        // turning a mapper's stray space into an event that fires at once.
        Assert.Equal(0.5f, EventAction.ParseFloat("0.5 "));
        Assert.Equal(2f, EventAction.ParseFloat("2x"));
        Assert.Equal(0f, EventAction.ParseFloat("later"));
        Assert.Equal(-3f, EventAction.ParseFloat("-3"));
    }

    [Fact]
    public void EveryConnectionGetsAnIdStampOfItsOwn()
    {
        var first = EventAction.Parse("a,Use,,0,-1", _stamps);
        var second = EventAction.Parse("a,Use,,0,-1", _stamps);

        Assert.NotEqual(first.IdStamp, second.IdStamp);
        Assert.True(first.IdStamp > 0, "stock pre-increments, so 0 stays free for 'no output'");
    }

    //=========================================================================
    // The name-matching rule, against the native one.
    //=========================================================================

    [Fact]
    public void AnExactNameMatchesRegardlessOfCase()
    {
        Assert.True(EntityNameMatch.Match("gate_north", "gate_north"));
        Assert.True(EntityNameMatch.Match("GATE_NORTH", "gate_north"));
        Assert.True(EntityNameMatch.Match("gate_north", "GATE_NORTH"));
        Assert.False(EntityNameMatch.Match("gate_south", "gate_north"));
    }

    [Fact]
    public void AWildcardMatchesWhereverTheWalkStopped()
    {
        // Stock's comment says "only thing supported is trailing *" and the code
        // does more than that. Reproduced rather than corrected, because
        // correcting it would change what an existing map does — so the fact
        // pins the REAL behaviour, not the comment.
        Assert.True(EntityNameMatch.Match("gate_*", "gate_north"));
        Assert.True(EntityNameMatch.Match("ga*e_north", "gate_north"));
        Assert.True(EntityNameMatch.Match("*", "anything"));
        Assert.False(EntityNameMatch.Match("gate_*", "door_north"));
    }

    [Fact]
    public void AnUnnamedEntityAnswersOnlyToNothingOrToEverything()
    {
        // EntityNames_Match's NULL_STRING arm, which the native tests caught a
        // first draft of this getting wrong.
        Assert.True(EntityNameMatch.Match("", ""));
        Assert.True(EntityNameMatch.Match("*", ""));
        Assert.True(EntityNameMatch.Match(null, ""));
        Assert.False(EntityNameMatch.Match("gate", ""));
    }

    //=========================================================================
    // Ordering.
    //=========================================================================

    [Fact]
    public void TwoEventsDueAtOneInstantDeliverInPostingOrder()
    {
        var queue = new EntityIoQueue(new RecordingLog());
        var seen = new List<string>();

        queue.Add(1f, "first", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);
        queue.Add(1f, "second", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);
        queue.Add(1f, "third", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);

        queue.Service(1f, e => seen.Add(e.Target));

        Assert.Equal(new[] { "first", "second", "third" }, seen);
    }

    [Fact]
    public void AnEarlierEventDeliversFirstHoweverItWasPosted()
    {
        var queue = new EntityIoQueue(new RecordingLog());
        var seen = new List<string>();

        queue.Add(5f, "late", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);
        queue.Add(1f, "early", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);
        queue.Add(3f, "middle", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);

        queue.Service(10f, e => seen.Add(e.Target));

        Assert.Equal(new[] { "early", "middle", "late" }, seen);
    }

    [Fact]
    public void AnEventPostedByADeliveryIsServicedInTheSamePass()
    {
        // CEventQueue::ServiceEvents restarts from the head after every
        // delivery, which is what makes a chain of zero-delay relays resolve
        // within one frame instead of one link per frame. A great many maps
        // depend on it without saying so.
        var queue = new EntityIoQueue(new RecordingLog());
        var seen = new List<string>();

        queue.Add(1f, "first", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);

        int delivered = queue.Service(1f, e =>
        {
            seen.Add(e.Target);

            if (e.Target == "first")
                queue.Add(1f, "second", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);
        });

        Assert.Equal(new[] { "first", "second" }, seen);
        Assert.Equal(2, delivered);
    }

    [Fact]
    public void AnEventPostedByADeliveryGoesBehindTheOnesAlreadyDue()
    {
        var queue = new EntityIoQueue(new RecordingLog());
        var seen = new List<string>();

        queue.Add(1f, "first", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);
        queue.Add(1f, "second", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);

        queue.Service(1f, e =>
        {
            seen.Add(e.Target);

            if (e.Target == "first")
                queue.Add(1f, "posted", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);
        });

        Assert.Equal(new[] { "first", "second", "posted" }, seen);
    }

    //=========================================================================
    // Delay.
    //=========================================================================

    [Fact]
    public void ADelayedEventWaitsForItsTime()
    {
        var queue = new EntityIoQueue(new RecordingLog());
        var seen = new List<string>();

        queue.Add(2f, "later", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);

        Assert.Equal(0, queue.Service(1.9f, e => seen.Add(e.Target)));
        Assert.Equal(1, queue.Count);

        Assert.Equal(1, queue.Service(2f, e => seen.Add(e.Target)));
        Assert.Equal(0, queue.Count);
        Assert.Equal(new[] { "later" }, seen);
    }

    [Fact]
    public void AConnectionsDelayIsMeasuredFromWhenItFired()
    {
        var connections = new EntityConnections();
        var queue = new EntityIoQueue(new RecordingLog());

        connections.Add("OnTrigger", EventAction.Parse("plug,Go,,1.5,-1", _stamps));
        connections.Fire(queue, "OnTrigger", curTime: 10f, value: null,
                         activator: EntityHandle.None, caller: A);

        Assert.Equal(11.5f, queue.Pending[0].FireTime);
    }

    [Fact]
    public void TheLongestDelayOnAnOutputIsWhatGetMaxDelayAnswered()
    {
        var connections = new EntityConnections();

        connections.Add("OnTrigger", EventAction.Parse("a,Go,,0.25,-1", _stamps));
        connections.Add("OnTrigger", EventAction.Parse("b,Go,,2,-1", _stamps));
        connections.Add("OnTrigger", EventAction.Parse("c,Go,,1,-1", _stamps));

        Assert.Equal(2f, connections.MaxDelay("OnTrigger"));
        Assert.Equal(0f, connections.MaxDelay("OnNothing"));
    }

    //=========================================================================
    // times.
    //=========================================================================

    [Fact]
    public void AOneShotConnectionFiresOnceAndIsThenGone()
    {
        // Stock UNLINKS the action at zero rather than leaving it inert, and a
        // mapper counts on that: a trigger_once whose connection stayed in the
        // list would fire again the moment anything re-triggered it.
        var connections = new EntityConnections();
        var queue = new EntityIoQueue(new RecordingLog());

        connections.Add("OnTrigger", EventAction.Parse("plug,Go,,0,1", _stamps));

        Assert.Equal(1, connections.Fire(queue, "OnTrigger", 0f, null, EntityHandle.None, A));
        Assert.Equal(0, connections.Fire(queue, "OnTrigger", 0f, null, EntityHandle.None, A));
        Assert.Equal(0, connections.Count);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void ATwoShotConnectionSpendsExactlyTwoFirings()
    {
        var connections = new EntityConnections();
        var queue = new EntityIoQueue(new RecordingLog());

        connections.Add("OnTrigger", EventAction.Parse("plug,Go,,0,2", _stamps));

        Assert.Equal(1, connections.Fire(queue, "OnTrigger", 0f, null, EntityHandle.None, A));
        Assert.Equal(1, connections.On("OnTrigger")[0].TimesToFire);

        Assert.Equal(1, connections.Fire(queue, "OnTrigger", 0f, null, EntityHandle.None, A));
        Assert.Equal(0, connections.Count);

        Assert.Equal(0, connections.Fire(queue, "OnTrigger", 0f, null, EntityHandle.None, A));
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void AFireAlwaysConnectionSurvivesEveryFiring()
    {
        var connections = new EntityConnections();
        var queue = new EntityIoQueue(new RecordingLog());

        connections.Add("OnTrigger", EventAction.Parse("plug,Go,,0,-1", _stamps));

        for (int i = 0; i < 5; i++)
            Assert.Equal(1, connections.Fire(queue, "OnTrigger", 0f, null, EntityHandle.None, A));

        Assert.Equal(1, connections.Count);
        Assert.Equal(EventAction.FireAlways, connections.On("OnTrigger")[0].TimesToFire);
        Assert.Equal(5, queue.Count);
    }

    [Fact]
    public void OneOutputsOneShotDoesNotSpendTheOthersOnTheSameOutput()
    {
        var connections = new EntityConnections();
        var queue = new EntityIoQueue(new RecordingLog());

        connections.Add("OnTrigger", EventAction.Parse("once,Go,,0,1", _stamps));
        connections.Add("OnTrigger", EventAction.Parse("always,Go,,0,-1", _stamps));

        Assert.Equal(2, connections.Fire(queue, "OnTrigger", 0f, null, EntityHandle.None, A));
        Assert.Equal(1, connections.Fire(queue, "OnTrigger", 0f, null, EntityHandle.None, A));
        Assert.Equal("always", connections.On("OnTrigger")[0].Target);
    }

    [Fact]
    public void AParameterOverrideReplacesTheFiredValue()
    {
        var connections = new EntityConnections();
        var queue = new EntityIoQueue(new RecordingLog());

        connections.Add("OnTrigger", EventAction.Parse("plug,Go,forced,0,-1", _stamps));
        connections.Add("OnTrigger", EventAction.Parse("plug,Go,,0,-1", _stamps));

        connections.Fire(queue, "OnTrigger", 0f, "passed", EntityHandle.None, A);

        Assert.Equal("forced", queue.Pending[0].Value);
        Assert.Equal("passed", queue.Pending[1].Value);
    }

    //=========================================================================
    // Re-entrancy.
    //=========================================================================

    [Fact]
    public void ServicingFromInsideADeliveryDoesNothing()
    {
        // The queue is serviced from one place, once a frame. An entity
        // reaching back in from its own input handler would deliver the events
        // queued behind it out of order and underneath the loop already walking
        // them; the outer pass picks them up on its next turn, same frame.
        var queue = new EntityIoQueue(new RecordingLog());
        var seen = new List<string>();
        int inner = -1;

        queue.Add(0f, "first", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);
        queue.Add(0f, "second", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);

        queue.Service(0f, e =>
        {
            seen.Add(e.Target);

            if (e.Target == "first")
                inner = queue.Service(0f, _ => seen.Add("REENTERED"));
        });

        Assert.Equal(0, inner);
        Assert.Equal(new[] { "first", "second" }, seen);
    }

    [Fact]
    public void AZeroDelayCycleIsRefusedRatherThanHanging()
    {
        // Two entities firing each other with no delay is an infinite loop in
        // stock: the queue never empties and curtime never advances. Bounded
        // here, once, with a message — a deliberate divergence, far past any
        // real map, that turns a hang into a diagnosis.
        var queue = new EntityIoQueue(new RecordingLog());
        int delivered = 0;

        queue.Add(0f, "loop", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);

        queue.Service(0f, e =>
        {
            delivered++;
            queue.Add(0f, "loop", EntityHandle.None, "Go", null, EntityHandle.None, EntityHandle.None, 0);
        });

        Assert.Equal(EntityIoQueue.MaxChainPerPass, delivered);
        Assert.Equal(1, queue.CyclesRefused);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void AnOutputFiredFromAnInputHandlerIsQueuedRatherThanNested()
    {
        // Source's I/O is never re-entrant through an output: firing one POSTS,
        // and the post is delivered by the pass that is already running. So the
        // input depth stays at one however long the chain is, and the registry's
        // 64-deep cycle guard is never reached by ordinary map logic.
        var registry = Fresh();

        var source = Relay(registry, A, "src");
        var target = Relay(registry, B, "dst");

        source.Chain = true;
        registry.KeyValue(A, "test_io_relay", "OnTrigger", "dst,Land,,0,-1");

        registry.Directory.FireInputAt(A, "Trip", null, curTime: 0f);
        registry.Directory.ServiceIo(0f);

        Assert.Equal(new[] { "Trip" }, source.Inputs);
        Assert.Equal(new[] { "Land" }, target.Inputs);
        Assert.Equal(1, source.DepthSeen);
        Assert.Equal(1, target.DepthSeen);
    }

    //=========================================================================
    // Connections through managed keyvalue handling.
    //=========================================================================

    [Fact]
    public void AMapConnectionOnADeclaredOutputIsClaimedAndParsed()
    {
        var registry = Fresh();
        Relay(registry, A, "src");

        Assert.True(registry.KeyValue(A, "test_io_relay", "OnTrigger", "dst,Land,x,1.5,2"));

        var actions = registry.Directory.ConnectionsOf(A).On("OnTrigger");

        Assert.Single(actions);
        Assert.Equal("dst", actions[0].Target);
        Assert.Equal("Land", actions[0].TargetInput);
        Assert.Equal(1.5f, actions[0].Delay);
        Assert.Equal(2, actions[0].TimesToFire);
    }

    [Fact]
    public void AConnectionKeyNeverReachesTheClassAsAKeyvalue()
    {
        // The output name is this class's own, so nothing downstream has a use
        // for it, and letting it fall through would put every connection in the
        // map through the unclaimed-key near-miss check.
        var registry = Fresh();
        var relay = Relay(registry, A, "src");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "dst,Land,,0,-1");

        Assert.Empty(relay.Inputs);
        Assert.Equal(1, registry.Directory.ConnectionCount);
    }

    [Fact]
    public void TargetnameFeedsTheDirectorysNameIndex()
    {
        // The whole reason ENTITY_OP_KEY_VALUE cannot be elided per class: the
        // index every output resolves against is fed from this one key.
        var registry = Fresh();
        Relay(registry, A, "gate_north");

        Assert.Equal("gate_north", registry.Directory.NameOf(A));

        var found = new List<EntityHandle>();
        Assert.Equal(1, registry.Directory.FindByName("gate_north", found));
        Assert.Equal(A, found[0]);
    }

    [Fact]
    public void AnEntityRenamedLosesItsOldNameFromTheIndex()
    {
        var registry = Fresh();
        Relay(registry, A, "old");

        registry.KeyValue(A, "test_io_relay", "targetname", "new");

        var found = new List<EntityHandle>();
        Assert.Equal(0, registry.Directory.FindByName("old", found));
        Assert.Equal(1, registry.Directory.FindByName("new", found));
    }

    [Fact]
    public void AnEmptyTargetIsNotAWildcard()
    {
        // FindEntityByName's first line refuses a null or empty name, and it
        // must: a connection whose target nobody filled in would otherwise
        // select every unnamed entity in the map.
        var registry = Fresh();
        Relay(registry, A, "src");

        var found = new List<EntityHandle>();
        Assert.Equal(0, registry.Directory.FindByName("", found));
        Assert.Equal(0, registry.Directory.FindByName(null, found));
    }

    //=========================================================================
    // Delivery, and the procedural names.
    //=========================================================================

    [Fact]
    public void AnOutputReachesEveryEntityOfThatName()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        var first = Relay(registry, B, "plug");
        var second = Relay(registry, C, "plug");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "plug,Land,,0,-1");
        source.OnTrigger.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(new[] { "Land" }, first.Inputs);
        Assert.Equal(new[] { "Land" }, second.Inputs);
    }

    [Fact]
    public void AWildcardTargetReachesEveryMatchingName()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        var north = Relay(registry, B, "gate_north");
        var south = Relay(registry, C, "gate_south");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "gate_*,Land,,0,-1");
        source.OnTrigger.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Single(north.Inputs);
        Assert.Single(south.Inputs);
    }

    [Fact]
    public void AClassnameIsOnlyTriedWhenTheNameFoundNothing()
    {
        // ServiceEvents' targetFound flag. Trying both would deliver twice to
        // an entity whose targetname and classname are the same string.
        var registry = Fresh();
        var source = Relay(registry, A, "src");

        registry.Spawn(B, "test_io_sink");
        var sink = (TestIoSink)registry.Get(B)!;

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "test_io_sink,Land,,0,-1");
        source.OnTrigger.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(new[] { "Land" }, sink.Inputs);
    }

    [Fact]
    public void ActivatorResolvesToWhoeverCausedTheChain()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        var cause = Relay(registry, C, "cause");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "!activator,Land,,0,-1");
        source.OnTrigger.Fire(C);
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(new[] { "Land" }, cause.Inputs);
    }

    [Fact]
    public void SelfAndCallerBothResolveToTheFiringEntity()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "!self,Land,,0,-1");
        registry.KeyValue(A, "test_io_relay", "OnSecond", "!caller,Back,,0,-1");

        source.OnTrigger.Fire();
        source.OnSecond.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(new[] { "Land", "Back" }, source.Inputs);
    }

    [Fact]
    public void PlayerResolvesThroughTheProvider()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        var player = Relay(registry, Player, "the_player");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "!player,Land,,0,-1");
        source.OnTrigger.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(new[] { "Land" }, player.Inputs);
    }

    [Fact]
    public void AnUnknownProceduralNameReachesNobody()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        var other = Relay(registry, B, "notme");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "!nonsense,Land,,0,-1");
        source.OnTrigger.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Empty(other.Inputs);
        Assert.Equal(1, registry.Directory.IoUnhandled);
    }

    [Fact]
    public void AnOutputWithNoActivatorNamesTheFiringEntity()
    {
        // The SDK writes this as FireOutput( this, this ): an output fired from
        // a think names nobody, and a downstream input that reads its activator
        // should see the entity that fired rather than nothing at all.
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        var target = Relay(registry, B, "dst");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "dst,Land,,0,-1");
        source.OnTrigger.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(A, target.Activators[0]);
    }

    [Fact]
    public void AnInputPostedWithNoActivatorArrivesWithNone()
    {
        var registry = Fresh();
        var target = Relay(registry, B, "dst");

        registry.Directory.FireInputAt(B, "Land", null, curTime: 0f);
        registry.Directory.ServiceIo(0f);

        Assert.Equal(EntityHandle.None, target.Activators[0]);
    }

    /// <summary>
    /// The raw handle 0 is entry zero, a real slot, and is delivered as
    /// itself: only an omitted activator is "nobody".
    /// </summary>
    [Fact]
    public void AnInputPostedWithEntryZeroAsActivatorArrivesWithEntryZero()
    {
        var registry = Fresh();
        var target = Relay(registry, B, "dst");

        registry.Directory.FireInputAt(B, "Land", null, curTime: 0f, activator: new EntityHandle(0u));
        registry.Directory.ServiceIo(0f);

        Assert.Equal(new EntityHandle(0u), target.Activators[0]);
    }

    [Fact]
    public void AnOutputACLassNeverDeclaredIsReportedRatherThanFired()
    {
        var registry = Fresh();
        Relay(registry, A, "src");

        Assert.Equal(0, registry.FireOutput(A, "OnNothing", null, EntityHandle.None));
    }

    [Fact]
    public void AnEventForAnEntityThatWentIsCancelledWithIt()
    {
        // stock's CBaseEntity::UpdateOnRemove calls g_EventQueue.CancelEvents(
        // this ): an entity that is going takes the events it posted with it.
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        var target = Relay(registry, B, "dst");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "dst,Land,,5,-1");
        source.OnTrigger.Fire();

        Assert.Equal(1, registry.Directory.Io.Count);

        registry.Directory.Unbind(A);

        Assert.Equal(0, registry.Directory.Io.Count);

        registry.Directory.ServiceIo(registry.Now + 10f);
        Assert.Empty(target.Inputs);
    }

    //=========================================================================
    // The reload record — this step's gate.
    //=========================================================================

    [Fact]
    public void ADelayedOutputSurvivesAHotReload()
    {
        // THE GATE. Before step 2a the queue was g_EventQueue -- native memory
        // holding EHANDLEs -- so a two-second output posted before a reload was
        // delivered afterwards by code that no longer existed, and there was no
        // record of it anywhere managed. It is in the reload record now.
        _reload.Clear();

        var before = Fresh();
        var source = Relay(before, A, "src");
        Relay(before, B, "dst");

        before.KeyValue(A, "test_io_relay", "OnTrigger", "dst,Land,,2,-1");
        source.OnTrigger.Fire();

        float now = before.Now;
        before.SaveForReload(_reload);

        Assert.True(ReloadCarrier.IoCount(_reload) > 0, "the record carries the I/O half");

        // The assembly the entities came from is notionally gone; a brand new
        // registry is what the far side of a reload has.
        var after = Fresh();
        after.RestoreAfterReload(_reload);

        var target = (TestIoRelay)after.Get(B)!;

        after.Directory.ServiceIo(now + 1.9f);
        Assert.Empty(target.Inputs);

        after.Directory.ServiceIo(now + 2.0f);
        Assert.Equal(new[] { "Land" }, target.Inputs);
    }

    [Fact]
    public void TheRecordCarriesWhatWasLeftOfTheDelayAndNotWhenItWasDue()
    {
        // A reload does not stop the clock, so an absolute fire time saved
        // before the unload would come back already past.
        var queue = new EntityIoQueue(new RecordingLog());
        queue.Add(100f, "dst", EntityHandle.None, "Land", null, EntityHandle.None, A, 7);

        var saved = queue.Save(curTime: 98.5f);

        Assert.True(Math.Abs(saved[0].Delay - 1.5f) < 1e-4f, $"delay left was {saved[0].Delay}");
        Assert.Equal("dst", saved[0].Target);
        Assert.Equal(7, saved[0].OutputId);

        var restored = new EntityIoQueue(new RecordingLog());
        restored.Restore(saved, curTime: 500f);

        Assert.True(Math.Abs(restored.Pending[0].FireTime - 501.5f) < 1e-3f,
                    $"restored fire time was {restored.Pending[0].FireTime}");
    }

    [Fact]
    public void AnEventAlreadyDueComesBackDueAndNotInThePast()
    {
        var queue = new EntityIoQueue(new RecordingLog());
        queue.Add(1f, "dst", EntityHandle.None, "Land", null, EntityHandle.None, A, 0);

        var saved = queue.Save(curTime: 9f);

        Assert.Equal(0f, saved[0].Delay);
    }

    [Fact]
    public void AConnectionAndItsRemainingFiringsSurviveAHotReload()
    {
        _reload.Clear();

        var before = Fresh();
        var source = Relay(before, A, "src");
        Relay(before, B, "dst");

        before.KeyValue(A, "test_io_relay", "OnTrigger", "dst,Land,,0,2");
        source.OnTrigger.Fire();

        Assert.Equal(1, before.Directory.ConnectionsOf(A).On("OnTrigger")[0].TimesToFire);

        before.SaveForReload(_reload);

        var after = Fresh();
        after.RestoreAfterReload(_reload);

        var carried = after.Directory.ConnectionsOf(A).On("OnTrigger");

        Assert.Single(carried);
        Assert.Equal("dst", carried[0].Target);
        Assert.Equal(1, carried[0].TimesToFire);
    }

    [Fact]
    public void TakingTheIoRecordEmptiesIt()
    {
        // A restore that ran twice would deliver every delayed output a second
        // time, which is worse than replaying a row: a row lands on one entity.
        _reload.Clear();

        var directory = new EntityDirectory(new RecordingLog());
        directory.Io.Add(1f, "dst", EntityHandle.None, "Land", null,
                         EntityHandle.None, EntityHandle.None, 0);

        ReloadCarrier.PutIo(_reload, directory.SaveIo(0f));

        Assert.Equal(1, ReloadCarrier.TakeIo(_reload).Count);
        Assert.Equal(0, ReloadCarrier.TakeIo(_reload).Count);
    }

    [Fact]
    public void ClearingASidesRecordDropsItsIoHalfToo()
    {
        var directory = new EntityDirectory(new RecordingLog());
        directory.Io.Add(1f, "dst", EntityHandle.None, "Land", null,
                         EntityHandle.None, EntityHandle.None, 0);

        ReloadCarrier.PutIo(_reload, directory.SaveIo(0f));
        _reload.Clear();

        Assert.Equal(0, ReloadCarrier.IoCount(_reload));
    }

    //=========================================================================
    // The route the deleted slot left behind.
    //=========================================================================

    [Fact]
    public void FiringAnInputThroughTheEngineAreaReachesTheDirectory()
    {
        // IEngineEntities.FireInput was one line into the EntityFireInput ABI
        // slot, which step 2a deletes. It is a managed route now, and still
        // synchronous and still answering whether the entity took it — which is
        // what the slot did, because it called AcceptInput rather than queueing.
        var registry = Fresh();
        var target = Relay(registry, B, "dst");

        Assert.True(registry.Io.FireInput(B, "Land", "3", A));

        Assert.Equal(new[] { "Land" }, target.Inputs);
        Assert.Equal(new[] { "3" }, target.Values);
        Assert.Equal(A, target.Activators[0]);
    }

    [Fact]
    public void AnUnboundRouteAnswersFalseRatherThanThrowing()
    {
        // The route is CLEARED rather than left holding a delegate over an
        // object of a side that is being unloaded, so "nothing is bound" is a
        // state the game really reaches -- between an unload and the next
        // registry's constructor. It must answer, not throw into the engine's
        // C++ frames.
        var registry = Fresh();
        registry.Io.Unbind();

        Assert.False(registry.Io.FireInput(B, "Land", null, EntityHandle.None));
    }

    //=========================================================================
    // THE ONE QUEUE -- the sub-ioqueue lane.
    //
    // Everything below is about the two facts that lane exists for: an event
    // that missed its own frame is COUNTED, and an input that reached nobody
    // is LOUD.
    //=========================================================================

    [Fact]
    public void AnIdleQueueCountsNothingAsPostedAfterThePass()
    {
        // The control that proves the instrument can read zero. Without it
        // "postedAfterThePass is 0" would be true of a counter that never
        // increments at all.
        var registry = Fresh();

        registry.Directory.ServiceIo(registry.Now);
        registry.Directory.ServiceIo(registry.Now + 1f);

        Assert.Equal(0L, registry.Directory.Io.PostedAfterThePass);
    }

    [Fact]
    public void AnEventPostedBeforeThePassIsNotLate()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        Relay(registry, B, "dst");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "dst,Land,,0,-1");
        source.OnTrigger.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(0L, registry.Directory.Io.PostedAfterThePass);
    }

    [Fact]
    public void AnEventPostedByADeliveryIsNotLate()
    {
        // Inside a Service pass the head is re-read, so a zero-delay event a
        // delivery posts is delivered in the same pass. It is not late and must
        // not be counted as such.
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        var middle = Relay(registry, B, "mid");
        Relay(registry, C, "end");

        middle.Chain = true;
        registry.KeyValue(A, "test_io_relay", "OnTrigger", "mid,Trip,,0,-1");
        registry.KeyValue(B, "test_io_relay", "OnTrigger", "end,Land,,0,-1");

        source.OnTrigger.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(0L, registry.Directory.Io.PostedAfterThePass);
    }

    [Fact]
    public void AnEventPostedAtATimeAlreadyServicedIsCountedLate()
    {
        // THE TWO-SERVICE-POINTS DEFECT, in one fact. The pass for this instant
        // has run; an event posted for this instant afterwards cannot be
        // delivered until the next one, which is a whole frame away.
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        Relay(registry, B, "dst");

        float now = registry.Now;
        registry.Directory.ServiceIo(now);

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "dst,Land,,0,-1");
        source.OnTrigger.Fire();

        Assert.Equal(1L, registry.Directory.Io.PostedAfterThePass);
    }

    [Fact]
    public void ALateEventIsStillDeliveredOnTheNextPass()
    {
        // Counted is not dropped. The number is a diagnosis, not a refusal.
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        var target = Relay(registry, B, "dst");

        float now = registry.Now;
        registry.Directory.ServiceIo(now);

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "dst,Land,,0,-1");
        source.OnTrigger.Fire();

        Assert.Empty(target.Inputs);
        registry.Directory.ServiceIo(now);
        Assert.Equal(new[] { "Land" }, target.Inputs);
    }

    [Fact]
    public void ALevelChangeForgetsTheServicedWatermark()
    {
        // curtime starts over on a new level, so a watermark carried across one
        // would make every event of the first frames read as late.
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        Relay(registry, B, "dst");

        registry.Directory.ServiceIo(600f);
        registry.Directory.Clear();

        var again = Relay(registry, A, "src");
        Relay(registry, B, "dst");
        registry.KeyValue(A, "test_io_relay", "OnTrigger", "dst,Land,,0,-1");
        again.OnTrigger.Fire();

        Assert.Equal(0L, registry.Directory.Io.PostedAfterThePass);
    }

    [Fact]
    public void AnUnhandledInputCountsItsDistinctPair()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "nobody,Land,,0,-1");
        source.OnTrigger.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(1, registry.Directory.IoUnhandledDistinct);
        Assert.Equal(1L, registry.Directory.IoUnhandled);
    }

    [Fact]
    public void OneDeadConnectionFiringTwiceIsOneDistinctPair()
    {
        // The reason the report is per pair and not per event: a repeating relay
        // with one dead connection must not spend the whole reporting budget.
        var registry = Fresh();
        var source = Relay(registry, A, "src");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "nobody,Land,,0,-1");

        for (int i = 0; i < 40; i++)
        {
            source.OnTrigger.Fire();
            registry.Directory.ServiceIo(registry.Now);
        }

        Assert.Equal(1, registry.Directory.IoUnhandledDistinct);
        Assert.Equal(40L, registry.Directory.IoUnhandled);
    }

    [Fact]
    public void ANewDeadPairIsStillDistinctAfterFortyOfAnother()
    {
        // THE FACT THE OLD FIRST-EIGHT RULE COULD NOT CARRY. Under it the ninth
        // unhandled event was silent whatever it named, so a genuinely new
        // misdelivery arriving after a noisy old one -- which is exactly the
        // shape of status/entlist-server.md defect 3 -- reached nobody and said
        // nothing.
        var registry = Fresh();
        var source = Relay(registry, A, "src");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "nobody,Land,,0,-1");

        for (int i = 0; i < 40; i++)
        {
            source.OnTrigger.Fire();
            registry.Directory.ServiceIo(registry.Now);
        }

        registry.KeyValue(A, "test_io_relay", "OnSecond", "someone_else,Open,,0,-1");
        source.OnSecond.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(2, registry.Directory.IoUnhandledDistinct);
    }

    [Fact]
    public void TheSameTargetWithADifferentInputIsADistinctPair()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "nobody,Land,,0,-1");
        registry.KeyValue(A, "test_io_relay", "OnSecond", "nobody,Open,,0,-1");

        source.OnTrigger.Fire();
        source.OnSecond.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(2, registry.Directory.IoUnhandledDistinct);
    }

    [Fact]
    public void ALevelChangeMakesADeadPairReportableAgain()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "nobody,Land,,0,-1");
        source.OnTrigger.Fire();
        registry.Directory.ServiceIo(registry.Now);
        Assert.Equal(1, registry.Directory.IoUnhandledDistinct);

        registry.Directory.Clear();
        Assert.Equal(0, registry.Directory.IoUnhandledDistinct);

        // ...and the LIFETIME total does not reset with it.
        Assert.Equal(1L, registry.Directory.IoUnhandled);
    }

    [Fact]
    public void TheSdkWalkIsAskedOnlyWhenNothingManagedAnswered()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        Relay(registry, B, "dst");

        var asked = new List<string>();
        var kept = registry.Directory.SdkDelivery;

        try
        {
            registry.Directory.SdkDelivery = (t, i, v, a) => { asked.Add(t); return 0; };

            registry.KeyValue(A, "test_io_relay", "OnTrigger", "dst,Land,,0,-1");
            source.OnTrigger.Fire();
            registry.Directory.ServiceIo(registry.Now);

            Assert.Empty(asked);
        }
        finally
        {
            registry.Directory.SdkDelivery = kept;
        }
    }

    [Fact]
    public void TheSdkWalkTakesTheInputNothingManagedWanted()
    {
        // The SDK hierarchy's entities -- CEntityDissolve, CRagdollProp and the
        // rest of the classes the compiled stock TUs still construct -- are not
        // in the managed directory. Losing this walk would be a capability
        // removed, so it is a fact.
        var registry = Fresh();
        var source = Relay(registry, A, "src");

        var asked = new List<string>();
        var kept = registry.Directory.SdkDelivery;

        try
        {
            registry.Directory.SdkDelivery = (t, i, v, a) => { asked.Add(t + "/" + i); return 1; };

            registry.KeyValue(A, "test_io_relay", "OnTrigger", "prop_ragdoll,Kill,,0,-1");
            source.OnTrigger.Fire();
            registry.Directory.ServiceIo(registry.Now);

            Assert.Equal(new[] { "prop_ragdoll/Kill" }, asked);

            // AND IT IS NOT REPORTED UNHANDLED, because it was handled.
            Assert.Equal(0L, registry.Directory.IoUnhandled);
        }
        finally
        {
            registry.Directory.SdkDelivery = kept;
        }
    }

    [Fact]
    public void ASdkWalkThatFoundNothingLeavesTheInputUnhandled()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");

        var kept = registry.Directory.SdkDelivery;

        try
        {
            registry.Directory.SdkDelivery = (t, i, v, a) => 0;

            registry.KeyValue(A, "test_io_relay", "OnTrigger", "nobody,Land,,0,-1");
            source.OnTrigger.Fire();
            registry.Directory.ServiceIo(registry.Now);

            Assert.Equal(1L, registry.Directory.IoUnhandled);
        }
        finally
        {
            registry.Directory.SdkDelivery = kept;
        }
    }

    [Fact]
    public void AProceduralNameNeverReachesTheSdkWalk()
    {
        // "!activator" and its friends resolve against managed handles and have
        // no SDK meaning; asking the native walk about one would be a crossing
        // spent on a question it cannot answer.
        var registry = Fresh();
        var source = Relay(registry, A, "src");

        var asked = new List<string>();
        var kept = registry.Directory.SdkDelivery;

        try
        {
            registry.Directory.SdkDelivery = (t, i, v, a) => { asked.Add(t); return 0; };

            registry.KeyValue(A, "test_io_relay", "OnTrigger", "!nonsense,Land,,0,-1");
            source.OnTrigger.Fire();
            registry.Directory.ServiceIo(registry.Now);

            Assert.Empty(asked);
        }
        finally
        {
            registry.Directory.SdkDelivery = kept;
        }
    }

    [Fact]
    public void AnIdleDirectoryAsksTheSdkWalkNothing()
    {
        // THE CONTROL FOR THE ONLY OUTBOUND CROSSING THIS LANE ADDED. It reads
        // zero when nothing is delivered, which is what makes the number below
        // a measurement rather than a decoration.
        var registry = Fresh();

        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(0L, registry.Directory.IoSdkAsks);
        Assert.Equal(0L, registry.Directory.IoSdkTook);
    }

    [Fact]
    public void AnInputAManagedEntityTookAsksTheSdkWalkNothing()
    {
        // ...and it stays zero for a frame in which every input found a managed
        // target, which is every frame of a map whose entities are all managed.
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        Relay(registry, B, "dst");

        registry.KeyValue(A, "test_io_relay", "OnTrigger", "dst,Land,,0,-1");
        source.OnTrigger.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(0L, registry.Directory.IoSdkAsks);
    }

    [Fact]
    public void OneEventNothingManagedWantedIsOneAsk()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        var kept = registry.Directory.SdkDelivery;

        try
        {
            registry.Directory.SdkDelivery = (t, i, v, a) => 0;

            registry.KeyValue(A, "test_io_relay", "OnTrigger", "nobody,Land,,0,-1");
            source.OnTrigger.Fire();
            registry.Directory.ServiceIo(registry.Now);

            Assert.Equal(1L, registry.Directory.IoSdkAsks);
            Assert.Equal(0L, registry.Directory.IoSdkTook);
        }
        finally
        {
            registry.Directory.SdkDelivery = kept;
        }
    }

    [Fact]
    public void WhatTheSdkTookIsCountedSeparatelyFromWhatWasAsked()
    {
        // The two numbers are not the same question. IoUnhandled is the asks
        // that came back EMPTY; the difference is what the SDK hierarchy took,
        // and a build where the walk started answering would move one and not
        // the other.
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        var kept = registry.Directory.SdkDelivery;

        try
        {
            registry.Directory.SdkDelivery = (t, i, v, a) => 3;

            registry.KeyValue(A, "test_io_relay", "OnTrigger", "prop_ragdoll,Kill,,0,-1");
            source.OnTrigger.Fire();
            registry.Directory.ServiceIo(registry.Now);

            Assert.Equal(1L, registry.Directory.IoSdkAsks);
            Assert.Equal(3L, registry.Directory.IoSdkTook);
            Assert.Equal(0L, registry.Directory.IoUnhandled);
        }
        finally
        {
            registry.Directory.SdkDelivery = kept;
        }
    }

    [Fact]
    public void AProceduralNameCostsNoAsk()
    {
        var registry = Fresh();
        var source = Relay(registry, A, "src");
        var kept = registry.Directory.SdkDelivery;

        try
        {
            registry.Directory.SdkDelivery = (t, i, v, a) => 0;

            registry.KeyValue(A, "test_io_relay", "OnTrigger", "!nonsense,Land,,0,-1");
            source.OnTrigger.Fire();
            registry.Directory.ServiceIo(registry.Now);

            Assert.Equal(0L, registry.Directory.IoSdkAsks);
        }
        finally
        {
            registry.Directory.SdkDelivery = kept;
        }
    }
}
