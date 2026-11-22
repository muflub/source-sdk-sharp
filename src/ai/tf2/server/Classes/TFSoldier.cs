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
    /// The Soldier's archetypes (C.3.1), drawn by the bot's seed and weight: the worked example of the shape, the stock
    /// "Pocket" (Rocket Launcher 18, Shotgun 10, Shovel 6, each standing for its reskins). The Soldier lane adds its own
    /// rows here.
    /// </summary>
    public static TFArchetypeTable Archetypes() => new(TFClass.Soldier,
    [
        new TFArchetype(Pocket, 1f, [new(TFLoadoutSlot.Primary, [18]), new(TFLoadoutSlot.Secondary, [10]), new(TFLoadoutSlot.Melee, [6])]),
    ]);

    /// <summary>The stock Soldier's archetype's name.</summary>
    public const string Pocket = "Pocket";

    /// <summary>What the foundation composes: the class, its ready flag, its rows, its archetypes.</summary>
    public static TFClassSpec Spec() => new(TFClass.Soldier, Ready, Abilities(), Archetypes());
}

public sealed partial class TFDecider
{
    /// <summary>The Soldier's play: the rocket jump on the general play.</summary>
    private sealed class SoldierPlay(TFDecider d) : ClassPlay(d, TFClass.Soldier)
    {
        /// <summary>The Soldier's rocket jump (TFDecider.Soldier.cs, TFDecider.Jump.cs): the Soldier's alone.</summary>
        public override void OneShots() => D.RocketJump();
    }
}
