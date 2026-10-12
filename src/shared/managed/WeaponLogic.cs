//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What one side must do for the weapon machine — the hands, where
/// <see cref="WeaponLogic"/> is the head.
///
/// <para>
/// The machine decides WHEN; an implementation of this does the thing. The
/// server's implementation traces bullets, spawns projectiles and emits sounds
/// on the carrier; the client's replay implementation does almost nothing,
/// because effects are the server's until client-side effects land — but it
/// still answers <see cref="LookupSequence"/> and <see cref="AnimDuration"/>,
/// because those write PREDICTED STATE and the two sides must write the same
/// numbers (they read the same model, so they do).
/// </para>
/// </summary>
public interface IWeaponArms
{
    /// <summary>Plays a soundscript entry on the carrier. Silently nothing for "".</summary>
    void Sound(string soundScript);

    /// <summary>Hitscan fire: trace, damage, impact — the ammo row says how hard.</summary>
    void FireBullets(in WeaponType type, int pellets, float spreadDegrees);

    /// <summary>One melee swing: a short trace, the row's own damage.</summary>
    void MeleeSwing(in WeaponType type);

    /// <summary>Sends what the weapon launches instead of a bullet.</summary>
    void Launch(in WeaponType type, string projectile, float speed, bool thrown);

    /// <summary>
    /// The named animation's sequence index IN THE CURRENT VIEWMODEL, or -1.
    /// Both sides answer from the same model, which is what lets the sequence
    /// number be predicted state.
    /// </summary>
    int LookupSequence(WeaponAnim anim);

    /// <summary>How long that animation runs, which is how long a reload takes.</summary>
    float AnimDuration(WeaponAnim anim);
}

/// <summary>
/// How an animation KIND becomes a sequence index on the model in hand.
///
/// SHARED, and that is load-bearing rather than tidy: the sequence index the
/// machine writes into the viewmodel's state is PREDICTED, so the server's
/// arms and the client's replay arms must resolve an animation through the
/// same lists or every shot mispredicts by one field.
///
/// <para>
/// ACTIVITIES first, names second. Sequence names are not uniform across
/// HL2's v_ models — the SMG's fire is 'fire01', the pulse rifle's 'IR_fire',
/// the crowbar has no fire at all, only 'hitcenter1' — which is exactly why
/// stock's SendWeaponAnim took an activity. The live bug this closed: every
/// fire-NAME lookup answered -1 on every HL2 v_ model, the machine sent no
/// fire animation, and nothing anywhere said so. The name lists stay as the
/// fallback for a model with untagged sequences.
/// </para>
/// </summary>
public static class WeaponAnimNames
{
    public static string[] For(WeaponAnim anim) => anim switch
    {
        WeaponAnim.Primary => new[] { "fire", "attack1", "shoot", "fire1" },
        WeaponAnim.Reload  => new[] { "reload", "reload1" },
        WeaponAnim.Draw    => new[] { "draw", "deploy" },
        _                  => new[] { "idle01", "idle" },
    };

    /// <summary>
    /// Which ACTIVITY names an animation kind tries, most specific first.
    /// Primary carries the two stand-ins stock resolves through weapon
    /// activity tables: the crowbar tags its swings ACT_VM_HITCENTER and the
    /// grenade tags its throw ACT_VM_THROW — neither model has a
    /// PRIMARYATTACK, and without the fallbacks their triggers are as silent
    /// as the bug this fixed.
    /// </summary>
    public static string[] ActivitiesFor(WeaponAnim anim) => anim switch
    {
        WeaponAnim.Primary => new[] { "ACT_VM_PRIMARYATTACK", "ACT_VM_HITCENTER", "ACT_VM_THROW" },
        WeaponAnim.Reload  => new[] { "ACT_VM_RELOAD" },
        WeaponAnim.Draw    => new[] { "ACT_VM_DRAW" },
        _                  => new[] { "ACT_VM_IDLE" },
    };

    /// <summary>
    /// THE resolution, whole: activities through the deterministic
    /// first-match lookup, then names, then -1. One method so the server's
    /// arms, the client's replay arms and the report cannot drift — they all
    /// answer through this.
    /// </summary>
    public static int Resolve(IEngineEntityRender render, EntityHandle viewModel, WeaponAnim anim)
    {
        foreach (string activity in ActivitiesFor(anim))
        {
            int sequence = render.LookupSequenceByActivity(viewModel, activity);

            if (sequence >= 0)
                return sequence;
        }

        foreach (string name in For(anim))
        {
            int sequence = render.LookupSequence(viewModel, name);

            if (sequence >= 0)
                return sequence;
        }

        return -1;
    }
}

/// <summary>
/// When a weapon fires, when it reloads, and what it sounds like.
///
/// This is CBaseCombatWeapon's state machine, moved — and since Phase 10, moved
/// AGAIN: off the native shell's members and onto <see cref="WeaponNetState"/>,
/// the declared blob that is the wire format and the prediction schema at once.
/// The machine runs once per player command on the server and once per REPLAYED
/// command on the client (Phase 9c4's crossing, phase 1), so everything it
/// computes must come out the same from the same inputs on both sides —
/// <paramref name="effectsLive"/> is the only licence to do anything a replay
/// must not repeat.
///
/// <para>
/// The rules are stock's, deliberately and line for line where it matters,
/// because a weapon that behaves nearly like Half-Life's is worse than one that
/// behaves like it: the difference shows up as a gun that feels wrong and
/// nobody can say why.
/// </para>
/// </summary>
public sealed class WeaponLogic
{
    private readonly IWeaponArms _arms;

    public WeaponLogic(IWeaponArms arms)
    {
        _arms = arms;
    }

    /// <summary>
    /// Told about every projectile as it leaves. The server subscribes to
    /// attribute the throw; the client's arms never launch, so it never fires
    /// there. Carried on the ARMS side now — see the server's WeaponArms.
    /// </summary>
    public Action<EntityHandle, EntityHandle, Vector>? Launched { get; set; }

    //-------------------------------------------------------------------------
    // The per-command machine.
    //-------------------------------------------------------------------------

    /// <summary>
    /// One player command's worth of weapon behaviour, after the movement has
    /// run (phase 1). Reads and writes ONLY the two declared states plus the
    /// arms — which is the whole of why the client can replay it.
    /// </summary>
    /// <param name="effectsLive">
    /// True on the server and on the client's first prediction of a command;
    /// false for every replay of one already predicted. Sounds and traces hang
    /// off it; state writes never do.
    /// </param>
    public void RunCommand(WeaponNetState w, ViewModelNetState vm,
                           int buttons, float now, bool effectsLive)
    {
        if (WeaponTypes.ByIndex(w.WeaponTypeId) is not WeaponType type)
            return;

        int pressed = buttons & ~w.LastButtons;
        w.LastButtons = buttons;

        var held = (InputButtons)buttons;

        // Reloading is checked before firing, because a reload in progress is
        // what decides whether firing is possible at all.
        if (type.ClipSize > 0 && w.InReload)
        {
            FinishReloadIfDue(w, in type, now);
            return;
        }

        // Secondary before primary, which is stock's order and matters when
        // both are held: a player who alt-fires while holding the trigger down
        // means the alt-fire, and checking primary first would swallow it every
        // frame.
        if (held.HasFlag(InputButtons.Attack2)
            && type.Secondary is not null
            && w.NextSecondaryAttack <= now)
        {
            SecondaryAttack(w, vm, in type, now, effectsLive);
            return;
        }

        if (held.HasFlag(InputButtons.Attack) && w.NextPrimaryAttack <= now)
        {
            if (IsEmpty(w, in type))
            {
                HandleFireOnEmpty(w, in type, now, effectsLive);
                return;
            }

            // The leading edge resets the clock. Without it, a player who holds
            // the trigger through a lull gets a burst of owed shots the moment
            // they press it again — the catch-up loop would fire one for every
            // interval since they last shot.
            if (((InputButtons)pressed).HasFlag(InputButtons.Attack))
                w.NextPrimaryAttack = now;

            PrimaryAttack(w, vm, in type, now, effectsLive);
            return;
        }

        // Reload on request, or because the clip ran dry and nothing is held.
        bool wantsReload = held.HasFlag(InputButtons.Reload)
                        || (buttons == 0 && type.ClipSize > 0 && w.Clip <= 0);

        if (wantsReload && type.ClipSize > 0 && w.NextPrimaryAttack <= now)
        {
            Reload(w, vm, in type, now, effectsLive);
            return;
        }

        // Nothing being asked for: let the view model settle back to idle.
        // A viewmodel whose idle is never sent stops animating once its deploy
        // finishes, which reads as the gun freezing in the player's hands.
        if (buttons == 0 && now >= w.TimeWeaponIdle)
        {
            SendAnim(vm, WeaponAnim.Idle, 0f);
            w.TimeWeaponIdle = now + IdleRepeat;
        }
    }

    /// <summary>
    /// One pull of the trigger.
    ///
    /// The shape is stock's: a catch-up loop that owes one shot per fire-rate
    /// interval since the last, clamped to what is in the clip. That loop is
    /// what keeps rate of fire independent of framerate, and dropping it would
    /// make every automatic weapon slower on a busy server.
    /// </summary>
    public void PrimaryAttack(WeaponNetState w, ViewModelNetState vm,
                              in WeaponType type, float now, bool effectsLive)
    {
        // An empty clip reloads instead of firing, which is what makes a reload
        // happen at all without anyone pressing a key for it.
        if (type.ClipSize > 0 && w.Clip <= 0)
        {
            Reload(w, vm, in type, now, effectsLive);
            return;
        }

        int shots = ShotsOwed(w.NextPrimaryAttack, now, type.Interval);

        w.NextPrimaryAttack = NextAttackAfter(w.NextPrimaryAttack, now, type.Interval);
        w.TimeWeaponIdle = now + IdleSettle;

        // A round is a round whatever it is loaded with, so the clip is spent
        // per SHOT and the pellets are multiplied afterwards. A shotgun that
        // took seven rounds per pull would empty in one.
        if (type.ClipSize > 0)
        {
            shots = Math.Min(shots, w.Clip);
            w.Clip -= shots;
        }
        else if (type.AmmoName.Length > 0)
        {
            shots = Math.Min(shots, w.Reserve);
            w.Reserve -= shots;
        }
        else
        {
            // Melee: nothing to spend, one swing per interval.
            shots = Math.Min(shots, 1);
        }

        if (shots <= 0)
            return;

        SendAnim(vm, WeaponAnim.Primary, 0f);

        // The sound is per shot, stock's order — before the round leaves.
        if (effectsLive)
        {
            for (int i = 0; i < shots; i++)
                _arms.Sound(type.FireSound);
        }

        if (type.IsMelee)
        {
            if (effectsLive)
                _arms.MeleeSwing(in type);
            return;
        }

        // A weapon either throws or shoots, never both. A launcher that also
        // fired a bullet down the middle would be two weapons wearing one model.
        if (type.Projectile.Length > 0)
        {
            if (effectsLive)
                _arms.Launch(in type, type.Projectile, type.LaunchSpeed, type.Thrown);
            return;
        }

        if (effectsLive)
            _arms.FireBullets(in type, shots * Math.Max(1, type.Pellets), type.Spread);
    }

    /// <summary>
    /// The other trigger.
    ///
    /// Not a second copy of <see cref="PrimaryAttack"/> with different numbers,
    /// and the difference is the catch-up loop: an alt-fire is a single
    /// deliberate press — a grenade, both barrels — and owing several of those
    /// on a slow frame would empty the pool in one tick rather than keep a
    /// rate honest.
    /// </summary>
    public void SecondaryAttack(WeaponNetState w, ViewModelNetState vm,
                                in WeaponType type, float now, bool effectsLive)
    {
        if (type.Secondary is not { } secondary)
            return;

        // Charged before anything happens, so a press that cannot be paid for
        // still costs the interval. Otherwise an empty alt-fire is checked
        // every single frame the button is held.
        w.NextSecondaryAttack = now + secondary.Interval;
        w.TimeWeaponIdle = now + IdleSettle;

        if (!SpendSecondary(w, in secondary))
        {
            if (effectsLive)
                _arms.Sound(type.EmptySound);
            return;
        }

        SendAnim(vm, WeaponAnim.Primary, 0f);

        if (effectsLive)
        {
            _arms.Sound(secondary.Sound.Length > 0 ? secondary.Sound : type.FireSound);

            if (secondary.Projectile.Length > 0)
                _arms.Launch(in type, secondary.Projectile, secondary.LaunchSpeed, secondary.Thrown);
            else
                _arms.FireBullets(in type, Math.Max(1, secondary.Pellets), secondary.Spread);
        }
    }

    /// <summary>
    /// Pays for one alt-fire, from whichever pool it draws on. False when it
    /// cannot be paid for, in which case nothing has been spent.
    /// </summary>
    private static bool SpendSecondary(WeaponNetState w, in SecondaryFire secondary)
    {
        if (secondary.FromClip)
        {
            // Checked before spending, because a shotgun with one shell left
            // must not fire half a double blast and lose the shell.
            if (w.Clip < secondary.AmmoCost)
                return false;

            w.Clip -= secondary.AmmoCost;
            return true;
        }

        if (w.SecondaryReserve < secondary.AmmoCost)
            return false;

        w.SecondaryReserve -= secondary.AmmoCost;
        return true;
    }

    /// <summary>
    /// Starts a reload. It finishes later, in <see cref="FinishReloadIfDue"/>,
    /// which is what gives it a duration rather than making it instantaneous.
    /// </summary>
    public void Reload(WeaponNetState w, ViewModelNetState vm,
                       in WeaponType type, float now, bool effectsLive)
    {
        if (type.ClipSize <= 0 || w.InReload)
            return;

        // Nothing to load, or nowhere to put it.
        if (w.Reserve <= 0 || w.Clip >= type.ClipSize)
            return;

        SendAnim(vm, WeaponAnim.Reload, 0f);

        if (effectsLive)
            _arms.Sound(type.ReloadSound);

        w.InReload = true;

        // The duration is the animation's, read from the model rather than
        // declared, so it cannot drift from the view model the table names. A
        // model with no reload animation still reloads, just immediately.
        float duration = _arms.AnimDuration(WeaponAnim.Reload);

        w.NextPrimaryAttack = now + duration;
        w.TimeWeaponIdle    = now + duration;
    }

    /// <summary>Moves the ammunition once the reload animation has run its course.</summary>
    private static void FinishReloadIfDue(WeaponNetState w, in WeaponType type, float now)
    {
        if (w.NextPrimaryAttack > now)
            return;

        // What the reserve HAS rather than what the clip WANTS: the last eleven
        // rounds should load as eleven, not as nothing.
        int wanted = type.ClipSize - w.Clip;
        int loaded = Math.Min(wanted, w.Reserve);

        w.Reserve -= loaded;
        w.Clip    += loaded;
        w.InReload = false;
    }

    /// <summary>
    /// Whether there is anything to fire. A melee weapon is never empty, which
    /// is what stops a crowbar clicking.
    /// </summary>
    private static bool IsEmpty(WeaponNetState w, in WeaponType type)
    {
        if (type.IsMelee)
            return false;

        return type.ClipSize > 0 ? w.Clip <= 0 : w.Reserve <= 0;
    }

    /// <summary>
    /// The click of an empty gun, and a short delay so it clicks at a rate
    /// rather than every tick.
    /// </summary>
    private void HandleFireOnEmpty(WeaponNetState w, in WeaponType type,
                                   float now, bool effectsLive)
    {
        if (effectsLive)
            _arms.Sound(type.EmptySound);

        w.NextPrimaryAttack = now + EmptyClickInterval;

        // An empty gun reloads itself, which is what stock does through
        // ReloadOrSwitchWeapons and is the difference between a gun that has
        // run out and a gun that is broken.
        if (type.ClipSize > 0 && w.Reserve > 0)
        {
            w.InReload = true;
            float duration = _arms.AnimDuration(WeaponAnim.Reload);
            w.NextPrimaryAttack = now + Math.Max(duration, EmptyClickInterval);
        }
    }

    /// <summary>
    /// Points the viewmodel at a sequence. The parity bump is what restarts an
    /// animation the model is already in — stock's m_nAnimationParity, kept for
    /// stock's reason: without it the second shot of a pair is invisible.
    /// </summary>
    private void SendAnim(ViewModelNetState vm, WeaponAnim anim, float rate)
    {
        int sequence = _arms.LookupSequence(anim);

        if (sequence < 0)
            return;

        vm.Sequence        = sequence;
        vm.AnimationParity = SourceSharp.Shared.AnimationParity.Next(vm.AnimationParity);
        vm.PlaybackRate    = rate > 0f ? rate : 1f;
    }

    private const float EmptyClickInterval = 0.5f;

    /// <summary>Seconds after a shot before the idle animation is due.</summary>
    private const float IdleSettle = 1.0f;

    /// <summary>How often the idle is re-sent while nothing happens.</summary>
    private const float IdleRepeat = 5.0f;

    //-------------------------------------------------------------------------
    // Launch geometry, separated so it can be tested without a game.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Which way and how fast a projectile leaves.
    ///
    /// Pulled out as a static taking the aiming basis so it can be tested: the
    /// engine-backed alternative reads a player's eyes, which answers zero
    /// outside a game and would make every assertion here pass for the wrong
    /// reason.
    /// </summary>
    public static Vector LaunchVelocity(Angles.Basis aim, float launchSpeed, bool isThrown)
    {
        // A throw is lofted, because a grenade thrown flat at the floor is a
        // grenade dropped at your own feet. An aimed shot is not, because a
        // bolt that landed below the crosshair is a broken crossbow.
        Vector direction = isThrown
            ? (aim.Forward + aim.Up * LaunchLoft).Normalized
            : aim.Forward;

        return direction * launchSpeed;
    }

    /// <summary>How far ahead of the eye a projectile appears.</summary>
    public const float LaunchOffset = 24f;

    /// <summary>How much of the throw goes upward. A lob, not a bowl.</summary>
    public const float LaunchLoft = 0.25f;

    //-------------------------------------------------------------------------
    // The arithmetic, separated so it can be tested without a game.
    //-------------------------------------------------------------------------

    /// <summary>
    /// How many shots a trigger pull owes: one per fire-rate interval between
    /// the last shot and now.
    ///
    /// This is what keeps rate of fire independent of framerate — on a slow
    /// frame a fast weapon owes more than one — and it is bounded, because a
    /// weapon whose clock is far in the past would otherwise owe thousands.
    /// </summary>
    public static int ShotsOwed(float nextAttack, float now, float interval)
    {
        if (interval <= 0f)
            return 1;

        if (nextAttack > now)
            return 0;

        int owed = (int)((now - nextAttack) / interval) + 1;

        return Math.Clamp(owed, 1, MaxShotsPerFrame);
    }

    /// <summary>
    /// When the weapon may fire next, advanced by whole intervals from the last
    /// deadline rather than set from now — which is what stops a fast weapon
    /// losing fractions of an interval every frame and firing slower than it
    /// should.
    /// </summary>
    public static float NextAttackAfter(float nextAttack, float now, float interval)
    {
        if (interval <= 0f)
            return now;

        float at = nextAttack;

        for (int i = 0; i < MaxShotsPerFrame && at <= now; i++)
            at += interval;

        // A clock so far behind that whole intervals cannot catch up is reset,
        // rather than left to owe shots forever.
        return at <= now ? now + interval : at;
    }

    /// <summary>
    /// A ceiling on the catch-up loop. A weapon deployed but never fired has a
    /// deadline of zero, and without this it would owe one shot per interval
    /// since the map loaded.
    /// </summary>
    public const int MaxShotsPerFrame = 8;
}
