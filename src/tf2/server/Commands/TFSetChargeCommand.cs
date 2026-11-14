using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Medigun;

namespace TF2.Server.Commands;

/// <summary>
/// <c>tf2_setcharge &lt;0..1&gt;</c>: the client command that stages the
/// issuing medic's ÜberCharge meter. Stock keeps <c>SetChargeLevel</c> public
/// for exactly this kind of dev and HUD-side caller
/// (tf_weapon_medigun.cpp, the setter's clamp is the meter's own); the
/// console had no way to reach it, and every reading that hangs off the
/// meter — the Overdose's speed, the HUD's bar — wants one. The level goes
/// through the medigun service's publish, so the carrier mirror and the net
/// state move with it as on any real gain.
/// </summary>
[SideService(Side.Server)]
public sealed class TFSetChargeCommand(ITFMedigunWeapons mediguns, SourceSharp.Shared.SvCheatsConVar cheats, ILog log)
    : IClientCommandHook, IOrderedHook
{
    public const string Verb = "tf2_setcharge";

    /// <summary>A stage of its own in TF2's thousand, beside the equip's (+90).</summary>
    public const int Order = 1_000_000 - 1_000 + 91;

    public int HookOrder => Order;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (!string.Equals(command, Verb, StringComparison.OrdinalIgnoreCase))
            return false;

        // A cheat, as addcond is: without sv_cheats any player could fill
        // their own uber.
        if (!cheats.Value)
            return true;

        if (args.Length < 1 || !float.TryParse(args[0], CultureInfo.InvariantCulture, out float level))
        {
            log.Warning("[TF2/Medigun] tf2_setcharge takes a meter level, 0..1\n");
            return true;
        }

        // The setter clamps into the meter's 0..1 and publishes; the level is
        // the whole command.
        mediguns.SetChargeLevel(player.EntryIndex, level);
        return true;
    }
}
