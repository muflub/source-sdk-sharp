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

    /// <summary>
    /// A program (plan_bots_teams_tf2_part_g G.5 step 2): the applier steps it a command at a time
    /// (<see cref="IAbilityRun"/>), telling it the bot's own state and taking that command's view, move
    /// and buttons from it, until it says it is over, the bot dies, or <see cref="AbilityRow.HoldTicks"/>
    /// commands have gone. The game's binding supplies it by the row's name (<see cref="IAbilityPrograms"/>).
    /// </summary>
    Program,
}

/// <summary>
/// What an <see cref="ActionKind.Ability"/> one-shot's <see cref="ActionIntent.WeaponSlot"/> holds: the
/// row of the table in its low <see cref="RowBits"/> bits, and over them an argument the row's program
/// is begun with (0, none: a one-shot that names only a row reads as it always did).
/// </summary>
public static class AbilityCall
{
    public const int RowBits = 8;

    /// <summary>The highest row and the highest argument a one-shot can name.</summary>
    public const int MaxRow = (1 << RowBits) - 1, MaxArgument = (1 << (31 - RowBits)) - 1;

    /// <summary>The slot that names <paramref name="row"/> with <paramref name="argument"/>.</summary>
    public static int Pack(int row, int argument)
    {
        if ((uint)row > MaxRow || (uint)argument > MaxArgument)
            throw new ArgumentOutOfRangeException(nameof(row), $"row {row} with argument {argument}: a one-shot names a row to {MaxRow} and an argument to {MaxArgument}");
        return row | (argument << RowBits);
    }

    /// <summary>The row <paramref name="slot"/> names, or -1 for a slot that names none.</summary>
    public static int Row(int slot) => slot < 0 ? -1 : slot & MaxRow;

    /// <summary>The argument <paramref name="slot"/> carries, 0 for none.</summary>
    public static int Argument(int slot) => slot < 0 ? 0 : slot >> RowBits;
}

/// <summary>
/// What a program is told of its bot each command: where it is, how it moves, the view its last
/// command sent, and whether it stands on something. The bot's own state, as its client has it.
/// <paramref name="Bot"/> is which bot it is (<see cref="ActorId.None"/> when the caller does not say): a program whose
/// game lends the hands in flight to the bot's own play (round 2's S1) reads that play's ask by it.
/// </summary>
public readonly record struct AbilityBody(AiVector Origin, AiVector Velocity, AiAngles View, bool OnGround, ActorId Bot = default);

/// <summary>
/// One command of a program. <paramref name="View"/> is where the view is to come to, not this
/// command's view: the applier turns the view there through the bot's own motion
/// (<see cref="HumanMotion"/>: a reaction, then a flick), and <paramref name="Look"/> numbers the
/// look, a new number a new thing to react to. <paramref name="Wish"/> is the move in the world's
/// frame, its length the move a key sends: the applier makes of it the forward and side moves of the
/// view it does send. <paramref name="Over"/>: the program is done and this command is not sent;
/// the bot's intent has the command back. <paramref name="Weapon"/> is a weapon the command selects, as a
/// <see cref="ActionKind.SelectWeapon"/> one-shot names it, or null (the default, so <c>default</c> selects nothing): a
/// program's commands are the bot's alone while it runs, so a weapon its game lends in flight (round 2's S1: a Soldier's
/// melee taken out in a rocket jump) is selected here.
/// </summary>
public readonly record struct AbilityCommand(AiAngles View, int Look, AiVector Wish, int Buttons, bool Over = false, int? Weapon = null)
{
    /// <summary>The command that ends a program.</summary>
    public static AbilityCommand Done => new(default, 0, default, 0, Over: true);
}

/// <summary>How a program's run ended.</summary>
public enum AbilityEnd : byte
{
    /// <summary>It said so itself (<see cref="AbilityCommand.Over"/>).</summary>
    Over = 0,

    /// <summary>The bot died, or left the game or the level.</summary>
    Died,

    /// <summary>Its row's commands ran out first.</summary>
    TimedOut,
}

/// <summary>
/// One run of a program for one bot (plan_bots_teams_tf2_part_g G.5 step 2): stepped once a command
/// on the game's thread, by the applier alone.
/// </summary>
public interface IAbilityRun
{
    /// <summary>This command, for the bot as <paramref name="body"/> has it.</summary>
    AbilityCommand Step(in AbilityBody body);

    /// <summary>The run is over, for <paramref name="why"/>, with the bot as <paramref name="body"/> has it: called once.</summary>
    void Ended(AbilityEnd why, in AbilityBody body);
}

/// <summary>
/// A program a game's binding supplies for an <see cref="AbilityRun.Program"/> row: it begins a run
/// for a bot, or refuses.
/// </summary>
public interface IAbilityProgram
{
    /// <summary>
    /// A run begun for the bot as <paramref name="body"/> has it, with the one-shot's
    /// <paramref name="argument"/>, the numbers its view moves by and a command's length; null when
    /// the argument names nothing it can run.
    /// </summary>
    IAbilityRun? Begin(int argument, in AbilityBody body, in MotionParams motion, float tickSeconds);
}

/// <summary>The programs of a game's binding, by the name of the row each runs.</summary>
public interface IAbilityPrograms
{
    /// <summary>The program for the row called <paramref name="ability"/>, or null.</summary>
    IAbilityProgram? Named(string ability);
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
/// <param name="HoldTicks">How many ticks the buttons are held; for <see cref="AbilityRun.Program"/>, the most commands a run may take.</param>
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
