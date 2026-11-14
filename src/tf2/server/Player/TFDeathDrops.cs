//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;
using TF2.Server.Entities.Items;
using TF2.Server.Medigun;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;

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
                                 IEngineEntityRender render, ITFMedigunWeapons medigun,
                                 AttributeHooks hooks, ITFWeaponOwners owners,
                                 IUniformRandom random, ITFPlayerConditions conditions,
                                 TFDamageRecords hitRecords, ITFPlayerData carriers)
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
        // Stock's three killer-side arms, in Event_Killed's own order: the
        // kill's crit boost through Event_KilledOther (:11693 :11135-11155),
        // the ammo pack (:12161) and the killer's health pack (:12307). Each
        // runs whether or not the others bailed, so none rides inside another's
        // early returns.
        AddKillRage(death);        // Event_Killed's sniper rage (:11364-11376), before the killed-other call at :11693
        AddKillCritBoost(death);   // Event_KilledOther, asked of the recent damager (:11693)
        DropAmmoPack(death);       // :12161, in Event_Killed's own order
        DropHealthPack(death, death.Attacker);   // :12307, the killer's held item answering
        GrowClipOnKill(death);     // GetMaxClip1's blast arm (:594-600), folded onto the kill
    }

    /// <summary>
    /// The Dead Ringer's feign (<c>FeignDeath</c>, tf_player.cpp:15740-15743):
    /// the same dress, model and owner walk as a real death's pack, but the
    /// pack itself is an empty one — stock's <c>DropAmmoPack(info, true
    /// /*Empty*/, bDisguised)</c>, where <c>bEmpty</c> reaches only the pack's
    /// contents (<c>InitAmmoPack</c>, tf_ammo_pack.cpp:120-151), the dropped
    /// weapon unaffected (:13096-13100). None of the kill-side arms rides a
    /// feign: <c>Event_KilledOther</c> and the clip's blast arm are a real
    /// kill's; the feign's own health-pack asks (:15745-15758) ride here with
    /// the medieval mode's answer deferred beside its modes.
    /// </summary>
    public void OnFeignDeathDropped(PlayerDeath death)
    {
        DropAmmoPack(death, empty: true);            // :15740-15743, stock's empty pack
        DropHealthPack(death, death.Player);         // :15750-15757, the spy's own held item answering
    }

    /// <summary>
    /// <c>CTFPlayer::DropAmmoPack</c> (:13029-13112) behind
    /// <c>ShouldDropAmmoPack</c> (:13015-13024), with the
    /// <c>AmmoPackCleanUp</c> (:13110) that follows the create.
    /// </summary>
    private void DropAmmoPack(PlayerDeath death, bool empty = false)
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
                      skin: skin, empty: empty, isSuicide: isSuicide); // :13107, nSkin the team colour

        log.Msg($"[SourceSharp/TFDeathDrops] dropped {TFAmmoPack.Class} dressed as {pickedName}\n");

        AmmoPackCleanUp(death.Player); // :13110
    }

    /// <summary>
    /// <c>CTFPlayer::DropHealthPack</c> (:13140-13153), reached from
    /// <c>Event_Killed</c>'s attribute arm (:12307-12314): the kill asks the
    /// ATTACKER's held item for <c>drop_health_pack_on_kill</c> — the Candy
    /// Cane — and stock's int walk answers <c>== 1</c> (:12311). The small kit
    /// lands at the dying player's world-space centre (:13142-13143) with an
    /// impulse of two random XY draws in [-1,1], z pinned to 1, normalised and
    /// times 250 (:13146-13150), through <c>DropSingleInstance</c> owned by the
    /// dying player with the thrower's own-touch delay 0 (:13151). Each caller
    /// names whose held item answers (:12307 the killer's, :15750 the feigning
    /// spy's own): <paramref name="subject"/>.
    /// </summary>
    private void DropHealthPack(PlayerDeath death, EntityHandle subject)
    {
        if (death.Attacker == EntityHandle.None) // :12307 `if (pPlayerAttacker)`
            return;

        if (hooks.Int(0, "drop_health_pack_on_kill", owners.ActiveItemOf(subject.EntryIndex)) != 1)
            return; // :12311

        var kit = entities.Create(HealthKitSmall.SmallClass); // :13143
        if (!entities.IsValid(kit)) // :13144, the assert_cast null arm
            return;

        entities.SetOrigin(kit, entities.GetWorldSpaceCenter(death.Player)); // :13142, Create's origin
        entities.Spawn(kit);                                                  // DispatchSpawn inside Create

        if (directory.Get(kit) is HealthKit small)
        {
            float x = random.RandomFloat(-1f, 1f); // :13146 RandomVector(-1, 1)
            float y = random.RandomFloat(-1f, 1f);
            float length = MathF.Sqrt(x * x + y * y + 1f); // z pinned to 1 (:13147), VectorNormalize (:13148)
            small.DropSingleInstance(new Vector(250f * x / length, 250f * y / length, 250f / length),
                                     death.Player, throwerTouchDelay: 0f); // :13150-13151
        }
    }

    /// <summary>
    /// The heads half of <c>CTFRocketLauncher_AirStrike::OnPlayerKill</c>
    /// (<c>tf_weapon_rocketlauncher.cpp:411-432</c>): the killer's shared
    /// head count becomes its own pile plus the victim's pile plus one
    /// (<c>SetDecapitations</c>, :419-424), asked of the weapon's owner
    /// through <see cref="ITFPlayerData"/> — the stock walk needs the hook
    /// only after the bump, for the achievement (:426-431), so a kill with no
    /// attribute still counts. The clip side reads the count back through
    /// <see cref="TFWeaponBehaviour"/>' blast arm
    /// (<c>tf_weaponbase.cpp:594-600</c>), stock's own read of the same
    /// number. The count's reset at death sits with
    /// <see cref="TFSwordHeads"/>' carrier walk.
    /// </summary>
    private const string AirStrikeClass = "tf_weapon_rocketlauncher_airstrike";

    private void GrowClipOnKill(PlayerDeath death)
    {
        if (death.Attacker == EntityHandle.None)   // :416's weapon-owner guard
            return;

        // Stock's override lives on the Air Strike's class alone: any other
        // launcher's kill must leave the count the swords share alone.
        if (arsenal.ActiveWeaponName(death.Attacker) != AirStrikeClass)
            return;

        if (carriers.Predicted(death.Attacker.EntryIndex) is not { } killer)   // :415's GetOwner
            return;

        killer.Decapitations += (carriers.Predicted(death.Player.EntryIndex)?.Decapitations ?? 0) + 1;   // :419-424
    }

    /// <summary>
    /// <c>add_onkill_critboost_time</c> (the Killing Gloves), the kill arm of
    /// <c>Event_KilledOther</c> (:11135-11146): stock walks the killing weapon's
    /// item (<c>info.GetWeapon()</c>, the dynamic_cast arm at :11135-11136), and
    /// any non-zero walked answer stamps <c>TF_COND_CRITBOOSTED_ON_KILL</c> for
    /// the walked seconds plus the perceptual second (:11142-11145). The
    /// minicrit sibling (:11148-11154, <c>add_onkill_minicritboost_time</c>) and
    /// the weapon's own <c>OnPlayerKill</c> (:11138) wait with their carriers
    /// (docs/plans/tf2/deferred.tsv).
    /// </summary>
    private void AddKillCritBoost(PlayerDeath death)
    {
        if (death.Attacker == EntityHandle.None)
            return; // :11136: no killing player, so no killing weapon to ask

        if (hitRecords.LastHit(death.EntIndex) is not { } wound)
            return; // :11135: info.GetWeapon(), the hit's own weapon record

        int boost = hooks.Int(0, "add_onkill_critboost_time",
                              owners.ItemOf(death.Attacker.EntryIndex, wound.WeaponName));
        if (boost != 0) // :11142
            conditions.Add(death.Attacker, TFCond.CritboostedOnKill, boost + 1f); // :11145
    }

    /// <summary>
    /// <c>rage_on_kill</c> (the Hitmaker's charge), the sniper arm of
    /// <c>Event_Killed</c> (:11364-11376): a killing sniper walks the killing
    /// weapon's item for the gain, and a non-zero one rides onto the killer's
    /// rage meter through <c>ModifyRage</c>
    /// (<c>tf_player_shared.cpp:13903-13906</c>) — which is
    /// <c>SetRageMeter</c> over meter plus gain: the deploy's drain refuses a
    /// fresh gain unless the sniper charge buff already rides the owner or
    /// the owner is the spy (:13873), and the meter holds at the hundred
    /// (:13883). The assist half (<c>tf_gamerules.cpp:11415-11427</c>,
    /// <c>rage_on_assists</c> over the credited assister) waits with the
    /// healer/assist track it reads (<c>m_Shared.m_aHealers</c>, the same
    /// deferred state as the heal-assist counts); named in the row's evidence.
    /// </summary>
    private void AddKillRage(PlayerDeath death)
    {
        if (death.Attacker == EntityHandle.None)
            return; // :11365 with no killing player there is no sniper to ask

        if (hitRecords.LastHit(death.EntIndex) is not { } wound)
            return; // info.GetWeapon()'s record: no wound, no killing weapon to walk

        if (carriers.Predicted(death.Attacker.EntryIndex) is not { } killer)
            return; // the owner of the gain, stock's m_Shared on the killing player

        if (killer.Class() != TFClass.Sniper)
            return; // :11365 IsPlayerClass(TF_CLASS_SNIPER)

        float gain = hooks.Float(0f, "rage_on_kill",
                                 owners.ItemOf(death.Attacker.EntryIndex, wound.WeaponName));
        if (gain == 0f) // :11371
            return;

        // :13873: the drain only refuses while no sniper buff rides the owner
        // and the owner is not the spy; :13883 caps the write.
        if (!killer.RageDraining || killer.Class() == TFClass.Spy
            || conditions.InCond(death.Attacker, TFCond.SniperchargeRageBuff))
            killer.RageMeter = Math.Min(TFRageMeter.FullMeter, killer.RageMeter + gain);
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
