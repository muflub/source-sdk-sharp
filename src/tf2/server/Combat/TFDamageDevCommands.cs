using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Combat;

namespace TF2.Server.Combat;

/// <summary>
/// The damage layer's live stimuli (Stage 2.7), client commands for the
/// player who types them, behind <c>sv_cheats</c> as stock's hurt commands
/// are:
///
/// <para>
/// — <c>tf2_dev_burn [seconds]</c>: sets the player alight as stock's
///   <c>SelfBurn</c>-style callers do (<c>Burn( this, ... )</c>), from the
///   flamethrower, for the given flame life (default
///   <c>tf_afterburn_max_duration</c>); the afterburn then ticks through the
///   whole damage path.
/// </para>
/// <para>
/// — <c>tf2_dev_hurt &lt;damage&gt; [crit]</c>: one bullet hit with
///   <c>DMG_USEDISTANCEMOD</c> from the world on the player, critical when
///   asked, through the core's damage op and so the TF layer (no attacker:
///   no range, but the crit multiplier and the conditions apply).
/// </para>
/// Each prints what it did; <c>tf_debug_damage 1</c> prints what the layer
/// made of it.
/// </summary>
[SideService(Side.Server)]
public sealed class TFDamageDevCommands(IEngineDamage damage, DamageHitGroups hits, TFAfterburns afterburns,
                                        IGameClock clock, SourceSharp.Shared.SvCheatsConVar cheats, ILog log)
    : IClientCommandHook, IOrderedHook
{
    public const string Burn = "tf2_dev_burn";
    public const string Hurt = "tf2_dev_hurt";

    /// <summary>Stage 2.7's band (+700..+799 of TF2's thousand).</summary>
    public const int Order = 1_000_000 - 1_000 + 705;

    public int HookOrder => Order;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        bool burn = string.Equals(command, Burn, StringComparison.OrdinalIgnoreCase);
        bool hurt = string.Equals(command, Hurt, StringComparison.OrdinalIgnoreCase);
        if (!burn && !hurt)
            return false;

        if (!cheats.Value)
        {
            log.Warning($"[TF2/Damage] {command} needs sv_cheats 1\n");
            return true;
        }

        if (burn)
        {
            float seconds = args.Length > 0 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float s)
                ? s : TFBurn.MaxDuration;
            if (afterburns.Of(player.EntryIndex) is not { } afterburn)
            {
                log.Warning($"[TF2/Damage] {Burn}: {player} has no afterburn slice\n");
                return true;
            }

            afterburn.Ignite(clock.Now, player, TFWeaponId.Flamethrower, "tf_weapon_flamethrower", seconds);
            log.Msg($"[TF2/Damage] {Burn}: {player} alight for {afterburn.Burn.AfterburnDuration:F2} s\n");
            return true;
        }

        if (args.Length < 1 || !float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float amount))
        {
            log.Msg($"[TF2/Damage] {Hurt} <damage> [crit]\n");
            return true;
        }

        bool crit = args.Length > 1 && string.Equals(args[1], "crit", StringComparison.OrdinalIgnoreCase);
        log.Msg($"[TF2/Damage] {Hurt}: {amount:F2} to {player}{(crit ? ", critical" : "")}\n");
        using (hits.Push(HitGroup.Generic, crit))
            damage.Apply(player, EntityHandle.None, amount, DamageType.Bullet | TFDamageBits.UseDistanceMod);
        return true;
    }
}
