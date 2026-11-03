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
/// What every TF weapon behaviour is built from, one set per side: the
/// scripts, the hook chain, the crit roll with its buckets and convars, the
/// owners (the carrier, when composed) and the effects the server performs.
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
    ITFWeaponOwners? owners = null,
    ITFWeaponEffects? effects = null,
    ITFBuilderPlacement? builder = null,
    ITFPlayerData? playerData = null,
    Rules.ITFRoundSetup? roundSetup = null,
    WeaponMedigunChargeRateConVar? medigunChargeRate = null,
    TFUseFixedWeaponspreadsConVar? fixedSpread = null,
    ITFAutoReload? autoReload = null,
    TFFlameThrowerBurstammoConVar? flamethrowerBurstammo = null,
    ITFMedigunPrediction? medigunPrediction = null)
{
    public TFWeaponScripts Scripts { get; } = scripts ?? throw new ArgumentNullException(nameof(scripts));

    public TFWeaponHookChain Hooks { get; } = hooks ?? throw new ArgumentNullException(nameof(hooks));

    public TFCritBuckets Buckets { get; } = buckets ?? throw new ArgumentNullException(nameof(buckets));

    public ITFCritSettings CritSettings { get; } = crits ?? throw new ArgumentNullException(nameof(crits));

    /// <summary>The players holding the weapons; a classless stand-in until the carrier is composed.</summary>
    public ITFWeaponOwners Owners { get; } = owners ?? new NoTFWeaponOwners();

    /// <summary>What the server does for a shot the core has no arm for; nothing until Stage 2 composes it.</summary>
    public ITFWeaponEffects Effects { get; } = effects ?? new NoTFWeaponEffects();

    /// <summary>The builder's placement; nothing until the server composes it (the replay asks nothing).</summary>
    public ITFBuilderPlacement? Builder { get; } = builder;

    /// <summary>
    /// The per-player TF state a predicted rule reads — the client's local
    /// working copy (the medigun's replayed charge is its only reader so far).
    /// Both sides register an owner, so a side that composes parts without one
    /// simply has a rule that predicts nothing.
    /// </summary>
    public ITFPlayerData? PlayerData { get; } = playerData;

    /// <summary>The round's setup gate (<see cref="Rules.ITFRoundSetup"/>); the
    /// medigun's charge asks it — nothing until a side registers the answer.</summary>
    public Rules.ITFRoundSetup? RoundSetup { get; } = roundSetup;

    /// <summary>The meter's build rate (<c>weapon_medigun_charge_rate</c>): the
    /// predicted replay pays it as the server's arm does. A rig that composes
    /// parts by hand and names no convar reads stock's default.</summary>
    public float MedigunChargeRate => medigunChargeRate?.Value ?? 40f;

    /// <summary>
    /// <c>tf_use_fixed_weaponspreads</c>: the multi-pellet loads leave in the
    /// fixed pattern when it is set. Null before the side's registry runs, the
    /// convar absent — the random sample, stock's default.
    /// </summary>
    public TFUseFixedWeaponspreadsConVar? FixedSpread { get; } = fixedSpread;

    /// <summary>
    /// <c>cl_autoreload</c> per player: the idle top-up's wish. Null before the
    /// side's registry runs — nobody has asked for one, stock's field default.
    /// </summary>
    public ITFAutoReload? AutoReload { get; } = autoReload;

    /// <summary>The airblast's ammunition (<c>tf_flamethrower_burstammo</c>,
    /// <c>tf_weapon_flamethrower.cpp:70</c>): both sides spend it, so the
    /// predicted replay pays the same convar. A rig that composes parts by
    /// hand and names no convar reads stock's default.</summary>
    public int FlamethrowerBurstammo => flamethrowerBurstammo?.Value ?? 20;

    /// <summary>The medigun's replayed half — the target pick the client's
    /// prediction runs beside the server's effects. Null on the server: its
    /// own arm owns the whole frame.</summary>
    public ITFMedigunPrediction? MedigunPrediction { get; } = medigunPrediction;

    /// <summary>The side's crit roll (a scratch stream reseeded on every draw).</summary>
    public TFCritRoll Crits { get; } = new();
}
