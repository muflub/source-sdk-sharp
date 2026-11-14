extern alias client;

using SourceSharp.Abi;

using Xunit;

using CI = client::SourceSharp.Client.Interop;
using Standalone = client::SourceSharp.Client.Standalone;
using Abi = client::SourceSharp.Abi;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Mirror = client::SourceSharp.Client.Standalone.ClientLaserDot;

namespace TF2.Tests.Client;

/// <summary>
/// The laser dot's client half (<see cref="Mirror"/>): the one sprite, bound at
/// the create from the holder's team byte — stock's two team textures for a
/// TF2 holder (<c>tf_weapon_laser_pointer.cpp:41-42</c>, the arms at :256), the
/// plain RPG sprite for a holder with no team byte at all (every RPG dot, the
/// hl2mp arm's own stock).
/// </summary>
public sealed class ClientLaserDotTests
{
    private static readonly Engine TheEngine = new();

    private sealed class FakeFx : CI.IFxRender
    {
        public readonly List<(int Material, CI.FxVertex[] Vertices, bool ByteColor)> Draws = [];
        public readonly Dictionary<string, int> Materials = [];

        public int MaterialFind(string name)
        {
            if (!Materials.TryGetValue(name, out int id))
                id = Materials[name] = Materials.Count + 1;
            return id;
        }

        public void MaterialRelease(int material) { }

        public void MaterialAddRef(int material) { }

        public string MaterialName(int material) => "";

        public void DrawQuads(int material, ReadOnlySpan<CI.FxVertex> vertices, bool byteColor)
            => Draws.Add((material, vertices.ToArray(), byteColor));

        public CI.FxView View() => default;

        public Vector MainViewOrigin() => default;

        public int LocalPlayer() => 0;

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

    // A view straight down -Z whose axes the quad builder borrows; the plain
    // single sprite lives on this and nothing else here is asserted.
    private static readonly CI.CurrentViewState TheView =
        new(0, new Vector(0f, 0f, 100f), new Vector(0f, 0f, -1f), new Vector(1f, 0f, 0f), new Vector(0f, 1f, 0f));

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

    /// <summary>Deterministic flutter: always the floor of the range.</summary>
    private sealed class FakeRandom : Abi.IUniformRandom
    {
        public float RandomFloat(float min, float max) => min;
        public int RandomInt(int min, int max) => min;
    }

    /// <summary>A dot the create has run for: material bound, draw asked for.</summary>
    private static (FakeFx Fx, Mirror Dot) Drawn(byte team)
    {
        var engine = new Engine();
        var fx = new FakeFx();
        var view = new FakeView(TheView);

        engine.Returns("Entities.GetOrigin", Vector.Zero);

        var dot = new Mirror(engine, fx, view, engine, new FakeRandom());
        dot.Handle = new EntityHandle(0x0004_0021u);
        dot.State.BindLocal();
        dot.State.Team = team;
        dot.BindIndex(9);
        dot.BindEngine(engine, engine);
        dot.OnDataChanged(default, DataUpdateType.Created);
        dot.OnDraw();
        return (fx, dot);
    }

    [Fact]
    public void The_create_binds_the_holders_team_sprite()
    {
        // The blue sprite for a BLUE holder (team 3) and the red sprite for
        // any other team (:41-42's two names, the ChangeTeam arms at :256).
        var blue = Drawn(team: 3);
        Assert.Equal(blue.Fx.Materials["effects/sniperdot_blue.vmt"], Assert.Single(blue.Fx.Draws).Material);

        var red = Drawn(team: 2);
        Assert.Equal(red.Fx.Materials["effects/sniperdot_red.vmt"], Assert.Single(red.Fx.Draws).Material);
    }

    [Fact]
    public void The_rpg_dot_keeps_the_plain_sprite()
    {
        // No team byte at all — the RPG's own arm, hl2mp stock's
        // RPG_LASER_SPRITE — keeps the plain sprite (:49's name).
        var plain = Drawn(team: 0);
        Assert.Equal(plain.Fx.Materials["sprites/redglow1"], Assert.Single(plain.Fx.Draws).Material);
    }
}
