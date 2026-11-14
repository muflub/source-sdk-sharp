extern alias client;

using SourceSharp.Abi;
using SourceSharp.Tests;

using Xunit;

using CI = client::SourceSharp.Client.Interop;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Mirror = client::TF2.Client.Entities.ClientRespawnRoomVisualizer;
using Dot = client::TF2.Client.Entities.ClientSniperDot;
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
/// nothing for a local player on the wall's own team, the team's translucent
/// billboard for every other eye, with the null-player arm drawing for
/// everybody. The shimmer's numbers are the ones the wire carries.
/// </summary>
public sealed class ClientRespawnRoomVisualizerTests
{
    private const int Local = 2;

    private static readonly EntityHandle Wall = new(0x0004_0031u);

    // The view SniperDot's rig uses: high above looking down −Z, axes the
    // billboard builder borrows, so the quad comes out axis-aligned.
    private static readonly CI.CurrentViewState TheView =
        new(0, new Vector(0f, 0f, 100f), new Vector(0f, 0f, -1f), new Vector(1f, 0f, 0f), new Vector(0f, 1f, 0f));

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

    private sealed class FakeView(CI.CurrentViewState view) : CI.IPixelVisSlots
    {
        public CI.CurrentViewState View() => view;

        public int QueryCreate() => 0;

        public void QueryDestroy(int query) { }

        public void QueryReset(int query) { }

        public void QueryPixels(int visible, int count, out int visiblePixels, out int countPixels)
            => visiblePixels = countPixels = 0;

        public void Diameters(ReadOnlySpan<float> spheres, Span<float> diameters) { }
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
        public readonly FakeView View = new(TheView);
        public readonly PlayerResources Resources = new();

        public Rig(RoundStateId round = RoundStateId.RoundRunning)
        {
            Engine.Returns("Entities.GetOrigin", Vector.Zero);
            Rounds = new FrozenRound(round);
        }

        public FrozenRound Rounds { get; }

        /// <summary>A wall the create has run for: the team's sprite picked, the draw asked for.</summary>
        public Mirror Drawn(byte team, byte localTeam = 0)
        {
            var viz = new Mirror(Engine, Fx, View, Rounds, Resources);
            viz.Handle = Wall;
            viz.State.BindLocal();
            viz.State.Team = team;
            viz.State.Width = 96f;
            viz.State.Height = 128f;
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

        /// <summary>The widest x/y offset from the drawn centre the quad reaches.</summary>
        public (float X, float Y) HalfSpans(CI.FxVertex[] vertices)
        {
            float x = 0f, y = 0f;
            foreach (var v in vertices)
            {
                x = MathF.Max(x, MathF.Abs(v.Position.X));
                y = MathF.Max(y, MathF.Abs(v.Position.Y));
            }
            return (x, y);
        }
    }

    /// <summary>The win state's arm: nothing draws, the rooms stand open.</summary>
    [Fact]
    public void NothingDrawsDuringTeamWin()
    {
        var rig = new Rig(RoundStateId.TeamWin);
        var viz = rig.Drawn((byte)TFTeam.Blue, localTeam: (byte)TFTeam.Red);

        Assert.Equal(0, viz.OnDraw());
        Assert.Empty(rig.Fx.Draws);
    }

    /// <summary>
    /// The own-team arm: a blue player sees through his own room's doorway —
    /// no quads, the draw answers zero. Same engine, same wall, but the local
    /// row carries the other team, and the shimmer stands.
    /// </summary>
    [Fact]
    public void ATeammateSeesThroughTheDoorwayAndAnEnemyGetsTheShimmer()
    {
        var mate = new Rig();
        mate.Fx.LocalPlayerAnswer = Local; // stock's pLocalPlayer arm needs a local here
        var wall = mate.Drawn((byte)TFTeam.Blue, localTeam: (byte)TFTeam.Blue);
        Assert.Equal(0, wall.OnDraw());
        Assert.Empty(mate.Fx.Draws);

        var enemy = new Rig();
        enemy.Fx.LocalPlayerAnswer = Local;
        var other = enemy.Drawn((byte)TFTeam.Blue, localTeam: (byte)TFTeam.Red);
        Assert.Equal(1, other.OnDraw());
        Assert.Single(enemy.Fx.Draws);
    }

    /// <summary>Stock's null-player arm: with no local player yet, every eye gets the wall.</summary>
    [Fact]
    public void EveryEyeGetsTheWallWithNoLocalPlayer()
    {
        var rig = new Rig();
        // Same team as the wall — but nobody is here yet, so the draw answers for everybody.
        var viz = rig.Drawn((byte)TFTeam.Blue, localTeam: (byte)TFTeam.Blue);
        Assert.Equal(1, viz.OnDraw());
        Assert.Single(rig.Fx.Draws);
    }

    /// <summary>
    /// The drawn arm's numbers: the team's sprite, the shimmer's half-alpha,
    /// g_aTeamColors (BLUE blue, RED red), and the billboard the wire's box
    /// says — the rig's 96×128 at the origin, half-spans 48 wide, 64 tall.
    /// </summary>
    [Fact]
    public void TheShimmerIsTheTeamsColourAtHalfAlphaAcrossTheBrushBox()
    {
        var blue = new Rig();
        blue.Fx.LocalPlayerAnswer = Local;
        var blueWall = blue.Drawn((byte)TFTeam.Blue, localTeam: (byte)TFTeam.Red);
        Assert.Equal(1, blueWall.OnDraw());
        var blueDraw = Assert.Single(blue.Fx.Draws);
        Assert.Equal(blue.Fx.Materials[Dot.SpriteBlue], blueDraw.Material);
        Assert.True(blueDraw.ByteColor);
        var blueCorner = blueDraw.Vertices[0];
        Assert.Equal(0f, blueCorner.R);
        Assert.Equal(0f, blueCorner.G);
        Assert.Equal(255f, blueCorner.B);
        Assert.Equal(128f, blueCorner.A);
        Assert.Equal((48f, 64f), blue.HalfSpans(blueDraw.Vertices));

        var red = new Rig();
        red.Fx.LocalPlayerAnswer = Local;
        var redWall = red.Drawn((byte)TFTeam.Red, localTeam: (byte)TFTeam.Blue);
        Assert.Equal(1, redWall.OnDraw());
        var redDraw = Assert.Single(red.Fx.Draws);
        Assert.Equal(red.Fx.Materials[Dot.SpriteRed], redDraw.Material);
        var redCorner = redDraw.Vertices[0];
        Assert.Equal(255f, redCorner.R);
        Assert.Equal(0f, redCorner.G);
        Assert.Equal(0f, redCorner.B);
        Assert.Equal(128f, redCorner.A);
    }

    /// <summary>The create's material is the draw's; taken off, it is released.</summary>
    [Fact]
    public void TheWallReleasesItsSpriteWhenItIsRemoved()
    {
        var rig = new Rig();
        rig.Fx.LocalPlayerAnswer = Local;
        var viz = rig.Drawn((byte)TFTeam.Blue, localTeam: (byte)TFTeam.Red);
        viz.OnDraw();
        int material = Assert.Single(rig.Fx.Draws).Material;

        viz.OnRemoved();

        Assert.Contains(material, rig.Fx.Released);
    }
}
