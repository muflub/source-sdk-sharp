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
/// Copies the player's carried clips into the carrier every frame — the server
/// half of the published clip, <see cref="TFPlayerNetState.Clips"/>. Stock's
/// <c>m_iClip1</c> lives on each weapon entity and networks whether the weapon
/// is held or holstered (<c>basecombatweapon_shared.cpp</c>'s send table), so
/// the resupply's rewrite of a holstered reach the client at once; our carry is
/// the <see cref="SourceSharp.Server.IWeaponGiver">arsenal's</see> per-player
/// rows, so the mirror is the frame's word instead: one frame late at the
/// widest, which is nothing the client's next switch could see.
/// </summary>
public sealed class TFWeaponClipMirror(IEngineEntities entities, TFPlayerCarriers carriers, IWeaponGiver arsenal)
    : IPlayerComponent
{
    private readonly IEngineEntities _entities = entities;
    private readonly TFPlayerCarriers _carriers = carriers;
    private readonly IWeaponGiver _arsenal = arsenal;

    private EntityHandle _player = EntityHandle.None;

    /// <summary>After the pools (50): the clips read what the frame settled.</summary>
    public int Order => 51;

    public void Attach(EntityHandle player) => _player = player;

    public void Tick(float now)
    {
        if (_player == EntityHandle.None
            || _carriers.Get(_entities.GetIndex(_player)) is not { } state)
            return;

        for (int row = 0; row < state.Clips.Length; row++)
            state.Clips[row] = _arsenal.CarriedClipOf(_player, row);
    }

    public void Detach() => _player = EntityHandle.None;
}
