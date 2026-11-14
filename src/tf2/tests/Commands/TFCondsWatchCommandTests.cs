using System.Runtime.CompilerServices;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Commands;
using TF2.Shared;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Commands;

/// <summary>
/// <see cref="TFCondsWatchCommand"/> (<c>tf2_conds_watch &lt;player&gt;
/// &lt;seconds&gt;</c>) over its <see cref="TFCondsWatch"/> holder: the
/// <see cref="TFCondsCommand"/> line once a tick until the seconds are up, and
/// the watch stopped at the tick the watched player is no longer alive —
/// behind sv_cheats like the bare command.
/// </summary>
public sealed class TFCondsWatchCommandTests
{
    private static readonly EntityHandle Player = new(0x0001_0000u | 3);

    private readonly RecordingLog _log = new();
    private readonly TFCondsWatch _watch = new();

    /// <summary>The engine double with the named answers the line is made of; the alive word scripted per call.</summary>
    private RecordingEngine Engine(params bool[] alive)
    {
        var engine = new RecordingEngine();
        engine.Returns("Players.GetHandle", Player);
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Players.GetTeam", 3);
        engine.Returns("Entities.GetHealth", 125);
        engine.Returns("Entities.GetOrigin", new Vector(-1200f, 600f, 400f));
        engine.Returns("Entities.GetVelocity", new Vector(0f, 0f, -150f));
        engine.Returns("Players.GetActiveWeapon", "tf_weapon_pistol");
        int asked = 0;
        engine.ReturnsEachCall("Players.IsAlive", () => asked < alive.Length ? alive[asked++] : true);
        return engine;
    }

    private sealed class SpyState : ITFPlayerData
    {
        private readonly TFPlayerNetState _state = Spy();

        private static TFPlayerNetState Spy()
        {
            var state = new TFPlayerNetState();
            state.BindLocal();
            state.PlayerClass = (int)TFClass.Spy;
            state.CloakMeter = 87f;
            state.ChargeMeter = 42f;
            state.PlayerCond = 1 << (int)TFCond.Stealthed;
            return state;
        }

        public TFPlayerNetState? Predicted(int playerIndex) => _state;
        public TFPlayerNetState? Networked(int playerIndex) => _state;
    }

    private TFCondsWatchCommand Command(RecordingEngine engine, string cheats = "1")
        => new(_log, DeclaredConVars.Holding<SourceSharp.Shared.SvCheatsConVar>(cheats),
               engine, engine, new SpyState(), _watch);

    private int Printed() => _log.Lines.Count(l => !l.Warning);

    [Fact]
    public void Without_sv_cheats_the_watch_is_refused_and_the_holder_stays_idle()
    {
        Command(Engine(true), cheats: "0").RunLine("3 2");

        Assert.Contains(_log.Lines, l => l.Warning && l.Text.Contains("sv_cheats"));
        _watch.OnThink(1f);
        Assert.Equal(0, Printed());

        // The differential on the same rig with cheats on: the watch ticks
        // and the line arrives, so the silence above is the gate.
        var on = new RecordingEngine();
        on.Returns("Players.GetHandle", Player);
        on.Returns("Entities.IsValid", true);
        on.Returns("Players.GetTeam", 3);
        on.Returns("Entities.GetHealth", 125);
        on.Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f));
        on.Returns("Entities.GetVelocity", new Vector(0f, 0f, 0f));
        on.Returns("Players.GetActiveWeapon", "tf_weapon_pistol");
        on.Returns("Players.IsAlive", true);
        Command(on).RunLine("3 2");
        _watch.OnThink(1f);
        Assert.Equal(1, Printed());
    }

    [Fact]
    public void The_watch_prints_the_line_once_a_tick_until_its_seconds_are_up()
    {
        var command = Command(Engine(true, true, true));

        // Idle before the line runs: an empty holder answers nothing.
        _watch.OnThink(1f);
        Assert.Equal(0, Printed());

        command.RunLine("3 2");
        _watch.OnThink(1f);
        Assert.Equal(1, Printed());
        _watch.OnThink(1f);
        Assert.Equal(2, Printed());
        _watch.OnThink(1f);   // the two seconds are spent: the watch is off
        Assert.Equal(2, Printed());

        Assert.Equal(
            "[TF2/Conds] 3 Spy team 3 hp 125 at -1200.0 600.0 400.0 vz -150 " +
            "weapon tf_weapon_pistol conds TF_COND_STEALTHED cloak 87 charge 42\n",
            _log.Lines[^1].Text);
    }

    /// <summary>
    /// The shape <see cref="TF2LifecycleOrderTests"/> holds for the level and
    /// hook tiers, at the frame tier the live boot caught: two ticking holders
    /// at the default 0 tie and StageUniqueness refuses the whole tf2 load.
    /// </summary>
    [Fact]
    public void The_watch_holder_takes_a_stage_of_its_own_in_TF2s_range()
    {
        var ticking = typeof(TF2.Server.TFClassTableLoader).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IFrameLifecycle).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttributes(typeof(SideServiceAttribute), false).Length > 0)
            .Select(t => (t.Name, Order: ((IFrameLifecycle)RuntimeHelpers.GetUninitializedObject(t)).FrameOrder))
            .ToList();

        Assert.True(ticking.Count >= 3, $"only {ticking.Count} ticking holders: the scan is not reading the assembly");
        Assert.Contains(("TFCondsWatch", TFCondsWatch.Order), ticking);
        Assert.InRange(TFCondsWatch.Order, 1_000_000 - 1_000, 1_000_000 - 1);
        Assert.All(ticking.GroupBy(o => o.Order), g => Assert.Single(g));
    }

    [Fact]
    public void The_watch_stops_at_the_tick_the_watched_player_is_no_longer_alive()
    {
        // Ten seconds of window, the death after the second tick: two lines,
        // not the ten the window would have run — the stop is the death, not
        // the clock.
        Command(Engine(true, false)).RunLine("3 10");

        _watch.OnThink(1f);
        _watch.OnThink(1f);
        _watch.OnThink(1f);

        Assert.Equal(2, Printed());
    }
}
