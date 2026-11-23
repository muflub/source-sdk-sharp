extern alias client;

using SourceSharp.Abi;
using SourceSharp.Tests;

using Xunit;

using CI = client::SourceSharp.Client.Interop;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Mirror = client::TF2.Client.Entities.ClientRespawnRoomVisualizer;
using PlayerResource = client::TF2.Client.Player.ClientTFPlayerResource;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using RoundView = client::TF2.Shared.Rules.ITFRoundStateView;
using RoundStateId = client::TF2.Shared.Rules.RoundState;
using TFTeam = client::TF2.Shared.TFTeam;

namespace TF2.Tests.Client;

/// <summary>
/// The visualizer's client draw (<see cref="Mirror"/>): stock's
/// <c>C_FuncRespawnRoomVisualizer::DrawModel</c> arms
/// (client/tf/c_func_respawnroom.cpp:48-64) — nothing in the win state,
/// nothing for a local player on the wall's own team, and the brush itself
/// (<c>BaseClass::DrawModel</c>) for every other eye, with the null-player arm
/// drawing for everybody.
/// </summary>
public sealed class ClientRespawnRoomVisualizerTests
{
    private const int Local = 2;

    private static readonly EntityHandle Wall = new(0x0004_0031u);

    private const int Drawn = client::SourceSharp.Client.Standalone.ClientStandaloneEntity.DrawsModel;

    // The brush's own box, as the client's model reads it (cp_badlands' *53).
    private static readonly Vector BrushMins = new(-88f, -4f, -56f);
    private static readonly Vector BrushMaxs = new(88f, 4f, 56f);

    private sealed class FakeFx : CI.IFxRender
    {
        public readonly List<(int Material, CI.FxVertex[] Vertices, bool ByteColor)> Draws = [];
        public readonly Dictionary<string, int> Materials = [];
        public readonly List<int> Released = [];
        public int LocalPlayerAnswer;

        public int MaterialFind(string name)
        {
            if (!Materials.TryGetValue(name, out int id))
                id = Materials[name] = Materials.Count + 1;
            return id;
        }

        public void MaterialRelease(int material) => Released.Add(material);

        public void MaterialAddRef(int material) { }

        public string MaterialName(int material) => "";

        public void DrawQuads(int material, ReadOnlySpan<CI.FxVertex> vertices, bool byteColor)
            => Draws.Add((material, vertices.ToArray(), byteColor));

        public CI.FxView View() => default;

        public Vector MainViewOrigin() => default;

        public int LocalPlayer() => LocalPlayerAnswer;

        public QAngle ViewAngles() => default;

        public int EntityAttachment(int entIndex, int attachment, out Vector origin, out string model)
        {
            origin = default;
            model = "";
            return -1;
        }

        public void DispatchParticleEffect(int systemIndex, Vector start, Vector end, QAngle angles, int entIndex) { }

        public bool EntityRenderAngles(int entIndex, out QAngle angles)
        {
            angles = default;
            return false;
        }

        public void EjectBrass(Vector origin, QAngle angles, QAngle gunAngles, int type) { }
    }

    /// <summary>The round state, frozen for the fact.</summary>
    private sealed class FrozenRound(RoundStateId state) : RoundView
    {
        public RoundStateId State { get; } = state;

        public TFTeam Winner { get; } = TFTeam.Unassigned;
    }

    private sealed class Rig
    {
        public readonly Engine Engine = new();
        public readonly FakeFx Fx = new();
        public readonly PlayerResources Resources = new();

        public Rig(RoundStateId round = RoundStateId.RoundRunning, bool modelKnown = true)
        {
            Engine.Returns("WorldQuery.ClientEntityModelBounds", modelKnown);
            Engine.Returns("WorldQuery.ClientEntityModelBounds.Mins", BrushMins);
            Engine.Returns("WorldQuery.ClientEntityModelBounds.Maxs", BrushMaxs);
            Engine.Returns("WorldQuery.ClientEntityModelBounds.Angles", Vector.Zero);
            Rounds = new FrozenRound(round);
        }

        public FrozenRound Rounds { get; }

        /// <summary>A wall the create has run for, on a team, with the local player's row on another (0: none).</summary>
        public Mirror Created(byte team, byte localTeam = 0)
        {
            var viz = new Mirror(Engine, Fx, Engine, Rounds, Resources);
            viz.Handle = Wall;
            viz.State.BindLocal();
            viz.State.Team = team;
            viz.BindEngine(Engine, Engine);

            if (localTeam != 0)
            {
                var cross = new PlayerResource(Engine, Resources);
                cross.State.BindLocal();
                cross.State.Team[Local] = localTeam;
                cross.OnSpawn();
            }

            viz.OnDataChanged(default, DataUpdateType.Created);
            return viz;
        }
    }

    /// <summary>The win state's arm: nothing draws, the rooms stand open; DrawModel answers stock's 1.</summary>
    [Fact]
    public void NothingDrawsDuringTeamWin()
    {
        var rig = new Rig(RoundStateId.TeamWin);
        rig.Fx.LocalPlayerAnswer = Local;
        var viz = rig.Created((byte)TFTeam.Blue, localTeam: (byte)TFTeam.Red);

        Assert.Equal(1, viz.OnDraw());
        Assert.Empty(rig.Fx.Draws);
    }

    /// <summary>
    /// The own-team arm: a blue player sees through his own room's doorway —
    /// nothing drawn, the draw answers stock's 1. Same wall, but the local row
    /// carries the other team, and the brush draws.
    /// </summary>
    [Fact]
    public void ATeammateSeesThroughTheDoorwayAndAnEnemyGetsTheBrush()
    {
        var mate = new Rig();
        mate.Fx.LocalPlayerAnswer = Local;
        var wall = mate.Created((byte)TFTeam.Blue, localTeam: (byte)TFTeam.Blue);
        Assert.Equal(1, wall.OnDraw());

        var enemy = new Rig();
        enemy.Fx.LocalPlayerAnswer = Local;
        var other = enemy.Created((byte)TFTeam.Blue, localTeam: (byte)TFTeam.Red);
        Assert.Equal(Drawn, other.OnDraw());
    }

    /// <summary>Stock's null-player arm: with no local player yet, every eye gets the brush.</summary>
    [Fact]
    public void EveryEyeGetsTheBrushWithNoLocalPlayer()
    {
        var rig = new Rig();
        // Same team as the wall — but nobody is here yet, so the draw answers for everybody.
        var viz = rig.Created((byte)TFTeam.Blue, localTeam: (byte)TFTeam.Blue);
        Assert.Equal(Drawn, viz.OnDraw());
    }

    /// <summary>
    /// The drawn arm is stock's <c>BaseClass::DrawModel</c>: the map's brush in
    /// its own material, for either team's wall. The class draws nothing of its
    /// own over it — no sprite, no billboard in a team colour.
    /// </summary>
    [Fact]
    public void TheEnemysDrawIsTheBrushAndNothingOfItsOwn()
    {
        foreach ((byte team, byte local) in new[] { ((byte)TFTeam.Red, (byte)TFTeam.Blue), ((byte)TFTeam.Blue, (byte)TFTeam.Red) })
        {
            var rig = new Rig();
            rig.Fx.LocalPlayerAnswer = Local;
            var viz = rig.Created(team, localTeam: local);

            Assert.Equal(Drawn, viz.OnDraw());
            Assert.Empty(rig.Fx.Draws);
            Assert.Empty(rig.Fx.Materials);
        }
    }

    /// <summary>
    /// The create asks for the class's draw hook over the brush's own box (the
    /// model's bounds, stock's render bounds), in the translucent pass.
    /// </summary>
    [Fact]
    public void TheCreateAsksForTheDrawOverTheBrushsBox()
    {
        var rig = new Rig();
        rig.Created((byte)TFTeam.Red);

        var draw = Assert.Single(rig.Engine.Calls, c => c.Member == "EntityRender.SetCustomDraw");
        Assert.Equal(new object?[] { Wall, BrushMins, BrushMaxs }, draw.Args);
        var blend = Assert.Single(rig.Engine.Calls, c => c.Member == "EntityRender.SetBlending");
        Assert.Equal(true, blend.Args[1]);
    }

    /// <summary>
    /// With the model's box not known yet the draw hook is not asked for over
    /// an empty box (which would cull the brush); the next update asks again.
    /// </summary>
    [Fact]
    public void AnUnknownBoxWaitsForTheNextUpdate()
    {
        var rig = new Rig(modelKnown: false);
        var viz = rig.Created((byte)TFTeam.Red);
        Assert.False(rig.Engine.Called("EntityRender.SetCustomDraw"));

        rig.Engine.Returns("WorldQuery.ClientEntityModelBounds", true);
        viz.OnDataChanged(default, DataUpdateType.DataTableChanged);
        Assert.Equal(1, rig.Engine.CountOf("EntityRender.SetCustomDraw"));
    }

    /// <summary>The brush is drawn at its DrawModel: no prepared form in the view's batch.</summary>
    [Fact]
    public void TheDrawIsMadeInPlace()
    {
        var viz = new Rig().Created((byte)TFTeam.Red);
        Assert.Equal(client::SourceSharp.Client.Standalone.ClientStandaloneEntity.DrawsInPlace, viz.OnDrawBatched());
    }
}
