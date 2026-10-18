namespace SourceSharp.Abi;

/// <summary>
/// Puts the real engine into a container, one registration per area.
///
/// <para>
/// Called by each side's module during init, before anything that could ask for
/// one exists. Both sides register the same twenty-two: the interfaces describe
/// the ENGINE, which is the same engine, and a member that only works on one
/// side already said so in its own documentation. Splitting the set per side
/// would mean an entity's constructor telling you which side it runs on, which
/// is exactly the knowledge the parallel hierarchy exists to avoid.
/// </para>
/// </summary>
public static partial class EngineServices
{
    /// <summary>
    /// Every area interface, paired with the real binding for it.
    ///
    /// Reflected over rather than listed by hand, and this is the one place in
    /// the mod where that is the safer choice: a twenty-third area added to
    /// this file and forgotten here would not fail to compile. It would fail at
    /// the first entity that asked for it, in a map, at runtime. The pairing
    /// rule is a name — IEngineFoo is implemented by EngineFoo — and a mismatch
    /// is reported rather than skipped.
    /// </summary>
    /// <summary>
    /// Whether a type is an engine AREA: an <c>IEngine*</c> interface the
    /// container holds one binding for.
    ///
    /// <para>
    /// <b>One exception, and it is a type rather than a naming quirk.</b>
    /// <see cref="IEngineStringTable"/> is a per-table HANDLE — there is one
    /// object per <c>[StringTable]</c> declaration, each carrying a different
    /// name, made by <see cref="StringTableRegistry"/> and registered under the
    /// declaring interface. There is no single <c>EngineStringTable</c> for the
    /// container to hold and there could not be one, because "the string table"
    /// is not a thing; a particular table is. The area behind them all is
    /// <c>IEngineStringTables</c>, plural, which is bound normally.
    /// </para>
    ///
    /// <para>
    /// It is written as one predicate rather than as a filter in each of the
    /// two places that walk the areas, because those two disagreeing is exactly
    /// how an area gets registered that the parity fact cannot see.
    /// </para>
    /// </summary>
    /// <summary>
    /// plan_engineapi_vtables B2: every engine area, each bound to ONE side's
    /// engine API rather than following the thread's active side. What a side's
    /// own container is built with, through its <see cref="ModuleEngine"/>
    /// (plan_abi_hotreload D8: the module is handed its engine rather than
    /// naming a side for a static to look up).
    /// </summary>
    internal static int AddTo(ServiceContainer services, EngineSide side)
    {
        int count = 0;

        foreach (var (contract, binding) in Bindings(side, services))
        {
            services.AddInstance(contract, binding);
            count++;
        }

        // AND THE SIDE'S THREE ROUTES UP INTO ITS REGISTRY (plan_abi_hotreload
        // D7). They were static classes; they are this side's own instances, so
        // the registry the composition builds binds into exactly the routes this
        // side's engine reads, and two compositions over two sides share none.
        // Not engine areas, so not in the count.
        services.AddInstance(side.ShellLess);
        services.AddInstance(side.EntityIo);
        services.AddInstance(side.Removal);

        // The side's surfaceprop table, over its own routing (it was a static
        // that read Engine.Current). Not an engine area either.
        services.AddInstance(side.Surfaces);

        // AND THE SIDE'S LOG (statics to zero: it was the static Log): an ILog
        // over this side's own console, which everything this composition
        // builds is handed by constructor, and the lines that console prints,
        // for the dev API's tap and a console command's capture.
        services.AddInstance(side.Log);
        services.AddInstance(side.Lines);

        // AND THE SIDE'S VPHYSICS FORWARDER TABLES (plan_engine_forwarders.md),
        // which a vphysics service or command is built over. Bound on first use,
        // so registering them asks the engine nothing.
        services.AddInstance(side.VPhysics);

        return count;
    }

    public static bool IsArea(Type contract)
        => contract.IsInterface
        && contract.Name.StartsWith("IEngine", StringComparison.Ordinal)
        && !typeof(IEngineStringTable).IsAssignableFrom(contract);

    /// <summary>
    /// Every engine area paired with the binding class that implements it,
    /// without building any. Paired by name, and reflected over rather than
    /// listed, so an area added with no binding behind it says so here.
    /// </summary>
    internal static IEnumerable<(Type Contract, Type Implementation)> Areas(ILog log)
    {
        var assembly = typeof(EngineServices).Assembly;

        foreach (var contract in assembly.GetTypes())
        {
            if (!IsArea(contract))
                continue;

            var implementation = assembly.GetType($"SourceSharp.Abi.Engine{contract.Name[7..]}");

            if (implementation is null || !contract.IsAssignableFrom(implementation))
            {
                log.Warning($"[SourceSharp] {contract.Name} has no Engine{contract.Name[7..]} binding\n");
                continue;
            }

            yield return (contract, implementation);
        }
    }

    /// <summary>
    /// Every engine area's binding, built.
    ///
    /// <para>
    /// Bound to <paramref name="side"/> when the binding takes one (every
    /// EngineBinding.cs adapter since B2, and since plan_abi_hotreload D8 they
    /// take nothing else). A binding's other
    /// constructor parameters are what its side's composition already holds,
    /// resolved from <paramref name="from"/> (plan_abi_hotreload D7): the
    /// render binding's mirror is the one instance the module also hands its
    /// push handlers, which a constructor of its own could never be. The
    /// longest constructor that can be satisfied wins, and a binding none can
    /// satisfy is a composition error, thrown rather than skipped.
    /// </para>
    /// </summary>
    internal static IEnumerable<(Type Contract, object Binding)> Bindings(EngineSide side, IServiceLocator from)
    {
        foreach (var (contract, implementation) in Areas(side.Log))
            yield return (contract, Build(implementation, side, from));
    }

    private static object Build(Type implementation, EngineSide side, IServiceLocator from)
    {
        var constructors = implementation.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length);

        foreach (var constructor in constructors)
        {
            var parameters = constructor.GetParameters();
            var args = new object?[parameters.Length];
            bool satisfied = true;

            for (int i = 0; i < parameters.Length && satisfied; i++)
            {
                args[i] = parameters[i].ParameterType == typeof(EngineSide)
                    ? side
                    : from.Resolve(parameters[i].ParameterType);

                satisfied = args[i] is not null;
            }

            if (satisfied)
                return constructor.Invoke(args);
        }

        throw new ServiceResolutionException(
            $"{implementation.Name}: no constructor can be satisfied for " +
            $"{side.Side}'s container; register what it takes first");
    }
}
