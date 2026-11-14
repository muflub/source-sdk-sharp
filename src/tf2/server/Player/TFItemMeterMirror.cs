//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// Copies the player's held-aside item meters into the carrier every frame —
/// the server half of the published pair, <see cref="TFPlayerNetState
/// .ItemMeters"/> and <see cref="TFPlayerNetState.ItemMeterTimes"/>. Stock's
/// charge rides the inventory, on the player
/// (<c>CTFPlayerShared::UpdateItemChargeMeters</c>,
/// tf_player_shared.cpp:7870-7904), so a holstered item's meter is the
/// client's from the frame it moved; our carry is the <see
/// cref="SourceSharp.Server.IWeaponGiver">arsenal's</see> per-player rows,
/// so the mirror is the frame's word instead, on the carried clip's
/// showing: one frame late at the widest, which is nothing the client's
/// next switch could see.
/// </summary>
public sealed class TFItemMeterMirror(IEngineEntities entities, TFPlayerCarriers carriers, IWeaponGiver arsenal)
    : IPlayerComponent
{
    private readonly IEngineEntities _entities = entities;
    private readonly TFPlayerCarriers _carriers = carriers;
    private readonly IWeaponGiver _arsenal = arsenal;

    private EntityHandle _player = EntityHandle.None;

    /// <summary>After the carried clips (51): the meters read what the frame settled.</summary>
    public int Order => 52;

    public void Attach(EntityHandle player) => _player = player;

    public void Tick(float now)
    {
        if (_player == EntityHandle.None
            || _carriers.Get(_entities.GetIndex(_player)) is not { } state)
            return;

        for (int slot = 0; slot < state.ItemMeters.Length; slot++)
        {
            float meter = 0f;
            float stamp = 0f;
            int row = _arsenal.CarriedRowAt(_player, slot);
            if (row >= 0)
                _arsenal.CarriedMeterOf(_player, row, out meter, out stamp);

            state.ItemMeters[slot] = meter;
            state.ItemMeterTimes[slot] = stamp;
        }
    }

    public void Detach() => _player = EntityHandle.None;
}
