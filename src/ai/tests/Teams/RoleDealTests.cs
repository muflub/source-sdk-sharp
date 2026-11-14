using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// The team role allocator (plan_bots_teams_tf2_part_g G.2.2, <see cref="RoleDeal"/> and
/// <see cref="RoleHold"/>): the bots' roles follow the split policy it is handed at every team
/// size, a person seen doing a job is one bot fewer on it, the deal is the same for the same
/// inputs in any order, and a role is not changed more often than its hold time.
/// </summary>
public sealed class RoleDealTests
{
    /// <summary>A team of <paramref name="bots"/> bots in slots 2, 4, 6, ... (the other team has the odd ones) and <paramref name="humans"/> people after them.</summary>
    private static RoleMember[] Team(int bots, params TeamRole[] humans)
        => [.. Enumerable.Range(0, bots).Select(i => new RoleMember(2 + 2 * i, false)),
            .. humans.Select((doing, i) => new RoleMember(2 + 2 * (bots + i), true, doing))];

    private static int Count(RoleMember[] team, RoleCounts counts, TeamRole role, int carrier = 0)
        => team.Count(m => !m.Human && RoleDeal.Of(m.Slot, team, counts, carrier) == role);

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void TheDefendersAreThePolicysAtEveryTeamSize(int size)
    {
        RoleMember[] team = Team(size);
        RoleCounts policy = RoleSplits.ThirdDefend(size);
        Assert.Equal((policy.Defend, 0, size - policy.Defend),
                     (Count(team, policy, TeamRole.Defend), Count(team, policy, TeamRole.Escort), Count(team, policy, TeamRole.Attack)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void WhileATeammateCarriesTheEscortsAreThePolicys(int size)
    {
        RoleMember[] team = Team(size);
        RoleCounts policy = RoleSplits.ThirdDefend(size);
        int carrier = team[0].Slot;
        // The carrier attacks, and there cannot be more escorts than bots left over.
        int escorts = Math.Min(policy.Escort, size - policy.Defend - 1);
        Assert.Equal((policy.Defend, escorts), (Count(team, policy, TeamRole.Defend, carrier), Count(team, policy, TeamRole.Escort, carrier)));
    }

    [Theory]
    [InlineData(2, 0, 1)]
    [InlineData(4, 1, 1)]
    [InlineData(6, 2, 2)]
    [InlineData(8, 2, 2)]
    public void TheDefaultPolicyIsAThirdDefendingAndAThirdEscorting(int size, int defend, int escort)
        => Assert.Equal(new RoleCounts(defend, escort), RoleSplits.ThirdDefend(size));

    /// <summary>The split is the policy's, not the allocator's: another policy deals other counts.</summary>
    [Fact]
    public void AnotherPolicyDealsItsOwnCounts()
    {
        static RoleCounts Half(int size) => new(size / 2, 0);
        RoleMember[] team = Team(6);
        Assert.Equal((3, 0), (Count(team, Half(6), TeamRole.Defend, team[^1].Slot), Count(team, Half(6), TeamRole.Escort, team[^1].Slot)));
    }

    [Fact]
    public void APersonDefendingIsOneBotFewerDefending()
    {
        RoleCounts policy = RoleSplits.ThirdDefend(6);
        Assert.Equal((2, 1), (Count(Team(5, TeamRole.Attack), policy, TeamRole.Defend), Count(Team(5, TeamRole.Defend), policy, TeamRole.Defend)));
    }

    [Fact]
    public void APersonWithTheCarrierIsOneBotFewerEscorting()
    {
        RoleCounts policy = RoleSplits.ThirdDefend(6);
        RoleMember[] alone = Team(5, TeamRole.Attack), together = Team(5, TeamRole.Escort);
        int carrier = alone[0].Slot;
        Assert.Equal((2, 1), (Count(alone, policy, TeamRole.Escort, carrier), Count(together, policy, TeamRole.Escort, carrier)));
    }

    [Fact]
    public void TheCarrierIsDealtNoEscort()
    {
        RoleMember[] team = Team(6);
        RoleCounts policy = RoleSplits.ThirdDefend(6);
        // The team's first bot would lead the escorts' line: as the carrier it attacks, and the line is the two after it.
        int carrier = team[0].Slot;
        Assert.Equal(TeamRole.Attack, RoleDeal.Of(carrier, team, policy, carrier));
        Assert.Equal(policy.Escort, Count(team, policy, TeamRole.Escort, carrier));
    }

    [Fact]
    public void TheSameTeamInAnyOrderIsTheSameDeal()
    {
        RoleMember[] team = Team(7, TeamRole.Defend), shuffled = [.. team.Reverse()];
        RoleCounts policy = RoleSplits.ThirdDefend(8);
        int carrier = team[5].Slot;
        foreach (RoleMember m in team)
            Assert.Equal(RoleDeal.Of(m.Slot, team, policy, carrier), RoleDeal.Of(m.Slot, shuffled, policy, carrier));
    }

    /// <summary>
    /// Slot order: it is the team's last bots that defend, so every bot that knows the roster knows
    /// who, and the first slots (a listen server's host has slot 1) are never the ones counted on.
    /// </summary>
    [Fact]
    public void TheLastSlotsDefend()
    {
        RoleMember[] team = Team(6);
        RoleCounts policy = RoleSplits.ThirdDefend(6);
        Assert.Equal([10, 12], team.Where(m => RoleDeal.Of(m.Slot, team, policy) == TeamRole.Defend).Select(m => m.Slot));
    }

    /// <summary>While a teammate carries, the escorts are the first of the bots who do not defend, the carrier left out of their line.</summary>
    [Fact]
    public void TheEscortsAreTheFirstBotsWhoDoNotDefend()
    {
        RoleMember[] team = Team(8);
        RoleCounts policy = RoleSplits.ThirdDefend(8);
        Assert.Equal([TeamRole.Escort, TeamRole.Attack, TeamRole.Escort, TeamRole.Attack, TeamRole.Attack, TeamRole.Attack, TeamRole.Defend, TeamRole.Defend],
                     team.Select(m => RoleDeal.Of(m.Slot, team, policy, carrierSlot: 4)));
    }

    /// <summary>A defender who carries (it took the flag at its own door) is out of nobody's line: the escorts are still the first who do not defend.</summary>
    [Fact]
    public void ADefenderCarryingLeavesTheEscortsLineAsItWas()
    {
        RoleMember[] team = Team(6);
        RoleCounts policy = RoleSplits.ThirdDefend(6);
        Assert.Equal([TeamRole.Escort, TeamRole.Escort, TeamRole.Attack, TeamRole.Attack, TeamRole.Defend, TeamRole.Defend],
                     team.Select(m => RoleDeal.Of(m.Slot, team, policy, carrierSlot: 12)));
    }

    /// <summary>A person is dealt nothing and stands in nobody's line, whatever their slot: with one in slot 1 and one in the last, the defenders are the last two bots.</summary>
    [Fact]
    public void APersonsSlotTakesNoPlaceAmongTheBots()
    {
        RoleMember[] team = [new RoleMember(1, true), .. Team(4), new RoleMember(31, true)];
        RoleCounts policy = RoleSplits.ThirdDefend(6);
        Assert.Equal([6, 8], team.Where(m => !m.Human && RoleDeal.Of(m.Slot, team, policy) == TeamRole.Defend).Select(m => m.Slot));
    }

    private const float Tick = 0.015f;

    private static Tick At(float seconds) => new((int)(seconds / Tick));

    [Fact]
    public void TheFirstDealIsTakenAtOnce()
    {
        var hold = new RoleHold();
        Assert.True(hold.Take(TeamRole.Defend, At(1f), Tick));
        Assert.Equal((true, TeamRole.Defend, 0), (hold.Dealt, hold.Role, hold.Flips));
    }

    [Fact]
    public void AnotherDealInsideTheHoldTimeIsNotTaken()
    {
        var hold = new RoleHold();
        hold.Take(TeamRole.Defend, At(1f), Tick);
        Assert.False(hold.Take(TeamRole.Attack, At(1f + RoleHold.HoldSeconds - 0.5f), Tick));
        Assert.Equal(TeamRole.Defend, hold.Role);
    }

    [Fact]
    public void AnotherDealAfterTheHoldTimeIsTaken()
    {
        var hold = new RoleHold();
        hold.Take(TeamRole.Defend, At(1f), Tick);
        Assert.True(hold.Take(TeamRole.Attack, At(1f + RoleHold.HoldSeconds + 0.5f), Tick));
        Assert.Equal((TeamRole.Attack, 1), (hold.Role, hold.Flips));
    }

    /// <summary>
    /// Going with a teammate who has just taken the objective does not wait: an attacker dealt the
    /// escort takes it at once, however lately it became an attacker (an escort is an attacker
    /// while nobody carries, so this takes it off no job).
    /// </summary>
    [Fact]
    public void AnAttackerDealtTheEscortTakesItAtOnce()
    {
        var hold = new RoleHold();
        hold.Take(TeamRole.Attack, At(1f), Tick);
        Assert.True(hold.Take(TeamRole.Escort, At(1.5f), Tick));
        Assert.Equal((TeamRole.Escort, 1), (hold.Role, hold.Flips));
    }

    [Fact]
    public void LeavingTheEscortWaitsTheHoldOut()
    {
        var hold = new RoleHold();
        hold.Take(TeamRole.Attack, At(1f), Tick);
        hold.Take(TeamRole.Escort, At(2f), Tick);
        bool early = hold.Take(TeamRole.Attack, At(2f + RoleHold.HoldSeconds - 0.5f), Tick);
        bool late = hold.Take(TeamRole.Attack, At(2f + RoleHold.HoldSeconds + 0.5f), Tick);
        Assert.Equal((false, true, TeamRole.Attack), (early, late, hold.Role));
    }

    /// <summary>Only an attacker joins at once: a defender dealt the escort keeps its post for the hold time.</summary>
    [Fact]
    public void ADefenderDealtTheEscortWaitsTheHoldOut()
    {
        var hold = new RoleHold();
        hold.Take(TeamRole.Defend, At(1f), Tick);
        Assert.False(hold.Take(TeamRole.Escort, At(2f), Tick));
        Assert.Equal(TeamRole.Defend, hold.Role);
    }

    /// <summary>The objective taken and lost every second for a minute: the bot goes with every carrier, and is taken off the escort at most once a hold time.</summary>
    [Fact]
    public void AStormOfCarriersTakesABotOffTheEscortAtMostOnceAHoldTime()
    {
        var hold = new RoleHold();
        int left = 0, missed = 0;
        for (int s = 0; s <= 60; s++)
        {
            TeamRole dealt = s % 2 == 0 ? TeamRole.Attack : TeamRole.Escort, was = hold.Role;
            hold.Take(dealt, At(s), Tick);
            left += was == TeamRole.Escort && hold.Role == TeamRole.Attack ? 1 : 0;
            missed += dealt == TeamRole.Escort && hold.Role != TeamRole.Escort ? 1 : 0;
        }

        Assert.Equal(0, missed);
        Assert.InRange(left, 1, (int)(60f / RoleHold.HoldSeconds));
    }

    /// <summary>A deal that changes every second for a minute flips the role at most once a hold time.</summary>
    [Fact]
    public void AStormOfDealsFlipsTheRoleAtMostOnceAHoldTime()
    {
        var hold = new RoleHold();
        for (int s = 0; s <= 60; s++)
            hold.Take(s % 2 == 0 ? TeamRole.Defend : TeamRole.Attack, At(s), Tick);
        Assert.InRange(hold.Flips, 1, (int)(60f / RoleHold.HoldSeconds));
    }
}
