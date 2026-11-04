using System.Globalization;
using System.Text;

using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Commands;

/// <summary>
/// <c>tf2_schema_dump [defindex|name]</c>: what the item schema parsed for one
/// item (stage1 1.4's live smoke), or, with no argument, what the load cost
/// and found. A dev reading, not a stock command; read-only.
/// </summary>
[ConCommand("tf2_schema_dump",
            Help = "tf2_schema_dump [defindex|name]: the parsed item schema entry, or the load's counts.")]
public sealed class SchemaDumpCommand(ItemSchema schema, ILog log)
{
    private IEnumerable<string> Names() => schema.Items.Select(i => i.Name);

    public void Execute([Complete(From = nameof(Names)), Rest] string item = "") => log.Msg(Describe(schema, item.Trim('"')));

    /// <summary>The text the command prints for <paramref name="arg"/>.</summary>
    public static string Describe(ItemSchema schema, string arg)
    {
        if (!schema.EnsureLoaded())
            return $"[TF2/Schema] not loaded: {schema.Failure}\n";

        if (arg.Length == 0)
            return $"[TF2/Schema] {ItemSchema.FilePath}: {schema.Stats}\n";

        var item = int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int defindex)
            ? schema.Item(defindex)
            : schema.Item(arg);
        if (item is null)
            return $"[TF2/Schema] no item {arg}\n";

        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.Append(inv, $"[TF2/Schema] {item.Defindex} {item.Name}\n");
        sb.Append(inv, $"  item_class {item.ItemClass ?? "(none)"}  item_name {item.ItemName}  quality {item.Quality}\n");
        sb.Append(inv, $"  baseitem {(item.BaseItem ? 1 : 0)}  enabled {(item.Enabled ? 1 : 0)}  hidden {(item.Hidden ? 1 : 0)}" +
                       $"  attach_to_hands {(item.AttachToHands ? 1 : 0)}  equip_type {item.EquipType}  item_slot {item.DefaultSlot}\n");

        sb.Append("  used_by_classes:");
        foreach (var cls in item.UsedByClasses)
            sb.Append(inv, $" {cls}={item.Slot(cls)}");
        sb.Append('\n');

        sb.Append(inv, $"  model_player {item.ModelPlayer ?? "(none)"}\n");
        foreach (var cls in item.UsedByClasses)
        {
            if (item.PlayerModel(cls) is { } model && model != item.ModelPlayer)
                sb.Append(inv, $"  model_player[{cls}] {model}\n");
        }

        sb.Append(inv, $"  attributes ({item.Attributes.Count}):\n");
        foreach (var attribute in item.Attributes)
        {
            var def = attribute.Definition;
            sb.Append(inv, $"    {def.Defindex} \"{def.Name}\" = {attribute}  [{def.AttributeClass ?? "-"}, {def.DescriptionFormat}]\n");
        }
        return sb.ToString();
    }
}
