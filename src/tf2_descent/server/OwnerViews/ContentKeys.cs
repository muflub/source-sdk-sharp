using TF2.Descent.Rules.Content;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.OwnerViews;

/// <summary>
/// The rows the owner's messages name by index (plan_descent_logic UI3), as
/// the rules hold them: item bases, affixes, skills, unusual effects,
/// uniques, attributes and equipment slots, each in table order. The client
/// reads the same tables from the bytes it embeds and checks the
/// <see cref="Fingerprint"/> the server sends with them.
/// </summary>
public static class ContentKeys
{
    /// <summary>The fingerprint of the indexed tables (<see cref="ContentFingerprint"/>).</summary>
    public static uint Fingerprint(DescentContent content) => ContentFingerprint.Of(
        content.ItemBases.Rows.Select(r => r.Id), content.Affixes.Rows.Select(r => r.Id),
        content.Skills.Rows.Select(r => r.Id), content.UnusualEffects.Rows.Select(r => r.Id),
        content.Uniques.Rows.Select(r => r.Id), content.Attributes.Rows.Select(r => r.Id),
        content.InventorySettings.Equipment.Select(r => r.Id));

    /// <summary>The index of the row <paramref name="id"/> in <paramref name="table"/>, or -1.</summary>
    public static int IndexOf<T>(Table<T> table, string? id) where T : IContentRow
    {
        if (id is null)
            return -1;
        var rows = table.Rows;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Id == id)
                return i;
        return -1;
    }
}
