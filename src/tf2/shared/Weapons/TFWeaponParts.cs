using SourceSharp.Abi;

using TF2.Shared.Player;

namespace TF2.Shared;

/// <summary>
/// Whether a player wants the idle top-up — stock's <c>cl_autoreload</c>
/// (<c>c_tf_player.cpp:299</c> on the client,
/// <c>tf_gamerules.cpp:10269</c>'s <c>m_bAutoReload</c> on the server). Each
/// side answers from its own copy of the value.
/// </summary>
public interface ITFAutoReload
{
    /// <summary>The named player's wish; the ownerless context (0) is nobody.</summary>
    bool Wants(int ownerIndex);
}

/// <summary>
/// The bucket of crits the spy's sapper kills store (<c>CTFPlayerShared::
/// m_iRevengeCrits</c>): a building removed with its sapper riding pays the
/// sapper's owner one stored crit (<c>CBaseObject::UpdateOnRemove</c>,
/// <c>tf_obj.cpp:269-283</c>), and a revolver whose item collects them spends
/// one per shot (<c>CTFRevolver::PrimaryAttack</c>,
/// <c>tf_weapon_revolver.cpp:159-171</c>). The server's bucket is the real
/// one; the client's replay asks nothing, its boost comes on the condition
/// state, and its own answer spends nothing.
/// </summary>
public interface ITFRevengeCrits
{
    /// <summary>The riding sapper's owner takes one crit (<c>tf_obj.cpp:269-283</c>).</summary>
    void Grant(EntityHandle owner);

    /// <summary>One stored crit off the bucket for the shot just fired, behind
    /// the held item's <c>sapper_kills_collect_crits</c> == 1
    /// (<c>tf_weapon_revolver.h:51</c>, consumed at :159).</summary>
    void SpendOnShot(int ownerIndex);
}

/// <summary>
/// What every TF weapon behaviour is built from, one set per side: the
/// scripts, the hook chain, the crit roll with its buckets and convars, the
/// owners, the effects the server performs, and each side's answer to every
/// seam — a side without an arm registers the silent one
/// (<see cref="NoTFWeaponOwners"/>'s kind), never a null.
/// A behaviour object serves every player on its side and keeps nothing per
/// player: what it remembers about a weapon is the weapon's own predicted
/// state, and the crit buckets (each side's own, as stock's) are here.
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFWeaponParts(
    TFWeaponScripts scripts,
    TFWeaponHookChain hooks,
    TFCritBuckets buckets,
    ITFCritSettings crits,
    ITFWeaponOwners owners,
    ITFWeaponEffects effects,
    ITFBuilderPlacement builder,
    ITFPlayerData playerData,
    Rules.ITFRoundSetup roundSetup,
    WeaponMedigunChargeRateConVar medigunChargeRate,
    TFUseFixedWeaponspreadsConVar fixedSpread,
    ITFAutoReload autoReload,
    TFFlameThrowerBurstammoConVar flamethrowerBurstammo,
    ITFMedigunPrediction medigunPrediction,
    ITFSapperState sapper,
    ITFSentryWrangle sentryWrangle,
    ITFRevengeCrits revengeCrits)
{
    public TFWeaponScripts Scripts { get; } = scripts ?? throw new ArgumentNullException(nameof(scripts));

    public TFWeaponHookChain Hooks { get; } = hooks ?? throw new ArgumentNullException(nameof(hooks));

    public TFCritBuckets Buckets { get; } = buckets ?? throw new ArgumentNullException(nameof(buckets));

    public ITFCritSettings CritSettings { get; } = crits ?? throw new ArgumentNullException(nameof(crits));

    /// <summary>The players holding the weapons, each side's own.</summary>
    public ITFWeaponOwners Owners { get; } = owners ?? throw new ArgumentNullException(nameof(owners));

    /// <summary>What the side does for a shot the core has no arm for.</summary>
    public ITFWeaponEffects Effects { get; } = effects ?? throw new ArgumentNullException(nameof(effects));

    /// <summary>The builder's placement: the server's placement walk, the
    /// client's silent answer (the replay places nothing).</summary>
    public ITFBuilderPlacement Builder { get; } = builder ?? throw new ArgumentNullException(nameof(builder));

    /// <summary>
    /// The per-player TF state a predicted rule reads — the client's local
    /// working copy (the medigun's replayed charge is its only reader so far).
    /// Both sides register an owner.
    /// </summary>
    public ITFPlayerData PlayerData { get; } = playerData ?? throw new ArgumentNullException(nameof(playerData));

    /// <summary>The round's setup gate (<see cref="Rules.ITFRoundSetup"/>); the
    /// medigun's charge asks it, each side's own answer.</summary>
    public Rules.ITFRoundSetup RoundSetup { get; } = roundSetup ?? throw new ArgumentNullException(nameof(roundSetup));

    /// <summary>The meter's build rate (<c>weapon_medigun_charge_rate</c>): the
    /// predicted replay pays it as the server's arm does.</summary>
    public float MedigunChargeRate => medigunChargeRate.Value;

    /// <summary>
    /// <c>tf_use_fixed_weaponspreads</c>: the multi-pellet loads leave in the
    /// fixed pattern when it is set.
    /// </summary>
    public TFUseFixedWeaponspreadsConVar FixedSpread { get; } =
        fixedSpread ?? throw new ArgumentNullException(nameof(fixedSpread));

    /// <summary>
    /// <c>cl_autoreload</c> per player: the idle top-up's wish, each side's
    /// own copy.
    /// </summary>
    public ITFAutoReload AutoReload { get; } = autoReload ?? throw new ArgumentNullException(nameof(autoReload));

    /// <summary>The airblast's ammunition (<c>tf_flamethrower_burstammo</c>,
    /// <c>tf_weapon_flamethrower.cpp:70</c>): both sides spend it, so the
    /// predicted replay pays the same convar.</summary>
    public int FlamethrowerBurstammo => flamethrowerBurstammo.Value;

    /// <summary>The medigun's replayed half — the target pick the client's
    /// prediction runs beside the server's effects. The server's own answer
    /// is the silent one: its arm owns the whole frame.</summary>
    public ITFMedigunPrediction MedigunPrediction { get; } =
        medigunPrediction ?? throw new ArgumentNullException(nameof(medigunPrediction));

    /// <summary>The side's crit roll (a scratch stream reseeded on every draw).</summary>
    public TFCritRoll Crits { get; } = new();

    /// <summary>
    /// The sapper's use — the trigger's place, the wrench's blow. The server's
    /// use layer answers; the client's replay asks its silent one.
    /// </summary>
    public ITFSapperState Sapper { get; } = sapper ?? throw new ArgumentNullException(nameof(sapper));

    /// <summary>
    /// The Wrangler's word to the sentries (<see cref="TFWranglerBehaviour"/>):
    /// the server's word to its buildings, the client's replay a silent one.
    /// </summary>
    public ITFSentryWrangle SentryWrangle { get; } =
        sentryWrangle ?? throw new ArgumentNullException(nameof(sentryWrangle));

    /// <summary>
    /// The sapper's stored crits (<see cref="ITFRevengeCrits"/>): the server's
    /// bucket, the client's replay a silent one.
    /// </summary>
    public ITFRevengeCrits RevengeCrits { get; } = revengeCrits ?? throw new ArgumentNullException(nameof(revengeCrits));
}
