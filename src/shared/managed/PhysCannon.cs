//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// Stock's <c>physcannon_*</c> convars (weapon_physcannon.cpp, HL2MP's shared
/// copy), at their defaults. They are <c>FCVAR_REPLICATED | FCVAR_CHEAT</c>
/// there, so a server that did not cheat could never move them; held here as
/// the numbers both sides read.
/// </summary>
public static class PhysCannonTuning
{
    /// <summary><c>physcannon_minforce</c>: the launch speed of the heaviest object.</summary>
    public const float MinForce = 700f;

    /// <summary><c>physcannon_maxforce</c>: the launch speed of anything up to 100 kg.</summary>
    public const float MaxForce = 1500f;

    /// <summary><c>physcannon_maxmass</c>: the heaviest object the gun will pick up.</summary>
    public const float MaxMass = 250f;

    /// <summary><c>physcannon_tracelength</c>: the punt's reach, and a quarter of the pull's.</summary>
    public const float TraceLength = 250f;

    /// <summary><c>physcannon_pullforce</c>: the nudge a distant object gets towards the gun.</summary>
    public const float PullForce = 4000f;

    /// <summary><c>physcannon_cone</c>: the cosine of the cone a pull also searches.</summary>
    public const float Cone = 0.97f;

    /// <summary>The error the held object may build up before it is let go (UpdateObject's 12).</summary>
    public const float HoldError = 12f;

    /// <summary>
    /// The launch speed of a held or punted object of <paramref name="mass"/> kg:
    /// stock's ApplyVelocityBasedForce, which holds the maximum up to 100 kg
    /// and splines down to the minimum at 600.
    /// </summary>
    public static float LaunchSpeed(float mass)
    {
        float force = MaxForce;

        if (mass > 100f)
        {
            mass = MathF.Min(mass, 1000f);
            force = SimpleSplineRemapVal(mass, 100f, 600f, MaxForce, MinForce);
        }

        return force;
    }

    /// <summary>mathlib's SimpleSplineRemapVal (unclamped, as stock calls it here).</summary>
    public static float SimpleSplineRemapVal(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;

        float cVal = (val - a) / (b - a);
        return c + (d - c) * SimpleSpline(cVal);
    }

    /// <summary>mathlib's SimpleSpline: 3x² − 2x³.</summary>
    public static float SimpleSpline(float value)
    {
        float valueSquared = value * value;
        return 3 * valueSquared - 2 * valueSquared * value;
    }
}

/// <summary>
/// The soundscript entries weapon_physcannon.txt's SoundData block names, by
/// the slot stock's <c>WeaponSound</c> reads them from.
/// </summary>
public static class PhysCannonSounds
{
    /// <summary>single_shot: launch held object.</summary>
    public const string Launch = "Weapon_PhysCannon.Launch";

    /// <summary>empty: dry fire, no target.</summary>
    public const string DryFire = "Weapon_PhysCannon.DryFire";

    /// <summary>special1: pick up object.</summary>
    public const string Pickup = "Weapon_PhysCannon.Pickup";

    /// <summary>special2: open claws, target object.</summary>
    public const string OpenClaws = "Weapon_PhysCannon.OpenClaws";

    /// <summary>melee_hit: close claws, target out of range or invalid.</summary>
    public const string CloseClaws = "Weapon_PhysCannon.CloseClaws";

    /// <summary>melee_miss: drop object.</summary>
    public const string Drop = "Weapon_PhysCannon.Drop";

    /// <summary>special3: too heavy.</summary>
    public const string TooHeavy = "Weapon_PhysCannon.TooHeavy";

    /// <summary>reload ("Ignore?" in the script).</summary>
    public const string Charge = "Weapon_PhysCannon.Charge";

    /// <summary>The motor's loop while holding, which stock precaches by name.</summary>
    public const string HoldSound = "Weapon_PhysCannon.HoldSound";

    /// <summary>Every entry, for the precache.</summary>
    public static readonly ImmutableArray<string> All =
        [Launch, DryFire, Pickup, OpenClaws, CloseClaws, Drop, TooHeavy, Charge, HoldSound];
}

/// <summary>Stock's EFFECT_* — what the gun's glow is doing, networked and predicted.</summary>
public enum PhysCannonEffect
{
    None = 0,
    Closed = 1,
    Ready = 2,
    Holding = 3,
    Launch = 4,
}

/// <summary>Stock's ELEMENT_STATE_*: a delayed change of the claws.</summary>
public enum PhysCannonElements
{
    None = -1,
    Open = 0,
    Closed = 1,
}

/// <summary>What a pull found (stock's FindObjectResult_t, with its deny split out).</summary>
public enum PhysCannonFind
{
    /// <summary>Picked up: the world's grab controller holds it now.</summary>
    Found,

    /// <summary>Nothing taken: out of reach (perhaps pulled closer) or not physics.</summary>
    NotFound,

    /// <summary>CanPickupObject refused it (too heavy, nothing there): NotFound, with the deny sound.</summary>
    Denied,

    /// <summary>Constrained and being ripped off: try again almost at once.</summary>
    BeingDetached,
}

/// <summary>What the punt's trace found ahead (PrimaryAttack when nothing is held).</summary>
public enum PhysCannonPunt
{
    DryFire,
    NonVPhysics,
    VPhysics,
}

/// <summary>
/// What one side's physcannon does to the world, for <see cref="PhysCannonBehaviour"/>.
///
/// <para>
/// The behaviour is weapon_physcannon.cpp's shared state machine; this is
/// everything in it that touches something other than the two declared
/// states. <see cref="Authoritative"/> is stock's <c>#ifndef CLIENT_DLL</c>:
/// the server answers every question, and the client's replay answers the
/// few its own copy of the world can (none yet: it holds no grab controller).
/// The carrier is named by entindex throughout, as the weapon's state names
/// its owner; an object is an entindex too, 0 for none.
/// </para>
/// </summary>
public interface IPhysCannonWorld
{
    /// <summary>The server (true), or the client's replay (false).</summary>
    bool Authoritative { get; }

    /// <summary>The object the carrier's grab controller holds, or 0.</summary>
    int Attached(int owner);

    /// <summary>
    /// CGrabController::UpdateObject: points the held object at the hold
    /// position. False when it must be let go (too much error, the carrier
    /// standing on it, no physics, motion disabled).
    /// </summary>
    bool UpdateHeld(int owner);

    /// <summary>
    /// The grab controller's DetachEntity and the drop callbacks
    /// (Pickup_OnPhysGunDrop, the owner entity put back). Nothing for a
    /// carrier holding nothing.
    /// </summary>
    void DetachHeld(int owner, bool launched);

    /// <summary>
    /// CheckForTarget's question: does the 4-unit hull ahead, within the
    /// trace length, touch something the gun could pick up or that forces it
    /// open?
    /// </summary>
    bool TargetAhead(int owner);

    /// <summary>
    /// FindObject: the trace, the cone, the mass limit, the attach (on
    /// <see cref="PhysCannonFind.Found"/>, with <paramref name="attached"/>
    /// the object now held) or the pull.
    /// </summary>
    PhysCannonFind FindObject(int owner, out int attached);

    /// <summary>How far the held object's centre is from the carrier's, or −1 when unknown.</summary>
    float HeldDistance(int owner, int held);

    /// <summary>
    /// The launch of a just-detached object: ApplyVelocityBasedForce along the
    /// carrier's view, and the launch effect at its centre.
    /// </summary>
    void Launch(int owner, int entity);

    /// <summary>The punt's trace and what it hit (<paramref name="entity"/>, 0 for none).</summary>
    PhysCannonPunt AimPunt(int owner, out int entity);

    /// <summary>
    /// PuntNonVPhysics' server half: one point of crush damage along the view,
    /// and the launch effect where the trace ended.
    /// </summary>
    void PuntNonVPhysics(int owner, int entity);

    /// <summary>
    /// PuntVPhysics' server half: the forces, the pickup callbacks, the
    /// launch effect where the trace ended. False when the object turned out
    /// to have no physics objects, which stock answers with a dry fire.
    /// </summary>
    bool PuntVPhysics(int owner, int entity);

    /// <summary>CBasePlayer::ViewPunch on the carrier.</summary>
    void ViewPunch(int owner, QAngle punch);

    /// <summary>PrimaryFireEffect's server-only white flash (UTIL_ScreenFade).</summary>
    void FireFlash(int owner);

    /// <summary>A random float in [low, high] (stock's <c>random-&gt;RandomFloat</c>).</summary>
    float RandomFloat(float low, float high);

    /// <summary>A random int in [low, high] (stock's <c>SharedRandomInt</c>).</summary>
    int RandomInt(int low, int high);
}

/// <summary>
/// weapon_physcannon: CWeaponPhysCannon's state machine, the shared half of
/// weapon_physcannon.cpp (HL2MP), run by <see cref="WeaponLogic"/> on the
/// server and on the client's replay over the two declared states.
///
/// <para>
/// It replaces the whole command, as stock's ItemPreFrame and ItemPostFrame
/// overrides replace the base weapon's: secondary pulls and holds, primary
/// punts what is held or aimed at, and neither spends ammunition. The world
/// it acts on is <see cref="IPhysCannonWorld"/>; everything it decides is
/// written to <see cref="WeaponNetState"/>'s <c>Cannon*</c> fields, which is
/// where stock's members went.
/// </para>
/// </summary>
public sealed class PhysCannonBehaviour : IWeaponBehaviour
{
    /// <summary>The key a weapon row names to run this (<see cref="WeaponType.Behaviour"/>).</summary>
    public const string Key = "physcannon";

    private readonly IPhysCannonWorld _world;

    public PhysCannonBehaviour(IPhysCannonWorld world) => _world = world;

    /// <summary>ItemPreFrame then ItemPostFrame, as the player's think runs them: the whole command.</summary>
    public bool RunCommand(in WeaponAttackContext attack, int buttons, int pressed)
    {
        var c = new Command(this, attack, buttons, pressed);
        c.ItemPreFrame();
        c.ItemPostFrame();
        return true;
    }

    /// <summary>CWeaponPhysCannon::Deploy: claws shut, ready, and both triggers live at once.</summary>
    public void Deploy(in WeaponAttackContext attack)
    {
        var c = new Command(this, attack, 0, 0);
        c.CloseElements();
        c.DoEffect(PhysCannonEffect.Ready);
        attack.Weapon.NextPrimaryAttack = attack.Now;
        attack.Weapon.NextSecondaryAttack = attack.Now;
    }

    /// <summary>CanHolster: never while something is held.</summary>
    public bool CanHolster(in WeaponAttackContext attack) => !attack.Weapon.CannonActive;

    /// <summary>Holster's and Drop's ForceDrop: claws shut, let go, effects off.</summary>
    public void Holster(in WeaponAttackContext attack)
    {
        var c = new Command(this, attack, 0, 0);
        c.CloseElements();
        c.DetachObject(playSound: true, launched: false);
        c.DoEffect(PhysCannonEffect.None);
    }

    /// <summary>CWeaponPhysCannon::Drop removes the gun rather than leaving it on the floor.</summary>
    public bool LeavesPickup => false;

    /// <summary>One command's worth of the machine: the context and the buttons it runs over.</summary>
    private readonly struct Command
    {
        private readonly IPhysCannonWorld _world;
        private readonly WeaponAttackContext _a;
        private readonly int _buttons;
        private readonly int _pressed;

        public Command(PhysCannonBehaviour owner, in WeaponAttackContext attack, int buttons, int pressed)
        {
            _world = owner._world;
            _a = attack;
            _buttons = buttons;
            _pressed = pressed;
        }

        private WeaponNetState W => _a.Weapon;
        private float Now => _a.Now;
        private int Owner => W.OwnerIndex;
        private bool Server => _world.Authoritative;

        private bool Held(WeaponButtons b) => (_buttons & (int)b) != 0;

        /// <summary>
        /// m_grabController.GetAttached(): the server's grab controller, and on
        /// the client the object the server says is held — what stock's
        /// ManagePredictedObject attaches the client's controller to.
        /// </summary>
        private int HeldObject() => Server ? _world.Attached(Owner) : W.CannonHeld;

        public void ItemPreFrame()
        {
            // Update the object if the weapon is switched on.
            if (W.CannonActive)
                UpdateObject();
        }

        public void ItemPostFrame()
        {
            // Check for object in pickup range.
            if (!W.CannonActive)
            {
                CheckForTarget();

                if (W.CannonElementDebounce < Now && W.CannonChangeState != (int)PhysCannonElements.None)
                {
                    if (W.CannonChangeState == (int)PhysCannonElements.Open)
                        OpenElements();
                    else if (W.CannonChangeState == (int)PhysCannonElements.Closed)
                        CloseElements();

                    W.CannonChangeState = (int)PhysCannonElements.None;
                }
            }

            // NOTE: Attack2 is considered pressed until the first item is picked up.
            int attack2Mask = _buttons & ~W.CannonAttack2Debounce;

            if ((attack2Mask & (int)WeaponButtons.Attack2) != 0)
            {
                SecondaryAttack();
            }
            else
            {
                // Reset our debouncer.
                W.CannonDenyPlayed = false;

                if (!W.CannonActive)
                    DoEffect(PhysCannonEffect.Ready);
            }

            if (!Held(WeaponButtons.Attack2))
                W.CannonAttack2Debounce = 0;

            if (Held(WeaponButtons.Attack))
                PrimaryAttack();
            else
                WeaponIdle();
        }

        private void UpdateObject()
        {
            if (!_world.UpdateHeld(Owner))
                DetachObject(playSound: true, launched: false);
        }

        private void CheckForTarget()
        {
            // Server only in stock, as is everything that traces for a target.
            if (!Server)
                return;

            if (W.CannonCheckSuppress > Now)
                return;

            if (W.CannonActive)
                return;

            if (_world.TargetAhead(Owner))
            {
                W.CannonChangeState = (int)PhysCannonElements.None;
                OpenElements();
                return;
            }

            // Close the elements after a delay to prevent overact state switching.
            if (W.CannonElementDebounce < Now && W.CannonChangeState == (int)PhysCannonElements.None)
            {
                W.CannonChangeState = (int)PhysCannonElements.Closed;
                W.CannonElementDebounce = Now + 0.5f;
            }
        }

        private void PrimaryAttack()
        {
            if (W.NextPrimaryAttack > Now)
                return;

            if (W.CannonActive)
            {
                // Punch the object being held, if it is within punt range.
                int held = HeldObject();

                if (held != 0 && _world.HeldDistance(Owner, held) > PhysCannonTuning.TraceLength)
                {
                    // We can't punt this yet.
                    DryFire();
                    return;
                }

                LaunchObject();
                PrimaryFireEffect();
                SendAnim(WeaponAnim.Secondary);
                return;
            }

            // If not active, just issue a physics punch in the world.
            W.NextPrimaryAttack = Now + 0.5f;

            switch (_world.AimPunt(Owner, out int entity))
            {
                case PhysCannonPunt.NonVPhysics:
                    PuntNonVPhysics(entity);
                    break;

                case PhysCannonPunt.VPhysics:
                    PuntVPhysics(entity);
                    break;

                default:
                    DryFire();
                    break;
            }
        }

        private void SecondaryAttack()
        {
            if (W.NextSecondaryAttack > Now)
                return;

            // See if we should drop a held item.
            if (W.CannonActive && (_pressed & (int)WeaponButtons.Attack2) != 0)
            {
                W.NextPrimaryAttack = Now + 0.5f;
                W.NextSecondaryAttack = Now + 0.5f;

                DetachObject(playSound: true, launched: false);
                DoEffect(PhysCannonEffect.Ready);
                SendAnim(WeaponAnim.Primary);
                return;
            }

            // Otherwise pick it up: FindObject is the server's alone.
            if (!Server)
                return;

            switch (FindObject())
            {
                case PhysCannonFind.Found:
                    Sound(PhysCannonSounds.Pickup);
                    SendAnim(WeaponAnim.Primary);
                    W.NextSecondaryAttack = Now + 0.5f;

                    // We found an object. Debounce the button.
                    W.CannonAttack2Debounce |= _buttons;
                    break;

                case PhysCannonFind.BeingDetached:
                    W.NextSecondaryAttack = Now + 0.01f;
                    break;

                default:
                    W.NextSecondaryAttack = Now + 0.1f;
                    CloseElements();
                    break;
            }

            DoEffect(PhysCannonEffect.Holding);
        }

        private PhysCannonFind FindObject()
        {
            PhysCannonFind found = _world.FindObject(Owner, out int attached);

            if (found == PhysCannonFind.Denied)
            {
                // Make a noise to signify we can't pick this up.
                if (!W.CannonDenyPlayed)
                {
                    W.CannonDenyPlayed = true;
                    Sound(PhysCannonSounds.TooHeavy);
                }

                return PhysCannonFind.NotFound;
            }

            if (found == PhysCannonFind.Found)
            {
                // AttachObject's half that is the gun's rather than the grab controller's.
                W.CannonActive = true;
                W.CannonHeld = attached;

                // Don't drop again for a slight delay, in case they were pulling objects near them.
                W.NextSecondaryAttack = Now + 0.4f;

                DoEffect(PhysCannonEffect.Holding);
                OpenElements();
            }

            return found;
        }

        public void DetachObject(bool playSound, bool launched)
        {
            if (!Server)
            {
                // The client's grab controller would let go here; it holds nothing.
                _world.DetachHeld(Owner, launched);
                return;
            }

            if (!W.CannonActive)
                return;

            _world.DetachHeld(Owner, launched);

            W.CannonActive = false;
            W.CannonHeld = 0;

            if (playSound)
                Sound(PhysCannonSounds.Drop);
        }

        private void LaunchObject()
        {
            int held = HeldObject();

            if (!(W.CannonLastPunted == held && Now < W.CannonRepuntTime))
            {
                // FIRE!!!
                if (held != 0)
                {
                    DetachObject(playSound: false, launched: true);

                    W.CannonLastPunted = held;
                    W.CannonRepuntTime = Now + 0.5f;

                    // Launch, and the repulse effect at its centre.
                    _world.Launch(Owner, held);

                    // Don't allow the gun to regrab a thrown object!!
                    W.NextSecondaryAttack = Now + 0.5f;
                    W.NextPrimaryAttack = Now + 0.5f;

                    W.CannonEffect = (int)PhysCannonEffect.Launch;
                    W.CannonHeld = 0;
                    W.CannonActive = false;
                }
            }

            // Close the elements and suppress checking for a bit.
            W.CannonChangeState = (int)PhysCannonElements.Closed;
            W.CannonElementDebounce = Now + 0.1f;
            W.CannonCheckSuppress = Now + 0.25f;
        }

        private void PuntNonVPhysics(int entity)
        {
            if (W.CannonLastPunted == entity && Now < W.CannonRepuntTime)
                return;

            if (Server)
            {
                W.CannonLastPunted = entity;
                W.CannonRepuntTime = Now + 0.5f;

                _world.PuntNonVPhysics(Owner, entity);
                W.CannonEffect = (int)PhysCannonEffect.Launch;
            }

            PrimaryFireEffect();
            SendAnim(WeaponAnim.Secondary);

            W.CannonChangeState = (int)PhysCannonElements.Closed;
            W.CannonElementDebounce = Now + 0.5f;
            W.CannonCheckSuppress = Now + 0.25f;
        }

        private void PuntVPhysics(int entity)
        {
            if (W.CannonLastPunted == entity && Now < W.CannonRepuntTime)
                return;

            W.CannonLastPunted = entity;
            W.CannonRepuntTime = Now + 0.5f;

            if (Server && !_world.PuntVPhysics(Owner, entity))
            {
                DryFire();
                return;
            }

            // Add recoil.
            _world.ViewPunch(Owner, new QAngle(_world.RandomFloat(1f, 2f), _world.RandomFloat(-1f, 1f), 0f));

            // The explosion effect went out with the punt; the state says so.
            W.CannonEffect = (int)PhysCannonEffect.Launch;

            PrimaryFireEffect();
            SendAnim(WeaponAnim.Secondary);

            W.CannonChangeState = (int)PhysCannonElements.Closed;
            W.CannonElementDebounce = Now + 0.5f;
            W.CannonCheckSuppress = Now + 0.25f;

            // Don't allow the gun to regrab a thrown object!!
            W.NextSecondaryAttack = Now + 0.5f;
            W.NextPrimaryAttack = Now + 0.5f;
        }

        private void DryFire()
        {
            SendAnim(WeaponAnim.Primary);
            Sound(PhysCannonSounds.DryFire);
        }

        private void PrimaryFireEffect()
        {
            _world.ViewPunch(Owner, new QAngle(-6f, _world.RandomInt(-2, 2), 0f));

            if (Server)
                _world.FireFlash(Owner);

            Sound(PhysCannonSounds.Launch);
        }

        private void WeaponIdle()
        {
            if (Now <= W.TimeWeaponIdle)
                return;

            // Shake when holding an item; otherwise idle simply.
            SendAnim(W.CannonActive ? WeaponAnim.Reload : WeaponAnim.Idle);
        }

        public void OpenElements()
        {
            if (W.CannonOpen)
                return;

            Sound(PhysCannonSounds.OpenClaws);
            SendAnim(WeaponAnim.Idle);
            W.CannonOpen = true;
            DoEffect(PhysCannonEffect.Ready);
        }

        public void CloseElements()
        {
            if (!W.CannonOpen)
                return;

            Sound(PhysCannonSounds.CloseClaws);
            SendAnim(WeaponAnim.Idle);
            W.CannonOpen = false;
            DoEffect(PhysCannonEffect.Closed);
        }

        public void DoEffect(PhysCannonEffect effect) => W.CannonEffect = (int)effect;

        private void Sound(string script)
        {
            if (_a.EffectsLive)
                _a.Arms.Sound(script);
        }

        /// <summary>
        /// SendWeaponAnim: the activity's sequence on the viewmodel, restarted,
        /// and the idle due when it ends (SetIdealActivity's SetWeaponIdleTime).
        /// A model without the activity changes nothing, as stock's -1 does.
        /// </summary>
        private void SendAnim(WeaponAnim anim)
        {
            int sequence = _a.Arms.LookupSequence(anim);

            if (sequence < 0)
                return;

            _a.ViewModel.Sequence = sequence;
            _a.ViewModel.AnimationParity = AnimationParity.Next(_a.ViewModel.AnimationParity);
            _a.ViewModel.PlaybackRate = 1f;
            W.TimeWeaponIdle = Now + _a.Arms.AnimDuration(anim);
        }
    }
}

/// <summary>The physcannon's behaviour under its key, for a side's table.</summary>
public sealed class PhysCannonBehaviourSource(IPhysCannonWorld world) : IWeaponBehaviourSource
{
    private readonly PhysCannonBehaviour _behaviour = new(world);

    public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours()
    {
        yield return new(PhysCannonBehaviour.Key, _behaviour);
    }
}

/// <summary>
/// The client's replay of the physcannon (stock's CLIENT_DLL arms of the
/// shared file): it traces for nothing and holds nothing, so every question
/// that stock answers server-side only is answered "nothing there", and the
/// replay predicts what the client could decide alone — the drop, the dry
/// fire, the launch of what the server says is held, the claws and the
/// animations they send.
/// </summary>
public sealed class ReplayPhysCannonWorld : IPhysCannonWorld
{
    public bool Authoritative => false;
    public int Attached(int owner) => 0;
    public bool UpdateHeld(int owner) => false;
    public void DetachHeld(int owner, bool launched) { }
    public bool TargetAhead(int owner) => false;

    public PhysCannonFind FindObject(int owner, out int attached)
    {
        attached = 0;
        return PhysCannonFind.NotFound;
    }

    public float HeldDistance(int owner, int held) => -1f;
    public void Launch(int owner, int entity) { }

    public PhysCannonPunt AimPunt(int owner, out int entity)
    {
        entity = 0;
        return PhysCannonPunt.DryFire;
    }

    public void PuntNonVPhysics(int owner, int entity) { }
    public bool PuntVPhysics(int owner, int entity) => true;
    public void ViewPunch(int owner, QAngle punch) { }
    public void FireFlash(int owner) { }
    public float RandomFloat(float low, float high) => low;
    public int RandomInt(int low, int high) => low;
}
