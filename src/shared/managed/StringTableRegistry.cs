using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>One declared table: what was written, and what answers for it.</summary>
public sealed class StringTableDeclaration
{
    internal StringTableDeclaration(string name, int maxEntries, Type contract, IEngineStringTable table)
    {
        Name = name;
        MaxEntries = maxEntries;
        Contract = contract;
        Table = table;
    }

    /// <summary>The table's name as the engine knows it.</summary>
    public string Name { get; }

    /// <summary>The size the server creates it at.</summary>
    public int MaxEntries { get; }

    /// <summary>The declaring interface — what a consumer injects.</summary>
    public Type Contract { get; }

    /// <summary>What is registered under <see cref="Contract"/>.</summary>
    public IEngineStringTable Table { get; }
}

/// <summary>
/// Every <c>[StringTable]</c> declaration, found by walking the loaded types,
/// registered in the container under its own interface, and — on the server —
/// created inside the one window the engine allows a create in.
///
/// <para>
/// <b>Written against <see cref="UserMessageRegistry.RegisterAll"/></b>, which
/// is the house pattern for this: the same <c>LoadedAssemblies.Types</c> walk,
/// the same duplicate-name
/// <c>HashSet&lt;string&gt;</c>, and the same refusal-with-a-reason rather than
/// a skip. What differs is spelled out in <see cref="StringTableAttribute"/>:
/// the declaration is an INTERFACE here, so that registry's
/// <c>IsAbstract || IsInterface</c> rejection inverts into a requirement.
/// </para>
///
/// <para>
/// <b>TWO PASSES, and the split is the design.</b>
/// <see cref="RegisterAll"/> discovers and registers and makes NO engine call
/// at all; <see cref="CreateAll"/> makes the tables and runs only from the
/// server's <c>CreateNetworkStringTables</c>. They are separate because they
/// happen at different times and one of them happens more often:
/// </para>
///
/// <list type="bullet">
/// <item>
/// The container is built at module start. A create there would be outside the
/// engine's window and refused, and a consumer resolved there would have
/// nothing to inject.
/// </item>
/// <item>
/// <b>A <c>cs_sv_reload</c> rebuilds the managed side WITHOUT another
/// <c>CreateNetworkStringTables</c></b> — that is per map load, not per managed
/// reload. So the reloaded module runs <see cref="RegisterAll"/> again and not
/// <see cref="CreateAll"/>, and every declared table works immediately, because
/// what was registered is a NAME and the engine still has the table. An id
/// bound at create time would leave every table unusable until the next map.
/// </item>
/// <item>
/// The client never calls <see cref="CreateAll"/> at all. It registers the same
/// declarations and finds by name on use — see
/// <see cref="IEngineStringTable"/>. That is the owner's ruling: the server
/// only creates, the client only finds, and the client may be lazy.
/// </item>
/// </list>
///
/// <para>
/// <b>The object is never replaced.</b> One <see cref="StringTableHandle"/> per
/// declaration lives as long as the module does, so a consumer that injected it
/// once keeps working across map changes with nothing re-bound — there is no id
/// to re-bind. That is the strongest form of the "prefer re-binding" rule
/// rather than an exception to it.
/// </para>
/// </summary>
public sealed class StringTableRegistry
{
    private readonly IEngineStringTables _tables;
    private readonly IStringTableWriter? _writer;
    private readonly List<StringTableDeclaration> _declared = new();

    /// <summary>
    /// <paramref name="services"/> is asked for the id-addressed engine area,
    /// rather than the area being a constructor parameter, because that area is
    /// <c>internal</c> on purpose: a public constructor naming it would be the
    /// one place mod code could get hold of a <c>TABLEID</c> again.
    /// </summary>
    /// <param name="writes">
    /// False on the client, and it is the owner's ruling made structural: the
    /// server only CREATES and the client only FINDS. A client that could add
    /// to a server table is the desync string tables exist to avoid, so the
    /// side is stated where the module is built rather than asked of a
    /// process-wide static — the same shape as
    /// <c>UserMessageRegistry(receives:)</c>.
    /// </param>
    /// <remarks>
    /// A writing side's adds go through the <see cref="IStringTableWriter"/>
    /// its composition registered (the server's, StringTableWriter in
    /// Server.Interop): the add is a server-only slot, so this shared registry
    /// never names it (P1 run 3).
    /// </remarks>
    public StringTableRegistry(IServiceLocator services, bool writes)
        : this(services.Require<IEngineStringTables>(), writes ? services.Require<IStringTableWriter>() : null,
               services.Require<ILog>())
    {
    }

    internal StringTableRegistry(IEngineStringTables tables, IStringTableWriter? writer, ILog log)
    {
        _tables = tables;
        _writer = writer;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    // Where a declaration problem is said (statics to zero).
    private readonly ILog _log;

    /// <summary>This registry's log, which creating its tables reports through.</summary>
    public ILog Log => _log;

    /// <summary>Every declaration found, in discovery order.</summary>
    public IReadOnlyList<StringTableDeclaration> Declarations => _declared;

    /// <summary>
    /// Finds every <c>[StringTable]</c> interface beside
    /// <paramref name="assembly"/>, builds the one object that answers for it,
    /// and registers that object in <paramref name="services"/> under the
    /// declaring interface. Returns how many. <b>No engine call happens here.</b>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// If a declaration cannot be registered, rather than skipping it — a table
    /// that quietly did not register is a consumer that fails to resolve much
    /// later, with the reason a hundred lines away.
    /// </exception>
    public int RegisterAll(Assembly assembly, ServiceContainer services)
        => Register(LoadedAssemblies.Types(assembly, _log), services);

    /// <summary>
    /// The same work over a list somebody else chose, which is what a test
    /// drives: the walk above spans every loaded assembly, so a fact about a
    /// duplicate name or a malformed declaration could not otherwise say which
    /// declarations it was talking about.
    /// </summary>
    internal int Register(IEnumerable<Type> types, ServiceContainer services)
    {
        _declared.Clear();

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            var attr = type.GetCustomAttribute<StringTableAttribute>();
            if (attr is null)
                continue;

            // A table is one thing on a listen server, where both modules are
            // in one process: the server creates it and the client reads the
            // replicated copy. That both sides declare the same tables is held
            // at build time (ProtocolSetTests), since plan_side_isolation P7.

            if (!type.IsInterface)
                throw new InvalidOperationException(
                    $"[StringTable(\"{attr.Name}\")] on {type.Name}, which is not an interface. " +
                    "A declaration is an interface a consumer injects; the class that answers for " +
                    "it is generated.");

            if (!typeof(IEngineStringTable).IsAssignableFrom(type))
                throw new InvalidOperationException(
                    $"[StringTable(\"{attr.Name}\")] on {type.Name}, which does not extend " +
                    $"{nameof(IEngineStringTable)}. Without it the declaration has no members and " +
                    "nothing a consumer could call.");

            if (attr.MaxEntries <= 0)
                throw new InvalidOperationException(
                    $"[StringTable(\"{attr.Name}\")] on {type.Name} is sized {attr.MaxEntries}. " +
                    "The engine sizes a table when it creates it and there is no resize, so a " +
                    "table with no room in it can never be written to.");

            // The engine registers tables BY NAME, so two declarations sharing
            // one would be two interfaces over one table -- each seeing the
            // other's strings, and the second create answering the first's id.
            if (!seen.Add(attr.Name))
                throw new InvalidOperationException(
                    $"two string tables are declared '{attr.Name}', the second on {type.Name}. " +
                    "The engine registers by name, so one would be reading the other's strings.");

            var binding = BindingFor(type, attr);

            _declared.Add(new StringTableDeclaration(attr.Name, attr.MaxEntries, type, binding));
            services.AddInstance(type, binding);
        }

        // KEY ORDER (plan_side_isolation §2.1): the order tables are created in
        // is their engine TABLEID, so it must not depend on which assembly a
        // declaration was compiled into -- after the shared projects fold into
        // each side, that differs between the two sides.
        _declared.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        _log.Msg($"[SourceSharp] registered {_declared.Count} string table declaration(s)\n");
        return _declared.Count;
    }

    /// <summary>
    /// The id-addressed area this registry binds over. Internal, as the area
    /// is: the server's create path (<c>StringTableCreation.CreateAll</c>, in
    /// SourceSharp.Server.Interop) reaches it, since creating a table is a
    /// server-only slot and this assembly is loaded by both sides.
    /// </summary>
    internal IEngineStringTables Tables => _tables;

    /// <summary>
    /// The object registered under a declaration: the generated class that
    /// implements it, holding the one handle that does the work.
    ///
    /// <para>
    /// <b>Generated, not reflected into being</b> — see
    /// <c>StringTableGenerator</c>. An interface cannot be instantiated, so
    /// something has to make a type for it; the alternative was
    /// <c>DispatchProxy</c>, which emits into a dynamic assembly at runtime and
    /// would be a second thing keeping this module's collectible load context
    /// alive across a <c>cs_sv_reload</c>. A generator also refuses a malformed
    /// declaration at COMPILE time, where reflection could only refuse it at
    /// startup.
    /// </para>
    /// </summary>
    private IEngineStringTable BindingFor(Type contract, StringTableAttribute attr)
    {
        var handle = new StringTableHandle(_tables, attr.Name, _writer, _log);

        foreach (var candidate in contract.Assembly.GetTypes())
        {
            if (candidate.IsAbstract || candidate.IsInterface || !contract.IsAssignableFrom(candidate))
                continue;

            if (Activator.CreateInstance(candidate, handle) is IEngineStringTable made)
                return made;
        }

        throw new InvalidOperationException(
            $"[StringTable(\"{attr.Name}\")] on {contract.Name} has nothing implementing it. " +
            "The implementation is written by SourceSharp.Generators, so the project the " +
            "declaration lives in has to reference it.");
    }
}
