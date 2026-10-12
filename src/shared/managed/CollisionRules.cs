//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// Which collision groups collide with which — declared here, pushed to native
/// as a matrix at startup.
///
/// <para>
/// <b>What this replaces.</b> <c>CGameRules::ShouldCollide</c>: sixty-seven
/// lines of ordered <c>if</c>s over two group indices. It was pure integer
/// logic — no entity, no engine call — and total over a twenty-by-twenty
/// domain, which is what makes a table able to hold it exactly.
/// </para>
///
/// <para>
/// <b>Why a table and not a managed call.</b> This is asked once per traced
/// entity, from <c>CTraceFilterSimple</c>, so it runs thousands of times a
/// frame. A crossing per call is out of the question and so is branching
/// through fourteen comparisons; the answer is now one masked load.
/// </para>
///
/// <para>
/// <b>Both sides register it.</b> Unlike the rules' policy table, collision is
/// asked on the client too — <c>util_shared.cpp</c> is shared and the trace
/// filter runs during prediction. If the two sides answered from different
/// tables the client would predict collisions the server disagreed with, and
/// that shows up as jitter rather than as an error.
/// </para>
///
/// <para>
/// <b>The rules are stated once, as a predicate.</b> The matrix is derived from
/// it rather than written out, so there is one statement of each rule — which
/// is the same complaint this port makes about stock spelling every damage mask
/// twice.
/// </para>
/// </summary>
public static class CollisionRules
{
    /// <summary>LAST_SHARED_COLLISION_GROUP.</summary>
    public const int GroupCount = (int)EngineCollisionGroup.Count;

    /// <summary>
    /// Whether two groups collide.
    ///
    /// <para>
    /// A transcription of stock's predicate, in stock's order, because the
    /// order is load-bearing: several of these overlap and the first match
    /// wins. The <c>HL2MP</c> arms are included — this mod does not define it,
    /// so stock compiles them.
    /// </para>
    /// </summary>
    public static bool ShouldCollide(EngineCollisionGroup a, EngineCollisionGroup b)
    {
        // Stock sorts first, which is what makes the relation symmetric.
        if (a > b)
            (a, b) = (b, a);

        // Players do not collide with pushaway props; the player-pushaway code
        // moves them instead.
        if ((a == EngineCollisionGroup.Player || a == EngineCollisionGroup.PlayerMovement)
            && b == EngineCollisionGroup.PushAway)
            return false;

        // ...but debris does, and this must precede the blanket debris rule
        // below or it would never be reached.
        if (a == EngineCollisionGroup.Debris && b == EngineCollisionGroup.PushAway)
            return true;

        // Anything inside a vehicle collides with nothing.
        if (a == EngineCollisionGroup.InVehicle || b == EngineCollisionGroup.InVehicle)
            return false;

        // Door blockers stop everything that is not an NPC.
        if (b == EngineCollisionGroup.DoorBlocker && a != EngineCollisionGroup.Npc)
            return false;

        if (a == EngineCollisionGroup.Player && b == EngineCollisionGroup.PassableDoor)
            return false;

        // Debris hits world and static only. After the pushaway exception.
        if (a == EngineCollisionGroup.Debris || a == EngineCollisionGroup.DebrisTrigger)
            return false;

        // Dissolving things collide with nothing but the ungrouped.
        if (a == EngineCollisionGroup.Dissolving || b == EngineCollisionGroup.Dissolving)
        {
            if (a != EngineCollisionGroup.None)
                return false;
        }

        if (a == EngineCollisionGroup.InteractiveDebris && b == EngineCollisionGroup.InteractiveDebris)
            return false;

        if (a == EngineCollisionGroup.InteractiveDebris
            && (b == EngineCollisionGroup.PlayerMovement || b == EngineCollisionGroup.Player))
            return false;

        if (a == EngineCollisionGroup.BreakableGlass && b == EngineCollisionGroup.BreakableGlass)
            return false;

        if (b == EngineCollisionGroup.Interactive && a != EngineCollisionGroup.None)
            return false;

        if (b == EngineCollisionGroup.Projectile)
        {
            if (a == EngineCollisionGroup.Debris || a == EngineCollisionGroup.Weapon
                || a == EngineCollisionGroup.Projectile)
                return false;
        }

        if (b == EngineCollisionGroup.Weapon)
        {
            if (a == EngineCollisionGroup.Vehicle || a == EngineCollisionGroup.Player
                || a == EngineCollisionGroup.Npc)
                return false;
        }

        // Vehicle clips stop vehicles and nothing else — note this RETURNS in
        // both arms, so it is the last word for any pair involving one.
        if (a == EngineCollisionGroup.VehicleClip || b == EngineCollisionGroup.VehicleClip)
            return a == EngineCollisionGroup.Vehicle;

        return true;
    }

    /// <summary>
    /// The rules as a matrix: row <c>a</c>, bit <c>b</c>.
    ///
    /// <para>
    /// Built from <see cref="ShouldCollide"/> rather than written out, so the
    /// predicate above is the only statement of the rules.
    /// </para>
    /// </summary>
    public static uint[] BuildMatrix()
    {
        var rows = new uint[GroupCount];

        for (int a = 0; a < GroupCount; a++)
        {
            uint row = 0;

            for (int b = 0; b < GroupCount; b++)
            {
                if (ShouldCollide((EngineCollisionGroup)a, (EngineCollisionGroup)b))
                    row |= 1u << b;
            }

            rows[a] = row;
        }

        return rows;
    }

    /// <summary>
    /// Push them across. Called once per side at startup — see the class
    /// comment for why the client registers too. Answers whether the side's
    /// native received them.
    /// </summary>
    public static bool Register(IEngineCollisionRules engine)
        => engine.RegisterCollisionRules(BuildMatrix());

    /// <summary>
    /// FNV-1a over the rows, for the startup line each side logs: two sides
    /// that registered the same matrix print the same number.
    /// </summary>
    public static uint RowHash(uint[] rows)
    {
        uint hash = 2166136261u;
        foreach (uint row in rows)
        {
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash ^= (row >> shift) & 0xFFu;
                hash *= 16777619u;
            }
        }
        return hash;
    }
}
