extern alias client;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_devapi_interfaces exit criteria, read from the built metadata and from
/// the project files.
///
/// <para>
/// <b>Metadata</b> is every AssemblyRef and the assembly every TypeRef resolves
/// to. <b>Project files</b> are every ProjectReference that links an assembly.
/// The second exists because a constant leaves no trace in metadata (it is
/// inlined), and §5 measured const-only couplings: a dev API that read only a
/// <c>const</c> of the Abi would pass the metadata rule.
/// </para>
///
/// <para>
/// <b>Pending.</b> While the dev API halves still reached the Abi and the
/// contracts, those references were listed in <see cref="Pending"/>; a pending
/// entry no longer observed fails, so the list could only shrink. It is empty
/// since P3b, and stays as the mechanism for any future transition.
/// </para>
/// </summary>
public class DebugApiClosureTests
{
    private const string ServerInterface = "Debug.Api.Server.Interfaces";
    private const string ClientInterface = "Debug.Api.Client.Interfaces";
    private const string DebugApiShared = "Debug.Api.Shared";
    private const string DebugApiServer = "Debug.Api.Server";
    private const string DebugApiClient = "Debug.Api.Client";

    /// <summary>What each assembly may reference besides System.*, once the plan is done.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Allowed = new Dictionary<string, string[]>
    {
        [ServerInterface] = [],
        [ClientInterface] = [],
        [DebugApiShared] = [],
        [DebugApiServer] = [ServerInterface, DebugApiShared],
        [DebugApiClient] = [ClientInterface, DebugApiShared],
    };

    /// <summary>
    /// References the plan has not removed yet (P2 to P4). An entry names an
    /// assembly in metadata and a project file in a csproj.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Pending = new Dictionary<string, string[]>
    {
        // Empty since P3b (2026-10-02): both dev API halves reference only
        // System.*, their Interface and Debug.Api.Shared.
    };

    private static readonly IReadOnlyDictionary<string, string> Projects = new Dictionary<string, string>
    {
        [ServerInterface] = "src/debug_api/interfaces/Debug.Api.Server.Interfaces.csproj",
        [ClientInterface] = "src/debug_api_client/interfaces/Debug.Api.Client.Interfaces.csproj",
        [DebugApiShared] = "src/debug_api_shared/Debug.Api.Shared.csproj",
        [DebugApiServer] = "src/debug_api/Debug.Api.Server.csproj",
        [DebugApiClient] = "src/debug_api_client/Debug.Api.Client.csproj",
    };

    /// <summary>The game: nothing here may reference the dev API or its shared code.</summary>
    private static readonly IReadOnlyDictionary<string, string> GameProjects = new Dictionary<string, string>
    {
        ["SourceSharp.Abi"] = "src/abi/SourceSharp.Abi.csproj",
        ["SourceSharp.Shared.Interop"] = "src/shared/interop/SourceSharp.Shared.Interop.csproj",
        ["SourceSharp.Server"] = "src/server/managed/SourceSharp.Server.csproj",
        ["SourceSharp.Server.Interop"] = "src/server/interop/SourceSharp.Server.Interop.csproj",
        ["SourceSharp.Client"] = "src/client/managed/SourceSharp.Client.csproj",
        ["SourceSharp.Client.Interop"] = "src/client/interop/SourceSharp.Client.Interop.csproj",
    };

    private static readonly string[] DebugApiAssemblies =
        [DebugApiShared, DebugApiServer, DebugApiClient, "SourceSharp.DevApi.Contracts"];

    // ---- readers ----------------------------------------------------------

    internal static string DllOf(string name)
    {
        string path = Path.Combine(Path.GetDirectoryName(typeof(DebugApiClosureTests).Assembly.Location)!, name + ".dll");
        Assert.True(File.Exists(path), $"{path} was not found; a check that read nothing would pass");
        return path;
    }

    private static string ScopeOf(MetadataReader md, TypeReferenceHandle h)
    {
        EntityHandle scope = md.GetTypeReference(h).ResolutionScope;
        while (scope.Kind == HandleKind.TypeReference)
            scope = md.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
        return scope.Kind == HandleKind.AssemblyReference
            ? md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name)
            : "";
    }

    /// <summary>Every assembly the file names: its AssemblyRefs, and each TypeRef's resolution scope.</summary>
    internal static (SortedSet<string> AssemblyRefs, SortedSet<string> TypeRefScopes) ReferencesOf(string dll)
    {
        using var stream = File.OpenRead(dll);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        var assemblies = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var h in md.AssemblyReferences)
            assemblies.Add(md.GetString(md.GetAssemblyReference(h).Name));

        var scopes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var h in md.TypeReferences)
        {
            string scope = ScopeOf(md, h);
            if (scope.Length > 0)
                scopes.Add(scope);
        }

        return (assemblies, scopes);
    }

    /// <summary>The TypeRefs that resolve to <paramref name="target"/>, nested ones included.</summary>
    internal static int TypeRefsInto(string dll, string target)
    {
        using var stream = File.OpenRead(dll);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        return md.TypeReferences.Count(h => ScopeOf(md, h) == target);
    }

    /// <summary>The file names of every ProjectReference that links an assembly (analyzers excluded).</summary>
    internal static SortedSet<string> ProjectReferencesOf(string csprojXml)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var e in XDocument.Parse(csprojXml).Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            if (string.Equals((string?)e.Attribute("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase))
                continue;
            string include = ((string?)e.Attribute("Include") ?? "").Replace('\\', '/');
            names.Add(Path.GetFileNameWithoutExtension(include));
        }

        return names;
    }

    private static string ReadProject(string relative)
    {
        string path = RepoLayout.Repo(relative.Split('/'));
        Assert.True(File.Exists(path), $"{relative} was not found; a check that read nothing would pass");
        return File.ReadAllText(path);
    }

    internal static bool IsSystem(string name) =>
        name == "System" || name.StartsWith("System.", StringComparison.Ordinal);

    /// <summary>
    /// What breaks the rule: names that are neither System.* nor allowed nor
    /// pending, plus pending names no longer observed (the list must shrink).
    /// </summary>
    internal static List<string> Violations(IEnumerable<string> observed, IReadOnlyCollection<string> allowed,
                                            IReadOnlyCollection<string> pending)
    {
        var seen = observed.ToHashSet(StringComparer.Ordinal);
        var bad = seen.Where(n => !IsSystem(n) && !allowed.Contains(n) && !pending.Contains(n))
                      .Select(n => "not allowed: " + n).ToList();
        bad.AddRange(pending.Where(p => !seen.Contains(p)).Select(p => "pending but gone, delete it: " + p));
        return bad;
    }

    private static string[] PendingFor(string assembly) => Pending.TryGetValue(assembly, out var p) ? p : [];

    // ---- the exit-criteria rules -------------------------------------------

    public static TheoryData<string> Ruled => new(Allowed.Keys);

    [Theory]
    [MemberData(nameof(Ruled))]
    public void Its_metadata_references_only_what_the_plan_allows(string assembly)
    {
        var (refs, scopes) = ReferencesOf(DllOf(assembly));

        // The stimulus reaches the subject: every assembly here references at
        // least System.Runtime, so an empty set means the reader read nothing.
        Assert.NotEmpty(refs);

        Assert.Empty(Violations(refs, Allowed[assembly], PendingFor(assembly)));
        // A TypeRef resolves through an AssemblyRef, so pending names gone from
        // the TypeRefs are judged by the AssemblyRef line above, not here.
        Assert.DoesNotContain(Violations(scopes, Allowed[assembly], PendingFor(assembly)),
                              v => v.StartsWith("not allowed", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Ruled))]
    public void Its_project_references_only_what_the_plan_allows(string assembly)
    {
        var refs = ProjectReferencesOf(ReadProject(Projects[assembly]));
        Assert.Empty(Violations(refs, Allowed[assembly], PendingFor(assembly)));
    }

    [Fact]
    public void Each_debug_api_half_links_its_interface_and_the_shared_code()
    {
        // Retention: the allowed links are really there, so the rule above is
        // about projects that are wired, not ones that link nothing.
        Assert.Superset(new SortedSet<string> { ServerInterface, DebugApiShared },
                        ProjectReferencesOf(ReadProject(Projects[DebugApiServer])));
        Assert.Superset(new SortedSet<string> { ClientInterface, DebugApiShared },
                        ProjectReferencesOf(ReadProject(Projects[DebugApiClient])));
        Assert.Contains(ServerInterface, ProjectReferencesOf(ReadProject(GameProjects["SourceSharp.Server"])));
        Assert.Contains(ClientInterface, ProjectReferencesOf(ReadProject(GameProjects["SourceSharp.Client"])));
    }

    public static TheoryData<string> Game => new(GameProjects.Keys);

    [Theory]
    [MemberData(nameof(Game))]
    public void No_game_assembly_references_the_debug_api(string assembly)
    {
        var (refs, scopes) = ReferencesOf(DllOf(assembly));
        Assert.NotEmpty(refs);
        Assert.DoesNotContain(refs.Concat(scopes), DebugApiAssemblies.Contains);

        Assert.DoesNotContain(ProjectReferencesOf(ReadProject(GameProjects[assembly])), DebugApiAssemblies.Contains);
    }

    /// <summary>
    /// P2's progress, as a ceiling that may only fall: the TypeRefs Debug.Api.Server
    /// still makes into the Abi. Each P2 group lowers it; P4 takes it to zero.
    /// Measured with this reader at c876026: 60 (59 top-level types and
    /// ReloadStore.ReloadTally). Lowered by each group:
    /// G10 (LocalCommandLine) 59, G9 (Log) 58, G8 58 (its types are still
    /// named by G7 and G11), G4 (Output, ScriptedEntity) 56, G3 (the five
    /// Scripted*Info records) 51, G7 (ReloadStore, ReloadTally, ReloadCarrier,
    /// ServiceInfo) 47, G6 (NetTrafficReport, NetTrafficClass, NetTrafficField;
    /// NetTrafficSampler was const-only) 44, G1 (ConVarBase, ConCommandInfo,
    /// ConVarFlagNames, ConVarFlags, IEngineConsole, NativeConsole,
    /// NativeConsoleEntry) 37, G2 (the six engine areas and the clock,
    /// IServiceLocator, its Require extension, EntityHandle, Vector,
    /// DamageType) 26, G5 (the seventeen net schema, tier, refusal, payload and
    /// enum types) 9, G11 (PredictionErrors, BuildInfo, BuildHalf, BuildStamp,
    /// Side) 4. The four left are the channel mirror's ring and record types,
    /// which P4 moves to Debug.Api.Shared with the codec. P4c (the mirror and the
    /// codec in Debug.Api.Shared) 0.
    /// </summary>
    private const int DebugApiServerAbiTypeRefCeiling = 0;

    /// <summary>
    /// The same for Debug.Api.Client (9 at c876026); P3 and P4 lower it. P3's log,
    /// build and command-line hooks: 3 (the prediction ring, its record and
    /// PredictionErrors). P4c 5, for one step (2026-10-02): until the
    /// prediction hook (P3b), the client maps its ring's records onto the
    /// codec's wire records, which names PredictionFieldError and
    /// PredictionComponent too. P3b (the prediction hook) 0.
    /// </summary>
    private const int DebugApiClientAbiTypeRefCeiling = 0;

    [Fact]
    public void The_debug_api_names_no_more_abi_types_than_its_ceiling()
    {
        Assert.InRange(TypeRefsInto(DllOf(DebugApiServer), "SourceSharp.Abi"), 0, DebugApiServerAbiTypeRefCeiling);
        Assert.InRange(TypeRefsInto(DllOf(DebugApiClient), "SourceSharp.Abi"), 0, DebugApiClientAbiTypeRefCeiling);
    }

    // ---- fail-first arms: each check reports a planted reference -------------

    [Fact]
    public void The_metadata_check_sees_a_game_reference()
    {
        // This test assembly references SourceSharp.Server (the typeof keeps
        // the reference), so it stands in for a dev API that names the game.
        Assert.NotNull(typeof(SourceSharp.Server.ServerModule));
        var (refs, scopes) = ReferencesOf(typeof(DebugApiClosureTests).Assembly.Location);

        Assert.Contains("not allowed: SourceSharp.Server", Violations(refs, [], []));
        Assert.Contains("not allowed: SourceSharp.Server", Violations(scopes, [], []));
    }

    [Fact]
    public void The_typeref_count_sees_references_into_an_assembly()
    {
        Assert.NotNull(typeof(SourceSharp.Server.ServerModule));
        Assert.True(TypeRefsInto(typeof(DebugApiClosureTests).Assembly.Location, "SourceSharp.Server") > 0);
        Assert.Equal(0, TypeRefsInto(DllOf(ServerInterface), "SourceSharp.Abi"));
    }

    [Fact]
    public void The_project_check_sees_a_planted_reference()
    {
        string clean = ReadProject(Projects[ServerInterface]);
        Assert.Empty(Violations(ProjectReferencesOf(clean), [], []));

        string planted = clean.Replace("</Project>",
            "<ItemGroup><ProjectReference Include=\"..\\..\\abi\\SourceSharp.Abi.csproj\" /></ItemGroup></Project>",
            StringComparison.Ordinal);
        Assert.NotEqual(clean, planted);
        Assert.Contains("not allowed: SourceSharp.Abi", Violations(ProjectReferencesOf(planted), [], []));
    }

    [Fact]
    public void The_project_check_ignores_an_analyzer_but_not_an_assembly()
    {
        const string Xml =
            "<Project><ItemGroup>" +
            "<ProjectReference Include=\"..\\gen\\Gen.csproj\" OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" />" +
            "<ProjectReference Include=\"..\\x\\SourceSharp.Abi.csproj\" />" +
            "</ItemGroup></Project>";
        Assert.Equal(["SourceSharp.Abi"], ProjectReferencesOf(Xml));
    }

    [Fact]
    public void A_pending_reference_that_is_gone_is_reported()
    {
        Assert.Equal(["pending but gone, delete it: SourceSharp.Abi"],
                     Violations(["System.Runtime"], [], ["SourceSharp.Abi"]));
        Assert.Empty(Violations(["System.Runtime", "SourceSharp.Abi"], [], ["SourceSharp.Abi"]));
    }

    [Fact]
    public void The_game_check_sees_a_debug_api_link()
    {
        // Debug.Api.Server's project links Debug.Api.Shared: the filter the game
        // projects are held to must report that link when run over it.
        Assert.Contains(DebugApiShared,
                        ProjectReferencesOf(ReadProject(Projects[DebugApiServer])).Where(DebugApiAssemblies.Contains));
    }

    [Fact]
    public void The_client_loads_its_debug_api_only_when_asked()
    {
        Assert.Null(client::SourceSharp.Client.DebugApiClientLoader.LoadIfRequested(["hl2_linux64"], TestDebugHooks.Client(), new SourceSharp.Abi.RecordingLog()));

        using var loaded = client::SourceSharp.Client.DebugApiClientLoader.LoadIfRequested(
            ["hl2_linux64", DebugChannelProtocol.ClientParam], TestDebugHooks.Client(), new SourceSharp.Abi.RecordingLog());
        Assert.IsType<DebugChannelClient>(loaded);
    }
}
