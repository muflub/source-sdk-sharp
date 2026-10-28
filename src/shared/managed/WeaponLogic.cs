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

    /// <summary>
    /// A sound only the predicting client plays: stock's WeaponSound under
    /// <c>#ifdef CLIENT_DLL</c> (DefaultReload's). The server's arms play nothing.
    /// </summary>
    void ClientSound(string soundScript)
    {
    }

    /// <summary>
    /// A sound only the server plays: stock's from a server-only path
    /// (Operator_HandleAnimEvent's). The client's arms play nothing.
    /// </summary>
    void ServerSound(string soundScript) => Sound(soundScript);

    /// <summary>Hitscan fire: trace, damage, impact — the ammo row says how hard.</summary>
    void FireBullets(in WeaponType type, int pellets, float spreadDegrees);

    /// <summary>One melee swing: a short trace, the row's own damage.</summary>
    void MeleeSwing(in WeaponType type);

    /// <summary>Sends what the weapon launches instead of a bullet.</summary>
    void Launch(in WeaponType type, string projectile, float speed, bool thrown);

    /// <summary>
    /// Launches with a velocity the CALLER worked out and a fuse the weapon
    /// declares, and reports the launch (entity, launcher, velocity, fuse) so
    /// the owning side can attribute the shot and pass the fuse on.
    ///
    /// <para>
    /// <see cref="Launch"/> beside it is the STOCK path and stays untouched: it
    /// derives the velocity itself from a speed and the thrown/aimed rule, and
    /// the module attributes the throw after the fact. This one is the arms a
    /// projectile mod needs and the stock set lacks — a behaviour that wants
    /// its own launch vector (a lobbed arc it computed, a gun-specific muzzle
    /// speed) and its own fuse has no other door, and a mod that had to spawn
    /// the projectile itself would have to know the launcher's eye position,
    /// which is the arms' business, not the mod's.
    /// </para>
    /// </summary>
    /// <param name="type">The row launching it; its <see cref="WeaponType.Thrown"/>
    /// still decides whether the velocity is an impulse or a set velocity.</param>
    /// <param name="projectile">The projectile's classname.</param>
    /// <param name="velocity">The velocity it leaves with, unmodified.</param>
    /// <param name="fuse">
    /// Seconds until what it launched goes off — carried to the projectile by
    /// the same report that attributes the shot, because a fuse belongs to the
    /// gun that fired it and not to the entity class that flies.
    /// </param>
    void LaunchWith(in WeaponType type, string projectile, Vector velocity, float fuse)
    {
        // Nothing by default, so the client's replay arms and a test's
        // recording arms inherit the behaviour they already had: a launch on
        // the replay side is a no-op whatever door it comes through.
    }

    //-------------------------------------------------------------------------
    // THE SLAM'S HANDS (CWeapon_SLAM, weapon_slam.cpp, run by SlamMachine).
    //
    // Default bodies, so the arms that never carry a SLAM — a test's recording
    // arms, a mod's — inherit "no such animation, nothing in front of me, and
    // nothing placed". The first three write PREDICTED state and are answered
    // on both sides; the rest make or set off entities and are the server's,
    // called only while effects are live.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The sequence tagged with an activity IN THE CURRENT VIEWMODEL, or -1 —
    /// stock's <c>SelectWeightedSequence</c>, for a weapon that names its
    /// animations by activity rather than by <see cref="WeaponAnim"/> kind.
    /// </summary>
    int LookupActivity(string activity) => -1;

    /// <summary>How long a viewmodel sequence runs; 0 for none.</summary>
    float SequenceDuration(int sequence) => 0f;

    /// <summary>
    /// What is within <paramref name="reach"/> of the carrier's eye along their
    /// aim, through <c>MASK_SOLID</c> — the trace <c>CanAttachSLAM</c> and the
    /// two attaches make.
    /// </summary>
    WallProbe ProbeWall(float reach) => WallProbe.Nothing;

    /// <summary><c>CWeapon_SLAM::SatchelThrow</c>'s spawn: a satchel thrown from the carrier.</summary>
    void ThrowCharge(in WeaponType type) { }

    /// <summary><c>CWeapon_SLAM::SatchelAttach</c>'s spawn: a satchel stuck to the wall ahead.</summary>
    void AttachCharge(in WeaponType type) { }

    /// <summary><c>CWeapon_SLAM::TripmineAttach</c>'s spawn: a tripmine planted on the wall ahead.</summary>
    void AttachTripmine(in WeaponType type) { }

    /// <summary><c>CWeapon_SLAM::SatchelDetonate</c>: every live satchel this carrier threw goes off.</summary>
    void DetonateCharges() { }

    /// <summary><c>AnyUndetonatedCharges</c>: whether a satchel this carrier threw is still live.</summary>
    bool ChargesOut() => false;

    /// <summary>
    /// CWeaponFrag's ThrowGrenade, LobGrenade or RollGrenade, server half: a
    /// frag grenade from where and at the velocity stock's throw puts it,
    /// with its timer set. Nothing by default, as <see cref="LaunchWith"/>:
    /// the client's replay throws nothing.
    /// </summary>
    void ThrowGrenade(in WeaponType type, GrenadeThrow kind, float timer)
    {
    }

    //-------------------------------------------------------------------------
    // THE RPG'S HANDS (CWeaponRPG, shared/hl2mp/weapon_rpg.cpp). What stock
    // does only #ifndef CLIENT_DLL: nothing by default, which is what the
    // client's replay and a test's arms do.
    //-------------------------------------------------------------------------

    /// <summary>
    /// CWeaponRPG::PrimaryAttack's server half: CMissile::Create at the muzzle
    /// (twelve ahead, six right, three down of the shoot position), facing the
    /// aim, owned by the carrier, with its grace period when the first 128
    /// units ahead of the eye are clear.
    /// </summary>
    void LaunchMissile(in WeaponType type) { }

    /// <summary>
    /// The laser dot the RPG owns, made to match: none when
    /// <paramref name="exists"/> is false (StopGuiding's UTIL_Remove), else one
    /// (CreateLaserPointer's CLaserDot::Create) switched
    /// <paramref name="on"/> or off and moved to where the carrier's aim ends
    /// (UpdateLaserPosition). Idempotent: it is asked every command.
    /// </summary>
    void UpdateLaserDot(bool exists, bool on) { }

    /// <summary>
    /// CBasePlayer::SetFOV( weapon, fov, rate ) for the carrier: a zoom the
    /// weapon asks for (0 gives the view back). The server's alone, as stock's
    /// is; the client's replay asks nothing.
    /// </summary>
    void SetFov(int fov, float rate)
    {
    }

    /// <summary>
    /// The named animation's sequence index IN THE CURRENT VIEWMODEL, or -1.
    /// Both sides answer from the same model, which is what lets the sequence
    /// number be predicted state.
    /// </summary>
    int LookupSequence(WeaponAnim anim);

    /// <summary>How long that animation runs, which is how long a reload takes.</summary>
    float AnimDuration(WeaponAnim anim);
}

/// <summary>How a frag grenade leaves the hand: CWeaponFrag's three throws.</summary>
public enum GrenadeThrow
{
    /// <summary>ThrowGrenade: overhand, from the primary.</summary>
    Throw,

    /// <summary>LobGrenade: underhand, from the secondary standing.</summary>
    Lob,

    /// <summary>RollGrenade: along the floor, from the secondary crouched.</summary>
    Roll,
}

/// <summary>What <see cref="IWeaponArms.ProbeWall"/> found ahead of the carrier.</summary>
public enum WallProbe
{
    /// <summary>The trace ran its whole length.</summary>
    Nothing = 0,

    /// <summary>It stopped on something that is not alive: a wall, a crate.</summary>
    Surface = 1,

    /// <summary>
    /// It stopped on a combat character, which <c>CanAttachSLAM</c> refuses —
    /// "Don't attach to a living creature" — and the attaches do not.
    /// </summary>
    Character = 2,
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
        WeaponAnim.Secondary    => new[] { "altfire", "fire2", "secondaryattack" },
        WeaponAnim.DryFire      => new[] { "dryfire" },
        WeaponAnim.Pump         => new[] { "pump" },
        WeaponAnim.ReloadStart  => new[] { "reload1" },
        WeaponAnim.ReloadFinish => new[] { "reload3" },
        WeaponAnim.PullbackHigh => new[] { "drawbackhigh" },
        WeaponAnim.PullbackLow  => new[] { "drawbacklow" },
        WeaponAnim.Throw        => new[] { "throw" },
        WeaponAnim.Haulback     => new[] { "lob" },
        WeaponAnim.Fidget       => new[] { "idle_empty" },
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
        WeaponAnim.Secondary    => new[] { "ACT_VM_SECONDARYATTACK" },
        WeaponAnim.DryFire      => new[] { "ACT_VM_DRYFIRE" },
        WeaponAnim.Pump         => new[] { "ACT_SHOTGUN_PUMP" },
        WeaponAnim.ReloadStart  => new[] { "ACT_SHOTGUN_RELOAD_START" },
        WeaponAnim.ReloadFinish => new[] { "ACT_SHOTGUN_RELOAD_FINISH" },
        WeaponAnim.PullbackHigh => new[] { "ACT_VM_PULLBACK_HIGH" },
        WeaponAnim.PullbackLow  => new[] { "ACT_VM_PULLBACK_LOW" },
        WeaponAnim.Throw        => new[] { "ACT_VM_THROW" },
        WeaponAnim.Haulback     => new[] { "ACT_VM_HAULBACK" },
        WeaponAnim.Fidget       => new[] { "ACT_VM_FIDGET" },
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
    private readonly WeaponCatalog _catalog;
    private readonly WeaponBehaviours _behaviours;

    /// <summary>
    /// The machine for one player, on one side.
    /// </summary>
    /// <param name="arms">This side's hands — the effects and the sequence lookups.</param>
    /// <param name="catalog">
    /// The side's weapon table, which is what turns the networked
    /// <see cref="WeaponNetState.WeaponTypeId"/> into a row. Injected rather
    /// than read from a static because the table now carries the mod's rows as
    /// well, and the two sides must be reading the SAME list to agree about an
    /// index at all.
    /// </param>
    /// <param name="behaviours">
    /// The side's behaviour table — what a row's
    /// <see cref="WeaponType.Behaviour"/> key resolves to. Both sides must
    /// resolve one key to one behaviour or prediction diverges, which is why
    /// this is asked once per command and passed down to the attack points.
    /// </param>
    public WeaponLogic(IWeaponArms arms,
                       WeaponCatalog? catalog = null, WeaponBehaviours? behaviours = null)
    {
        _arms = arms;
        // Null takes the stock tables: the core rows and no behaviours, which
        // is exactly what a test of the stock machine wants, and what a side
        // with no mod assemblies would build anyway.
        _catalog = catalog ?? WeaponCatalog.Stock();
        _behaviours = behaviours ?? WeaponBehaviours.Stock();
        _slam = new SlamMachine(arms);
    }

    private readonly SlamMachine _slam;

    /// <summary>
    /// A weapon's own draw, for a row whose machine picks it: the SLAM's
    /// <c>Deploy</c>, which chooses among six draws by what it is facing and
    /// whether a satchel is out. False for every other row, whose draw is the
    /// generic one the caller sends. Run by the server's switch, which is not
    /// a predicted command.
    /// </summary>
    public bool Deploy(WeaponNetState w, ViewModelNetState vm, float now)
    {
        if (_catalog.ByIndex(w.WeaponTypeId) is not WeaponType type || type.Kind != WeaponKind.Slam)
            return false;

        _slam.Deploy(w, vm, now, in type);
        return true;
    }

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
    /// <param name="behaviour">
    /// The row's behaviour, resolved ONCE per command by the side's
    /// <see cref="WeaponBehaviours"/> — so the server's run and the client's
    /// replay of one command ask the same key and get the same answer. Null
    /// (the overwhelmingly common case: a row that names no behaviour) leaves
    /// every decision below with its stock answer.
    /// </param>
    public void RunCommand(WeaponNetState w, ViewModelNetState vm,
                           int buttons, float now, bool effectsLive)
    {
        if (_catalog.ByIndex(w.WeaponTypeId) is not WeaponType type)
            return;

        var behaviour = _behaviours.For(type.Behaviour);

        // A weapon whose stock class replaces CBaseCombatWeapon::ItemPostFrame
        // outright (the shotgun) runs its own command.
        if (behaviour is not null)
        {
            int pressed = buttons & ~w.LastButtons;
            if (behaviour.RunCommand(Context(in type, w, vm, now, effectsLive), buttons, pressed))
            {
                w.LastButtons = buttons;
                return;
            }
        }

        // A weapon with a machine of its own runs it instead of the generic
        // step: the SLAM's ItemPostFrame shares only the clocks with it.
        if (type.Kind == WeaponKind.Slam)
            _slam.RunCommand(w, vm, buttons, now, effectsLive, in type);
        else
            Step(w, vm, buttons, now, effectsLive, in type, behaviour);

        // The tail, run for EVERY command whatever the weapon did in it —
        // firing, reloading or standing idle — as stock's ItemPostFrame was.
        // Not inside the arms below, because a mod's per-frame bookkeeping that
        // lived in the fire path would quietly stop during a reload.
        behaviour?.ItemPostFrame(Context(in type, w, vm, now, effectsLive));
    }

    /// <summary>
    /// The command itself: what the weapon was asked for and what it did about
    /// it. Split from <see cref="RunCommand"/> only so the post-frame tail runs
    /// for the arms that <c>return</c> early, and left alone otherwise — every
    /// rule below is stock's.
    /// </summary>
    private void Step(WeaponNetState w, ViewModelNetState vm,
                      int buttons, float now, bool effectsLive,
                      in WeaponType type, IWeaponBehaviour? behaviour)
    {
        int pressed = buttons & ~w.LastButtons;
        w.LastButtons = buttons;

        int clipSize = ClipSize(behaviour, in type);
        var held = (InputButtons)buttons;

        // Reloading is checked before firing, because a reload in progress is
        // what decides whether firing is possible at all.
        if (clipSize > 0 && w.InReload)
        {
            FinishReloadIfDue(w, in type, now, clipSize);
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
            SecondaryAttack(w, vm, in type, now, effectsLive, behaviour);
            return;
        }

        if (held.HasFlag(InputButtons.Attack) && w.NextPrimaryAttack <= now)
        {
            if (IsEmpty(w, in type, clipSize))
            {
                HandleFireOnEmpty(w, in type, now, effectsLive, clipSize);
                return;
            }

            // The leading edge resets the clock. Without it, a player who holds
            // the trigger through a lull gets a burst of owed shots the moment
            // they press it again — the catch-up loop would fire one for every
            // interval since they last shot.
            if (((InputButtons)pressed).HasFlag(InputButtons.Attack))
                w.NextPrimaryAttack = now;

            PrimaryAttack(w, vm, in type, now, effectsLive, behaviour);
            return;
        }

        // Reload on request, or because the clip ran dry and nothing is held.
        bool wantsReload = held.HasFlag(InputButtons.Reload)
                        || (buttons == 0 && clipSize > 0 && w.Clip <= 0);

        if (wantsReload && clipSize > 0 && w.NextPrimaryAttack <= now)
        {
            Reload(w, vm, in type, now, effectsLive, behaviour);
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

    /// <summary>The context one decision point is asked about.</summary>
    private WeaponAttackContext Context(in WeaponType type, WeaponNetState w, ViewModelNetState vm,
                                        float now, bool effectsLive)
        => new(type, w, vm, now, effectsLive, _arms);

    /// <summary>
    /// The fire rate this machine uses for a row: the row's own, unless a
    /// behaviour has an opinion. Deterministic by rule — both sides ask one key
    /// and must get the same number, or the catch-up loop mispredicts.
    /// </summary>
    private float Interval(IWeaponBehaviour? behaviour, in WeaponType type)
        => behaviour is null ? type.Interval : behaviour.ModifyFireInterval(in type, type.Interval);

    /// <summary>
    /// The clip this machine uses for a row: the row's own, unless a behaviour
    /// has an opinion. Zero or less is the row's own "no clip".
    /// </summary>
    private int ClipSize(IWeaponBehaviour? behaviour, in WeaponType type)
        => behaviour is null ? type.ClipSize : behaviour.ModifyClipSize(in type, type.ClipSize);

    /// <summary>
    /// One pull of the trigger.
    ///
    /// The shape is stock's: a catch-up loop that owes one shot per fire-rate
    /// interval since the last, clamped to what is in the clip. That loop is
    /// what keeps rate of fire independent of framerate, and dropping it would
    /// make every automatic weapon slower on a busy server.
    /// </summary>
    /// <param name="behaviour">The row's resolved behaviour, or null for stock.</param>
    public void PrimaryAttack(WeaponNetState w, ViewModelNetState vm,
                              in WeaponType type, float now, bool effectsLive,
                              IWeaponBehaviour? behaviour = null)
    {
        // The veto, asked BEFORE anything is spent or moved: a behaviour that
        // says no pays for nothing, spends no round and leaves no animation.
        if (behaviour is not null && !behaviour.BeforePrimaryAttack(Context(in type, w, vm, now, effectsLive)))
            return;

        // An empty clip reloads instead of firing, which is what makes a reload
        // happen at all without anyone pressing a key for it.
        int clipSize = ClipSize(behaviour, in type);

        if (clipSize > 0 && w.Clip <= 0)
        {
            Reload(w, vm, in type, now, effectsLive, behaviour);
            return;
        }

        float interval = Interval(behaviour, in type);

        int shots = ShotsOwed(w.NextPrimaryAttack, now, interval);

        w.NextPrimaryAttack = NextAttackAfter(w.NextPrimaryAttack, now, interval);
        w.TimeWeaponIdle = now + IdleSettle;

        // A round is a round whatever it is loaded with, so the clip is spent
        // per SHOT and the pellets are multiplied afterwards. A shotgun that
        // took seven rounds per pull would empty in one.
        if (clipSize > 0)
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
        }
        // A weapon either throws or shoots, never both. A launcher that also
        // fired a bullet down the middle would be two weapons wearing one model.
        else if (type.Projectile.Length > 0)
        {
            if (effectsLive)
                _arms.Launch(in type, type.Projectile, type.LaunchSpeed, type.Thrown);
        }
        else if (effectsLive)
        {
            _arms.FireBullets(in type, shots * Math.Max(1, type.Pellets), type.Spread);
        }

        // The tail, after the stock shot has been spent and the arms have fired.
        behaviour?.PrimaryAttack(Context(in type, w, vm, now, effectsLive));
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
    /// <param name="behaviour">The row's resolved behaviour, or null for stock.</param>
    public void SecondaryAttack(WeaponNetState w, ViewModelNetState vm,
                                in WeaponType type, float now, bool effectsLive,
                                IWeaponBehaviour? behaviour = null)
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

        // The tail, once the alt-fire has been paid for and the arms have fired.
        behaviour?.SecondaryAttack(Context(in type, w, vm, now, effectsLive));
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
    /// <param name="behaviour">The row's resolved behaviour, or null for stock.</param>
    public void Reload(WeaponNetState w, ViewModelNetState vm,
                       in WeaponType type, float now, bool effectsLive,
                       IWeaponBehaviour? behaviour = null)
    {
        int clipSize = ClipSize(behaviour, in type);

        if (clipSize <= 0 || w.InReload)
            return;

        // Nothing to load, or nowhere to put it.
        if (w.Reserve <= 0 || w.Clip >= clipSize)
            return;

        SendAnim(vm, WeaponAnim.Reload, 0f);

        // DefaultReload's WeaponSound( RELOAD ), which is the client's alone.
        if (effectsLive)
            _arms.ClientSound(type.ReloadSound);

        w.InReload = true;

        // The duration is the animation's, read from the model rather than
        // declared, so it cannot drift from the view model the table names. A
        // model with no reload animation still reloads, just immediately.
        float duration = _arms.AnimDuration(WeaponAnim.Reload);

        w.NextPrimaryAttack = now + duration;
        w.TimeWeaponIdle    = now + duration;

        behaviour?.Reload(Context(in type, w, vm, now, effectsLive));
    }

    /// <summary>
    /// Moves the ammunition once the reload animation has run its course.
    /// <paramref name="clipSize"/> is what the machine decided the clip holds,
    /// which may be a behaviour's answer rather than the row's.
    /// </summary>
    private static void FinishReloadIfDue(WeaponNetState w, in WeaponType type, float now, int clipSize)
    {
        if (w.NextPrimaryAttack > now)
            return;

        // What the reserve HAS rather than what the clip WANTS: the last eleven
        // rounds should load as eleven, not as nothing.
        int wanted = clipSize - w.Clip;
        int loaded = Math.Min(wanted, w.Reserve);

        w.Reserve -= loaded;
        w.Clip    += loaded;
        w.InReload = false;
    }

    /// <summary>
    /// Whether there is anything to fire. A melee weapon is never empty, which
    /// is what stops a crowbar clicking.
    /// </summary>
    private static bool IsEmpty(WeaponNetState w, in WeaponType type, int clipSize)
    {
        if (type.IsMelee)
            return false;

        return clipSize > 0 ? w.Clip <= 0 : w.Reserve <= 0;
    }

    /// <summary>
    /// The click of an empty gun, and a short delay so it clicks at a rate
    /// rather than every tick.
    /// </summary>
    private void HandleFireOnEmpty(WeaponNetState w, in WeaponType type,
                                   float now, bool effectsLive, int clipSize)
    {
        if (effectsLive)
            _arms.Sound(type.EmptySound);

        w.NextPrimaryAttack = now + EmptyClickInterval;

        // An empty gun reloads itself, which is what stock does through
        // ReloadOrSwitchWeapons and is the difference between a gun that has
        // run out and a gun that is broken.
        if (clipSize > 0 && w.Reserve > 0)
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
    private void SendAnim(ViewModelNetState vm, WeaponAnim anim, float rate) => Play(_arms, vm, anim, rate);

    /// <summary>
    /// Stock's SendWeaponAnim for a behaviour: the sequence, the parity bump
    /// and the rate, exactly as the machine sends its own. False when the
    /// model has no such animation.
    /// </summary>
    public static bool Play(IWeaponArms arms, ViewModelNetState vm, WeaponAnim anim, float rate = 0f)
    {
        int sequence = arms.LookupSequence(anim);

        if (sequence < 0)
            return false;

        vm.Sequence        = sequence;
        vm.AnimationParity = SourceSharp.Shared.AnimationParity.Next(vm.AnimationParity);
        vm.PlaybackRate    = rate > 0f ? rate : 1f;
        return true;
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
