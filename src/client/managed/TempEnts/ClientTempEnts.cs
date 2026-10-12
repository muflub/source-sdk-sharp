using System.Reflection;
using SourceSharp.Abi;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// Routes an arriving temp entity to whichever managed class plays it.
///
/// <para>
/// THE WHOLE RECEIVING PATH ON THIS SIDE, and it is four steps: native decoded
/// the event into a blob through a RecvTable built from the shared declaration;
/// this turns the handle back into that declaration; a
/// <see cref="TempEntReader"/> addresses the blob through it; and the class
/// registered for the name plays the effect.
/// </para>
///
/// <para>
/// A HANDLE THIS SIDE DOES NOT KNOW IS A WARNING ONCE, not per event. A
/// firefight sends hundreds a second, so warning per event would fill a console
/// and hide the line that explained it — and never warning would make a whole
/// effect disappear silently, which is the failure this port is scored against.
/// </para>
///
/// <para>
/// A REGISTERED TE WITH NO PLAYER IS ALSO SAID ONCE, and it is a different
/// statement from an unknown handle: it means the table paired, the event
/// arrived, the blob decoded, and nobody has written the C# that draws it. That
/// is the ordinary state of a C-Phase 3 lane's class before that lane lands,
/// and it must be visible rather than look like nothing happened.
/// </para>
/// </summary>
public sealed class ClientTempEnts
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly TempEntRegistry _registry;
    private readonly Dictionary<string, IClientTempEnt> _players = new(StringComparer.Ordinal);
    private readonly HashSet<int> _unknownHandles = new();
    private readonly HashSet<string> _unplayed = new(StringComparer.Ordinal);

    /// <summary>How many temp entities this side has played since load.</summary>
    public int Played { get; private set; }

    /// <summary>How many arrived that nothing played.</summary>
    public int Dropped { get; private set; }

    /// <summary>
    /// The last blob that played, KEPT, and the class it belonged to.
    ///
    /// <para>
    /// A COPY, because the span is the native shell's own storage and the next
    /// event of the same class decodes into it.
    /// </para>
    ///
    /// <para>
    /// It is here for one reason: "the client played something" is not the same
    /// claim as "the client played the right numbers", and this project's own
    /// memory on verifying a datatable says the round trip needs a value
    /// written on the server and CHECKED on the client — printing it and
    /// comparing by eye has already hidden a failure here for a whole session.
    /// So the last blob survives its call and <c>cs_cl_te_report</c> reads it
    /// back through the same declaration the server packed it with.
    /// </para>
    /// </summary>
    public TempEntType? LastPlayed { get; private set; }

    private byte[] _lastBlob = Array.Empty<byte>();

    /// <summary>The bytes of <see cref="LastPlayed"/>, as they arrived.</summary>
    public ReadOnlySpan<byte> LastBlob => _lastBlob;

    public ClientTempEnts(ILog log, TempEntRegistry registry)
    {

        _log = log;
        _registry = registry;
    }

    /// <summary>Every class name a player is registered for. Read by the report command.</summary>
    public IReadOnlyCollection<string> PlayerNames => _players.Keys;

    /// <summary>
    /// The player registered for a class name, or null if nothing claims it.
    ///
    /// <para>
    /// THE INSTANCE, not just the name, and it exists because a player can now
    /// have something to say that a name cannot: behaviour ledger row C1 closed
    /// by making the blast push on ragdolls real, and "the push is wired" and
    /// "the push has happened" are different claims. <c>cs_cl_te_report</c>
    /// reads the second one off the same instance the client dispatches into —
    /// a report resolving a second instance would print counters that had never
    /// seen an effect, which is a shape this project has twice been misled by.
    /// </para>
    /// </summary>
    public IClientTempEnt? Player(string className)
        => _players.TryGetValue(className, out IClientTempEnt? player) ? player : null;

    /// <summary>
    /// Discovers every <see cref="IClientTempEnt"/> in the assembly and builds
    /// it through the container, so a player can ask for the engine areas it
    /// draws with rather than reaching a static.
    /// </summary>
    public int RegisterAll(Assembly assembly, IServiceLocator services)
    {
        int found = 0;

        foreach (var type in LoadedAssemblies.Types(assembly, _log))
        {
            if (type.IsAbstract || !typeof(IClientTempEnt).IsAssignableFrom(type))
                continue;

            var attribute = type.GetCustomAttribute<ClientTempEntAttribute>();

            if (attribute is null)
            {
                _log.Warning(
                    $"[SourceSharp/Client] {type.Name} implements IClientTempEnt with no " +
                    "[ClientTempEnt] on it, so nothing can route to it. Name the temp " +
                    "entity class it plays.\n");
                continue;
            }

            if (_players.ContainsKey(attribute.ClassName))
            {
                _log.Warning(
                    $"[SourceSharp/Client] two client players claim '{attribute.ClassName}'; " +
                    $"{type.Name} is ignored. One effect, one player.\n");
                continue;
            }

            // Built through the container, so a player asks for the engine
            // areas it draws with rather than reaching a static. A refusal is a
            // warning naming the class and the effect then does not play, which
            // is the same answer ClientStandaloneRegistry gives for the same
            // reason: a half-built player is worse than none.
            object made;

            try
            {
                made = ServiceActivator.For(type).Create(services);
            }
            catch (ServiceResolutionException failure)
            {
                _log.Warning($"[SourceSharp/Client] {type.Name} cannot be built, so " +
                            $"'{attribute.ClassName}' will not play: {failure.Message}\n");
                continue;
            }
            catch (TargetInvocationException failure)
            {
                _log.Warning($"[SourceSharp/Client] {type.Name} threw while being built: " +
                            $"{failure.InnerException?.Message ?? failure.Message}\n");
                continue;
            }

            Add(attribute.ClassName, (IClientTempEnt)made);
            found++;
        }

        return found;
    }

    /// <summary>
    /// Registers one player under the class name it plays, answering false if
    /// something already claims that name.
    ///
    /// <para>
    /// What <see cref="RegisterAll"/> does with what it discovers, exposed
    /// because a test needs a player it built itself: one that resolved from a
    /// container would be testing the wiring as well as the routing and could
    /// not say which of them broke (docs/writing-tests.md).
    /// </para>
    /// </summary>
    public bool Add(string className, IClientTempEnt player)
    {
        if (_players.ContainsKey(className))
            return false;

        _players[className] = player;
        return true;
    }

    /// <summary>
    /// A new level began. Players that remember something about the last one
    /// hear about it.
    ///
    /// <para>
    /// WHAT THIS IS FOR is one sentence in the behaviour ledger:
    /// <see cref="IClientTempEntLevel"/> has the whole argument. In short, a
    /// Part 1 row that is still open must say so ONCE PER LEVEL — not per
    /// event, which would bury it, and not once per process, which a player
    /// joining a second map would never see.
    /// </para>
    ///
    /// <para>
    /// The unknown-handle and nothing-plays-this warnings above are NOT reset
    /// here, on purpose. Those describe a registration mismatch, which cannot
    /// change without a reload; repeating them per level would be noise about a
    /// fact that has not moved.
    /// </para>
    /// </summary>
    public void LevelInit()
    {
        foreach (IClientTempEnt player in _players.Values)
        {
            if (player is IClientTempEntLevel perLevel)
                perLevel.LevelInit();
        }
    }

    /// <summary>
    /// One arrived. The blob is borrowed native memory, valid only for the
    /// duration of this call.
    /// </summary>
    /// <summary>
    /// One raised LOCALLY by a native forward, as the declaration itself.
    ///
    /// <para>
    /// The player is found by the class name the declaration carries, which is
    /// the same string <see cref="Dispatch"/> looks up after decoding a blob —
    /// so a forwarded effect and a received one of the same class reach exactly
    /// the same player. No blob is built: the state was constructed from the
    /// forward's own arguments.
    /// </para>
    /// </summary>
    public bool PlayLocally(TempEntState state)
    {
        string className = TempEntityAttribute.ClassNameOf(state.GetType());

        if (!_players.TryGetValue(className, out IClientTempEnt? player))
        {
            Dropped++;

            if (_unplayed.Add(className))
            {
                _log.Warning(
                    $"[SourceSharp/Client] a native forward raised '{className}' and no managed " +
                    "class plays it. The forward reached C# and the effect is still invisible " +
                    "— write an IClientTempEnt for it.\n");
            }

            return false;
        }

        player.Play(state);
        Played++;
        return true;
    }

    public void Dispatch(int handle, ReadOnlySpan<byte> blob)
    {
        TempEntType? type = _registry.FindByHandle(handle);

        if (type is null)
        {
            Dropped++;

            if (_unknownHandles.Add(handle))
            {
                _log.Warning(
                    $"[SourceSharp/Client] a temp entity arrived on handle {handle}, which " +
                    "this side registered nothing for. Native and managed disagree about " +
                    "what was registered; the registration lines in the console say what " +
                    "each of them thinks.\n");
            }

            return;
        }

        if (!_players.TryGetValue(type.Class.ClassName, out IClientTempEnt? player))
        {
            Dropped++;

            if (_unplayed.Add(type.Class.ClassName))
            {
                _log.Warning(
                    $"[SourceSharp/Client] '{type.Class.ClassName}' arrived and decoded, and " +
                    "no managed class plays it. The table paired and the effect is still " +
                    "invisible — write an IClientTempEnt for it.\n");
            }

            return;
        }

        var reader = new TempEntReader(type, blob);

        player.Play(in reader);
        Played++;

        // AFTER the play, so a player that throws does not leave a blob
        // claiming to be the last thing that happened.
        if (_lastBlob.Length != blob.Length)
            _lastBlob = new byte[blob.Length];

        blob.CopyTo(_lastBlob);
        LastPlayed = type;
    }
}
