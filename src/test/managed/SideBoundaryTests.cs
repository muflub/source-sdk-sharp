using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SourceSharp.Bootstrap;
using SourceSharp.Client.Interop;
using SourceSharp.Server.Interop;
using SourceSharp.Shared.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_side_entries SE-last: no boundary function takes a side, the Bootstrap
/// references no SourceSharp.Abi type, and each side's own entry is found by
/// its fixed name.
/// </summary>
public sealed class SideBoundaryTests
{
    //-------------------------------------------------------------------------
    // No boundary function takes a side parameter.
    //-------------------------------------------------------------------------

    /// <summary>A parameter list names a side: a side type, or a parameter called side.</summary>
    private static bool NamesASide(string parameters)
        => Regex.IsMatch(parameters, @"\bSourceSharpSide\b|\b(?:Side|side|nSide|eSide|iSide)\b");

    private static string Native(string file)
    {
        string path = RepoLayout.Repo("src", "host", "native", file);
        Assert.True(File.Exists(path), $"{path} was not found; a scan that read nothing would pass");
        string text = File.ReadAllText(path);
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\n]*", "");
    }

    [Fact]
    public void NoExportInTheNativeHeaderTakesASide()
    {
        string header = Native("sourcesharp.h");
        var exports = Regex.Matches(header, @"SOURCESHARP_EXPORT[^;(]*?\b(SourceSharp_\w+)\s*\(([^)]*)\)")
                           .Select(m => (Name: m.Groups[1].Value, Parameters: m.Groups[2].Value))
                           .ToList();

        // The stimulus reaches the subject: the six per-side entries are read.
        foreach (string side in new[] { "Server", "Client" })
            foreach (string entry in new[] { "Load", "ReloadCheck", "Unload" })
                Assert.Contains(exports, e => e.Name == $"SourceSharp_{entry}{side}");

        Assert.Empty(exports.Where(e => NamesASide(e.Parameters)).Select(e => e.Name));
        Assert.DoesNotContain("SourceSharpSide", header);
    }

    [Fact]
    public void NoHostDelegateTakesASide()
    {
        string host = Native("sourcesharp.cpp");
        var delegates = Regex.Matches(host, @"typedef\s+int32_t\s*\(\s*CORECLR_DELEGATE_CALLTYPE\s*\*\s*(bootstrap_\w+_fn)\s*\)\s*\(([^)]*)\)")
                             .Select(m => (Name: m.Groups[1].Value, Parameters: m.Groups[2].Value))
                             .ToList();

        Assert.Equal(["bootstrap_load_fn", "bootstrap_reload_check_fn", "bootstrap_unload_fn"],
                     delegates.Select(d => d.Name).Order(StringComparer.Ordinal));
        Assert.Empty(delegates.Where(d => NamesASide(d.Parameters)).Select(d => d.Name));
    }

    /// <summary>The [UnmanagedCallersOnly] methods of a type, each with what it takes.</summary>
    private static List<MethodInfo> Entries(Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
               .Where(m => m.GetCustomAttribute<UnmanagedCallersOnlyAttribute>() is not null)
               .ToList();

    private static IEnumerable<string> SideParameters(IEnumerable<MethodInfo> methods)
        => methods.SelectMany(m => m.GetParameters()
            .Where(p => p.ParameterType.IsEnum
                        || (p.Name ?? "").Contains("side", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{m.DeclaringType!.Name}.{m.Name}({p.ParameterType.Name} {p.Name})"));

    [Fact]
    public void NoBootstrapEntryTakesASide()
    {
        var entries = Entries(typeof(ServerEntry)).Concat(Entries(typeof(ClientEntry))).ToList();

        Assert.Equal(["LoadClient", "LoadServer", "ReloadCheckClient", "ReloadCheckServer", "UnloadClient", "UnloadServer"],
                     entries.Select(m => m.Name).Order(StringComparer.Ordinal));
        Assert.Empty(SideParameters(entries));
    }

    [Fact]
    public void NoSideEntryTakesASide()
    {
        var entries = Entries(typeof(ServerSideEntry)).Concat(Entries(typeof(ClientSideEntry))).ToList();

        Assert.Equal(8, entries.Count);
        Assert.Empty(SideParameters(entries));
    }

    //-------------------------------------------------------------------------
    // The Bootstrap references no SourceSharp.Abi type, read from metadata.
    //-------------------------------------------------------------------------

    /// <summary>
    /// What an assembly's metadata says of SourceSharp.Abi: whether it lists the
    /// assembly as a reference, and every type it references from it.
    /// </summary>
    private static (bool References, List<string> Types) AbiIn(Assembly assembly)
    {
        string path = assembly.Location;
        Assert.True(File.Exists(path), $"{path} was not found");

        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader md = pe.GetMetadataReader();

        var abiRefs = md.AssemblyReferences
            .Where(h => md.GetString(md.GetAssemblyReference(h).Name) == "SourceSharp.Abi")
            .Select(h => (EntityHandle)h)
            .ToHashSet();

        var types = new List<string>();
        foreach (TypeReferenceHandle h in md.TypeReferences)
        {
            // Nested types resolve through their enclosing type; walk out to the assembly.
            TypeReference t = md.GetTypeReference(h);
            EntityHandle scope = t.ResolutionScope;
            while (scope.Kind == HandleKind.TypeReference)
                scope = md.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;

            if (abiRefs.Contains(scope))
                types.Add(md.GetString(t.Namespace) + "." + md.GetString(t.Name));
        }

        return (abiRefs.Count > 0, types);
    }

    [Fact]
    public void TheReaderSeesAbiInAnAssemblyThatReferencesIt()
    {
        // The control: the same reader, on the side's shared entry code.
        var (references, types) = AbiIn(typeof(SideLoad).Assembly);

        Assert.True(references, "SourceSharp.Shared.Interop lists SourceSharp.Abi");
        Assert.Contains("SourceSharp.Abi.ManagedVtables", types);
    }

    [Fact]
    public void TheBootstrapReferencesNoAbiType()
    {
        var (references, types) = AbiIn(typeof(Entry).Assembly);

        Assert.False(references, "SourceSharp.Bootstrap lists SourceSharp.Abi among its references");
        Assert.Empty(types);
    }

    //-------------------------------------------------------------------------
    // Each side's own entry is found by its fixed name.
    //-------------------------------------------------------------------------

    [Fact]
    public void EachSidesEntryResolvesByItsFixedName()
    {
        BootSide server = BootSide.ForServer();
        BootSide client = BootSide.ForClient();

        Assert.Equal(typeof(ServerSideEntry).FullName, server.EntryType);
        Assert.Equal(typeof(ServerSideEntry).Assembly.GetName().Name, server.EntryAssembly);
        Assert.Equal(typeof(ClientSideEntry).FullName, client.EntryType);
        Assert.Equal(typeof(ClientSideEntry).Assembly.GetName().Name, client.EntryAssembly);

        Assert.True(SideEntryPoints.Resolve(typeof(ServerSideEntry).Assembly, server.EntryType, server.Name, out string why) is not null, why);
        Assert.True(SideEntryPoints.Resolve(typeof(ClientSideEntry).Assembly, client.EntryType, client.Name, out why) is not null, why);
    }

    [Fact]
    public void AnEntryUnderAnotherNameFailsTheLoadWithTheName()
    {
        BootSide server = BootSide.ForServer();

        Assert.Null(SideEntryPoints.Resolve(typeof(ServerSideEntry).Assembly, server.EntryType + "Renamed", server.Name, out string why));
        Assert.Contains(server.EntryType + "Renamed", why);
        Assert.Contains("fixed name", why);

        // The other side's method names on this side's type: every one missing,
        // each named.
        Assert.Null(SideEntryPoints.Resolve(typeof(ServerSideEntry).Assembly, server.EntryType, "Client", out why));
        Assert.Contains("LoadClient, StampedClient, ReloadCheckClient, UnloadClient", why);
    }
}
