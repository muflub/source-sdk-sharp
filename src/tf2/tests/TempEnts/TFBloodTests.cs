extern alias client;

using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Server;

using Xunit;

// Per-side interop types: the server's copy under its plain name (this test
// project's unaliased view), the client's under the alias.
using RecordingEngine = SourceSharp.Abi.RecordingEngine;
using RecordingLog = SourceSharp.Abi.RecordingLog;
using TempEntRegistry = SourceSharp.Abi.TempEntRegistry;
using TempEntType = SourceSharp.Abi.TempEntType;
using TeFilters = SourceSharp.Abi.TeFilters;

using CAbi = client::SourceSharp.Abi;
using CInterop = client::SourceSharp.Client.Interop;
using CClient = client::SourceSharp.Client;
using CClientTempEnts = client::SourceSharp.Client.TempEnts.ClientTempEnts;

namespace TF2.Tests.TempEnts;

/// <summary>
/// The blood temp entity, both halves: what the server sends and what the
/// client plays. The round trip is two registries over the two sides' own
/// compilations, joined by the blob — the shape the game runs.
/// </summary>
public sealed class TFBloodTests
{
    /// <summary>Component-wise and EXACT: nothing on this path encodes.</summary>
    private static bool Same(Vector a, Vector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>Component-wise to a tight epsilon: the trig-derived vectors,
    /// where MathF's π/2 is not quite π/2.</summary>
    private static bool Near(Vector a, Vector b)
        => MathF.Abs(a.X - b.X) < 1e-4f && MathF.Abs(a.Y - b.Y) < 1e-4f && MathF.Abs(a.Z - b.Z) < 1e-4f;

    // =====================================================================
    // The round trip.
    // =====================================================================

    [Fact]
    public void The_blood_tempent_decodes_what_the_server_sends()
    {
        var (temps, engine, registry) = ServerWriter();
        TempEntType? type = registry.Find("CTETFBlood");
        Assert.True(type is { Registered: true }, "the blood tempent was not registered on the server side");

        temps.Blood(TeFilters.Broadcast, 0f, new Vector(1, 2, 3), new Vector(0, 1, 0), 2);

        var blob = engine.Last("Effects.TempEntPlayback")!.Value.ArgAs<byte[]>(4);

        var players = ClientPlayers(out OneShotSpy oneShots, out _, out _, out _, out _, out _);
        players.Dispatch(type!.Handle, blob);

        // The impact, decoded: at the origin, facing back into the hit
        // (VectorAngles( -normal ) of (0,1,0) is yaw 270, whose forward is
        // (0,-1,0)), both control points on it, and the entindex resolved to
        // the player the water level is asked of.
        var impact = oneShots.Starts[0];
        Assert.Equal("blood_impact_red_01", impact.Name);
        Assert.True(Same(new Vector(1, 2, 3), impact.SortOrigin), $"sort origin was {impact.SortOrigin}");
        Assert.Equal(0, impact.Points[0].Point);
        Assert.True(Same(new Vector(1, 2, 3), impact.Points[0].Position), $"point 0 was {impact.Points[0].Position}");
        Assert.Equal(1, impact.Points[1].Point);
        Assert.True(Same(new Vector(1, 2, 3), impact.Points[1].Position), $"point 1 was {impact.Points[1].Position}");
        Assert.True(Near(new Vector(0, -1, 0), impact.Forward), $"forward was {impact.Forward}");
    }

    // =====================================================================
    // The impact's five arms, in stock's order.
    // =====================================================================

    [Fact]
    public void A_won_birthday_roll_names_bday_blood()
    {
        var rig = new Rig(cvars: new() { ["tf_birthday"] = "1" }, rolls: [0.1f]);

        rig.Arrive();

        Assert.Equal("bday_blood", rig.OneShots.Starts[0].Name);
        // The roll is spent, and only that one: the spray that follows reads
        // no random at this normal and distance.
        var drawn = Assert.Single(rig.Random.Drawn);
        Assert.Equal((0f, 1f, 0.1f), drawn);
    }

    [Fact]
    public void A_lost_birthday_roll_falls_to_the_pyro_arm()
    {
        var rig = new Rig(cvars: new() { ["tf_birthday"] = "1", ["localplayer_visionflags"] = "1" },
                          rolls: [0.9f]);

        rig.Arrive();

        Assert.Equal("pyrovision_blood", rig.OneShots.Starts[0].Name);
        // The birthday arm's roll was still spent: stock's order, not a
        // reordering where the pyro arm reads first.
        Assert.Equal((0f, 1f, 0.9f), Assert.Single(rig.Random.Drawn));
    }

    [Fact]
    public void A_pyro_vision_hit_names_pyrovision_blood_without_rolling()
    {
        var rig = new Rig(cvars: new() { ["localplayer_visionflags"] = "1" });

        rig.Arrive();

        Assert.Equal("pyrovision_blood", rig.OneShots.Starts[0].Name);
        Assert.Empty(rig.Random.Drawn);
    }

    [Fact]
    public void Low_violence_names_the_lowV_impact()
    {
        var rig = new Rig(lowViolence: true);

        rig.Arrive();

        Assert.Equal("lowV_blood_impact_red_01", rig.OneShots.Starts[0].Name);
        Assert.Empty(rig.Random.Drawn);
    }

    [Fact]
    public void An_eyes_underwater_player_names_the_water_impact_and_sprays_nothing()
    {
        var rig = new Rig(waterLevel: CAbi.GameMovement.WlEyes);

        rig.Arrive();

        var only = Assert.Single(rig.OneShots.Starts);
        Assert.Equal("water_blood_impact_red_01", only.Name);
        Assert.Empty(rig.Random.Drawn);
    }

    [Fact]
    public void Low_violence_underwater_names_the_lowV_water_impact()
    {
        var rig = new Rig(waterLevel: CAbi.GameMovement.WlEyes, lowViolence: true);

        rig.Arrive();

        Assert.Equal("lowV_water_blood_impact_red_01", Assert.Single(rig.OneShots.Starts).Name);
    }

    // =====================================================================
    // The spray.
    // =====================================================================

    [Fact]
    public void A_view_within_400_units_gets_the_near_spray()
    {
        var rig = new Rig();

        rig.Arrive();

        Assert.Equal(2, rig.OneShots.Starts.Count);
        Assert.Equal("blood_spray_red_01", rig.OneShots.Starts[1].Name);
        Assert.Empty(rig.Random.Drawn);
    }

    [Fact]
    public void A_view_beyond_400_units_gets_the_far_spray()
    {
        var rig = new Rig();
        rig.Fx.Listener = new Vector(600, 0, 0);

        rig.Arrive();

        Assert.Equal("blood_spray_red_01_far", rig.OneShots.Starts[1].Name);
    }

    [Fact]
    public void A_spray_along_the_view_is_pushed_off_it()
    {
        var rig = new Rig(rolls: [1.0f, 0.3f]);
        // The normal (0,1,0) runs exactly along a view at yaw 90.
        rig.Fx.ViewAnglesAnswer = new QAngle(0, 90, 0);

        rig.Arrive();

        // The impact still faces the hit; the spray's normal was pushed LEFT
        // (the close-range roll came up 0.3), so its yaw turned past 135.
        var impact = rig.OneShots.Starts[0];
        var spray = rig.OneShots.Starts[1];
        Assert.True(Near(new Vector(0, -1, 0), impact.Forward), $"impact forward was {impact.Forward}");
        Assert.True(spray.Forward.X < -0.7f, $"pushed forward was {spray.Forward}");

        // Both rolls, in stock's order: the push from [0.5, 1.5] first, then
        // the close-range side roll.
        Assert.Equal([(0.5f, 1.5f, 1.0f), (0f, 1f, 0.3f)], rig.Random.Drawn);
    }

    [Fact]
    public void A_spray_across_the_view_is_not_pushed_and_rolls_nothing()
    {
        var rig = new Rig();

        rig.Arrive();

        // normal (0,1,0) against the default view's forward (1,0,0) is a dot
        // of zero: no push, no rolls, and the spray's angles are the normal's
        // — VectorAngles((0,1,0)) is yaw 90, whose forward is (0,1,0).
        var spray = rig.OneShots.Starts[1];
        Assert.True(Near(new Vector(0, 1, 0), spray.Forward), $"spray forward was {spray.Forward}");
        Assert.Empty(rig.Random.Drawn);
    }

    // =====================================================================
    // Rigs.
    // =====================================================================

    /// <summary>
    /// The server's writer, over this side's own compilation — the registry
    /// every side assembly's declarations are registered into, as the game
    /// composition does.
    /// </summary>
    private static (TFTempEnts Temps, RecordingEngine Engine, TempEntRegistry Registry) ServerWriter()
    {
        var engine = new RecordingEngine();
        int next = 5;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);
        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", typeof(TFTempEnts).Assembly, engine);
        return (new TFTempEnts(registry, engine, new RecordingLog()), engine, registry);
    }

    /// <summary>
    /// The client's players over its own compilation, with the one player
    /// added by hand — the built-through-the-container path is the wiring, and
    /// this fact is about the playing.
    /// </summary>
    private static CClientTempEnts ClientPlayers(
        out OneShotSpy oneShots, out Standalones standalones, out ConsoleFake console,
        out ScriptedRandom random, out CAbi.RecordingEngine engine, out FxSpy fx)
    {
        engine = new CAbi.RecordingEngine();
        int next = 5;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);
        var registry = new CAbi.TempEntRegistry(new CAbi.RecordingLog());
        registry.RegisterAll("Test", typeof(client::TF2.Client.ClientTeTFBlood).Assembly, engine);

        oneShots = new OneShotSpy();
        standalones = new Standalones();
        console = new ConsoleFake([]);
        random = new ScriptedRandom();
        fx = new FxSpy();

        var players = new CClientTempEnts(new CAbi.RecordingLog(), registry);
        Assert.True(players.Add("CTETFBlood",
            new client::TF2.Client.ClientTeTFBlood(oneShots, standalones, new LowViolence(false), console, random, fx)));
        return players;
    }

    /// <summary>UTIL_IsLowViolence's answer, fixed.</summary>
    private sealed class LowViolence(bool low) : client::SourceSharp.Client.Effects.IClientLowViolence
    {
        public bool IsLowViolence => low;
    }

    /// <summary>One player and its doubles, arrived at by hand.</summary>
    private sealed class Rig
    {
        public readonly OneShotSpy OneShots = new();
        public readonly ScriptedRandom Random;
        public readonly Standalones Standalones;
        public readonly ConsoleFake Console;
        public readonly FxSpy Fx = new();
        public readonly client::TF2.Client.ClientTeTFBlood Player;

        public Rig(byte waterLevel = 0, Dictionary<string, string>? cvars = null,
                   float[]? rolls = null, bool lowViolence = false)
        {
            Random = new ScriptedRandom(rolls ?? []);
            Standalones = new Standalones(waterLevel);
            Console = new ConsoleFake(cvars ?? []);

            Player = new client::TF2.Client.ClientTeTFBlood(
                OneShots, Standalones, new LowViolence(lowViolence), Console, Random, Fx);
        }

        public void Arrive(Vector? origin = null, Vector? normal = null, int entIndex = 2)
            => Player.Play(new client::TF2.Shared.TeTFBlood
            {
                Origin = origin ?? new Vector(10, 0, 0),
                Normal = normal ?? new Vector(0, 1, 0),
                EntIndex = entIndex,
            });
    }

    /// <summary>The one-shots' starts, in order.</summary>
    private sealed class OneShotSpy : CInterop.IClientParticleOneShots
    {
        public readonly List<(string Name, Vector SortOrigin, CInterop.ParticleControlPoint[] Points,
                              Vector Forward, Vector Right, Vector Up)> Starts = [];

        public string? NameFromIndex(int index) => throw new NotSupportedException();

        public bool Start(string name, Vector sortOrigin, ReadOnlySpan<CInterop.ParticleControlPoint> points,
                          Vector forward, Vector right, Vector up)
        {
            Starts.Add((name, sortOrigin, points.ToArray(), forward, right, up));
            return true;
        }
    }

    /// <summary>One player at entity index 2, at a given water level.</summary>
    private sealed class Standalones(byte waterLevel = 0) : CClient.IClientStandalones
    {
        public IReadOnlyList<CClient.Standalone.ClientStandaloneEntity> Live() => [];

        public CClient.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
        {
            if (entityIndex != 2)
                return null;

            var player = new CClient.Standalone.ClientSsPlayer(new CAbi.RecordingEngine());
            player.State.BindLocal();
            player.State.WaterLevel = waterLevel;
            return player;
        }
    }

    /// <summary>The engine's console, as a name table.</summary>
    private sealed class ConsoleFake(Dictionary<string, string> values) : CAbi.IEngineConsole
    {
        public void ExecuteServerCommand(string command) => throw new NotSupportedException();
        public string? FindConVarValue(string name) => values.GetValueOrDefault(name);
        public string GetConVarValue(int id) => throw new NotSupportedException();
        public void Msg(string message) { }
        public int RegisterConCommand(string name, string help, CAbi.ConVarFlags flags) => throw new NotSupportedException();
        public int RegisterConVar(string name, string defaultValue, string help, int flags, float? min = null, float? max = null) => throw new NotSupportedException();
        public void SendClientCommand(EntityHandle player, string command) => throw new NotSupportedException();
        public void SetConVarValue(int id, string value) => throw new NotSupportedException();
        public void SetConVarValueByName(string name, string value) => throw new NotSupportedException();
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out CAbi.NativeConsoleEntry entry) { entry = default; return false; }
        public void Warning(string message) { }
    }

    /// <summary>A scripted stream: the rolls in order, and what was drawn.</summary>
    private sealed class ScriptedRandom(params float[] values) : CAbi.IUniformRandom
    {
        private readonly Queue<float> _values = new(values);

        public readonly List<(float Min, float Max, float Value)> Drawn = [];

        public float RandomFloat(float min, float max)
        {
            if (_values.Count == 0)
                throw new InvalidOperationException($"unexpected RandomFloat({min}, {max})");

            float value = _values.Dequeue();
            Drawn.Add((min, max, value));
            return value;
        }

        public int RandomInt(int min, int max) => throw new NotSupportedException();
    }

    /// <summary>The view the spray reads.</summary>
    private sealed class FxSpy : CInterop.IFxRender
    {
        public Vector Listener;
        public QAngle ViewAnglesAnswer;

        public int MaterialFind(string name) => throw new NotSupportedException();
        public void MaterialAddRef(int material) { }
        public void MaterialRelease(int material) { }
        public string MaterialName(int material) => throw new NotSupportedException();
        public void DrawQuads(int material, ReadOnlySpan<CInterop.FxVertex> vertices, bool byteColor) { }
        public CInterop.FxView View() => throw new NotSupportedException();
        public Vector MainViewOrigin() => Listener;
        public int LocalPlayer() => throw new NotSupportedException();
        public QAngle ViewAngles() => ViewAnglesAnswer;
        public int EntityAttachment(int entIndex, int attachment, out Vector origin, out string model)
            => throw new NotSupportedException();
        public void DispatchParticleEffect(int systemIndex, Vector start, Vector end, QAngle angles, int entIndex) { }
        public bool EntityRenderAngles(int entIndex, out QAngle angles) => throw new NotSupportedException();
        public void EjectBrass(Vector origin, QAngle angles, QAngle gunAngles, int type) { }
    }
}
