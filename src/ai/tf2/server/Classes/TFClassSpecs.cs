using System.Collections.Immutable;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>What one class's file hands the foundation (plan_bots_teams_tf2 P7): its class, whether the composition may field it, its ability rows and its archetypes.</summary>
public sealed record TFClassSpec(TFClass Class, bool Ready, ImmutableArray<AbilityRow> Abilities, TFArchetypeTable? Archetypes = null)
{
    /// <summary>The class's archetypes: none for a file that names none.</summary>
    public TFArchetypeTable Ways => Archetypes ?? TFArchetypeTable.None(Class);
}

/// <summary>
/// The nine classes' files composed (plan_bots_teams_tf2 C.3, C.4, P7): which classes are ready to be fielded, and
/// the one ability table. The table is the shared rows (TF2's first fifteen, <see cref="Knowledge.TFAbilities"/>)
/// in block 0, then each class's rows in a block of its own of <see cref="BlockRows"/>, at
/// <see cref="BlockOf"/> its class number, the rest of a block unused. An ability one-shot names a row by its
/// index and recordings keep it, so a row must never move: a class adding a row moves no other class's, and
/// none of the shared ones.
/// </summary>
public static class TFClassSpecs
{
    /// <summary>The rows of one block: the most one class may have, and where the next class's begin.</summary>
    public const int BlockRows = 16;

    /// <summary>The name of a row no ability uses: a block's room to grow. The applier runs nothing for it.</summary>
    public const string Unused = "";

    /// <summary>The first row of <paramref name="playerClass"/>'s block: block 0 is the shared rows, Scout's is 16, Engineer's 144.</summary>
    public static int BlockOf(TFClass playerClass) => BlockRows * (int)playerClass;

    /// <summary>The nine playable classes' files, Scout to Engineer.</summary>
    public static ImmutableArray<TFClassSpec> All() =>
    [
        TFScoutClass.Spec(), TFSniperClass.Spec(), TFSoldierClass.Spec(), TFDemomanClass.Spec(), TFMedicClass.Spec(),
        TFHeavyClass.Spec(), TFPyroClass.Spec(), TFSpyClass.Spec(), TFEngineerClass.Spec(),
    ];

    /// <summary>The file of <paramref name="playerClass"/>, or null for a class with none.</summary>
    public static TFClassSpec? Of(TFClass playerClass)
    {
        foreach (TFClassSpec spec in All())
        {
            if (spec.Class == playerClass)
                return spec;
        }

        return null;
    }

    /// <summary>Whether <paramref name="playerClass"/>'s file says the composition may field it.</summary>
    public static bool Ready(TFClass playerClass) => Of(playerClass)?.Ready ?? false;

    /// <summary>
    /// The one table: <paramref name="shared"/> in block 0, each of <paramref name="classes"/>' rows at its block,
    /// <see cref="Unused"/> rows between. Throws for shared rows or a class's rows that overflow a block, or two files
    /// of one class: the table would move a row.
    /// </summary>
    public static AbilityTable Compose(ImmutableArray<AbilityRow> shared, ImmutableArray<TFClassSpec> classes)
    {
        if (shared.Length > BlockRows)
            throw new InvalidOperationException($"{shared.Length} shared ability rows: block 0 holds {BlockRows}");
        int last = 0;
        foreach (TFClassSpec spec in classes)
            last = Math.Max(last, BlockOf(spec.Class) + BlockRows);
        var rows = new AbilityRow[Math.Max(BlockRows, last)];
        Array.Fill(rows, new AbilityRow(Unused, AbilityRun.Command));
        shared.CopyTo(rows, 0);
        var seen = new HashSet<TFClass>();
        foreach (TFClassSpec spec in classes)
        {
            if (!seen.Add(spec.Class))
                throw new InvalidOperationException($"two class files for {spec.Class}");
            ImmutableArray<AbilityRow> own = spec.Abilities.IsDefault ? [] : spec.Abilities;
            if (own.Length > BlockRows)
                throw new InvalidOperationException($"{spec.Class} has {own.Length} ability rows: a block holds {BlockRows}");
            if (BlockOf(spec.Class) + BlockRows - 1 > AbilityCall.MaxRow)
                throw new InvalidOperationException($"{spec.Class}'s block passes row {AbilityCall.MaxRow}, the last a one-shot can name");
            own.CopyTo(rows, BlockOf(spec.Class));
        }

        return new AbilityTable([.. rows]);
    }
}
