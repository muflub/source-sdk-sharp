//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Entities.Items;
using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Server.Voice;
using TF2.Shared;

namespace TF2.Server.Buildings;

/// <summary>
/// <c>obj_dispenser</c> (tf_obj_dispenser.cpp): the field servicer. Standing
/// in its radius, a friendly is healed at its level's rate, refilled at the
/// level's ammo rate, and topped up with metal from the dispenser's own pool
/// — which refills itself on its own clock.
///
/// <para>
/// <b>The clocks.</b> Stock heals continuously and dispenses on a beat; both
/// run here on the one <c>DispenseThink</c> at <c>DISPENSER_THINK_DELAY</c>.
/// Healing carries its fraction between ticks (stock heals through
/// <c>CTFPlayerShared::Heal</c> at <c>StartHealing</c>'s rate,
/// tf_obj_dispenser.cpp:857): at the level's rate
/// a tenth of a second pays its slice. Ammo gives once a second when
/// something was given, every tenth when nothing was; each give is the
/// level's rate of the receiver's own maximum of that ammo, and metal comes
/// out of the pool (up to the level's drop at a time, refilled by the
/// level's refill every six seconds to a pool of four hundred).
/// </para>
///
/// <para>
/// The touch list is asked of the world rather than
/// kept from touch callbacks: same-team, alive and seen is what stock's
/// <c>CouldHealTarget</c> asks too. The heal rides the shared's accumulation
/// (<see cref="ITFPlayerHealing"/>: the cloak, the cap and the builder's
/// score). The dispenser-generating buildings and the engineer-only metal
/// arms are deferred rows.
/// </para>
/// </summary>
[ScriptedEntity(TFDispenser.MapClass, ScriptedEntityKind.Item)]
public sealed class TFDispenser(ITFMapPlayers players, ITFObjectAnnouncer announcer, IEnginePrecache precache,
                                 IEngineEntityRender render, IEngineWorldQuery world, ITFAmmoPurse purse,
                                 ITFPlayerHealing healing, TFPlayerCarriers? carriers = null,
                                 TFConceptSpeech? speech = null)
    : TFBaseObject(players, announcer, precache, render, purse, carriers, speech)
{
    public const string MapClass = "obj_dispenser";

    /// <summary><c>DISPENSER_THINK_DELAY</c>: the one clock the services run on.</summary>
    public const float DispenseThinkDelay = 0.1f;

    /// <summary>
    /// <c>GetDispenserRadius</c>'s 64 — stock scales it only by the owner's
    /// <c>mult_dispenser_radius</c> attribute, never by the level
    /// (tf_obj_dispenser.cpp:955-965).
    /// </summary>
    public const float DispenserRadius = 64f;

    /// <summary>
    /// <c>g_flDispenserHealRates</c> (tf_obj_dispenser.cpp:92-98), indexed by
    /// <c>GetUpgradeLevel()</c> in <c>GetHealRate</c> (:837).
    /// </summary>
    public static float HealRateForLevel(int level) => level switch
    {
        2 => 15f,
        >= 3 => 20f,
        _ => 10f,
    };

    /// <summary>
    /// <c>g_flDispenserAmmoRates</c> (tf_obj_dispenser.cpp:100-106), indexed
    /// by <c>GetUpgradeLevel()</c> (:529).
    /// </summary>
    public static float AmmoRateForLevel(int level) => level switch
    {
        2 => 0.3f,
        >= 3 => 0.4f,
        _ => 0.2f,
    };

    /// <summary><c>DISPENSER_START_METAL_AMOUNT</c>, <c>DISPENSER_MAX_METAL_AMMO</c> and the refill's clock.</summary>
    public const int StartMetal = 25, MaxMetal = 400;

    /// <summary><c>DISPENSER_DROP_METAL</c> + ((level−1) × 10) — the metal give's ceiling (tf_obj_dispenser.cpp:584).</summary>
    public static int DropMetalForLevel(int level) => DropMetal + (level - 1) * 10;

    /// <summary><c>DISPENSER_DROP_METAL</c>, the level table's base.</summary>
    public const int DropMetal = 40;

    /// <summary>
    /// The self-refill: <c>DISPENSER_MAX_METAL_AMMO × 0.1</c> + ((level−1) ×
    /// 10) every six seconds (tf_obj_dispenser.cpp:606-613) — 20, 30 and 40.
    /// </summary>
    public static int RefillMetalForLevel(int level) => (int)(MaxMetal * 0.1f) + (level - 1) * 10;

    public const float FirstRefillDelay = 3f, RefillInterval = 6f;

    private const string DispenseThink = "DispenseThink";
    private const float FirstDispenseDelay = 0.5f, GiveInterval = 1f;

    private readonly IEngineWorldQuery _world = world;
    private readonly ITFPlayerHealing _healing = healing;
    private readonly EntityHandle[] _sphere = new EntityHandle[32];

    /// <summary>Stock's <c>m_hHealingTargets</c>: who is being healed this think, and the
    /// scratch set it is diffed against (swapped, so no tick allocates).</summary>
    private HashSet<EntityHandle> _healingTargets = new(), _seen = new();

    [SaveField] private float _nextDispenseTime, _nextMetalRefillTime;

    public override TFObjectType Type => TFObjectType.Dispenser;

    /// <summary>
    /// <c>CTFDispenser::OnGoActive</c>: the pool at its starting amount, the
    /// refill's clock wound, and the service booked.
    /// </summary>
    protected override void OnGoActive()
    {
        Metal = StartMetal;
        _nextDispenseTime = GameTime + FirstDispenseDelay;
        _nextMetalRefillTime = GameTime + FirstRefillDelay;
        SetContextThink(DispenseThink, DispenseThinkDelay);
    }

    protected override void OnDestroyed()
    {
        StopThinking(DispenseThink);
        StopHealingEveryone();
    }

    protected override void OnCarried()
    {
        StopThinking(DispenseThink);
        StopHealingEveryone();
    }

    /// <summary>
    /// <c>CObjectDispenser::StartUpgrading</c> (:328-330): the healing list is
    /// cleared as the level climbs, so DispenseThink adds everyone again at the
    /// new rate — the edge above, not a per-tick re-arm.
    /// </summary>
    protected override void OnStartUpgrade() => StopHealingEveryone();

    /// <summary>
    /// <c>CTFDispenser::DispenseThink</c>: heal a slice, and dispense when
    /// the beat has come; the beat is slower after a real give, faster when
    /// nothing needed anything.
    /// </summary>
    public override void OnThink(string context)
    {
        if (context != DispenseThink || IsDead || !IsBuilt)
            return;

        // Stock's DispenseThink disabled arm: don't heal or dispense ammo,
        // and stop healing everyone (ResetHealingTargets). RefillThink's own
        // arm refuses the pool the same way.
        if (IsDisabled)
        {
            StopHealingEveryone();
            SetContextThink(DispenseThink, DispenseThinkDelay);
            return;
        }

        RefillMetal();
        HealNearby();

        if (_nextDispenseTime <= GameTime)
            _nextDispenseTime = GameTime + (DispenseNearby() ? GiveInterval : DispenseThinkDelay);

        SetContextThink(DispenseThink, DispenseThinkDelay);
    }

    /// <summary><c>CTFDispenser::RefillThink</c>: the pool climbs its level's refill at a time to its four hundred.</summary>
    private void RefillMetal()
    {
        if (_nextMetalRefillTime > GameTime || Metal >= MaxMetal)
            return;

        Metal = Math.Min(MaxMetal, Metal + RefillMetalForLevel(Level));
        _nextMetalRefillTime = GameTime + RefillInterval;
    }

    /// <summary>
    /// Stock's heal arm (tf_obj_dispenser.cpp:846-861): every serviced player
    /// is handed to the shared's accumulation as a healer — this dispenser's
    /// rate, the builder scoring the heal, and the bonus of 1 that never
    /// overheals — and paid out by the patient's own tick; a player who
    /// walked out of the radius (or died) is told the healing stopped.
    /// </summary>
    private void HealNearby()
    {
        _seen.Clear();
        foreach (EntityHandle player in Serviced())
        {
            // Stock's edge (tf_obj_dispenser.cpp:694-698): StartHealing only for a
            // player who is not already a healing target — the entry carries its
            // accumulation across the ticks, and the rate is the attach's. A rate
            // change (an upgrade) re-arms through StopHealingEveryone below.
            if (!_healingTargets.Contains(player))
                _healing.Heal(player, Handle, HealRateForLevel(Level), GameTime,
                              overhealBonus: 1f, decayMult: 1f, dispenserHeal: true,
                              scorer: Builder == EntityHandle.None ? -1 : Builder.EntryIndex);
            _seen.Add(player);
        }

        foreach (EntityHandle was in _healingTargets)
            if (!_seen.Contains(was))
                _healing.StopHealing(was, Handle, GameTime);
        (_healingTargets, _seen) = (_seen, _healingTargets);
    }

    /// <summary>Stock's <c>ResetHealingTargets</c>: the healing stops for everyone tracked.</summary>
    private void StopHealingEveryone()
    {
        foreach (EntityHandle player in _healingTargets)
            _healing.StopHealing(player, Handle, GameTime);
        _healingTargets.Clear();
    }

    /// <summary>
    /// <c>CTFDispenser::DispenseWeaponAmmo</c> and the metal give, once a
    /// beat: a fifth of the receiver's own maximum of each ammo, and metal
    /// from the pool — true when the beat gave anything.
    /// </summary>
    private bool DispenseNearby()
    {
        bool gave = false;

        foreach (EntityHandle player in Serviced())
        {
            gave |= Give(player, TFAmmo.Primary);
            gave |= Give(player, TFAmmo.Secondary);
            gave |= GiveMetal(player);
        }

        return gave;
    }

    private bool Give(EntityHandle player, TFAmmo ammo)
    {
        int count = (int)(Players.MaxAmmo(player, ammo) * AmmoRateForLevel(Level));
        return count > 0 && Players.GiveAmmo(player, count, ammo) > 0;
    }

    /// <summary>Stock's metal give: the pool's own metal, up to the drop at a time.</summary>
    private bool GiveMetal(EntityHandle player)
    {
        int count = Math.Min(Metal, DropMetalForLevel(Level));
        if (count <= 0)
            return false;

        int given = Players.GiveAmmo(player, count, TFAmmo.Metal);
        Metal -= given;
        return given > 0;
    }

    /// <summary>
    /// The friendly neighbours: <c>CouldHealTarget</c>'s asks over a radius
    /// sweep — same team, alive, and seen from the dispenser's centre
    /// through <c>MASK_DISPENSER</c> (tf_obj_dispenser.cpp:896, 903).
    /// </summary>
    private IEnumerable<EntityHandle> Serviced()
    {
        Vector center = Entities.GetWorldSpaceCenter(Handle);
        int count = _world.EntitiesInSphere(center, DispenserRadius, _sphere);

        var result = new List<EntityHandle>();
        for (int i = 0; i < count && i < _sphere.Length; i++)
        {
            EntityHandle candidate = _sphere[i];
            if (!Entities.IsPlayer(candidate) || !Players.IsAlive(candidate))
                continue;

            if (Players.Team(candidate) != Team)
                continue;

            TraceResult trace = _world.TraceLine(center, Entities.GetWorldSpaceCenter(candidate), TraceMask.Dispenser, Handle);
            if (trace.Hit && trace.Entity != candidate)
                continue;

            result.Add(candidate);
        }

        return result;
    }
}
