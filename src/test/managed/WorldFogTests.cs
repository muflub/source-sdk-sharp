extern alias client;
using ClientFogState = client::SourceSharp.Shared.FogState;
using System.Linq;
using System.Reflection;

using SourceSharp.Abi;
using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The world-fog wire — plan_entity.md step 8e-render.
///
/// <para>
/// <b>WHY EVERY LINK IS ASSERTED APART.</b> World fog did not render on this
/// client for the whole life of the port, and nothing anywhere failed: a fog
/// that never arrives renders as no fog, and a map with no fog controller
/// renders as no fog, so the two are one screenshot. Step 7c measured it and
/// correctly made it a printed ledger row rather than a feature. Anything that
/// can be observed only by looking at the screen is exactly what these facts
/// must not rely on, so each stage of the chain is a number:
/// </para>
///
/// <list type="number">
/// <item>the master controller is found, and ONLY the master;</item>
/// <item>its numbers go into the player's networked state;</item>
/// <item>they survive the round trip through <c>PlayerNetState</c>;</item>
/// <item>the client turns the LOCAL player's copy into a push;</item>
/// <item>"no fog controller" is a CLEAR and not a fog that is switched off.</item>
/// </list>
/// </summary>
public class WorldFogTests
{
    static WorldFogTests()
    {
        StandaloneNetStates.Discover(typeof(PlayerNetState).Assembly, new RecordingLog());
    }

    private static readonly EntityHandle FogEnt = new(0x0059_0001u);
    private static readonly EntityHandle FogEnt2 = new(0x0059_0002u);

    private const int MasterFlag = 1;

    /// <summary>A registry holding fog controllers, which is also the directory.</summary>
    private static ScriptedEntityRegistry Registry(RecordingEngine engine)
    {
        var services = Services(out _);
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvFogController).Assembly);
        registry.Services = services;
        return registry;
    }

    private static ScriptedEntityRegistry WithFog(
        EntityHandle at, int spawnFlags, params (string Key, string Value)[] keys)
    {
        var services = Services(out var engine);
        engine.Ignores("WorldQuery.GetArea");
        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        engine.Returns("Entities.GetSpawnAngles", default(Vector));

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvFogController).Assembly);
        registry.Services = services;

        foreach (var (key, value) in keys)
            registry.KeyValue(at, EntityNames.EnvFogController, key, value);

        registry.Spawn(at, EntityNames.EnvFogController);
        return registry;
    }

    //=========================================================================
    // 1. WHICH CONTROLLER.
    //=========================================================================

    [Fact]
    public void AMapWithNoFogControllerHasNoFog()
    {
        var services = Services(out _);
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvFogController).Assembly);
        registry.Services = services;

        Assert.False(new WorldFog(registry).Read().Valid);
    }

    [Fact]
    public void TheMasterControllersNumbersAreTheWorldsFog()
    {
        var registry = WithFog(FogEnt, MasterFlag,
            ("fogenable", "1"), ("fogstart", "512"), ("fogend", "2048"),
            ("farz", "4096"), ("fogmaxdensity", "0.75"));

        FogState fog = new WorldFog(registry).Read();

        Assert.True(fog.Valid);
        Assert.True(fog.Enable);
        Assert.Equal(512f, fog.Start);
        Assert.Equal(2048f, fog.End);
        Assert.Equal(4096f, fog.FarZ);
        Assert.Equal(0.75f, fog.MaxDensity);
    }

    [Fact]
    public void AControllerWithNoMasterFlagIsStillTheMapsFog()
    {
        // STOCK'S RULE, IN STOCK'S OWN WORDS (CFogSystem::LevelInitPostEntity):
        // "On level load find the master fog controller. If no controller is
        // set as Master, use the first fog controller found."
        //
        // THIS FACT REPLACES ITS OWN OPPOSITE, and the live gate is why. The
        // first version of this lane read only the loop's IsMaster() arm and
        // missed the `m_pMasterController == NULL` arm above it, so it required
        // the flag. Every managed test agreed with it, because they were written
        // from the same wrong reading. Then cs_cl_fog on ss_sandbox reported "a
        // viewer is present and NOTHING has been pushed" -- and NEITHER map in
        // this build carries a spawnflags key on its env_fog_controller:
        //
        //     strings -a game/mod_sharp/maps/dm_lockdown.bsp | grep -B 12 env_fog_controller
        //       "fogenable" "1"  "fogstart" "512"  "fogend" "5000"   and no spawnflags
        //
        // so the strict rule delivered fog to nobody on either map. A wire that
        // works perfectly and renders no fog is the defect this whole step
        // closed, put back one layer up.
        var registry = WithFog(FogEnt, spawnFlags: 0,
            ("fogenable", "1"), ("fogstart", "512"));

        FogState fog = new WorldFog(registry).Read();

        Assert.True(fog.Valid);
        Assert.Equal(512f, fog.Start);
    }

    [Fact]
    public void FogSwitchedOffIsNotTheSameAsNoController()
    {
        // THE DISTINCTION THE WHOLE DESIGN RESTS ON. A zeroed struct reads as
        // "fog enabled = false", which every caller treats as a real answer;
        // NULL is "nobody has an answer", which is the early-out stock takes for
        // a player with no fog controller. Step 7c refused to return a zeroed
        // struct for exactly this reason, and this is that refusal as a fact.
        var registry = WithFog(FogEnt, MasterFlag, ("fogenable", "0"));

        FogState fog = new WorldFog(registry).Read();

        Assert.True(fog.Valid);
        Assert.False(fog.Enable);
        Assert.NotEqual(FogState.None, fog);
    }

    [Fact]
    public void AMasterOverridesAControllerFoundBeforeIt()
    {
        // The other half of stock's loop: a later controller that declares
        // itself master replaces whatever was taken first. A rule that only
        // took the first would pass the fact above and hand a map with a spare
        // controller and a flagged one the WRONG fog -- silently, because both
        // answers are a plausible fog.
        var services = Services(out var engine);
        engine.Ignores("WorldQuery.GetArea");
        engine.Returns("Entities.GetSpawnAngles", default(Vector));

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvFogController).Assembly);
        registry.Services = services;

        engine.Returns("Entities.GetSpawnFlags", 0);
        registry.KeyValue(FogEnt, EntityNames.EnvFogController, "fogenable", "1");
        registry.KeyValue(FogEnt, EntityNames.EnvFogController, "fogstart", "111");
        registry.Spawn(FogEnt, EntityNames.EnvFogController);

        engine.Returns("Entities.GetSpawnFlags", MasterFlag);
        registry.KeyValue(FogEnt2, EntityNames.EnvFogController, "fogenable", "1");
        registry.KeyValue(FogEnt2, EntityNames.EnvFogController, "fogstart", "222");
        registry.Spawn(FogEnt2, EntityNames.EnvFogController);

        FogState fog = new WorldFog(registry).Read();

        Assert.True(fog.Valid);
        Assert.Equal(222f, fog.Start);
    }

    //=========================================================================
    // 2 and 3. INTO THE PLAYER'S BLOB, AND BACK OUT.
    //=========================================================================

    private static FogState Sample => new(
        Valid: true, Enable: true, Blend: true, Radial: false,
        DirPrimary: new Vector(1f, 0f, 0f),
        ColorPrimary: 0x11223344u, ColorSecondary: 0x55667788u,
        Start: 128f, End: 4096f, FarZ: 8192f, MaxDensity: 0.5f);

    /// <summary><see cref="Sample"/>, as the client's copy of the shared type: what a client reads arrived over the wire.</summary>
    private static ClientFogState ClientSample => SideCopy.To<ClientFogState>(Sample);

    [Fact]
    public void EveryFieldSurvivesTheRoundTripThroughThePlayersState()
    {
        // ELEVEN FIELDS, NOT A SPOT CHECK. WriteTo and From are two hand-written
        // lists that have to agree, and the failure of a field left out of one
        // of them is a value that is silently zero on the client — a fog that is
        // the right colour and starts at zero distance, say, which reads as a
        // renderer bug.
        var state = new PlayerNetState();
        state.BindLocal();

        Sample.WriteTo(state);

        Assert.Equal(Sample, FogState.From(state));
    }

    [Fact]
    public void AFreshPlayersStateCarriesNoFog()
    {
        var state = new PlayerNetState();
        state.BindLocal();

        Assert.Equal(FogState.None, FogState.From(state));
        Assert.False(FogState.From(state).Valid);
    }

    [Fact]
    public void TheFogFieldsAreNotPredicted()
    {
        // The movement loop neither reads nor writes any of them, so a tolerance
        // on one would put fog on the misprediction ring for a value that can
        // only ever agree — noise in the one meter that has to stay meaningful.
        foreach (string name in new[]
                 {
                     "FogValid", "FogEnable", "FogBlend", "FogRadial", "FogDirPrimary",
                     "FogColorPrimary", "FogColorSecondary", "FogStart", "FogEnd",
                     "FogFarZ", "FogMaxDensity",
                 })
        {
            var property = typeof(PlayerNetState).GetProperty(name);
            Assert.True(property is not null, $"PlayerNetState declares {name}");
            Assert.Null(property!.GetCustomAttribute<PredictedAttribute>());
        }
    }

    //=========================================================================
    // 4 and 5. THE CLIENT'S PUSH.
    //=========================================================================

    private const int LocalPlayerIndex = 1;
    private const uint LocalHandle = 0x0100_0001u;
    private const uint OtherHandle = 0x0100_0002u;

    private sealed class Standalones : IClientStandalones
    {
        public readonly List<ClientStandaloneEntity> Entities = new();
        public IReadOnlyList<ClientStandaloneEntity> Live() => Entities;
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private static ClientSsPlayer Player(RecordingEngine engine, uint handle)
    {
        var player = new ClientSsPlayer(engine) { Handle = new EntityHandle(handle) };
        player.State.BindLocal();
        player.Enabled = false;
        return player;
    }

    private static (ClientWorldFog Fog, RecordingEngine Engine, Standalones Live) Client(
        int localIndex = LocalPlayerIndex)
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", localIndex);

        var live = new Standalones();
        return (new ClientWorldFog(live, engine, engine), engine, live);
    }

    [Fact]
    public void TheLocalPlayersFogIsPushedToTheRenderer()
    {
        var (fog, engine, live) = Client();
        var player = Player(engine, LocalHandle);
        ClientSample.WriteTo(player.State);
        live.Entities.Add(player);

        fog.Pass();

        Assert.Equal(1, fog.Pushes);
        Assert.True(fog.SawViewer);
        Assert.Contains(engine.Calls, c => c.Member == "Visibility.SetViewerFogParams");
    }

    [Fact]
    public void ThePushCarriesTheNumbersRatherThanAnEmptyStruct()
    {
        // A push that reached native with a zeroed struct would satisfy the
        // count above and render nothing, which is the state this whole step
        // exists to leave behind.
        var (fog, engine, live) = Client();
        var player = Player(engine, LocalHandle);
        ClientSample.WriteTo(player.State);
        live.Entities.Add(player);

        fog.Pass();

        FogParams pushed = (FogParams)engine.Calls
            .Last(c => c.Member == "Visibility.SetViewerFogParams").Args[0]!;

        Assert.Equal(1, pushed.Enable);
        Assert.Equal(1, pushed.Blend);
        Assert.Equal(0, pushed.Radial);
        Assert.Equal(0x11223344u, pushed.ColorPrimary);
        Assert.Equal(0x55667788u, pushed.ColorSecondary);
        Assert.Equal(128f, pushed.Start);
        Assert.Equal(4096f, pushed.End);
        Assert.Equal(8192f, pushed.FarZ);
        Assert.Equal(0.5f, pushed.MaxDensity);
        Assert.Equal(1f, pushed.DirPrimaryX);
    }

    [Fact]
    public void ANonLocalPlayersFogIsNotWhatTheViewerSees()
    {
        // Every player's blob carries fog today, because the tier tables have no
        // local-exclusive branch. Taking whichever player was found first would
        // be right by accident now and wrong the moment per-player fog exists,
        // which is what this shape leaves room for.
        var (fog, engine, live) = Client();

        var other = Player(engine, OtherHandle);
        ClientSample.WriteTo(other.State);
        live.Entities.Add(other);

        fog.Pass();

        Assert.False(fog.SawViewer);
        Assert.Equal(0, fog.Pushes);
        Assert.Equal(1, fog.Clears);
    }

    [Fact]
    public void AViewerWithNoFogControllerClearsRatherThanPushingFogOff()
    {
        var (fog, engine, live) = Client();
        live.Entities.Add(Player(engine, LocalHandle));

        fog.Pass();

        Assert.Equal(1, fog.Clears);
        Assert.Equal(0, fog.Pushes);
        Assert.Contains(engine.Calls, c => c.Member == "Visibility.ClearViewerFogParams");
    }

    [Fact]
    public void TheFogIsPushedOnceRatherThanEveryFrame()
    {
        var (fog, engine, live) = Client();
        var player = Player(engine, LocalHandle);
        ClientSample.WriteTo(player.State);
        live.Entities.Add(player);

        fog.Pass();
        fog.Pass();
        fog.Pass();

        Assert.Equal(3, fog.Passes);
        Assert.Equal(1, fog.Pushes);
    }

    [Fact]
    public void AChangedFogIsPushedAgain()
    {
        // The control for the fact above: an edge-triggered push that never
        // re-fired would pass it and leave a map's fog frozen at whatever it was
        // when the player joined.
        var (fog, engine, live) = Client();
        var player = Player(engine, LocalHandle);
        ClientSample.WriteTo(player.State);
        live.Entities.Add(player);

        fog.Pass();

        player.State.FogStart = 999f;
        fog.Pass();

        Assert.Equal(2, fog.Pushes);
    }

    [Fact]
    public void ALevelChangeMakesTheNextPassPushAgain()
    {
        // The compare is against what was last PUSHED, so a new map whose fog
        // happens to equal the old map's would never be pushed and the renderer
        // would go on holding the previous level's numbers.
        var (fog, engine, live) = Client();
        var player = Player(engine, LocalHandle);
        ClientSample.WriteTo(player.State);
        live.Entities.Add(player);

        fog.Pass();
        fog.LevelInit();
        fog.Pass();

        Assert.Equal(1, fog.Pushes);
        Assert.Equal(1, fog.Passes);
    }

    [Fact]
    public void AViewerThatGoesAwayClearsTheFog()
    {
        // Between a level change and the first player update there is no viewer,
        // and the fog held from the previous map would otherwise go on being
        // rendered over the new one.
        var (fog, engine, live) = Client();
        var player = Player(engine, LocalHandle);
        ClientSample.WriteTo(player.State);
        live.Entities.Add(player);

        fog.Pass();
        live.Entities.Clear();
        fog.Pass();

        Assert.Equal(1, fog.Pushes);
        Assert.Equal(1, fog.Clears);
    }
}
