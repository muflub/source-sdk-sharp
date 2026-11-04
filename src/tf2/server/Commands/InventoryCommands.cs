using System.Globalization;
using System.Text;

using SourceSharp.Abi;

using TF2.Server.Econ;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Commands;

/// <summary>
/// <c>tf2_give &lt;player&gt; &lt;defindex&gt;</c>: grants an item into a
/// player's local inventory (stage1 1.6; dev, cheat-flagged: there is no GC
/// to say no). The player is the client's entity index.
/// </summary>
[ConCommand("tf2_give", Help = "tf2_give <player entindex> <defindex>: grant an item into the local inventory.",
            Flags = ConVarFlags.Cheat)]
public sealed class GiveItemCommand(LocalInventory inventory, ILog log)
{
    public void Execute(int player, int defindex)
    {
        if (inventory.Grant(player, defindex) is { } item)
            log.Msg($"[TF2/Inventory] granted {item} to client {player}\n");
    }
}

/// <summary>
/// <c>tf2_loadout &lt;player&gt; [class]</c>: the items a class spawns with for
/// a player, slot by slot, owned or base. Without a class it asks the
/// registered <see cref="ITFPlayerClasses"/>.
/// </summary>
[ConCommand("tf2_loadout", Help = "tf2_loadout <player entindex> [class]: the class loadout, slot by slot.")]
public sealed class LoadoutCommand(TFLoadoutProvider loadout, LocalInventory inventory, ILog log,
                                   ITFPlayerClasses? classes = null)
{
    public void Execute(int player, [Complete("scout", "sniper", "soldier", "demoman", "medic", "heavy", "pyro", "spy", "engineer")] string? className = null) => log.Msg(Describe(player, className));

    /// <summary>The text the command prints.</summary>
    public string Describe(int player, string? className = null)
    {
        TFClass cls;
        if (className is not null)
        {
            int index = TFItemStrings.ClassIndex(className);
            if (index is < 1 or > 9)
                return $"[TF2/Inventory] no class {className} (scout .. engineer)\n";
            cls = (TFClass)index;
        }
        else
        {
            cls = classes?.ClassOf(new EntityHandle((uint)player)) ?? TFClass.Undefined;
        }

        var sb = new StringBuilder();
        string owner = inventory.Of(player) is { } owned ? $"{owned.Items.Count} owned" : "no active inventory";
        sb.Append(CultureInfo.InvariantCulture, $"[TF2/Inventory] client {player} as {cls} ({owner}):\n");
        foreach (var entry in loadout.Loadout(player, cls))
            sb.Append(CultureInfo.InvariantCulture, $"  {entry.Slot}: {entry.Item} ({entry.Item.Definition.ItemClass})\n");
        return sb.ToString();
    }
}
