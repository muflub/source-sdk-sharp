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
/// The HL2MP weapons' own rules on top of the shared weapon machine: what each
/// stock weapon class (src/game/shared/hl2mp/weapon_*.cpp) does that
/// CBaseCombatWeapon does not. Keyed by <see cref="WeaponType.Behaviour"/>;
/// state lives on <see cref="WeaponNetState"/>, so the client replays it.
/// </summary>
public static class Hl2mpWeaponBehaviours
{
    public const string Pistol = "hl2mp_pistol";
    public const string Shotgun = "hl2mp_shotgun";
    public const string Frag = "hl2mp_frag";
    public const string Smg1 = "hl2mp_smg1";
    public const string Crossbow = "hl2mp_crossbow";
    public const string Rpg = "hl2mp_rpg";

    /// <summary>Every core behaviour, new each call: they hold no state.</summary>
    public static IEnumerable<KeyValuePair<string, IWeaponBehaviour>> All()
    {
        yield return new(Pistol, new PistolBehaviour());
        yield return new(Shotgun, new ShotgunBehaviour());
        yield return new(Frag, new FragBehaviour());
        yield return new(Smg1, new Smg1Behaviour());
        yield return new(Crossbow, new CrossbowBehaviour());
        yield return new(Rpg, new RpgBehaviour());
    }

    /// <summary>
    /// CWeaponRPG::NotifyRocketDied: the RPG's missile has gone off or been
    /// shot down. The arsenal calls it, outside a command, when the missile it
    /// launched stops waiting on its launcher.
    /// </summary>
    public static void RocketDied(in WeaponAttackContext attack) => RpgBehaviour.NotifyRocketDied(attack);

    /// <summary>
    /// SendWeaponAnim: the animation, and the idle due once it has played.
    /// Answers its length, stock's SequenceDuration().
    /// </summary>
    private static float Send(in WeaponAttackContext attack, WeaponAnim anim)
    {
        WeaponLogic.Play(attack.Arms, attack.ViewModel, anim);
        float duration = attack.Arms.AnimDuration(anim);
        attack.Weapon.TimeWeaponIdle = attack.Now + duration;
        return duration;
    }

    /// <summary>
    /// CWeaponShotgun, whose ItemPostFrame replaces the shared machine's: a
    /// pump owed after every shot, both barrels on the primary's clock and
    /// clip, and a reload one shell per ACT_VM_RELOAD that firing can cut
    /// short. Not ported: the underwater refusal and the switch away from an
    /// empty gun, which need the owner.
    /// </summary>
    private sealed class ShotgunBehaviour : IWeaponBehaviour
    {
        public bool RunCommand(in WeaponAttackContext attack, int buttons, int pressed)
        {
            var w = attack.Weapon;
            var type = attack.Type;
            var held = (InputButtons)buttons;
            float now = attack.Now;

            if (w.NeedPump && held.HasFlag(InputButtons.Reload))
                w.DelayedReload = true;

            if (w.InReload)
            {
                if (held.HasFlag(InputButtons.Attack) && w.Clip >= 1 && !w.NeedPump)
                {
                    w.InReload = false;
                    w.NeedPump = false;
                    w.DelayedFire1 = true;
                }
                else if (held.HasFlag(InputButtons.Attack2) && w.Clip >= 2 && !w.NeedPump)
                {
                    w.InReload = false;
                    w.NeedPump = false;
                    w.DelayedFire2 = true;
                }
                else if (w.NextPrimaryAttack <= now)
                {
                    if (w.Reserve <= 0 || w.Clip >= type.ClipSize)
                        FinishReload(attack);
                    else
                        Reload(attack);
                    return true;
                }
            }

            if (w.NeedPump && w.NextPrimaryAttack <= now)
            {
                Pump(attack);
                return true;
            }

            if ((w.DelayedFire2 || held.HasFlag(InputButtons.Attack2)) && w.NextPrimaryAttack <= now)
            {
                w.DelayedFire2 = false;
                if (w.Clip <= 1)
                {
                    // One shell left: a single shot instead.
                    if (w.Clip == 1)
                        PrimaryAttack(attack);
                    else if (w.Reserve <= 0)
                        DryFire(attack);
                    else
                        StartReload(attack);
                }
                else
                {
                    if ((pressed & (int)InputButtons.Attack) != 0)
                        w.NextPrimaryAttack = now;
                    SecondaryAttack(attack);
                }
            }
            else if ((w.DelayedFire1 || held.HasFlag(InputButtons.Attack)) && w.NextPrimaryAttack <= now)
            {
                w.DelayedFire1 = false;
                if (w.Clip <= 0)
                {
                    if (w.Reserve <= 0)
                        DryFire(attack);
                    else
                        StartReload(attack);
                }
                else
                {
                    if ((pressed & (int)InputButtons.Attack) != 0)
                        w.NextPrimaryAttack = now;
                    PrimaryAttack(attack);
                }
            }

            if (held.HasFlag(InputButtons.Reload) && !w.InReload)
            {
                StartReload(attack);
                return true;
            }

            // No fire buttons down: an empty shotgun switches away once its wait
            // is over (its own copy of ReloadOrSwitchWeapons, which waits on the
            // primary alone); one with shells reloads, then WeaponIdle.
            if ((held & (InputButtons.Attack | InputButtons.Attack2)) == 0
                && WeaponLogic.SwitchIfExhausted(attack.Arms, w, now, attack.EffectsLive,
                                                 w.Clip > 0 || w.Reserve > 0, waitsOnSecondary: false))
                return true;

            if (w.Reserve > 0 && w.Clip <= 0 && w.NextPrimaryAttack < now && StartReload(attack))
                return true;

            if (now >= w.TimeWeaponIdle)
                Send(attack, WeaponAnim.Idle);
            return true;
        }

        private static bool StartReload(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            if (w.NeedPump || w.Reserve <= 0 || w.Clip >= attack.Type.ClipSize)
                return false;

            w.NextPrimaryAttack = attack.Now + Send(attack, WeaponAnim.ReloadStart);
            w.InReload = true;
            return true;
        }

        private static void Reload(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            if (w.Reserve <= 0 || w.Clip >= attack.Type.ClipSize)
                return;

            // FillClip: one shell.
            w.Clip++;
            w.Reserve--;

            if (attack.EffectsLive)
                attack.Arms.Sound(attack.Type.ReloadSound);
            w.NextPrimaryAttack = attack.Now + Send(attack, WeaponAnim.Reload);
        }

        private static void FinishReload(in WeaponAttackContext attack)
        {
            attack.Weapon.InReload = false;
            attack.Weapon.NextPrimaryAttack = attack.Now + Send(attack, WeaponAnim.ReloadFinish);
        }

        private static void Pump(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            w.NeedPump = false;

            if (w.DelayedReload)
            {
                w.DelayedReload = false;
                StartReload(attack);
            }

            if (attack.EffectsLive)
                attack.Arms.Sound(PumpSound);
            w.NextPrimaryAttack = attack.Now + Send(attack, WeaponAnim.Pump);
        }

        private static void DryFire(in WeaponAttackContext attack)
        {
            if (attack.EffectsLive)
                attack.Arms.Sound(attack.Type.EmptySound);
            attack.Weapon.NextPrimaryAttack = attack.Now + Send(attack, WeaponAnim.DryFire);
        }

        private static void PrimaryAttack(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            var type = attack.Type;

            // The sound before the round leaves, as stock's comment insists.
            if (attack.EffectsLive)
                attack.Arms.Sound(type.FireSound);
            w.NextPrimaryAttack = attack.Now + Send(attack, WeaponAnim.Primary);
            w.Clip -= 1;

            if (attack.EffectsLive)
                attack.Arms.FireBullets(in type, type.Pellets, type.Spread);
            w.NeedPump = true;
        }

        private static void SecondaryAttack(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            var type = attack.Type;
            var secondary = type.Secondary!.Value;

            if (attack.EffectsLive)
                attack.Arms.Sound(secondary.Sound);
            w.NextPrimaryAttack = attack.Now + Send(attack, WeaponAnim.Secondary);
            w.Clip -= 2;

            if (attack.EffectsLive)
                attack.Arms.FireBullets(in type, secondary.Pellets, secondary.Spread);
            w.NeedPump = true;
        }

        /// <summary>The shotgun script's SPECIAL1.</summary>
        private const string PumpSound = "Weapon_Shotgun.Special1";
    }

    /// <summary>
    /// CWeaponFrag: the pin is pulled while the trigger is held (PULLBACK_HIGH
    /// for the primary, PULLBACK_LOW for the secondary), and released once the
    /// pullback has played to its finish event the grenade goes: overhand
    /// (THROW), or underhand (HAULBACK, a lob) or along the floor
    /// (SECONDARYATTACK, a roll, when crouched). The grenade leaves, and one is
    /// spent, at the throw sequence's own event; the next is drawn when that
    /// sequence has played. The events are stock's Operator_HandleAnimEvent,
    /// timed from v_grenade.mdl's cycles. Once the last is gone the hands take
    /// the next best weapon (ReloadOrSwitchWeapons).
    /// </summary>
    private sealed class FragBehaviour : IWeaponBehaviour
    {
        // v_grenade.mdl's events, dumped from the model: EVENT_WEAPON_SEQUENCE_FINISHED
        // at 0.8 of each pullback, EVENT_WEAPON_THROW at 0.091 of the throw, THROW2
        // (the roll) and THROW3 (the lob) at 0.143 of theirs.
        public const int SequenceFinishedEvent = 3900, ThrowEvent = 3005, RollEvent = 3013, LobEvent = 3016;
        private const float PullbackEventCycle = 0.8f, ThrowEventCycle = 0.091f, UnderhandEventCycle = 0.143f;

        private const int PausedPrimary = 1, PausedSecondary = 2;

        /// <summary>GRENADE_TIMER.</summary>
        private const float GrenadeTimer = 2.5f;

        /// <summary>RETHROW_DELAY.</summary>
        private const float RethrowDelay = 0.5f;

        /// <summary>The script's SPECIAL1; its single and double shots are common/null.wav.</summary>
        private const string RollSound = "WeaponFrag.Roll";

        public bool RunCommand(in WeaponAttackContext attack, int buttons, int pressed)
        {
            var w = attack.Weapon;
            var held = (InputButtons)buttons;
            float now = attack.Now;

            // The viewmodel's event, as its frame advance dispatches it.
            if (w.AnimEvent != 0 && now >= w.AnimEventAt)
            {
                int animEvent = w.AnimEvent;
                w.AnimEvent = 0;
                HandleAnimEvent(attack, animEvent);
            }

            if (w.DrawbackFinished)
            {
                if (w.AttackPaused == PausedPrimary && !held.HasFlag(InputButtons.Attack))
                {
                    SendWithEvent(attack, WeaponAnim.Throw, ThrowEvent, ThrowEventCycle);
                    w.DrawbackFinished = false;
                }
                else if (w.AttackPaused == PausedSecondary && !held.HasFlag(InputButtons.Attack2))
                {
                    if (held.HasFlag(InputButtons.Duck))
                        SendWithEvent(attack, WeaponAnim.Secondary, RollEvent, UnderhandEventCycle);
                    else
                        SendWithEvent(attack, WeaponAnim.Haulback, LobEvent, UnderhandEventCycle);
                    w.DrawbackFinished = false;
                }
            }

            // CBaseCombatWeapon::ItemPostFrame, for a weapon with no clip.
            bool fired = false;
            if (held.HasFlag(InputButtons.Attack2) && w.NextSecondaryAttack <= now)
            {
                fired = true;
                SecondaryAttack(attack);
            }

            if (!fired && held.HasFlag(InputButtons.Attack) && w.NextPrimaryAttack <= now && w.Reserve > 0)
                PrimaryAttack(attack);

            if ((held & (InputButtons.Attack | InputButtons.Attack2 | InputButtons.Reload)) == 0)
            {
                // ReloadOrSwitchWeapons: the last grenade gone, the hands take the next.
                if (WeaponLogic.SwitchIfExhausted(attack.Arms, w, now, attack.EffectsLive, w.Reserve > 0))
                    return true;
                if (now >= w.TimeWeaponIdle)
                    Send(attack, WeaponAnim.Idle);
            }

            if (w.Redraw && now >= w.SequenceEndsAt)
                Reload(attack);
            return true;
        }

        private static void PrimaryAttack(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            if (w.Redraw)
                return;

            w.AttackPaused = PausedPrimary;
            SendWithEvent(attack, WeaponAnim.PullbackHigh, SequenceFinishedEvent, PullbackEventCycle);

            // Put off indefinitely: nobody knows how long the grenade is held.
            w.TimeWeaponIdle = float.MaxValue;
            w.NextPrimaryAttack = float.MaxValue;
        }

        private static void SecondaryAttack(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            if (w.Redraw || w.Reserve <= 0)
                return;

            w.AttackPaused = PausedSecondary;
            SendWithEvent(attack, WeaponAnim.PullbackLow, SequenceFinishedEvent, PullbackEventCycle);

            w.TimeWeaponIdle = float.MaxValue;
            w.NextSecondaryAttack = float.MaxValue;
        }

        /// <summary>Operator_HandleAnimEvent.</summary>
        private static void HandleAnimEvent(in WeaponAttackContext attack, int animEvent)
        {
            var w = attack.Weapon;
            GrenadeThrow kind;
            string sound;

            switch (animEvent)
            {
                case SequenceFinishedEvent:
                    w.DrawbackFinished = true;
                    return;
                case ThrowEvent:
                    (kind, sound) = (GrenadeThrow.Throw, attack.Type.FireSound);
                    break;
                case RollEvent:
                    (kind, sound) = (GrenadeThrow.Roll, RollSound);
                    break;
                case LobEvent:
                    (kind, sound) = (GrenadeThrow.Lob, attack.Type.FireSound);
                    break;
                default:
                    return;
            }

            // ThrowGrenade / RollGrenade / LobGrenade, then DecrementAmmo: from
            // Operator_HandleAnimEvent, which is the server's.
            if (attack.EffectsLive)
            {
                attack.Arms.ThrowGrenade(attack.Type, kind, GrenadeTimer);
                attack.Arms.ServerSound(sound);
            }

            w.Redraw = true;
            w.Reserve -= 1;

            w.NextPrimaryAttack = attack.Now + RethrowDelay;
            w.NextSecondaryAttack = attack.Now + RethrowDelay;
            w.TimeWeaponIdle = float.MaxValue;
        }

        /// <summary>CWeaponFrag::Reload: the next grenade drawn, once both triggers are free.</summary>
        private static void Reload(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            if (w.Reserve <= 0 || w.NextPrimaryAttack > attack.Now || w.NextSecondaryAttack > attack.Now)
                return;

            float duration = Send(attack, WeaponAnim.Draw);
            w.SequenceEndsAt = attack.Now + duration;
            w.NextPrimaryAttack = attack.Now + duration;
            w.NextSecondaryAttack = attack.Now + duration;
            w.Redraw = false;
        }

        /// <summary>SendWeaponAnim of a sequence that carries an event, which falls due at its cycle.</summary>
        private static void SendWithEvent(in WeaponAttackContext attack, WeaponAnim anim, int animEvent, float cycle)
        {
            var w = attack.Weapon;
            float duration = Send(attack, anim);
            w.SequenceEndsAt = attack.Now + duration;
            w.AnimEvent = animEvent;
            w.AnimEventAt = attack.Now + cycle * duration;
        }
    }

    /// <summary>
    /// CWeaponSMG1::SecondaryAttack: the grenade launcher, which may cut a
    /// reload short. With no grenade it dry-fires and waits half a second;
    /// with one it plays the double shot, launches the row's projectile
    /// (grenade_ar2), spends it, holds the primary off for half a second and
    /// the next grenade for a second. Everything else is the shared machine's.
    /// Not ported: the underwater refusal, which needs the owner.
    /// </summary>
    private sealed class Smg1Behaviour : IWeaponBehaviour
    {
        private const float EmptyRefire = 0.5f, PrimaryAfterGrenade = 0.5f, GrenadeRefire = 1.0f;

        /// <summary>CWeaponSMG1::Reload: a started reload plays WeaponSound( RELOAD ) on both sides.</summary>
        public void Reload(in WeaponAttackContext attack)
        {
            if (attack.EffectsLive)
                attack.Arms.Sound(attack.Type.ReloadSound);
        }

        public bool RunCommand(in WeaponAttackContext attack, int buttons, int pressed)
        {
            var w = attack.Weapon;
            if (!((InputButtons)buttons).HasFlag(InputButtons.Attack2) || w.NextSecondaryAttack > attack.Now
                || attack.Type.Secondary is not { } secondary)
                return false;

            if (w.SecondaryReserve <= 0)
            {
                Send(attack, WeaponAnim.DryFire);
                if (attack.EffectsLive)
                    attack.Arms.Sound(attack.Type.EmptySound);
                w.NextSecondaryAttack = attack.Now + EmptyRefire;
                return true;
            }

            w.InReload = false;

            // The sound before the round, as stock's CMachineGun insists.
            if (attack.EffectsLive)
            {
                attack.Arms.Sound(secondary.Sound);
                attack.Arms.Launch(attack.Type, secondary.Projectile, secondary.LaunchSpeed, secondary.Thrown);
            }

            Send(attack, WeaponAnim.Secondary);
            w.SecondaryReserve -= 1;
            w.NextPrimaryAttack = attack.Now + PrimaryAfterGrenade;
            w.NextSecondaryAttack = attack.Now + GrenadeRefire;
            return true;
        }
    }

    /// <summary>
    /// CWeaponCrossbow (weapon_crossbow.cpp, hl2mp): a bolt at BOLT_AIR_VELOCITY
    /// with Single and BoltFly, both triggers held off 0.75 s, and the reload
    /// owed once the shot has played (m_bMustReload); the scope on each press
    /// of the second trigger, even mid-reload; the reload's and the draw's
    /// model events, the first of the reload's electrifying the bolt
    /// (BoltElectrify, the glowing skin); the fidget for an idle with nothing
    /// loaded. Not ported: the underwater speed, which needs the owner.
    /// </summary>
    private sealed class CrossbowBehaviour : IWeaponBehaviour
    {
        // v_crossbow.mdl's events, dumped: the reload's EVENT_WEAPON_THROW at
        // 0.509 (CHARGER_STATE_START_LOAD), THROW2 at 0.636 (START_CHARGE) and
        // THROW3 at 1.0 (READY); the draw's THROW2 at 0.467 and THROW3 at 0.933.
        private const int StartLoad = 3005, StartCharge = 3013, Ready = 3016;
        private static readonly ImmutableArray<(int Event, float Cycle)> ReloadEvents =
            [(StartLoad, 0.509f), (StartCharge, 0.636f), (Ready, 1f)];
        private static readonly ImmutableArray<(int Event, float Cycle)> DrawEvents = [(StartCharge, 0.467f), (Ready, 0.933f)];

        /// <summary>CWeaponCrossbow::FireBolt's refire.</summary>
        private const float Refire = 0.75f;

        /// <summary>BOLT_WATER_VELOCITY: a bolt fired from under water.</summary>
        public const float WaterSpeed = 1500f;

        /// <summary>SequenceDuration's answer for a sequence the model does not have.</summary>
        private const float MustReloadAfter = 0.1f;

        /// <summary>The script's SPECIAL1 and SPECIAL2.</summary>
        private const string Electrify = "Weapon_Crossbow.BoltElectrify", BoltFly = "Weapon_Crossbow.BoltFly";

        /// <summary>BOLT_SKIN_NORMAL and BOLT_SKIN_GLOW.</summary>
        private const int SkinNormal = 0, SkinGlow = 1;

        /// <summary>ToggleZoom's two SetFOV calls.</summary>
        private const int ZoomFov = 20;
        private const float ZoomInRate = 0.1f, ZoomOutRate = 0.2f;

        public bool RunCommand(in WeaponAttackContext attack, int buttons, int pressed)
        {
            var w = attack.Weapon;
            var held = (InputButtons)buttons;
            float now = attack.Now;

            if (w.AnimEvent != 0 && now >= w.AnimEventAt)
            {
                int animEvent = w.AnimEvent;
                w.AnimEvent = 0;
                HandleEvent(attack, animEvent);
            }

            // CheckZoomToggle, from ItemBusyFrame and ItemPostFrame alike.
            if ((pressed & (int)InputButtons.Attack2) != 0)
                ToggleZoom(attack);

            if (w.MustReload && now >= w.TimeWeaponIdle)
                Reload(attack);

            // CBaseCombatWeapon::ItemPostFrame for a one-round clip.
            if (w.InReload)
            {
                if (w.NextPrimaryAttack <= now)
                    FinishReload(attack);
                return true;
            }

            if (held.HasFlag(InputButtons.Attack) && w.NextPrimaryAttack <= now)
            {
                if (w.Clip > 0)
                    FireBolt(attack);
                else
                    Reload(attack);
                return true;
            }

            if (held.HasFlag(InputButtons.Reload))
            {
                Reload(attack);
                return true;
            }

            if ((held & (InputButtons.Attack | InputButtons.Attack2 | InputButtons.Reload)) == 0)
            {
                // ReloadOrSwitchWeapons (the script's item_flags 0: it switches
                // away with no bolt left anywhere, and reloads itself once both
                // triggers are free), then WeaponIdle.
                if (WeaponLogic.SwitchIfExhausted(attack.Arms, w, now, attack.EffectsLive, w.Clip > 0 || w.Reserve > 0))
                    return true;
                if (w.Clip <= 0 && w.Reserve > 0 && w.NextPrimaryAttack < now && w.NextSecondaryAttack < now)
                    Reload(attack);
                else if (now >= w.TimeWeaponIdle)
                    Send(attack, w.Clip <= 0 ? WeaponAnim.Fidget : WeaponAnim.Idle);
            }

            return true;
        }

        /// <summary>CWeaponCrossbow::Deploy: the glowing bolt, and the draw's charge-up.</summary>
        public void Deploy(in WeaponAttackContext attack)
        {
            if (attack.Weapon.Clip > 0)
                attack.ViewModel.Skin = SkinGlow;
            Schedule(attack, DrawEvents, 0, attack.Now, attack.Arms.AnimDuration(WeaponAnim.Draw));
        }

        /// <summary>CWeaponCrossbow::Holster: out of the scope, the charger off.</summary>
        public void Holster(in WeaponAttackContext attack)
        {
            if (attack.Weapon.InZoom)
                ToggleZoom(attack);
            attack.ViewModel.Skin = SkinNormal;
            attack.Weapon.AnimEvent = 0;
        }

        private static void FireBolt(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            var type = attack.Type;

            // BOLT_WATER_VELOCITY with the shooter's eyes under water, else BOLT_AIR_VELOCITY.
            if (attack.EffectsLive)
                attack.Arms.Launch(in type, type.Projectile, attack.Arms.EyesUnderwater() ? WaterSpeed : type.LaunchSpeed,
                                   thrown: false);

            w.Clip -= 1;
            if (attack.EffectsLive)
            {
                attack.Arms.Sound(type.FireSound);
                attack.Arms.Sound(BoltFly);
            }

            Send(attack, WeaponAnim.Primary);
            w.NextPrimaryAttack = w.NextSecondaryAttack = attack.Now + Refire;

            // DoLoadEffect's glow then the DISCHARGE that puts it out: an empty bow.
            attack.ViewModel.Skin = SkinNormal;
            w.AnimEvent = 0;

            // SetWeaponIdleTime( curtime + SequenceDuration( ACT_VM_PRIMARYATTACK ) )
            // hands an activity to a call that takes a sequence: out of range
            // on any model, it answers its 0.1, and the reload cuts the shot.
            w.MustReload = true;
            w.TimeWeaponIdle = attack.Now + MustReloadAfter;
        }

        /// <summary>CWeaponCrossbow::Reload: DefaultReload's ACT_VM_RELOAD, whose events charge the bolt.</summary>
        private static void Reload(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            if (w.InReload || w.Reserve <= 0 || w.Clip >= attack.Type.ClipSize)
                return;

            // DefaultReload's sound: the client's alone.
            if (attack.EffectsLive)
                attack.Arms.ClientSound(attack.Type.ReloadSound);

            float duration = Send(attack, WeaponAnim.Reload);
            w.NextPrimaryAttack = w.NextSecondaryAttack = attack.Now + duration;
            w.InReload = true;
            w.MustReload = false;
            Schedule(attack, ReloadEvents, 0, attack.Now, duration);
        }

        private static void FinishReload(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            int take = Math.Min(attack.Type.ClipSize - w.Clip, w.Reserve);
            w.Clip += take;
            w.Reserve -= take;
            w.InReload = false;
        }

        /// <summary>SetChargerState, as Operator_HandleAnimEvent drives it; the next event of the sequence is due after.</summary>
        private static void HandleEvent(in WeaponAttackContext attack, int animEvent)
        {
            if (animEvent == StartLoad)
            {
                // Operator_HandleAnimEvent is the server's.
                if (attack.EffectsLive)
                    attack.Arms.ServerSound(Electrify);
                attack.ViewModel.Skin = SkinGlow;
            }

            var events = attack.Weapon.InReload ? ReloadEvents : DrawEvents;
            int at = -1;
            for (int i = 0; i < events.Length; i++)
            {
                if (events[i].Event == animEvent)
                    at = i;
            }

            if (at >= 0)
            {
                WeaponAnim anim = attack.Weapon.InReload ? WeaponAnim.Reload : WeaponAnim.Draw;
                float duration = attack.Arms.AnimDuration(anim);
                float started = attack.Weapon.SequenceEndsAt - duration;
                Schedule(attack, events, at + 1, started, duration);
            }
        }

        /// <summary>The <paramref name="next"/>th event of a sequence that started at <paramref name="started"/>, if any.</summary>
        private static void Schedule(in WeaponAttackContext attack, ImmutableArray<(int Event, float Cycle)> events, int next,
                                     float started, float duration)
        {
            var w = attack.Weapon;
            w.SequenceEndsAt = started + duration;
            if (next >= events.Length)
            {
                w.AnimEvent = 0;
                return;
            }

            w.AnimEvent = events[next].Event;
            w.AnimEventAt = started + events[next].Cycle * duration;
        }

        /// <summary>ToggleZoom: SetFOV( 20, 0.1 ) in, ( 0, 0.2 ) out.</summary>
        private static void ToggleZoom(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            w.InZoom = !w.InZoom;
            if (attack.EffectsLive)
                attack.Arms.SetFov(w.InZoom ? ZoomFov : 0, w.InZoom ? ZoomInRate : ZoomOutRate);
        }
    }

    /// <summary>
    /// CWeaponRPG (shared/hl2mp/weapon_rpg.cpp) on CBaseCombatWeapon's
    /// ItemPostFrame for a weapon with no clip, and CBaseHL2MPCombatWeapon's
    /// lowered idle.
    ///
    /// <para>
    /// One missile at a time: the trigger refuses while it flies, and while the
    /// reload its death starts is playing; the next shot waits half a second.
    /// The laser comes on with the first frame after the draw (SuppressGuiding
    /// starts the guiding whenever there is no dot), is switched off while the
    /// weapon is lowered — which an RPG with nothing left to fire is — and goes
    /// with a holster, a drop, or the last round once its missile is gone. HL2MP
    /// gives the RPG no alt-fire: its laser cannot be toggled.
    /// </para>
    ///
    /// <para>
    /// State is <see cref="WeaponNetState.Special"/>'s <see cref="RpgState"/>
    /// bits; <see cref="WeaponNetState.SequenceEndsAt"/> is the draw's end
    /// (stock's SetNextAttack, before which ItemPostFrame does not run) and
    /// <see cref="WeaponNetState.SpecialTime"/> the empty click's clock
    /// (m_flNextEmptySoundTime).
    /// </para>
    ///
    /// <para>
    /// An RPG with nothing left to fire and no missile in flight is put away
    /// for the next best weapon (ReloadOrSwitchWeapons' SwitchToNextBestWeapon).
    /// </para>
    /// </summary>
    private sealed class RpgBehaviour : IWeaponBehaviour
    {
        /// <summary>PrimaryAttack's m_flNextPrimaryAttack = curtime + 0.5f.</summary>
        private const float Refire = 0.5f;

        /// <summary>HandleFireOnEmpty's m_flNextEmptySoundTime = curtime + 0.5.</summary>
        private const float EmptySoundInterval = 0.5f;

        /// <summary>weapon_rpg.txt's SPECIAL1 and SPECIAL2.</summary>
        internal const string LaserOn = "Weapon_RPG.LaserOn", LaserOff = "Weapon_RPG.LaserOff";

        public void Deploy(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;

            // m_bInitialStateUpdate, the draw as the activity, and DefaultDeploy's
            // SetNextAttack( curtime + SequenceDuration() ).
            w.Special = RpgState.WithActivity(RpgState.Set(w.Special, RpgState.InitialStateUpdate, true), RpgActivity.Draw);
            w.SequenceEndsAt = attack.Now + attack.Arms.AnimDuration(WeaponAnim.Draw);
        }

        public bool CanHolster(in WeaponAttackContext attack) => !RpgState.Has(attack.Weapon.Special, RpgState.MissileOut);

        public void Holster(in WeaponAttackContext attack)
        {
            StopGuiding(attack);
            if (attack.EffectsLive)
                attack.Arms.UpdateLaserDot(exists: false, on: false);
        }

        public bool RunCommand(in WeaponAttackContext attack, int buttons, int pressed)
        {
            var w = attack.Weapon;
            float now = attack.Now;

            // CBasePlayer::ItemPostFrame: ItemBusyFrame until the draw is done.
            if (now < w.SequenceEndsAt)
                return true;

            var held = (InputButtons)buttons;
            int released = w.LastButtons & ~buttons;

            // CBaseCombatWeapon::ItemPostFrame. The alt-fire is the base's, which
            // does nothing, and never blocks the primary.
            if (held.HasFlag(InputButtons.Attack) && w.NextPrimaryAttack <= now)
            {
                if (w.Reserve <= 0)
                {
                    HandleFireOnEmpty(attack);
                }
                else
                {
                    if ((pressed & (int)InputButtons.Attack) != 0 || (released & (int)InputButtons.Attack2) != 0)
                        w.NextPrimaryAttack = now;

                    PrimaryAttack(attack);
                }
            }

            if ((held & (InputButtons.Attack | InputButtons.Attack2 | InputButtons.Reload)) == 0)
            {
                // ReloadOrSwitchWeapons: no clip to reload; nothing to fire and
                // no missile in flight, the hands take the next best weapon.
                w.Special = RpgState.Set(w.Special, RpgState.FireOnEmpty, false);

                bool switched = WeaponLogic.SwitchIfExhausted(attack.Arms, w, now, attack.EffectsLive, HasAnyAmmo(w));
                if (!switched && !w.InReload)
                    WeaponIdle(attack);
            }

            // CWeaponRPG::ItemPostFrame's tail.
            if (RpgState.Has(w.Special, RpgState.InitialStateUpdate) && RpgState.Activity(w.Special) != RpgActivity.Draw)
            {
                StartGuiding(attack);
                w.Special = RpgState.Set(w.Special, RpgState.InitialStateUpdate, false);
            }

            SuppressGuiding(attack, RpgState.Activity(w.Special) == RpgActivity.IdleLowered);

            if (w.Reserve <= 0 && !RpgState.Has(w.Special, RpgState.MissileOut))
                StopGuiding(attack);

            // UpdateLaserPosition, and the dot made or removed to match.
            if (attack.EffectsLive)
                attack.Arms.UpdateLaserDot(RpgState.Has(w.Special, RpgState.DotOut), RpgState.Has(w.Special, RpgState.DotOn));

            return true;
        }

        /// <summary>
        /// PrimaryAttack: nothing while a missile flies or the reload plays;
        /// else the missile, a round, ACT_VM_PRIMARYATTACK and the shot's sound.
        /// </summary>
        private static void PrimaryAttack(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;

            if (RpgState.Has(w.Special, RpgState.MissileOut) || RpgState.Activity(w.Special) == RpgActivity.Reload)
                return;

            w.NextPrimaryAttack = attack.Now + Refire;

            if (attack.EffectsLive)
                attack.Arms.LaunchMissile(attack.Type);
            w.Special = RpgState.Set(w.Special, RpgState.MissileOut, true);

            w.Reserve -= 1;
            Send(attack, RpgActivity.Primary);

            if (attack.EffectsLive)
                attack.Arms.Sound(attack.Type.FireSound);
        }

        /// <summary>HandleFireOnEmpty: the click, at most twice a second, then nothing until released.</summary>
        private static void HandleFireOnEmpty(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;

            if (RpgState.Has(w.Special, RpgState.FireOnEmpty))
            {
                // ReloadOrSwitchWeapons, on the release: nothing to reload.
                w.Special = RpgState.Set(w.Special, RpgState.FireOnEmpty, false);
                return;
            }

            if (w.SpecialTime < attack.Now)
            {
                if (attack.EffectsLive)
                    attack.Arms.Sound(attack.Type.EmptySound);
                w.SpecialTime = attack.Now + EmptySoundInterval;
            }

            w.Special = RpgState.Set(w.Special, RpgState.FireOnEmpty, true);
        }

        /// <summary>
        /// CBaseHL2MPCombatWeapon::WeaponIdle: down to ACT_VM_IDLE_LOWERED and
        /// held there while it should be lowered (an RPG with no round and no
        /// missile), straight back up when it should not, and the idle again
        /// whenever the last one has played.
        /// </summary>
        private static void WeaponIdle(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            RpgActivity activity = RpgState.Activity(w.Special);
            bool elapsed = attack.Now > w.TimeWeaponIdle;

            if (!HasAnyAmmo(w))
            {
                if (activity != RpgActivity.IdleLowered || elapsed)
                    Send(attack, RpgActivity.IdleLowered);
            }
            else if (activity == RpgActivity.IdleLowered || elapsed)
            {
                // m_flRaiseTime is never set without Ready(), so a lowered RPG
                // that has something to fire again comes up at once.
                Send(attack, RpgActivity.Idle);
            }
        }

        /// <summary>HasAnyAmmo: a missile in flight counts.</summary>
        private static bool HasAnyAmmo(WeaponNetState w) => w.Reserve > 0 || RpgState.Has(w.Special, RpgState.MissileOut);

        /// <summary>StartGuiding: unless suppressed, guiding, LaserOn and the dot (if there is a round for it).</summary>
        private static void StartGuiding(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            if (RpgState.Has(w.Special, RpgState.HideGuiding))
                return;

            w.Special = RpgState.Set(w.Special, RpgState.Guiding, true);

            // CreateLaserPointer: once, and only with a round to guide; made off.
            // LaserOn with the dot it makes (see the class remarks).
            if (!RpgState.Has(w.Special, RpgState.DotOut) && w.Reserve > 0)
            {
                w.Special = RpgState.Set(RpgState.Set(w.Special, RpgState.DotOut, true), RpgState.DotOn, false);
                if (attack.EffectsLive)
                    attack.Arms.ServerSound(LaserOn);
            }
        }

        /// <summary>StopGuiding: not guiding, LaserOff, and the dot gone.</summary>
        private static void StopGuiding(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;

            // LaserOff with the dot it removes (see the class remarks).
            if (RpgState.Has(w.Special, RpgState.DotOut) && attack.EffectsLive)
                attack.Arms.ServerSound(LaserOff);

            w.Special = RpgState.Set(w.Special, RpgState.Guiding | RpgState.DotOut | RpgState.DotOn, false);
        }

        /// <summary>
        /// SuppressGuiding: hidden or not, and with no dot the guiding is
        /// started (which makes one when it can); the dot is on when not hidden.
        /// </summary>
        private static void SuppressGuiding(in WeaponAttackContext attack, bool hide)
        {
            var w = attack.Weapon;
            w.Special = RpgState.Set(w.Special, RpgState.HideGuiding, hide);

            if (!RpgState.Has(w.Special, RpgState.DotOut))
            {
                StartGuiding(attack);
                if (!RpgState.Has(w.Special, RpgState.DotOut))
                    return;
            }

            w.Special = RpgState.Set(w.Special, RpgState.DotOn, !hide);
        }

        /// <summary>NotifyRocketDied: the missile is gone, and the reload starts unless it already is.</summary>
        internal static void NotifyRocketDied(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            w.Special = RpgState.Set(w.Special, RpgState.MissileOut, false);

            if (RpgState.Activity(w.Special) == RpgActivity.Reload)
                return;

            // Reload: with a round left, the reload's sound and ACT_VM_RELOAD.
            if (w.Reserve <= 0)
                return;

            if (attack.EffectsLive)
                attack.Arms.Sound(attack.Type.ReloadSound);
            Send(attack, RpgActivity.Reload);
        }

        /// <summary>
        /// SendWeaponAnim through SetIdealActivity: an activity the model does
        /// not carry changes nothing; one it does becomes the weapon's activity
        /// and the viewmodel's sequence, and the idle is due when it has played.
        /// </summary>
        private static void Send(in WeaponAttackContext attack, RpgActivity activity)
        {
            int sequence = attack.Arms.LookupActivity(RpgState.ActivityName(activity));
            if (sequence < 0)
                return;

            var w = attack.Weapon;
            var vm = attack.ViewModel;
            w.Special = RpgState.WithActivity(w.Special, activity);
            vm.Sequence = sequence;
            vm.AnimationParity = AnimationParity.Next(vm.AnimationParity);
            vm.PlaybackRate = 1f;
            w.TimeWeaponIdle = attack.Now + attack.Arms.SequenceDuration(sequence);
        }
    }

    /// <summary>
    /// CWeaponPistol: a click refires PISTOL_FASTEST_REFIRE_TIME after the
    /// last shot, while a held trigger waits GetFireRate() (the row's 0.5).
    /// </summary>
    private sealed class PistolBehaviour : IWeaponBehaviour
    {
        private const float FastestRefire = 0.1f;

        /// <summary>PISTOL_ACCURACY_SHOT_PENALTY_TIME and PISTOL_ACCURACY_MAXIMUM_PENALTY_TIME.</summary>
        private const float ShotPenalty = 0.2f, MaximumPenalty = 1.5f;

        /// <summary>VECTOR_CONE_1DEGREES and VECTOR_CONE_6DEGREES: the sines the cone is lerped between.</summary>
        private const float AccurateCone = 0.00873f, InaccurateCone = 0.05234f;

        /// <summary>
        /// UpdatePenaltyTime, from ItemPreFrame (and ItemBusyFrame, which runs
        /// at the same point): with the trigger up and the click's wait over,
        /// the penalty drains by the time since it was last brought up to date.
        /// </summary>
        public void ItemPreFrame(in WeaponAttackContext attack, int buttons)
        {
            var w = attack.Weapon;
            if (((InputButtons)buttons & InputButtons.Attack) == 0 && w.SoonestPrimaryAttack < attack.Now)
                w.AccuracyPenalty = Math.Clamp(w.AccuracyPenalty - (attack.Now - w.AccuracyPenaltyAt), 0f, MaximumPenalty);
            w.AccuracyPenaltyAt = attack.Now;
        }

        /// <summary>
        /// GetBulletSpread: VectorLerp( VECTOR_CONE_1DEGREES,
        /// VECTOR_CONE_6DEGREES ) by the penalty over its maximum, in the
        /// cone's sines, as stock lerps the vector.
        /// </summary>
        public float? BulletSpread(in WeaponAttackContext attack)
        {
            float ramp = Math.Clamp(attack.Weapon.AccuracyPenalty / MaximumPenalty, 0f, 1f);
            float sine = AccurateCone + (InaccurateCone - AccurateCone) * ramp;
            return MathF.Asin(sine) * 180f / MathF.PI;
        }

        /// <summary>PrimaryAttack's tail: the click's soonest refire, and the shot's penalty, which may pass the maximum.</summary>
        public void PrimaryAttack(in WeaponAttackContext attack)
        {
            attack.Weapon.SoonestPrimaryAttack = attack.Now + FastestRefire;
            attack.Weapon.AccuracyPenalty += ShotPenalty;
        }

        /// <summary>CWeaponPistol::Reload: a started reload plays WeaponSound( RELOAD ) on both sides, and clears the penalty.</summary>
        public void Reload(in WeaponAttackContext attack)
        {
            if (attack.EffectsLive)
                attack.Arms.Sound(attack.Type.ReloadSound);
            attack.Weapon.AccuracyPenalty = 0f;
        }

        public void ItemPostFrame(in WeaponAttackContext attack)
        {
            var w = attack.Weapon;
            if (w.InReload)
                return;

            var held = (InputButtons)w.LastButtons;

            if (held.HasFlag(InputButtons.Attack2))
            {
                w.SoonestPrimaryAttack = attack.Now + FastestRefire;
                w.NextPrimaryAttack = attack.Now + FastestRefire;
            }

            // "Allow a refire as fast as the player can click."
            if (!held.HasFlag(InputButtons.Attack) && w.SoonestPrimaryAttack < attack.Now)
                w.NextPrimaryAttack = attack.Now - 0.1f;
        }
    }
}
