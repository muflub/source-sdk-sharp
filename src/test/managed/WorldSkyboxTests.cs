extern alias client;
using ClientSkyState = client::SourceSharp.Shared.SkyState;
using System.Collections.Generic;
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
/// The 3D-skybox wire — the <c>defect-skybox</c> lane.
///
/// <para>
/// <b>WHY EVERY LINK IS ASSERTED APART.</b> <see cref="WorldFogTests"/>'s
/// argument, and it is sharper here because THREE states render as the same
/// screenshot: a map with no <c>sky_camera</c>, a map with no sky FACE over the
/// player, and a wire that has stopped working. Step 7c measured the third and
/// correctly made it a printed ledger row rather than a feature, and it stayed
/// open longer than any other entry on this port. So each stage of the chain is
/// a number:
/// </para>
///
/// <list type="number">
/// <item>the map's sky camera is found;</item>
/// <item>its numbers go into the player's networked state;</item>
/// <item>they survive the round trip through <c>PlayerNetState</c>;</item>
/// <item>the client turns the LOCAL player's copy into a push;</item>
/// <item>"no sky camera" is a CLEAR and not an area-255 camera.</item>
/// </list>
///
/// <para>
/// The last of those is the modelling decision this wire turns on and the one a
/// simplification removes first — see <see cref="SkyState"/>.
/// </para>
/// </summary>
public class WorldSkyboxTests
{
    static WorldSkyboxTests()
    {
        StandaloneNetStates.Discover(typeof(PlayerNetState).Assembly, new RecordingLog());
    }

    private static readonly EntityHandle SkyEnt = new(0x005A_0001u);
    private static readonly EntityHandle SkyEnt2 = new(0x005A_0002u);

    private static ScriptedEntityRegistry WithSky(
        EntityHandle at, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SkyCamera).Assembly);
        registry.Services = Ignoring("WorldQuery.GetArea", "Entities.GetOrigin");

        foreach (var (key, value) in keys)
            registry.KeyValue(at, EntityNames.SkyCamera, key, value);

        registry.Spawn(at, EntityNames.SkyCamera);
        return registry;
    }

    //=========================================================================
    // 1. WHICH CAMERA.
    //=========================================================================

    [Fact]
    public void AMapWithNoSkyCameraHasNoSkybox()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SkyCamera).Assembly);
        registry.Services = Ignoring("WorldQuery.GetArea", "Entities.GetOrigin");

        Assert.False(new WorldSkybox(registry).Read().Valid);
    }

    [Fact]
    public void TheSkyCamerasNumbersAreTheWorldsSkybox()
    {
        var registry = WithSky(SkyEnt,
            ("scale", "8"), ("fogenable", "1"), ("fogstart", "512"),
            ("fogend", "2048"), ("fogmaxdensity", "0.75"));

        SkyState sky = new WorldSkybox(registry).Read();

        Assert.True(sky.Valid);
        Assert.Equal(8, sky.Scale);
        Assert.True(sky.FogEnable);
        Assert.Equal(512f, sky.FogStart);
        Assert.Equal(2048f, sky.FogEnd);
        Assert.Equal(0.75f, sky.FogMaxDensity);
    }

    [Fact]
    public void TheLastCameraToSpawnWins()
    {
        // SkyCamera.OnSpawn pushes to native at every spawn and the native side
        // keeps the last push, so the reconciler has to agree with it or the
        // two halves of one map disagree about which camera is filming. Stock
        // takes the HEAD of its list and says "UNDONE: Support multiple sky
        // cameras?" beside it; a map with two is already undefined.
        var registry = WithSky(SkyEnt, ("scale", "4"));

        registry.KeyValue(SkyEnt2, EntityNames.SkyCamera, "scale", "32");
        registry.Spawn(SkyEnt2, EntityNames.SkyCamera);

        SkyState sky = new WorldSkybox(registry).Read();

        Assert.True(sky.Valid);
        Assert.Equal(32, sky.Scale);
    }

    //=========================================================================
    // 2 and 3. INTO THE PLAYER'S BLOB, AND BACK OUT.
    //=========================================================================

    private static SkyState Sample => new(
        Valid: true,
        Origin: new Vector(64f, -128f, 256f),
        Scale: 16,
        Area: 3,
        FogEnable: true, FogBlend: true, FogRadial: false,
        FogDirPrimary: new Vector(1f, 0f, 0f),
        FogColorPrimary: 0x11223344u, FogColorSecondary: 0x55667788u,
        FogStart: 128f, FogEnd: 4096f, FogMaxDensity: 0.5f);

    /// <summary><see cref="Sample"/>, as the client's copy of the shared type: what a client reads arrived over the wire.</summary>
    private static ClientSkyState ClientSample => SideCopy.To<ClientSkyState>(Sample);

    [Fact]
    public void EveryFieldSurvivesTheRoundTripThroughThePlayersState()
    {
        // THIRTEEN FIELDS, NOT A SPOT CHECK. WriteTo and From are two
        // hand-written lists that have to agree, and a field left out of one of
        // them is silently zero on the client — an AREA of zero, say, which is a
        // real visleaf area and renders someone else's room as the sky.
        var state = new PlayerNetState();
        state.BindLocal();

        Sample.WriteTo(state);

        Assert.Equal(Sample, SkyState.From(state));
    }

    [Fact]
    public void AFreshPlayersStateCarriesNoSkybox()
    {
        var state = new PlayerNetState();
        state.BindLocal();

        Assert.Equal(SkyState.None, SkyState.From(state));
        Assert.False(SkyState.From(state).Valid);
    }

    [Fact]
    public void AZeroedStateIsNotAnAreaZeroCamera()
    {
        // THE DISTINCTION THE WHOLE WIRE TURNS ON. A zeroed blob has Area 0, and
        // 0 is a REAL visleaf area — the engine's "there is no sky camera" is
        // 255. If Valid did not exist, a player who had not been written yet
        // would be indistinguishable from one filming area 0.
        var state = new PlayerNetState();
        state.BindLocal();

        SkyState fresh = SkyState.From(state);

        Assert.False(fresh.Valid);
        Assert.Equal(0, fresh.Area);
        Assert.NotEqual(255, fresh.Area);
    }

    [Fact]
    public void TheSkyboxFieldsAreNotPredicted()
    {
        // The movement loop neither reads nor writes any of them, so a tolerance
        // on one would put the skybox on the misprediction ring for a value that
        // can only ever agree — noise in the one meter that has to stay
        // meaningful.
        foreach (string name in new[]
                 {
                     "SkyValid", "SkyOrigin", "SkyScale", "SkyArea", "SkyFogEnable",
                     "SkyFogBlend", "SkyFogRadial", "SkyFogDirPrimary",
                     "SkyFogColorPrimary", "SkyFogColorSecondary", "SkyFogStart",
                     "SkyFogEnd", "SkyFogMaxDensity",
                 })
        {
            var property = typeof(PlayerNetState).GetProperty(name);
            Assert.True(property is not null, $"PlayerNetState declares {name}");
            Assert.Null(property!.GetCustomAttribute<PredictedAttribute>());
        }
    }

    //=========================================================================
    // The server's reconciler.
    //=========================================================================

    [Fact]
    public void APlayerIsWrittenOnceAndThenLeftAlone()
    {
        var registry = WithSky(SkyEnt, ("scale", "16"));
        var skybox = new WorldSkybox(registry);

        skybox.Update();
        int afterFirst = skybox.Writes;

        skybox.Update();
        skybox.Update();

        // No player in this registry, so the interesting number is that the
        // poll ran and wrote nothing rather than throwing.
        Assert.Equal(3, skybox.Polls);
        Assert.Equal(afterFirst, skybox.Writes);
    }

    [Fact]
    public void ALevelShutdownForgetsTheCamera()
    {
        var registry = WithSky(SkyEnt, ("scale", "16"));
        var skybox = new WorldSkybox(registry);

        skybox.Update();
        Assert.True(skybox.Current.Valid);

        ((IGameLifecycle)skybox).OnLevelShutdown();

        Assert.False(skybox.Current.Valid);
        Assert.Equal(0, skybox.Polls);
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

    private static ClientSsPlayer Player(client::SourceSharp.Abi.RecordingEngine engine, uint handle)
    {
        var player = new ClientSsPlayer(engine) { Handle = new EntityHandle(handle) };
        player.State.BindLocal();
        player.Enabled = false;
        return player;
    }

    private static (ClientSkybox Sky, client::SourceSharp.Abi.RecordingEngine Engine, Standalones Live) Client(
        int localIndex = LocalPlayerIndex)
    {
        var engine = ClientDoubles.Fake();
        engine.Returns("Hud.LocalPlayerIndex", localIndex);

        var live = new Standalones();
        return (new ClientSkybox(live, engine, engine), engine, live);
    }

    [Fact]
    public void TheLocalPlayersSkyboxIsPushedToTheRenderer()
    {
        var (sky, engine, live) = Client();
        var player = Player(engine, LocalHandle);
        ClientSample.WriteTo(player.State);
        live.Entities.Add(player);

        sky.Pass();

        Assert.Equal(1, sky.Pushes);
        Assert.True(sky.SawViewer);
        Assert.Contains(engine.Calls, c => c.Member == "Visibility.SetViewerSkyboxParams");
    }

    [Fact]
    public void ThePushCarriesTheNumbersRatherThanAnEmptyStruct()
    {
        // A push that reached native with a zeroed struct would satisfy the
        // count above and render the wrong area, which is worse than rendering
        // nothing.
        var (sky, engine, live) = Client();
        var player = Player(engine, LocalHandle);
        ClientSample.WriteTo(player.State);
        live.Entities.Add(player);

        sky.Pass();

        SkyboxParams pushed = (SkyboxParams)engine.Calls
            .Last(c => c.Member == "Visibility.SetViewerSkyboxParams").Args[0]!;

        Assert.Equal(64f, pushed.OriginX);
        Assert.Equal(-128f, pushed.OriginY);
        Assert.Equal(256f, pushed.OriginZ);
        Assert.Equal(16, pushed.Scale);
        Assert.Equal(3, pushed.Area);
        Assert.Equal(1, pushed.FogEnable);
        Assert.Equal(1, pushed.FogBlend);
        Assert.Equal(0, pushed.FogRadial);
        Assert.Equal(1f, pushed.FogDirX);
        Assert.Equal(0x11223344u, pushed.FogColorPrimary);
        Assert.Equal(0x55667788u, pushed.FogColorSecondary);
        Assert.Equal(128f, pushed.FogStart);
        Assert.Equal(4096f, pushed.FogEnd);
        Assert.Equal(0.5f, pushed.FogMaxDensity);
    }

    [Fact]
    public void ANonLocalPlayersSkyboxIsNotWhatTheViewerSees()
    {
        // Every player's blob carries the skybox today, because the tier tables
        // have no local-exclusive branch. Taking whichever player was found
        // first would be right by accident now and wrong the moment a map gives
        // two players different sky cameras.
        var (sky, engine, live) = Client();

        var other = Player(engine, OtherHandle);
        ClientSample.WriteTo(other.State);
        live.Entities.Add(other);

        sky.Pass();

        Assert.False(sky.SawViewer);
        Assert.Equal(0, sky.Pushes);
        Assert.Equal(1, sky.Clears);
    }

    [Fact]
    public void AViewerWithNoSkyCameraClearsRatherThanPushingArea255()
    {
        var (sky, engine, live) = Client();
        live.Entities.Add(Player(engine, LocalHandle));

        sky.Pass();

        Assert.Equal(1, sky.Clears);
        Assert.Equal(0, sky.Pushes);
        Assert.Contains(engine.Calls, c => c.Member == "Visibility.ClearViewerSkyboxParams");
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Visibility.SetViewerSkyboxParams");
    }

    [Fact]
    public void TheSkyboxIsPushedOnceRatherThanEveryFrame()
    {
        var (sky, engine, live) = Client();
        var player = Player(engine, LocalHandle);
        ClientSample.WriteTo(player.State);
        live.Entities.Add(player);

        sky.Pass();
        sky.Pass();
        sky.Pass();

        Assert.Equal(3, sky.Passes);
        Assert.Equal(1, sky.Pushes);
    }

    [Fact]
    public void AChangedSkyboxIsPushedAgain()
    {
        // The control for the fact above: an edge-triggered push that never
        // re-fired would pass it and leave the skybox frozen at whatever it was
        // when the player joined.
        var (sky, engine, live) = Client();
        var player = Player(engine, LocalHandle);
        ClientSample.WriteTo(player.State);
        live.Entities.Add(player);

        sky.Pass();

        player.State.SkyArea = 9;
        sky.Pass();

        Assert.Equal(2, sky.Pushes);
    }

    [Fact]
    public void ALevelChangeMakesTheNextPassPushAgain()
    {
        // The compare is against what was last PUSHED, so a new map whose sky
        // camera happens to equal the old map's would never be pushed and the
        // renderer would go on setting the PREVIOUS level's visleaf area bit.
        var (sky, engine, live) = Client();
        var player = Player(engine, LocalHandle);
        ClientSample.WriteTo(player.State);
        live.Entities.Add(player);

        sky.Pass();
        sky.LevelInit();
        sky.Pass();

        Assert.Equal(1, sky.Pushes);
        Assert.Equal(1, sky.Passes);
    }

    [Fact]
    public void AViewerThatGoesAwayClearsTheSkybox()
    {
        // Between a level change and the first player update there is no viewer,
        // and the previous map's miniature world would otherwise go on being
        // drawn over the new one.
        var (sky, engine, live) = Client();
        var player = Player(engine, LocalHandle);
        ClientSample.WriteTo(player.State);
        live.Entities.Add(player);

        sky.Pass();
        live.Entities.Clear();
        sky.Pass();

        Assert.Equal(1, sky.Pushes);
        Assert.Equal(1, sky.Clears);
    }
}
