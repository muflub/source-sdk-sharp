using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>ServerModule.Initialize</c> builds services in statement order, and on
/// the server a contract the engine areas already registered can be
/// REPLACED part-way down (<c>IEngineEffects</c> by <c>EffectsOverTempEnts</c>).
/// A <c>_services.Create&lt;T&gt;()</c> above the replacement does not throw:
/// it silently takes the engine's instance, whose managed-answered members do
/// nothing. The arsenal did exactly that, so every bullet's
/// <c>ImpactEffect</c> went to an empty body and no impact was ever drawn or
/// heard. Held as a fact about the source, as the client's twin is.
/// </summary>
public class ServerModuleWiringOrderTests
{
    private static string Source()
        => File.ReadAllText(RepoLayout.Sdk("src/sourcesharp/managed/SourceSharp.Server/ServerModule.cs"));

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

    private static readonly Regex Call = new(@"^\s*([A-Z]\w*)\(\);", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// DELIBERATE, and by name: <c>ArsenalPlayers</c> decorates the engine's
    /// <c>IEnginePlayers</c> and needs the arsenal to do it, so these, created
    /// before it, hold the undecorated players. Each reads only members the
    /// decorator passes straight through
    /// (<see cref="The_waived_early_players_read_nothing_the_decorator_answers"/>).
    /// DeathNotice was one until 2026-10-26, and read GetActiveWeapon, which
    /// the decorator answers: every gunshot kill was reported as the world's.
    /// </summary>
    internal static readonly HashSet<string> Waived = new(StringComparer.Ordinal)
    {
        "PvsCheckClient(IEnginePlayers)", "Arsenal(IEnginePlayers)", "ImpulseCheats(IEnginePlayers)",
        "PlayerFlashlight(IEnginePlayers)",
    };

    /// <summary>
    /// Every registration Initialize makes, by contract, at the position it
    /// happens: its own statements, and a parameterless private method's
    /// registrations at the line that calls it (ComposeEffects).
    /// </summary>
    private static List<(int At, string Contract)> Registrations(string source, int open, int close)
    {
        var at = new List<(int, string)>();

        foreach (Match r in Registration.Matches(source, open))
        {
            if (r.Index > close)
                break;
            at.Add((r.Index, Simple(r.Groups[1].Value)));
        }

        foreach (Match c in Call.Matches(source, open))
        {
            if (c.Index > close)
                break;

            string signature = $"private void {c.Groups[1].Value}()";
            if (!source.Contains(signature, StringComparison.Ordinal))
                continue;

            var (bodyOpen, bodyClose) = BodyOf(source, signature);
            foreach (Match r in Registration.Matches(source, bodyOpen))
            {
                if (r.Index > bodyClose)
                    break;
                at.Add((c.Index, Simple(r.Groups[1].Value)));
            }
        }

        return at;
    }

    /// <summary>Each (created type, parameter) whose contract Initialize (re)registers after the creation.</summary>
    internal static List<string> LateRegistrations(string source)
    {
        var (open, close) = BodyOf(source, "public void Initialize(Side side)");
        var registrations = Registrations(source, open, close);

        var server = typeof(ServerModule).Assembly;
        var assemblies = server.GetReferencedAssemblies()
            .Where(a => a.Name?.StartsWith("SourceSharp", StringComparison.Ordinal) == true)
            .Select(Assembly.Load)
            .Prepend(server)
            .ToArray();
        var everyType = assemblies.SelectMany(a => a.GetTypes()).ToArray();
        var late = new List<string>();

        foreach (Match m in Creation.Matches(source, open))
        {
            if (m.Index > close)
                break;

            string qualified = m.Groups[1].Value;
            string name = Simple(qualified);
            var types = everyType.Where(t => t.Name == name && !t.IsGenericTypeDefinition).ToArray();
            if (types.Length > 1)
                types = types.Where(t => t.FullName!.EndsWith("." + qualified, StringComparison.Ordinal)).ToArray();
            Assert.True(types.Length == 1, $"Create<{qualified}> names one server type (found {types.Length})");

            foreach (var parameter in Chosen(types[0]).GetParameters())
            {
                string wanted = parameter.ParameterType.Name;

                if (!Waived.Contains($"{name}({wanted})") && registrations.Any(r => r.At > m.Index && r.Contract == wanted))
                    late.Add($"{name}({wanted} {parameter.Name})");
            }
        }

        return late;
    }

    [Fact]
    public void No_created_service_takes_a_contract_registered_or_replaced_below_it()
    {
        var late = LateRegistrations(Source());

        Assert.True(late.Count == 0, "created before its parameter's (re)registration: " + string.Join(", ", late));
    }

    /// <summary>
    /// What a waived early consumer calls on its players is a member
    /// ArsenalPlayers forwards unchanged: the members the decorator answers
    /// itself (those whose body is not a one-line forward to the engine's) are
    /// named in none of their sources.
    /// </summary>
    [Fact]
    public void The_waived_early_players_read_nothing_the_decorator_answers()
    {
        string decorator = File.ReadAllText(RepoLayout.Repo("src", "server", "managed", "ArsenalPlayers.cs"));
        string[] answered = Regex.Matches(decorator, @"public \S+ (\w+)\([^)]*\)(?!\s*=> _inner)")
            .Select(m => m.Groups[1].Value)
            .Where(n => n is not ("ArsenalPlayers"))
            .ToArray();
        Assert.Contains("GetActiveWeapon", answered);
        Assert.Contains("GiveItem", answered);

        foreach (string pair in Waived)
        {
            string type = pair[..pair.IndexOf('(')];
            string file = Directory.EnumerateFiles(RepoLayout.Repo("src", "server", "managed"), type + ".cs", SearchOption.AllDirectories).Single();
            string code = File.ReadAllText(file);
            foreach (string member in answered)
                Assert.False(Regex.IsMatch(code, @"\bplayers\." + member + @"\("), $"{type} calls IEnginePlayers.{member}, which the decorator answers");
        }
    }

    [Fact]
    public void A_creation_moved_above_a_replacement_is_caught()
    {
        // The negative control: a Create of a type taking IEngineEffects, put
        // at the top of Initialize, above EffectsOverTempEnts' replacement.
        string source = Source();
        const string replacement = "_services.AddInstance<IEngineEffects>(new EffectsOverTempEnts(";
        Assert.True(source.Contains(replacement, StringComparison.Ordinal),
                    "the replacement is where the control expects it");

        var (open, _) = BodyOf(source, "public void Initialize(Side side)");
        string moved = source.Insert(open + 1, "\n_services.Create<Arsenal>();\n");

        Assert.Contains("Arsenal(IEngineEffects effects)", LateRegistrations(moved));
    }
}
