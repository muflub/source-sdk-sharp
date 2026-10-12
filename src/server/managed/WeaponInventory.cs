using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// A player's inventory, read as untyped entity handles.
///
/// <para>
/// The managed half of Phase 10's handle work: m_hMyWeapons is a
/// CBaseEntity-typed array on the native player, and until the crossing landed
/// a parallel-hierarchy weapon could not safely sit in it — nor could C# ask
/// what a slot held without a CBaseCombatWeapon in the question. These reads
/// go handle-first, so they survive the weapons leaving that class.
/// </para>
///
/// <para>
/// Separate from the command that prints it, so what the lines SAY is
/// testable: the command is a console name and a write, and this is
/// everything that decides the content.
/// </para>
/// </summary>
public static class WeaponInventory
{
    /// <summary>One occupied inventory slot, as facts rather than pointers.</summary>
    public readonly record struct Slot(int Index, uint Handle, string ClassName, bool Active);

    /// <summary>How many slots the native array carries — MAX_WEAPONS.</summary>
    public const int SlotCount = 48;

    /// <summary>
    /// Reads the occupied slots of one player. Empty slots are skipped: the
    /// array is sparse and 45 lines of "nothing" would bury the three that
    /// answer anything.
    /// </summary>
    public static List<Slot> Read(IEnginePlayers players, IEngineEntities entities, EntityHandle player)
    {
        var slots = new List<Slot>();
        EntityHandle active = players.GetActiveWeaponEntity(player);

        for (int i = 0; i < SlotCount; i++)
        {
            EntityHandle weapon = players.GetWeaponEntity(player, i);

            // None is what the native side answers for an empty slot; checked
            // by value rather than by IsValid so this read has no second
            // engine call per slot and stays honest under a fake in tests.
            if (weapon == EntityHandle.None)
                continue;

            slots.Add(new Slot(i, weapon.Value, entities.GetClassName(weapon), weapon == active));
        }

        return slots;
    }

    /// <summary>
    /// The report, one line per occupied slot plus one for the view model.
    /// The handle is printed because the handle IS the point: it is the name
    /// managed code addresses the weapon by, and a listing that only gave
    /// classnames would be the old string-shaped API again.
    /// </summary>
    public static List<string> Report(IReadOnlyList<Slot> slots, uint viewModelHandle)
    {
        var lines = new List<string>();

        if (slots.Count == 0)
        {
            lines.Add("  no weapons");
        }
        else
        {
            foreach (var slot in slots)
            {
                lines.Add($"  slot {slot.Index,2}: {slot.ClassName}  handle 0x{slot.Handle:X8}{(slot.Active ? "  (active)" : "")}");
            }
        }

        lines.Add(viewModelHandle != EntityHandle.None.Value
            ? $"  viewmodel: handle 0x{viewModelHandle:X8}"
            : "  viewmodel: none");

        return lines;
    }
}
