using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// A hit on the player fires exactly one <c>player_hurt</c>, as stock's
/// <c>CBasePlayer::OnTakeDamage</c> (tf_player.cpp): the victim's userid,
/// their health after the hit floored at zero, the post-armour
/// <c>damageamount</c> — what the combat text keys off — the attacker's
/// userid when the attacker is a player, and the crit bit from the open
/// <see cref="DamageHitGroups"/> scope.
/// </summary>
public class PlayerHurtEventTests
{
    private static readonly EntityHandle Victim = new(0x0001_0003u);
    private static readonly EntityHandle Attacker = new(0x0002_0001u);
    private static readonly EntityHandle Prop = new(0x0003_0002u);

    private sealed class LoopbackEvents : IEngineGameEvents
    {
        private readonly Dictionary<IntPtr, (string Name, Dictionary<string, object> Fields)> _live = new();
        private int _next = 1;
        public readonly List<(string Name, Dictionary<string, object> Fields)> Fired = new();

        public IntPtr EventCreate(string name)
        {
            var h = new IntPtr(_next++);
            _live[h] = (name, new Dictionary<string, object>());
            return h;
        }
        public void EventSetInt(IntPtr h, string key, int value) => _live[h].Fields[key] = value;
        public void EventSetBool(IntPtr h, string key, bool value) => _live[h].Fields[key] = value;
        public void EventSetString(IntPtr h, string key, string value) => _live[h].Fields[key] = value;
        public bool EventFire(IntPtr h, bool dontBroadcast = false)
        {
            Fired.Add(_live[h]);
            _live.Remove(h);
            return true;
        }
        public int EventGetInt(IntPtr h, string key) => 0;
        public float EventGetFloat(IntPtr h, string key) => 0f;
        public bool EventGetBool(IntPtr h, string key) => false;
        public string EventGetString(IntPtr h, string key) => "";
        public void ListenForGameEvent(string name) { }
    }

    private static (SsPlayer Player, LoopbackEvents Events) Rig(DamageHitGroups? hits = null, int health = 100)
    {
        var engine = Fake();
        engine.Returns("Entities.GetHealth", health).Returns("Entities.GetIndex", 3)
              .Returns("Entities.IsValid", true).Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
              .Returns("Entities.IsPlayer", false)
              .ReturnsEachCall("Players.GetUserId", () => engine.Calls[^1].Args[0] is EntityHandle h
                  ? h == Victim ? 7 : h == Attacker ? 9 : 0
                  : 0)
              .ReturnsEachCall("Entities.IsPlayer", () => engine.Calls[^1].Args[0] is EntityHandle h && h == Attacker);
        var events = new LoopbackEvents();
        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid(), messages: engine,
                                  rules: PlayerDamageRulesTests.Rules(team: 0, hits: hits),
                                  events: events, entities: engine, hitGroups: hits)
            { Handle = Victim };
        player.BindAreas(Areas(engine));
        player.State.BindLocal();
        return (player, events);
    }

    [Fact]
    public void AHitFiresPlayerHurtWithThePostArmourAmount()
    {
        var (player, events) = Rig();

        int taken = player.OnDamagedAt(Attacker, 25f, DamageType.Bullet, default, default);

        var e = Assert.Single(events.Fired);
        Assert.Equal("player_hurt", e.Name);
        Assert.Equal(7, e.Fields["userid"]);
        Assert.Equal(25, e.Fields["damageamount"]);
        Assert.Equal(75, e.Fields["health"]);
        Assert.Equal(9, e.Fields["attacker"]);
    }

    [Fact]
    public void AnOpenCritScopeSetsTheCritBit()
    {
        var hits = new DamageHitGroups();
        using var _ = hits.Push(HitGroup.Generic, critical: true);
        var (player, events) = Rig(hits);

        player.OnDamagedAt(Attacker, 25f, DamageType.Bullet, default, default);

        var e = Assert.Single(events.Fired);
        Assert.Equal(true, e.Fields["crit"]);
    }

    [Fact]
    public void APropAttackerSendsNoAttackerId()
    {
        var (player, events) = Rig();

        player.OnDamagedAt(Prop, 25f, DamageType.Bullet, default, default);

        var e = Assert.Single(events.Fired);
        Assert.Equal(0, e.Fields["attacker"]);
    }

    [Fact]
    public void GodTakesNothingAndFiresNothing()
    {
        var (player, events) = Rig();
        player.GodMode = true;

        int taken = player.OnDamagedAt(Attacker, 25f, DamageType.Bullet, default, default);

        Assert.Equal(0, taken);
        Assert.Empty(events.Fired);
    }

    private sealed class Told : IPlayerHurtHook, IHooks<IPlayerHurtHook>
    {
        public readonly List<PlayerHurt> All = new();

        IReadOnlyList<IPlayerHurtHook> IHooks<IPlayerHurtHook>.All => [this];

        public void Each(string what, Action<IPlayerHurtHook> call) => call(this);

        public void OnPlayerHurt(in PlayerHurt hurt) => All.Add(hurt);
    }

    [Fact]
    public void AHitTellsTheHurtHooksWhoHitWhomForHowMuch()
    {
        var engine = Fake();
        engine.Returns("Entities.GetHealth", 100).Returns("Entities.GetIndex", 3)
              .Returns("Entities.IsValid", true).Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
              .Returns("Entities.IsPlayer", false);
        var told = new Told();
        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid(), messages: engine,
                                  rules: PlayerDamageRulesTests.Rules(team: 0), entities: engine, hurtHooks: told)
            { Handle = Victim };
        player.BindAreas(Areas(engine));
        player.State.BindLocal();

        int taken = player.OnDamagedAt(Attacker, 25f, DamageType.Bullet, default, default);

        Assert.Equal([new PlayerHurt(Victim, Attacker, taken, 100 - taken)], told.All);
        Assert.True(taken > 0, "the hit took health");
    }
}
