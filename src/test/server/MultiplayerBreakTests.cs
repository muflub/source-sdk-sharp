using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Who makes a broken prop's pieces in multiplayer, as stock's
/// CBreakableProp::Break and PropBreakableCreateAll decide it. With more than
/// one player and breakable_multiplayer on, a prop sends the clients a
/// te->PhysicsProp with the breakmodel flag and they break their own copies;
/// the server makes pieces only when the model's prop_data says
/// multiplayer_break server or both, and then only the pieces marked server.
/// With one player every piece is the server's. Found in an ss_sandbox A/B
/// (2026-10-28): stock's chair broke into no server entities, ours into six
/// prop_physics.
/// </summary>
public sealed unsafe class MultiplayerBreakTests : IDisposable
{
    private static readonly EntityHandle Prop = new(0x0005_0101u);
    private static readonly EntityHandle Gib = new(0x0005_0102u);

    private readonly CGlobalVarsRaw* _globals = (CGlobalVarsRaw*)NativeMemory.AllocZeroed((nuint)sizeof(CGlobalVarsRaw));

    public void Dispose() => NativeMemory.Free(_globals);

    /// <summary>A convar backend that keeps what it is told, so breakable_multiplayer can be turned off.</summary>
    private sealed class Backend : IConVarBackend
    {
        private readonly Dictionary<int, string> _held = new();
        private int _next;

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            _held[_next] = defaultValue;
            return _next++;
        }

        public void Set(int id, string value) => _held[id] = value;
        public string Get(int id) => _held.TryGetValue(id, out string? v) ? v : "";
        public string? ValueOf(string name) => null;
    }

    private sealed class SentProps : TempEntsDouble
    {
        public readonly List<(int ModelIndex, int Skin, int Flags, float Delay, TeFilterKind Filter)> Sent = new();

        public override void PhysicsProp(TeFilter filter, float delay, int modelIndex, int skin, Vector position,
                                         QAngle angles, Vector velocity, int flags, int effects)
            => Sent.Add((modelIndex, skin, flags, delay, filter.Kind));
    }

    private static (string, BreakPiece) Piece(string model, MultiplayerBreak mode)
        => (model, new BreakPiece { FadeTime = 20f, Health = 1f, MultiplayerBreakMode = (int)mode });

    private static readonly (string, BreakPiece)[] Pieces =
    {
        Piece("models/default.mdl", MultiplayerBreak.Default),
        Piece("models/server.mdl", MultiplayerBreak.ServerSide),
        Piece("models/client.mdl", MultiplayerBreak.ClientSide),
    };

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public required SentProps Temps;

        public void Break() => Registry.Input(Prop, "Break", "", 0f, EntityHandle.None);

        public string[] ServerPieces => Engine.AllOf("Entities.SetKeyValue")
                                              .Where(c => c.ArgAs<EntityHandle>(0) == Gib && c.ArgAs<string>(1) == "model")
                                              .Select(c => c.ArgAs<string>(2)).ToArray();
    }

    private Rig Spawned(int maxClients, string? mode = null, bool breakableMultiplayer = true)
    {
        _globals->maxClients = maxClients;

        var services = Services(out var engine);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var temps = new SentProps();
        services.AddInstance<ITempEnts>(temps);
        services.AddInstance<IEntityDirectory>(registry);
        var convars = new ConVarRegistry(new Backend());
        convars.RegisterTypes([typeof(BreakableMultiplayerConVar)]);
        convars.Get<BreakableMultiplayerConVar>()!.SetValue(breakableMultiplayer);
        services.AddInstance(convars);
        registry.Services = services;
        engine.Strict = false;

        string modeKey = mode is null ? "" : $" multiplayer_break {mode}";
        engine.Returns("Environment.Globals", new Globals(_globals))
              .Returns("EntityRender.GetModelKeyValueText", $"mdlkeyvalue {{ prop_data {{ health 10{modeKey} }} }}")
              .Returns("Precache.PrecacheModel", 42)
              .Returns("Precache.BreakPieceCount", Pieces.Length)
              .Returns("Precache.BreakPieceAt.pieces", (IReadOnlyList<(string, BreakPiece)>)Pieces)
              .Returns("Entities.Count", 100)
              .Returns("Entities.Create", Gib)
              .Returns("Entities.SetKeyValue", true);

        registry.KeyValue(Prop, EntityNames.PropPhysics, "model", "models/props_junk/wood_crate001a.mdl");
        registry.KeyValue(Prop, EntityNames.PropPhysics, "skin", "2");
        registry.Spawn(Prop, EntityNames.PropPhysics);
        ((PropPhysics)registry.Get(Prop)!).OnPhysicsCreated();
        return new Rig { Registry = registry, Engine = engine, Temps = temps };
    }

    [Fact]
    public void In_multiplayer_its_pieces_go_to_the_clients_and_the_server_makes_none()
    {
        var rig = Spawned(maxClients: 2);

        rig.Break();

        Assert.Equal(new[] { (42, 2, 1, -1f, TeFilterKind.Pas) }, rig.Temps.Sent);
        Assert.Empty(rig.ServerPieces);
    }

    /// <summary>The skin input writes m_nSkin, the field the keyfield writes, which the temp entity sends.</summary>
    [Fact]
    public void The_pieces_it_sends_wear_the_skin_its_input_last_set()
    {
        var rig = Spawned(maxClients: 2);

        rig.Registry.Input(Prop, "skin", "3", 3f, EntityHandle.None);
        rig.Break();

        Assert.Equal(3, Assert.Single(rig.Temps.Sent).Skin);
    }

    [Fact]
    public void With_one_player_every_piece_is_the_servers()
    {
        var rig = Spawned(maxClients: 1);

        rig.Break();

        Assert.Empty(rig.Temps.Sent);
        Assert.Equal(new[] { "models/default.mdl", "models/server.mdl", "models/client.mdl" }, rig.ServerPieces);
    }

    [Fact]
    public void With_breakable_multiplayer_off_every_piece_is_the_servers()
    {
        var rig = Spawned(maxClients: 2, breakableMultiplayer: false);

        rig.Break();

        Assert.Empty(rig.Temps.Sent);
        Assert.Equal(3, rig.ServerPieces.Length);
    }

    [Theory]
    [InlineData("server", 0)]
    [InlineData("both", 1)]
    [InlineData("client", 1)]
    public void Its_prop_datas_multiplayer_break_says_who_makes_what(string mode, int sent)
    {
        var rig = Spawned(maxClients: 2, mode);

        rig.Break();

        Assert.Equal(sent, rig.Temps.Sent.Count);
        Assert.Equal(mode == "client" ? Array.Empty<string>() : new[] { "models/server.mdl" }, rig.ServerPieces);
    }

    /// <summary>
    /// A physics_cannister's explosion calls PropBreakableCreateAll at its
    /// default location: in multiplayer it makes all but the pieces marked
    /// client; with one player, all.
    /// </summary>
    [Theory]
    [InlineData(2, "models/default.mdl,models/server.mdl")]
    [InlineData(1, "models/default.mdl,models/server.mdl,models/client.mdl")]
    public void A_cannister_makes_all_but_the_client_pieces_in_multiplayer(int maxClients, string expected)
    {
        _globals->maxClients = maxClients;
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PhysicsCannister).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Environment.Globals", new Globals(_globals))
              .Returns("Entities.IsValid", true)
              .Returns("EntityPhysics.GetMass", 10f)
              .Returns("Precache.BreakPieceCount", Pieces.Length)
              .Returns("Precache.BreakPieceAt.pieces", (IReadOnlyList<(string, BreakPiece)>)Pieces)
              .Returns("Entities.Count", 100)
              .Returns("Entities.Create", Gib)
              .Returns("Entities.SetKeyValue", true);
        registry.KeyValue(Prop, EntityNames.PhysicsCannister, "model", "models/props_c17/canister01a.mdl");
        registry.Spawn(Prop, EntityNames.PhysicsCannister);

        registry.Input(Prop, "Explode", "", 0f, EntityHandle.None);

        Assert.Equal(expected.Split(','), engine.AllOf("Entities.SetKeyValue")
                                                .Where(c => c.ArgAs<EntityHandle>(0) == Gib && c.ArgAs<string>(1) == "model")
                                                .Select(c => c.ArgAs<string>(2)));
    }

    /// <summary>
    /// PropBreakableCreateAll with its default location (a cannister's break)
    /// skips only the pieces marked client; CBreakableProp's server half
    /// (defaultLocation false) skips the default ones too.
    /// </summary>
    [Theory]
    [InlineData(BreakPieceSide.All, "models/default.mdl,models/server.mdl,models/client.mdl")]
    [InlineData(BreakPieceSide.DefaultLocation, "models/default.mdl,models/server.mdl")]
    [InlineData(BreakPieceSide.ServerMarked, "models/server.mdl")]
    public void Each_side_makes_the_pieces_stock_leaves_it(BreakPieceSide side, string expected)
    {
        var engine = Fake();
        engine.Returns("Precache.BreakPieceCount", Pieces.Length)
              .Returns("Precache.BreakPieceAt.pieces", (IReadOnlyList<(string, BreakPiece)>)Pieces)
              .Returns("Entities.Count", 100)
              .Returns("Entities.Create", Gib)
              .Returns("Entities.SetKeyValue", true);

        new PropBreak(new RecordingLog(), engine, engine, engine)
            .CreateAll("models/crate.mdl", Prop, new BreakContext(Vector.Zero, Vector.Zero, Vector.Zero, Vector.Zero,
                                                                  PropBreakPlan.DefaultBurstScale, CollisionGroup.Interactive, 0),
                       side: side);

        Assert.Equal(expected.Split(','), engine.AllOf("Entities.SetKeyValue")
                                                .Where(c => c.ArgAs<string>(1) == "model")
                                                .Select(c => c.ArgAs<string>(2)));
    }
}
