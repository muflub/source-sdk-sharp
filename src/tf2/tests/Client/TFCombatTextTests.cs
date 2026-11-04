extern alias client;

using SourceSharp.Abi;

using Xunit;

using Adapter = client::TF2.Client.Ui.TFCombatText;
using Args = client::SourceSharp.Shared.PlayerHurtArgs;
using Clock = client::SourceSharp.Abi.IGameClock;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using EventLink = client::TF2.Client.EventLink;
using HurtEvent = client::SourceSharp.Shared.PlayerHurtEvent;
using DeathEvent = client::SourceSharp.Shared.PlayerDeathEvent;
using StartTouch = client::SourceSharp.Shared.ControlPointStartTouchEvent;
using EndTouch = client::SourceSharp.Shared.ControlPointEndTouchEvent;
using WinPanel = client::SourceSharp.Shared.TeamplayWinPanelEvent;
using RoundStart = client::SourceSharp.Shared.TeamplayRoundStartEvent;
using Interface = client::SourceSharp.Client;
using Standalone = client::SourceSharp.Client.Standalone;
using TFClass = client::TF2.Shared.TFClass;
using TFCond = client::TF2.Shared.TFCond;
using TFTeam = client::TF2.Shared.TFTeam;
using TFPlayerData = client::TF2.Shared.Player.ITFPlayerData;
using TFPlayerNetState = client::TF2.Shared.Player.TFPlayerNetState;
using BeamExecutor = client::SourceSharp.Client.Interop.IClientBeamExecutor;
using FxRender = client::SourceSharp.Client.Interop.IFxRender;
using FxView = client::SourceSharp.Client.Interop.FxView;
using FxVertex = client::SourceSharp.Client.Interop.FxVertex;

// The contract assembly is unaliased: the record and the interface are
// global, as the damage history's are.

namespace TF2.Tests.Client;

/// <summary>
/// The combat text's read (stock's <c>CAccountPanel</c>, tf_hud_account.cpp):
/// a player_hurt whose attacker is the local player records one delta item —
/// the post-armour <c>damageamount</c>, its crit bit, the show time — under
/// the <c>hud_combattext</c> gate (:620), the self-damage skip (:546), the
/// stealthed-or-disguised enemy spy's (:550-566), the expired leaving and a
/// new level purging (<c>Reset</c>).
/// </summary>
public sealed class TFCombatTextTests
{
    private sealed class RigClock : Clock
    {
        public float Now { get; set; } = 10f;
    }

    /// <summary>The carriers as the client's prediction holds them: none
    /// decoded until the fact puts one there.</summary>
    private sealed class RigPlayers : TFPlayerData
    {
        public readonly Dictionary<int, TFPlayerNetState> Carriers = new();

        public TFPlayerNetState? Predicted(int playerIndex) => null;

        public TFPlayerNetState? Networked(int playerIndex) => Carriers.GetValueOrDefault(playerIndex);
    }

    /// <summary>The fx seam's reads: the main view the record's trace ends
    /// on, and the render's current view beneath it — two answers a fact
    /// tells apart.</summary>
    private sealed class RigFx : FxRender
    {
        public Vector MainViewOrigin() => new(4f, 5f, 6f);

        public FxView View() => new(new Vector(0f, 0f, -100f), new Vector(0f, 1f, 0f), 1280);

        public int MaterialFind(string name) => 0;

        public void MaterialAddRef(int material) { }

        public void MaterialRelease(int material) { }

        public string MaterialName(int material) => string.Empty;

        public void DrawQuads(int material, ReadOnlySpan<FxVertex> vertices, bool byteColor) { }

        public int LocalPlayer() => 2;

        public QAngle ViewAngles() => new(0f, 0f, 0f);

        public int EntityAttachment(int entIndex, int attachment, out Vector origin, out string model)
        {
            origin = default;
            model = string.Empty;
            return 0;
        }

        public void DispatchParticleEffect(int systemIndex, Vector start, Vector end, QAngle angles, int entIndex) { }

        public bool EntityRenderAngles(int entIndex, out QAngle angles)
        {
            angles = default;
            return false;
        }

        public void EjectBrass(Vector origin, QAngle angles, QAngle gunAngles, int type) { }
    }

    /// <summary>The projection, the one read the paint makes: the risen z
    /// comes back as x and the y as y, so a fact reads the anchor back.</summary>
    private sealed class RigBeams : BeamExecutor
    {
        public Vector ScreenPosition(in Vector p) => new(p.Z, p.Y, 0f);

        public void View(bool insideView, Span<float> out42) { }

        public bool ModelInfo(int modelIndex, out int frameCount)
        {
            frameCount = 0;
            return false;
        }

        public int ModelIndex(string name) => 0;

        public EntityHandle EntityAt(int entry) => EntityHandle.None;

        public bool EntityHasModel(EntityHandle entity) => false;

        public int EntityFacts(EntityHandle entity, int attachment, bool tryAttachment, Span<float> out9) => 0;

        public void BoxVisibility(in Vector mins, in Vector maxs, out bool visible, out bool culled)
        {
            visible = false;
            culled = false;
        }

        public int RenderableAdd(in Vector origin, in Vector mins, in Vector maxs) => 0;

        public void RenderableChanged(int renderable, in Vector origin, in Vector mins, in Vector maxs) { }

        public void RenderableRemove(int renderable) { }

        public bool BindSprite(int modelIndex, int frame, int renderMode, bool wireframe) => false;

        public void SetHdrColorScale(int modelIndex, int renderMode, float scale) { }

        public bool Glow(int modelIndex, bool bind) => false;

        public void Strip(ReadOnlySpan<float> vertices) { }

        public void Mesh(int primitive, int primitives, int colorKind, ReadOnlySpan<float> vertices) { }
    }

    private sealed class Rig
    {
        public readonly RigClock Clock = new();
        public readonly Engine Engine = new();
        public readonly Standalones St = new();
        public readonly Standalone.ClientPlayerResource Core;
        public readonly RigPlayers Players = new();
        public readonly RigBeams Beamer = new();
        public readonly RigFx Fx = new();
        public readonly HurtEvent Hurts = new();
        public readonly Adapter Source;

        public readonly client::TF2.Client.HudCombatTextConVar Enabled = DeclaredClientConVars.Holding<client::TF2.Client.HudCombatTextConVar>("1");

        public Rig(int localUserId = 7)
        {
            Engine.Returns("Hud.LocalPlayerIndex", 2);
            Engine.Returns("Hud.LocalPlayerOrigin", new Vector(0f, 0f, 0f));
            Engine.Returns("WorldQuery.ClientTraceLine",
                new ClientTraceResult(true, 1f, Vector.Zero, Vector.Zero, 0, 0, 0, 0));
            Engine.Returns("Effects.ClientEntityOrigin", true);
            Engine.Returns("Effects.ClientEntityOrigin.Origin", new Vector(100f, 200f, 0f));
            Core = new Standalone.ClientPlayerResource(new Engine());
            Core.State.BindLocal();
            Core.State.SetRow(2, 0, 0, 100, localUserId, (int)TFTeam.Red, connected: true, alive: true, valid: true);
            St.All.Add(Core);
            Source = new Adapter(Clock, Engine, St, Enabled, Players, Beamer, Engine, Engine, Fx,
                new EventLink(new DeathEvent(), Hurts, new StartTouch(), new EndTouch(),
                              new WinPanel(), new RoundStart()));
        }

        /// <summary>A connected row, and the carrier the client has decoded
        /// for it when there is one.</summary>
        public void Player(int index, int team, int userId, TFPlayerNetState? carrier = null)
        {
            Core.State.SetRow(index, 0, 0, 100, userId, team, connected: true, alive: true, valid: true);
            if (carrier is not null)
                Players.Carriers[index] = carrier;
        }

        /// <summary>One player_hurt the way the engine dispatches it.</summary>
        public void Hurt(int attacker, int victim = 9, int amount = 25, bool crit = false)
            => Hurts.Raise(new Args(victim, 75, attacker, amount, crit));
    }

    private sealed class Standalones : Interface.IClientStandalones
    {
        public readonly List<Standalone.ClientStandaloneEntity> All = [];

        public IReadOnlyList<Standalone.ClientStandaloneEntity> Live() => All;

        public Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    [Fact]
    public void A_hurt_by_the_local_player_records_one_number()
    {
        var r = new Rig();
        r.Hurt(7);

        var n = Assert.Single(Enumerable.Range(0, r.Source.Count).Select(r.Source.Number));
        Assert.Equal((25, false, 10f), (n.Amount, n.Crit, n.At));
    }

    [Fact]
    public void The_crit_bit_rides_the_number()
    {
        var r = new Rig();
        r.Hurt(7, crit: true);

        Assert.True(r.Source.Number(0).Crit);
    }

    [Fact]
    public void A_hurt_by_someone_else_records_nothing()
    {
        var r = new Rig();
        r.Hurt(9);       // the victim is 9: they hurt themselves by our own wiring's numbers
        r.Hurt(3);

        Assert.Equal(0, r.Source.Count);
    }

    [Fact]
    public void The_self_damage_skip_holds()
    {
        var r = new Rig();
        r.Hurt(7, victim: 7);

        Assert.Equal(0, r.Source.Count);
    }

    [Fact]
    public void CombattextOffRecordsNothing()
    {
        var r = new Rig();
        DeclaredClientConVars.Hold(r.Enabled, "0");

        r.Hurt(7);

        Assert.Equal(0, r.Source.Count);
    }

    [Fact]
    public void The_expired_leave_and_a_new_level_purge()
    {
        var r = new Rig();
        r.Hurt(7);
        r.Clock.Now = 11f;
        r.Hurt(7, amount: 40);
        Assert.Equal(2, r.Source.Count);

        r.Clock.Now = 11f + Adapter.DeltaLifetime + 0.1f;
        Assert.Equal(0, r.Source.Count);      // both past the delta lifetime

        r.Hurt(7);
        r.Clock.Now = 5f;                     // the clock going back: a new level
        Assert.Equal(0, r.Source.Count);
    }

    [Fact]
    public void The_oldest_makes_room_past_the_cap()
    {
        var r = new Rig();
        for (int i = 0; i < Adapter.MaxTexts + 1; i++)
        {
            r.Hurt(7, amount: 10 + i);
            r.Clock.Now += 0.05f;
        }

        Assert.Equal(Adapter.MaxTexts, r.Source.Count);
        Assert.Equal(11, r.Source.Number(0).Amount);      // the first left, not the last
    }

    /// <summary>An enemy spy's carrier, bound and dressed: the team its
    /// disguise shows, cloaked when the fact says so.</summary>
    private static TFPlayerNetState SpyCarrier(int disguiseTeam, bool stealthed = false)
    {
        var spy = new TFPlayerNetState();
        spy.BindLocal();
        spy.PlayerClass = (int)TFClass.Spy;
        spy.DisguiseTeam = disguiseTeam;
        if (stealthed)
            spy.PlayerCond = 1 << (int)TFCond.Stealthed;
        return spy;
    }

    [Fact]
    public void An_enemy_spy_disguised_as_our_team_hides_their_damage()
    {
        var r = new Rig();
        r.Player(3, (int)TFTeam.Blue, 9, SpyCarrier((int)TFTeam.Red));

        r.Hurt(7, victim: 9);

        Assert.Equal(0, r.Source.Count);
    }

    [Fact]
    public void An_enemy_spy_cloaked_hides_their_damage()
    {
        var r = new Rig();
        r.Player(3, (int)TFTeam.Blue, 9, SpyCarrier((int)TFTeam.Blue, stealthed: true));

        r.Hurt(7, victim: 9);

        Assert.Equal(0, r.Source.Count);
    }

    [Fact]
    public void An_enemy_spy_in_the_open_shows_their_damage()
    {
        var r = new Rig();
        r.Player(3, (int)TFTeam.Blue, 9, SpyCarrier((int)TFTeam.Blue));

        r.Hurt(7, victim: 9);

        var n = Assert.Single(Enumerable.Range(0, r.Source.Count).Select(r.Source.Number));
        Assert.Equal(25, n.Amount);
    }

    [Fact]
    public void A_teammate_spy_shows_their_damage_even_hidden()
    {
        var r = new Rig();
        r.Player(3, (int)TFTeam.Red, 9, SpyCarrier((int)TFTeam.Red, stealthed: true));

        r.Hurt(7, victim: 9);

        Assert.Equal(1, r.Source.Count);
    }

    [Fact]
    public void A_connected_enemy_without_a_carrier_shows_their_damage()
    {
        var r = new Rig();
        r.Player(3, (int)TFTeam.Blue, 9);

        r.Hurt(7, victim: 9);

        Assert.Equal(1, r.Source.Count);
    }

    [Fact]
    public void An_occluded_victim_records_nothing()
    {
        var r = new Rig();
        r.Player(3, (int)TFTeam.Blue, 9);
        r.Engine.Returns("WorldQuery.ClientTraceLine",
            new ClientTraceResult(true, 0.5f, Vector.Zero, Vector.Zero, 0, 0, 0, 0));

        r.Hurt(7, victim: 9);

        Assert.Equal(0, r.Source.Count);
    }

    [Fact]
    public void The_anchor_is_the_crown_and_rises_with_the_life()
    {
        var r = new Rig();
        r.Player(3, (int)TFTeam.Blue, 9);

        r.Hurt(7, victim: 9);

        Assert.Equal(1, r.Source.Count);

        // The origin (100, 200, 0), the local player at the world's zero: the
        // distance² 50000 clamps the offset at 16, the crown is the hull's 82
        // over the origin, and the rise is the lifetime's share of 32. The
        // stand-in answers the z as x and the y as y.
        Assert.True(r.Source.Screen(0, 0f, out var atBirth));
        Assert.Equal((98f, 200f), (atBirth.X, atBirth.Y));

        Assert.True(r.Source.Screen(0, 1f, out var atDeath));
        Assert.Equal((130f, 200f), (atDeath.X, atDeath.Y));
    }

    [Fact]
    public void The_occlusion_trace_ends_at_the_main_view()
    {
        // The hurt arrives outside the render's access window: the trace's
        // end is the main view's origin (stock's tf_hud_account.cpp:624),
        // the fx seam's, not the render's current view the window guards.
        var r = new Rig();
        r.Player(3, (int)TFTeam.Blue, 9);

        r.Hurt(7, victim: 9);

        var trace = r.Engine.Last("WorldQuery.ClientTraceLine")!.Value;
        Assert.Equal(new Vector(4f, 5f, 6f), trace.ArgAs<Vector>(1));
    }

    [Fact]
    public void A_victim_without_a_row_records_but_projects_nothing()
    {
        var r = new Rig();
        r.Hurt(7);       // the victim has no row: no centre to trace

        Assert.Equal(1, r.Source.Count);
        Assert.False(r.Source.Screen(0, 0f, out _));
    }
}
