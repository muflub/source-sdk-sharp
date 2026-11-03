using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// The composition-order census shared by both sides'
/// <c>*CompositionNullBakeTests</c>.
///
/// <para>
/// <see cref="ServiceActivator.Create"/> fills an optional constructor
/// parameter from its declared default when nothing is registered for it
/// <em>yet</em> — and a service built during composition keeps that bake for
/// life. That is the silent half of the wiring-order class the
/// <c>*ModuleWiringOrderTests</c> hold for required parameters, which throw:
/// an optional one answers null on every frame afterwards. ILocalArsenal did
/// (a0762adb0): the client's movement composition baked
/// <c>TFClientWeaponOwners.arsenal</c> null and <c>tf_use_min_viewmodels</c>
/// moved nothing.
/// </para>
///
/// <para>
/// The census walks the module's own statement order: every
/// <c>_services.Create/Resolve/ResolveAll</c> in <c>Initialize</c> is a walk
/// whose transitive closure — constructors read under the activator's rule,
/// <see cref="ServiceActivator.For"/>'s widest-public pick — is built at that
/// statement, judging every optional reference parameter against the
/// registrations visible there. A constructor handed the
/// <see cref="IServiceLocator"/> walks registrations in its body (the movement
/// resolver's <c>ResolveAll</c> does), so the scan's lazy services build at
/// the same statement. An explicit <c>Resolve</c> in a body is the author's
/// own null-tolerance, not the activator's silent bake, and is not a walk.
/// A parameter whose service is registered later in the same body, or by the
/// side scan, is a bake; one registered before its walk, or nowhere at all
/// (the engine compose and untyped instance registrations the source cannot
/// name), is not.
/// </para>
/// </summary>
public static class CompositionNullBake
{
    private static (int Open, int Close) BodyOf(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidOperationException($"the source declares no {signature}");

        int open = source.IndexOf('{', at);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return (open, i);
        }

        throw new InvalidOperationException($"{signature} has no closing brace");
    }

    private static string Simple(string typeName)
    {
        int dot = typeName.LastIndexOf('.');
        return dot < 0 ? typeName : typeName[(dot + 1)..];
    }

    // kind AddInstance | AddFactory | Add, first operand, optional second.
    private static readonly Regex Registration =
        new(@"_services\.(?<kind>AddInstance|AddFactory|Add)<\s*(?<a>[A-Za-z_][\w.]*)\s*(?:,\s*(?<b>[A-Za-z_][\w.]*)\s*)?>",
            RegexOptions.Compiled);

    // The hooks factories register under the open generic; a parameter names
    // it the same way.
    private static readonly Regex HooksFactory =
        new(@"_services\.AddFactory<IHooks<[^>]+>>", RegexOptions.Compiled);

    private static readonly Regex Creation =
        new(@"_services\.Create<\s*(?<contract>[A-Za-z_][\w.]*)\s*>\(\)", RegexOptions.Compiled);

    private static readonly Regex ResolveOne =
        new(@"_services\.(?:Require|Resolve)<\s*(?<contract>[A-Za-z_][\w.]*)\s*>\(\)", RegexOptions.Compiled);

    private static readonly Regex ResolveByTypeof =
        new(@"_services\.Resolve\(typeof\((?<contract>[A-Za-z_][\w.]*)\)\)", RegexOptions.Compiled);

    private static readonly Regex ResolveMany =
        new(@"_services\.ResolveAll<\s*(?<contract>[A-Za-z_][\w.]*)\s*>\(\)", RegexOptions.Compiled);

    private static readonly Regex UntypedInstance =
        new(@"_services\.AddInstance\(\s*(?<field>_[A-Za-z]\w*)\s*\)", RegexOptions.Compiled);

    private static readonly Regex FieldDeclaration =
        new(@"private\s+(?<decl>[A-Za-z_][\w.]*?(?:<[^;=]*?>)?\??)\s+(?<field>_[A-Za-z]\w*)\s*[;=]",
            RegexOptions.Compiled);

    private static ConstructorInfo? Chosen(Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length == 0)
            return null;
        var marked = constructors.Where(c => c.GetCustomAttribute<InjectAttribute>() is not null).ToArray();
        if (marked.Length == 1)
            return marked[0];
        return constructors.OrderByDescending(c => c.GetParameters().Length).First();
    }

    private static Type? Named(string contract, Type[] everyType)
    {
        var found = everyType.Where(t => t.Name == Simple(contract)).ToArray();
        if (found.Length > 1)
            found = found.Where(t => t.FullName!.EndsWith("." + contract, StringComparison.Ordinal)).ToArray();
        return found.Length == 1 ? found[0] : null;
    }

    /// <summary>One registry entry, as a walk reads it: placed when, made how.</summary>
    private sealed record Entry(int At, bool Instance, Type? Implementation);

    public sealed record Census(
        List<string> Bakes,
        int ParametersSeen,
        int Walks,
        int Entries,
        string Debug);

    /// <summary>
    /// Walks <paramref name="source"/>'s composition for <paramref name="side"/>.
    /// <paramref name="seed"/> is any type of the calling test assembly; the
    /// graph's types come from its reference tree. <paramref name="rows"/>,
    /// when given, takes one line per optional parameter met.
    /// </summary>
    public static Census Walk(string source, Side side, Type seed, Type modType, List<string>? rows = null)
    {
        var (open, close) = BodyOf(source, "public void Initialize(Side side)");
        var everyType = EveryType(seed, modType);

        // The side scan: one statement registering every [SideService] type
        // and, with it, every contract its As names.
        int scanAt = source.IndexOf("SideServices.Register(", open, StringComparison.Ordinal);
        if (scanAt < 0)
            throw new InvalidOperationException("the module never runs the side scan");

        var sideTypes = everyType
            .Where(t => t.GetCustomAttributes<SideServiceAttribute>().Any(a => a.Side == side))
            .ToArray();
        if (sideTypes.Length < 10)
            throw new InvalidOperationException(
                $"the scan found only {sideTypes.Length} {side} side services " +
                $"of {everyType.Length} types; sample {string.Join(",", sideTypes.Take(5).Select(t => t.Name))} " +
                $"tf2rules={everyType.Any(t => t.Name == "TFMovementRules")}");

        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        void Register(string contract, int at, bool instance, Type? implementation)
        {
            if (contract.Length == 0 || entries.ContainsKey(contract))
                return;
            entries[contract] = new Entry(at, instance, implementation);
        }

        foreach (Match m in Registration.Matches(source))
        {
            // A registration outside the body happened before any walk.
            int at = m.Index >= open && m.Index <= close ? m.Index : -1;
            string a = m.Groups["a"].Value;
            string? b = m.Groups["b"] is { Success: true } v && v.Value.Length > 0 ? v.Value : null;

            if (m.Groups["kind"].Value == "Add" && b is not null)
            {
                Register(Simple(a), at, instance: false, Named(a, everyType));
                Register(Simple(b), at, instance: false, Named(b, everyType));
            }
            else
            {
                Register(Simple(a), at, instance: m.Groups["kind"].Value != "Add",
                         Named(b ?? a, everyType));
            }
        }

        // The hooks factories the module declares, and — for the chains the
        // side registrars add hooks to (AddHooks) — the scan, where every
        // registrar runs. The earlier of the two is when a hooks contract
        // first exists; the registrars cannot precede their scan.
        int hooksAt = scanAt;
        foreach (Match m in HooksFactory.Matches(source))
        {
            if (m.Index < open || m.Index > close)
                continue;
            hooksAt = Math.Min(hooksAt, m.Index);
        }

        Register("IHooks`1", hooksAt, instance: false, null);

        // Untyped instance registrations name a field; the field's declared
        // type is the contract.
        var fieldTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in FieldDeclaration.Matches(source))
            fieldTypes.TryAdd(m.Groups["field"].Value, m.Groups["decl"].Value);
        foreach (Match m in UntypedInstance.Matches(source))
        {
            if (!fieldTypes.TryGetValue(m.Groups["field"].Value, out var declared))
                continue;
            string bare = declared.TrimEnd('?');
            int at = m.Index >= open && m.Index <= close ? m.Index : -1;
            Register(bare.Split('<').Last().Split('.').Last(), at, instance: true, Named(bare, everyType));
        }

        foreach (var type in sideTypes)
        {
            Register(type.Name, scanAt, instance: false, type);
            foreach (var contract in type.GetCustomAttributes<SideServiceAttribute>().First(a => a.Side == side).As)
                Register(contract.Name, scanAt, instance: false, type);
        }

        // The walks, in statement order. A resolve inside a lambda — the anim
        // layers' collect, the hooks factories — runs when the delegate does,
        // not at the statement that declares it; only a statement's own walk
        // judges the order.
        bool InLambda(Match m)
        {
            int lineStart = source.LastIndexOf('\n', Math.Max(m.Index - 1, 0)) + 1;
            return source[lineStart..m.Index].Contains("=>", StringComparison.Ordinal);
        }

        var walks = new List<(string Kind, Type Contract, int At)>();
        void Take(MatchCollection matches, string kind)
        {
            foreach (Match m in matches)
            {
                if (m.Index < open || m.Index > close || InLambda(m))
                    continue;
                var type = Named(m.Groups["contract"].Value, everyType);
                if (type is not null)
                    walks.Add((kind, type, m.Index));
            }
        }

        Take(Creation.Matches(source), "create");
        Take(ResolveOne.Matches(source), "resolve");
        Take(ResolveByTypeof.Matches(source), "resolve");
        Take(ResolveMany.Matches(source), "resolve-all");
        walks.Sort((a, b) => a.At.CompareTo(b.At));

        var built = new HashSet<Type>();
        var bakes = new List<string>();
        int parametersSeen = 0;

        void Build(int at, Type contract)
        {
            // The entry the contract is registered under decides what a
            // resolve does. An entry placed after this walk is not there yet,
            // whatever it is; the activator builds the bare contract.
            if (entries.TryGetValue(contract.Name, out var entry) && entry.At <= at)
            {
                if (entry.Instance)
                    return;
                contract = entry.Implementation ?? contract;
            }

            if (contract.IsInterface || contract.IsAbstract || !built.Add(contract))
                return;

            var constructor = Chosen(contract);
            if (constructor is null)
                return;

            foreach (var parameter in constructor.GetParameters())
            {
                var pt = parameter.ParameterType;
                if (!parameter.HasDefaultValue || parameter.DefaultValue is not null || pt.IsValueType
                    || pt == typeof(string) || typeof(Delegate).IsAssignableFrom(pt))
                    continue;

                parametersSeen++;
                string row = $"{contract.Name}({pt.Name} {parameter.Name})";

                if (entries.TryGetValue(pt.Name, out var service) && service.At <= at)
                {
                    rows?.Add($"{row}: registered by the walk that builds it");
                    if (!service.Instance)
                        Build(at, pt);
                    continue;
                }

                if (service is not null)
                {
                    string note = $"BAKED at walk {at - open}, registered at {service.At - open}";
                    bakes.Add($"{row}: {note}");
                    rows?.Add($"{row}: {note}");
                    continue;
                }

                // No entry the source names: the engine compose registered it
                // before Initialize, or nothing ever does; neither is a bake.
                rows?.Add(
                    pt.Namespace?.StartsWith("SourceSharp.Abi", StringComparison.Ordinal) == true
                        ? $"{row}: an engine area; the compose registered it before Initialize"
                        : $"{row}: no entry in the module; composed elsewhere or absent");
            }

            // A constructor handed the locator walks registrations itself, in
            // its body — the movement resolver's ResolveAll is the paradigm —
            // so every lazy side service builds at the same statement, by the
            // same rule.
            if (!constructor.GetParameters().Any(p => p.ParameterType == typeof(IServiceLocator)))
                return;

            foreach (var lazy in entries.Values)
            {
                if (lazy.Instance || lazy.At > at || lazy.Implementation is not { } impl
                    || !impl.GetCustomAttributes<SideServiceAttribute>().Any()
                    || built.Contains(impl))
                    continue;

                Build(at, impl);
            }
        }

        foreach (var (kind, contract, at) in walks)
        {
            if (kind != "resolve-all")
            {
                Build(at, contract);
                continue;
            }

            foreach (var (name, entry) in entries)
            {
                if (entry.Instance || !contract.IsAssignableFrom(entry.Implementation))
                    continue;
                if (Named(name, everyType) is { } concrete)
                    Build(at, concrete);
            }
        }

        return new Census(bakes, parametersSeen, walks.Count, entries.Count,
            $"walks={walks.Count} entries={entries.Count} sideServices={sideTypes.Length} built={built.Count}");
    }

    /// <summary>
    /// Every type the graph can name: <paramref name="seed"/>'s assembly, the
    /// references it names (the game sides, the TF2 layers) and theirs, by
    /// name, loaded once.
    /// </summary>
    private static Type[] EveryType(params Type[] seeds)
    {
        var loaded = new Dictionary<string, Assembly>();
        foreach (var seed in seeds)
            loaded[seed.Assembly.GetName().Name!] = seed.Assembly;

        for (int i = 0; i < loaded.Count; i++)
        {
            var seen = loaded.Values.ElementAt(i);
            foreach (var reference in seen.GetReferencedAssemblies())
            {
                if (reference.Name is not { } name
                    || !(name.StartsWith("SourceSharp", StringComparison.Ordinal)
                         || name.StartsWith("TF2.", StringComparison.Ordinal))
                    || loaded.ContainsKey(name))
                    continue;
                try
                {
                    loaded[name] = Assembly.Load(name);
                }
                catch
                {
                    // A reference the test does not carry: the graph types it
                    // holds are the ones the module names, and those load.
                }
            }
        }

        var types = new List<Type>();
        foreach (var assembly in loaded.Values)
        {
            try
            {
                types.AddRange(assembly.GetTypes());
            }
            catch
            {
                // A reflection-heavy assembly: the types it offers, the walk
                // takes; the rest no statement names by this name.
            }
        }

        // A mod's shared sources compile into both sides' assemblies, so a
        // closure holding both holds each shared type twice. The copies are
        // reflection-equivalent - one source, two assemblies - so the first
        // stands for both and a name the walk states resolves once.
        return [.. types.Where(t => !t.IsGenericTypeDefinition)
                        .DistinctBy(t => t.FullName, StringComparer.Ordinal)];
    }
}
