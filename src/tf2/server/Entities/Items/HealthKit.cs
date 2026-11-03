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
/// The parts of a pickup a player is told about: the HUD's pickup line, the
/// <c>item_pickup</c> and heal events, the touch sound. One seam so a fact
/// reads what a kit announced without an engine.
/// </summary>
public interface ITFPickupNotices
{
    /// <summary>The <c>ItemPickup</c> user message, reliable, to the player alone.</summary>
    void ItemPickup(EntityHandle player, string className);

    /// <summary>The <c>item_pickup</c> game event (<c>userid</c>, <c>item</c>).</summary>
    void ItemPickupEvent(EntityHandle player, string item);

    /// <summary>
    /// The <c>player_healed</c> game event (patient, healer, amount).
    /// <c>healer</c> is the kit's owner — stock's <c>ToTFPlayer( GetOwnerEntity() )</c>
    /// (entity_healthkit.cpp:104-105): the event carries the owner's userid,
    /// and 0 when the kit has no owner or its owner is not a player.
    /// </summary>
    void PlayerHealed(EntityHandle patient, EntityHandle healer, int amount);

    /// <summary>The <c>player_healonhit</c> game event: the HUD's +N over the player.</summary>
    void PlayerHealOnHit(EntityHandle player, int amount);

    /// <summary>
    /// The <c>player_stealsandvich</c> game event: the health pack's own
    /// (tf_ammo_pack.cpp:332-341). <c>target</c> is the picker's userid;
    /// <c>owner</c> is the owner's, and only when the pack's owner is a
    /// player at all (stock's <c>ToTFPlayer(GetOwnerEntity())</c> arm).
    /// </summary>
    void PlayerStealsSandvich(EntityHandle owner, EntityHandle picker);

    /// <summary>A pickup sound at the player.</summary>
    void Sound(EntityHandle player, string sound);
}

/// <summary>The engine's <see cref="ITFPickupNotices"/>.</summary>
[SideService(Side.Server, As = [typeof(ITFPickupNotices)])]
public sealed class EngineTFPickupNotices(IUserMessageSender messages, IEngineGameEvents events, IEnginePlayers players,
                                          IEngineEntities entities, IEngineSound sound) : ITFPickupNotices
{
    public void ItemPickup(EntityHandle player, string className)
        => messages.Send(new ItemPickup { Item = className }, entities.GetIndex(player), reliable: true);

    public void ItemPickupEvent(EntityHandle player, string item)
        => Fire("item_pickup", e =>
        {
            events.EventSetInt(e, "userid", players.GetUserId(player));
            events.EventSetString(e, "item", item);
        });

    public void PlayerHealed(EntityHandle patient, EntityHandle healer, int amount)
        => Fire("player_healed", e =>
        {
            events.EventSetInt(e, "priority", 1); // HLTV event priority
            events.EventSetInt(e, "patient", players.GetUserId(patient));
            // Stock's `ToTFPlayer( GetOwnerEntity() )` arm (:104-105): a kit
            // owned by a player names that player, and any other owner — or
            // none — is healer 0.
            events.EventSetInt(e, "healer", entities.IsPlayer(healer) ? players.GetUserId(healer) : 0);
            events.EventSetInt(e, "amount", amount);
        });

    public void PlayerHealOnHit(EntityHandle player, int amount)
        => Fire("player_healonhit", e =>
        {
            events.EventSetInt(e, "amount", amount);
            events.EventSetInt(e, "entindex", entities.GetIndex(player));
            events.EventSetInt(e, "weapon_def_index", ItemDefIndexInvalid);
        });

    public void PlayerStealsSandvich(EntityHandle owner, EntityHandle picker)
        => Fire("player_stealsandvich", e =>
        {
            // The owner field is stock's `ToTFPlayer( GetOwnerEntity() )` guard
            // (tf_ammo_pack.cpp:335-338): a pack owned by something that is not
            // a player — none here, but the arm is stock's — has no owner userid.
            if (entities.IsPlayer(owner))
                events.EventSetInt(e, "owner", players.GetUserId(owner));

            events.EventSetInt(e, "target", players.GetUserId(picker));
        });

    /// <summary>
    /// Stock plays it to the player alone (<c>CSingleUserRecipientFilter</c>);
    /// the sound area has no single-recipient filter, so the player's
    /// neighbours hear it too (docs/divergences.md).
    /// </summary>
    public void Sound(EntityHandle player, string sound1) => sound.EntityEmitSound(player, sound1);

    /// <summary><c>INVALID_ITEM_DEF_INDEX</c> (econ_item_constants.h), as the event's short.</summary>
    private const int ItemDefIndexInvalid = 65535;

    private void Fire(string name, Action<IntPtr> fill)
    {
        IntPtr e = events.EventCreate(name);
        if (e == IntPtr.Zero)
            return;

        fill(e);
        events.EventFire(e);
    }
}

/// <summary>
/// item_healthkit_full (stock's <c>CHealthKit</c>, entity_healthkit.cpp): heals
/// the kit's share of the player's maximum, and puts out fire, bleeding and
/// plague even for a player at full health.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Item)]
public class HealthKit(ITFMapPlayers players, IEngineFiles files, IEngineWorldQuery world, PlacedObjects placed,
        ITFPickupNotices notices,
                       ITFWeaponOwners? owners = null, AttributeHooks? attributes = null,
                       ITFPlayerStats? stats = null, TFDamageRecords? records = null,
                       ITFPlayerData? disguises = null, TFClassTable? classes = null)
    : TFPowerup(players, files, world, placed)
{
    public const string MapClass = "item_healthkit_full";

    /// <summary><c>TF_HEALTHKIT_PICKUP_SOUND</c>.</summary>
    public const string PickupSound = "HealthKit.Touch";

    /// <summary>What a kit's touch announces.</summary>
    protected readonly ITFPickupNotices Notices = notices;

    private readonly ITFWeaponOwners _owners = owners ?? new NoTFWeaponOwners();
    private readonly AttributeHooks _attributes = attributes ?? new AttributeHooks();
    /// <summary><c>CGameStats::Event_PlayerHealthkitPickup</c> (:180); the derived kits count their own stat.</summary>
    protected readonly ITFPlayerStats? _stats = stats;

    /// <summary>The last-hit seam behind stock's <c>GetLastEntityDamaged</c> arm (:148).</summary>
    private readonly TFDamageRecords? _records = records;

    /// <summary>The carrier behind the disguise arm's fake health — stock's
    /// <c>m_iDisguiseHealth</c>, read at :117 and written at :127.</summary>
    private readonly ITFPlayerData? _disguises = disguises;

    /// <summary>The class scripts behind <c>GetDisguiseMaxHealth</c> (:8358-8369).</summary>
    private readonly TFClassTable? _classes = classes;

    /// <summary><c>m_hOwnerEntity</c>: who threw this kit. The lunchbox throw
    /// writes it (<c>SetOwnerEntity</c>, deferred with the weapon —
    /// docs/plans/tf2/deferred.tsv), and a map kit has none.</summary>
    [SaveField] private uint _owner = EntityHandle.None.Value;

    /// <summary><c>m_bThrownSingleInstance</c>: a thrown kit takes only the :182 arm.</summary>
    [SaveField] private bool _thrownSingleInstance;

    /// <summary><c>GetOwnerEntity()</c>: the player who threw this kit, or none.</summary>
    public EntityHandle OwnerEntity => new(_owner);

    /// <summary><c>CBaseEntity::SetOwnerEntity</c>, as the lunchbox throw calls it when the sandvich leaves the hand.</summary>
    public void SetOwner(EntityHandle owner) => _owner = owner.Value;

    /// <summary><c>SetThrownSingleInstance</c> (entity_healthkit.h): what the throw flips on the kit it creates.</summary>
    public void SetThrownSingleInstance() => _thrownSingleInstance = true;

    /// <summary>Whether this kit came from a lunchbox throw rather than the map.</summary>
    public bool ThrownSingleInstance => _thrownSingleInstance;

    public override PowerupSize Size => PowerupSize.Full;

    protected override string DefaultModel => "models/items/medkit_large.mdl";

    /// <summary><c>GetHealthKitName</c>: the <c>item_pickup</c> event's item.</summary>
    public virtual string HealthKitName => "medkit_large";

    /// <summary>The classname the HUD's pickup line names (<c>GetClassname</c>).</summary>
    protected virtual string ClassNameForPickup => MapClass;

    protected override string Describe() => HealthKitName;

    /// <summary>
    /// <c>CHealthKit::MyTouch</c> (:66-218), stock's shape: the sandvich's
    /// owner takes only the ammo half (:80-88 — one grenade and the pickup);
    /// otherwise the kit heals <c>ceil( max * ratio )</c>, scaled by the
    /// player's <c>mult_health_frompacks</c> attribute
    /// (<c>CALL_ATTRIB_HOOK_FLOAT_ON_OTHER</c>, :95), through
    /// <c>TakeHealth</c>, and the pickup happens when health was given or the
    /// player was burning, bleeding or plagued — clearing those
    /// (<c>HealthKitPickupEffects</c>) — or, for a full-health heavy who owns
    /// a lunchbox, as the unowned kit's one grenade (:182-196). A thrown kit
    /// (<see cref="ThrownSingleInstance"/>) takes no :182 arm at all. A
    /// disguised spy's fake health takes the kit's share of the disguise
    /// class's maximum beside the heal (:115-130).
    /// The rune bonus (and the disguise arm's plague-rune gate), the
    /// drown-damage subtraction, the owner's lunchbox kill-eater events, the
    /// heavy's achievement, the bonus points' user message and the arms'
    /// item-charge refills are not ported (docs/plans/tf2/deferred.tsv); the
    /// gamestats counts — the kit's own (:180) and the owner's heal assist
    /// (:150-151) — are.
    /// </summary>
    protected override bool MyTouch(EntityHandle player)
    {
        bool success = false;
        bool performPickup = false;

        bool heavyWithSandvich = HeavyWithSandvich(player); // :75

        // In the case of sandvich's owner, only restore ammo (:79-88).
        if (OwnerEntity == player && heavyWithSandvich)
        {
            if (Players.GiveAmmo(player, 1, TFAmmo.Grenades1) > 0) // :82
            {
                success = true;
                performPickup = true;
                // SetItemChargeMeter(SECONDARY, 100) — deferred (:86).
            }
        }
        else
        {
            // The rune's health bonus (:91) is deferred with the runes.
            float health = MathF.Ceiling(Players.MaxHealth(player) * PackRatio); // :93
            health = _attributes.Float(health, "mult_health_frompacks", _owners.ManagerOf(player.EntryIndex)); // :95
            int given = Players.TakeHealth(player, health); // :97
            if (given > 0)
                Notices.PlayerHealed(player, OwnerEntity, given); // :99-113, the healer arm at :104-105

            // :115-130 — the disguised spy's fake health takes the pack's
            // share of the disguise class's maximum. Stock also refuses the
            // arm while the spy carries the plague rune (the
            // GetCarryingRuneType() arm, :115) — deferred with the runes.
            // :129 is bSuccess only, no bPerformPickup: a full-health spy
            // takes the kit for the fake health alone — no sound, no event,
            // no stat — until the arm below also fires.
            if (Players.InCond(player, TFCond.Disguised) && _disguises?.Predicted(player.EntryIndex) is { } carrier)
            {
                float flDisguiseHealth = carrier.DisguiseHealth; // :117
                float flDisguiseMaxHealth = DisguiseMaxHealth(carrier, player); // :118, :8358-8369
                float flHealthToAdd = MathF.Ceiling(flDisguiseMaxHealth * PackRatio); // :119, PackRatios[GetPowerupSize()]

                // :122-125 — "don't want to add more than we're allowed to
                // have": the add is the room to the maximum, negative for a
                // fake health the healers have over-charged, so the kit
                // pulls it back down to the ceiling.
                if (flHealthToAdd > flDisguiseMaxHealth - flDisguiseHealth)
                    flHealthToAdd = flDisguiseMaxHealth - flDisguiseHealth;

                carrier.DisguiseHealth = (int)(flDisguiseHealth + flHealthToAdd); // :127
                success = true; // :129
            }

            if (given > 0 || Players.InCond(player, TFCond.Bleeding)
                            || Players.InCond(player, TFCond.Burning) || Players.InCond(player, TFCond.Plague))
            {
                // :132-181. The drown-damage subtraction (:138) and the
                // heavy's achievement user message (:170-176) are deferred.
                performPickup = true;
                success = true;

                Notices.Sound(player, PickupSound); // :140-141

                // :143-168 — a kit owned by the picker's teammate credits the
                // owner with the assist (:150-151), not while the picker is
                // hurting themselves: stock :148 asks the picker's last entity
                // damaged (NULL passes; the last-hit seam answers for it, its
                // attacker being who that hit came from). The owner's lunchbox
                // kill-eater events (:154-166), the bonus points'
                // PlayerBonusPoints user message and the GC-match 90 s
                // anti-farm arms (tf_gamestats.cpp:742-751, :926-935) are
                // deferred — this build has no GC match, so only the
                // no-match arm of stock's guards can run.
                EntityHandle owner = OwnerEntity;
                if (owner != EntityHandle.None && owner != player
                    && Players.Team(owner) == Players.Team(player)
                    && _records?.LastHit(player.EntryIndex)?.Attacker != player)
                {
                    _stats?.Increment(owner, TFStat.BonusPoints, 10); // :150 — Event_PlayerAwardBonusPoints
                    if (given is >= 0 and <= 1000) // the bogus-value refusal, tf_gamestats.cpp:755-763
                        _stats?.Increment(owner, TFStat.HealingAssist, given); // :151 — Event_PlayerHealedOtherAssist
                }

                HealthKitPickupEffects(player, given); // :178

                _stats?.Increment(player, TFStat.Healthkits); // :180 — Event_PlayerHealthkitPickup
            }
            else if (!ThrownSingleInstance)
            {
                // :182-196 — a full-health heavy takes an unowned sandvich as
                // their secondary's one grenade; no touch sound, as in stock.
                if (heavyWithSandvich && Players.GiveAmmo(player, 1, TFAmmo.Grenades1) > 0) // :186
                {
                    performPickup = true;
                    success = true;
                    // SetItemChargeMeter(SECONDARY, 100) — deferred (:190).
                }
            }
        }

        if (performPickup) // :199-214
        {
            Notices.ItemPickup(player, ClassNameForPickup);
            Notices.ItemPickupEvent(player, HealthKitName);
        }

        return success;
    }

    /// <summary>
    /// <c>bIsAnyHeavyWithSandvichEquippedPickingUp</c> (:75):
    /// <c>Weapon_OwnsThisID( TF_WEAPON_LUNCHBOX )</c> and the heavy class.
    /// </summary>
    protected bool HeavyWithSandvich(EntityHandle player)
        => _owners.ItemOf(player.EntryIndex, TFWeaponNames.Name(TFWeaponId.Lunchbox).ToLowerInvariant()) is not null
           && Players.ClassOf(player) == TFClass.HeavyWeapons;

    /// <summary>
    /// <c>CTFPlayerShared::GetDisguiseMaxHealth</c> (:8358-8369): the disguise
    /// class's script maximum; a class with no data answers the player's own
    /// maximum, as stock's <c>m_pOuter->GetMaxHealth()</c> arm.
    /// </summary>
    private int DisguiseMaxHealth(TFPlayerNetState carrier, EntityHandle player)
    {
        int max = _classes is null ? 0 : _classes[(TFClass)carrier.DisguiseClass].MaxHealth;
        return max > 0 ? max : Players.MaxHealth(player);
    }

    /// <summary>
    /// <c>CTFPlayerShared::HealthKitPickupEffects</c> (tf_player_shared.cpp):
    /// a fire blanket, sutures and the plague cure, then the HUD's +N unless
    /// the player is stealthed.
    /// </summary>
    private void HealthKitPickupEffects(EntityHandle player, int given)
    {
        if (Players.InCond(player, TFCond.Burning))
            Players.RemoveCond(player, TFCond.Burning);

        if (Players.InCond(player, TFCond.Bleeding))
            Players.RemoveCond(player, TFCond.Bleeding);

        if (Players.InCond(player, TFCond.Plague))
            Players.RemoveCond(player, TFCond.Plague);

        if (given > 0 && !Stealthed(player))
            Notices.PlayerHealOnHit(player, given);
    }

    /// <summary><c>CTFPlayerShared::IsStealthed</c>: cloaked, in any of the three ways.</summary>
    private bool Stealthed(EntityHandle player)
        => Players.InCond(player, TFCond.Stealthed) || Players.InCond(player, TFCond.StealthedUserBuff)
        || Players.InCond(player, TFCond.StealthedUserBuffFading);
}

/// <summary>item_healthkit_small (<c>CHealthKitSmall</c>).</summary>
[ScriptedEntity(SmallClass, ScriptedEntityKind.Item)]
public sealed class HealthKitSmall(ITFMapPlayers players, IEngineFiles files, IEngineWorldQuery world, PlacedObjects placed,
        ITFPickupNotices notices,
                                   ITFWeaponOwners? owners = null, AttributeHooks? attributes = null,
                                   ITFPlayerStats? stats = null, TFDamageRecords? records = null,
                                   ITFPlayerData? disguises = null, TFClassTable? classes = null)
    : HealthKit(players, files, world, placed, notices, owners, attributes, stats, records, disguises, classes)
{
    public const string SmallClass = "item_healthkit_small";

    public override PowerupSize Size => PowerupSize.Small;

    protected override string DefaultModel => "models/items/medkit_small.mdl";

    public override string HealthKitName => "medkit_small";

    protected override string ClassNameForPickup => SmallClass;
}

/// <summary>item_healthkit_medium (<c>CHealthKitMedium</c>).</summary>
[ScriptedEntity(MediumClass, ScriptedEntityKind.Item)]
public sealed class HealthKitMedium(ITFMapPlayers players, IEngineFiles files, IEngineWorldQuery world, PlacedObjects placed,
        ITFPickupNotices notices,
                                    ITFWeaponOwners? owners = null, AttributeHooks? attributes = null,
                                    ITFPlayerStats? stats = null, TFDamageRecords? records = null,
                                    ITFPlayerData? disguises = null, TFClassTable? classes = null)
    : HealthKit(players, files, world, placed, notices, owners, attributes, stats, records, disguises, classes)
{
    public const string MediumClass = "item_healthkit_medium";

    public override PowerupSize Size => PowerupSize.Medium;

    protected override string DefaultModel => "models/items/medkit_medium.mdl";

    public override string HealthKitName => "medkit_medium";

    protected override string ClassNameForPickup => MediumClass;
}
