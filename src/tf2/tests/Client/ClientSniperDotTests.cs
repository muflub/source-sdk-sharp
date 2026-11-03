extern alias client;

using SourceSharp.Abi;
using SourceSharp.Tests;

using Xunit;

using ClientSide = client::SourceSharp.Client;
using CI = client::SourceSharp.Client.Interop;
using Standalone = client::SourceSharp.Client.Standalone;
using WeaponFx = client::SourceSharp.Client.WeaponEffects;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Mirror = client::TF2.Client.Entities.ClientSniperDot;
using TFCond = client::TF2.Shared.TFCond;
using TFCondMask = client::TF2.Shared.Conditions.TFCondMask;
using TFPlayer = client::TF2.Shared.Player;
using TFTeam = client::TF2.Shared.TFTeam;

namespace TF2.Tests.Client;

/// <summary>
/// The sniper dot's client draw (<see cref="Mirror"/>): the team's sprite at
/// the create, the local re-trace against the networked origin, the two
/// sprites' sizes and the strength between them, and the third-person
/// silence — each against the stock lines it ports
/// (<c>shared/tf/tf_weapon_sniperrifle.cpp:1490-1718</c>). The wire round
/// trip is the server state's own snapshot: the shape the game runs.
/// </summary>
public sealed class ClientSniperDotTests
{
    private const int Local = 2;

    private static readonly EntityHandle Dot = new(0x0004_0021u);

    // A view 100 up looking down -Z, whose axes the quad builder borrows: the
    // corners come out axis-aligned, x and y about the drawn point.
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

    private sealed class ThirdPerson : WeaponFx.IThirdPersonCamera
    {
        public bool Answer;

        public bool IsThirdPerson => Answer;
    }

    private sealed class Standalones : ClientSide.IClientStandalones
    {
        public readonly Dictionary<int, Standalone.ClientStandaloneEntity> ByIndex = [];

        public IReadOnlyList<Standalone.ClientStandaloneEntity> Live() => [.. ByIndex.Values];

        public Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => ByIndex.GetValueOrDefault(entityIndex);

        public IEnumerable<Standalone.ClientStandaloneEntity> OfClass(string? className)
            => [.. ByIndex.Values.Where(e => e.ClassName == className)];

        public IEnumerable<Standalone.ClientStandaloneEntity> OfName(string? name) => [];
    }

    /// <summary>The replay: nobody's rifle is predicted unless held here.</summary>
    private sealed class DrawStates : WeaponFx.IWeaponDrawStates
    {
        public readonly Dictionary<Standalone.ClientSsWeapon, client::SourceSharp.Shared.WeaponNetState?> Held = [];

        public client::SourceSharp.Shared.WeaponNetState? PredictedStateOf(Standalone.ClientSsWeapon weapon)
            => Held.GetValueOrDefault(weapon);
    }

    private sealed class PlayerData : TFPlayer.ITFPlayerData
    {
        public readonly Dictionary<int, TFPlayer.TFPlayerNetState> Players = [];

        public TFPlayer.TFPlayerNetState? Predicted(int playerIndex) => Players.GetValueOrDefault(playerIndex);

        public TFPlayer.TFPlayerNetState? Networked(int playerIndex) => Players.GetValueOrDefault(playerIndex);
    }

    /// <summary>One TF2 row, the way the mod's source appends it.</summary>
    private sealed class TestTypes : client::SourceSharp.Shared.IWeaponTypeSource
    {
        public IEnumerable<client::SourceSharp.Shared.WeaponType> Rows()
        {
            yield return new("tf_weapon_sniperrifle", 0.5f);
        }
    }

    /// <summary>A player carrier holding exactly <paramref name="conds"/>.</summary>
    private static TFPlayer.TFPlayerNetState Carrier(params TFCond[] conds)
    {
        var state = new TFPlayer.TFPlayerNetState();
        state.BindLocal();
        TFCondMask mask = default;
        foreach (TFCond cond in conds)
            mask |= TFCondMask.Of(cond);
        TFPlayer.TFPlayerNetStateViews.SetConditions(state, mask);
        return state;
    }

    private sealed class Rig
    {
        public readonly Engine Engine = new();
        public readonly FakeFx Fx = new();
        public readonly FakeView View;
        public readonly ThirdPerson Camera = new();
        public readonly Standalones Standalones = new();
        public readonly DrawStates States = new();
        public readonly PlayerData Players = new();
        public readonly client::SourceSharp.Shared.WeaponCatalog Weapons = new(() => [new TestTypes()]);

        public Rig()
        {
            View = new(TheView);
            Engine.Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f));
            Engine.Returns("Clock.Now", 10f);
        }

        /// <summary>A dot the create has run for: material picked, draw asked for.</summary>
        public Mirror Drawn(byte team = (byte)TFTeam.Red, int owner = 0, float chargeStart = 8.5f)
        {
            Mirror dot = Bare(team, owner, chargeStart);
            dot.OnDataChanged(default, DataUpdateType.Created);
            return dot;
        }

        public Mirror Bare(byte team = (byte)TFTeam.Red, int owner = 0, float chargeStart = 8.5f)
        {
            var dot = new Mirror(Engine, Fx, View, Engine, Camera, Engine, Standalones, Weapons, States, Players);
            dot.Handle = Dot;
            dot.State.BindLocal();
            dot.State.Team = team;
            dot.State.OwnerIndex = owner;
            dot.State.ChargeStartTime = chargeStart;
            dot.BindIndex(9);
            dot.BindEngine(Engine, Engine);
            return dot;
        }

        /// <summary>The local arm's trace stops at <paramref name="end"/>.</summary>
        public void TraceEnds(Vector end)
            => Engine.Returns("WorldQuery.ClientTraceLine",
                              new ClientTraceResult(true, 0.5f, end, new Vector(0f, 0f, 1f), 0, 0, 0, 0));

        /// <summary>Where the server's last think put this dot.</summary>
        public void Origin(Vector origin) => Engine.Returns("Entities.GetOrigin", origin);

        /// <summary>The widest x/y offset from <paramref name="centre"/> the
        /// four vertices from <paramref name="at"/> reach — the quad's half size.</summary>
        private static float HalfSpan(CI.FxVertex[] vertices, int at, Vector centre)
        {
            float half = 0f;
            foreach (var v in vertices[at..(at + 4)])
                half = MathF.Max(half, MathF.Max(MathF.Abs(v.Position.X - centre.X), MathF.Abs(v.Position.Y - centre.Y)));
            return half;
        }

        /// <summary>The single draw's (outer, inner) half sizes about <paramref name="centre"/>.</summary>
        public (float Outer, float Inner) Halves(Vector centre)
        {
            var draw = Assert.Single(Fx.Draws);
            Assert.Equal(8, draw.Vertices.Length);
            return (HalfSpan(draw.Vertices, 0, centre), HalfSpan(draw.Vertices, 4, centre));
        }
    }

    // ---- the create ----------------------------------------------------------

    [Fact]
    public void The_create_asks_for_the_teams_sprite()
    {
        var blue = new Rig();
        blue.Drawn(team: (byte)TFTeam.Blue).OnDraw();
        Assert.Equal(blue.Fx.Materials["effects/sniperdot_blue.vmt"], Assert.Single(blue.Fx.Draws).Material);

        // The else arm: red, for RED and for any team that is not blue
        // (:1705-1712).
        var red = new Rig();
        red.Drawn(team: (byte)TFTeam.Red).OnDraw();
        Assert.Equal(red.Fx.Materials["effects/sniperdot_red.vmt"], Assert.Single(red.Fx.Draws).Material);
    }

    [Fact]
    public void The_draw_is_the_translucent_pass_inside_a_generous_box()
    {
        var rig = new Rig();
        rig.Drawn();

        var blend = rig.Engine.Last("EntityRender.SetBlending");
        Assert.NotNull(blend);
        Assert.True(blend!.Value.ArgAs<bool>(1));
        Assert.Equal(255, blend.Value.ArgAs<int>(3));

        var box = Assert.Single(rig.Engine.Calls, c => c.Member == "EntityRender.SetCustomDraw");
        Assert.Equal(new Vector(-64f, -64f, -64f), box.ArgAs<Vector>(1));
        Assert.Equal(new Vector(64f, 64f, 64f), box.ArgAs<Vector>(2));
    }

    [Fact]
    public void The_removal_releases_the_sprite_once()
    {
        var rig = new Rig();
        var dot = rig.Drawn();
        dot.OnDraw();
        int material = Assert.Single(rig.Fx.Draws).Material;

        dot.OnRemoved();
        dot.OnRemoved();

        Assert.Equal([material], rig.Fx.Released);
    }

    // ---- the local arm -------------------------------------------------------

    [Fact]
    public void The_local_dot_draws_at_the_aim_trace_backed_off_the_wall()
    {
        var rig = new Rig();
        rig.Fx.LocalPlayerAnswer = Local;
        rig.TraceEnds(new Vector(0f, 0f, -40f));
        rig.Drawn(owner: Local).OnDraw();

        // The trace ended 40 down; backed off four along the view's forward
        // the point is 36 down (:1553). The outer sprite's corners are one
        // out along each view axis (two across, :1498, :1520); the inner is
        // the fallback strength: a charge 1.5 s old of the 3 s the base rate
        // needs is 0.55 (:1635).
        Vector centre = new(0f, 0f, -36f);
        var draw = Assert.Single(rig.Fx.Draws);
        Assert.Equal(new Vector(-1f, -1f, -36f), draw.Vertices[0].Position);
        Assert.Equal(new Vector(-1f, 1f, -36f), draw.Vertices[1].Position);
        Assert.Equal(new Vector(1f, 1f, -36f), draw.Vertices[2].Position);
        Assert.Equal(new Vector(1f, -1f, -36f), draw.Vertices[3].Position);

        var (outer, inner) = rig.Halves(centre);
        Assert.Equal(1f, outer, 4);
        Assert.Equal(0.55f, inner, 4);
    }

    [Fact]
    public void The_trace_is_the_views_own_line_past_the_owner()
    {
        var rig = new Rig();
        rig.Fx.LocalPlayerAnswer = Local;
        rig.TraceEnds(new Vector(0f, 0f, -40f));
        rig.Drawn(owner: Local).OnDraw();

        var trace = rig.Engine.Calls.Single(c => c.Member == "WorldQuery.ClientTraceLine");
        Assert.Equal(new Vector(0f, 0f, 100f), trace.ArgAs<Vector>(0));
        // c_fMaxSizeDistVR 384 along the view's forward (:1509, :1519).
        Assert.Equal(new Vector(0f, 0f, 100f - 384f), trace.ArgAs<Vector>(1));
        Assert.Equal(TraceMask.Shot, trace.ArgAs<TraceMask>(2));
        Assert.Equal(Local, trace.ArgAs<int>(4));
    }

    [Fact]
    public void The_aiming_unzoomed_dot_is_four_across()
    {
        var rig = new Rig();
        rig.Fx.LocalPlayerAnswer = Local;
        rig.Players.Players[Local] = Carrier(TFCond.Aiming);
        rig.TraceEnds(new Vector(0f, 0f, -40f));
        rig.Drawn(owner: Local).OnDraw();

        // Four across (The Classic's wide dot, :1523-1527); 36 down is inside
        // the 200 the growth starts past, so the size stands.
        var (outer, inner) = rig.Halves(new Vector(0f, 0f, -36f));
        Assert.Equal(2f, outer, 4);
        Assert.Equal(2f * 0.55f, inner, 4);
    }

    [Fact]
    public void The_wide_dot_grows_with_its_distance()
    {
        var rig = new Rig();
        rig.Fx.LocalPlayerAnswer = Local;
        rig.Players.Players[Local] = Carrier(TFCond.Aiming);
        rig.TraceEnds(new Vector(0f, 0f, -300f));
        rig.Drawn(owner: Local).OnDraw();

        // Backed off to 296 down, 396 from the eye: four grown by 396/200
        // (:1563-1571), half 3.96.
        var (outer, _) = rig.Halves(new Vector(0f, 0f, -296f));
        Assert.Equal(3.96f, outer, 3);
    }

    [Fact]
    public void Another_players_dot_sits_where_the_server_put_it()
    {
        var rig = new Rig();
        rig.Origin(new Vector(3f, 4f, 5f));
        rig.Drawn(owner: 5).OnDraw();

        // No other player's eye pitch (the class doc): no trace, the networked
        // origin, six across (stock's dormant arm, :1573-1578, :1498). The
        // sprite faces the eye here, so its extents tilt a hair off the axes:
        // the centre is exact, the sizes to two places.
        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "WorldQuery.ClientTraceLine");
        var draw = Assert.Single(rig.Fx.Draws);
        Vector centre = default;
        foreach (var v in draw.Vertices[..4])
            centre += v.Position;
        centre = centre * (1f / 4f);
        Assert.Equal(new Vector(3f, 4f, 5f), centre);

        var (outer, inner) = rig.Halves(new Vector(3f, 4f, 5f));
        Assert.Equal(3f, outer, 2);
        Assert.Equal(3f * 0.55f, inner, 2);
    }

    // ---- the strength --------------------------------------------------------

    [Fact]
    public void The_strength_is_the_carried_rifles_charge()
    {
        var rig = new Rig();
        rig.Fx.LocalPlayerAnswer = Local;
        rig.TraceEnds(new Vector(0f, 0f, -40f));

        var rifle = new Standalone.ClientSsWeapon(new Engine());
        rifle.State.BindLocal();
        rifle.State.OwnerIndex = Local;
        rifle.State.WeaponTypeId = rig.Weapons.IndexOf(Mirror.RifleName);
        rifle.State.BehaviourMeter = 9f;
        rig.Standalones.ByIndex[15] = rifle;

        rig.Drawn(owner: Local).OnDraw();

        // A charge begun at 9 read at 10: 50 of the 150, the inner sprite a
        // third of the outer (GetHUDDamagePerc, :1309-1312, :1625-1632).
        var (outer, inner) = rig.Halves(new Vector(0f, 0f, -36f));
        Assert.Equal(1f, outer, 4);
        Assert.Equal(1f / 3f, inner, 3);
    }

    [Fact]
    public void The_rage_buff_shortens_the_fallbacks_charge_time()
    {
        var calm = new Rig();
        calm.Fx.LocalPlayerAnswer = Local;
        calm.TraceEnds(new Vector(0f, 0f, -40f));
        calm.Drawn(owner: Local, chargeStart: 8.8f).OnDraw();
        var (_, calmInner) = calm.Halves(new Vector(0f, 0f, -36f));
        Assert.Equal(0.46f, calmInner, 3);

        // The buffed rate 62.5 halves the fallback's charge time to 2.4 s: the
        // same 1.2 s old dot is half charged, 0.55, where the calm 3 s rate
        // gives 0.5 (:1610-1621, :1635).
        var rage = new Rig();
        rage.Fx.LocalPlayerAnswer = Local;
        rage.Players.Players[Local] = Carrier(TFCond.SniperchargeRageBuff);
        rage.TraceEnds(new Vector(0f, 0f, -40f));
        rage.Drawn(owner: Local, chargeStart: 8.8f).OnDraw();
        var (_, rageInner) = rage.Halves(new Vector(0f, 0f, -36f));
        Assert.Equal(0.55f, rageInner, 3);
    }

    // ---- the camera and the wire --------------------------------------------

    [Fact]
    public void Third_person_silences_the_dot()
    {
        var rig = new Rig();
        rig.Camera.Answer = true;
        rig.Drawn(owner: Local).OnDraw();

        // ShouldDraw's third-person arm: no sprites at all (:1656-1658).
        Assert.Empty(rig.Fx.Draws);
    }

    [Fact]
    public void The_servers_wire_picks_the_teams_sprite_here()
    {
        // The server half publishes the owner's team over the wire it binds;
        // the snapshot is the bytes the game moves.
        var serverEngine = TestServices.Fake();
        var server = new TF2.Server.Entities.SniperDot(serverEngine, serverEngine);
        server.Handle = Dot;
        server.BindAreas(TestServices.Areas(serverEngine));
        server.NetStates = NetStateBinding.AttachLocal(server, Dot, new NetStateFields(new RecordingLog()));
        serverEngine.Returns("Entities.GetIndex", 4)
                    .Returns("Players.GetTeam", (int)TF2.Shared.TFTeam.Blue);
        server.Begin(new EntityHandle(0x0001_0003u), 3.5f);

        var rig = new Rig();
        var mirror = rig.Bare();
        byte[] wire = server.State.Snapshot();
        mirror.State.Decode(wire);
        mirror.OnDataChanged(wire, DataUpdateType.Created);
        mirror.OnDraw();

        // The blue sprite, from the team the wire carried (:1705-1708); the
        // charge clock rode beside it and the fallback reads it.
        Assert.Equal(rig.Fx.Materials["effects/sniperdot_blue.vmt"], Assert.Single(rig.Fx.Draws).Material);
        Assert.Equal(3.5f, mirror.State.ChargeStartTime);
    }
}
