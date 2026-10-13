using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

//=============================================================================
// ENGINE INTERFACES THROUGH GENERATED NATIVE FORWARDERS
// (plan_engine_forwarders.md).
//
// A forwarder table is an array of plain function pointers, each a generated
// native function that calls one engine method through the engine object's
// own vtable: `_fwd->GetModel(_self, index)`. The C++ compiler that built the
// forwarder knew which ABI it was building for, so nothing here does: there is
// no slot number, no destructor pair, no overload order and no Windows index.
//
// THE ONE CHECK, AT BIND. Native serves each table with its count and a hash
// per slot (src/fwd/native/ss_fwd.h); the generated struct carries the same
// numbers (EngineForwarders.g.cs). Both are written from one row by
// tools/engine-forwarders, so they differ only when the assembly and the
// native library beside it were built from different generations, and then
// the bind refuses, naming the slot. Nothing is checked per call.
//=============================================================================

/// <summary>
/// What a generated forwarder struct says about itself: the name native
/// serves it under, how many slots it has and each slot's hash and name.
/// </summary>
internal interface IForwarderTable
{
    static abstract string TableName { get; }

    static abstract int SlotCount { get; }

    static abstract ReadOnlySpan<uint> SlotHashes { get; }

    static abstract string SlotName(int slot);
}

/// <summary>
/// The table native serves (<c>SSForwarderTable</c>): the forwarders, their
/// hashes and their count, in that order.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ForwarderTableHeader
{
    public void** Slots;
    public uint* SlotHashes;
    public int SlotCount;
}

internal static unsafe class Forwarders
{
    /// <summary>
    /// <typeparamref name="TFwd"/>'s table from <paramref name="factory"/>'s
    /// internal factory, held against the struct's count and hashes; or null,
    /// with the reason.
    /// </summary>
    internal static bool TryAcquire<TFwd>(NativeInterfaceFactory factory, out TFwd* table, out string why)
        where TFwd : unmanaged, IForwarderTable
    {
        table = null;

        if (!factory.TryCreate(
                SourceSharpFactoryType.Internal, TFwd.TableName,
                out nint answer, out _, out string? error))
        {
            why = error!;
            return false;
        }

        return TryBind((ForwarderTableHeader*)answer, out table, out why);
    }

    /// <summary>
    /// The binding half of <see cref="TryAcquire{TFwd}"/>, over a header in
    /// hand: count first, then every slot's hash, then the table.
    /// </summary>
    internal static bool TryBind<TFwd>(ForwarderTableHeader* header, out TFwd* table, out string why)
        where TFwd : unmanaged, IForwarderTable
    {
        table = null;

        if (header == null || header->Slots == null || header->SlotHashes == null)
        {
            why = $"\"{TFwd.TableName}\" answered with no table";
            return false;
        }

        if (header->SlotCount != TFwd.SlotCount)
        {
            why = $"native serves \"{TFwd.TableName}\" with {header->SlotCount} forwarder(s) and "
                  + $"{typeof(TFwd).Name} declares {TFwd.SlotCount}: the assembly and the "
                  + "forwarder library beside it were generated apart. Rebuild both "
                  + "(tools/engine-forwarders --write regenerates the pair).";
            return false;
        }

        ReadOnlySpan<uint> expected = TFwd.SlotHashes;

        for (int slot = 0; slot < expected.Length; slot++)
        {
            if (header->SlotHashes[slot] != expected[slot])
            {
                why = $"\"{TFwd.TableName}\" slot {slot} ({TFwd.SlotName(slot)}) has hash "
                      + $"0x{header->SlotHashes[slot]:x8} in native and 0x{expected[slot]:x8} in "
                      + $"{typeof(TFwd).Name}: its signature differs between the forwarder "
                      + "library and this assembly. Rebuild both.";
                return false;
            }
        }

        table = (TFwd*)header->Slots;
        why = string.Empty;
        return true;
    }
}
