using System.Reflection;
using System.Runtime.CompilerServices;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Motion;
using TF2.Descent.Server.Horde.Senses;
using TF2.Descent.Server.Horde.Session;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The horde session's place among the server's services (plan_descent_logic
/// HO4, HO5): the chains it joins have orders of their own after TF2's band
/// (a tie aborts the chain's dispatch live: HookOrderTests' rule, for the
/// chains HookOrderTests does not list); and the server never references the
/// AI's server binding, whose services would join any composition that loads it
/// (LoadedAssemblies sweeps every reference: HO4's nav ruling).
/// </summary>
public sealed class HordeSessionShapeTests
{
    private static int OrderOf(Type t, Type chain)
    {
        object o = RuntimeHelpers.GetUninitializedObject(t);
        return chain == typeof(IFrameLifecycle) ? ((IFrameLifecycle)o).FrameOrder : (o as IOrderedHook)?.HookOrder ?? 0;
    }

    private static (string Name, int Order)[] Members(Assembly a, Type chain) =>
        [.. a.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && chain.IsAssignableFrom(t)
                                    && t.GetCustomAttributes<SideServiceAttribute>().Any(s => s.Side == Side.Server))
                        .Select(t => (t.Name, OrderOf(t, chain)))];

    [Theory]
    [InlineData(typeof(ISoundEmitHook))]
    [InlineData(typeof(IPlayerDeathHook))]
    public void The_sessions_chain_members_have_orders_of_their_own_after_TF2s_band(Type chain)
    {
        var descent = Members(typeof(HordeSession).Assembly, chain);
        Assembly[] others = [typeof(TF2.Server.Modes.TFGameModeSelector).Assembly, typeof(GameModes).Assembly];
        var theirs = others.SelectMany(a => Members(a, chain)).ToArray();
        string[] mine = [nameof(HordeHearingTap), nameof(HordeHeroDeaths)];

        var members = descent.Where(d => mine.Contains(d.Name)).ToArray();
        Assert.NotEmpty(members);
        foreach (var (name, order) in members)
        {
            Assert.True(order >= ModStages.First, $"{chain.Name}: {name} at {order}, inside TF2's band or the core's");
            Assert.DoesNotContain(theirs, t => t.Order == order);
            Assert.Single(descent, d => d.Order == order);
        }
    }

    [Fact]
    public void A_hit_reaches_the_tally_then_the_brains_then_the_immune_notice_then_the_blasts_push_then_the_ragdoll()
    {
        var members = Members(typeof(HordeSession).Assembly, typeof(IRobotHitSink)).OrderBy(m => m.Order).Select(m => m.Name).ToArray();
        Assert.Equal([nameof(RobotHitTally), nameof(HordeHitSense), nameof(RobotImmuneNotices), nameof(RobotBlastPush),
                      nameof(TF2.Descent.Server.Horde.Champions.ChampionDeaths), nameof(TF2.Descent.Server.Horde.Champions.ChampionTeleports),
                      nameof(RobotRagdolls)], members);
    }

    /// <summary>HO12's guard chain: the buster's spin (the hands) first, then the champions' über and shield; each order its own.</summary>
    [Fact]
    public void A_hit_is_guarded_by_the_busters_spin_then_the_champions()
    {
        var members = Members(typeof(HordeSession).Assembly, typeof(IRobotDamageGuard)).OrderBy(m => m.Order).ToArray();

        Assert.Equal([nameof(TF2.Descent.Server.Horde.Attacks.RobotAttacks), nameof(TF2.Descent.Server.Horde.Champions.ChampionGuards)], members.Select(m => m.Name));
        Assert.Equal(members.Length, members.Select(m => m.Order).Distinct().Count());
        Assert.All(members, m => Assert.True(m.Order >= ModStages.First));
    }

    /// <summary>HO12's tickers, the champions' guards, and HO13's boss encounter and its verbs, each at an order of its own; the encounter's phase is current before a windup lands.</summary>
    [Fact]
    public void The_hordes_tickers_are_the_champions_guards_and_the_boss_fight()
    {
        var members = Members(typeof(HordeSession).Assembly, typeof(IHordeTicker));

        Assert.Equal([nameof(TF2.Descent.Server.Horde.Boss.BossAbilities), nameof(TF2.Descent.Server.Horde.Boss.BossEncounter),
                      nameof(TF2.Descent.Server.Horde.Champions.ChampionGuards)], members.Select(m => m.Name).Order());
        Assert.Equal(members.Length, members.Select(m => m.Order).Distinct().Count());
        Assert.All(members, m => Assert.True(m.Order >= ModStages.First));
        Assert.True(members.Single(m => m.Name == "BossAbilities").Order > members.Single(m => m.Name == "BossEncounter").Order);
    }

    /// <summary>HO13's ability chain: the bosses' verbs alone, at an order of their own.</summary>
    [Fact]
    public void The_ability_chain_is_the_bosses_verbs()
    {
        var members = Members(typeof(HordeSession).Assembly, typeof(TF2.Descent.Server.Horde.Boss.IRobotAbility));

        var only = Assert.Single(members);
        Assert.Equal(nameof(TF2.Descent.Server.Horde.Boss.BossAbilities), only.Name);
        Assert.True(only.Order >= ModStages.First);
    }

    [Fact]
    public void The_descent_server_never_references_the_ai_server_binding()
    {
        var references = typeof(HordeSession).Assembly.GetReferencedAssemblies().Select(r => r.Name).ToArray();
        Assert.Contains("SourceSharp.Ai", references);
        Assert.DoesNotContain("SourceSharp.Ai.Server", references);
    }
}
