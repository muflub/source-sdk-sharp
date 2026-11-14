namespace SourceSharp.Ai;

/// <summary>A bot's part in its team's play on a map with an objective to take and one to keep (plan_bots_teams_tf2_part_g G.2.2).</summary>
public enum TeamRole : byte
{
    /// <summary>Goes for the enemy's objective.</summary>
    Attack = 0,

    /// <summary>Stays by its own team's.</summary>
    Defend,

    /// <summary>Goes with the teammate who carries the enemy's; an attacker while nobody does.</summary>
    Escort,
}

/// <summary>How many of a team defend and how many go with its carrier; everyone else attacks.</summary>
public readonly record struct RoleCounts(int Defend, int Escort);

/// <summary>
/// A team's split policy: the counts per role for a team of <paramref name="teamSize"/> players,
/// its people and its bots together. The allocator (<see cref="RoleDeal"/>) is handed one and
/// states no split of its own.
/// </summary>
public delegate RoleCounts RoleSplit(int teamSize);

/// <summary>The split policies the core names.</summary>
public static class RoleSplits
{
    /// <summary>
    /// A third of the team defends and a third goes with its carrier, one at least; the rest
    /// attack. Thirds round down, so a team of two has no defender.
    /// </summary>
    public static RoleCounts ThirdDefend(int teamSize) => new(Math.Max(0, teamSize / 3), Math.Max(1, teamSize / 3));
}

/// <summary>
/// One player of a team as the allocator is told of them: the slot, whether a person plays it,
/// and for a person what they are seen to be doing (a person the bot has not seen lately is
/// taken to attack). A bot's <see cref="Doing"/> is not read: bots take what they are dealt.
/// </summary>
public readonly record struct RoleMember(int Slot, bool Human, TeamRole Doing = TeamRole.Attack);

/// <summary>
/// The team role allocator (plan_bots_teams_tf2_part_g G.2.2; plan_bots_teams_tf2 B.4): who of a
/// team's bots attacks, defends and escorts, from the split policy's counts, what the team's
/// people are doing and whether a teammate carries the objective. There is no shared mind: each
/// bot works its own role out from what its own HUD and eyes tell it, and because the deal is a
/// function of that alone, in slot order, bots who know the same arrive at the same deal.
///
/// <para>The people come first: each one seen defending is one defender the bots need not
/// find, and each one seen with the carrier one escort. What is left of the policy's counts goes
/// to the bots by slot: the team's last bots defend (never its first slots: a listen server's
/// host has slot 1, and a person is dealt nothing whatever their slot), and while a teammate
/// carries, the first of the bots who do not defend escort; the carrier itself is dealt no
/// escort.</para>
/// </summary>
public static class RoleDeal
{
    /// <summary>
    /// The role of the bot in <paramref name="slot"/> among <paramref name="team"/> (itself
    /// included, in any order) under <paramref name="counts"/>. <paramref name="carrierSlot"/>
    /// is the teammate carrying the enemy's objective, 0 for none: escorts are dealt only then.
    /// </summary>
    public static TeamRole Of(int slot, ReadOnlySpan<RoleMember> team, RoleCounts counts, int carrierSlot = 0)
    {
        int defending = 0, escorting = 0;
        foreach (ref readonly RoleMember m in team)
        {
            if (!m.Human)
                continue;
            defending += m.Doing == TeamRole.Defend ? 1 : 0;
            escorting += m.Doing == TeamRole.Escort && carrierSlot != 0 ? 1 : 0;
        }

        // This bot's place among the team's bots by slot: how many come before it and after it, and whether a bot before it carries.
        int before = 0, after = 0;
        bool carrierBefore = false;
        foreach (ref readonly RoleMember m in team)
        {
            if (m.Human)
                continue;
            before += m.Slot < slot ? 1 : 0;
            after += m.Slot > slot ? 1 : 0;
            carrierBefore |= m.Slot == carrierSlot && m.Slot < slot;
        }

        if (after < Math.Max(0, counts.Defend - defending))
            return TeamRole.Defend;
        if (carrierSlot == 0 || slot == carrierSlot)
            return TeamRole.Attack;

        // The escorts' line: the bots who do not defend (every bot before one that does not), less the carrier, in slot order.
        int ahead = before - (carrierBefore ? 1 : 0);
        return ahead < Math.Max(0, counts.Escort - escorting) ? TeamRole.Escort : TeamRole.Attack;
    }
}

/// <summary>
/// The role a bot holds (plan_bots_teams_tf2_part_g G.2.2's hold time): a deal that would change
/// it is taken only once the bot has held its present role for the hold time, so a run of flag
/// events does not flip it back and forth. Its first deal is taken at once, and so is an
/// attacker's deal of the escort: an escort is an attacker while nobody carries, so going with a
/// teammate who has just taken the objective takes the bot off no job, and waiting the hold out
/// would leave a new carrier alone. Leaving the escort, and a defender's post either way, wait.
/// </summary>
public sealed class RoleHold
{
    /// <summary>How long a role is held before another deal may change it.</summary>
    public const float HoldSeconds = 10f;

    private Tick _since;

    /// <summary>The role held; <see cref="TeamRole.Attack"/> before any deal.</summary>
    public TeamRole Role { get; private set; }

    /// <summary>Whether a deal has been taken.</summary>
    public bool Dealt { get; private set; }

    /// <summary>How many times the role changed after the first deal, for the facts and the counters.</summary>
    public int Flips { get; private set; }

    /// <summary>Offers <paramref name="dealt"/>: true when the role held is now another.</summary>
    public bool Take(TeamRole dealt, Tick now, float tickSeconds, float holdSeconds = HoldSeconds)
    {
        if (!Dealt)
        {
            (Dealt, Role, _since) = (true, dealt, now);
            return true;
        }

        bool joins = Role == TeamRole.Attack && dealt == TeamRole.Escort;
        if (dealt == Role || (!joins && (now - _since) * tickSeconds < holdSeconds))
            return false;
        (Role, _since) = (dealt, now);
        Flips++;
        return true;
    }

    /// <summary>Nothing is held: the bot's team changed, or it is a new map.</summary>
    public void Reset() => (Dealt, Role, _since) = (false, TeamRole.Attack, default);
}
