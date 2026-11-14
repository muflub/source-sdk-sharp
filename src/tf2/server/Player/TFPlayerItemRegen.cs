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
using TF2.Shared.Player;

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
/// not they healed (:1900). A negative run — the Blutsauger's drained regen —
/// hurts the carrier instead once it passes a point down (:1882-1886): the
/// overhang ceil'd into generic self-damage through <see cref="IEngineDamage"/>.
/// The medic's own base regen (:1797-1829) adds to the same accumulation
/// first, so a Blutsauger medic still nets a gain. The heal events (:1870-1898)
/// and the ammo regen (:1904-) wait with their carriers
/// (docs/plans/tf2/deferred.tsv). With no manager the walk answers the zero
/// base and the slice heals nothing, as stock's hook on an attribute-less
/// player does.
/// </summary>
public sealed class TFPlayerItemRegen(ITFPlayerResupply resupply, AttributeHooks hooks, ITFWeaponOwners owners,
                                      IEnginePlayers enginePlayers, TFDamageRecords hitRecords,
                                      IEngineDamage damage, IEngineEntities entities,
                                      ITFPlayerData? players = null) : IPlayerComponent
{
    /// <summary>The think's period (<c>TF_REGEN_TIME</c>, tf_shareddefs.h:916).</summary>
    public const float RegenInterval = 1.0f;

    /// <summary>The medic's base rate (<c>TF_REGEN_AMOUNT</c>, tf_shareddefs.h:917).</summary>
    public const float MedicRegenAmount = 3f;

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

        if (owners.ClassOf(_index) == TFClass.Medic)
            _accumulated += MedicRegen(now);

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

        int spent = 0;
        if (_accumulated >= 1f)
        {
            spent = (int)MathF.Floor(_accumulated);  // :1864
            resupply.TakeHealth(_player, spent);     // :1867, the seam holds at the class max (:1865)
        }
        else if (_accumulated < -1f)
        {
            // :1882-1886: a drained regen hurts the carrier itself, the
            // ceil'd overhang as generic self-damage (the Blutsauger's -2).
            spent = (int)MathF.Ceiling(_accumulated);                       // :1884
            damage.TakeDamage(_player, _player, -spent, DamageType.Generic); // :1885
        }

        _accumulated -= spent;                       // :1900: the whole rounds are spent either way
    }

    /// <summary>
    /// The medic's base regen (:1797-1812): doubled over a hurt patient, and
    /// scaling from one to two over five to ten seconds since the last hit.
    /// </summary>
    private float MedicRegen(float now)
    {
        float peace = hitRecords.LastHit(_index) is { } hit ? now - hit.Time : float.PositiveInfinity;
        float scale = TFDamageModifyRules.RemapValClamped(peace, 5f, 10f, 1f, 2f);

        float amount = MedicRegenAmount;
        int patientIndex = players?.Networked(_index)?.HealTargetIndex ?? 0;
        if (patientIndex > 0)
        {
            EntityHandle patient = enginePlayers.GetHandle(patientIndex);
            if (patient != EntityHandle.None && entities.GetHealth(patient) < resupply.MaxHealth(patient))
                amount += MedicRegenAmount;
        }

        return amount * scale;
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
                                       IEngineDamage damage, IEngineEntities entities,
                                       AttributeHooks? hooks = null, ITFWeaponOwners? owners = null,
                                       TFDamageRecords? hitRecords = null, ITFPlayerData? players = null)
    : IPlayerComponentSource, IOrderedHook
{
    /// <summary>After the bleed's 707 in the combat band.</summary>
    public const int Order = 1_000_000 - 1_000 + 709;

    public int HookOrder => Order;

    public IEnumerable<IPlayerComponent> Create(EntityHandle player)
    {
        yield return new TFPlayerItemRegen(resupply, hooks ?? new AttributeHooks(),
                                           owners ?? new NoTFWeaponOwners(), enginePlayers,
                                           hitRecords ?? new TFDamageRecords(), damage, entities, players);
    }
}
