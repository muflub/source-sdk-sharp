using SourceSharp.Abi;

namespace TF2.Server.Econ;

/// <summary>
/// Whether TF2's own inventory editing is open to a player. The loadout
/// screen's ask (<see cref="TFLoadoutServer"/>), its equip
/// (<c>tf2_equip</c>) and the grant (<c>tf2_give</c>) read and write the
/// <see cref="LocalInventory"/> directly. A further mod whose players' kit
/// comes from items of its own closes them for those players, so the screen
/// neither shows nor changes an inventory that no longer decides the kit.
///
/// <para>
/// A <b>CHAIN</b> (<see cref="IHooks{T}"/>, registered by
/// <see cref="TFEconRegistrar"/>): every gate is asked, and one closed answer
/// closes it. TF2 registers none, so in TF2's own game it is open. A gate
/// that throws is named on the log and closes nothing.
/// </para>
/// </summary>
public interface ITFLocalInventoryGate
{
    /// <summary>
    /// False closes <paramref name="entIndex"/>'s local inventory, with
    /// <paramref name="reason"/> for the log.
    /// </summary>
    bool IsOpen(int entIndex, out string reason);
}

/// <summary>The gates' one answer, for the readers of the local inventory (<see cref="ITFLocalInventoryGate"/>).</summary>
[SideService(Side.Server)]
public sealed class TFLocalInventoryAccess(IHooks<ITFLocalInventoryGate> gates, ILog log)
{
    /// <summary>Whether every gate leaves <paramref name="entIndex"/>'s local inventory open; the first closing gate's reason when not.</summary>
    public bool IsOpen(int entIndex, out string reason)
    {
        foreach (var gate in gates.All)
        {
            try
            {
                if (!gate.IsOpen(entIndex, out reason))
                    return false;
            }
            catch (Exception e)
            {
                log.Warning($"[TF2/Loadout] inventory gate {gate.GetType().FullName} threw: {e.Message}\n");
            }
        }

        reason = string.Empty;
        return true;
    }
}
