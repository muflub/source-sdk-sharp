//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Client.Ragdolls;

/// <summary>
/// ragdoll_shared.h's <c>ragdollelement_t</c>: one limb. <see cref="Object"/>
/// and <see cref="Constraint"/> are vphysics' pointers, held as words and
/// called through their vtables (IRagdollVPhysics).
/// </summary>
public sealed class RagdollElement
{
    public Vector OriginParentSpace;

    /// <summary>All valid elements have an object.</summary>
    public nint Object;

    /// <summary>All valid elements have a constraint, except the root.</summary>
    public nint Constraint;

    public int ParentIndex;

    internal void Clear()
    {
        OriginParentSpace = Vector.Zero;
        Object = 0;
        Constraint = 0;
        ParentIndex = 0;
    }
}

/// <summary>ragdoll_shared.h's <c>ragdollanimatedfriction_t</c>.</summary>
public struct RagdollAnimatedFriction
{
    public float FrictionTimeIn;
    public float FrictionTimeOut;
    public float FrictionTimeHold;
    public int MinAnimatedFriction;
    public int MaxAnimatedFriction;
}

/// <summary>
/// One jointed body: ragdoll_shared.h's <c>ragdoll_t</c> and the members
/// game/client/ragdoll.cpp's <c>CRagdoll</c> keeps beside it (its bounds,
/// its radius, and the settle detector's clock), plus who it belongs to.
/// </summary>
public sealed class RagdollBody
{
    /// <summary><c>RAGDOLL_MAX_ELEMENTS</c>.</summary>
    public const int MaxElements = 24;

    public RagdollBody()
    {
        for (int i = 0; i < MaxElements; i++)
            List[i] = new RagdollElement();
    }

    // ---- ragdoll_t ------------------------------------------------------------

    public int ListCount;

    public bool AllowStretch;

    /// <summary>The <c>IPhysicsConstraintGroup</c>.</summary>
    public nint Group;

    public readonly RagdollElement[] List = new RagdollElement[MaxElements];

    public readonly int[] BoneIndex = new int[MaxElements];

    public RagdollAnimatedFriction AnimFriction;

    // ---- CRagdoll -----------------------------------------------------------

    /// <summary><c>m_mins</c>/<c>m_maxs</c>: the cull box, relative to the origin.</summary>
    public Vector Mins;

    public Vector Maxs;

    /// <summary><c>m_radius</c>: half the model's diagonal.</summary>
    public float Radius;

    /// <summary><c>m_lastUpdate</c>: the curtime VPhysicsUpdate last ran at (-FLT_MAX: never).</summary>
    public float LastUpdate = -float.MaxValue;

    /// <summary><c>m_allAsleep</c>.</summary>
    public bool AllAsleep;

    /// <summary><c>m_vecLastOrigin</c>.</summary>
    public Vector LastOrigin;

    /// <summary><c>m_flLastOriginChangeTime</c> (-1: never moved).</summary>
    public float LastOriginChangeTime = -1f;

    // ---- the body's place in the client ----------------------------------------

    /// <summary>The entity this body belongs to (the native library's owner word).</summary>
    public EntityHandle Owner = EntityHandle.None;

    /// <summary>The tick the body was made on (stock's creation-tick memory).</summary>
    public int CreateTick = -1;

    /// <summary>A number no other body of this process has had; the skeleton's cache key for it.</summary>
    public uint Serial;

    /// <summary>Stock's <c>IsValid</c>: a body with at least one element.</summary>
    public bool IsValid => ListCount > 0;
}

/// <summary>
/// ragdoll_shared.h's <c>ragdollparams_t</c>: what a body is built from.
/// </summary>
public sealed class RagdollParams
{
    /// <summary>The owner's game data word (physics_gamedata.h), or 0.</summary>
    public nint GameData;

    public RagdollCollide Collide;

    public StudioHdr StudioHdr = null!;

    /// <summary>The model's name, as native text vphysics may keep (<c>pszName()</c>).</summary>
    public nint Name;

    public int ModelIndex;

    public Vector ForcePosition;

    public Vector ForceVector;

    public int ForceBoneIndex;

    /// <summary>Bone-to-world, one per bone of the model: the pose the body collapses from.</summary>
    public BoneMatrix3x4[] CurrentBones = [];

    public float JointFrictionScale = 1f;

    public bool AllowStretch;

    public bool FixedConstraints;
}
