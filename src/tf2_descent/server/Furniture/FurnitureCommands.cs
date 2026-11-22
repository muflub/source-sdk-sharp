using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Furniture;

/// <summary><c>tf2_descent_furniture [room]</c>: every furniture row (or one room's), its place, room, awake or asleep, entity or data, state (dev).</summary>
[ConCommand("tf2_descent_furniture", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_furniture [room]: print the level's furniture (kind, variant, place, room, awake or asleep, entity or data, state) and the pool")]
public sealed class FurnitureCommand(FurnitureService furniture, ILog log)
{
    public void Execute([ArgRange(-1, 4096)] int room = -1) => log.Msg(furniture.Report(room));
}

/// <summary>
/// <c>tf2_descent_furniture_use &lt;index&gt; &lt;player slot&gt;</c>: the player uses
/// furniture row <c>index</c> through the path +USE takes (<see cref="FurnitureService.UseAs"/>),
/// wherever he stands (dev).
/// </summary>
[ConCommand("tf2_descent_furniture_use", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_furniture_use <index> <player slot>: the player uses furniture row <index>, as +USE does")]
public sealed class FurnitureUseCommand(FurnitureService furniture, IFurnitureHeroes heroes, ILog log)
{
    public void Execute([ArgRange(0, 4096)] int index, [ArgRange(1, 64)] int slot)
    {
        if (index >= furniture.Rows.Count)
        {
            log.Warning($"[TF2Descent/Furniture] tf2_descent_furniture_use: no row {index} (the level has {furniture.Rows.Count})\n");
            return;
        }

        if (heroes.Living().FirstOrDefault(h => h.Slot == slot) is not { } hero)
        {
            log.Warning($"[TF2Descent/Furniture] tf2_descent_furniture_use: no living hero in slot {slot}\n");
            return;
        }

        _ = Report(index, slot, furniture.UseAs(furniture.Rows[index], hero));
    }

    private async Task Report(int index, int slot, Task<FurnitureService.UseOutcome> use) =>
        log.Msg($"[TF2Descent/Furniture] tf2_descent_furniture_use: row {index}, player {slot}: {await use}\n");
}

/// <summary>
/// <c>tf2_descent_furniture_hurt &lt;index&gt; &lt;amount&gt; &lt;player slot&gt;</c>: damage
/// on an explosive row through the path its entity's hits take (<see cref="FurnitureService.HurtRow"/>),
/// as if the player had struck it (slot 0: no hero) (dev).
/// </summary>
[ConCommand("tf2_descent_furniture_hurt", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_furniture_hurt <index> <amount> <player slot, 0 for none>: damage on an explosive row, as the player's hit")]
public sealed class FurnitureHurtCommand(FurnitureService furniture, IFurnitureHeroes heroes, ILog log)
{
    public void Execute([ArgRange(0, 4096)] int index, float amount, [ArgRange(0, 64)] int slot)
    {
        if (index >= furniture.Rows.Count || furniture.Rows[index] is not { Kind: Rules.Furniture.FurnitureKind.Explosive } row)
        {
            log.Warning($"[TF2Descent/Furniture] tf2_descent_furniture_hurt: row {index} is no explosive\n");
            return;
        }

        var hero = slot > 0 ? heroes.Living().FirstOrDefault(h => h.Slot == slot) : null;
        int left = furniture.HurtRow(row, hero?.Player ?? EntityHandle.None, amount);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Furniture] tf2_descent_furniture_hurt: row {index} took {amount:0.##} from {(hero is null ? "no hero" : "player " + slot)}: {row.StateText()}, engine health {left}\n"));
    }
}

/// <summary><c>tf2_descent_breakables</c>: the level's crates and barrels on their one entity, each slot's row and state (dev).</summary>
[ConCommand("tf2_descent_breakables", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_breakables: print the crates and barrels the breakables' entity holds, each slot's place and state")]
public sealed class BreakablesCommand(FurnitureBreakables breakables, ILog log)
{
    public void Execute() => log.Msg(breakables.Report());
}

/// <summary>
/// <c>tf2_descent_break &lt;slot&gt; [player slot]</c>: the breakables' slot is struck through the
/// path a hit on the entity takes (<see cref="FurnitureBreakables.Strike"/>), as the player's hit (0:
/// no hero) (dev).
/// </summary>
[ConCommand("tf2_descent_break", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_break <slot> [player slot, 0 for none]: strike the breakables' slot, as the player's hit")]
public sealed class BreakCommand(FurnitureBreakables breakables, IFurnitureHeroes heroes, ILog log)
{
    public void Execute([ArgRange(0, 4096)] int slot, [ArgRange(0, 64)] int player = 1)
    {
        var hero = player > 0 ? heroes.Living().FirstOrDefault(h => h.Slot == player) : null;
        bool struck = breakables.Strike(slot, hero?.Player ?? EntityHandle.None, 1f);
        log.Msg($"[TF2Descent/Breakables] tf2_descent_break: slot {slot} by {(hero is null ? "no hero" : "player " + player)}: {(struck ? "struck" : "not struck (no such slot, or broken)")}\n");
    }
}

/// <summary><c>tf2_descent_break_roll &lt;outcome|-&gt;</c>: the next crate or barrel broken rolls this outcome instead of its own (dev).</summary>
[ConCommand("tf2_descent_break_roll", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_break_roll <nothing|australium|potion|item|blast|cloud|ambush|->: the next break rolls this outcome (- for its own roll)")]
public sealed class BreakRollCommand(FurnitureService furniture, ILog log)
{
    public void Execute(string outcome)
    {
        if (outcome == "-")
        {
            furniture.ForceNext(null);
            log.Msg("[TF2Descent/Breakables] tf2_descent_break_roll: the next break rolls its own\n");
            return;
        }

        if (!Enum.TryParse<Rules.Furniture.BreakOutcome>(outcome, ignoreCase: true, out var o))
        {
            log.Warning($"[TF2Descent/Breakables] tf2_descent_break_roll: '{outcome}' is no outcome ({string.Join(", ", Enum.GetNames<Rules.Furniture.BreakOutcome>()).ToLowerInvariant()})\n");
            return;
        }

        furniture.ForceNext(o);
        log.Msg($"[TF2Descent/Breakables] tf2_descent_break_roll: the next break rolls {o.ToString().ToLowerInvariant()}\n");
    }
}
