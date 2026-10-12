using SourceSharp.Client.Interop;

namespace SourceSharp.Client;

/// <summary>
/// The frame's bone batch table across <c>cs_cl_reload</c> (plan_physics_bones_perf
/// A2's caveat): native may hold the old build's table address until the next
/// frame's first BoneBatch ask, so the old table can be neither freed at the
/// reload nor left behind. The old build carries the table's address; the new
/// build adopts it (<see cref="ClientBoneBatch.Adopt"/>), answers native that
/// same address, and frees its own constructor's table, which native was never
/// handed. A section of its own, so a build that carried none (or a different
/// shape) is a missing or failed section, never a misread address.
/// </summary>
public sealed class ClientBoneBatchCarry(ClientSkeletons skeletons) : IReloadParticipant
{
    /// <summary>The section's first word: what says the next ints are a batch table.</summary>
    public const int Magic = 0x42425431; // "BBT1"

    /// <summary>Whether this build took a previous build's table over.</summary>
    public bool Adopted { get; private set; }

    public void Save(ReloadWriter writer)
    {
        ClientBoneBatch batch = skeletons.Batch;
        long address = batch.Address;
        (int entries, int matrices) = batch.Capacity;

        writer.Write(Magic);
        writer.Write((int)(address & 0xFFFFFFFF));
        writer.Write((int)(address >> 32));
        writer.Write(entries);
        writer.Write(matrices);
    }

    public void Restore(ReloadReader reader)
    {
        if (reader.ReadInt() != Magic)
            throw new ReloadFormatException("not a bone batch table");

        long address = (uint)reader.ReadInt() | ((long)reader.ReadInt() << 32);
        int entries = reader.ReadInt();
        int matrices = reader.ReadInt();

        Adopted = skeletons.Batch.Adopt((nint)address, entries, matrices);
    }
}
