//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared.Bones;
using NVector = System.Numerics.Vector3;

namespace SourceSharp.Client.Ragdolls;

/// <summary>What a line traced into an entity's hitboxes answered.</summary>
/// <param name="Fraction">How far along the line it hit (1: it did not).</param>
/// <param name="EndPos">Where the line stopped.</param>
/// <param name="PhysicsBone">The physics bone of the hitbox it hit (<c>trace_t::physicsbone</c>).</param>
public readonly record struct RagdollHitboxHit(float Fraction, Vector EndPos, int PhysicsBone);

/// <summary>
/// <c>enginetrace-&gt;ClipRayToEntity( ray, MASK_SHOT, entity, &amp;tr )</c>, as a
/// bolt asks it of a body: <c>MASK_SHOT</c> carries <c>CONTENTS_HITBOX</c>, so
/// the answer is the entity's hitbox trace.
/// </summary>
public interface IRagdollHitboxTrace
{
    /// <summary>False when the entity has no hitboxes to trace (stock's trace then stays at fraction 1).</summary>
    bool ClipRayToEntity(EntityHandle entity, Vector start, Vector end, out RagdollHitboxHit hit);
}

/// <summary>The client world's physics object: stock's <c>GetWorldPhysObject()</c> (<c>g_PhysWorldObject</c>), 0 outside a level.</summary>
public interface IClientWorldPhysObject
{
    nint World { get; }
}

/// <summary>
/// One pin, as the report reads it back: the body, the element the physics
/// bone named, where the bolt stuck, where the line entered the element
/// (the element's end of the ballsocket, in the world, when it was made) and
/// that end in the element's own space.
/// </summary>
public readonly record struct BoltPinRecord(EntityHandle Owner, int PhysicsBone, nint Element, Vector BoltPoint,
                                            Vector EntryPoint, Vector ElementLocal, nint Constraint);

/// <summary>
/// <c>StickRagdollNow</c>'s enumeration (c_stickybolt.cpp): a body the bolt
/// passed through on its way to <paramref name="origin"/> is pinned there.
/// </summary>
public interface IRagdollBoltPin
{
    /// <summary>True when a body was pinned.</summary>
    bool Stick(Vector origin, Vector direction);
}

/// <summary>No ragdolls on this client: nothing is ever pinned.</summary>
public sealed class NoRagdollBoltPin : IRagdollBoltPin
{
    public bool Stick(Vector origin, Vector direction) => false;
}

/// <summary>
/// c_stickybolt.cpp's <c>CRagdollBoltEnumerator</c>: a crossbow bolt that
/// stuck at <c>origin</c> after flying along <c>direction</c> traces 128 units
/// back along its flight; the first body it passed through has the element
/// it hit doubled in mass and tied to the world by a ballsocket constraint,
/// the world's end at the bolt and the element's where the line entered it,
/// and <c>Weapon_Crossbow.BoltSkewer</c> plays from the world at the body.
///
/// <para>
/// <b>THE ENUMERATION.</b> Stock walks the spatial partition along the line
/// (<c>PARTITION_CLIENT_RESPONSIVE_EDICTS</c>) and skips every entity that is
/// not a ragdoll, so only the bodies <see cref="IClientRagdolls"/> holds can
/// answer; here each is traced, and the one the line enters first is the one
/// pinned. Stock stops at the first in the partition's walk, which visits the
/// line's cells from its start; two bodies in one cell may come in either
/// order there, and come nearest first here.
/// </para>
///
/// <para>
/// <b>THE CONSTRAINT IS NOT KEPT</b>, as stock keeps none: nothing destroys
/// it, and vphysics lets go of it when either object goes (the body's
/// elements when the corpse goes, the world at the level's end).
/// </para>
/// </summary>
public sealed class RagdollBoltPin(IClientRagdolls ragdolls, IRagdollHitboxTrace hitboxes, IRagdollVPhysics vphysics,
                                   IClientWorldPhysObject world, IFxGameSounds sounds) : IRagdollBoltPin
{
    /// <summary>How far back along the flight the line is traced.</summary>
    public const float TraceBack = 128f;

    /// <summary>The sound a pin plays.</summary>
    public const string SkewerSound = "Weapon_Crossbow.BoltSkewer";

    /// <summary>How many pins the report remembers (the oldest goes first).</summary>
    public const int Remembered = 32;

    private readonly List<BoltPinRecord> _made = [];

    /// <summary>Pins made since the module loaded, for the reports.</summary>
    public int Pins { get; private set; }

    /// <summary>The last <see cref="Remembered"/> pins, oldest first (cs_cl_boltpins).</summary>
    public IReadOnlyList<BoltPinRecord> Made => _made;

    /// <summary>
    /// Where the pinned element's end of the ballsocket is now, in the world.
    /// False when the body went, or no longer holds that element: its object
    /// is not read once it may have been destroyed.
    /// </summary>
    public bool AttachedWorld(in BoltPinRecord pin, out Vector at)
    {
        at = Vector.Zero;
        if (pin.Element == 0 || ragdolls.ElementObject(pin.Owner, pin.PhysicsBone) != pin.Element)
            return false;

        at = vphysics.LocalToWorld(pin.Element, pin.ElementLocal);
        return true;
    }

    public bool Stick(Vector origin, Vector direction)
    {
        // Vector vecEnd = vecOrigin - vecDirection * 128; shotRay.Init( vecOrigin, vecEnd );
        Vector end = origin - direction * TraceBack;

        EntityHandle best = EntityHandle.None;
        nint bestObject = 0;
        RagdollHitboxHit bestHit = default;

        foreach (EntityHandle owner in ragdolls.Owners)
        {
            if (!hitboxes.ClipRayToEntity(owner, origin, end, out RagdollHitboxHit hit))
                continue;

            // Find the real object we hit.
            if (hit.PhysicsBone < 0)
                continue;

            nint element = ragdolls.ElementObject(owner, hit.PhysicsBone);
            if (element == 0 || hit.Fraction >= 1f)
                continue;

            if (bestObject == 0 || hit.Fraction < bestHit.Fraction)
            {
                best = owner;
                bestObject = element;
                bestHit = hit;
            }
        }

        if (bestObject == 0)
            return false;

        nint reference = world.World;
        nint environment = vphysics.Environment();
        if (reference == 0 || environment == 0)
            return false;

        float mass = vphysics.Mass(bestObject);
        vphysics.SetMass(bestObject, mass * 2f);

        ConstraintBallsocketParams ballsocket = ConstraintBallsocketParams.Defaults();
        ballsocket.ConstraintPosition0 = vphysics.WorldToLocal(reference, origin);
        ballsocket.ConstraintPosition1 = vphysics.WorldToLocal(bestObject, bestHit.EndPos);

        nint constraint = vphysics.CreateBallsocketConstraint(environment, reference, bestObject, 0, ballsocket);
        Pins++;
        if (_made.Count == Remembered)
            _made.RemoveAt(0);
        _made.Add(new BoltPinRecord(best, bestHit.PhysicsBone, bestObject, origin, bestHit.EndPos,
                                     ballsocket.ConstraintPosition1, constraint));

        // CPASAttenuationFilter( pEnt ), CHAN_VOICE, from the world at the
        // entity's origin. The name is a sound script, so the script's own
        // volume, level and pitch play (no SND_CHANGE_* flag is set).
        ragdolls.State(best, out Vector at);
        if (sounds.Parameters(SkewerSound, out GameSoundParams p))
            sounds.EmitAtPosition(p, p.Volume, p.Pitch, at);

        return true;
    }
}

/// <summary>
/// <see cref="IClientWorldPhysObject"/> over the client's physics system, which
/// is made after the effects are, so it is asked for when a bolt lands.
/// </summary>
public sealed class ClientWorldPhysObject(Func<nint> world) : IClientWorldPhysObject
{
    public nint World => world();
}

/// <summary><see cref="IRagdollHitboxTrace"/> over the client's managed skeletons.</summary>
public sealed class ClientRagdollHitboxes(ClientSkeletons skeletons) : IRagdollHitboxTrace
{
    /// <summary><c>MASK_SHOT</c>.</summary>
    public const int MaskShot = 0x46004003;

    public bool ClipRayToEntity(EntityHandle entity, Vector start, Vector end, out RagdollHitboxHit hit)
    {
        var tr = new BoneTrace();

        if (!skeletons.TestHitboxes(entity, new NVector(start.X, start.Y, start.Z), new NVector(end.X, end.Y, end.Z),
                                    MaskShot, tr))
        {
            hit = new RagdollHitboxHit(1f, end, -1);
            return false;
        }

        hit = new RagdollHitboxHit(tr.Fraction, new Vector(tr.EndPos.X, tr.EndPos.Y, tr.EndPos.Z),
                                   tr.Fraction < 1f ? tr.PhysicsBone : -1);
        return true;
    }
}
