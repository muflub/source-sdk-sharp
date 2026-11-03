//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// Copies the player's ammo pools into the carrier every frame — the server
/// half of the published pool, <see cref="TFPlayerNetState.Ammo"/>. Stock's
/// m_iAmmo is the player's own array, written the moment anything moves it
/// (<c>RemoveAmmo</c> / <c>GiveAmmo</c>, basecombatcharacter_shared.cpp; the
/// resupply's fill, tf_player.cpp:4349) and networked from the snapshot. Our
/// inventory lives in the <see cref="IWeaponGiver">arsenal's</see> per-player
/// carry, so the mirror is the frame's word instead: one frame late at the
/// widest, which is nothing the client's next switch could see.
/// </summary>
public sealed class TFAmmoPoolMirror(IEngineEntities entities, TFPlayerCarriers carriers, IWeaponGiver arsenal)
    : IPlayerComponent
{
    private readonly IEngineEntities _entities = entities;
    private readonly TFPlayerCarriers _carriers = carriers;
    private readonly IWeaponGiver _arsenal = arsenal;

    private EntityHandle _player = EntityHandle.None;

    /// <summary>After the attributes (45): the pools read what the frame settled.</summary>
    public int Order => 50;

    public void Attach(EntityHandle player) => _player = player;

    public void Tick(float now)
    {
        if (_player == EntityHandle.None
            || _carriers.Get(_entities.GetIndex(_player)) is not { } state)
            return;

        for (var ammo = TFAmmo.Primary; ammo < TFAmmo.Count; ammo++)
            state.Ammo[(int)ammo] = _arsenal.PoolOf(_player, TFAmmoNames.Name(ammo), out int count) ? count : 0;
    }

    public void Detach() => _player = EntityHandle.None;
}
