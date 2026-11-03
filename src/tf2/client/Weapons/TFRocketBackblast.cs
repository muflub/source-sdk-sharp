//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.WeaponEffects;

using SourceSharp.Shared;

namespace TF2.Client.Weapons;

/// <summary>
/// The rocket launcher's backblast: <c>CTFRocketLauncher::CreateMuzzleFlashEffects</c>'
/// tail (<c>tf_weapon_rocketlauncher.cpp:378-390</c>) — after the muzzle flash,
/// everyone but the shooter sees the <c>rocketbackblast</c> particle on the
/// weapon's <c>backblast</c> attachment, <c>PATTACH_POINT_FOLLOW</c>. The
/// shooter's own gun blasts nothing (<c>pOwner-&gt;IsLocalPlayer()</c>, under
/// stock's "Don't do backblast effects in first person": their flash is the
/// viewmodel's, which the predicted replay's <c>ClientWeaponArms.MuzzleFlash</c>
/// already carries).
///
/// <para>
/// STOCK'S ASK RIDES THE MUZZLE-FLASH PARITY, bumped once per shot; this
/// port's <see cref="WeaponNetState"/> carries no parity to derive it from, so
/// the shot is the launch's own state edge — the frame the next shot is pushed
/// later AND a round is gone (<see cref="SourceSharp.Shared.TFProjectileBehaviour"/>'s
/// launch, <c>TFShellBehaviours.cs:119,135</c>). The singly reload pushes the
/// same clock once a shell with the clip standing or growing
/// (<c>TFWeaponBehaviour.cs:514,706</c>), so the round is what makes it a shot;
/// the frame a gun is first seen records and blasts nothing.
/// </para>
///
/// <para>
/// The particle starts by name against the loaded .pcfs, as every draw
/// effect's does — no precache row, whose table serves the index path the
/// temp-entity dispatches ride (the client's own <c>PrecacheParticleSystem</c>
/// is the no-op stub). One system per bearer: the next shot's start replaces
/// the last, and a spent blast's particles finish in the air, stock's
/// <c>ParticleProp()->Create</c> fire-and-forget.
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFRocketBackblast(IEngineEffects effects, IEngineEntityRender render, IEngineHud screen)
    : IWeaponDrawEffect
{
    /// <summary>The blast (<c>tf_weapon_rocketlauncher.cpp:388</c>).</summary>
    public const string Particle = "rocketbackblast";

    /// <summary>The attachment it hangs off, on the world model (:388).</summary>
    public const string Attachment = "backblast";

    /// <summary>One gun's last seen launch clock and rounds, the edge's before.</summary>
    private sealed class Gun
    {
        public bool Seen;
        public float NextAttack;
        public int Clip;
    }

    private readonly Dictionary<EntityHandle, Gun> _guns = [];

    public string WeaponName => "tf_weapon_rocketlauncher";

    public bool TwoPass => false;

    /// <summary>The blast is a particle; nothing draws in the viewmodel pass.</summary>
    public void ViewModelDrawn(in WeaponDrawContext context) { }

    /// <summary>The blast is a particle; the model draws.</summary>
    public int DrawModel(in WeaponDrawContext context, int flags) => -1;

    public void Think(in WeaponDrawContext context)
    {
        EntityHandle weapon = context.Weapon.Handle;
        if (!_guns.TryGetValue(weapon, out Gun? gun))
            _guns[weapon] = gun = new Gun();

        float next = context.State.NextPrimaryAttack;
        int clip = context.State.Clip;
        bool fired = gun.Seen && next != gun.NextAttack && clip < gun.Clip;
        gun.Seen = true;
        gun.NextAttack = next;
        gun.Clip = clip;

        // Don't do backblast effects in first person: the shooter's own gun.
        if (!fired || context.Owner == screen.LocalPlayerIndex)
            return;

        int backblast = render.LookupAttachment(weapon, Attachment);
        if (backblast <= 0 || !effects.ParticleSystemStart(weapon, Particle))
            return;

        effects.ParticleSystemFollowEntity(weapon, 0, weapon, backblast);
    }

    /// <summary>The gun is another kind now, or gone: the next shot re-arms from nothing.</summary>
    public void Forget(EntityHandle weapon) => _guns.Remove(weapon);
}
