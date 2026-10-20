using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>A victim that remembers the hitgroup its last hit landed on.</summary>
[ScriptedEntity("test_hitgroup_probe")]
public sealed class HitGroupProbeEntity : ScriptedEntity
{
    public HitGroup? Last { get; private set; }

    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
    {
        Last = hitGroup;
        return 1;
    }
}

/// <summary>
/// The victim's half of the hitgroup (tf2 stage 0.8): the damage op hands the
/// registry the hitgroup the shooter's <see cref="DamageHitGroups"/> scope
/// holds, and the registry hands it to <see cref="ScriptedEntity.OnDamagedAt"/>.
/// The shooter's half is <c>WeaponImpactTests</c>' hitgroup facts.
/// </summary>
public class DamageHitGroupTests
{
    private static readonly EntityHandle Victim = new(0x0001_0005);
    private static readonly EntityHandle Attacker = new(0x0001_0001);

    private static (ScriptedEntityRegistry Registry, HitGroupProbeEntity Probe) Rig()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(DamageHitGroupTests).Assembly);
        registry.Services = Services(out _);
        registry.KeyValue(Victim, "test_hitgroup_probe", "targetname", "probe");
        return (registry, (HitGroupProbeEntity)registry.Get(Victim)!);
    }

    [Fact]
    public void TheDamageRouteHandsTheVictimTheHitgroup()
    {
        var (registry, probe) = Rig();

        int taken = registry.Damaged(Victim, Attacker, 10f, (int)DamageType.Bullet,
                                     default, default, HitGroup.LeftLeg);

        Assert.True(taken == 1, "the probe answered the hit");
        Assert.True(probe.Last == HitGroup.LeftLeg, $"the victim was told LeftLeg (read {probe.Last})");
    }

    [Fact]
    public void DamageWithNoHitgroupReachesTheVictimAsGeneric()
    {
        var (registry, probe) = Rig();

        registry.Damaged(Victim, Attacker, 10f, (int)DamageType.Blast);

        Assert.True(probe.Last == HitGroup.Generic,
            $"a hit with no trace behind it is Generic, stock's zero (read {probe.Last?.ToString() ?? "no call"})");
    }

    [Fact]
    public void TheScopeAnswersTheInnermostHitgroupThenGeneric()
    {
        var hits = new DamageHitGroups();
        Assert.True(hits.Current == HitGroup.Generic, "outside every scope");

        using (hits.Push(HitGroup.Chest))
        {
            using (hits.Push(HitGroup.Head))
                Assert.True(hits.Current == HitGroup.Head, "the innermost scope wins");
            Assert.True(hits.Current == HitGroup.Chest, "closing it reveals the outer one");
        }

        Assert.True(hits.Current == HitGroup.Generic, "and closing that leaves Generic");
    }

    //-------------------------------------------------------------------------
    // The player: the hitgroup reaches the rules' DamageContext.
    //-------------------------------------------------------------------------

    private sealed class Spy : IDamageRule
    {
        public List<HitGroup> Seen { get; } = new();
        public Decision Decide(in DamageContext hit) { Seen.Add(hit.HitGroup); return Decision.UseStockRule; }
    }

    [Fact]
    public void ThePlayersHitgroupReachesTheDamageRulesContext()
    {
        var spy = new Spy();
        var player = PlayerDamageRulesTests.Player(PlayerDamageRulesTests.Rules(team: 0, rule: spy));

        player.OnDamagedAt(Attacker, 10f, DamageType.Bullet, default, default, HitGroup.Head);
        player.OnDamagedAt(Attacker, 10f, DamageType.Bullet, default, default, HitGroup.Stomach);

        Assert.Equal([HitGroup.Head, HitGroup.Stomach], spy.Seen);
    }

    [Fact]
    public void AnUnlocatedHitReachesTheContextAsGeneric()
    {
        var spy = new Spy();
        var player = PlayerDamageRulesTests.Player(PlayerDamageRulesTests.Rules(team: 0, rule: spy));

        player.OnDamaged(Attacker, 10f, DamageType.Fall);

        Assert.Equal([HitGroup.Generic], spy.Seen);
    }

    //-------------------------------------------------------------------------
    // player_debug_print_damage: the line the live gate reads.
    //-------------------------------------------------------------------------

    private static List<string> PrintedFor(string print, HitGroup hitGroup)
    {
        var convars = ConVarRegistry.For(UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        convars.Get<PlayerDebugPrintDamageConVar>()!.Bind(-1, "player_debug_print_damage", print);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsPlayer).Assembly);
        var services = Services(out var engine, convars);
        engine.Strict = false;   // the player's spawn-free build reads more than this fact asks
        registry.Services = services;
        registry.KeyValue(Victim, EntityNames.SsPlayer, "targetname", "victim");
        var player = (SsPlayer)registry.Get(Victim)!;

        var lines = new List<string>();
        registry.Lines.Tap += (line, _) => lines.Add(line);
        player.OnDamagedAt(Attacker, 40f, DamageType.Bullet, default, default, hitGroup);
        return lines.Where(l => l.Contains(" took ")).ToList();
    }

    [Fact]
    public void TheDamagePrintNamesTheHitgroup()
    {
        var head = PrintedFor("1", HitGroup.Head);
        var chest = PrintedFor("1", HitGroup.Chest);

        Assert.True(head.Count == 1 && head[0].Contains("hitgroup=1"), $"a head hit prints hitgroup=1: [{string.Join("|", head)}]");
        Assert.True(chest.Count == 1 && chest[0].Contains("hitgroup=2"), $"a chest hit prints hitgroup=2: [{string.Join("|", chest)}]");
    }

    [Fact]
    public void TheDamagePrintIsSilentWhenOff()
    {
        Assert.Empty(PrintedFor("0", HitGroup.Head));
    }
}
