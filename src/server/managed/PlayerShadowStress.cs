//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// CBasePlayer::VPhysicsUpdate's stress for one player's awake hull: stock's
/// <c>CalculateObjectStress( VPhysicsGetObject(), this, &amp;stressOut )</c> as
/// CBaseCombatCharacter::CalculatePhysicsStressDamage asks it, with each
/// contact's facts read from vphysics and the sum
/// <see cref="PhysicsObjectStress"/> (plan_physics_bones_native_zero 2.3;
/// native's CStandalonePlayer::ShadowStress and object_stress.h at
/// bc21e4d46).
///
/// <para>
/// Two of stock's friendly tests have no subject in this mod: a liked
/// relationship (HL2MP's rules never fill the relationship table) and a
/// nav-ignored pusher (only logic_navigation and HL2's NPCs set it). The
/// third, a player-held object, is the game flag and is read. Contacts
/// aggregate under the other object's owner (its handle; the world, or
/// nobody, as stock's key); 64 at most.
/// </para>
/// </summary>
public sealed class PlayerShadowStress(IVPhysicsFrameCalls v, IPhysicsSolverWorld world)
{
    /// <summary>FVPHYSICS_PLAYER_HELD.</summary>
    public const ushort PlayerHeld = 0x0004;

    /// <summary>FVPHYSICS_PENETRATING.</summary>
    public const ushort Penetrating = 0x0040;

    /// <summary>CALLBACK_IS_PLAYER_CONTROLLER.</summary>
    public const ushort IsPlayerController = 0x2000;

    /// <summary>MOVETYPE_VPHYSICS.</summary>
    public const int MoveTypeVPhysics = 6;

    /// <summary>How many pushing contacts are summed (stock's fixed table).</summary>
    public const int MaxContacts = 64;

    // The world's key: never an entity's (a handle plus one) and never nobody's (0).
    private const nint WorldKey = -1;

    private readonly SnapshotContact[] _snapshot = new SnapshotContact[256];
    private readonly StressContact[] _contacts = new StressContact[MaxContacts];

    /// <summary>
    /// The stress on <paramref name="shadow"/>, the awake active hull of the
    /// player <paramref name="facts"/> describes, as a STRESS event's fields:
    /// the received stress (kg), the hull's mass and the Stress* bits.
    /// </summary>
    public (float Received, float Mass, uint Flags) Measure(nint environment, nint shadow, in PlayerShadowFacts facts)
    {
        int total = v.Contacts(shadow, _snapshot);
        int read = Math.Min(total, _snapshot.Length);
        nint worldObject = world.WorldObject();

        int n = 0;
        for (int i = 0; i < read && n < MaxContacts; i++)
        {
            SnapshotContact c = _snapshot[i];
            if (!(c.NormalForce > 0f))
                continue;

            nint other = c.Other;
            nint key = 0;
            int moveType = 0;
            EntityHandle owner = EntityHandle.None;
            if (other != 0 && other == worldObject)
            {
                key = WorldKey;
            }
            else
            {
                owner = PhysicsGameData.Owner(v.GameData(other));
                if (owner != EntityHandle.None && world.TryEntity(owner, out SolverEntity f))
                {
                    key = (nint)owner.Value + 1;
                    moveType = f.MoveType;
                }
                else
                {
                    owner = EntityHandle.None;
                }
            }

            (bool hasShadow, bool physicallyControlled, _) = v.ShadowController(other);
            _contacts[n++] = new StressContact(
                c.NormalForce, c.Normal, key, v.IsMoveable(other),
                Friendly: (v.GameFlags(other) & PlayerHeld) != 0,
                PhysicallyControlled: moveType == MoveTypeVPhysics || (hasShadow && physicallyControlled),
                IsOwnersGround: owner != EntityHandle.None && owner.Value == facts.Ground,
                Mass: v.Mass(other));
        }

        // The impulse the hull's controller last applied: a shadow
        // controller's, or the player controller's when the hull is one.
        Vector lastImpulse = Vector.Zero;
        (bool shadowControlled, _, Vector shadowImpulse) = v.ShadowController(shadow);
        if (shadowControlled)
            lastImpulse = shadowImpulse;
        else if ((v.CallbackFlags(shadow) & IsPlayerController) != 0)
            lastImpulse = v.PlayerControllerLastImpulse(facts.Controller);

        float mass = v.Mass(shadow);
        var subject = new StressSubject(mass, v.IsMoveable(shadow), v.IsStatic(shadow),
                                        facts.MoveType == MoveTypeVPhysics, lastImpulse,
                                        v.Gravity(environment).Length, v.Timestep(environment));

        ObjectStress stress = PhysicsObjectStress.Compute(in subject, _contacts.AsSpan(0, n));

        uint flags = 0;
        if (stress.HasNonStaticStress)
            flags |= PhysicsEvent.StressNonStatic;
        if (stress.HasLargeObjectContact)
            flags |= PhysicsEvent.StressLargeObject;
        if ((v.GameFlags(shadow) & Penetrating) != 0)
            flags |= PhysicsEvent.StressPenetrating;

        return (stress.ReceivedStress, mass, flags);
    }
}
