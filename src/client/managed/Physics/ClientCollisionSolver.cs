//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Physics;

/// <summary>What the client's collision solver reads.</summary>
public interface IClientCollisionFacts
{
    /// <summary><c>IPhysicsObject::GetGameFlags</c>.</summary>
    ushort GameFlags(nint physicsObject);

    /// <summary>The owner's collision group (stock's <c>GetCollisionGroup()</c>).</summary>
    EngineCollisionGroup GroupOf(EntityHandle owner);

    /// <summary><c>IPhysicsObject::GetContents</c>.</summary>
    uint Contents(nint physicsObject);

    /// <summary>Whether <c>IPhysicsObject::GetShadowController</c> answers one.</summary>
    bool HasShadowController(nint physicsObject);

    /// <summary><c>cl_ragdoll_collide</c>.</summary>
    bool RagdollCollide { get; }
}

/// <summary>
/// The client's IPhysicsCollisionSolver decisions, as stock's client
/// CCollisionEvent made them (game/client/physics.cpp), for objects whose game
/// data names a managed owner (plan_client_novalve C6, cluster 9; lane
/// c6-input). An object with no owner word -- the world, which this build
/// gives no game data -- collides, as stock's <c>!pEntity</c> arm answers.
/// </summary>
public sealed class ClientCollisionSolver(IClientCollisionFacts facts) : IClientCollisionSolverDecisions
{
    /// <summary>FVPHYSICS_PART_OF_RAGDOLL.</summary>
    private const ushort PartOfRagdoll = 0x0008;

    /// <summary>FVPHYSICS_NO_SELF_COLLISIONS.</summary>
    private const ushort NoSelfCollisions = 0x8000;

    /// <summary>
    /// MASK_SOLID, CBaseEntity's own PhysicsSolidMaskForEntity and the answer
    /// for every owner on this side (physicsowner.h's neutral for the question).
    /// </summary>
    private const uint MaskSolid = 0x1 | 0x2 | 0x8 | 0x4000 | 0x2000000;   // SOLID|WINDOW|GRATE|MOVEABLE|MONSTER

    /// <summary>
    /// CCollisionEvent::ShouldCollide (client): owners resolved from the game
    /// data, the per-entity self-collision switch, the game rules' group
    /// table, cl_ragdoll_collide between two ragdolls, both objects' contents
    /// against the other's solid mask, and two shadow-controlled objects.
    /// <para>
    /// Three stock arms have no subject here, named rather than dropped: the
    /// entity-collision pair hash (nothing on this client disables a pair, so
    /// it is empty), and the AI-mover arms (a client ragdoll has neither a move
    /// parent nor MOVETYPE_PUSH).
    /// </para>
    /// </summary>
    /// <summary>Pairs asked about, and the ones answered "no", since the process began.</summary>
    public long Asked { get; private set; }

    /// <inheritdoc cref="Asked"/>
    public long Refused { get; private set; }

    public int ShouldCollide(nint obj0, nint obj1, nint gameData0, nint gameData1)
    {
        Asked++;
        int answer = Decide(obj0, obj1, gameData0, gameData1);
        if (answer == 0)
            Refused++;
        return answer;
    }

    private int Decide(nint obj0, nint obj1, nint gameData0, nint gameData1)
    {
        EntityHandle owner0 = PhysicsGameData.Owner(gameData0);
        EntityHandle owner1 = PhysicsGameData.Owner(gameData1);
        if (owner0 == EntityHandle.None || owner1 == EntityHandle.None)
            return 1;

        ushort flags0 = facts.GameFlags(obj0);
        ushort flags1 = facts.GameFlags(obj1);

        if (owner0 == owner1)
        {
            // allow all-or-nothing per-entity disable
            return ((flags0 | flags1) & NoSelfCollisions) != 0 ? 0 : 1;
        }

        // Obey collision group rules
        if (!CollisionRules.ShouldCollide(facts.GroupOf(owner0), facts.GroupOf(owner1)))
            return 0;

        if ((flags0 & PartOfRagdoll) != 0 && (flags1 & PartOfRagdoll) != 0 && !facts.RagdollCollide)
            return 0;

        // check contents
        if ((facts.Contents(obj0) & MaskSolid) == 0 || (facts.Contents(obj1) & MaskSolid) == 0)
            return 0;

        // two objects under shadow control should not collide
        if (facts.HasShadowController(obj0) && facts.HasShadowController(obj1))
            return 0;

        return 1;
    }

    /// <summary>
    /// Every pair goes to the default solver. Stock's client
    /// <c>CCollisionEvent::ShouldSolvePenetration</c> first hands a ragdoll
    /// overlapping itself to its own constraint group
    /// (<c>IPhysicsConstraintGroup::SolvePenetration</c>) and answers 0; that
    /// arm is not carried yet (docs/todo.md), and
    /// <see cref="SelfPenetrationSolvedByOwner"/> is its rule.
    /// </summary>
    public int ShouldSolvePenetration(nint obj0, nint obj1, nint gameData0, nint gameData1, float dt) => 1;

    /// <summary>
    /// Whether a penetrating pair is a ragdoll overlapping ITSELF, which stock
    /// has the ragdoll's constraint group solve: both objects one owner's,
    /// and the first a ragdoll part. A pair with no game data is NOT one
    /// owner's, though two nulls compare equal: a standalone ragdoll's objects
    /// once carried none, and stock's <c>pGameData0 == pGameData1</c> then
    /// dereferenced a null owner (the native rule this ports,
    /// self_penetration_rule.h, held that guard).
    /// </summary>
    public static bool SelfPenetrationSolvedByOwner(bool hasGameData, bool sameGameData, bool partOfRagdoll)
        => hasGameData && sameGameData && partOfRagdoll;

    public bool ShouldFreezeObject(nint obj) => true;

    public int AdditionalCollisionChecksThisTick(int currentChecksDone) => 0;

    public bool ShouldFreezeContacts(int objectCount) => true;
}
