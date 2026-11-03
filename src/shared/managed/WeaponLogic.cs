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
    /// The carrier's state, whose punch a shot kicks: the server's player, the
    /// predicting client's working copy. Null where there is no carrier to kick.
    /// </summary>
    PlayerNetState? Carrier => null;

    /// <summary>
    /// The .357's jolt to the aim itself (<c>SetViewAngles</c> under <c>CLIENT_DLL</c>, first
    /// prediction only). The server's arms do nothing: the view is the client's.
    /// </summary>
    void DisorientView()
    {
    }

    /// <summary>A shot changed <see cref="Carrier"/>'s punch: the side keeps it where its next command reads it.</summary>
    void PunchChanged(PlayerNetState carrier)
    {
    }

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

    /// <summary>
    /// Hitscan fire at a damage the CALLER decided: <paramref name="shot"/>'s
    /// damage per bullet in place of the ammo row's, and its crit flag carried
    /// to the victim beside the damage call (the server's
    /// <c>DamageHitGroups</c> scope, read into the rules' <c>DamageContext</c>).
    /// The ammo row still names the damage type and the force.
    ///
    /// <para>
    /// What a crit is worth is the caller's rule, not the arms': stock TF
    /// triples the damage and sets <c>DMG_CRITICAL</c> in its own weapon code,
    /// and the core carries the number and the flag it is handed, unchanged.
    /// </para>
    ///
    /// <para>
    /// The default ignores the shot and fires the stock arm, so the client's
    /// replay arms and a test's recording arms keep the behaviour they had: on
    /// the replay side a bullet is a no-op whichever door it comes through.
    /// </para>
    /// </summary>
    void FireBullets(in WeaponType type, int pellets, float spreadDegrees, ShotDamage shot)
        => FireBullets(in type, pellets, spreadDegrees);

    /// <summary>
    /// Hitscan fire whose pellets leave in <paramref name="fixedPellets"/>'s
    /// pattern instead of the random sample — the behaviour's fixed spread,
    /// <c>tf_use_fixed_weaponspreads</c> (stock's <c>bFixedSpread</c>,
    /// tf_fx_shared.cpp:302). Null is the random sample; the pattern is asked
    /// once per attack and its index is the pellet's in the volley.
    /// The default drops the pattern, so the arms that never heard of one keep
    /// the behaviour they had.
    /// </summary>
    void FireBullets(in WeaponType type, int pellets, float spreadDegrees, ShotDamage shot,
                     PelletPattern? fixedPellets)
        => FireBullets(in type, pellets, spreadDegrees, shot);

    /// <summary>One melee swing: a short trace, the row's own damage.</summary>
    void MeleeSwing(in WeaponType type);

    /// <summary>
    /// Whether a swing now would meet something: CBaseHL2MPBludgeonWeapon::
    /// Swing's aim, the ray and then the hull for a near miss. Asked on every
    /// pass, the replay's too, because it picks the animation (a hit's
    /// ACT_VM_HITCENTER or a miss's ACT_VM_MISSCENTER) and the animation is
    /// predicted.
    /// </summary>
    bool MeleeConnects(in WeaponType type) => true;

    /// <summary>
    /// A swing met nothing: Swing's miss tail, ImpactWater's splash where the
    /// swing went into water. The server's; nothing on the client's replay.
    /// </summary>
    void MeleeMiss(in WeaponType type) { }

    /// <summary>
    /// The weapon in the hands has nothing left to fire: stock's
    /// <c>SwitchToNextBestWeapon</c> for the carrier (CMultiplayRules::
    /// GetNextBestWeapon by weight). True when the hands will take another,
    /// which happens once this command has run. The server's; the client's
    /// replay never switches.
    /// </summary>
    bool SwitchToNextBest() => false;

    /// <summary>
    /// The carrier's eyes are under water or slime (GetWaterLevel() == 3):
    /// the crossbow fires slower there. The server's; the replay launches
    /// nothing, so it is never asked.
    /// </summary>
    bool EyesUnderwater() => false;

    /// <summary>
    /// Where this command's aims look from and along — the eye the prediction
    /// ran the movement to and the command's own angles, the pair a trace
    /// needs to see what the server's run of the command saw (the medigun's
    /// beam retarget). False with nothing set: no eye to aim from.
    /// </summary>
    bool Aim(out Vector eye, out Vector forward)
    {
        eye = default;
        forward = default;
        return false;
    }
    /// One melee swing at <paramref name="shot"/>'s damage, its crit flag
    /// carried to the victim as <see cref="FireBullets(in WeaponType, int, float, ShotDamage)"/>
    /// carries a bullet's. The default fires the stock swing, for the same
    /// reason.
    /// </summary>
    void MeleeSwing(in WeaponType type, ShotDamage shot) => MeleeSwing(in type);

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

    //-------------------------------------------------------------------------
    // THE GUN'S FLASH AND BRASS (the firing effects of a predicted shot. Stock
    // TF arms these client-side — CreateMuzzleFlashEffects and OnFireEvent's
    // brass — under its first-prediction rules, and the server has nothing to
    // present, so both default to nothing, as the RPG's hands above do: what
    // a client presents is none of the machine's or the server's business.)
    //-------------------------------------------------------------------------

    /// <summary>
    /// The muzzle flash of a shot: the script's muzzle-flash particle effect,
    /// to be attached at the hands' muzzle. Only the name travels — where it
    /// attaches is the presentation's, as stock's <c>DispatchParticleEffect</c>
    /// leaves it to the entity and its attachment name.
    /// </summary>
    void MuzzleFlash(in WeaponType type, string particleEffect) { }

    /// <summary>
    /// The shell a fired gun ejects, from the viewmodel's ejector. The stock
    /// effect's data carries the item and the gun with it (stock's
    /// <c>OnFireEvent</c> for <c>"TF_EjectBrass"</c>):
    /// <paramref name="itemDefIndex"/> the carried item's definition index, 0
    /// for none, and <paramref name="weaponId"/> the weapon's own id.
    /// </summary>
    void ShellEject(in WeaponType type, int itemDefIndex, int weaponId) { }

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
/// One shot's damage as the weapon decided it: the damage each bullet (or the
/// swing) deals, and whether the shot is a critical hit.
///
/// <para>
/// The machine's stock answer is the row's own numbers — the ammo row's
/// <see cref="AmmoType.Damage"/> for a bullet, <see cref="WeaponType.MeleeDamage"/>
/// for a swing — and not critical; a behaviour changes it in
/// <see cref="IWeaponBehaviour.ModifyShot"/>. The flag is only carried: the
/// core never multiplies by it, so a mod's crit rule is the whole of what a crit
/// does (stock TF: <c>DMG_CRITICAL</c> and three times the damage, decided in
/// <c>CTFWeaponBase</c>, not in the base weapon).
/// </para>
/// </summary>
/// <param name="Damage">Damage per bullet, or the swing's.</param>
/// <param name="Critical">Whether this shot is a critical hit.</param>
/// <param name="WeaponScriptDamage">
/// Whether the bullets deal the weapon script's damage instead, with the gib
/// rule: HL2MP's FireBullets (CHL2MP_Player::FireBullets' m_iPlayerDamage),
/// which the machine's stock shot asks for. A shot a behaviour decides leaves
/// it false and is dealt as it says.
/// </param>
public readonly record struct ShotDamage(float Damage, bool Critical = false, bool WeaponScriptDamage = false);

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
/// <summary>
/// CBaseHL2MPBludgeonWeapon::Swing's geometry, which the server's swing and the
/// client's predicted one both read.
/// </summary>
public static class BludgeonSwing
{
    /// <summary>BLUDGEON_HULL_DIM.</summary>
    public const float HullDim = 16f;

    /// <summary>The swing's hull, <see cref="HullDim"/> each way.</summary>
    public static Vector Hull => new(HullDim, HullDim, HullDim);

    /// <summary>Swing's "cuberoot of 2" backoff: 1.732 * BLUDGEON_HULL_DIM.</summary>
    public const float HullRadius = 1.732f * HullDim;

    /// <summary>Swing's "sort of facing the guy" threshold.</summary>
    public const float FacingDot = 0.70721f;
}

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
        WeaponAnim.Miss         => new[] { "misscenter1", "misscenter" },
        WeaponAnim.Prefire      => new[] { "prefire" },
        WeaponAnim.Postfire     => new[] { "postfire" },
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
        // After the shotgun's names, TF's own base activities: a singly reload
        // asks them (tf_weaponbase.cpp:1978/:2080) and the viewmodel act table
        // rewrites them per role, which the shotgun names reach no TF row of.
        WeaponAnim.ReloadStart  => new[] { "ACT_SHOTGUN_RELOAD_START", "ACT_RELOAD_START" },
        WeaponAnim.ReloadFinish => new[] { "ACT_SHOTGUN_RELOAD_FINISH", "ACT_RELOAD_FINISH" },
        WeaponAnim.PullbackHigh => new[] { "ACT_VM_PULLBACK_HIGH" },
        WeaponAnim.PullbackLow  => new[] { "ACT_VM_PULLBACK_LOW" },
        WeaponAnim.Throw        => new[] { "ACT_VM_THROW" },
        WeaponAnim.Haulback     => new[] { "ACT_VM_HAULBACK" },
        WeaponAnim.Fidget       => new[] { "ACT_VM_FIDGET" },
        WeaponAnim.Miss         => new[] { "ACT_VM_MISSCENTER" },
        WeaponAnim.Prefire      => new[] { "ACT_MP_ATTACK_STAND_PREFIRE" },
        WeaponAnim.Postfire     => new[] { "ACT_MP_ATTACK_STAND_POSTFIRE" },
        _                  => new[] { "ACT_VM_IDLE" },
    };

    /// <summary>
    /// THE resolution, whole: activities through the deterministic
    /// first-match lookup, then names, then -1. One method so the server's
    /// arms, the client's replay arms and the report cannot drift — they all
    /// answer through this.
    ///
    /// <para>
    /// Each activity name passes through <paramref name="viewmodel"/>'s chain
    /// first, so a mod can rename the ask before the model sees it (TF2's
    /// per-role arms; stock's TranslateViewmodelHandActivity). The NAME
    /// fallback below is not translated: it is the crutch for a model with
    /// untagged sequences, and a rule speaks activities. Null (a mod-less
    /// game, a test) asks the machine's own names, which is the HL2 answer.
    /// </para>
    /// </summary>
    public static int Resolve(IEngineEntityRender render, EntityHandle viewModel, WeaponAnim anim,
                              ViewmodelActivity? viewmodel = null, int playerIndex = 0)
    {
        foreach (string activity in ActivitiesFor(anim))
        {
            int sequence = render.LookupSequenceByActivity(viewModel,
                viewmodel?.Translate(playerIndex, anim, activity) ?? activity);

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
    /// <summary>The kicks' stream, re-seeded per shot from the command, so both sides draw alike.</summary>
    private readonly SeededUniformStream _kickRandom = new();

    private readonly IWeaponArms _arms;
    private readonly WeaponCatalog _catalog;
    private readonly WeaponBehaviours _behaviours;
    private readonly AmmoCatalog _ammo;

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
    /// <param name="ammo">
    /// The side's ammo table, which answers a bullet's stock damage (the
    /// ammo row's) for <see cref="ShotDamage"/> before a behaviour is asked.
    /// </param>
    public WeaponLogic(IWeaponArms arms,
                       WeaponCatalog? catalog = null, WeaponBehaviours? behaviours = null,
                       AmmoCatalog? ammo = null)
    {
        _arms = arms;
        // Null takes the stock tables: the core rows and no behaviours, which
        // is exactly what a test of the stock machine wants, and what a side
        // with no mod assemblies would build anyway.
        _catalog = catalog ?? WeaponCatalog.Stock();
        _behaviours = behaviours ?? WeaponBehaviours.Stock();
        _slam = new SlamMachine(arms);
        _ammo = ammo ?? AmmoCatalog.Stock();
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
    /// <param name="command">
    /// The command's seed and the weapon's and owner's indices, handed to
    /// every behaviour point in <see cref="WeaponAttackContext.Command"/>. The
    /// stock machine reads none of it; both sides derive it from the command
    /// (<see cref="WeaponCommand.For"/>), so a behaviour's roll agrees.
    /// </param>
    public void RunCommand(WeaponNetState w, ViewModelNetState vm,
                           int buttons, float now, bool effectsLive,
                           WeaponCommand command = default)
    {
        if (_catalog.ByIndex(w.WeaponTypeId) is not WeaponType type)
            return;

        var behaviour = _behaviours.For(type.Behaviour);

        behaviour?.ItemPreFrame(Context(in type, w, vm, now, effectsLive, command), buttons);

        // A weapon whose stock class replaces CBaseCombatWeapon::ItemPostFrame
        // outright (the shotgun) runs its own command.
        if (behaviour is not null)
        {
            int pressed = buttons & ~w.LastButtons;
            if (behaviour.RunCommand(Context(in type, w, vm, now, effectsLive, command), buttons, pressed))
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
            Step(w, vm, buttons, now, effectsLive, in type, behaviour, command);

        // The tail, run for EVERY command whatever the weapon did in it —
        // firing, reloading or standing idle — as stock's ItemPostFrame was.
        // Not inside the arms below, because a mod's per-frame bookkeeping that
        // lived in the fire path would quietly stop during a reload.
        behaviour?.ItemPostFrame(Context(in type, w, vm, now, effectsLive, command));
    }

    /// <summary>
    /// The command itself: what the weapon was asked for and what it did about
    /// it. Split from <see cref="RunCommand"/> only so the post-frame tail runs
    /// for the arms that <c>return</c> early, and left alone otherwise — every
    /// rule below is stock's.
    /// </summary>
    private void Step(WeaponNetState w, ViewModelNetState vm,
                      int buttons, float now, bool effectsLive,
                      in WeaponType type, IWeaponBehaviour? behaviour, WeaponCommand command)
    {
        int pressed = buttons & ~w.LastButtons;
        w.LastButtons = buttons;

        if (((InputButtons)buttons).HasFlag(InputButtons.Attack))
        {
            if (w.FireStartedAt <= 0f)
                w.FireStartedAt = now;
        }
        else
        {
            w.FireStartedAt = 0f;
        }

        int clipSize = ClipSize(behaviour, in type, command.OwnerIndex);
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
        if (held.HasFlag(InputButtons.Attack2) && w.NextSecondaryAttack <= now)
        {
            if (type.Secondary is not null)
            {
                SecondaryAttack(w, vm, in type, now, effectsLive, behaviour, command);
            }
            else
            {
                // A row with no ammunition secondary still reaches its
                // behaviour's secondary on the held trigger — stock's
                // ItemPostFrame calls SecondaryAttack() for every weapon the
                // same way, and the TF arms whose secondary is the class's
                // special skill (CTFWeaponBaseGun::SecondaryAttack,
                // tf_weaponbase_gun.cpp:234-251) live there. Nothing is spent
                // here; the behaviour arms its own refire.
                behaviour?.SecondaryAttack(Context(in type, w, vm, now, effectsLive, command));
            }

            return;
        }

        if (held.HasFlag(InputButtons.Attack) && w.NextPrimaryAttack <= now)
        {
            if (IsEmpty(w, in type, clipSize))
            {
                HandleFireOnEmpty(w, vm, in type, now, effectsLive, behaviour, command);
                return;
            }

            // The leading edge resets the clock. Without it, a player who holds
            // the trigger through a lull gets a burst of owed shots the moment
            // they press it again — the catch-up loop would fire one for every
            // interval since they last shot.
            if (((InputButtons)pressed).HasFlag(InputButtons.Attack))
                w.NextPrimaryAttack = now;

            PrimaryAttack(w, vm, in type, now, effectsLive, behaviour, command);
            return;
        }

        // ReloadOrSwitchWeapons: with nothing held and nothing left to fire,
        // the hands take the next best weapon.
        if ((held & (InputButtons.Attack | InputButtons.Attack2 | InputButtons.Reload)) == 0
            && SwitchIfExhausted(_arms, w, now, effectsLive, HasAnyAmmo(w, in type, clipSize)))
            return;

        // Reload on request, or because the clip ran dry and nothing is held,
        // or because the owner's wish tops the clip up while idle
        // (cl_autoreload; tf_weaponbase.cpp:2454's pressed reload).
        bool wantsReload = held.HasFlag(InputButtons.Reload)
                        || (buttons == 0 && clipSize > 0 && w.Clip <= 0)
                        || (behaviour?.AutoReload(Context(in type, w, vm, now, effectsLive, command), buttons) ?? false);

        if (wantsReload && clipSize > 0 && w.NextPrimaryAttack <= now)
        {
            Reload(w, vm, in type, now, effectsLive, behaviour, command);
            return;
        }

        // Nothing being asked for: let the view model settle back to idle.
        // A viewmodel whose idle is never sent stops animating once its deploy
        // finishes, which reads as the gun freezing in the player's hands.
        if (buttons == 0 && now >= w.TimeWeaponIdle)
        {
            SendAnim(vm, WeaponAnim.Idle, 0f, behaviour, Context(in type, w, vm, now, effectsLive, command));
            w.TimeWeaponIdle = now + IdleRepeat;
        }
    }

    /// <summary>The context one decision point is asked about.</summary>
    private WeaponAttackContext Context(in WeaponType type, WeaponNetState w, ViewModelNetState vm,
                                        float now, bool effectsLive, WeaponCommand command)
        => new(type, w, vm, now, effectsLive, _arms, command);

    /// <summary>
    /// The fire rate this machine uses for a row: the row's own, unless a
    /// behaviour has an opinion. Deterministic by rule — both sides ask one key
    /// and must get the same number, or the catch-up loop mispredicts.
    /// </summary>
    private float Interval(IWeaponBehaviour? behaviour, in WeaponType type, int ownerIndex)
        => behaviour is null ? type.Interval : behaviour.ModifyFireInterval(in type, type.Interval, ownerIndex);

    /// <summary>
    /// The clip this machine uses for a row: the row's own, unless a behaviour
    /// has an opinion. Zero or less is the row's own "no clip". Asked about the
    /// player whose hands the weapon is in, so a per-player clip scales the
    /// same on the server's run and the client's replay.
    /// </summary>
    private int ClipSize(IWeaponBehaviour? behaviour, in WeaponType type, int ownerIndex)
        => behaviour is null ? type.ClipSize : behaviour.ModifyClipSize(in type, type.ClipSize, ownerIndex);

    /// <summary>
    /// One pull of the trigger.
    ///
    /// The shape is stock's: a catch-up loop that owes one shot per fire-rate
    /// interval since the last, clamped to what is in the clip. That loop is
    /// what keeps rate of fire independent of framerate, and dropping it would
    /// make every automatic weapon slower on a busy server.
    /// </summary>
    /// <param name="behaviour">The row's resolved behaviour, or null for stock.</param>
    /// <param name="command">The command being run, for the behaviour's context.</param>
    public void PrimaryAttack(WeaponNetState w, ViewModelNetState vm,
                              in WeaponType type, float now, bool effectsLive,
                              IWeaponBehaviour? behaviour = null, WeaponCommand command = default)
    {
        // The veto, asked BEFORE anything is spent or moved: a behaviour that
        // says no pays for nothing, spends no round and leaves no animation.
        if (behaviour is not null && !behaviour.BeforePrimaryAttack(Context(in type, w, vm, now, effectsLive, command)))
            return;

        // An empty clip reloads instead of firing, which is what makes a reload
        // happen at all without anyone pressing a key for it.
        int clipSize = ClipSize(behaviour, in type, command.OwnerIndex);

        if (clipSize > 0 && w.Clip <= 0)
        {
            Reload(w, vm, in type, now, effectsLive, behaviour, command);
            return;
        }

        float interval = Interval(behaviour, in type, command.OwnerIndex);

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

        if (type.Kick is { } kick && _arms.Carrier is { } carrier)
            kick.BeforeShot(carrier);

        // A swing that meets nothing plays its miss (Swing's ACT_VM_MISSCENTER).
        bool connects = !type.IsMelee || _arms.MeleeConnects(in type);
        SendAnim(vm, connects ? WeaponAnim.Primary : WeaponAnim.Miss, 0f, behaviour, Context(in type, w, vm, now, effectsLive, command));

        // The shot's damage and crit flag: the row's, unless the behaviour
        // decides. Asked on the replay as well as the live run, because a
        // behaviour's roll may write predicted state (a crit bucket, a seed it
        // has drawn from) and both sides must have rolled it.
        bool hitscan = type.IsMelee || type.Projectile.Length == 0;
        ShotDamage shot = hitscan ? StockShot(in type) : default;

        if (hitscan && behaviour is not null)
            shot = behaviour.ModifyShot(Context(in type, w, vm, now, effectsLive, command), shot);

        // The sound is per shot, stock's order — before the round leaves. The
        // behaviour may answer the shot's sound (stock TF picks BURST or SINGLE
        // by the crit it rolled); null leaves the row's own, which is what every
        // stock HL2 weapon and every replay's arms hear.
        if (effectsLive)
        {
            string fireSound = behaviour?.FireSound(Context(in type, w, vm, now, effectsLive, command), shot)
                               ?? type.FireSound;
            for (int i = 0; i < shots; i++)
                _arms.Sound(fireSound);
        }

        if (type.IsMelee)
        {
            if (effectsLive && connects)
                _arms.MeleeSwing(in type, shot);
            else if (effectsLive)
                _arms.MeleeMiss(in type);
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
            _arms.FireBullets(in type, shots * Math.Max(1, type.Pellets),
                              behaviour?.BulletSpread(Context(in type, w, vm, now, effectsLive, command)) ?? type.Spread,
                              shot,
                              behaviour?.FixedPellets(Context(in type, w, vm, now, effectsLive, command)));
        }

        // After the shot, which read the punch as it stood; on a replay too: the
        // punch is predicted state, not an effect.
        Kick(type.Kick, w, now, effectsLive, command);

        // The tail, after the stock shot has been spent and the arms have fired.
        behaviour?.PrimaryAttack(Context(in type, w, vm, now, effectsLive, command));
    }

    /// <summary>
    /// The row's own shot: a swing's <see cref="WeaponType.MeleeDamage"/>, a
    /// bullet's ammo-row damage (stock's FireBullets reads the ammo definition
    /// unless the shot overrides it), never critical.
    /// </summary>
    private ShotDamage StockShot(in WeaponType type)
        => type.IsMelee
            ? new ShotDamage(type.MeleeDamage)
            : new ShotDamage(_ammo.For(type.AmmoName) is AmmoType ammo ? ammo.Damage : 0f, WeaponScriptDamage: true);

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
    /// <param name="command">The command being run, for the behaviour's context.</param>
    public void SecondaryAttack(WeaponNetState w, ViewModelNetState vm,
                                in WeaponType type, float now, bool effectsLive,
                                IWeaponBehaviour? behaviour = null, WeaponCommand command = default)
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

        SendAnim(vm, WeaponAnim.Primary, 0f, behaviour, Context(in type, w, vm, now, effectsLive, command));

        if (effectsLive)
        {
            _arms.Sound(secondary.Sound.Length > 0 ? secondary.Sound : type.FireSound);

            if (secondary.Projectile.Length > 0)
                _arms.Launch(in type, secondary.Projectile, secondary.LaunchSpeed, secondary.Thrown);
            else
                _arms.FireBullets(in type, Math.Max(1, secondary.Pellets), secondary.Spread);
        }

        Kick(secondary.Kick, w, now, effectsLive, command);

        // The tail, once the alt-fire has been paid for and the arms have fired.
        behaviour?.SecondaryAttack(Context(in type, w, vm, now, effectsLive, command));
    }

    /// <summary>What a shot does to its carrier's view, if the row says.</summary>
    private void Kick(WeaponKick? kick, WeaponNetState w, float now, bool effectsLive, WeaponCommand command)
    {
        if (kick is not { } k)
            return;

        if (k.Disorients && effectsLive)
            _arms.DisorientView();

        if (_arms.Carrier is not { } carrier)
            return;

        k.Apply(carrier, _kickRandom, command.Seed, w.FireStartedAt > 0f ? now - w.FireStartedAt : 0f);
        _arms.PunchChanged(carrier);
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
    /// <param name="command">The command being run, for the behaviour's context.</param>
    public void Reload(WeaponNetState w, ViewModelNetState vm,
                       in WeaponType type, float now, bool effectsLive,
                       IWeaponBehaviour? behaviour = null, WeaponCommand command = default)
    {
        int clipSize = ClipSize(behaviour, in type, command.OwnerIndex);

        if (clipSize <= 0 || w.InReload)
            return;

        // Nothing to load, or nowhere to put it.
        if (w.Reserve <= 0 || w.Clip >= clipSize)
            return;

        // The duration is the animation's, read from the model rather than
        // declared, so it cannot drift from the view model the table names. A
        // model with no reload animation still reloads, just immediately.
        float baseDuration = _arms.AnimDuration(WeaponAnim.Reload);

        // Stock's SetReloadTimer's arms (tf_weaponbase.cpp:2266-2331): the
        // behaviour scales the reload's time, and the viewmodel plays its
        // animation at the base over the scaled so the animation ends with the
        // clock, as stock sets the playback rate (:2322).
        float duration = behaviour?.ModifyReloadTime(in type, baseDuration, command.OwnerIndex) ?? baseDuration;

        SendAnim(vm, WeaponAnim.Reload, baseDuration > 0f && duration > 0f ? baseDuration / duration : 0f, behaviour, Context(in type, w, vm, now, effectsLive, command));

        // DefaultReload's WeaponSound( RELOAD ), which is the client's alone.
        if (effectsLive)
            _arms.ClientSound(type.ReloadSound);

        w.InReload = true;

        w.NextPrimaryAttack = now + duration;
        w.TimeWeaponIdle    = now + duration;

        behaviour?.Reload(Context(in type, w, vm, now, effectsLive, command));
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
    /// CBaseCombatWeapon::HasAnyAmmo for a row the machine runs: a weapon that
    /// uses no ammo always has some; else rounds in the clip or the pool, or
    /// in the secondary pool.
    ///
    /// <para>
    /// Whether a name is USED is stock's registered check, not the name's
    /// length: the script's ammo string becomes an ammo-system index
    /// (<c>GetAmmoDef()->Index</c>, basecombatweapon_shared.cpp:263) and an
    /// unregistered name leaves it -1, so <c>UsesPrimaryAmmo</c> (:1306-1321)
    /// says false and HasAnyAmmo (:1239-1247) early-trues. A fists row whose
    /// script says lowercase <c>none</c> (kept as a literal by ParseAmmo's
    /// strcmp parity) therefore uses no ammo and keeps itself when fired, as
    /// stock's fists do. Managed: the ammo catalog answers the index — a name
    /// with no row is an unused ammo slot.
    /// </para>
    /// </summary>
    public bool HasAnyAmmo(WeaponNetState w, in WeaponType type, int clipSize)
    {
        bool usesPrimary = _ammo.For(type.AmmoName) is not null,
             usesSecondary = _ammo.For(type.SecondaryAmmoName) is not null;
        if (!usesPrimary && !usesSecondary)
            return true;

        bool primary = usesPrimary && ((clipSize > 0 && w.Clip > 0) || w.Reserve > 0);
        return primary || (usesSecondary && w.SecondaryReserve > 0);
    }

    /// <summary>ReloadOrSwitchWeapons' m_flNextPrimaryAttack = curtime + 0.3 after the switch.</summary>
    public const float ExhaustedSwitchDelay = 0.3f;

    /// <summary>
    /// CBaseCombatWeapon::ReloadOrSwitchWeapons' switch, for the machine and
    /// the behaviours that replace its idle tail: nothing left to fire and the
    /// trigger's wait over (both triggers', but the shotgun's own copy waits on
    /// the primary alone), and the arms take the next best weapon.
    /// </summary>
    public static bool SwitchIfExhausted(IWeaponArms arms, WeaponNetState w, float now, bool effectsLive,
                                         bool hasAnyAmmo, bool waitsOnSecondary = true)
    {
        if (hasAnyAmmo || !effectsLive || w.NextPrimaryAttack >= now || (waitsOnSecondary && w.NextSecondaryAttack >= now))
            return false;

        if (!arms.SwitchToNextBest())
            return false;

        w.NextPrimaryAttack = now + ExhaustedSwitchDelay;
        return true;
    }

    /// <summary>
    /// The click of an empty gun, and a short delay so it clicks at a rate
    /// rather than every tick.
    /// </summary>
    private void HandleFireOnEmpty(WeaponNetState w, ViewModelNetState vm, in WeaponType type,
                                   float now, bool effectsLive,
                                   IWeaponBehaviour? behaviour, WeaponCommand command)
    {
        if (effectsLive)
            _arms.Sound(type.EmptySound);

        w.NextPrimaryAttack = now + EmptyClickInterval;

        // An empty gun reloads itself, which is what stock does through
        // ReloadOrSwitchWeapons and is the difference between a gun that has
        // run out and a gun that is broken. The whole Reload, as stock's calls
        // it: its animation and sound with its clock, which the behaviour scales.
        // The click's floor still floors a heavily scaled one.
        Reload(w, vm, in type, now, effectsLive, behaviour, command);
        if (w.InReload)
            w.NextPrimaryAttack = Math.Max(w.NextPrimaryAttack, now + EmptyClickInterval);
    }

    /// <summary>
    /// Points the viewmodel at a sequence. The parity bump is what restarts an
    /// animation the model is already in — stock's m_nAnimationParity, kept for
    /// stock's reason: without it the second shot of a pair is invisible.
    /// </summary>
    private void SendAnim(ViewModelNetState vm, WeaponAnim anim, float rate,
                          IWeaponBehaviour? behaviour = null, WeaponAttackContext context = default)
        => Play(_arms, vm, anim, rate, behaviour, context);

    /// <summary>
    /// Stock's SendWeaponAnim for a behaviour: the sequence, the parity bump
    /// and the rate, exactly as the machine sends its own. False when the
    /// model has no such animation.
    /// </summary>
    public static bool Play(IWeaponArms arms, ViewModelNetState vm, WeaponAnim anim, float rate = 0f,
                            IWeaponBehaviour? behaviour = null, WeaponAttackContext context = default)
    {
        int sequence = arms.LookupSequence(anim);

        if (sequence < 0)
            return false;

        // Stock's SendWeaponAnim is per weapon: the base restarts every send
        // (CBaseCombatWeapon::SendWeaponAnim) and each weapon answers for its
        // own looping anims through this seam — CTFMinigun::SendWeaponAnim's
        // hold-down arm (tf_weapon_minigun.cpp:961-967): "If we're already
        // playing the fire anim, let it continue. It loops." The answer
        // compares the ACTIVITIES as stock does; the base answer restarts, so
        // every other gun (the pistol, the SMG, the shotgun) still restarts
        // its fire anim on every shot of a held burst.
        if (behaviour is not null && behaviour.AnimContinuesOnResend(in context, anim))
            return true;

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
