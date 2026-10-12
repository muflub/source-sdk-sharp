using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>ClientModule.Initialize</c> builds services in statement order, so a
/// <c>_services.Create&lt;T&gt;()</c> placed before the registration of one of
/// T's constructor parameters throws at load and the client never starts
/// (ShardEffects before IFxRoutineEnvironment did exactly that). A full
/// Initialize needs an engine, so this is held as a fact about the source,
/// with the constructors read by reflection under the activator's own rule.
/// </summary>
public class ClientModuleWiringOrderTests
{
    private static string Source()
        => File.ReadAllText(RepoLayout.Sdk("src/sourcesharp/managed/SourceSharp.Client/ClientModule.cs"));

    private static (int Open, int Close) BodyOf(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the source declares {signature}");

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

    private static readonly Regex Registration =
        new(@"_services\.(?:AddInstance|AddFactory|Add)<\s*([A-Za-z_][\w.]*)\s*[,>]", RegexOptions.Compiled);

    private static readonly Regex Creation =
        new(@"_services\.Create<\s*([A-Za-z_][\w.]*)\s*>\(\)", RegexOptions.Compiled);

    private static ConstructorInfo Chosen(Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var marked = constructors.Where(c => c.GetCustomAttribute<InjectAttribute>() is not null).ToArray();
        if (marked.Length == 1)
            return marked[0];
        return constructors.OrderByDescending(c => c.GetParameters().Length).First();
    }

    /// <summary>Each (created type, parameter) whose registration comes after the creation.</summary>
    internal static List<string> LateRegistrations(string source)
    {
        var (open, close) = BodyOf(source, "public void Initialize(Side side)");

        // Registered anywhere outside Initialize (the engine areas compose first).
        var outside = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Registration.Matches(source))
            if (m.Index < open || m.Index > close)
                outside.Add(Simple(m.Groups[1].Value));

        var firstInside = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in Registration.Matches(source, open))
        {
            if (m.Index > close)
                break;
            firstInside.TryAdd(Simple(m.Groups[1].Value), m.Index);
        }

        var client = typeof(ClientModule).Assembly;
        var assemblies = client.GetReferencedAssemblies()
            .Where(a => a.Name?.StartsWith("SourceSharp", StringComparison.Ordinal) == true)
            .Select(Assembly.Load)
            .Prepend(client)
            .ToArray();
        var everyType = assemblies.SelectMany(a => a.GetTypes()).ToArray();
        var late = new List<string>();
        int checkedPairs = 0;

        foreach (Match m in Creation.Matches(source, open))
        {
            if (m.Index > close)
                break;

            string qualified = m.Groups[1].Value;
            string name = Simple(qualified);
            var types = everyType.Where(t => t.Name == name && !t.IsGenericTypeDefinition).ToArray();
            if (types.Length > 1)
                types = types.Where(t => t.FullName!.EndsWith("." + qualified, StringComparison.Ordinal)).ToArray();
            Assert.True(types.Length == 1, $"Create<{m.Groups[1].Value}> names one client type (found {types.Length})");

            foreach (var parameter in Chosen(types[0]).GetParameters())
            {
                string wanted = parameter.ParameterType.Name;
                if (outside.Contains(wanted) || !firstInside.TryGetValue(wanted, out int registeredAt))
                    continue;

                checkedPairs++;
                if (registeredAt > m.Index)
                    late.Add($"{name}({wanted} {parameter.Name})");
            }
        }

        Assert.True(checkedPairs > 0, "at least one created type takes a service Initialize registers");
        return late;
    }

    [Fact]
    public void Every_created_service_finds_its_parameters_already_registered()
    {
        var late = LateRegistrations(Source());

        Assert.True(late.Count == 0, "registered after the Create that needs it: " + string.Join(", ", late));
    }

    [Fact]
    public void A_creation_moved_above_its_parameter_is_caught()
    {
        // The negative control: the same source with FxRoutineEnvironment's
        // registration moved to the end of Initialize.
        string source = Source();
        const string line = "_services.AddInstance<Effects.IFxRoutineEnvironment>(_services.Create<Effects.FxRoutineEnvironment>());";
        Assert.True(source.Contains(line, StringComparison.Ordinal), "the registration line is where the control expects it");

        string removed = source.Replace(line, "", StringComparison.Ordinal);
        string moved = removed.Insert(BodyOf(removed, "public void Initialize(Side side)").Close, line + "\n");

        Assert.Contains(LateRegistrations(moved), s => s.StartsWith("FxEffects(IFxRoutineEnvironment", StringComparison.Ordinal));
    }
}
