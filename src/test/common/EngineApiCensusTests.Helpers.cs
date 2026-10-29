using System.Text.RegularExpressions;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>The members of <see cref="EngineApiCensusTests"/> other test files use, linked into every test project that needs them (plan_side_isolation P6): the facts stay in one project.</summary>
public partial class EngineApiCensusTests
{
    /// <summary>
    /// The server's slots: every method of its shim areas and its console, and
    /// the one slot the root serves. The root's other methods (Acquire,
    /// Release) are how the areas are reached, not slots.
    /// </summary>
    internal static HashSet<string> ServerMethods()
    {
        var names = new HashSet<string>();
        foreach (string header in new[] { "ishim_areas.h", "ishim_console.h" })
        {
            string path = RepoLayout.Repo("src", "shared", "native", header);
            Assert.True(File.Exists(path), $"the server's shim interfaces at {path}");

            int before = names.Count;
            foreach (Match m in Regex.Matches(AbiParityTests.CodeOnly(File.ReadAllText(path)),
                                              @"\bvirtual\s[^;(]*?\b(\w+)\s*\("))
                names.Add(m.Groups[1].Value);
            Assert.True(names.Count > before, $"read the methods of {header}");
        }

        string root = File.ReadAllText(RepoLayout.Repo("src", "shared", "native", "ishim_root.h"));
        Assert.Matches(@"\bvirtual\s[^;(]*?\bAcquireInterface\s*\(", AbiParityTests.CodeOnly(root));
        names.Add("AcquireInterface");
        return names;
    }

    /// <summary>
    /// The client's slots (plan_client_vtables CC-last: it fills no struct):
    /// every method of its own contract, ishim_areas_client.h; every method of
    /// a shared base in ishim_areas.h (a class another class there extends),
    /// which the client implements on its own; its console; and the one slot
    /// the root serves. All hand-kept headers, as the server's are.
    /// </summary>
    internal static HashSet<string> ClientMethods()
    {
        var names = new HashSet<string> { "AcquireInterface" };

        string Code(string header)
        {
            string path = RepoLayout.Repo("src", "shared", "native", header);
            Assert.True(File.Exists(path), $"the client's shim interfaces at {path}");
            return AbiParityTests.CodeOnly(File.ReadAllText(path));
        }

        static IEnumerable<string> Methods(string body)
            => Regex.Matches(body, @"\bvirtual\s[^;(]*?\b(\w+)\s*\(").Select(m => m.Groups[1].Value);

        int before = names.Count;
        names.UnionWith(Methods(Code("ishim_areas_client.h")));
        Assert.True(names.Count > before + 100, "read the client-only methods of ishim_areas_client.h");

        before = names.Count;
        names.UnionWith(Methods(Code("ishim_console.h")));
        Assert.True(names.Count > before, "read the console's methods");

        string areas = Code("ishim_areas.h");
        var bases = Regex.Matches(areas, @":\s*public\s+(\w+)").Select(m => m.Groups[1].Value).ToHashSet();
        // 9 since lane c2-sims took IShimEngine's file reads to the shared base
        // (no server area extends IShimEngine now; the factory below serves it);
        // 8 since plan_server_native_v2 T3 retired IShimStringTablesServer; 7 since
        // IShimWorld emptied (PointContents folded, 2026-10-19); 5 since T6 part 3
        // retired IShimCollision (2026-10-27). A floor for the reader, not a
        // target: the port shrinks it.
        Assert.True(bases.Count >= 5, $"read the shared bases ({bases.Count})");

        // And a shared interface no server area extends (IShimEvents, since
        // plan_side_isolation P2 took its user-message slot to IShimUserMessages):
        // the client serves it all the same, which is what its factory hands out.
        string factory = File.ReadAllText(RepoLayout.Repo("src", "client", "native", "shell_shim_areas_client.cpp"));
        var served = Regex.Matches(factory, @"static_cast<\s*(\w+)\s*\*\s*>\(\s*&areas\.").Select(m => m.Groups[1].Value).ToList();
        Assert.True(served.Count >= 10, $"read the client factory's areas ({served.Count})");
        bases.UnionWith(served);
        foreach (Match c in Regex.Matches(areas, @"abstract_class\s+(\w+)[^{]*\{(.*?)\n\};", RegexOptions.Singleline))
        {
            if (bases.Contains(c.Groups[1].Value))
                names.UnionWith(Methods(c.Groups[2].Value));
        }

        return names;
    }
}
