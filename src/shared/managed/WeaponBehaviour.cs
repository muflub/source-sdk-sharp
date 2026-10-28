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
    /// </summary>
    float ModifyFireInterval(in WeaponType type, float interval) => interval;

    /// <summary>
    /// The size of a full clip, which decides what the clip spends down to,
    /// what a fresh give fills to, and when a reload is full. Zero or less is
    /// the row's own "no clip" (a pool-fed or melee weapon), so tuning a clip
    /// away is the same statement the row makes with <c>-1</c>. Return
    /// <paramref name="clipSize"/> for the row's own.
    /// </summary>
    int ModifyClipSize(in WeaponType type, int clipSize) => clipSize;

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
public readonly record struct WeaponAttackContext(
    WeaponType Type,
    WeaponNetState Weapon,
    ViewModelNetState ViewModel,
    float Now,
    bool EffectsLive,
    IWeaponArms Arms);

/// <summary>
/// The behaviours a mod registers, keyed by the name a
/// <see cref="WeaponType.Behaviour"/> row carries.
///
/// <para>
/// A source is found the way every other mod service is: marked
/// <c>[SideService]</c> and collected from the side's container when the side
/// first asks its machine a question. Both sides run the same sources — the
/// client predicts the shot, so it must resolve the same behaviour the server
/// resolved — and the collection is ordered by assembly name so the first owner
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
