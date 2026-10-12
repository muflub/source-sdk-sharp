using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// One entity's entry in the native control block.
///
/// <para>
/// MUST match SourceSharpEntitySlot_t in host/sourcesharp.h. This is mapped
/// memory, not a copy: a state object holds a pointer to one of these for the
/// life of its binding, so the layout is the ABI and a field added on one side
/// and not the other reads the next entity's serial.
/// </para>
///
/// <para>
/// EACH WORD HAS EXACTLY ONE WRITER. Native writes <see cref="Serial"/> when it
/// opens and closes a slot; C# writes <see cref="Dirty"/> when it writes a
/// field, and native clears it once per frame as it drains. Neither writes the
/// other's, and both run on the game thread — which is the whole of why this is
/// safe without a lock, and the first thing that breaks if managed work is ever
/// moved off that thread.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct EntitySlot
{
    public uint Serial;
    public uint Dirty;
}
