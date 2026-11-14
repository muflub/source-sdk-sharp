using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>How the applier runs a row of a binding's <see cref="AbilityTable"/> (plan_bots_teams_tf2 C.3).</summary>
public enum AbilityRun : byte
{
    /// <summary>A client command, as a player's key sends one (<c>build 2 0</c>, <c>disguise 8 2</c>).</summary>
    Command = 0,

    /// <summary>A button mask pressed for <see cref="AbilityRow.HoldTicks"/> ticks (<c>IN_ATTACK2</c>).</summary>
    Buttons,

    /// <summary>
    /// The row's <see cref="AbilityRow.Steps"/>, one after another, from the bot's own eye (a rocket jump:
    /// look down, jump and duck, fire at the ground). The applier runs them; the game only writes the data.
    /// </summary>
    Sequence,
}

/// <summary>
/// One step of a <see cref="AbilityRun.Sequence"/> row (plan_bots_teams_tf2 C.3): the buttons
/// held for <paramref name="Ticks"/> commands, and the pitch the view turns to while it lasts
/// (<see cref="float.NaN"/>: the view goes where the intent looks).
/// </summary>
public readonly record struct AbilityStep(int Buttons, int Ticks, float Pitch = float.NaN)
{
    /// <summary>Whether the step turns the view to its own pitch.</summary>
    public bool TurnsTheView => !float.IsNaN(Pitch);
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
/// <param name="Steps">The steps, for <see cref="AbilityRun.Sequence"/>.</param>
public readonly record struct AbilityRow(string Name, AbilityRun Run, string Command = "", int Buttons = 0, int HoldTicks = 1,
                                         ImmutableArray<AbilityStep> Steps = default)
{
    /// <summary>How many steps the row has: 0 for a row that is not a sequence.</summary>
    public int StepCount => Steps.IsDefault ? 0 : Steps.Length;
}

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
