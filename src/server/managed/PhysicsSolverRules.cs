//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// One side of a candidate pair as <see cref="PhysicsSolverRules"/> reads it:
/// the owner's engine block and the object's own answers, read once
/// (physics_shell.h's SourceSharpCollisionSide_t). An unowned side is all zero.
/// </summary>
public struct CollisionSide
{
    public bool Valid;            // somebody owns it
    public int CollisionGroup;    // Collision_Group_t
    public int Solid;             // SolidType_t
    public int SolidFlags;        // FSOLID_*
    public int MoveType;          // MoveType_t
    public uint SolidMask;        // PhysicsSolidMaskForEntity
    public uint GameFlags;        // IPhysicsObject::GetGameFlags
    public uint Contents;         // IPhysicsObject::GetContents
    public bool Moveable;         // IPhysicsObject::IsMoveable
    public bool Trigger;          // IPhysicsObject::IsTrigger
    public bool Shadow;           // has a shadow controller
}

/// <summary>
/// The facts about a pair that are not about either side alone (physics_shell.h's
/// SourceSharpCollisionPairFacts_t): inputs, not rules, so the evaluation can
/// run stock's own order end to end.
/// </summary>
public struct CollisionPairFacts
{
    public bool SameEntity;       // both objects belong to one entity
    public bool WheelRefused;     // a vehicle-wheel rule has already said no
    public bool ForcedCollision;  // either side forces collision with the other
    public bool PairInHash;       // the game disabled this pair
    public bool OwnerPair;        // one side owns the other, both networked
    public bool SameRoot;         // both under one root move parent
}

/// <summary>Which clause said no; <see cref="Allowed"/> when none did.</summary>
public enum CollisionRefusal
{
    Allowed = 0,
    Unowned,
    SelfNoCollide,
    SameEntity,
    MutuallyStatic,
    Wheel,
    AiMoverImmovable,
    AiMoverPair,
    ShadowPair,
    SolidType,
    NotSolid,
    Trigger,
    Group,
    Contents,
    PairInHash,
    Owner,
    SameRoot,
    Count,
}

/// <summary>
/// Stock's <c>CCollisionEvent::ShouldCollide</c> rules (game/server/physics.cpp),
/// in stock's order, over two <see cref="CollisionSide"/>s and the pair's
/// <see cref="CollisionPairFacts"/>: the managed port of native's
/// SourceSharpCollisionRuleTable::Evaluate (physics_shell.h), with that table's
/// stock defaults as constants and the group matrix
/// <see cref="CollisionRules.ShouldCollide"/> builds.
///
/// <para>
/// THE ORDER IS LOAD-BEARING: a pair both mutually static and force-collided
/// answers 0 because the static clause comes first; "not solid" comes after
/// the trigger admission so a vphysics trigger still sees a solid object; the
/// group matrix comes after both.
/// </para>
/// </summary>
public sealed class PhysicsSolverRules
{
    /// <summary>FVPHYSICS_NO_SELF_COLLISIONS.</summary>
    public const uint NoSelfCollisionGameFlags = 0x8000;

    /// <summary>FVPHYSICS_CONSTRAINT_STATIC.</summary>
    public const uint MutuallyStaticGameFlags = 0x0002;

    /// <summary>SourceSharp's force-collide bit (func_clip_vphysics, physics stock parity A5).</summary>
    public const uint ForceCollideGameFlags = 0x1000;

    /// <summary>FSOLID_NOT_SOLID.</summary>
    public const int NotSolidFlag = 0x0004;

    /// <summary>FSOLID_TRIGGER.</summary>
    public const int TriggerFlag = 0x0008;

    public const int SolidNone = 0;
    public const int SolidBsp = 1;
    public const int SolidVPhysics = 6;

    public const int MoveTypeNone = 0;
    public const int MoveTypeVPhysics = 6;
    public const int MoveTypePush = 7;

    private readonly uint[] _groupRows;

    public PhysicsSolverRules() : this(CollisionRules.BuildMatrix())
    {
    }

    /// <summary>The group matrix: row g0, bit g1 is "g0 collides with g1".</summary>
    public PhysicsSolverRules(uint[] groupRows)
    {
        _groupRows = (uint[])groupRows.Clone();
    }

    /// <summary>
    /// The group matrix alone (CGameRules::ShouldCollide). A group it cannot
    /// index collides, as stock's answer for a group it did not name.
    /// </summary>
    public bool GroupsCollide(int g0, int g1)
    {
        if ((uint)g0 >= (uint)_groupRows.Length || (uint)g1 >= (uint)_groupRows.Length)
            return true;
        return (_groupRows[g0] & (1u << g1)) != 0;
    }

    /// <summary>How many of the first <paramref name="groups"/> rows let their group collide with anything.</summary>
    public int RowsSet(int groups)
    {
        int set = 0;
        for (int g = 0; g < groups && g < _groupRows.Length; g++)
        {
            if (_groupRows[g] != 0)
                set++;
        }

        return set;
    }

    /// <summary>The whole rule: 1 to collide, 0 not to, and which clause refused.</summary>
    public int Evaluate(in CollisionSide s0, in CollisionSide s1, in CollisionPairFacts facts,
                        out CollisionRefusal reason)
    {
        reason = CollisionRefusal.Allowed;

        // Nobody owns one of them: the game has no rule for it, and stock collides.
        if (!s0.Valid || !s1.Valid)
            return 1;

        // Two objects of one entity.
        if (facts.SameEntity)
        {
            if (((s0.GameFlags | s1.GameFlags) & NoSelfCollisionGameFlags) != 0)
                return Refuse(CollisionRefusal.SelfNoCollide, out reason);
            return 1;
        }

        // Both constrained to the world. BEFORE the forced-collision arm.
        if ((s0.GameFlags & s1.GameFlags & MutuallyStaticGameFlags) != 0)
            return Refuse(CollisionRefusal.MutuallyStatic, out reason);

        if (facts.WheelRefused)
            return Refuse(CollisionRefusal.Wheel, out reason);

        if (facts.ForcedCollision)
            return 1;

        // func_clip_vphysics with no filter, before the solid rules would
        // refuse its NOT_SOLID.
        if (ForcesCollision(s0, s1) || ForcesCollision(s1, s0))
            return 1;

        // An owner and what it owns, then one hierarchy (stock physics.cpp:514-529).
        if (facts.OwnerPair)
            return Refuse(CollisionRefusal.Owner, out reason);
        if (facts.SameRoot)
            return Refuse(CollisionRefusal.SameRoot, out reason);

        bool ai0 = s0.MoveType == MoveTypePush;
        bool ai1 = s1.MoveType == MoveTypePush;

        // AI movers do not collide with what cannot move, nor with each other.
        if ((ai0 && !s1.Moveable) || (ai1 && !s0.Moveable))
            return Refuse(CollisionRefusal.AiMoverImmovable, out reason);
        if (ai0 && ai1)
            return Refuse(CollisionRefusal.AiMoverPair, out reason);

        // Two shadow-controlled objects: the AI will figure it out.
        if (s0.Shadow && s1.Shadow)
            return Refuse(CollisionRefusal.ShadowPair, out reason);

        if (s0.Solid == SolidNone || s1.Solid == SolidNone)
            return Refuse(CollisionRefusal.SolidType, out reason);

        // Not solid collides with nothing, unless the other side is a vphysics
        // trigger and this one is solid.
        if (((s0.SolidFlags | s1.SolidFlags) & NotSolidFlag) != 0)
        {
            if (s0.Trigger && (s1.SolidFlags & NotSolidFlag) == 0)
                return 1;
            if (s1.Trigger && (s0.SolidFlags & NotSolidFlag) == 0)
                return 1;
            return Refuse(CollisionRefusal.NotSolid, out reason);
        }

        // A trigger only collides with what is solid in the vphysics sense.
        if ((s0.SolidFlags & TriggerFlag) != 0 && !TriggerAdmits(s1))
            return Refuse(CollisionRefusal.Trigger, out reason);
        if ((s1.SolidFlags & TriggerFlag) != 0 && !TriggerAdmits(s0))
            return Refuse(CollisionRefusal.Trigger, out reason);

        if (!GroupsCollide(s0.CollisionGroup, s1.CollisionGroup))
            return Refuse(CollisionRefusal.Group, out reason);

        // Contents against the other side's solid mask, both ways.
        if ((s0.Contents & s1.SolidMask) == 0 || (s1.Contents & s0.SolidMask) == 0)
            return Refuse(CollisionRefusal.Contents, out reason);

        // Last, as stock: a pair the game disabled explicitly.
        if (facts.PairInHash)
            return Refuse(CollisionRefusal.PairInHash, out reason);

        return 1;
    }

    private static int Refuse(CollisionRefusal why, out CollisionRefusal reason)
    {
        reason = why;
        return 0;
    }

    // CFuncVPhysicsClip::EntityPassesFilter's no-filter arm.
    private static bool ForcesCollision(in CollisionSide forcer, in CollisionSide other)
        => (forcer.GameFlags & ForceCollideGameFlags) != 0 && other.MoveType == MoveTypeVPhysics && other.Moveable;

    // SOLID_VPHYSICS or SOLID_BSP, or moved by MOVETYPE_VPHYSICS.
    private static bool TriggerAdmits(in CollisionSide s)
        => s.Solid == SolidVPhysics || s.Solid == SolidBsp || s.MoveType == MoveTypeVPhysics;
}
