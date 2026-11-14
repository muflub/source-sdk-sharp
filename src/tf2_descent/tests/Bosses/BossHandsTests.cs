using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Attacks;
using TF2.Descent.Server.Horde.Boss;
using TF2.Descent.Tests.Horde;
using Xunit;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// The horde's hands and HO13's abilities (plan_descent_logic HO13): an ability the hands have no
/// verb for goes to the ability chain; the hands an ability is given count its projectiles
/// against the robots' cap; Major Crits' rockets are crits (MvM's always_crit).
/// </summary>
public sealed class BossHandsTests
{
    private sealed class Taker(bool takes) : IRobotAbility
    {
        public List<(int Row, ActionIntent Action)> Asked { get; } = [];
        public IRobotAbilityHands? Hands { get; private set; }

        public bool Act(HordeController horde, RobotBody body, int row, in ActionIntent action, IRobotAbilityHands hands, long now)
        {
            Asked.Add((row, action));
            Hands = hands;
            return takes;
        }
    }

    private static ActionIntent Ask(int ability, int arg = 0) => new(ActionKind.Ability, FireStyle.Tap, ability, arg);

    [Fact]
    public void An_ability_the_hands_have_no_verb_for_goes_to_the_chain_and_counts_as_carried_out()
    {
        var rig = new AttackRig();
        var first = new Taker(false);
        var second = new Taker(true);
        rig.Abilities.AddRange([first, second]);
        var (id, body) = rig.Robot("sir_nukesalot");
        int family = HordeWeapons.NukeCannon;

        rig.Attacks.Act(body.Index, Ask(HordeAbilities.BossSpecial, 2));

        Assert.Equal([(id.Index, Ask(HordeAbilities.BossSpecial, 2))], second.Asked);
        Assert.Single(first.Asked);   // asked first, it passed
        Assert.Equal(1, rig.Attacks.Fired(family));
        Assert.Equal(0, rig.Attacks.Refused(family, AttackRefusal.Unknown));
    }

    [Fact]
    public void An_ability_no_member_takes_is_refused_as_unknown()
    {
        var rig = new AttackRig();
        rig.Abilities.Add(new Taker(false));
        var (_, body) = rig.Robot("sir_nukesalot");

        rig.Attacks.Act(body.Index, Ask(HordeAbilities.BossSpecial));

        Assert.Equal(1, rig.Attacks.Refused(HordeWeapons.NukeCannon, AttackRefusal.Unknown));
        Assert.Equal(0, rig.Attacks.Fired(HordeWeapons.NukeCannon));
    }

    [Fact]
    public void The_hands_an_ability_launches_with_count_against_the_projectile_cap()
    {
        var rig = new AttackRig();
        var taker = new Taker(true);
        rig.Abilities.Add(taker);
        var (_, body) = rig.Robot("sir_nukesalot");
        rig.Attacks.Act(body.Index, Ask(HordeAbilities.BossSpecial));
        var hands = taker.Hands!;
        int cap = TheRulesCap;

        int made = 0;
        for (int k = 0; k < cap + 3; k++)
            if (hands.Launch(body, "tf_projectile_pipe", default, new Vector(1000f, 0f, 0f), 50f, false) != EntityHandle.None)
                made++;

        Assert.Equal(cap, made);
        Assert.Equal(cap, rig.World.Launches.Count);
        Assert.Equal(3, rig.Attacks.RefusedAll(AttackRefusal.Cap));
        Assert.Equal(cap, rig.Attacks.Flying);
    }

    [Fact]
    public void Major_crits_rockets_are_crits_and_a_plain_soldier_s_are_not()
    {
        var rig = new AttackRig();
        var (_, crits) = rig.Robot("major_crits");
        rig.Fire(crits);
        Assert.Equal(1, rig.World.CriticalLaunches);

        var other = new AttackRig();
        var (_, soldier) = other.Robot("soldier");
        other.Fire(soldier);
        Assert.Single(other.World.Launches);
        Assert.Equal(0, other.World.CriticalLaunches);
    }

    private static int TheRulesCap => HordeFixtures.TheRules.Attacks.ProjectileCap;
}
