using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>How the applier runs a row of a binding's <see cref="AbilityTable"/> (plan_bots_teams_tf2 C.3).</summary>
public enum AbilityRun : byte
{
    /// <summary>A client command, as a player's key sends one (<c>build 2 0</c>, <c>disguise 8 2</c>).</summary>
    Command = 0,

    /// <summary>A button mask pressed for <see cref="AbilityRow.HoldTicks"/> ticks (<c>IN_ATTACK2</c>).</summary>
    Buttons,

    /// <summary>A sequence the applier knows by <see cref="AbilityRow.Name"/>, from the bot's own eye (a rocket jump).</summary>
    Sequence,
}

/// <summary>
/// One ability a game's bot can use (plan_bots_teams_tf2 C.3): what it is
/// called and how a usercmd reaches it. The core never names a game's verb:
/// a brain asks for a row by its index in the table, the applier runs it.
/// </summary>
/// <param name="Name">The binding's own name for the ability.</param>
/// <param name="Run">How it is run.</param>
/// <param name="Command">The client command, for <see cref="AbilityRun.Command"/>.</param>
/// <param name="Buttons">The buttons, for <see cref="AbilityRun.Buttons"/> (the engine's <c>IN_*</c> bits).</param>
/// <param name="HoldTicks">How many ticks the buttons are held.</param>
public readonly record struct AbilityRow(string Name, AbilityRun Run, string Command = "", int Buttons = 0, int HoldTicks = 1);

/// <summary>
/// The abilities a game's binding publishes with its <see cref="MapKnowledge"/>
/// (plan_bots_teams_tf2 C.3): an <see cref="ActionKind.Ability"/> one-shot names
/// a row by its index. HL2MP's is empty; <c>default</c> is the empty table.
/// </summary>
public readonly record struct AbilityTable(ImmutableArray<AbilityRow> Rows)
{
    /// <summary>How many rows the table has: 0 for the empty table.</summary>
    public int Count => Rows.IsDefault ? 0 : Rows.Length;

    /// <summary>Row <paramref name="index"/>, or false for one the table does not have.</summary>
    public bool TryGet(int index, out AbilityRow row)
    {
        row = default;
        if ((uint)index >= (uint)Count)
            return false;
        row = Rows[index];
        return true;
    }
}
