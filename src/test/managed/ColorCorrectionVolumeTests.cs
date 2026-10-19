extern alias client;
using ColorCorrectionVolumeNetState = client::SourceSharp.Shared.ColorCorrectionVolumeNetState;
using SourceSharp.Abi;
using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// color_correction_volume, both halves.
///
/// <para>
/// The port's claim is a relocation: stock computed the weight on the server
/// by watching "the local player" cross the brush — NULL in multiplayer, so
/// the weight this mod's native entity networked never left zero — and the
/// managed pair moves the crossing test and the fade to the client, where the
/// per-viewer fact it turns on actually exists. So the server tests check the
/// entity freezes the right facts into the wire state, and the client tests
/// check the fade those facts drive.
/// </para>
/// </summary>
public class ColorCorrectionVolumeTests
{
    private static bool Same(Vector a, Vector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    //-------------------------------------------------------------------------
    // The server half: keys in, wire facts out.
    //-------------------------------------------------------------------------

    private static (ColorCorrectionVolume Volume, RecordingEngine Engine) Spawn(
        uint index, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(ColorCorrectionVolume).Assembly);
        registry.Services = Services(out var engine);
        engine.Ignores("Entities.GetOrigin");

        // The brush the map carved: a 64-unit-tall box up and east of a
        // non-zero origin, so a sign error in the world transform shows.
        engine.Returns("Entities.GetBounds",
            (new Vector(-32f, -32f, 0f), new Vector(32f, 32f, 64f)));

        var handle = new EntityHandle(index);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, EntityNames.ColorCorrectionVolume, key, value);

        registry.Spawn(handle, EntityNames.ColorCorrectionVolume);
        return ((ColorCorrectionVolume)registry.Get(handle)!, engine);
    }

    [Fact]
    public void ClassnameResolves()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(ColorCorrectionVolume).Assembly);
        registry.Services = Services(out var engine);
        engine.Ignores("Entities.GetOrigin");
        engine.Returns("Entities.GetBounds", (default(Vector), default(Vector)));

        var handle = new EntityHandle(50);
        registry.Spawn(handle, EntityNames.ColorCorrectionVolume);

        Assert.True(registry.Get(handle) is ColorCorrectionVolume, "color_correction_volume resolves to a managed entity");
    }

    [Fact]
    public void SpawnFreezesTheMapIntoTheWire()
    {
        var (volume, engine) = Spawn(51,
            ("filename", "materials/correction/cave.raw"),
            ("maxweight", "0.75"),
            ("fadeDuration", "2.5"));

        Assert.True(volume.State.LookupFile == "materials/correction/cave.raw", "the lookup filename is on the wire");
        Assert.True(volume.State.MaxWeight == 0.75f, "maxweight is on the wire — the client's fade ceiling");
        Assert.True(volume.State.FadeDuration == 2.5f, "fadeDuration is on the wire — the client's fade clock");
        Assert.True(volume.State.Enabled, "enabled by default");

        // The box travels as world-space corners, not as offsets the client
        // would have to resolve against a server-only origin slot. The test
        // engine answers a zero origin, so world space IS the offsets here;
        // what matters is that both corners arrived intact.
        Assert.True(Same(volume.State.BoundsMin, new Vector(-32f, -32f, 0f)), "the lower corner is on the wire");
        Assert.True(Same(volume.State.BoundsMax, new Vector(32f, 32f, 64f)), "the upper corner is on the wire");

        // Always transmitted, like its point sibling: a fade must keep
        // arriving after the player walks away from the brush.
        Assert.True(engine.Called("EntityRender.SetTransmitAlways"), "the volume transmits to everyone always");
    }

    [Fact]
    public void StartDisabledAndTheInputs()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(ColorCorrectionVolume).Assembly);
        registry.Services = Services(out var engine);
        engine.Ignores("Entities.GetOrigin");
        engine.Returns("Entities.GetBounds", (default(Vector), default(Vector)));

        var handle = new EntityHandle(52);
        registry.KeyValue(handle, EntityNames.ColorCorrectionVolume, "StartDisabled", "1");
        registry.Spawn(handle, EntityNames.ColorCorrectionVolume);

        var volume = (ColorCorrectionVolume)registry.Get(handle)!;

        Assert.True(!volume.State.Enabled, "StartDisabled spawns it off");

        registry.Input(handle, "Enable", "", 0f, EntityHandle.None);
        Assert.True(volume.State.Enabled, "Enable switches it on");

        registry.Input(handle, "Disable", "", 0f, EntityHandle.None);
        Assert.True(!volume.State.Enabled, "Disable switches it off");
    }

    //-------------------------------------------------------------------------
    // The client half: the crossing fade this whole port exists to relocate.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A volume whose lookup loaded, ready to reweigh. The box is the unit
    /// 0..100 cube; the clock starts at zero and each test advances it with
    /// <c>Returns("Clock.Now", …)</c> — the last stub wins.
    /// </summary>
    private static (ClientColorCorrectionVolume Volume, client::SourceSharp.Abi.RecordingEngine Engine, ColorCorrections Service)
        LiveVolume(float maxWeight = 1f, float fadeDuration = 10f, bool enabled = true)
    {
        var engine = ClientDoubles.Fake()
            .Returns("Visibility.ColorCorrectionAdd", 7)
            .Returns("Clock.Now", 0f);

        var service = new ColorCorrections(engine);

        // Built directly rather than through the registry, so the state needs
        // its own buffer — the registry is what binds a blob normally.
        var state = new ColorCorrectionVolumeNetState();
        state.BindLocal();
        state.LookupFile   = "materials/correction/cave.raw";
        state.BoundsMin    = new Vector(0f, 0f, 0f);
        state.BoundsMax    = new Vector(100f, 100f, 100f);
        state.MaxWeight    = maxWeight;
        state.FadeDuration = fadeDuration;
        state.Enabled      = enabled;

        var volume = new ClientColorCorrectionVolume(service, engine, engine, engine)
        {
            State = state,
        };

        volume.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.True(service.LiveCount == 1, "a loaded lookup joins the per-frame list");

        return (volume, engine, service);
    }

    private static float LastWeight(client::SourceSharp.Abi.RecordingEngine engine)
        => (float)engine.Last("Visibility.ColorCorrectionSetWeight")!.Value.Args[1]!;

    [Fact]
    public void SteppingInFadesUpAndOutFadesDown()
    {
        var (volume, engine, _) = LiveVolume(maxWeight: 0.8f, fadeDuration: 10f);

        Vector inside  = new(50f, 50f, 50f);
        Vector outside = new(500f, 50f, 50f);

        // Standing outside: nothing.
        volume.Reweigh(outside);
        Assert.True(LastWeight(engine) == 0f, "outside the box the weight is zero");

        // Step in, and a quarter of the fade later the weight is a quarter of
        // the ceiling: the full journey takes fadeDuration.
        volume.Reweigh(inside);
        engine.Returns("Clock.Now", 2.5f);
        volume.Reweigh(inside);
        Assert.True(Math.Abs(LastWeight(engine) - 0.2f) < 0.0001f, "a quarter into the fade is a quarter of maxweight");

        // The rest of the journey, plus slack: clamped at the ceiling, which
        // is the mapper's maxweight and not stock's flat 1.0.
        engine.Returns("Clock.Now", 60f);
        volume.Reweigh(inside);
        Assert.True(LastWeight(engine) == 0.8f, "the fade tops out at maxweight");

        // Step out: the same rate downward. The first call is the crossing
        // itself, at the same instant as the last inside frame, so no time has
        // passed yet and the weight holds.
        volume.Reweigh(outside);
        Assert.True(LastWeight(engine) == 0.8f, "the crossing frame itself moves nothing");

        engine.Returns("Clock.Now", 65f);
        volume.Reweigh(outside);
        Assert.True(Math.Abs(LastWeight(engine) - 0.4f) < 0.0001f, "half the fade time out is half the ceiling gone");
    }

    [Fact]
    public void AnInterruptedCrossingReversesFromWhereItIs()
    {
        var (volume, engine, _) = LiveVolume(maxWeight: 1f, fadeDuration: 10f);

        Vector inside  = new(50f, 50f, 50f);
        Vector outside = new(500f, 50f, 50f);

        // One second in, then straight back out: the second spent walking in
        // takes one second to walk off, not the ten stock's arithmetic charged
        // for an interrupted fade.
        volume.Reweigh(inside);
        engine.Returns("Clock.Now", 1f);
        volume.Reweigh(inside);
        Assert.True(Math.Abs(LastWeight(engine) - 0.1f) < 0.0001f, "one second in is a tenth of the fade");

        engine.Returns("Clock.Now", 1.5f);
        volume.Reweigh(outside);
        Assert.True(Math.Abs(LastWeight(engine) - 0.05f) < 0.0001f, "reversing mid-fade continues from the current weight");

        engine.Returns("Clock.Now", 2f);
        volume.Reweigh(outside);
        Assert.True(LastWeight(engine) == 0f, "and reaches zero at the same rate it climbed");
    }

    [Fact]
    public void DisabledIsZeroNowNotAFadeAndInstantIsInstant()
    {
        // Stock's think writes a hard zero every tick while disabled; Disable
        // has never been a fade.
        var (off, offEngine, _) = LiveVolume(enabled: false);

        off.Reweigh(new Vector(50f, 50f, 50f));
        Assert.True(LastWeight(offEngine) == 0f, "a disabled volume weighs nothing even with the player inside");

        // fadeDuration 0 snaps — which is also what stock's uninitialised
        // m_FadeDuration did, via a division by zero and a clamp.
        var (snap, snapEngine, _) = LiveVolume(maxWeight: 0.6f, fadeDuration: 0f);

        snap.Reweigh(new Vector(50f, 50f, 50f));
        Assert.True(LastWeight(snapEngine) == 0.6f, "no fade time means the ceiling immediately");

        snap.Reweigh(new Vector(500f, 50f, 50f));
        Assert.True(LastWeight(snapEngine) == 0f, "and zero immediately on the way out");
    }

    [Fact]
    public void RemovalReleasesTheLookup()
    {
        var (volume, engine, service) = LiveVolume();

        volume.OnRemoved();

        Assert.True(service.LiveCount == 0, "a removed volume leaves the per-frame list");
        Assert.True(engine.Last("Visibility.ColorCorrectionRemove") is { } call && (int)call.Args[0]! == 7,
            "and gives the manager its lookup back — a leak here tints the map for the rest of the level");
    }
}
