extern alias client;
using System.Reflection;
using System.Runtime.CompilerServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE STATIC-STATE RULE OVER IL: what is left of the census now the build
/// enforces it.
///
/// <para>
/// THE PRIMARY GATE IS THE BUILD: StaticStateAnalyzer (src/analyzers) fails a
/// new mutable static in a runtime assembly (SS0001), a waiver row whose
/// static has gone (SS0002) and a waiver that is neither a constant table nor
/// a null object (SS0003), from the one list,
/// src/analyzers/static-state-waivers.txt. The census's ratchet facts went
/// once the analyzer, run with no waivers, flagged exactly the census's set
/// over the built assemblies (11 = 11, empty diff both ways, 2026-10-04).
/// </para>
///
/// <para>
/// WHAT STAYS, AND WHY: the rule over reflection (<see cref="MutableStaticsOf"/>)
/// is the oracle StaticStateAnalyzerTests compares the analyzer's symbol rule
/// to over a compiled corpus, so the two definitions cannot drift; the covered
/// list, which must equal the analyzer's gated projects; and the Abi's
/// zero, which no waiver may relax.
/// </para>
///
/// <para>
/// WHAT COUNTS as mutable: a static field that is not <c>const</c>, and is either
/// assignable (not <c>readonly</c>; this includes the backing field of a static
/// auto-property) or <c>readonly</c> but of a type whose instances change (a
/// collection, an array, a <c>Random</c>, a class with state). A <c>readonly</c>
/// field of a string, a primitive, an enum, a value type, a delegate, a
/// <c>Type</c>, a lock object or a read-only/frozen/immutable collection
/// interface is a constant and is not listed; an array or a type parameter
/// never is, whatever its name says. Compiler-generated types (lambda caches,
/// state machines) are not the code's own and are skipped; a <c>file</c>-local
/// type, whose CLR name also starts with '&lt;', is the code's own and is not.
/// </para>
///
/// <para>
/// The bootstrap assembly (<c>SourceSharp.Bootstrap</c>, <c>Entry.cs</c>) is not
/// referenced by this project and is not scanned; it is out of scope for the
/// rule's first pass and owns the process-wide handles by design.
/// </para>
/// </summary>
public class StaticStateCensusTests
{
    private static readonly Assembly[] Covered =
    {
        typeof(SourceSharp.Abi.ILog).Assembly,   // Abi itself (ServiceContainer, the old anchor, left for Shared.Interop in P1 run 3)
        typeof(SourceSharp.Server.ServerModule).Assembly,
        typeof(client::SourceSharp.Client.ClientModule).Assembly,
        typeof(Debug.Api.DebugApiServer).Assembly,
        // plan_abi_hotreload Phase 1 moves Abi files, and the statics they
        // hold, into the Interop assemblies; a static that moved out of sight
        // would read as converted. Each side's interop holds the shared
        // interop too (plan_shim_areas_split phase 3).
        typeof(SourceSharp.Server.Interop.FrameBinding).Assembly,
        typeof(client::SourceSharp.Client.Interop.ClientSideEntry).Assembly,
        // plan_devapi_interfaces: the dev API's hook assemblies hold no state.
        typeof(Debug.Api.DebugChannelClient).Assembly,
        Assembly.Load("Debug.Api.Server.Interfaces"),
        Assembly.Load("Debug.Api.Client.Interfaces"),
        // plan_shim_areas_split phase 4: each side's engine seams.
        Assembly.Load("SourceSharp.Server.Interface"),
        Assembly.Load("SourceSharp.Client.Interface"),
        Assembly.Load("Debug.Api.Shared"),
        // plan_client_ui: the UI toolkit and screens reload with the UI's own
        // AssemblyLoadContext, so a static there would survive nothing and
        // leak the old context; they hold none.
        Assembly.Load("SourceSharp.Client.Ui.Toolkit"),
        Assembly.Load("SourceSharp.Client.Ui"),
    };

    private static bool IsConstantType(Type t)
    {
        if (t.IsPrimitive || t.IsEnum || t.IsValueType || t.IsPointer || t.IsFunctionPointer)
            return true;

        if (t == typeof(string) || t == typeof(object) || t == typeof(Type) || t == typeof(Assembly)
            || t == typeof(System.Text.RegularExpressions.Regex)
            || t == typeof(System.Globalization.CultureInfo)
            || t == typeof(Lock))
            return true;

        // An array's slots can be reassigned whatever its element is (its CLR
        // name, "IReadOnlyList`1[]", would pass the prefix test below), and a
        // type parameter is whatever it is instantiated with.
        if (t.IsArray || t.IsGenericParameter)
            return false;

        if (typeof(Delegate).IsAssignableFrom(t))
            return true;

        string name = t.IsGenericType ? t.GetGenericTypeDefinition().Name : t.Name;

        return name.StartsWith("IReadOnly", StringComparison.Ordinal)
            || name.StartsWith("Frozen", StringComparison.Ordinal)
            || name.StartsWith("Immutable", StringComparison.Ordinal)
            || name.StartsWith("IImmutable", StringComparison.Ordinal);
    }

    private static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t is not null)!;
        }
    }

    /// <summary>
    /// A constant table one of this repo's source generators writes into every
    /// type it generates: the var set's descriptor table and default prototype
    /// (ScriptedVarsGenerator) and a networked state's layout
    /// (NetStateGenerator). Each is built once by its initialiser and only
    /// read, and there is one per generated type; listing four hundred of them
    /// by name would bury the
    /// statics a person wrote.
    /// </summary>
    private static bool IsGeneratorConstant(FieldInfo field)
    {
        if (!field.IsInitOnly)
            return false;

        Type declaring = field.DeclaringType!;

        if (typeof(SourceSharp.Abi.ScriptedVarSet).IsAssignableFrom(declaring))
            return field.Name is "s_Descriptors" or "s_Defaults";

        if (declaring.GetCustomAttribute<SourceSharp.Abi.StandaloneNetStateAttribute>() is not null)
            return field.Name == "NetLayout";

        return false;
    }

    /// <summary>The CLR name of a <c>file</c>-local type: <c>&lt;File&gt;F{checksum}__Name</c>.</summary>
    private static readonly System.Text.RegularExpressions.Regex FileLocalName =
        new(@"^<[^>]+>F[0-9A-F]+__", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static bool IsCompilerGenerated(Type t)
    {
        for (Type? at = t; at is not null; at = at.DeclaringType)
        {
            if ((at.Name.StartsWith('<') && !FileLocalName.IsMatch(at.Name))
                || at.GetCustomAttribute<CompilerGeneratedAttribute>() is not null)
                return true;
        }

        return false;
    }

    /// <summary>Every mutable static of the covered assemblies, as <c>Assembly: Namespace.Type.field</c>.</summary>
    internal static SortedSet<string> MutableStatics()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var assembly in Covered)
            found.UnionWith(MutableStaticsOf(assembly.GetName().Name!, TypesOf(assembly)));

        return found;
    }

    /// <summary>The census's rule over some types: what <see cref="MutableStatics"/> applies to each covered assembly.</summary>
    internal static SortedSet<string> MutableStaticsOf(string asm, IEnumerable<Type> types)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            if (IsCompilerGenerated(type))
                continue;

            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public
                                                 | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsLiteral)
                    continue;

                if (field.IsInitOnly && IsConstantType(field.FieldType))
                    continue;

                if (IsGeneratorConstant(field))
                    continue;

                string typeName = (type.FullName ?? type.Name).Replace('+', '.');
                found.Add($"{asm}: {typeName}.{field.Name}");
            }
        }

        return found;
    }

    /// <summary>
    /// The analyzer gates exactly the assemblies this census covers: the
    /// projects Directory.Build.props names in SourceSharpStaticStateGated. An
    /// assembly dropped from either list would be checked by one gate only.
    /// </summary>
    [Fact]
    public void TheAnalyzerGatesTheAssembliesTheCensusCovers()
    {
        string props = File.ReadAllText(RepoLayout.Repo("Directory.Build.props"));
        var match = System.Text.RegularExpressions.Regex.Match(
            props, @"<SourceSharpStaticStateGated>;([^<]*);</SourceSharpStaticStateGated>");
        Assert.True(match.Success, "Directory.Build.props declares SourceSharpStaticStateGated");

        var gated = match.Groups[1].Value.Split(';').Order(StringComparer.Ordinal).ToList();
        var covered = Covered.Select(a => a.GetName().Name!).Order(StringComparer.Ordinal).ToList();

        Assert.True(covered.Count > 0);
        Assert.Equal(covered, gated);
    }

    /// <summary>A mutable static, planted where the census rule is run over it, so the empty answer below is the rule's.</summary>
    private static class PlantedAbiStatic
    {
        internal static int Counter;

        internal static int Touch() => ++Counter;
    }

    /// <summary>
    /// The census reaches the Abi, and finds no static there: every Abi static
    /// became its side state's (SideState), and the one-slot Log the
    /// user-message files printed through is gone (they take the composition's
    /// ILog). The control: the same rule finds the planted one.
    /// </summary>
    [Fact]
    public void NoAbiStaticIsLeft()
    {
        Assert.True(PlantedAbiStatic.Touch() > 0);
        var planted = MutableStaticsOf("Planted", [typeof(PlantedAbiStatic)]);
        Assert.Equal(new[] { "Planted: SourceSharp.Tests.StaticStateCensusTests.PlantedAbiStatic.Counter" }, planted);

        var covered = Covered.Select(a => a.GetName().Name).ToList();
        Assert.Contains("SourceSharp.Abi", covered);

        var abi = MutableStatics().Where(k => k.StartsWith("SourceSharp.Abi: ", StringComparison.Ordinal)).ToList();
        Assert.True(abi.Count == 0, "a mutable static in the Abi: " + string.Join(", ", abi));
    }
}
