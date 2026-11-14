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
