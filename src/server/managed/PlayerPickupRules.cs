//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// <c>hl2mp_allow_pickup</c> (hl2mp_player.cpp:407): whether a player's +USE
/// carries what it is used on. Off in stock HL2MP.
/// </summary>
[ConVar("hl2mp_allow_pickup", "0")]
public sealed class Hl2mpAllowPickupConVar : BoolConVar { }

/// <summary>
/// What a player's +USE may do with a physics object.
/// <see cref="CanPickupObject"/> is CBasePlayer::CanPickupObject
/// (player.cpp:2814), which a prop's and a physbox's object caps ask;
/// <see cref="PickupObject"/> is CHL2MP_Player::PickupObject
/// (hl2mp_player.cpp:409) over CHL2_Player's, which stock HL2MP turns away
/// unless <c>hl2mp_allow_pickup</c> is on.
///
/// <para>
/// The carry itself, CPlayerPickupController (weapon_physcannon.cpp:655), is
/// not ported: with the convar on, a pickup that would start is refused and
/// said once (docs/divergences.md).
/// </para>
/// </summary>
public sealed class PlayerPickupRules
{
    /// <summary>CHL2_Player::PickupObject's mass limit, kg.</summary>
    public const float MassLimit = 35f;

    /// <summary>CHL2_Player::PickupObject's size limit: no side of the box longer.</summary>
    public const float SizeLimit = 128f;

    /// <summary>FVPHYSICS_NO_PLAYER_PICKUP.</summary>
    private const ushort NoPlayerPickup = 0x0080;

    private readonly IEngineEntities _entities;
    private readonly IEngineEntityPhysics _physics;
    private readonly IVPhysicsGrabCalls _v;
    private readonly IEntityDirectory _directory;
    private readonly ConVarRegistry _convars;
    private readonly ILog _log;

    private bool _saidCarryMissing;

    public PlayerPickupRules(IEngineEntities entities, IEngineEntityPhysics physics, IVPhysicsGrabCalls v,
                             IEntityDirectory directory, ConVarRegistry convars, ILog log)
    {
        _entities = entities;
        _physics = physics;
        _v = v;
        _directory = directory;
        _convars = convars;
        _log = log;
    }

    /// <summary>
    /// CBasePlayer::CanPickupObject: moves with physics, light enough, small
    /// enough, not refused by its flags, and either free to move or allowed
    /// to be freed by the gravity gun. One object per entity on this side
    /// (VPhysicsGetObjectList's list is the entity's object), and nothing here
    /// is ever hinged: PhysHinge does not call BecomeHinged.
    /// </summary>
    public bool CanPickupObject(EntityHandle obj, float massLimit = MassLimit, float sizeLimit = SizeLimit)
    {
        if (obj == EntityHandle.None || !_entities.IsValid(obj))
            return false;

        if (EngineBlockMoveType.Read(_entities, obj) != MoveType.VPhysics)
            return false;

        nint phys = _physics.PhysicsObject(obj);
        if (phys == 0)
            return false;

        if ((_v.GameFlags(phys) & NoPlayerPickup) != 0)
            return false;

        if (massLimit > 0f && _v.Mass(phys) > massLimit)
            return false;

        // Allow pickup of phys props that are motion enabled on player pickup.
        if (!_v.IsMoveable(phys) && _directory.Get(obj) is not IPhysGunTarget { EnablesOnPhyscannon: true })
            return false;

        if (sizeLimit > 0f)
        {
            var (mins, maxs) = _entities.GetBounds(obj);
            Vector size = maxs - mins;
            if (size.X > sizeLimit || size.Y > sizeLimit || size.Z > sizeLimit)
                return false;
        }

        return true;
    }

    /// <summary>Whether stock HL2MP would let a +USE carry anything (<c>hl2mp_allow_pickup</c>).</summary>
    public bool AllowPickup => _convars.Get<Hl2mpAllowPickupConVar>()?.Value ?? false;

    /// <summary>
    /// CHL2MP_Player::PickupObject: nothing unless <see cref="AllowPickup"/>;
    /// then CHL2_Player's: not what the player stands on, and within the
    /// limits. Answers whether a carry would have started, which this side
    /// then refuses (the class note).
    /// </summary>
    public bool PickupObject(EntityHandle player, EntityHandle obj, bool limitMassAndSize = true)
    {
        if (!AllowPickup)
            return false;

        if (_directory.Get(player) is SsPlayer p && p.GroundIndex == _entities.GetIndex(obj))
            return false;

        if (limitMassAndSize && !CanPickupObject(obj))
            return false;

        if (!_saidCarryMissing)
        {
            _saidCarryMissing = true;
            _log.Warning("[SourceSharp] hl2mp_allow_pickup: the +USE carry (CPlayerPickupController) is not ported; " +
                         "the pickup is refused\n");
        }

        return true;
    }
}
