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

namespace TF2.Server.Player;

/// <summary>
/// The Force-a-Nature's shove, the server's half of
/// <see cref="TFScattergunBehaviour"/>'s ask (<c>CTFScatterGun::FireBullet</c>'s
/// tail, tf_weapon_shotgun.cpp:353-359): the velocity becomes the shove, the
/// ground flag is stripped — the seam's clearGround, the flag and the ground
/// entity with it, as the airblast's comment carries — and the wielder eats
/// the 0.3 s forward-only movement stun the fire pays for.
/// </summary>
public sealed class TFScattergunKnockback(TFPlayerCarriers carriers, IEngineEntities entities) : IPlayerComponent
{
    /// <summary>Between the taunt attack (27) and the stun stack (30): the
    /// stun lands on a stack that is already there.</summary>
    public int Order => 28;

    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
        carriers.EnterPart(_index, this);
    }

    public void Tick(float now) { }

    public void Detach()
    {
        if (_index >= 0)
            carriers.LeavePart(_index, this);
        _index = -1;
        _player = EntityHandle.None;
    }

    /// <summary>The shove: the stun (stock's attackerless call, :335), then the
    /// delta off the velocity the engine holds, ground stripped.</summary>
    public void Knock(Vector shoved)
    {
        if (_player == EntityHandle.None || !entities.IsValid(_player))
            return;

        carriers.Part<TFStunStack>(_index)?.Stun(_player, TFScattergunBehaviour.StunTime, 1f,
            TFStunFlags.Movement | TFStunFlags.MovementForwardOnly, EntityHandle.None);
        entities.ApplyVelocityImpulse(_player, shoved - entities.GetVelocity(_player), clearGround: true);
    }
}
