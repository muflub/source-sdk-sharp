using SourceSharp.Abi;
namespace SourceSharp.Shared;

/// <summary>
/// One weapon's behaviour: the decisions <see cref="WeaponLogic"/> offers a mod
/// to make, with the stock answer as the default of every one of them.
///
/// <para>
/// <see cref="WeaponLogic"/> stays sealed and its stock path stays untouched. A
/// mod's behaviour is asked AT the named points and the machine runs stock the
/// moment the answer is the default one, so an unused seam costs one dictionary
/// ask per command and a used one cannot corrupt the stock rules — the machine
/// never delegates a whole attack, only these decisions.
/// </para>
///
/// <para>
/// A mod binds a behaviour to its rows with <see cref="WeaponType.Behaviour"/>:
/// the key is the name, and <see cref="IWeaponBehaviourSource"/> is where the
/// key is answered. An unresolved key is stock, out loud — a weapon whose
/// behaviour nobody registered behaves like a hitscan gun with the row's own
/// numbers, which is what "the mod forgot to register it" looks like.
/// </para>
///
/// <para>
/// <b>PREDICTION.</b> The machine runs this on the server AND on the client's
/// replay of every command, over the two declared states, and the answer must
/// come out the same from the same inputs on both sides. A behaviour may read
/// and write <see cref="WeaponAttackContext.Weapon"/> and
/// <see cref="WeaponAttackContext.ViewModel"/> freely — that is what the states
/// are for — but anything the replay must not repeat (a sound, a trace, a spawn)
/// has to hang off <see cref="WeaponAttackContext.EffectsLive"/>, exactly as the
/// machine's own arms calls do. A behaviour that fires an effect while
/// <c>EffectsLive</c> is false mispredicts the command, and does so silently
/// until the misprediction report says the weapon's blob diverged.
/// </para>
/// </summary>
public interface IWeaponBehaviour
{
    /// <summary>
    /// Asked before the primary attack does anything, before a round is spent
    /// and before the clock moves. False vetoes the shot: the machine touches
    /// no state and calls nothing else for this attack.
    ///
    /// <para>
    /// A veto that charges no cooldown of its own (by writing
    /// <see cref="WeaponNetState.NextPrimaryAttack"/> itself) is asked again on
    /// the next command, which is what a trigger held against a chambered weapon
    /// wants — and what a behaviour that only ever says no must expect to be
    /// asked thousands of times a second for.
    /// </para>
    /// </summary>
    bool BeforePrimaryAttack(in WeaponAttackContext attack) => true;

    /// <summary>
    /// Asked after the stock primary attack has run: the shot is spent, the
    /// animation is sent and the arms have fired. The place for a mod's own
    /// effect or bookkeeping that follows a shot, gated on
    /// <see cref="WeaponAttackContext.EffectsLive"/>.
    /// </summary>
    void PrimaryAttack(in WeaponAttackContext attack) { }

    /// <summary>
    /// Asked after the stock alt-fire has run and been paid for. Nothing about
    /// the alt-fire's own rate or pool is negotiable here; this is the tail, and
    /// the same live/replay rule applies.
    /// </summary>
    void SecondaryAttack(in WeaponAttackContext attack) { }

    /// <summary>
    /// Asked after a reload has started — the animation is sent and
    /// <see cref="WeaponNetState.InReload"/> is set, and the ammunition moves
    /// later, when the animation has run its course.
    /// </summary>
    void Reload(in WeaponAttackContext attack) { }

    /// <summary>
    /// The start of one command, before anything is fired or reloaded: stock's
    /// <c>ItemPreFrame</c>, with the command's own <paramref name="buttons"/>.
    /// </summary>
    void ItemPreFrame(in WeaponAttackContext attack, int buttons) { }

    /// <summary>
    /// The cone a shot is spread in, as its half-angle in degrees, when the
    /// weapon's own GetBulletSpread decides it (the pistol's, which widens with
    /// its accuracy penalty); null for the row's own.
    /// </summary>
    float? BulletSpread(in WeaponAttackContext attack) => null;

    /// <summary>
    /// The pattern this attack's pellets leave in, instead of the random
    /// sample: stock's fixed multi-pellet spread (<c>bFixedSpread</c>,
    /// <c>tf_fx_shared.cpp:302</c>) — <c>tf_use_fixed_weaponspreads</c>, a
    /// buckshot load of more than one. Null for the random sample, every
    /// weapon's answer by default.
    /// </summary>
    PelletPattern? FixedPellets(in WeaponAttackContext attack) => null;

    /// <summary>
    /// Whether this owner's wish tops the clip up while idle, pressed as a
    /// reload (<c>cl_autoreload</c> at the post frame,
    /// <c>tf_weaponbase.cpp:2454</c>): the machine asks beside the held
    /// reload key. The held attack buttons are the answer's to check.
    /// </summary>
    bool AutoReload(in WeaponAttackContext attack, int buttons) => false;

    /// <summary>
    /// The end of one command, whatever the weapon did in it — firing,
    /// reloading, or standing idle. Stock's <c>ItemPostFrame</c> tail, and the
    /// only behaviour point that runs every command.
    /// </summary>
    void ItemPostFrame(in WeaponAttackContext attack) { }

    /// <summary>
    /// The whole command, in place of the shared machine's: for a stock weapon
    /// whose ItemPostFrame replaces CBaseCombatWeapon's (the HL2MP shotgun).
    /// True when it ran the command; false leaves the machine to it. The
    /// machine records the buttons either way; ItemPostFrame is not called
    /// after a command this ran.
    /// </summary>
    bool RunCommand(in WeaponAttackContext attack, int buttons, int pressed) => false;

    /// <summary>
    /// The weapon's fire interval, which is the seconds between shots the
    /// primary attack's catch-up loop counts in. Returning 0 is not "no delay"
    /// — see <see cref="WeaponLogic.ShotsOwed"/>: a zero interval owes exactly
    /// one shot, and a negative is read as a weapon that never comes off
    /// cooldown. Return <paramref name="interval"/> for the row's own rate.
    /// <paramref name="ownerIndex"/> is the entity index of the player whose
    /// hands the weapon is in — <see cref="WeaponCommand.OwnerIndex"/> on a
    /// command's run, and the carrier's index when the arsenal fills a clip
    /// (0 when nobody was named). A per-player question must be answered from
    /// this index alone: the server's run and the client's replay of one
    /// command hand the same number, and any other owner source mispredicts
    /// the clocks.
    /// </summary>
    float ModifyFireInterval(in WeaponType type, float interval, int ownerIndex) => interval;

    /// <summary>
    /// The size of a full clip, which decides what the clip spends down to,
    /// what a fresh give fills to, and when a reload is full. Zero or less is
    /// the row's own "no clip" (a pool-fed or melee weapon), so tuning a clip
    /// away is the same statement the row makes with <c>-1</c>. Return
    /// <paramref name="clipSize"/> for the row's own. <paramref name="ownerIndex"/>
    /// is the player the clip is filled for, as in
    /// <see cref="ModifyFireInterval"/>.
    /// </summary>
    int ModifyClipSize(in WeaponType type, int clipSize, int ownerIndex) => clipSize;

    /// <summary>
    /// The seconds a reload takes, before a behaviour has an opinion on it:
    /// the machine's reload animation's duration, asked at the moments the
    /// machine sets a reload's clock. Stock's TF weapons run every such clock
    /// through <c>CTFWeaponBase::SetReloadTimer</c>
    /// (<c>shared/tf/tf_weaponbase.cpp:2266-2331</c>), whose arms scale it by
    /// the item's and the player's attributes — the answer here is what they
    /// scale. The machine sends the viewmodel's reload animation at the
    /// base-to-answer rate, as stock sets the playback rate to the base over
    /// the scaled (<c>:2322</c>). Return <paramref name="duration"/> for the
    /// row's own. <paramref name="ownerIndex"/> is the player whose reload it
    /// is, as in <see cref="ModifyFireInterval"/>: the reload's clock is
    /// predicted state, and a per-player question must be answered from this
    /// index alone or the replay mispredicts the clocks.
    /// </summary>
    float ModifyReloadTime(in WeaponType type, float duration, int ownerIndex) => duration;

    /// <summary>
    /// Asked when the weapon comes into the hands, after the side has set the
    /// deploy's timers and sent the draw: stock's per-class Deploy tail.
    /// </summary>
    void Deploy(in WeaponAttackContext attack) { }

    /// <summary>Whether the weapon may be put away now (stock's CanHolster).</summary>
    bool CanHolster(in WeaponAttackContext attack) => true;

    /// <summary>
    /// Asked as the weapon leaves the hands — a switch, a death, a strip, a
    /// disconnect — before its state is handed to the next weapon.
    /// </summary>
    void Holster(in WeaponAttackContext attack) { }

    /// <summary>
    /// Whether a death drops this weapon as a pickup. False for one whose stock
    /// Drop removes it instead (the physcannon).
    /// </summary>
    bool LeavesPickup => true;

    /// <summary>
    /// CHL2MP_Player::Weapon_Drop's grenade arm (hl2mp_player.cpp:1254-1265): the fuse
    /// of what this weapon lets go instead of being dropped, with
    /// <paramref name="buttons"/> held as the player died; null to drop it as usual.
    /// </summary>
    float? PrimedDropFuse(int buttons) => null;

    /// <summary>
    /// The damage and the crit flag of a primary attack's bullets or swing,
    /// asked once per attack after the round is spent and before the arms fire
    /// (a projectile weapon is not asked: it launches, it does not hit).
    /// <paramref name="shot"/> is the row's own — the ammo row's damage per
    /// bullet, or the swing's — and not critical; return it for stock.
    ///
    /// <para>
    /// Asked on the replay as well as on the live run, so a seeded roll here
    /// (<see cref="WeaponAttackContext.Command"/>) runs on both sides and may
    /// write the weapon's predicted state. What a crit does is the answer's
    /// damage: the core carries the flag to the victim and multiplies nothing.
    /// </para>
    /// </summary>
    ShotDamage ModifyShot(in WeaponAttackContext attack, ShotDamage shot) => shot;

    /// <summary>
    /// What one shot sounds like, asked once per attack while effects are
    /// live, just before the machine plays it: null for the row's own
    /// <see cref="WeaponType.FireSound"/>. Stock TF decides this per attack in
    /// <c>CTFWeaponBaseGun::PlayWeaponShootSound</c>
    /// (<c>tf_weaponbase_gun.cpp:829-839</c>) — the script's BURST when the
    /// attack is a crit and SINGLE otherwise — and <paramref name="shot"/> is
    /// this side's answer of <see cref="ModifyShot"/>. The sound is the only
    /// thing this decides: the damage stays the server's, settled by the
    /// server's own roll of the same seed.
    /// </summary>
    string? FireSound(in WeaponAttackContext attack, ShotDamage shot) => null;

    /// <summary>
    /// Whether a re-send of the animation already playing continues it where
    /// it stands instead of restarting it. Stock's base answer restarts every
    /// send (<c>CBaseCombatWeapon::SendWeaponAnim</c>) — return <c>false</c>
    /// for that. The answer is true for a looping anim only, as
    /// <c>CTFMinigun::SendWeaponAnim</c> answers it for its hold-down fire
    /// anim (<c>tf_weapon_minigun.cpp:961-967</c>: "If we're already playing
    /// the fire anim, let it continue. It loops."), comparing the ACTIVITIES
    /// as stock compares them (<c>GetActivity() == ACT_VM_PRIMARYATTACK</c>):
    /// the anim being sent and the one already playing. Asked at the
    /// machine's own send (<see cref="WeaponLogic.Play"/>).
    /// </summary>
    bool AnimContinuesOnResend(in WeaponAttackContext attack, WeaponAnim anim) => false;
}

/// <summary>
/// One command's worth of what the machine is doing, handed to an
/// <see cref="IWeaponBehaviour"/> at a decision point.
/// </summary>
/// <param name="Type">The row in the weapon table, as the machine read it.</param>
/// <param name="Weapon">
/// The carried weapon's DECLARED state — the wire format and the prediction
/// schema at once. Read and write it; the server settles the ammunition pools
/// from what it changes, and the client's replay compares it.
/// </param>
/// <param name="ViewModel">
/// The viewmodel's declared state, for an animation a behaviour sends through
/// the arms (see <see cref="IWeaponArms.LookupSequence"/>).
/// </param>
/// <param name="Now">
/// The clock the machine is running at — the command's own on the client's
/// replay, not the frame's, which is what keeps the timers from mispredicting.
/// </param>
/// <param name="EffectsLive">
/// True on the server and on the client's first prediction of a command; false
/// for every replay of one already predicted. Gate anything the replay must not
/// repeat on it.
/// </param>
/// <param name="Arms">
/// This side's hands: sound, bullets, the launch, the sequence lookups. The
/// state writes never go through them; the effects always do.
/// </param>
/// <param name="Command">
/// Which command this is and whose weapon runs it: the command's prediction
/// seed and the weapon's and owner's entity indices, the same numbers on the
/// server's run and on the client's replay of the command (see
/// <see cref="WeaponCommand"/>). A roll seeded from them comes out the same on
/// both sides.
/// </param>
public readonly record struct WeaponAttackContext(
    WeaponType Type,
    WeaponNetState Weapon,
    ViewModelNetState ViewModel,
    float Now,
    bool EffectsLive,
    IWeaponArms Arms,
    WeaponCommand Command = default);

/// <summary>
/// The command a weapon machine is running, as a predicted roll needs it: the
/// command's random seed and the entity indices of the weapon and its owner.
///
/// <para>
/// <b>THE SEED IS STOCK'S <c>CBaseEntity::GetPredictionRandomSeed</c></b>: the
/// user command's <c>random_seed</c>, which <c>CBasePlayer::PlayerRunCommand</c>
/// installs before the weapon's frame runs. Both ends derive it from the command
/// number alone (<see cref="UserCmdCodec.RandomSeedFor"/>, ReadUsercmd's
/// <c>MD5_PseudoRandom(command_number) &amp; 0x7fffffff</c>), so the server's
/// run of command N and the client's replay of command N read one seed.
/// </para>
///
/// <para>
/// <b>THE INDICES ARE ENTITY INDICES</b> (<c>entindex()</c>), which a networked
/// entity has the same on both sides: the low bits of either side's handle.
/// TF's crit roll seeds with <c>seed ^ (weapon entindex &lt;&lt; 8 | player
/// entindex)</c> (<c>CTFWeaponBase::CalcIsAttackCriticalHelper</c>,
/// <c>src/game/shared/tf/tf_weaponbase.cpp</c>), which is why the weapon's own
/// index rides here and not only the owner's. The mask is the mod's to apply:
/// the core hands over the three numbers and draws nothing from them.
/// </para>
///
/// <para>
/// <c>default</c> (all zero) outside a command — a test driving the machine
/// directly, or a caller that has no command to name.
/// </para>
/// </summary>
/// <param name="Seed">The command's prediction random seed (never negative).</param>
/// <param name="WeaponIndex">The weapon's entity index.</param>
/// <param name="OwnerIndex">The owning player's entity index.</param>
public readonly record struct WeaponCommand(int Seed, int WeaponIndex, int OwnerIndex)
{
    /// <summary>
    /// The command <paramref name="cmd"/> as run by <paramref name="weapon"/> in
    /// <paramref name="owner"/>'s hands: the one derivation both sides use.
    /// </summary>
    public static WeaponCommand For(in UserCmd cmd, EntityHandle weapon, EntityHandle owner)
        => new(UserCmdCodec.RandomSeedFor(cmd.CommandNumber), weapon.EntryIndex, owner.EntryIndex);
}

/// <summary>
/// The behaviours a mod registers, keyed by the name a
/// <see cref="WeaponType.Behaviour"/> row carries.
///
/// <para>
/// A source is found the way every other mod service is: marked
/// <c>[SideService]</c> and collected from the side's container when the side
/// first asks its machine a question. Both sides run the same sources — the
/// client predicts the shot, so it must resolve the same behaviour the server
/// resolved — and the collection is ordered by assembly layer (HookOrder) so the first owner
/// of a key is the same object on both sides (see
/// <see cref="WeaponCatalog"/>'s note on the same rule).
/// </para>
/// </summary>
public interface IWeaponBehaviourSource
{
    /// <summary>
    /// The behaviours this source provides, each under the key a row names. Keys
    /// are matched case-insensitively; a key another source or an earlier source
    /// already claimed is refused with a warning, so one key never means two
    /// behaviours and the wire stays one contract.
    /// </summary>
    IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours();
}

/// <summary>
/// The behaviour table one side reads: every registered key, and stock for
/// everything else.
///
/// <para>
/// Built lazily — the first question asked of a weapon machine collects the
/// sources — for the same reason <c>Hooks&lt;T&gt;</c> is lazy: the container is
/// not complete while the module is still registering, and a hook collected too
/// early is a hook list that silently stops at the core's own classes. Nothing
/// may register a service after the first weapon command runs, which is the rule
/// every hook in this game already lives by.
/// </para>
/// </summary>
public sealed class WeaponBehaviours
{
    private readonly Func<IReadOnlyList<IWeaponBehaviourSource>>? _sources;
    private readonly ILog? _log;

    // key -> behaviour, and the keys already reported as unclaimed so the
    // warning about a row naming a behaviour nobody gave is said once per key
    // and not once per shot.
    private Dictionary<string, IWeaponBehaviour>? _byKey;
    private readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The table for one side. <paramref name="sources"/> is asked once, on the
    /// first resolve — the module hands over the ask rather than the answer
    /// precisely so that it happens after the mod assemblies have registered.
    /// </summary>
    public WeaponBehaviours(Func<IReadOnlyList<IWeaponBehaviourSource>>? sources, ILog? log = null)
    {
        _sources = sources;
        _log = log;
    }

    /// <summary>No mod sources: the core HL2MP weapons' rules and nothing else.</summary>
    public static WeaponBehaviours Stock() => new(null);

    /// <summary>
    /// The behaviour a row's key names, or null for stock. A non-empty key
    /// nothing answered is reported once, because a weapon running stock while
    /// its mod believed it had a behaviour is exactly the silence this file
    /// exists to remove.
    /// </summary>
    public IWeaponBehaviour? For(string key)
    {
        _byKey ??= Collect();

        if (key.Length == 0)
            return null;

        if (_byKey.TryGetValue(key, out var behaviour))
            return behaviour;

        if (_warned.Add(key))
            _log?.Warning($"[SourceSharp] no weapon behaviour named '{key}'; that weapon runs stock\n");

        return null;
    }

    private Dictionary<string, IWeaponBehaviour> Collect()
    {
        var found = new Dictionary<string, IWeaponBehaviour>(StringComparer.OrdinalIgnoreCase);

        // The core HL2MP weapons' own rules, after every mod source has had
        // its say: a mod that names one of these keys owns it.
        void AddCore()
        {
            foreach (var (key, behaviour) in Hl2mpWeaponBehaviours.All())
                found.TryAdd(key, behaviour);
        }

        if (_sources is null)
        {
            AddCore();
            return found;
        }

        foreach (var source in HookOrder.Sorted(_sources()))
        {
            foreach (var (key, behaviour) in source.Behaviours())
            {
                if (key.Length == 0 || behaviour is null)
                {
                    _log?.Warning($"[SourceSharp] {source.GetType().FullName} offered a behaviour with no key; skipped\n");
                    continue;
                }

                if (found.ContainsKey(key))
                {
                    _log?.Warning($"[SourceSharp] {source.GetType().FullName} offered behaviour '{key}', " +
                                  "which another source already owns; the first owner answers\n");
                    continue;
                }

                found[key] = behaviour;
            }
        }

        AddCore();
        return found;
    }
}
