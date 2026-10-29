namespace SourceSharp.Abi;

/// <summary>
/// What answers to a classname when something asks for one to be created.
/// </summary>
public enum EntityFactoryOwner
{
    /// <summary>
    /// Nothing does. Asking produces no entity, on this map or any other, until
    /// a factory is registered — which is a REFUSAL and is printed as one
    /// (<c>plan_entity.md</c> §7), never a silent no-op.
    /// </summary>
    Nothing = 0,

    /// <summary>
    /// A <c>[ScriptedEntity]</c> class. Managed code creates it end to end:
    /// <c>MapSpawner</c> builds it from the map block, and the native factory in
    /// the dictionary exists only to allocate the shell the engine addresses it
    /// through.
    /// </summary>
    Managed,

    /// <summary>
    /// A factory installed from native code — <c>worldspawn</c>
    /// (<c>world_adapter.cpp</c>) and every <c>SOURCESHARP_LINK_ENTITY</c>
    /// (<c>shell_diagnostics.h</c>). Managed code does not build these; the map
    /// block goes to the native parse as residue and the factory does the rest.
    /// </summary>
    Native,
}

/// <summary>
/// <b>The port's managed view of <c>EntityFactoryDictionary()</c>.</b>
///
/// <para>
/// The dictionary itself is the shim's as of lane <c>sub-factory</c> — see
/// <c>src/sourcesharp/game/server/shell_standalone_class.cpp</c> for why it moved
/// off <c>src/game/server/util.cpp</c> rather than being deleted with it. This is
/// the half that lets managed code ANSWER QUESTIONS about it, which it has never
/// been able to do. <c>DevCommandsLive.cs</c> says so in its own words: "the
/// native factory dictionary is not reachable from here", and so
/// <c>ent_create</c> hedges about a classname it cannot check instead of refusing
/// one that nothing answers to.
/// </para>
///
/// <para>
/// <b>THIS IS AN AGREEMENT CHECK, NOT A SECOND TABLE.</b> The distinction is the
/// whole design. A managed mirror of a native table is a fixture, and this
/// project has recorded what fixtures do: they drift, silently, and every check
/// over them stays green while the thing they stand for moves. So the native
/// names are PULLED from the dictionary rather than declared here, and what this
/// class adds is the join between them and the managed registry — a join neither
/// side can compute alone, because neither side can see the other's list.
/// </para>
///
/// <para>
/// <b>NULL PROVIDERS MEAN "NOT ASKED", NOT "EMPTY".</b> The five delegates below
/// reach native through ABI slots appended at version <b>242</b> by the
/// <c>abi-tail</c> merge, and the server's composition binds them through
/// <c>EngineServices.EntityFactories</c> — on the SERVER arm only, and only when
/// the slots are really filled. They stay null
/// on the client, which has no factory dictionary at all, and null on any build
/// whose native half does not fill them.
/// </para>
///
/// <para>
/// With them null <see cref="Available"/> is false and every consumer keeps
/// exactly its present behaviour. <b>That distinction is load-bearing and must
/// not be simplified away:</b> an unwired table that answered "no factory" for
/// every name would turn <c>ent_create prop_physics</c> into a refusal for a
/// class that works, and would declare a hundred present capabilities absent.
/// </para>
/// </summary>
/// <remarks>
/// <b>AN INSTANCE, ONE PER SERVER COMPOSITION</b> (plan_abi_hotreload D7). The
/// table and its five providers were statics, so two managed game instances in
/// one process would have pulled into, and answered from, one table. The
/// server's composition builds one with <c>EngineServices.EntityFactories</c>,
/// registers it in its container, and hands it to the map spawner, the
/// development console and the dev API's report.
/// </remarks>
public sealed class EntityFactories
{
    // ------------------------------------------------------------------
    // The native edge. Five delegates rather than five direct Api calls, so
    // that this class can be driven by a fixture in xUnit and by the real
    // dictionary in the game, and so that "could not ask" stays expressible.
    //
    // WIRED BY THE SERVER'S COMPOSITION, through `EngineServices.EntityFactories`,
    // which asks `EngineSide.EntityFactories` — the side that holds the slots.
    //
    // THE REFUSAL LEDGER IS BOUND AS A SET OF THREE OR NOT AT ALL. `Refresh`
    // skips the whole ledger without all three, so a partial wiring reads as
    // "nothing has ever been refused".
    // ------------------------------------------------------------------

    /// <summary>How many classnames have a factory installed. Null until wired.</summary>
    public Func<int>? NativeCount { get; set; }

    /// <summary>The i'th installed classname in install order, or null past the end.</summary>
    public Func<int, string?>? NativeNameAt { get; set; }

    /// <summary>How many distinct classnames have been asked for and refused.</summary>
    public Func<int>? RefusalCount { get; set; }

    /// <summary>The i'th refused classname, or null past the end.</summary>
    public Func<int, string?>? RefusalNameAt { get; set; }

    /// <summary>How many times the i'th refused classname was asked for.</summary>
    public Func<int, int>? RefusalTimesAt { get; set; }

    /// <summary>
    /// Whether the native dictionary can be asked at all. False before the ABI
    /// slots are wired, and false is NOT the same answer as "it is empty" — see
    /// the class summary.
    /// </summary>
    public bool Available => NativeCount is not null && NativeNameAt is not null;

    private readonly HashSet<string> _native = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string ClassName, int Times)> _refused = new();
    private bool _pulled;

    /// <summary>
    /// Every classname the native dictionary holds a factory for, as of the last
    /// <see cref="Refresh"/>. Empty when <see cref="Available"/> is false.
    /// </summary>
    public IReadOnlyCollection<string> NativeNames => _native;

    /// <summary>
    /// The classnames something asked to create that nothing answered to, with
    /// how many times each was asked for.
    ///
    /// <para>
    /// Cumulative for the life of the process, because a refusal is a fact about
    /// the BUILD and not about the map that happened to name it. On dm_lockdown
    /// this is <c>info_node</c> — the map's AI node graph, whose definer
    /// (<c>ai_initutils.cpp</c>) is not a row of <c>server_csharp.vpc</c>. It
    /// was also <c>func_useableladder</c> and <c>info_ladder_dismount</c>, the
    /// map's ladders, until those became managed entities;
    /// <c>.claude/entity-port/status/p4-flip.md</c> §2 found the three by
    /// grepping a console log that no gate opens, and this is the same fact
    /// asked for.
    /// </para>
    /// </summary>
    public IReadOnlyList<(string ClassName, int Times)> Refusals => _refused;

    /// <summary>Whether <see cref="Refresh"/> has run and got an answer.</summary>
    public bool Pulled => _pulled;

    /// <summary>
    /// Re-read the native dictionary.
    ///
    /// <para>
    /// Called after a level loads and after a <c>cs_sv_reload</c>, which are the
    /// two moments the table can change: a map's parse is what produces refusals,
    /// and a reload is what re-declares every managed class. It is NOT called per
    /// entity or per frame — the whole table is a few hundred strings and the
    /// pull is two crossings plus one per name.
    /// </para>
    ///
    /// <para>
    /// A null or empty name from native ENDS the walk rather than being skipped.
    /// The contract is that indices are dense and NameAt answers null past the
    /// end, so a null in the middle means the far side is not the table this was
    /// written against, and walking on would read whatever came after it.
    /// </para>
    /// </summary>
    public void Refresh()
    {
        _native.Clear();
        _refused.Clear();
        _pulled = false;

        if (!Available)
            return;

        int count = NativeCount!();

        for (int i = 0; i < count; i++)
        {
            string? name = NativeNameAt!(i);

            if (string.IsNullOrEmpty(name))
                break;

            _native.Add(name);
        }

        _pulled = true;

        if (RefusalCount is null || RefusalNameAt is null || RefusalTimesAt is null)
            return;

        int refused = RefusalCount();

        for (int i = 0; i < refused; i++)
        {
            string? name = RefusalNameAt(i);

            if (string.IsNullOrEmpty(name))
                break;

            _refused.Add((name, RefusalTimesAt(i)));
        }
    }

    /// <summary>
    /// Which of the three things happens when <paramref name="className"/> is
    /// asked for.
    ///
    /// <para>
    /// <paramref name="isManaged"/> is the managed registry's own
    /// <c>IsRegistered</c>, passed in rather than reached for, because this
    /// assembly holds two registries — the server's and the client's — and a
    /// static that picked one would be right on one side and quietly wrong on
    /// the other.
    /// </para>
    ///
    /// <para>
    /// <b>Managed WINS over native, and the order is not arbitrary.</b> Every
    /// <c>[ScriptedEntity]</c> also has a native factory (that is what
    /// <c>SourceSharp_InstallStandaloneClass</c> installs), so a name that is
    /// both is a managed class seen from the other side — not a collision.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing is answered only when native was actually asked.</b> With the
    /// slots unwired every name that is not managed reads
    /// <see cref="EntityFactoryOwner.Native"/>, which is the assumption this
    /// build already runs on: the residue goes to the native parse and whatever
    /// answers, answers. Reporting "Nothing" from an empty table would refuse
    /// classes that work.
    /// </para>
    /// </summary>
    public EntityFactoryOwner OwnerOf(string? className, Func<string, bool> isManaged)
    {
        ArgumentNullException.ThrowIfNull(isManaged);

        if (string.IsNullOrEmpty(className))
            return EntityFactoryOwner.Nothing;

        if (isManaged(className))
            return EntityFactoryOwner.Managed;

        if (!_pulled)
            return EntityFactoryOwner.Native;

        return _native.Contains(className) ? EntityFactoryOwner.Native
                                            : EntityFactoryOwner.Nothing;
    }

    /// <summary>
    /// The residue classnames — the ones a map load handed to the native parse —
    /// split into the ones something answers to and the ones nothing does.
    ///
    /// <para>
    /// The second list is a capability this build does not have, named. On
    /// dm_lockdown it is three names covering 18 of the map's 605 blocks; the
    /// nineteenth is <c>worldspawn</c>, which does have a factory.
    /// </para>
    ///
    /// <para>
    /// Both lists are DE-DUPLICATED and keep first-seen order, because a caller
    /// wants "which classnames" and the residue holds one entry per block —
    /// fourteen <c>info_ladder_dismount</c> blocks are one missing capability,
    /// not fourteen.
    /// </para>
    /// </summary>
    public (List<string> Answered, List<string> Unanswered) SplitResidue(
        IEnumerable<string> residueClassNames, Func<string, bool> isManaged)
    {
        ArgumentNullException.ThrowIfNull(residueClassNames);
        ArgumentNullException.ThrowIfNull(isManaged);

        var answered = new List<string>();
        var unanswered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string name in residueClassNames)
        {
            if (string.IsNullOrEmpty(name) || !seen.Add(name))
                continue;

            if (OwnerOf(name, isManaged) == EntityFactoryOwner.Nothing)
                unanswered.Add(name);
            else
                answered.Add(name);
        }

        return (answered, unanswered);
    }

    /// <summary>
    /// The one line a map load prints about what it could not create, or null
    /// when there is nothing to say.
    ///
    /// <para>
    /// A string rather than a print, so a test can read it. The native side
    /// prints its own refusal once per classname for the life of the process
    /// (<c>shell_standalone_class.cpp</c>); this is the per-map summary, which is
    /// the thing a person watching a load actually wants: how many of THIS map's
    /// blocks went nowhere.
    /// </para>
    /// </summary>
    public static string? DeclareUnanswered(IReadOnlyList<string> unanswered, int residueBlocks)
    {
        ArgumentNullException.ThrowIfNull(unanswered);

        if (unanswered.Count == 0)
            return null;

        return $"[SourceSharp] this map names {unanswered.Count} entity class(es) that nothing " +
               $"in this build can create, out of {residueBlocks} block(s) handed to the native " +
               $"parse: {string.Join(", ", unanswered)}. Entities of those classes do not exist " +
               $"here and the capability they carry is absent.\n";
    }

    /// <summary>
    /// Whether a create of <paramref name="owner"/> needs the caller to go and
    /// look for live entities of the class before it says anything.
    ///
    /// <para>
    /// <b>A SHORT-CIRCUIT, EXPRESSED RATHER THAN RELIED ON.</b> The live-entity
    /// walk is an engine call, and the condition it used to sit behind was
    /// <c>!IsRegistered(..) &amp;&amp; !FindAllByClassname(..).Any()</c> — where
    /// C#'s <c>&amp;&amp;</c> did the skipping silently. Making it a named
    /// answer is what let nine facts catch the eager version immediately: a
    /// strict test engine has no stub for a call the command never used to make.
    /// </para>
    ///
    /// <para>
    /// False for a class we already know answers — a managed one, or a native
    /// one on a table that was actually asked. True otherwise, including the
    /// unwired case, where <c>Native</c> means "assumed" rather than "known".
    /// </para>
    /// </summary>
    public bool NeedsLiveCheck(EntityFactoryOwner owner) =>
        owner switch
        {
            EntityFactoryOwner.Managed => false,
            EntityFactoryOwner.Native => !_pulled,
            _ => true,
        };

    /// <summary>
    /// What to tell somebody who asked to create <paramref name="className"/>
    /// and for whom <see cref="NeedsLiveCheck"/> was true and found nothing
    /// live, or null when there is nothing worth saying.
    /// </summary>
    public string? CreateAdvice(string verb, string className, EntityFactoryOwner owner)
    {
        if (owner == EntityFactoryOwner.Nothing)
        {
            return $"{verb}: nothing in this build can create a '{className}' \u2014 it is in " +
                   $"neither the managed registry nor the entity factory dictionary. " +
                   $"See `dumpentityfactories`.\n";
        }

        if (owner == EntityFactoryOwner.Native && !_pulled)
        {
            // The text this replaced, word for word, for exactly the case it was
            // written about: the dictionary could not be asked.
            return $"{verb}: '{className}' is not a registered managed class and nothing of " +
                   $"that class is live; if the native factory has no such name either, " +
                   $"nothing will come back.\n";
        }

        return null;
    }

    /// <summary>
    /// Forget everything pulled. For tests, and for the reload path — a managed
    /// reload does not restart the process, so a stale table would outlive the
    /// build it described.
    /// </summary>
    public void Reset()
    {
        _native.Clear();
        _refused.Clear();
        _pulled = false;
    }
}
