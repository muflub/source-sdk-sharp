using System;
using System.Linq;

using SourceSharp.Abi;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// WIRING TWO ENTITIES UP WHILE THE GAME IS RUNNING —
/// <c>ScriptedEntity.OnAddOutput</c>.
///
/// <para>
/// This was a declared refusal: <c>ContractBaseline.Universal</c> listed
/// <c>input|AddOutput</c> among the inputs <c>CBaseEntity</c> declares and
/// nothing managed answers. The reason was real and has gone — the connection
/// table lived in native memory, so there was nothing on this side to hang one
/// on, and step 2a moved the whole table into <see cref="EntityDirectory"/>.
/// </para>
///
/// <para>
/// <b>WHY IT MATTERS TO THIS LANE.</b> Without it, nothing outside the map's own
/// parse can make a connection — so a test or a live gate can only exercise the
/// wiring dm_lockdown happened to declare. That is why <c>tools/live-gate</c>
/// could assert that 191 connections survived a hot reload and could not assert
/// that ONE of them still worked. A connection made through this input is
/// indistinguishable from a declared one afterwards, including to
/// <c>EntityDirectory.SaveIo</c>, so a test of it is a test of the real path.
/// </para>
/// </summary>
public class AddOutputTests
{
    private static readonly EntityHandle Source = new(0x0004_0021u);
    private static readonly EntityHandle Target = new(0x0004_0022u);

    private static ScriptedEntityRegistry Wired() => Wired(out _);

    private static ScriptedEntityRegistry Wired(out RecordingEngine engine)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(AddOutputTests).Assembly);
        registry.Services = Services(out engine);

        // CStandaloneEntity::KeyValue, which is what Entities.SetKeyValue reaches
        // for an entity with a native shell: the shell's arm (it keeps
        // "targetname", among others), then the UNCONDITIONAL offer back to
        // managed code, answered by this registry -- ENTITY_OP_KEY_VALUE.
        var recorder = engine;
        recorder.ReturnsEachCall("Entities.SetKeyValue", () =>
        {
            EngineCall call = recorder.Last("Entities.SetKeyValue")!.Value;
            string key = call.ArgAs<string>(1);
            bool shell = key.Equals("targetname", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("origin", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("angles", StringComparison.OrdinalIgnoreCase);
            bool managed = registry.KeyValue(call.ArgAs<EntityHandle>(0), "reload_fixture",
                                             key, call.ArgAs<string>(2));
            return shell || managed;
        });

        registry.KeyValue(Source, "reload_fixture", "speed", "1");
        registry.KeyValue(Target, "reload_fixture", "speed", "2");
        registry.KeyValue(Target, "reload_fixture", "targetname", "sink");

        return registry;
    }

    private static bool Fire(ScriptedEntityRegistry registry, EntityHandle at, string value)
        => registry.Input(at, "AddOutput", value, 0f, EntityHandle.None);

    /// <summary>
    /// What the input DID, which is a different question from whether it was
    /// accepted. <c>Input</c> answers true because a bound
    /// <c>[ScriptedEvent]</c> was invoked — that is what acceptance means — and
    /// the route is what says whether the value named an output or a key.
    /// </summary>
    private static bool Wire(ScriptedEntityRegistry registry, EntityHandle at, string value)
        => registry.AddOutput(at, registry.Get(at)!, value);

    //-- the connection arm ---------------------------------------------------

    [Fact]
    public void AddOutputHangsAConnectionOnADeclaredOutput()
    {
        var registry = Wired();

        Assert.True(Wire(registry, Source, "OnFirst sink:Nudge::0:-1"));
        Assert.Equal(1, registry.Directory.ConnectionCount);
    }

    /// <summary>
    /// The keyvalue arm is the entity's own KeyValue, and stock's moves the
    /// entity on "origin" (SetAbsOrigin, which networks the move): an
    /// AddOutput "origin x y z" moves a running entity on the client too.
    /// </summary>
    [Fact]
    public void AddOutputOriginMovesTheEntityAsStockKeyValueDoes()
    {
        var registry = Wired(out var engine);

        Assert.True(Wire(registry, Target, "origin 10 20 30"));
        Assert.Equal(new Vector(10f, 20f, 30f), engine.Last("Entities.SetOrigin")?.ArgAs<Vector>(1));
    }

    [Fact]
    public void AConnectionMadeAtRuntimeCarriesItsTargetAndInput()
    {
        var registry = Wired();
        Fire(registry, Source, "OnFirst sink:Nudge::0:-1");

        EventAction action = registry.Directory.ConnectionsOf(Source).On("OnFirst").Single();

        Assert.Equal("sink", action.Target);
        Assert.Equal("Nudge", action.TargetInput);
    }

    [Fact]
    public void AConnectionMadeAtRuntimeCarriesItsDelayAndFiringBudget()
    {
        var registry = Wired();
        Fire(registry, Source, "OnFirst sink:Nudge:kept:2.5:1");

        EventAction action = registry.Directory.ConnectionsOf(Source).On("OnFirst").Single();

        Assert.Equal(2.5f, action.Delay);
        Assert.Equal(1, action.TimesToFire);
        Assert.Equal("kept", action.Parameter);
    }

    /// <summary>
    /// The whole point: it delivers. A connection that is in the table and does
    /// not fire is the silent half-working shape this tree keeps meeting.
    /// </summary>
    [Fact]
    public void AConnectionMadeAtRuntimeDelivers()
    {
        var registry = Wired();
        Fire(registry, Source, "OnFirst sink:Nudge::0:-1");

        var sink = (ReloadFixtureEntity)registry.Get(Target)!;
        var live = (ReloadFixtureEntity)registry.Get(Source)!;

        live.OnFirst.Fire();
        // ServiceIo and not RunFrame -- the sub-ioqueue lane moved the I/O
        // pass out of the think pass and up to stock's position in
        // ServerGameDll.GameFrame. RunFrame is the thinks now.
        registry.Directory.ServiceIo(1.0f);

        Assert.Equal(1, sink.Nudges);
    }

    /// <summary>
    /// <c>IsDeclaredOutput</c> matches a mapper's casing; the table is keyed by
    /// the name the OUTPUT fires under. Storing the caller's spelling would hang
    /// a connection nothing ever fires.
    /// </summary>
    [Fact]
    public void AConnectionIsHungUnderTheOutputsOwnSpelling()
    {
        var registry = Wired();
        Fire(registry, Source, "onfirst sink:Nudge::0:-1");

        Assert.Single(registry.Directory.ConnectionsOf(Source).On("OnFirst"));
    }

    //-- the keyvalue arm, which is stock's other one -------------------------

    [Fact]
    public void AddOutputWritesAKeyWhenTheHeadIsNotAnOutput()
    {
        var registry = Wired();

        Assert.True(Wire(registry, Source, "speed 512.5"));
        Assert.Equal(512.5f, ((ReloadFixtureEntity)registry.Get(Source)!).Vars.Speed);
    }

    /// <summary>
    /// THE KEYVALUE ARM IS THE ENTITY'S OWN <c>KeyValue</c>, which is stock's:
    /// <c>CBaseEntity::InputAddOutput</c> ends in <c>KeyValue( sOutputName,
    /// sChar+1 )</c> on the entity itself. For an entity with a native shell that
    /// is <c>Entities.SetKeyValue</c> -- the shell's arm first (it keeps the name
    /// <c>GetName</c> reads), then the unconditional offer to managed code. A
    /// managed-only offer renamed the entity in the name index and nowhere else.
    /// </summary>
    [Fact]
    public void AddOutputKeyvalueGoesThroughTheEntitysOwnKeyValue()
    {
        var registry = Wired(out var engine);

        Assert.True(Wire(registry, Source, "targetname renamed"));

        EngineCall? call = engine.Last("Entities.SetKeyValue");

        Assert.True(call.HasValue, "AddOutput's keyvalue arm reaches the entity's own KeyValue");
        Assert.Equal(Source, call!.Value.ArgAs<EntityHandle>(0));
        Assert.Equal("targetname", call.Value.ArgAs<string>(1));
        Assert.Equal("renamed", call.Value.ArgAs<string>(2));
    }

    [Fact]
    public void AddOutputMakesNoConnectionForAKeyvalue()
    {
        var registry = Wired();
        Fire(registry, Source, "speed 512.5");

        Assert.Equal(0, registry.Directory.ConnectionCount);
    }

    //-- the refusal, which must not be silent --------------------------------

    [Fact]
    public void AddOutputRefusesWhatIsNeitherAnOutputNorAKey()
    {
        var registry = Wired();

        Assert.False(Wire(registry, Source, "nonsense whatever"));
        Assert.Equal(0, registry.Directory.ConnectionCount);
    }

    [Fact]
    public void AddOutputRefusesAnEmptyValue()
    {
        var registry = Wired();
        Assert.False(Wire(registry, Source, ""));
    }

    /// <summary>
    /// A value with no space in it is the ENGINE's own refusal, and it is
    /// refused here for its reason: <c>CBaseEntity::InputAddOutput</c> warns
    /// "AddOutput input fired with bad string" and does nothing at all when
    /// <c>strchr(' ')</c> finds nothing.
    /// </summary>
    [Fact]
    public void AValueWithNoSpaceInItIsRefused()
    {
        var registry = Wired();

        Assert.False(Wire(registry, Source, "OnFirst"));
        Assert.Equal(0, registry.Directory.ConnectionCount);
    }

    /// <summary>
    /// COLONS BECOME COMMAS, which is what the engine does in exactly this place
    /// and for the reason its own comment gives — "Hammer doesn't allow ,'s
    /// inside parameters" (<c>baseentity.cpp:6630</c>). Without the swap every
    /// field after the first lands inside the target's NAME.
    /// </summary>
    [Fact]
    public void ACommaDelimitedActionIsReadTheSameWayAsAColonDelimitedOne()
    {
        var registry = Wired();
        Wire(registry, Source, "OnFirst sink,Nudge,,0,-1");

        Assert.Equal("sink", registry.Directory.ConnectionsOf(Source).On("OnFirst").Single().Target);
    }

    //-- and the reason this lane wanted it -----------------------------------

    /// <summary>
    /// A connection made at runtime is carried across a hot reload by the same
    /// <c>SaveIo</c> that carries a declared one. That equivalence is what makes
    /// the live gate's wiring assertion a test of the real path rather than of a
    /// second mechanism.
    /// </summary>
    [Fact]
    public void AConnectionMadeAtRuntimeIsCarriedAcrossAReload()
    {
        var registry = Wired();
        Fire(registry, Source, "OnFirst sink:Nudge:kept:2.5:1");

        SavedIo saved = registry.Directory.SaveIo(0f);

        SavedConnection carried = saved.Connections
            .Single(c => c.OutputName == "OnFirst" && c.Target == "sink");

        Assert.Equal("Nudge", carried.TargetInput);
        Assert.Equal("kept", carried.Parameter);
        Assert.Equal(1, carried.TimesToFire);
    }

    /// <summary>
    /// And it comes back working, not merely recorded — the distinction 2a's own
    /// comment draws and the one the live gate could not make.
    /// </summary>
    [Fact]
    public void AConnectionMadeAtRuntimeStillDeliversAfterAReload()
    {
        var registry = Wired();
        Fire(registry, Source, "OnFirst sink:Nudge::0:-1");

        SavedIo saved = registry.Directory.SaveIo(0f);

        var reloaded = new ScriptedEntityRegistry(TestEngines.Server());
        reloaded.RegisterAll(typeof(AddOutputTests).Assembly);
        reloaded.Services = Services();
        reloaded.KeyValue(Source, "reload_fixture", "speed", "1");
        reloaded.KeyValue(Target, "reload_fixture", "speed", "2");
        reloaded.KeyValue(Target, "reload_fixture", "targetname", "sink");

        reloaded.Directory.RestoreIo(saved, 0f);

        var sink = (ReloadFixtureEntity)reloaded.Get(Target)!;
        ((ReloadFixtureEntity)reloaded.Get(Source)!).OnFirst.Fire();
        reloaded.Directory.ServiceIo(1.0f);

        Assert.Equal(1, sink.Nudges);
    }
}
