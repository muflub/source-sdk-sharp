//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;

using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Weapons;

/// <summary>
/// The flamethrower's fire, heard and seen: <c>CTFFlameThrower</c>'s client
/// half (shared/tf/tf_weapon_flamethrower.cpp) — <c>OnDataChanged</c>'s gate
/// (:2183-2285) run every frame over the carrier the draw effects hand it, the
/// pilot light's loop while the gun idles (<c>StartPilotLight</c> :2489, the
/// sound stock leaves with the model's own flame), the flame particle on the
/// muzzle and the start/loop crossfade while it burns (<c>StartFlame</c>
/// :2332-2426), the poof when it stops (<c>StopFlame</c> :2445), and the
/// airblast's puff and boom in the SECONDARY state (:2334).
///
/// </summary>
// The plain mark: the draw effects are collected by ResolveAll (the medigun
// holds the IWeaponDrawEffect claim; a second claimant is a startup error).
[SideService(Side.Client)]
public sealed class TFFlameThrowerFx(IEngineEffects effects, IEngineEntityRender render,
                                    ITFPlayerData data,
                                    IClientStandalones standalones, TFClientPlayerResources resources,
                                    IClientSoundController sounds, IEngineSound engineSound,
                                    IWeaponViewModelBearer bearers, TFWeaponScripts scripts)
    : IWeaponDrawEffect
{
    public const int ChannelStatic = 6, SoundLevelNorm = 75;

    /// <summary>The start/loop crossfade (StartFlame :2347).</summary>
    public const float Crossfade = 3.5f;

    /// <summary>The flame particles, by team and crit (GetNewFlameEffectInternal :283-310).</summary>
    public const string FlameParticle = "new_flame",
                        FlameCritRed = "new_flame_crit_red",
                        FlameCritBlue = "new_flame_crit_blue";

    /// <summary>The airblast's puff (StartFlame :2336).</summary>
    public const string BlastParticle = "pyro_blast";

    /// <summary>The muzzle attachment (FlameEffect_t::StartEffects :2712).</summary>
    public const string MuzzleAttachment = "muzzle";

    /// <summary>The point ring's size (stock's <c>MAX_POINT_MANAGER_POINTS</c>) and the floats per record: the point index, position, velocity, density, radius, duration.</summary>
    public const int RingSize = 30, Stride = 10;

    /// <summary>The birth interval and the point life (<c>tf_flamethrower_new_flame_fire_delay</c> 0.02, <c>tf_flamethrower_flametime</c> 0.5).</summary>
    public const float FireDelay = 0.02f, LifeTime = TFFlameDefs.LifeTime;

    /// <summary>The density floor, the box radius and the muzzle's start radius — stock's flame defaults (the item schema's attribute hooks stay constants).</summary>
    public const float MinDensity = 0.01f, BoxSize = 12f, StartRadius = 3f;

    /// <summary>The carrier's carry-along and the drag (<c>tf_flamethrower_float</c>, <c>tf_flamethrower_drag</c>; gravity keeps the member default of zero).</summary>
    public const float FloatUp = TFFlameDefs.Up, Drag = TFFlameDefs.Drag;

    /// <summary>The birth speed, <c>flame_speed</c>.</summary>
    public const float StartSpeed = TFFlameDefs.Speed;

    /// <summary>Which of the gun's sounds an arm asks: the script categories of <c>StartFlame</c>/<c>StartPilotLight</c>/<c>StopFlame</c>.</summary>
    public enum Arm
    {
        Pilot,
        Start,
        Loop,
        Stop,
        Blast,
    }

    /// <summary>The arm's category: the pilot's loop (:2496), the start (:2370), the crit's burst over the plain loop (:2380-2388), the poof (:2451), the blast (:2338).</summary>
    internal static WeaponScriptSound? CategoryOf(Arm arm, bool crit = false) => arm switch
    {
        Arm.Pilot => WeaponScriptSound.Special2,
        Arm.Start => WeaponScriptSound.Single,
        Arm.Loop => crit ? WeaponScriptSound.Burst : WeaponScriptSound.Special1,
        Arm.Stop => WeaponScriptSound.Special3,
        Arm.Blast => WeaponScriptSound.Double,
        _ => null,
    };

    /// <summary>One gun's running effects, keyed by the weapon.</summary>
    public sealed class Gun
    {
        public string? Flame;
        public int StartPatch;
        public int LoopPatch;
        public bool LoopCrit;
        public int PilotPatch;

        /// <summary>The flame's point ring: the birth clock, the last feed, the next slot, the live points, the one-crossing scratch.</summary>
        public float NextSpawn = float.NaN, LastFeed = float.NaN;
        public int NextSlot;
        public FlamePoint[]? Points;
        public readonly float[] Scratch = new float[(RingSize + 1) * Stride];
        public EntityHandle AttachOwner;
        public int AttachIndex;
    }

    /// <summary>A live flame point: its place, its carried velocity, the time it was born and the control point it feeds.</summary>
    public struct FlamePoint
    {
        public bool Alive;
        public float X, Y, Z;
        public float VX, VY, VZ;
        public float SpawnTime;
        public int Cp;
    }

    private readonly Dictionary<EntityHandle, Gun> _guns = [];

    public string WeaponName => "tf_weapon_flamethrower";

    public bool TwoPass => false;

    /// <summary>The flame is a particle system; nothing draws in the viewmodel pass.</summary>
    public void ViewModelDrawn(in WeaponDrawContext context) { }

    /// <summary>The flame is a particle system; the model draws.</summary>
    public int DrawModel(in WeaponDrawContext context, int flags) => -1;

    /// <summary>A gun's state, for the facts.</summary>
    internal Gun? GunOf(EntityHandle weapon) => _guns.GetValueOrDefault(weapon);

    /// <summary>
    /// The frame: OnDataChanged's gate, every frame. The carrier gone or dead,
    /// the pool empty — everything stops (:2212-2218); the idle gun burns its
    /// pilot light (:2207-2210); a burning state plays the flame (:2200-2205),
    /// the SECONDARY arm its blast (:2202, every carrier — the local gun's
    /// blast rides the same state edge rather than a network update).
    /// </summary>
    public void Think(in WeaponDrawContext context)
    {
        EntityHandle weapon = context.Weapon.Handle;
        Gun gun = GunFor(weapon);
        int owner = context.Owner;

        bool gate = data.Networked(owner) is not null && Alive(owner) && context.State.Reserve > 0;
        var state = TFFlamethrowerBehaviour.StateOf(context.State);
        bool crit = TFFlamethrowerBehaviour.CritLatched(context.State);

        if (!gate)
        {
            StopFlame(gun, weapon);
            StopPilot(gun, weapon);
            return;
        }

        if (state == TFFlamethrowerBehaviour.FlameState.Idle)
            StartPilot(gun, weapon);
        else
            StartFlame(gun, owner, weapon, state, crit, BearerOf(in context), context.Now);
    }

    /// <summary>
    /// The entity this frame's flame hangs off: the viewmodel attachment's
    /// muzzle in the first person, the carried gun's own model in the world
    /// pass — stock's bearer is the weapon the viewer sees
    /// (<c>GetWeaponForEffect</c>, the two arms of
    /// <c>GetAppropriateWorldOrViewModel</c>).
    /// </summary>
    private EntityHandle BearerOf(in WeaponDrawContext context)
        => context.FirstPerson && bearers.ViewModelBearer() is { } viewmodel ? viewmodel : context.Weapon.Handle;

    /// <summary>
    /// StartFlame. The SECONDARY arm is the blast (:2334-2342), taken once per
    /// burn — the state's edge, where stock re-runs it at every network update.
    /// The burning arms: the crit flip rebuilds the loop with no crossfade
    /// (:2349-2356), the pilot goes (:2358), and a gun with no sound yet takes
    /// the particle, the start sound fading out and the loop fading in over
    /// <see cref="Crossfade"/> (:2360-2400).
    /// </summary>
    private void StartFlame(Gun gun, int owner, EntityHandle weapon, TFFlamethrowerBehaviour.FlameState state, bool crit,
                            EntityHandle bearer, float now)
    {
        if (state == TFFlamethrowerBehaviour.FlameState.Secondary)
        {
            if (gun.Flame == BlastParticle)
                return;

            StopFlame(gun, weapon);

            if (effects.ParticleSystemStart(weapon, BlastParticle))
            {
                effects.ParticleSystemFollowEntity(weapon, 0, bearer, render.LookupAttachment(bearer, MuzzleAttachment));
                gun.Flame = BlastParticle;
            }

            Emit(weapon, CategoryOf(Arm.Blast));
            return;
        }

        bool abrupt = gun.LoopPatch != 0 && gun.LoopCrit != crit;
        if (abrupt)
        {
            Kill(ref gun.StartPatch);
            Kill(ref gun.LoopPatch);
        }

        StopPilot(gun, weapon);

        if (gun.StartPatch == 0 && gun.LoopPatch == 0)
        {
            StartFlameParticle(gun, owner, weapon, crit, bearer);

            if (!abrupt)
            {
                // The start sound, fading out (:2371-2377).
                gun.StartPatch = engineSound.SoundPatchCreate(weapon, ChannelStatic, SoundName(CategoryOf(Arm.Start)), SoundLevelNorm);
                sounds.Create(gun.StartPatch);
                sounds.Play(gun.StartPatch, 1f, 100f, 0f);
                sounds.ChangeVolume(gun.StartPatch, 0f, Crossfade);
            }

            // The loop, fading in — or full, after the flip (:2380-2400).
            gun.LoopPatch = engineSound.SoundPatchCreate(weapon, ChannelStatic,
                SoundName(CategoryOf(Arm.Loop, crit)), SoundLevelNorm);
            sounds.Create(gun.LoopPatch);
            if (abrupt)
                sounds.Play(gun.LoopPatch, 1f, 100f, 0f);
            else
            {
                sounds.Play(gun.LoopPatch, 0f, 100f, 0f);
                sounds.ChangeVolume(gun.LoopPatch, 1f, Crossfade);
            }

            gun.LoopCrit = crit;
        }

        FeedPoints(gun, owner, weapon, bearer, now);
    }

    /// <summary>RestartParticleEffect's particle (:2531-2545): the name the carrier's team and crit burn, the muzzle it rides.</summary>
    private void StartFlameParticle(Gun gun, int owner, EntityHandle weapon, bool crit, EntityHandle bearer)
    {
        string name = ParticleName(Team(owner), crit);

        // The particle restarts when the name changes — the crit flip
        // (PrimaryAttack :850-855) and the water crossings (:790-798) ride the
        // same compare; the underwater name waits for the client's water read.
        if (gun.Flame == name)
            return;

        if (gun.Flame is not null)
            effects.ParticleSystemStop(weapon);

        if (!effects.ParticleSystemStart(weapon, name))
        {
            gun.Flame = null;
            return;
        }

        effects.ParticleSystemFollowEntity(weapon, 0, bearer, render.LookupAttachment(bearer, MuzzleAttachment));
        gun.Flame = name;
    }

    /// <summary>
    /// The burning frame's flame points, in one crossing: the muzzle as point 0 and every
    /// live point after it, each carried a step by the clock since the last feed — stock's
    /// point manager re-feeds every update (<c>CTFPointManager::Update</c>). Births come
    /// every <see cref="FireDelay"/> from the carrier's eye direction; each point drifts by
    /// the carrier's velocity along its own heading plus the float, bleeds its speed to the
    /// drag, and thins its density with its age. A stall skips ahead rather than filling
    /// the ring one catch-up frame at a time; the newest point's index rides as the active
    /// control point.
    /// </summary>
    private void FeedPoints(Gun gun, int owner, EntityHandle weapon, EntityHandle bearer, float now)
    {
        FlamePoint[] points = gun.Points ??= new FlamePoint[RingSize];

        // The client's engine reads answer no velocity or eye angles for an entity: the player carries them.
        if (standalones.GetByIndex(owner) is not ClientSsPlayer carrier)
            return;

        Vector attVel = carrier.State.Velocity;

        if (!render.GetAttachment(bearer, AttachmentOf(gun, bearer), out Vector muzzle, out Vector _))
            return;

        if (float.IsNaN(gun.NextSpawn))
            gun.NextSpawn = now;

        if (gun.NextSpawn <= now)
        {
            Vector facing = MoveVec.AngleForward(carrier.EyeAngles());

            for (int guard = RingSize + 4; gun.NextSpawn <= now && guard > 0; --guard)
            {
                int slot = gun.NextSlot;
                points[slot] = new FlamePoint
                {
                    Alive = true,
                    X = muzzle.X, Y = muzzle.Y, Z = muzzle.Z,
                    VX = facing.X * StartSpeed,
                    VY = facing.Y * StartSpeed,
                    VZ = facing.Z * StartSpeed,
                    SpawnTime = gun.NextSpawn,
                    Cp = slot + 1,
                };

                gun.NextSlot = (gun.NextSlot + 1) % RingSize;
                gun.NextSpawn += FireDelay;

                // A long stall: jump the clock to the frame, not thirty catch-up births.
                if (gun.NextSpawn < now - RingSize * FireDelay)
                    gun.NextSpawn = now;
            }
        }

        float dt = float.IsNaN(gun.LastFeed) ? 0f : MathF.Max(0f, now - gun.LastFeed);
        gun.LastFeed = now;

        float[] buffer = gun.Scratch;

        // The last birth's index names the active point when none of them is alive.
        int activeIndex = (gun.NextSlot + RingSize - 1) % RingSize + 1;
        float newestX = attVel.X, newestY = attVel.Y, newestZ = attVel.Z;

        // Oldest to newest: the slot the next birth takes is the oldest still held.
        int written = 1;

        for (int step = 0; step < RingSize; step++)
        {
            ref FlamePoint p = ref points[(gun.NextSlot + step) % RingSize];

            if (!p.Alive)
                continue;

            float age = now - p.SpawnTime;

            if (age > LifeTime)
            {
                p.Alive = false;
                continue;
            }

            float speed = MathF.Sqrt(p.VX * p.VX + p.VY * p.VY + p.VZ * p.VZ);
            float dx = speed > 0f ? p.VX / speed : 0f;
            float dy = speed > 0f ? p.VY / speed : 0f;
            float dz = speed > 0f ? p.VZ / speed : 0f;

            // The carry-along: the carrier's speed along the point's heading, plus the float.
            float along = attVel.X * dx + attVel.Y * dy + attVel.Z * dz;

            p.X += dt * (p.VX + dx * along);
            p.Y += dt * (p.VY + dy * along);
            p.Z += dt * (p.VZ + dz * along + FloatUp);

            // Drag bleeds the speed along the old heading; the heading itself holds.
            float dragSpeed = MathF.Min(MathF.Max(speed - dt * Drag * speed, 0f), speed);
            p.VX = dx * dragSpeed;
            p.VY = dy * dragSpeed;
            p.VZ = dz * dragSpeed;

            float byAge = MathF.Min(MathF.Max(age / LifeTime, 0f), 1f);
            float density = 1f + (MinDensity - 1f) * byAge;

            int at = written * Stride;
            buffer[at] = p.Cp;
            buffer[at + 1] = p.X;
            buffer[at + 2] = p.Y;
            buffer[at + 3] = p.Z;
            buffer[at + 4] = attVel.X + p.VX;
            buffer[at + 5] = attVel.Y + p.VY;
            buffer[at + 6] = attVel.Z + p.VZ;
            buffer[at + 7] = density;
            buffer[at + 8] = BoxSize;
            buffer[at + 9] = LifeTime;

            written++;
            activeIndex = p.Cp;
            newestX = attVel.X + p.VX;
            newestY = attVel.Y + p.VY;
            newestZ = attVel.Z + p.VZ;
        }

        // Point 0 first, as stock feeds it: the muzzle's place; its velocity the carrier's
        // plus the newest point's; the pilot's own density and start radius.
        buffer[0] = 0f;
        buffer[1] = muzzle.X;
        buffer[2] = muzzle.Y;
        buffer[3] = muzzle.Z;
        buffer[4] = newestX;
        buffer[5] = newestY;
        buffer[6] = newestZ;
        buffer[7] = 1f;
        buffer[8] = StartRadius;
        buffer[9] = LifeTime;

        effects.ParticleSystemSetPoints(weapon, 0, activeIndex, buffer.AsSpan(0, written * Stride));
    }

    /// <summary>The gun's muzzle attachment, looked up once per bearer: the flame's follow and its feed ride the same index.</summary>
    private int AttachmentOf(Gun gun, EntityHandle bearer)
    {
        if (gun.AttachOwner != bearer || gun.AttachIndex == 0)
        {
            gun.AttachOwner = bearer;
            gun.AttachIndex = render.LookupAttachment(bearer, MuzzleAttachment);
        }

        return gun.AttachIndex;
    }

    /// <summary>StopFlame: the poof (:2447-2453), the patches, the particle (:2467).</summary>
    private void StopFlame(Gun gun, EntityHandle weapon)
    {
        if (gun.StartPatch != 0 || gun.LoopPatch != 0)
            engineSound.EntityEmitSound(weapon, SoundName(CategoryOf(Arm.Stop)));

        Kill(ref gun.StartPatch);
        Kill(ref gun.LoopPatch);

        if (gun.Flame is not null)
        {
            effects.ParticleSystemStop(weapon);
            gun.Flame = null;
        }

        // The ring empties with the burn: the next one starts its points at the front.
        gun.Points = null;
        gun.NextSpawn = float.NaN;
        gun.LastFeed = float.NaN;
        gun.NextSlot = 0;
    }

    /// <summary>StartPilotLight (:2489-2504): the flame stops first (:2493), then the loop.</summary>
    private void StartPilot(Gun gun, EntityHandle weapon)
    {
        if (gun.PilotPatch != 0)
            return;

        StopFlame(gun, weapon);

        gun.PilotPatch = engineSound.SoundPatchCreate(weapon, ChannelStatic, SoundName(CategoryOf(Arm.Pilot)), SoundLevelNorm);
        sounds.Create(gun.PilotPatch);
        sounds.Play(gun.PilotPatch, 1f, 100f, 0f);
    }

    /// <summary>StopPilotLight (:2509-2516).</summary>
    private void StopPilot(Gun gun, EntityHandle weapon)
    {
        if (gun.PilotPatch == 0)
            return;

        sounds.Destroy(gun.PilotPatch);
        engineSound.SoundPatchFree(gun.PilotPatch);
        gun.PilotPatch = 0;
    }

    /// <summary>DestroyEffects and the dead arm: everything the gun held goes.</summary>
    public void Forget(EntityHandle weapon)
    {
        if (!_guns.Remove(weapon, out Gun? gun))
            return;

        StopFlame(gun, weapon);
        StopPilot(gun, weapon);
    }

    private Gun GunFor(EntityHandle weapon)
    {
        if (!_guns.TryGetValue(weapon, out Gun? gun))
            _guns[weapon] = gun = new Gun();
        return gun;
    }

    private void Kill(ref int patch)
    {
        if (patch == 0)
            return;

        sounds.Destroy(patch);
        engineSound.SoundPatchFree(patch);
        patch = 0;
    }

    private void Emit(EntityHandle weapon, WeaponScriptSound? category) =>
        engineSound.EntityEmitSound(weapon, SoundName(category));

    /// <summary>The script sound a category names; null is stock's silence.</summary>
    private string SoundName(WeaponScriptSound? category) => category is { } kind ? scripts.Get(WeaponName)?.Sound(kind) ?? "" : "";

    /// <summary>UpdateEffects' owner gate: the carrier is there and alive.</summary>
    private bool Alive(int playerIndex)
        => standalones.GetByIndex(playerIndex) is ClientSsPlayer player && player.State.Alive;

    /// <summary>The carrier's team from the scoreboard.</summary>
    private int Team(int playerIndex)
        => playerIndex > 0 && playerIndex < TFPlayerResourceNetState.Slots
            ? resources.Live?.State.Team[playerIndex] ?? 0
            : 0;

    /// <summary>GetNewFlameEffectInternal's stock names, no attribute overrides yet (:283-310).</summary>
    internal static string ParticleName(int team, bool crit)
        => team == (int)TFTeam.Red
            ? crit ? FlameCritRed : FlameParticle
            : crit ? FlameCritBlue : FlameParticle;
}
