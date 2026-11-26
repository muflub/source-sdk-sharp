using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Econ;
using TF2.Shared.Player;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// What a Sniper's own HUD shows of his rifle and his jar (plan_bots_teams_tf2 P7, the Sniper's §4.6 item 4): whether he
/// is scoped (<c>sniper.zoomed</c>, 1 or 0: the scope overlay), the rifle's charge in damage points 0..150
/// (<c>sniper.charge</c>: the charge bar beside the crosshair, whole points), the jar's meter 0..1
/// (<c>sniper.jar</c>: the item effect bar under the health) and how long the bow in hand has been drawn, in seconds
/// (<c>sniper.draw</c>: the bow's charge bar fills over its first second; the hand on the trigger knows the rest). The bot's own only, each by its own handle: its networked
/// player state (the scope condition, the held-aside item meters its client is sent), its weapon in hand and its kit (read
/// again only when the kit's version moves). Read-only seams: nothing here can give, take or charge.
/// </summary>
[SideService(Side.Server)]
public sealed class TFSniperMeters(ITFOwnKit kits, IEnginePlayers players, IEntityDirectory directory, ITFPlayerData data,
                                   IEngineEnvironment environment) : ITFHudMeters, IOrderedHook
{
    public const string Zoomed = "sniper.zoomed", Charge = "sniper.charge", Jar = "sniper.jar", Draw = "sniper.draw";

    private readonly Dictionary<int, (int Version, int Rifle, int Jar, int Bow)> _kinds = new();

    /// <summary>The chain's order: 100 and the class's number (coordinator notice 1; the Medic's medigun reader is the unordered 0), so each class's reader has its own.</summary>
    public int HookOrder => 100 + (int)TFClass.Sniper;

    /// <summary>The jar's recharge in seconds when its item names none: stock's 20.1 s (<see cref="TFItemMeter.EffectBarSeconds"/>).</summary>
    public static float JarSeconds => TFItemMeter.EffectBarSeconds(TFWeaponId.Jar);

    public void Read(EntityHandle bot, TFClass playerClass, List<TFMeter> into)
    {
        if (playerClass != TFClass.Sniper)
            return;
        int index = bot.EntryIndex;
        float now = environment.Globals.IsAvailable ? environment.Globals.CurTime : 0f;
        TFPlayerNetState? own = data.Networked(index);
        bool zoomed = own is not null && (own.PlayerCond & (1 << (int)TFCond.Zoomed)) != 0;
        into.Add(new TFMeter(Zoomed, zoomed ? 1f : 0f));
        WeaponNetState? held = directory.Get(players.GetActiveWeaponEntity(bot)) is SsWeapon { State: { } s } ? s : null;
        (int rifle, int jar, int bow) = KindsOf(index);
        into.Add(new TFMeter(Charge, held is not null && rifle >= 0 && held.WeaponTypeId == rifle ? ChargeShown(held, now) : 0f));
        if (bow >= 0)
            into.Add(new TFMeter(Draw, held is not null && held.WeaponTypeId == bow ? DrawShown(held, now) : 0f));
        if (jar < 0)
            return;
        if (held is not null && held.WeaponTypeId == jar)
            into.Add(new TFMeter(Jar, Math.Clamp(held.ItemMeter, 0f, 1f)));
        else if (own is not null && own.CarriedSlotOf(jar) is >= 0 and var slot)
            // Holstered: the meter it went away with and when (the pair its client is sent), run on at the recharge.
            into.Add(new TFMeter(Jar, JarShown(own.ItemMeters[slot], own.ItemMeterTimes[slot], now)));
    }

    /// <summary>The kinds of the bot's scoped rifle, jar and bow (-1 for none), worked out again only when its kit's version moves.</summary>
    private (int Rifle, int Jar, int Bow) KindsOf(int index)
    {
        int version = kits.Version(index);
        if (_kinds.TryGetValue(index, out var held) && held.Version == version)
            return (held.Rifle, held.Jar, held.Bow);
        int rifle = -1, jar = -1, bow = -1;
        foreach (TFOwnKitItem item in kits.KitOf(index))
        {
            if (item.WeaponKind < 0)
                continue;
            if (item.Slot == TFLoadoutSlot.Primary && TFSniperKit.IsRifle(item.ItemClass))
                rifle = item.WeaponKind;
            else if (item.ItemClass == TFSniperKit.JarClass)
                jar = item.WeaponKind;
            else if (item.Slot == TFLoadoutSlot.Primary && item.ItemClass == TFSniperKit.BowClass)
                bow = item.WeaponKind;
        }

        _kinds[index] = (version, rifle, jar, bow);
        return (rifle, jar, bow);
    }

    /// <summary>How long a bow in hand has been drawn at <paramref name="now"/> (its draw's begin is the weapon's meter, TFBowBehaviour); 0 undrawn.</summary>
    public static float DrawShown(WeaponNetState bow, float now) => TFBowBehaviour.Drawn(bow) ? Math.Max(0f, now - bow.BehaviourMeter) : 0f;

    /// <summary>The charge bar of a rifle in hand at <paramref name="now"/>, in whole damage points (the bar's own steps): stock's rate.</summary>
    public static float ChargeShown(WeaponNetState rifle, float now) => MathF.Floor(TFSniperRifleBehaviour.Charge(rifle, now));

    /// <summary>A holstered jar's bar: the meter it holstered with, run on since <paramref name="stamp"/> at the jar's recharge, held at full.</summary>
    public static float JarShown(float meter, float stamp, float now) => Math.Clamp(meter + Math.Max(0f, now - stamp) / JarSeconds, 0f, 1f);
}

/// <summary>The Sniper's kit as its play and its HUD reader name it.</summary>
public static class TFSniperKit
{
    public const string RifleClass = "tf_weapon_sniperrifle", JarClass = "tf_weapon_jar", SmgClass = "tf_weapon_smg", ChargedSmgClass = "tf_weapon_charged_smg",
                        BowClass = "tf_weapon_compound_bow";

    /// <summary>Whether <paramref name="itemClass"/> is a scoped rifle (the stock rifle, the Bazaar Bargain's, the Classic): not the Huntsman.</summary>
    public static bool IsRifle(string itemClass) => itemClass.StartsWith(RifleClass, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="itemClass"/> is a sub-machine gun (the SMG, the Cleaner's Carbine).</summary>
    public static bool IsSmg(string itemClass) => itemClass is SmgClass or ChargedSmgClass;
}
