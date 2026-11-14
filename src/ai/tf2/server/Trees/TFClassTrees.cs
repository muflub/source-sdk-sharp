using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// Which class's subtree a TF2 bot runs (plan_bots_teams_tf2 C.3, P6 step 6). Only the Soldier's
/// is real so far; every other class runs the Soldier's (its fight with whatever is in its hands,
/// the CTF objectives and the tactical layer), and says so: <see cref="Borrowed"/> is true and the
/// doing record's branch names both. A class with no subtree of its own never uses the Soldier's
/// abilities: the rocket jump is the Soldier's row, read off the bot's real class.
/// </summary>
public static class TFClassTrees
{
    /// <summary>The class whose subtree <paramref name="playerClass"/> runs: the Soldier's, for every class until P7.</summary>
    public static TFClass TreeFor(TFClass playerClass) => TFClass.Soldier;

    /// <summary>Whether <paramref name="playerClass"/> runs another class's subtree, having none of its own yet.</summary>
    public static bool Borrowed(TFClass playerClass) => TreeFor(playerClass) != playerClass;

    /// <summary>What the doing record's branch says of the subtree: "Soldier", or "Soldier (as Scout)" for one borrowed.</summary>
    public static string Name(TFClass playerClass)
        => Borrowed(playerClass) ? $"{TreeFor(playerClass)} (as {playerClass})" : TreeFor(playerClass).ToString();
}
