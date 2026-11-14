//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Combat;

using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// The Force-a-Nature's shove, the server's half of
/// <see cref="TFScattergunBehaviour"/>'s ask (<c>CTFScatterGun::FireBullet</c>'s
/// tail, tf_weapon_shotgun.cpp:353-359): the velocity becomes the shove, the
/// ground flag is stripped — the seam's clearGround, the flag and the ground
/// entity with it, as the airblast's comment carries — and the wielder eats
/// the 0.3 s forward-only movement stun the fire pays for.
///
/// <para>
/// The tick is the wielder's OTHER half, spent: the victim's banked shoves
/// (<c>ApplyPostHitEffects</c>' shot-total gate, the damage layer's flush) —
/// the frame's damage is long since dealt when this stage runs.
/// </para>
/// </summary>
public sealed class TFScattergunKnockback(TFPlayerCarriers carriers, IEngineEntities entities,
                                          TFDamageLayer layer) : IPlayerComponent
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

    public void Tick(float now) => layer.FlushShoves(_index);

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

    /// <summary>
    /// The Thermal Thruster's launch, the server's half of
    /// <see cref="TFRocketPackBehaviour"/>'s ask (<c>RocketLaunchPlayer</c>,
    /// tf_weapon_rocketpack.cpp:453-478): the preserve-convars' default zeroes
    /// the carrier before the impulse (:465-477), so the flight force is the
    /// whole velocity — the ground flag stays, unlike the knock's — and the
    /// half-second movement stun lands under the cond's guard (:461). The pop
    /// calls with its smaller velocity the same way.
    /// </summary>
    public void Thrust(Vector velocity)
    {
        if (_player == EntityHandle.None || !entities.IsValid(_player))
            return;

        carriers.Part<TFStunStack>(_index)?.Stun(_player, 0.5f, 1f, TFStunFlags.Movement, EntityHandle.None);
        entities.ApplyVelocityImpulse(_player, velocity - entities.GetVelocity(_player), clearGround: false);
    }
}
