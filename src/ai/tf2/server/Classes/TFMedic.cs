using System.Collections.Immutable;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Medic's own (plan_bots_teams_tf2 C.3, C.3.1, P7): its play (<see cref="TFDecider.ClassPlay"/>'s members it
/// overrides), its ability rows, its archetypes and whether the composition may field it. The Medic lane's file:
/// another class's lane never edits it, and it never edits another's.
/// </summary>
public static class TFMedicClass
{
    /// <summary>Whether the composition fields the Medic (C.4): false until the Medic lane says its play is ready: until then the composition fields no Medic (a pin still may).</summary>
    public const bool Ready = false;

    /// <summary>
    /// The Medic's ability rows, appended in its own block of the one table (<see cref="TFClassSpecs.BlockOf"/>): a row
    /// added here moves no other row, so recordings keep their meaning. Add at the end only.
    /// </summary>
    public static ImmutableArray<AbilityRow> Abilities() => [];

    /// <summary>The Medic's archetypes (C.3.1), drawn by the bot's seed and weight: none yet, so every slot is the seeded draw's.</summary>
    public static TFArchetypeTable Archetypes() => TFArchetypeTable.None(TFClass.Medic);

    /// <summary>What the foundation composes: the class, its ready flag, its rows, its archetypes.</summary>
    public static TFClassSpec Spec() => new(TFClass.Medic, Ready, Abilities(), Archetypes());
}

public sealed partial class TFDecider
{
    /// <summary>The Medic's play: the general play, until the Medic lane writes its own.</summary>
    private sealed class MedicPlay(TFDecider d) : ClassPlay(d, TFClass.Medic)
    {
    }
}
