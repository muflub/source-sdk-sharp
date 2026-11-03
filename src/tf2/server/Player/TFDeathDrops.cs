//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities.Items;
using TF2.Server.Medigun;
using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// What a TF2 death drops: the weapon itself, and the ammo pack beside it.
///
/// <para>
/// The death-drop half of <c>CTFPlayer::Event_Killed</c> —
/// <c>DropAmmoPack</c> (tf_player.cpp:13029-13112) behind its
/// <c>ShouldDropAmmoPack</c> gate (:13015-13024), and the
/// <c>AmmoPackCleanUp</c> (:12975-13010) that keeps one owner to three packs.
/// Stock's death never drops a weapon-classname entity; the core's
/// <see cref="IPlayerDirectory.DropWeaponOnDeath"/> is the HL2MP answer, and
/// TF2 owns the drop, so <see cref="IGameLifecycle.OnLevelInit"/> turns it
/// off and this class makes the stock drop instead: a
/// <see cref="TFDroppedWeapon"/> wearing the dying weapon's worldmodel
/// (:13096-13099), and the <see cref="TFAmmoPack"/> (:13103-13107) carrying
/// the dead player's ammo snapshot — both where they fell, in stock's order.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFDeathDrops(ILog log, Arsenal arsenal, TFWeaponScripts scripts, ITFMapPlayers mapPlayers,
                                 IEntityDirectory directory, IPlayerDirectory records, IEngineEntities entities,
                                 IEngineEntityRender render, ITFMedigunWeapons medigun)
    : IPlayerDeathHook, IGameLifecycle, IOrderedHook
{
    /// <summary>The drop's order in the player band.</summary>
    public const int Order = TFPlayerOrders.DeathDrops;

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    int IGameLifecycle.LifecycleOrder => Order;

    /// <inheritdoc />
    void IGameLifecycle.OnLevelInit(string mapName)
    {
        // Stock's death never drops the weapon itself (Event_Killed → DropAmmoPack,
        // tf_player.cpp:13103), so the core's HL2MP answer is off for the level.
        records.DropWeaponOnDeath = false;
    }

    /// <inheritdoc />
    public void OnPlayerDied(PlayerDeath death)
    {
        // ShouldDropAmmoPack's gates (:13017-13021) ask the MvM and arena modes,
        // deferred with them (docs/plans/tf2/deferred.tsv): nothing to ask here.

        // The dress pick (DropAmmoPack :13033-13068): the active weapon when it
        // may dress the pack, else the heaviest droppable row of the inventory
        // walk. The scan starts iWeight at -1 and compares with a strict >, so
        // an unweighted (0) row still wins when nothing heavier exists, and
        // equal weights go to whoever the walk meets first (:13043-13059). A
        // row with no script: stock's GetTFWpnData() is never missing, so the
        // port reads its defaults — droppable, weight 0, id None.
        var holdings = arsenal.Holdings(death.Player);
        string? activeName = null;
        foreach (var (name, _, active) in holdings)
        {
            if (active)
            {
                activeName = name;
                break;
            }
        }

        string? pickedName = null;
        TFWeaponScript? pickedScript = null;
        int pickedClip = 0;
        if (activeName is not null && !(scripts.Get(activeName)?.DontDrop ?? false))
        {
            pickedName = activeName;   // :13063
            pickedScript = scripts.Get(activeName);
            pickedClip = holdings.First(h => h.ClassName == activeName).Clip;
        }
        else
        {
            int weight = -1; // :13040
            foreach (var (name, clip, _) in holdings)
            {
                var candidate = scripts.Get(name);
                if (candidate?.DontDrop ?? false)
                    continue; // :13049-13050
                int thisWeight = candidate?.Weight ?? 0; // :13052
                if (thisWeight > weight)                 // :13054
                {
                    weight = thisWeight;
                    pickedName = name;
                    pickedScript = candidate;
                    pickedClip = clip;
                }
            }
        }

        if (pickedName is null)
            return; // :13066-13068: didn't find one, bail

        // The worldmodel gate (:13084-13085): a dress weapon with no worldmodel
        // ends the drop entirely — the pack never spawns either, stock's one
        // return serving both creates.
        string? worldModel = arsenal.Weapons.For(pickedName)?.WorldModel;
        if (string.IsNullOrEmpty(worldModel))
            return;

        // The snapshot the pack stores (:13107 → tf_ammo_pack.cpp:137-139) and
        // the team read that decides the holiday-draw arm (:13094).
        var pools = arsenal.Pools(death.Player);
        int primary = pools.GetValueOrDefault(TFAmmoNames.Name(TFAmmo.Primary));
        int secondary = pools.GetValueOrDefault(TFAmmoNames.Name(TFAmmo.Secondary));
        int metal = pools.GetValueOrDefault(TFAmmoNames.Name(TFAmmo.Metal));
        bool isSuicide = death.Attacker != EntityHandle.None
                      && mapPlayers.Team(death.Attacker) == mapPlayers.Team(death.Player); // :13094

        // The dress's skin (:13077-13082 over CTFWeaponBase::GetSkin's team
        // fallback, tf_weaponbase.cpp:4924-4938): the drop-skin override is
        // stock's -1 base that no weapon in this SDK overrides and the item
        // skin needs an item, so the colour is the dying player's team —
        // blue 1, everything else red's 0.
        int skin = (TFTeam)mapPlayers.Team(death.Player) == TFTeam.Blue ? 1 : 0;

        // The dropped weapon's own carry (InitDroppedWeapon :566-572): the
        // clip it died with, and the primary reserve its owner held — the
        // medigun's charge too (:588-601), which a suicide drops drained and
        // which only a medigun holds.
        string? ammoName = arsenal.Weapons.For(pickedName)?.AmmoName;
        int ammoType = ammoName is { Length: > 0 } ? pools.GetValueOrDefault(ammoName) : 0;
        float charge = !isSuicide && pickedScript?.Id == TFWeaponId.Medigun
                     ? medigun.ChargeLevel(death.Player.EntryIndex) : 0;

        // The create (:13096, CTFDroppedWeapon::Create): the same-owner same-
        // weapon dedupe and the thirty-two cap (:492-517) first, then
        // CreateNoSpawn(origin, angles) + SetModelName + DispatchSpawn
        // (:519-524) — the origin is the death's, the angles the placement's
        // (deferred with it), the model the worldmodel (:13076).
        TFDroppedWeapon.CleanUp(directory, death.Player, pickedName);
        var droppedHandle = entities.Create(TFDroppedWeapon.Class);
        if (entities.IsValid(droppedHandle))
        {
            entities.SetOrigin(droppedHandle, death.Origin);
            render.SetModel(droppedHandle, worldModel);
            entities.Spawn(droppedHandle);

            if (directory.Get(droppedHandle) is TFDroppedWeapon dropped)
                dropped.Init(death.Player, pickedName, pickedClip, ammoType,
                             chargeLevel: charge, skin: skin); // :13099, bSwap false
        }

        // The create (:13103): CTFAmmoPack::Create is CreateNoSpawn(origin,
        // angles, owner) + SetModelName + DispatchSpawn (:100-110) — owner and
        // origin ride on the entity before the spawn, the model is the pack's
        // own Spawn's. The pack angles are the death spot's: stock's
        // CalculateAmmoPackPositionAndAngles (:13090) dresses them off the
        // worldmodel, deferred with it.
        var pack = entities.Create(TFAmmoPack.Class); // :13103
        if (!entities.IsValid(pack))
            return;                                   // Create's null arm, :103/:105
        entities.SetOwner(pack, death.Player);        // CreateNoSpawn's pOwner
        entities.SetOrigin(pack, death.Origin);
        entities.Spawn(pack);                         // DispatchSpawn, :106

        if (directory.Get(pack) is not TFAmmoPack ammoPack)
            return;

        ammoPack.Init(death.Player, (int)(pickedScript?.Id ?? TFWeaponId.None),
                      mapPlayers.ClassOf(death.Player), primary, secondary, metal,
                      skin: skin, isSuicide: isSuicide); // :13107, nSkin the team colour

        log.Msg($"[SourceSharp/TFDeathDrops] dropped {TFAmmoPack.Class} dressed as {pickedName}\n");

        AmmoPackCleanUp(death.Player); // :13110
    }

    /// <summary>
    /// <c>AmmoPackCleanUp</c> (:12975-13010): over the owner's packs on the
    /// floor, an owner past three packs loses the oldest — the walk counts
    /// only packs owned by this player (:12985) and the oldest comparison is
    /// a strict <c>&gt;</c> (:12993-12998), so ties keep whoever it met first.
    /// </summary>
    private void AmmoPackCleanUp(EntityHandle owner)
    {
        int count = 0;
        TFAmmoPack? oldest = null;
        foreach (var entity in directory.OfClass(TFAmmoPack.Class))
        {
            if (entity is not TFAmmoPack pack || pack.PackOwner != owner)
                continue; // :12983-12985

            count++;
            if (oldest is null || oldest.CreationTime > pack.CreationTime) // :12990-12998
                oldest = pack;
        }

        if (count > 3 && oldest is not null) // :13000-13008
            oldest.Remove();
    }
}
