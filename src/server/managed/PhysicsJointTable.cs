using System.Collections.Generic;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// Which of the three joint tables an id belongs to. The numbers are the
/// native id table's kinds (physics_shell.h's SS_PHYSID_KIND_*), so an id's
/// kind reads the same on both sides of the move.
/// </summary>
public enum PhysicsJointKind : byte
{
    None = 0,
    Constraint = 2,
    Spring = 3,
    Controller = 4,
}

/// <summary>
/// The ids managed's constraints, springs and motion controllers are handed
/// out under (plan_physics_bones_native_zero 2.4), moved from the native
/// shell's id table (physics_shell.h, plan_entity.md §3.8 step 3b) with its
/// rules unchanged:
///
/// <list type="bullet">
/// <item><description>AN ID WITH A SERIAL, not an index (<see cref="PhysicsId"/>).
/// Freeing a slot advances its serial, so every copy of the old id anybody
/// kept answers nothing rather than the slot's next occupant.</description></item>
/// <item><description>ONE ALLOCATOR, THREE KINDS. Every lookup names the kind it
/// expects, so a spring id handed to a constraint call answers nothing instead
/// of reaching a different table's row.</description></item>
/// <item><description>A freed slot goes to the head of the free list, as the
/// native table's did, so a create after a destroy reuses that slot with the
/// next serial.</description></item>
/// </list>
///
/// One instance per server composition, shared by <see cref="PhysicsConstraints"/>
/// and <see cref="PhysicsMotionControllers"/>.
/// </summary>
public sealed class PhysicsJointTable
{
    private struct Slot
    {
        public object? Entry;
        public uint Serial;
        public PhysicsJointKind Kind;
        public int NextFree;
    }

    private readonly Slot[] _slots = new Slot[PhysicsId.MaxSlots];
    private int _freeHead;

    public PhysicsJointTable()
    {
        for (int i = 0; i < _slots.Length; i++)
            _slots[i].NextFree = i + 1 < _slots.Length ? i + 1 : -1;
        _freeHead = 0;
    }

    /// <summary>Binds that found every slot taken.</summary>
    public int Refused { get; private set; }

    /// <summary>A new id for <paramref name="entry"/>, or 0 when the table is full.</summary>
    public uint Bind(PhysicsJointKind kind, object entry)
    {
        if (kind == PhysicsJointKind.None)
            return 0u;

        int index = _freeHead;
        if (index < 0)
        {
            Refused++;
            return 0u;
        }

        _freeHead = _slots[index].NextFree;
        ref Slot slot = ref _slots[index];
        slot.NextFree = -1;
        slot.Entry = entry;
        slot.Kind = kind;
        return PhysicsId.Make(index, slot.Serial).Value;
    }

    /// <summary>The entry an id names, when it is of <paramref name="kind"/> and its serial is current.</summary>
    public T? Lookup<T>(uint id, PhysicsJointKind kind) where T : class
    {
        var pid = new PhysicsId(id);
        int index = pid.Index;
        if (index < 0 || index >= _slots.Length)
            return null;

        ref Slot slot = ref _slots[index];
        if (slot.Kind != kind || slot.Kind == PhysicsJointKind.None || slot.Serial != pid.Serial)
            return null;

        return slot.Entry as T;
    }

    /// <summary>Frees the id's slot, advancing its serial. False for an id that names nothing.</summary>
    public bool Free(uint id, PhysicsJointKind kind)
    {
        if (Lookup<object>(id, kind) is null)
            return false;

        Release(new PhysicsId(id).Index);
        return true;
    }

    /// <summary>How many live entries are of <paramref name="kind"/>.</summary>
    public int CountOf(PhysicsJointKind kind)
    {
        int n = 0;
        foreach (ref readonly Slot slot in _slots.AsSpan())
        {
            if (slot.Kind == kind)
                n++;
        }
        return n;
    }

    /// <summary>Every live entry of <paramref name="kind"/>, with its id, in slot order.</summary>
    public IEnumerable<(uint Id, T Entry)> All<T>(PhysicsJointKind kind) where T : class
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Kind == kind && _slots[i].Entry is T entry)
                yield return (PhysicsId.Make(i, _slots[i].Serial).Value, entry);
        }
    }

    /// <summary>Frees every entry of <paramref name="kind"/>; answers how many.</summary>
    public int FreeAll(PhysicsJointKind kind)
    {
        int freed = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Kind != kind)
                continue;
            Release(i);
            freed++;
        }
        return freed;
    }

    /// <summary>
    /// Puts an entry back under the exact id it had (a reload's restore), so
    /// the ids the entities carried across it name it again. False when the
    /// id is malformed or its slot is taken.
    /// </summary>
    public bool Restore(uint id, PhysicsJointKind kind, object entry)
    {
        var pid = new PhysicsId(id);
        int index = pid.Index;
        if (kind == PhysicsJointKind.None || index < 0 || index >= _slots.Length
            || _slots[index].Kind != PhysicsJointKind.None)
            return false;

        Unlink(index);
        ref Slot slot = ref _slots[index];
        slot.Entry = entry;
        slot.Kind = kind;
        slot.Serial = pid.Serial;
        slot.NextFree = -1;
        return true;
    }

    private void Release(int index)
    {
        ref Slot slot = ref _slots[index];
        if (slot.Kind == PhysicsJointKind.None)
            return;

        slot.Serial = (slot.Serial + 1) & PhysicsId.SerialMask;
        slot.Entry = null;
        slot.Kind = PhysicsJointKind.None;
        slot.NextFree = _freeHead;
        _freeHead = index;
    }

    private void Unlink(int index)
    {
        if (_freeHead == index)
        {
            _freeHead = _slots[index].NextFree;
            return;
        }

        for (int i = _freeHead; i >= 0; i = _slots[i].NextFree)
        {
            if (_slots[i].NextFree == index)
            {
                _slots[i].NextFree = _slots[index].NextFree;
                return;
            }
        }
    }
}
