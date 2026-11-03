using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities.Items;
using TF2.Shared;

namespace TF2.Server.Buildings;

/// <summary>
/// The <c>sap</c> client command: the spy's sapper slot as an order, where
/// stock arms the placement on the builder PDA's
/// (<c>CTFWeaponBuilder::PrimaryAttack</c>) attack — the PDA and its
/// click-to-place preview are deferred rows, so the command stands in with
/// the build and destroy commands' shape.
/// </summary>
[SideService(Side.Server)]
public sealed class TFSapperCommand(ITFSapperState sappers, ITFMapPlayers players, ILog log)
    : IClientCommandHook, IOrderedHook
{
    public const string Verb = "sap";

    /// <summary>Stage 2.12's band, beside the build and destroy commands.</summary>
    public const int Order = 1_000_000 - 1_000 + 985;

    public int HookOrder => Order;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (!string.Equals(command, Verb, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!players.IsAlive(player))
        {
            log.Msg($"[TF2/Sapper] {player}: dead, cannot sap\n");
            return true;
        }

        // The sapper is the spy's: stock arms the placement only through the
        // tf_weapon_sapper in the spy's loadout, reached by its
        // CTFWeaponBuilder::PrimaryAttack (tf_weapon_builder.cpp:328-341).
        // No other class can be holding one, so the verb refuses them here —
        // the single carried weapon of this milestone has no slot to ask.
        if (players.ClassOf(player) != TFClass.Spy)
        {
            log.Msg($"[TF2/Sapper] {player}: the sapper is the spy's\n");
            return true;
        }

        if (!sappers.PlaceSapper(player.EntryIndex))
            log.Msg($"[TF2/Sapper] {player}: no enemy building in front to sap\n");

        return true;
    }
}
