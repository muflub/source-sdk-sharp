//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

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

    /// <summary>Every core behaviour, new each call: they hold no state.</summary>
    public static IEnumerable<KeyValuePair<string, IWeaponBehaviour>> All()
    {
        yield return new(Pistol, new PistolBehaviour());
        yield return new(Shotgun, new ShotgunBehaviour());
        yield return new(Frag, new FragBehaviour());
        yield return new(Smg1, new Smg1Behaviour());
    }

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

            // Reload an empty gun once it has waited as long as it must after
            // firing, then WeaponIdle.
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
    /// timed from v_grenade.mdl's cycles. Not ported: the switch away from an
    /// empty hand, which needs the owner.
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

            if ((held & (InputButtons.Attack | InputButtons.Attack2 | InputButtons.Reload)) == 0 && now >= w.TimeWeaponIdle)
                Send(attack, WeaponAnim.Idle);

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

            // ThrowGrenade / RollGrenade / LobGrenade, then DecrementAmmo.
            if (attack.EffectsLive)
            {
                attack.Arms.ThrowGrenade(attack.Type, kind, GrenadeTimer);
                attack.Arms.Sound(sound);
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
    /// CWeaponPistol: a click refires PISTOL_FASTEST_REFIRE_TIME after the
    /// last shot, while a held trigger waits GetFireRate() (the row's 0.5).
    /// </summary>
    private sealed class PistolBehaviour : IWeaponBehaviour
    {
        private const float FastestRefire = 0.1f;

        public void PrimaryAttack(in WeaponAttackContext attack)
            => attack.Weapon.SoonestPrimaryAttack = attack.Now + FastestRefire;

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
