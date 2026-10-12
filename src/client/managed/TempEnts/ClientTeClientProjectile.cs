//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>CTEClientProjectile</c>, played. One model flying along a velocity,
/// aligned to its own motion, colliding with everything except whoever fired
/// it.
///
/// <para>
/// The whole of <c>c_te_clientprojectile.cpp</c>'s
/// <c>C_TEClientProjectile::PostDataUpdate</c>:
/// </para>
/// <code>
/// tempents-&gt;ClientProjectile( m_vecOrigin, m_vecVelocity, vec3_origin,
///                             m_nModelIndex, m_nLifeTime, m_hOwner );
/// </code>
///
/// <para>
/// <b>THE ACCELERATION IS ALWAYS ZERO</b>, and it is a parameter anyway. Stock's
/// helper takes one and both of its callers pass <c>vec3_origin</c>; carrying
/// it means the helper is stock's function rather than a narrowed copy of it,
/// and the field crosses to a tempent whose <c>Frame</c> integrates it. A
/// projectile with acceleration is one line away rather than one slot away.
/// </para>
///
/// <para>
/// <b>THE OWNER ARRIVES AS AN EHANDLE AND IS USED AS AN INDEX.</b> The wire
/// carries <c>m_hOwner</c> — an entry index and a serial — and everything
/// downstream of here wants the index: <c>clientIndex</c> is what the collision
/// arm compares against, and <see cref="TempEntSpawn.OwnerEntityIndex"/> is
/// turned back into an entity by <c>cl_entitylist-&gt;GetEnt</c> on the native
/// side. The SERIAL IS DROPPED, deliberately: resolving a client handle through
/// <c>CBaseHandle::Get()</c> is behaviour ledger row C8, which segfaults on this
/// build's managed entities, and the index is the half of the handle that never
/// goes near that path.
/// </para>
///
/// <para>
/// <b>THIS IS THE ONE CLASS WHOSE EFFECT STILL DEPENDS ON THE NATIVE COLLISION
/// ARM.</b> <c>FTENT_COLLIDEALL</c>, <c>FTENT_ATTACHTOTARGET</c> and the owner
/// are all read inside <c>C_LocalTempEntity::Frame</c>'s collision block, which
/// is still native and is what holds ledger rows C4 and C5 open. All three
/// cross correctly and that block reads them exactly as it always has — the
/// helper moving to C# did not move the block and did not need to. See
/// <c>docs/te-legacy-pool-survey.md</c> section 6.
/// </para>
///
/// <para>
/// NO LEDGER ROW OF ITS OWN.
/// </para>
/// </summary>
[ClientTempEnt("CTEClientProjectile")]
public sealed class ClientTeClientProjectile : ClientTempEnt<TeClientProjectile>
{
    private readonly TempEntSpawns _spawns;

    public ClientTeClientProjectile(TempEntSpawns spawns)
    {
        _spawns = spawns;
    }

    public override void Play(TeClientProjectile te)
    {
        // SIX UNSIGNED BITS on the wire, so 0..63 seconds. Stock's helper takes
        // an int and adds it to curtime.

        _spawns.ClientProjectile(
            origin: te.Origin,
            velocity: te.Velocity,

            // vec3_origin, as both of stock's callers pass.
            acceleration: Vector.Zero,

            modelIndex: te.ModelIndex,
            lifetime: te.LifeTime,
            ownerEntityIndex: te.OwnerEntityIndex);
    }
}
