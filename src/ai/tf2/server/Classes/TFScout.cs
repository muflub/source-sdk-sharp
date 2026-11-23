using System.Collections.Immutable;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Scout's own (plan_bots_teams_tf2 C.3, C.3.1, P7): its play (<see cref="TFDecider.ClassPlay"/>'s members it
/// overrides), its ability rows, its archetypes and whether the composition may field it. The Scout lane's file:
/// another class's lane never edits it, and it never edits another's.
/// </summary>
public static class TFScoutClass
{
    /// <summary>Whether the composition fields the Scout (C.4): false until the Scout lane says its play is ready: until then the composition fields no Scout (a pin still may).</summary>
    public const bool Ready = false;

    /// <summary>
    /// The Scout's ability rows, appended in its own block of the one table (<see cref="TFClassSpecs.BlockOf"/>): a row
    /// added here moves no other row, so recordings keep their meaning. Add at the end only.
    /// </summary>
    public static ImmutableArray<AbilityRow> Abilities() => [];

    /// <summary>The Scout's archetypes (C.3.1), drawn by the bot's seed and weight: none yet, so every slot is the seeded draw's.</summary>
    public static TFArchetypeTable Archetypes() => TFArchetypeTable.None(TFClass.Scout);

    /// <summary>What the foundation composes: the class, its ready flag, its rows, its archetypes.</summary>
    public static TFClassSpec Spec() => new(TFClass.Scout, Ready, Abilities(), Archetypes());
}

public sealed partial class TFDecider
{
    /// <summary>The Scout's play: the general play, until the Scout lane writes its own.</summary>
    private sealed class ScoutPlay(TFDecider d) : ClassPlay(d, TFClass.Scout)
    {
    }
}
