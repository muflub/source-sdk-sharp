//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities.Items;
using TF2.Server.Entities.Rooms;
using TF2.Server.Medigun;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Rules;

namespace TF2.Server.Player;

/// <summary>
/// The dropped weapon's pickup: <c>CTFPlayer::TryToPickupDroppedWeapon</c>
/// (tf_player.cpp:22789-22808) and <c>CanPickupDroppedWeapon</c>
/// (tf_player_shared.cpp:14742-14767) on the use key's action-slot verbs
/// (<c>use_action_slot_item_server</c> and its <c>+</c>/<c>−</c>,
/// tf_gamerules.cpp:15324-1548). A player who presses use takes the
/// <see cref="TFDroppedWeapon"/> their sight reaches — <c>GetDroppedWeapon-
/// InRange</c>'s eye ray at <see cref="PickupRange"/>, MASK_SOLID |
/// CONTENTS_DEBRIS — and puts down what their hands hold in that weapon's
/// loadout slot: the held one goes to the floor as a new dropped weapon
/// (<c>PickupWeaponFromOther</c>, tf_player.cpp:22693-22784, the
/// <see cref="TFDroppedWeapon.CleanUp"/> head and <c>InitDroppedWeapon</c>'s
/// bSwap), the picked-up one is removed and carries its clip, reserve and
/// charge over (<c>InitPickedUpWeapon</c>, tf_dropped_weapon.cpp:618-640).
///
/// <para>
/// The stock members this port does not carry are the ones whose state the
/// port does not keep: the stealth-expire and feign-ready arms of
/// <c>CanAttack</c> (the stealth clock's expiry and the Dead Ringer's armed
/// latch), the CYOA and passtime and kart arms and the no-attack attribute
/// (modes and econ this build does not run), the verb's use-rate limit
/// (tf_gamerules.cpp:15326, no rate-limit infra), the picked-up weapon's
/// next-attack restore (the dropped weapon keeps no attack clock of its own
/// here), and the builder's sub-type (no builder items on this schema). In
/// the respawn room the held weapon is never thrown — stock throws only when
/// the held item is the player's own account, and every item here is
/// accountless, account 0 (tf_player.cpp:22732-22747).
/// </para>
/// </summary>
public interface ITFDroppedWeaponPickup
{
    /// <summary><c>CanPickupDroppedWeapon</c>: every gate but the reach.</summary>
    bool CanPickup(EntityHandle player, TFDroppedWeapon weapon);

    /// <summary><c>TryToPickupDroppedWeapon</c>: the reach, the swap and the removal.</summary>
    bool TryPickup(EntityHandle player);

    /// <summary>The key went down (<c>Pressed</c>): one pickup per hold.</summary>
    void Press(EntityHandle player);

    /// <summary>The key came up (<c>Released</c>).</summary>
    void Release(EntityHandle player);
}

/// <summary>The pickup's service, the <see cref="ITFDroppedWeaponPickup"/> answer.</summary>
[SideService(Side.Server, As = [typeof(ITFDroppedWeaponPickup)])]
public sealed class TFDroppedWeaponPickup(
    Arsenal arsenal, ITFMapPlayers players, IEngineEntities entities, IEngineEntityRender render,
    IEngineWorldQuery world, IEntityDirectory directory, IGameClock clock, ITFMedigunWeapons medigun,
    TFWeaponScripts scripts, ItemSchema schema, TFClassTable classes,
    ITFRespawnRooms? rooms = null, ITeamRoundState? rounds = null)
    : ITFDroppedWeaponPickup
{
    /// <summary><c>TF_WEAPON_PICKUP_RANGE</c> (tf_shareddefs.h:2697): 150.</summary>
    public const float PickupRange = 150f;

    // The key is down: one pickup per hold, stock's m_bUsingActionSlot guard
    // on the + verb (tf_gamerules.cpp:15336).
    private readonly HashSet<int> _pressing = [];

    public void Press(EntityHandle player)
    {
        if (!_pressing.Add(player.EntryIndex))
            return; // :15336, still held from the last press

        // Stock's Pressed also offers the action-slot item — the taunt and
        // the payload radar; those are their own lanes' verbs. The pickup
        // (:22789) is this service's share.
        TryPickup(player);
    }

    public void Release(EntityHandle player) => _pressing.Remove(player.EntryIndex);

    public bool TryPickup(EntityHandle player)
    {
        // CanAttack's arms the pickup inherits (:22791 → :12245-12274): the
        // cloak, the taunt, the phase and the losing team of a won round.
        if (players.InCond(player, TFCond.Stealthed) || players.InCond(player, TFCond.Taunting)
            || players.InCond(player, TFCond.Phase) || Loser(player))
            return false;

        // The shot already on its way holds the use (:22794): the hands'
        // next trigger time in the future.
        if (directory.Get(arsenal.CarriedPair(player).Weapon) is SsWeapon hands
            && hands.State.NextPrimaryAttack > clock.Now)
            return false;

        if (InRange(player) is not { } dropped)
            return false;

        // The mark: our registry's removal is synchronous, so a weapon this
        // frame has not yet removed cannot exist to be re-picked (:22798).

        return Swap(player, dropped);
    }

    public bool CanPickup(EntityHandle player, TFDroppedWeapon weapon)
        => TheWeapon(player, weapon) is not null;

    /// <summary>
    /// The gates of <see cref="CanPickupDroppedWeapon"/> as data: the item
    /// and the held row that would go down for it, null at the first refused
    /// (tf_player_shared.cpp:14744-14766, in stock's order).
    /// </summary>
    private (ItemDefinition Item, string Held)? TheWeapon(EntityHandle player, TFDroppedWeapon weapon)
    {
        TFClass cls = players.ClassOf(player);
        ItemDefinition? item = ItemOf(weapon.Weapon, cls);
        if (item is null)
            return null; // :14744, no item behind the lie

        // The spy's wear: the disguise's costume and the cloak's shimmer are
        // both hands-off (:14748).
        if (cls == TFClass.Spy
            && (players.InCond(player, TFCond.Disguised) || players.InCond(player, TFCond.Stealthed)))
            return null;

        if (players.InCond(player, TFCond.Taunting)) // :14751
            return null;

        if (!players.IsAlive(player)) // :14754
            return null;

        // The hands' own say (:14760): a weapon is in them and it takes a
        // stranger's — every row here can, stock's default-true
        // CanPickupOtherWeapon — so the hands must only be full.
        if (arsenal.CarriedPair(player).Weapon == EntityHandle.None)
            return null;

        // The slot's swap partner :14763-64: the weapon this class carries in
        // the dropped item's loadout slot. None, no pickup — the assert's
        // "no weapon to put down" (:22705).
        TFLoadoutSlot slot = item.Slot(cls);
        string? held = HeldInSlot(player, cls, slot);
        if (held is null)
            return null;

        // And the item must dress this class in a slot the pickup takes
        // (:14766, CanBeUsedByClass and IsValidPickupWeaponSlot — primary,
        // secondary, melee; tf_item_constants.h:152).
        if (!item.UsedBy(cls)
            || slot is not (TFLoadoutSlot.Primary or TFLoadoutSlot.Secondary or TFLoadoutSlot.Melee))
            return null;

        return (item, held);
    }

    /// <summary>
    /// <c>GetDroppedWeaponInRange</c> (tf_player_shared.cpp:14773-14794): the
    /// eye's ray to <see cref="PickupRange"/>, the lie it ends on, the gates,
    /// and the reach — the eye to the lie's origin, again
    /// <see cref="PickupRange"/> squared (:14790).
    /// </summary>
    private TFDroppedWeapon? InRange(EntityHandle player)
    {
        Vector eye = entities.GetEyePosition(player);
        Vector reach = eye + Angles.Forward(entities.GetEyeAngles(player)) * PickupRange; // :14777, :14780
        TraceResult trace = world.TraceLine(eye, reach, TraceMask.SolidDebris, player);

        if (directory.Get(trace.Entity) is not TFDroppedWeapon dropped
            || !CanPickup(player, dropped)) // :14782-87
            return null;

        Vector toWeapon = entities.GetOrigin(dropped.Handle) - eye;
        return toWeapon.LengthSquared > PickupRange * PickupRange ? null : dropped; // :14790
    }

    /// <summary>
    /// <c>PickupWeaponFromOther</c> (tf_player.cpp:22693-22784): the held
    /// weapon goes down as a new lie of its own, the picked-up one's clip,
    /// reserve and charge come across, and the hands take the new weapon.
    /// </summary>
    private bool Swap(EntityHandle player, TFDroppedWeapon dropped)
    {
        if (TheWeapon(player, dropped) is not { } found)
            return false;

        string heldName = found.Held;

        // What the throw would say about the weapon leaving: the clip the
        // hands hold (the live blob's, which <see cref="Arsenal.Holdings"/>
        // settles), the pool it feeds, the charge a medigun carries.
        int heldClip = arsenal.Holdings(player).First(h => h.ClassName == heldName).Clip;
        string? heldAmmoName = arsenal.Weapons.For(heldName)?.AmmoName;
        int heldAmmo = heldAmmoName is { Length: > 0 } ? arsenal.Pools(player).GetValueOrDefault(heldAmmoName) : 0;
        float heldCharge = scripts.Get(heldName)?.Id == TFWeaponId.Medigun
            ? medigun.ChargeLevel(player.EntryIndex) : 0f;
        int heldSkin = (TFTeam)players.Team(player) == TFTeam.Blue ? 1 : 0; // GetSkin's team fallback

        // The hands give the row up before the new one arrives — the reverse
        // of stock's give-then-drop, whose order only serves the same-named
        // weapon a second GiveNamedItem would duplicate; the row is gone
        // either way once both have run (:22703-22759).
        if (!arsenal.TakeWeapon(player, heldName))
            return false;

        if (!arsenal.GiveWeapon(player, dropped.Weapon))
        {
            // The item existed and the weapon row did not — the loadout that
            // granted the row cannot have lied; put back what was taken and
            // refuse.
            arsenal.GiveWeapon(player, heldName);
            return false;
        }

        // The held weapon to the floor, unless the respawn room keeps it
        // (:22732-22747): every item here is accountless, so stock's
        // own-account arm always true in the room — bShouldThrowHeldWeapon
        // false — and the room is the one place the floor stays clear. The
        // origin is the player's (:22728-30 stock computes the ammo-pack
        // pose; the placement is the death drop's deferred row 126's).
        if (rooms?.InRespawnRoom(player, sameTeamOnly: false) != true)
        {
            TFDroppedWeapon.CleanUp(directory, player, heldName);

            var thrownHandle = entities.Create(TFDroppedWeapon.Class);
            if (entities.IsValid(thrownHandle))
            {
                entities.SetOrigin(thrownHandle, entities.GetOrigin(player));
                if (arsenal.Weapons.For(heldName)?.WorldModel is { Length: > 0 } worldModel)
                    render.SetModel(thrownHandle, worldModel);

                entities.Spawn(thrownHandle);

                // (:22751-55, InitDroppedWeapon's bSwap.)
                if (directory.Get(thrownHandle) is TFDroppedWeapon thrown)
                    thrown.Init(player, heldName, heldClip, heldAmmo,
                                chargeLevel: heldCharge, skin: heldSkin, thrown: true);
            }
        }

        // What crosses (:618-640, InitPickedUpWeapon): the clip whole; the
        // reserve set, not added, to the lesser of the lie's count and the
        // carry limit; the engineer's metal what it was before the reserve
        // spoke (:624-33); the medigun's meter (:636-40). The next-attack
        // clocks and the charge meter keep as they were — the lie carries
        // none of them here.
        arsenal.SetClip(player, dropped.Weapon, dropped.Clip);

        string? ammoName = arsenal.Weapons.For(dropped.Weapon)?.AmmoName;
        if (ammoName is { Length: > 0 })
        {
            // The set, not an add (:629): the pool empties and takes the
            // lie's count, capped by GiveAmmo's own carry limit — stock's
            // Min(m_nAmmo, nMaxTotalAmmo).
            IReadOnlyDictionary<string, int> pools = arsenal.Pools(player);
            int metalBefore = pools.GetValueOrDefault(TFAmmoNames.Name(TFAmmo.Metal));

            pools.TryGetValue(ammoName, out int had);
            arsenal.TakeAmmo(player, had, ammoName);
            arsenal.GiveAmmo(player, dropped.Ammo, ammoName);

            int metalNow = arsenal.Pools(player).GetValueOrDefault(TFAmmoNames.Name(TFAmmo.Metal));
            if (metalNow != metalBefore)
            {
                // The reserve set took the metal with it (:631-33): give
                // back what the clobber took, up to the same limit rule.
                if (metalNow < metalBefore)
                    arsenal.GiveAmmo(player, metalBefore - metalNow, TFAmmoNames.Name(TFAmmo.Metal));
                else
                    arsenal.TakeAmmo(player, metalNow - metalBefore, TFAmmoNames.Name(TFAmmo.Metal));
            }
        }

        if (scripts.Get(dropped.Weapon)?.Id == TFWeaponId.Medigun)
            medigun.SetChargeLevel(player.EntryIndex, dropped.ChargeLevel);

        // The hands: the give deployed into empty ones; the holstered case
        // keeps the weapon that was active, stock's Weapon_CanSwitchTo
        // passing (:22769-22774 — the next-best walk is for the refusal
        // this port's arms never raise). The lie itself goes (:22802).
        dropped.Remove();

        // The pickup message waits a tenth of a second (:22777) — a client
        // side's nicety, no receiver on this arm.
        return true;
    }

    /// <summary>
    /// The item behind a dropped weapon's name, for the class picking it up:
    /// stock reads it off the lie's <c>m_Item</c>, whose defindex the schema
    /// carries. The walk takes the item whose class is the name, or whose
    /// class <see cref="TFItemClassTranslation.Translate"/>s to it — the
    /// translation is how one item became this class's entity
    /// (tf_player.cpp:22713).
    /// </summary>
    private ItemDefinition? ItemOf(string weapon, TFClass cls)
    {
        if (cls < TFClass.Scout || cls >= TFClass.CountAll)
            return null;

        foreach (ItemDefinition item in schema.Items)
        {
            if (item.ItemClass is not { Length: > 0 } itemClass)
                continue;

            if (string.Equals(TFItemClassTranslation.Translate(itemClass, classes[cls]), weapon,
                              StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }

    /// <summary>The player's own weapon in a loadout slot, through each holding's item (:14764).</summary>
    private string? HeldInSlot(EntityHandle player, TFClass cls, TFLoadoutSlot slot)
    {
        foreach (var (name, _, _) in arsenal.Holdings(player))
        {
            if (ItemOf(name, cls)?.Slot(cls) == slot)
                return name;
        }

        return null;
    }

    /// <summary><c>CTFPlayerShared::IsLoser</c> (:13673): the round won and not by the player's team.</summary>
    private bool Loser(EntityHandle player)
        => rounds is { } state && state.State == RoundState.TeamWin
           && state.WinningTeam != (TFTeam)players.Team(player);
}

/// <summary>
/// The action-slot verbs themselves (tf_gamerules.cpp:15324-1548): the plain
/// <c>use_action_slot_item_server</c>, its <c>+</c> press and its <c>−</c>
/// release, each answered once and claimed. The verb's own use-rate limit is
/// the pickup service's to keep or drop (see <see cref="TFDroppedWeaponPickup"/>).
/// </summary>
[SideService(Side.Server)]
public sealed class TFUseActionSlotCommand(ITFDroppedWeaponPickup pickups) : IClientCommandHook, IOrderedHook
{
    public const string Verb = "use_action_slot_item_server";

    /// <summary>The pickup's order, <see cref="TFPlayerOrders.UseActionSlot"/>.</summary>
    public const int Order = TFPlayerOrders.UseActionSlot;

    public int HookOrder => Order;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        switch (command)
        {
            case Verb: // :15324-31, the tap: pressed and released together.
                pickups.Press(player);
                pickups.Release(player);
                return true;

            case "+" + Verb: // :15332-41, the hold's downstroke.
                pickups.Press(player);
                return true;

            case "-" + Verb: // :15343-48, the hold's upstroke.
                pickups.Release(player);
                return true;
        }

        return false;
    }
}
