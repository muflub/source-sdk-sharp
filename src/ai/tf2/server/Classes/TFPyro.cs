using System.Collections.Immutable;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Pyro's own (plan_bots_teams_tf2 C.3, C.3.1, P7): its play (<see cref="TFDecider.ClassPlay"/>'s members it
/// overrides), its ability rows, its archetypes and whether the composition may field it. The Pyro lane's file:
/// another class's lane never edits it, and it never edits another's.
/// </summary>
public static class TFPyroClass
{
    /// <summary>Whether the composition fields the Pyro (C.4): false until the Pyro lane says its play is ready: until then the composition fields no Pyro (a pin still may).</summary>
    public const bool Ready = false;

    /// <summary>
    /// The Pyro's ability rows, appended in its own block of the one table (<see cref="TFClassSpecs.BlockOf"/>): a row
    /// added here moves no other row, so recordings keep their meaning. Add at the end only.
    /// </summary>
    public static ImmutableArray<AbilityRow> Abilities() => [];

    /// <summary>The Pyro's archetypes (C.3.1), drawn by the bot's seed and weight: none yet, so every slot is the seeded draw's.</summary>
    public static TFArchetypeTable Archetypes() => TFArchetypeTable.None(TFClass.Pyro);

    /// <summary>What the foundation composes: the class, its ready flag, its rows, its archetypes.</summary>
    public static TFClassSpec Spec() => new(TFClass.Pyro, Ready, Abilities(), Archetypes());
}

public sealed partial class TFDecider
{
    /// <summary>The Pyro's play: the general play, until the Pyro lane writes its own.</summary>
    private sealed class PyroPlay(TFDecider d) : ClassPlay(d, TFClass.Pyro)
    {
    }
}
