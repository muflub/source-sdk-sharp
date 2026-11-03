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
public sealed class GiveItemCommand(LocalInventory inventory, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int player)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int defindex))
        {
            log.Warning("[TF2/Inventory] usage: tf2_give <player entindex> <defindex>\n");
            return;
        }

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
                                   ITFPlayerClasses? classes = null) : IConCommand
{
    public void Execute(string args) => log.Msg(Describe(args));

    /// <summary>The text the command prints for <paramref name="args"/>.</summary>
    public string Describe(string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int player))
            return "[TF2/Inventory] usage: tf2_loadout <player entindex> [class]\n";

        TFClass cls;
        if (parts.Length == 2)
        {
            int index = TFItemStrings.ClassIndex(parts[1]);
            if (index is < 1 or > 9)
                return $"[TF2/Inventory] no class {parts[1]} (scout .. engineer)\n";
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
