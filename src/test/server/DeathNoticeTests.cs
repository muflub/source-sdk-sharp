using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// A kill fires exactly one player_death, as stock's CHL2MPRules::DeathNotice:
/// userid (the victim), attacker (the scorer's userid, 0 for none), weapon
/// (the scorer's active weapon when the scorer inflicted it, else the
/// inflictor's classname, with weapon_/npc_/func_ stripped, "physics" for
/// anything physics, "world" for nothing) and priority 7. Through the
/// engine's events, so the managed subscribers -- the rules' scoreboard --
/// hear it from the engine's dispatch like any other event.
/// </summary>
public class DeathNoticeTests
{
    private static readonly EntityHandle Victim = new(0x010001);
    private static readonly EntityHandle Killer = new(0x010002);
    private static readonly EntityHandle Prop = new(0x020009);

    /// <summary>
    /// The engine's event manager in miniature: created events hold their
    /// fields, a fire hands the event to the registry by name (the engine's
    /// listener dispatch), and the reads answer what was set.
    /// </summary>
    private sealed class LoopbackEvents : IEngineGameEvents
    {
        private readonly Dictionary<IntPtr, (string Name, Dictionary<string, object> Fields)> _live = new();
        private int _next = 1;
        public GameEventRegistry? Registry;
        public readonly List<(string Name, Dictionary<string, object> Fields)> Fired = new();

        public IntPtr EventCreate(string name)
        {
            var h = new IntPtr(_next++);
            _live[h] = (name, new Dictionary<string, object>());
            return h;
        }
        public void EventSetInt(IntPtr h, string key, int value) => _live[h].Fields[key] = value;
        public void EventSetFloat(IntPtr h, string key, float value) => _live[h].Fields[key] = value;
        public void EventSetBool(IntPtr h, string key, bool value) => _live[h].Fields[key] = value;
        public void EventSetString(IntPtr h, string key, string value) => _live[h].Fields[key] = value;
        public bool EventFire(IntPtr h)
        {
            var e = _live[h];
            Fired.Add(e);
            Registry?.Dispatch(e.Name, h);
            _live.Remove(h);
            return true;
        }
        public void EventFree(IntPtr h) => _live.Remove(h);
        public int EventGetInt(IntPtr h, string key) => _live[h].Fields.TryGetValue(key, out var v) && v is int i ? i : 0;
        public float EventGetFloat(IntPtr h, string key) => _live[h].Fields.TryGetValue(key, out var v) && v is float f ? f : 0f;
        public bool EventGetBool(IntPtr h, string key) => _live[h].Fields.TryGetValue(key, out var v) && v is bool b && b;
        public string EventGetString(IntPtr h, string key) => _live[h].Fields.TryGetValue(key, out var v) && v is string s ? s : "";
        public void ListenForGameEvent(string name) { }
    }

    private static (DeathNotice Notice, LoopbackEvents Events, DamageInflictors Inflictors) Rig(string killerWeapon = "weapon_pistol")
    {
        var engine = Fake();
        engine.ReturnsEachCall("Players.GetUserId", () => engine.Calls[^1].Args[0] is EntityHandle h ? (h == Victim ? 7 : h == Killer ? 9 : 0) : 0);
        engine.ReturnsEachCall("Entities.IsPlayer", () => engine.Calls[^1].Args[0] is EntityHandle h && (h == Victim || h == Killer));
        engine.ReturnsEachCall("Entities.GetClassName", () => engine.Calls[^1].Args[0] is EntityHandle h
            ? (h == Prop ? "prop_physics" : h.EntryIndex == 0 ? "worldspawn" : "ss_player") : "");
        engine.Returns("Players.GetActiveWeapon", killerWeapon).Returns("Entities.IsValid", true);
        var events = new LoopbackEvents();
        var inflictors = new DamageInflictors();
        return (new DeathNotice(events, engine, engine, inflictors), events, inflictors);
    }

    private static Dictionary<string, object> Only(LoopbackEvents events)
    {
        var e = Assert.Single(events.Fired);
        Assert.Equal("player_death", e.Name);
        return e.Fields;
    }

    [Fact]
    public void A_player_kill_names_both_userids_and_the_killers_weapon()
    {
        var (notice, events, _) = Rig();
        notice.Fire(Victim, Killer);
        var f = Only(events);
        Assert.Equal((7, 9, "pistol", 7), ((int)f["userid"], (int)f["attacker"], (string)f["weapon"], (int)f["priority"]));
    }

    [Fact]
    public void A_suicide_is_the_victims_own_userid_and_weapon()
    {
        var (notice, events, _) = Rig("weapon_crowbar");
        notice.Fire(Victim, Victim);
        var f = Only(events);
        Assert.Equal((7, 7, "crowbar"), ((int)f["userid"], (int)f["attacker"], (string)f["weapon"]));
    }

    [Fact]
    public void Nobody_is_the_world()
    {
        var (notice, events, _) = Rig();
        notice.Fire(Victim, EntityHandle.None);
        var f = Only(events);
        Assert.Equal((0, "world"), ((int)f["attacker"], (string)f["weapon"]));
    }

    [Fact]
    public void A_prop_is_physics_and_credits_nobody()
    {
        var (notice, events, _) = Rig();
        notice.Fire(Victim, Prop);
        var f = Only(events);
        Assert.Equal((0, "physics"), ((int)f["attacker"], (string)f["weapon"]));
    }

    [Fact]
    public void A_blast_is_named_by_its_inflictor_and_credited_to_its_thrower()
    {
        var (notice, events, inflictors) = Rig();
        using (inflictors.Push(Prop, default))
            notice.Fire(Victim, Killer);
        var f = Only(events);
        Assert.Equal((9, "physics"), ((int)f["attacker"], (string)f["weapon"]));
    }

    [Fact]
    public void The_rules_scoreboard_hears_the_death_through_the_engine_dispatch()
    {
        var (notice, events, _) = Rig();
        var log = new RecordingLog();
        var registry = new GameEventRegistry(events, log);
        events.Registry = registry;
        var services = new ServiceContainer();
        GameEventRegistry.AddEventsTo(services, typeof(PlayerDeathEvent).Assembly, log);
        registry.RegisterAll(typeof(PlayerDeathEvent).Assembly, services);

        var host = Fake().Returns("Host.ActiveSide", Side.Server);
        var board = new Scoreboard(host, new PlayerDisconnectEvent(), log);
        var convars = new ConVarRegistry(UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        var rules = new GameRules(log, convars, host,
                                  Fake().Ignores("Players.FindByUserId", "Players.GetUserId", "Players.GetTeam"),
                                  Fake().Returns("Entities.IsValid", false), board,
                                  services.Require<IGameEvent<PlayerDeathArgs>>());
        rules.ResetRound();
        board.Reset();

        notice.Fire(Victim, Killer);

        Assert.Equal((1, 1), (board.ScoreFor(9), board.DeathsFor(7)));
    }
}

/// <summary>
/// This mod's projectile and prop classes are named in player_death as stock's
/// own classes would be (the inflictor's classname, then stock's renames):
/// logs, stats tools and the kill feed read stock's names.
/// </summary>
public class DeathNoticeWeaponNameTests
{
    [Theory]
    [InlineData(EntityNames.SsBolt, "crossbow_bolt")]
    [InlineData(EntityNames.SsRocket, "rpg_missile")]
    [InlineData(EntityNames.SsGrenade, "grenade_frag")]
    [InlineData(EntityNames.SsEnergyball, "combine_ball")]
    [InlineData(EntityNames.SsBarrel, "physics")]
    public void A_projectile_is_named_as_stocks(string ours, string stock)
        => Assert.Equal(stock, DeathNotice.WeaponName(ours));

    [Theory]
    [InlineData("weapon_pistol", "pistol")]
    [InlineData("prop_physics_multiplayer", "physics")]
    [InlineData("func_door", "door")]
    public void Stocks_own_names_are_unchanged(string name, string expected)
        => Assert.Equal(expected, DeathNotice.WeaponName(name));
}
