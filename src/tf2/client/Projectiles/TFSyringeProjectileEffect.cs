//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.TempEnts;

using SourceSharp.Shared;

namespace TF2.Client.Projectiles;

/// <summary>
/// The <c>ClientProjectile_Syringe</c> named effect
/// (<c>ClientsideProjectileSyringeCallback</c>,
/// <c>tf_projectile_nail.cpp:121-143</c>): the needle the player watches is
/// the CLIENT tempent, not the server entity. The server dispatches it at
/// birth with the spawn point, the launch velocity, the lifetime and the
/// owner (<c>tf_projectile_base.cpp:217-235</c>), hides its own entity, and
/// this callback hands the pair to stock's <c>ClientProjectile</c> helper —
/// the one whose ATTACHTOTARGET stop sticks the needle in the wall for its
/// remaining lifetime (<see cref="TempEntSpawns.ClientProjectile"/> and the
/// collision arm of the tempent frame, already stock's).
///
/// <para>
/// <b>THE GRAVITY IS THE SYRINGE'S OWN</b>, unlike the DT-path handler: stock's
/// DT callers pass <c>vec3_origin</c> as the acceleration, while the TF
/// callback passes <c>SYRINGE_GRAVITY * 800</c> downward
/// (<c>tf_projectile_base.cpp:366-370</c>), so the needle arcs and comes to
/// rest on the surface it hits.
/// </para>
///
/// <para>
/// The team skin, the trail particle names and <c>EF_NOSHADOW</c>
/// (<c>tf_projectile_nail.cpp:124-138</c>) are not here: this build's tempents
/// carry no particles and no skins (docs/divergences.md). Stock's muzzle
/// re-aim for other players' shots (:317-328) reads the shooter's viewmodel,
/// a per-view choice the seam answers from the eye already.
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFSyringeProjectileEffect(TempEntSpawns spawns) : IClientEffectHandler
{
    /// <summary>Stock's <c>SYRINGE_GRAVITY</c> (<c>tf_projectile_nail.cpp:46</c>).</summary>
    private const float SyringeGravity = 0.3f;

    public string EffectName => "ClientProjectile_Syringe";

    public void Dispatch(in EffectData data)
    {
        // ClientsideProjectileCallback's gravity arm (:366-370): the helper's
        // acceleration is the syringe gravity times 800, downward; lifeTime,
        // the model index and the owner ride Flags, Material and EntIndex from
        // the server's dispatch (:217-230).
        spawns.ClientProjectile(
            origin: data.Origin,
            velocity: data.Start,
            acceleration: new Vector(0f, 0f, -SyringeGravity * 800f),
            modelIndex: data.Material,
            lifetime: data.Flags,
            ownerEntityIndex: data.EntIndex);
    }
}
