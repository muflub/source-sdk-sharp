using System.Collections.Immutable;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Soldier's own (plan_bots_teams_tf2 C.3, C.3.1, P7): its play (<see cref="TFDecider.ClassPlay"/>'s members it
/// overrides), its ability rows, its archetypes and whether the composition may field it. The Soldier lane's file:
/// another class's lane never edits it, and it never edits another's.
/// </summary>
public static class TFSoldierClass
{
    /// <summary>Whether the composition fields the Soldier (C.4): the Soldier's play is the one P6 built (the fight, the rocket jump, the objectives), so the composition fields it.</summary>
    public const bool Ready = true;

    /// <summary>
    /// The Soldier's ability rows, appended in its own block of the one table (<see cref="TFClassSpecs.BlockOf"/>): a row
    /// added here moves no other row, so recordings keep their meaning. Add at the end only.
    /// </summary>
    public static ImmutableArray<AbilityRow> Abilities() => [];

    /// <summary>
    /// The Soldier's archetypes (C.3.1; research p7-research/soldier.md §2, as the review corrected it), drawn by the bot's
    /// seed and weight. The stock Soldier is what most bots are (it reached the point first and dealt the most damage a bot in
    /// the A/B); the Direct Hit next (as fast, fewest deaths); the banner less (the least damage, so its meter seldom fills); the
    /// roamer rarely (the slowest to the point), drawable so a pin and its facts have a seed.
    /// </summary>
    public static TFArchetypeTable Archetypes() => new(TFClass.Soldier,
    [
        new TFArchetype(Stock, 0.6f, [new(TFLoadoutSlot.Primary, [18]), new(TFLoadoutSlot.Secondary, [10]), new(TFLoadoutSlot.Melee, [6])]),
        new TFArchetype(Roamer, 0.05f, [new(TFLoadoutSlot.Primary, [228, 18]), new(TFLoadoutSlot.Secondary, [133]), new(TFLoadoutSlot.Melee, [6])]),
        new TFArchetype(Bannerman, 0.15f, [new(TFLoadoutSlot.Primary, [18]), new(TFLoadoutSlot.Secondary, [129]), new(TFLoadoutSlot.Melee, [6])]),
        new TFArchetype(DirectHitter, 0.2f, [new(TFLoadoutSlot.Primary, [127]), new(TFLoadoutSlot.Secondary, [415, 10]), new(TFLoadoutSlot.Melee, [6])]),
    ]);

    /// <summary>The stock all-rounder (research §2 A): Rocket Launcher, Shotgun, Shovel, each standing for its reskins; every archetype's control.</summary>
    public const string Stock = "Stock";

    /// <summary>The roamer (research §2 C): the Black Box and the Gunboats; jumps more for less, goes ahead of its team, hunts the Medic and the Sniper.</summary>
    public const string Roamer = "Roamer";

    /// <summary>The banner Soldier (research §2 D): the Buff Banner in place of the shotgun, popped with its team (the Backup and the Concheror wait for a row of their own: a preference list draws its first offered item only).</summary>
    public const string Bannerman = "Banner";

    /// <summary>The Direct Hit Soldier (research §2 E): long range, body shots and airshots, the sentry killer; the Reserve Shooter beside it.</summary>
    public const string DirectHitter = "DirectHit";

    /// <summary>
    /// The archetype's factor on root branch <paramref name="branch"/> (the play's <c>Weigh</c>), each kept inside the bounds
    /// the general play's scores hold (the review's C3, C4; facts in TFSoldierArchetypeTests):
    /// <list type="bullet">
    /// <item>a roamer gathers with its team less, but its team branch stays over Search (0.40) and a remembered fight (0.30), so
    /// it pushes rather than looking into every sound (at 0.6 it searched 16 % of its time);</item>
    /// <item>a banner Soldier gathers with its team more, under a far Fetch (0.70);</item>
    /// <item>a Direct Hit weighs a sentry to shell more, still under the points (0.50);</item>
    /// <item>no archetype weighs the fight: over 1 a far fight passes a far Fetch (the ctf_2fort bridge) and a near one
    /// passes Survive.</item>
    /// </list>
    /// </summary>
    public static float BranchFactor(string archetype, string branch)
    {
        if (string.Equals(archetype, Roamer, StringComparison.OrdinalIgnoreCase))
            return branch == TFDecider.TeamBranch ? TFDecider.RoamerTeamWeight : 1f;
        if (string.Equals(archetype, Bannerman, StringComparison.OrdinalIgnoreCase))
            return branch == TFDecider.TeamBranch ? TFDecider.BannerTeamWeight : 1f;
        if (string.Equals(archetype, DirectHitter, StringComparison.OrdinalIgnoreCase))
            return branch == TFDecider.ClassBranch ? TFDecider.DirectShellWeight : 1f;
        return 1f;
    }

    /// <summary>What the foundation composes: the class, its ready flag, its rows, its archetypes.</summary>
    public static TFClassSpec Spec() => new(TFClass.Soldier, Ready, Abilities(), Archetypes());
}
