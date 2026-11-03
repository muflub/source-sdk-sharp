//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.WeaponEffects;

using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Client.Weapons;

/// <summary>
/// The minigun's loop sounds on the client: <c>CTFMinigun::WeaponSoundUpdate</c>
/// (<c>shared/tf/tf_weapon_minigun.cpp:1381-1496</c>). One sound per weapon
/// state, its name the weapon script's category — the wind-up on the spin-up,
/// the firing loop (<c>burst</c> while the last shot crit, else
/// <c>double_shot</c>), the spin, and the empty click — a patch on the gun
/// that the envelope controller plays.
///
/// <para>
/// The idle arm (:1387-1402) keeps the wind-down (<c>special2</c>) while the
/// barrel still turns, and keeps nothing once its velocity reaches zero; the
/// velocity itself is <c>UpdateBarrelMovement</c> (:1096-1119) — an approach
/// each frame toward the state's target, the max spin while the gun runs and
/// zero once it is idle. The state rides the weapon's predicted
/// <see cref="WeaponNetState"/>: the machine's own bits
/// (<see cref="TFMinigunBehaviour.StateOf"/>) and the last shot's crit latch
/// the machine leaves beside them.
/// </para>
/// </summary>
// [SideService(Side.Client)] plain: IWeaponDrawEffect is a collection seam
// (ClientModule's ResolveAll picks the side services up by their concrete
// registration), and TFMedigunBeam holds the contract's one claim.
[SideService(Side.Client)]
public sealed class TFMinigunLoop(TFWeaponScripts scripts, IClientSoundController sounds,
                                  IEngineSound engineSound) : IWeaponDrawEffect, IWeaponBoneSpin
{
    /// <summary>CHAN_STATIC, and ATTN_NORM as a sound level (ATTN_TO_SNDLVL).</summary>
    public const int ChannelStatic = 6, SoundLevelNorm = 75;

    /// <summary>The barrel's top speed (<c>MINIGUN_MAX_SPIN_UP</c>-sided, :1102).</summary>
    public const float MaxBarrelVelocity = 20f;

    /// <summary>The per-frame step of the approach (:1110, 0.1 each frame).</summary>
    public const float BarrelAccel = 0.1f;

    /// <summary>
    /// The loop the state asks for (:1395-1431): the wind-down over the idle
    /// arm while the barrel still turns (:1387-1394), and nothing once its
    /// velocity reaches zero (:1403-1406).
    /// </summary>
    public static WeaponScriptSound? CategoryOf(TFMinigunBehaviour.State state, bool crit, float velocity = 0f) => state switch
    {
        TFMinigunBehaviour.State.StartFiring => WeaponScriptSound.Special1,
        TFMinigunBehaviour.State.Firing => crit ? WeaponScriptSound.Burst : WeaponScriptSound.Double,
        TFMinigunBehaviour.State.Spinning => WeaponScriptSound.Special3,
        TFMinigunBehaviour.State.DryFire => WeaponScriptSound.Empty,
        TFMinigunBehaviour.State.Idle => velocity > 0f ? WeaponScriptSound.Special2 : null,
        _ => null,
    };

    /// <summary>The loop one gun holds: its patch, the script sound it plays, and the barrel's spin.</summary>
    private sealed class Gun
    {
        public int Patch;
        public string Name = "";
        public float Velocity;
        public float TargetVelocity;
        public float Angle;
    }

    private readonly Dictionary<EntityHandle, Gun> _guns = [];

    public string WeaponName => "tf_weapon_minigun";

    public bool TwoPass => false;

    public void ViewModelDrawn(in WeaponDrawContext context) { }

    public int DrawModel(in WeaponDrawContext context, int flags) => -1;

    /// <summary>
    /// The frame: the loop is chosen from the velocity as it stands, the way
    /// stock's <c>WeaponSoundUpdate</c> runs at the state-change event and at
    /// the frame the velocity lands on zero (:1096-1119, :1453-1487); the old
    /// patch is stopped before the new one is made, and the new one takes its
    /// full volume with a tenth-second ramp (:1469-1487 — the pitch change
    /// behind it never fires for the base gun, whose pitch stays 1). The
    /// barrel then steps toward its state's target.
    /// </summary>
    public void Think(in WeaponDrawContext context)
    {
        EntityHandle weapon = context.Weapon.Handle;
        if (!_guns.TryGetValue(weapon, out Gun? gun))
            _guns[weapon] = gun = new Gun();

        TFMinigunBehaviour.State state = TFMinigunBehaviour.StateOf(context.State);
        WeaponScriptSound? category = CategoryOf(state, TFMinigunBehaviour.CritLatched(context.State), gun.Velocity);
        string name = category is { } kind ? scripts.Get(WeaponName)?.Sound(kind) ?? "" : "";
        bool changed = gun.Name != name;

        if (changed)
        {
            if (gun.Patch != 0)
            {
                sounds.Destroy(gun.Patch);
                engineSound.SoundPatchFree(gun.Patch);
                gun.Patch = 0;
            }

            gun.Name = name;

            if (name.Length > 0)
            {
                gun.Patch = engineSound.SoundPatchCreate(weapon, ChannelStatic, name, SoundLevelNorm);
                sounds.Create(gun.Patch);
                sounds.Play(gun.Patch, 1f, 100f, 0f);
                sounds.ChangeVolume(gun.Patch, 1f, 0.1f);
            }
        }

        gun.TargetVelocity = state == TFMinigunBehaviour.State.Idle ? 0f : MaxBarrelVelocity;
        gun.Velocity = Approach(gun.Velocity, gun.TargetVelocity, BarrelAccel);

        // Stock's UpdateBarrelMovement tail (:1118): the rotation rides the
        // stepped velocity times the frame time, so it holds the moment the
        // velocity lands on zero. The render pass reads it through the bone
        // spin below; the frame time is the engine's frametime the host hands
        // every Think.
        gun.Angle += gun.Velocity * context.FrameTime;
    }

    /// <summary>The barrel's current spin, as the loop's own arms read it.</summary>
    public float BarrelVelocity(EntityHandle weapon) => _guns.TryGetValue(weapon, out Gun? gun) ? gun.Velocity : 0f;

    /// <summary>The barrel's held rotation in radians, stock's <c>m_flBarrelAngle</c> (:1118).</summary>
    public float BarrelAngle(EntityHandle weapon) => _guns.TryGetValue(weapon, out Gun? gun) ? gun.Angle : 0f;

    /// <summary>
    /// The render read (stock's <c>StandardBlendingRules</c> arm, :1072-1090):
    /// once the frame's Think ran, the drawn weapon's model gets its <c>barrel</c>
    /// bone turned to the held angle; before it ran there is nothing to hold.
    /// </summary>
    public (string BoneName, float AngleRadians)? TryGetBoneSpin(EntityHandle weapon) =>
        _guns.TryGetValue(weapon, out Gun? gun) ? ("barrel", gun.Angle) : null;

    /// <summary>The step toward the target, stopping exactly on it (:1110).</summary>
    private static float Approach(float value, float target, float step) =>
        value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);

    /// <summary>UpdateOnRemove (:1150-1156): the gun going takes its loop with it.</summary>
    public void Forget(EntityHandle weapon)
    {
        if (!_guns.Remove(weapon, out Gun? gun))
            return;

        if (gun.Patch != 0)
        {
            sounds.Destroy(gun.Patch);
            engineSound.SoundPatchFree(gun.Patch);
            gun.Patch = 0;
        }
    }
}
