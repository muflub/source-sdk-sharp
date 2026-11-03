//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Entities.Items;

/// <summary>
/// The ammo half every TF pack shares (<c>CAmmoPack::MyTouch</c>, duplicated
/// in stock's <c>CHealthAmmoKit::MyTouch</c>): the pack's share of each
/// maximum, rounded up, of primary, secondary and metal, the spy's cloak by
/// the same fraction, and an engineer's grenades1.
/// </summary>
public static class TFAmmoPackRules
{
    /// <summary><c>TF_AMMOPACK_PICKUP_SOUND</c>.</summary>
    public const string PickupSound = "AmmoPack.Touch";

    /// <summary>
    /// Gives a pack of <paramref name="ratio"/>; true when anything was
    /// taken. The spy's knife meter and the demoman's
    /// <c>ammo_gives_charge</c> are not ported (docs/plans/tf2/deferred.tsv).
    /// </summary>
    public static bool Give(ITFMapPlayers players, EntityHandle player, float ratio)
    {
        bool success = false;
        success |= GiveShare(players, player, TFAmmo.Primary, ratio);
        success |= GiveShare(players, player, TFAmmo.Secondary, ratio);
        success |= GiveShare(players, player, TFAmmo.Metal, ratio);

        // :86-89 — a spy riding by gets cloak by the pack's own fraction, and
        // a cloak that moved is a pack taken even from a fully stocked player.
        success |= players.AddToSpyCloakMeter(player, 100f * ratio);

        if (players.ClassOf(player) == TFClass.Engineer)
            success |= GiveShare(players, player, TFAmmo.Grenades1, ratio);

        return success;
    }

    /// <summary><c>GiveAmmo( ceil( GetMaxAmmo( ammo ) * ratio ), ammo )</c>.</summary>
    public static bool GiveShare(ITFMapPlayers players, EntityHandle player, TFAmmo ammo, float ratio)
        => players.GiveAmmo(player, (int)MathF.Ceiling(players.MaxAmmo(player, ammo) * ratio), ammo) > 0;
}

/// <summary>item_ammopack_full (stock's <c>CAmmoPack</c>, entity_ammopack.cpp).</summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Item)]
public class AmmoPack(ITFMapPlayers players, IEngineFiles files, IEngineWorldQuery world, PlacedObjects placed,
        ITFPickupNotices notices,
                      ITFPlayerStats? stats = null)
    : TFPowerup(players, files, world, placed)
{
    public const string MapClass = "item_ammopack_full";

    /// <summary><c>CGameStats::Event_PlayerAmmokitPickup</c> (:127).</summary>
    protected readonly ITFPlayerStats? _stats = stats;

    public override PowerupSize Size => PowerupSize.Full;

    protected override string DefaultModel => "models/items/ammopack_large.mdl";

    /// <summary><c>GetAmmoPackName</c>: the <c>item_pickup</c> event's item.</summary>
    public virtual string AmmoPackName => "ammopack_large";

    protected override string Describe() => AmmoPackName;

    /// <summary><c>CAmmoPack::MyTouch</c>.</summary>
    protected override bool MyTouch(EntityHandle player)
    {
        if (!TFAmmoPackRules.Give(Players, player, PackRatio))
            return false;

        notices.Sound(player, TFAmmoPackRules.PickupSound);
        notices.ItemPickupEvent(player, AmmoPackName);
        _stats?.Increment(player, TFStat.Ammokits); // :127 — Event_PlayerAmmokitPickup
        return true;
    }
}

/// <summary>item_ammopack_small (<c>CAmmoPackSmall</c>).</summary>
[ScriptedEntity(SmallClass, ScriptedEntityKind.Item)]
public sealed class AmmoPackSmall(ITFMapPlayers players, IEngineFiles files, IEngineWorldQuery world, PlacedObjects placed,
        ITFPickupNotices notices,
                                 ITFPlayerStats? stats = null)
    : AmmoPack(players, files, world, placed, notices, stats)
{
    public const string SmallClass = "item_ammopack_small";

    public override PowerupSize Size => PowerupSize.Small;

    protected override string DefaultModel => "models/items/ammopack_small.mdl";

    public override string AmmoPackName => "ammopack_small";
}

/// <summary>item_ammopack_medium (<c>CAmmoPackMedium</c>).</summary>
[ScriptedEntity(MediumClass, ScriptedEntityKind.Item)]
public sealed class AmmoPackMedium(ITFMapPlayers players, IEngineFiles files, IEngineWorldQuery world, PlacedObjects placed,
        ITFPickupNotices notices,
                                   ITFPlayerStats? stats = null)
    : AmmoPack(players, files, world, placed, notices, stats)
{
    public const string MediumClass = "item_ammopack_medium";

    public override PowerupSize Size => PowerupSize.Medium;

    protected override string DefaultModel => "models/items/ammopack_medium.mdl";

    public override string AmmoPackName => "ammopack_medium";
}

/// <summary>
/// item_healthammokit (stock's <c>CHealthAmmoKit</c>, entity_healthkit.cpp):
/// a small health kit that is also a small ammo pack. The ammo pickup's sound
/// and event are announced only when no health was given, and the ammo half
/// skips a heavy picking up their own sandvich-kit (<c>:246</c>) — that
/// pickup refills only the grenades, as the health half's owner arm.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Item)]
public sealed class HealthAmmoKit(ITFMapPlayers players, IEngineFiles files, IEngineWorldQuery world, PlacedObjects placed,
        ITFPickupNotices notices,
                                  ITFWeaponOwners? owners = null, AttributeHooks? attributes = null,
                                  ITFPlayerStats? stats = null, TF2.Server.Combat.TFDamageRecords? records = null,
                                  ITFPlayerData? disguises = null, TFClassTable? classes = null)
    : HealthKit(players, files, world, placed, notices, owners, attributes, stats, records, disguises, classes)
{
    public new const string MapClass = "item_healthammokit";

    public override PowerupSize Size => PowerupSize.Small;

    protected override string DefaultModel => "models/items/medkit_medium.mdl";

    public override string HealthKitName => "healthammokit";

    protected override string ClassNameForPickup => MapClass;

    protected override bool MyTouch(EntityHandle player)
    {
        bool health = base.MyTouch(player);

        // :246 — filtering out heavies picking up their own sandvich: their
        // kit refills only the grenades, through the base's owner arm.
        bool ammo = false;
        if (!HeavyWithSandvich(player) || OwnerEntity != player)
            ammo = TFAmmoPackRules.Give(Players, player, PackRatio);

        if (!health && ammo)
        {
            Notices.Sound(player, TFAmmoPackRules.PickupSound);
            Notices.ItemPickupEvent(player, HealthKitName);
            _stats?.Increment(player, TFStat.Ammokits); // :295 — Event_PlayerAmmokitPickup
        }

        return health || ammo;
    }
}
