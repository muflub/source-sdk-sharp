using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// What a Sniper's own HUD shows of his rifle and his jar (plan_bots_teams_tf2 P7, the Sniper's §4.6 item 4): whether he
/// is scoped (<c>sniper.zoomed</c>, 1 or 0: the scope overlay), the rifle's charge in damage points 0..150
/// (<c>sniper.charge</c>: the charge bar beside the crosshair, whole points) and the jar's meter 0..1
/// (<c>sniper.jar</c>: the item effect bar under the health). The bot's own weapons only; nobody else's.
/// </summary>
[SideService(Side.Server)]
public sealed class TFSniperMeters(ITFOwnKit kits, IEnginePlayers players, IEntityDirectory directory, ITFPlayerConditions conditions,
                                   IWeaponGiver arsenal, IEngineEnvironment environment) : ITFHudMeters, IOrderedHook
{
    public const string Zoomed = "sniper.zoomed", Charge = "sniper.charge", Jar = "sniper.jar";

    /// <summary>The chain's order: the class's number by 100 (the Medic's medigun reader is the unordered 0), so each class's reader has its own.</summary>
    public int HookOrder => 100 * (int)TFClass.Sniper;

    /// <summary>The jar's recharge in seconds when its item names none: stock's 20.1 s (<see cref="TFItemMeter.EffectBarSeconds"/>).</summary>
    public static float JarSeconds => TFItemMeter.EffectBarSeconds(TFWeaponId.Jar);

    public void Read(EntityHandle bot, TFClass playerClass, List<TFMeter> into)
    {
        if (playerClass != TFClass.Sniper)
            return;
        int index = bot.EntryIndex;
        float now = environment.Globals.IsAvailable ? environment.Globals.CurTime : 0f;
        into.Add(new TFMeter(Zoomed, conditions.InCond(bot, TFCond.Zoomed) ? 1f : 0f));
        WeaponNetState? held = directory.Get(players.GetActiveWeaponEntity(bot)) is SsWeapon { State: { } s } ? s : null;
        int rifle = -1, jar = -1;
        foreach (TFOwnKitItem item in kits.KitOf(index))
        {
            if (item.WeaponKind < 0)
                continue;
            if (item.Slot == TFLoadoutSlot.Primary && TFSniperKit.IsRifle(item.ItemClass))
                rifle = item.WeaponKind;
            else if (item.ItemClass == TFSniperKit.JarClass)
                jar = item.WeaponKind;
        }

        into.Add(new TFMeter(Charge, held is not null && held.WeaponTypeId == rifle && rifle >= 0 ? ChargeShown(held, now) : 0f));
        if (jar < 0)
            return;
        if (held is not null && held.WeaponTypeId == jar)
        {
            into.Add(new TFMeter(Jar, Math.Clamp(held.ItemMeter, 0f, 1f)));
            return;
        }

        // Holstered: the meter it went away with and when, run on (the pair the next deploy's restore advances from).
        if (arsenal.CarriedMeterOf(bot, jar, out float meter, out float stamp))
            into.Add(new TFMeter(Jar, JarShown(meter, stamp, now)));
        else
            into.Add(new TFMeter(Jar, 1f));
    }

    /// <summary>The charge bar of a rifle in hand at <paramref name="now"/>, in whole damage points (the bar's own steps): stock's rate.</summary>
    public static float ChargeShown(WeaponNetState rifle, float now) => MathF.Floor(TFSniperRifleBehaviour.Charge(rifle, now));

    /// <summary>A holstered jar's bar: the meter it holstered with, run on since <paramref name="stamp"/> at the jar's recharge, held at full.</summary>
    public static float JarShown(float meter, float stamp, float now) => Math.Clamp(meter + Math.Max(0f, now - stamp) / JarSeconds, 0f, 1f);
}

/// <summary>The Sniper's kit as its play and its HUD reader name it.</summary>
public static class TFSniperKit
{
    public const string RifleClass = "tf_weapon_sniperrifle", JarClass = "tf_weapon_jar", SmgClass = "tf_weapon_smg", ChargedSmgClass = "tf_weapon_charged_smg";

    /// <summary>Whether <paramref name="itemClass"/> is a scoped rifle (the stock rifle, the Bazaar Bargain's, the Classic): not the Huntsman.</summary>
    public static bool IsRifle(string itemClass) => itemClass.StartsWith(RifleClass, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="itemClass"/> is a sub-machine gun (the SMG, the Cleaner's Carbine).</summary>
    public static bool IsSmg(string itemClass) => itemClass is SmgClass or ChargedSmgClass;
}
