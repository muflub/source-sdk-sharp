//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Medigun;

/// <summary>
/// The player's medigun lane per frame, as a slice of the per-player composite
/// (Stage 2.9's components): every player gets the two patient arms of stock's
/// <c>ConditionGameRulesThink</c> (the heal accumulate and overheal drain, and
/// the charge-effect expiry) and the medic arms of the medigun's think (the
/// stale-client watchdog and the released-meter drain, stock's
/// <c>HealTargetThink</c> and <c>DrainCharge</c> behind <c>ItemPostFrame</c>).
///
/// <para>
/// THE MEDIGUN IS THE MEDIC'S ACTIVE WEAPON. Stock runs the beam and the drain
/// from the medigun's own <c>ItemPostFrame</c>, which the engine calls only
/// while it is the hand the player uses; going offhand is
/// <c>ItemHolsterFrame</c> (beam off, the medic's own uber off, the drain
/// going on) and coming back restores a running release. The port cannot be
/// called by the engine's item frames — no weapon runs managed code there yet
/// — so this component watches <see cref="IEnginePlayers.GetActiveWeapon"/>
/// and drives the holster pair at the transitions, a divergence row in
/// docs/divergences.md. Death is stock's <c>UpdateOnRemove</c>: beam, release
/// and uber all go.
/// </para>
/// </summary>
public sealed class TFMedigunComponent(ITFPlayerHealing healing, ITFMedigunWeapons weapons, IEnginePlayers players)
    : IPlayerComponent
{
    /// <summary><c>item_shop_medigun</c>'s classname — the hand this lane watches.</summary>
    public const string MedigunName = "tf_weapon_medigun";

    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;
    private float _last = float.NaN;
    private bool _holstered;

    /// <summary>After the wearables (30 is theirs); unique among components, so the
    /// stable sort's tie-break never decides for it.</summary>
    public int Order => 40;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
        _last = float.NaN;
        _holstered = true;
    }

    public void Tick(float now)
    {
        float frametime = float.IsNaN(_last) ? 0f : now - _last;
        _last = now;

        healing.TickPatient(_player, now, frametime);
        healing.TestAndExpireChargeEffect(_player, now);
        weapons.TickMedic(_index, now, frametime);

        if (!players.IsAlive(_player))
        {
            weapons.RemoveMedigun(_index, now);   // stock's UpdateOnRemove; repeat ticks no-op
            return;
        }

        // The engine's item frames, in: the holster pair fires at the transitions,
        // not every frame (RemoveChargeEffect is not a frame thing to redo).
        bool holding = players.GetActiveWeapon(_player) == MedigunName;
        if (holding && _holstered)
        {
            weapons.Unholster(_index, now);
            _holstered = false;
        }
        else if (!holding && !_holstered)
        {
            weapons.Holster(_index, now);
            _holstered = true;
        }
    }

    public void Detach()
    {
        _player = EntityHandle.None;
        _index = -1;
    }
}

/// <summary>
/// The medigun lane's place in the per-player composite: the source that gives
/// every player their <see cref="TFMedigunComponent"/> (Stage 2.9, the shape of
/// 2.7's afterburn source).
/// </summary>
[SideService(Side.Server)]
public sealed class TFMedigunComponents(ITFPlayerHealing healing, ITFMedigunWeapons weapons, IEnginePlayers players)
    : IPlayerComponentSource, IOrderedHook
{
    /// <summary>Stage 2.9's band (+900..+929 of TF2's thousand).</summary>
    public int HookOrder => TFMedigunOrders.Components;

    public IEnumerable<IPlayerComponent> Create(EntityHandle player)
    {
        yield return new TFMedigunComponent(healing, weapons, players);
    }
}
