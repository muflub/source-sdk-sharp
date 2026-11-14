//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Player;

/// <summary>
/// The item's health regen, <c>CTFPlayer::RegenThink</c>'s item arm
/// (<c>tf_player.cpp:1831-1854</c>) as the player's slice of the per-player
/// composite: every <see cref="RegenInterval"/> second the held item's
/// <c>add_health_regen</c> (the Blutsauger, the Amputator, the Concheror) is
/// walked over zero (:1832-1833, stock's hook from a zero base) and scaled by
/// the peace since the last hit (:1836-1850): a quarter of it inside five
/// seconds of one, half to full ramping over the next five, whole past ten —
/// and whole on a never-hit player, where stock's damage stamp is old enough
/// for <c>RemapValClamped</c> to clamp at the far end. What the scale left
/// accumulates (:1854); once the accumulation holds a whole point, that many
/// points heal (:1862-1867) through <see cref="ITFPlayerResupply.TakeHealth"/>,
/// whose clamp at the class max is stock's <c>GetHealth() &lt;
/// GetMaxHealth()</c> gate (:1865), and the whole rounds are spent whether or
/// not they healed (:1900). The medic's base regen (:1797-1829), the negative
/// accumulation's self-damage (:1882-1886), the heal events (:1870-1898) and
/// the ammo regen (:1904-) wait with their carriers
/// (docs/plans/tf2/deferred.tsv). With no manager the walk answers the zero
/// base and the slice heals nothing, as stock's hook on an attribute-less
/// player does.
/// </summary>
public sealed class TFPlayerItemRegen(ITFPlayerResupply resupply, AttributeHooks hooks, ITFWeaponOwners owners,
                                      IEnginePlayers enginePlayers, TFDamageRecords hitRecords) : IPlayerComponent
{
    /// <summary>The think's period (<c>TF_REGEN_TIME</c>, tf_shareddefs.h:916).</summary>
    public const float RegenInterval = 1.0f;

    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;
    private float _nextDue = float.NegativeInfinity;
    private float _accumulated;

    /// <summary>After the clip mirror's 51 in the composite's block.</summary>
    public int Order => 52;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
    }

    /// <summary>One second of stock's regen think: the item's rate, the peace scale, the whole points healed.</summary>
    public void Tick(float now)
    {
        if (!enginePlayers.IsAlive(_player))
            return; // :1784: the dead do not regen (stock re-queues the think on its own clock)

        if (now < _nextDue)
            return; // :1791-1792: too soon since the last round of heals
        _nextDue = now + RegenInterval; // :1788's queue is the interval

        float regen = hooks.Float(0f, "add_health_regen", owners.ManagerOf(_index)); // :1832-1833
        if (regen != 0f)
        {
            // :1836: the peace since the last hit; a player never hit since
            // the attach has had it all along, which clamps the scale at its
            // far end (stock's stamp starts older than any round).
            float peace = hitRecords.LastHit(_index) is { } hit ? now - hit.Time : float.PositiveInfinity;

            // :1837-1852, the non-MvM chain (this mod's mode is never MvM, so
            // the full-scale arm at :1839-1842 is the one that stands): the
            // quarter within five seconds of a hit, the remap over the next
            // five, full past them.
            float scale = peace < 5f ? 0.25f
                        : peace < 10f ? 0.5f + (peace - 5f) * 0.1f
                                      : 1f;
            regen *= scale;
        }

        _accumulated += regen; // :1854

        if (_accumulated < 1f)
            return; // :1862: nothing whole yet

        int heal = (int)MathF.Floor(_accumulated);   // :1864
        resupply.TakeHealth(_player, heal);          // :1867, the seam holds at the class max (:1865)
        _accumulated -= heal;                        // :1900: the whole rounds are spent either way
    }

    public void Detach()
    {
        _player = EntityHandle.None;
        _index = -1;
    }
}

/// <summary>
/// The item regen's source in the per-player composite. The hook walk, the
/// owners and the hit records are optional as everywhere in the player lane:
/// without them the slice answers like an attribute-less player, healing
/// nothing.
/// </summary>
[SideService(Side.Server)]
public sealed class TFPlayerItemRegens(ITFPlayerResupply resupply, IEnginePlayers enginePlayers,
                                       AttributeHooks? hooks = null, ITFWeaponOwners? owners = null,
                                       TFDamageRecords? hitRecords = null)
    : IPlayerComponentSource, IOrderedHook
{
    /// <summary>After the bleed's 707 in the combat band.</summary>
    public const int Order = 1_000_000 - 1_000 + 709;

    public int HookOrder => Order;

    public IEnumerable<IPlayerComponent> Create(EntityHandle player)
    {
        yield return new TFPlayerItemRegen(resupply, hooks ?? new AttributeHooks(),
                                           owners ?? new NoTFWeaponOwners(), enginePlayers,
                                           hitRecords ?? new TFDamageRecords());
    }
}
