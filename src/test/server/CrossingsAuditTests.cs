using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Pins the one thing <see cref="CrossingsTests"/> cannot: that every ABI thunk
/// actually counts itself. The per-thunk rule (each thunk opens by counting its
/// own op, and names no other) is the build's now: SS0070/SS0071,
/// CrossingsThunkAnalyzer. What stays here spans files the analyzer cannot see
/// together: one count per thunk in the entry files, one op per counted thunk
/// or binding, the report thunk uncounted, and the native console commands.
///
/// <para>
/// <b>Why an audit and not a call.</b> The thunks are
/// <c>[UnmanagedCallersOnly]</c>, which the runtime refuses to let managed code
/// invoke at all ("attempted to call an UnmanagedCallersOnly method from managed
/// code"). There is no in-process way to enter one and watch the counter move,
/// so the check that a thunk counts itself has to be made against the SOURCE.
/// A thunk with the increment deleted compiles, links, runs, and passes every
/// other test in this suite — including all 36 in <see cref="CrossingsTests"/>,
/// which exercise the counter and never the thunks — and the only symptom is a
/// number that is quietly too low in the instrument the whole entity port is
/// measured with.
/// </para>
///
/// <para>
/// <b>One exemption, named here rather than left as a gap.</b>
/// <c>CrossingsReport</c> is the thunk that READS the counters, so counting it
/// would put the act of measuring into the measurement — a live gate polling it
/// once a frame would find one crossing a frame that nothing in the game does.
/// An exemption list is not a place to put a thunk somebody could not be
/// bothered to hook; this one is the whole of it and the fact below says so by
/// name.
/// </para>
///
/// <para>
/// <b>THE SECOND EXEMPTION WENT WITH ITS SLOT.</b> <c>BenchmarkNoop</c> was
/// uncounted on purpose — a floor with a counter in it is not a floor — and its
/// sibling <c>BenchmarkTypical</c> was counted on purpose. Lane 1c-sv deleted
/// their only caller (<c>cs_bench_boundary</c>, 0 consumers) and lane 1e-abi
/// removed both ABI slots at version 245, so the two facts that pinned the
/// asymmetry are gone from this file with the thunks they read. They were
/// facts about <c>Entry.cs</c> text, and <c>Thunks().Single(...)</c> on a name
/// that no longer occurs throws rather than passing vacuously — but a fact that
/// can only ever fail is not a claim about the build either, so it is retired
/// rather than left to rot. The reasoning survives in
/// <c>CrossingOp</c>'s own remarks, for whatever prices a crossing next.
/// </para>
/// </summary>
public class CrossingsAuditTests
{
    /// <summary>
    /// The thunks that deliberately do NOT count themselves, with the reason.
    /// See the class remarks.
    /// </summary>
    private static readonly string[] Exempt =
    {
        "CrossingsReport",   // reads the counters; counting it measures the measurement
    };

    /// <summary>
    /// Every <c>[UnmanagedCallersOnly]</c> method in <c>Entry.cs</c>, as
    /// (name, the text of its body from the opening brace on).
    ///
    /// <para>
    /// The body is taken as raw text from the brace rather than brace-matched to
    /// its end, because every fact below is about the FIRST statement and
    /// nothing else. Brace matching over C# with interpolated strings in it is a
    /// parser, and a parser that got one thunk wrong would silently stop
    /// auditing it.
    /// </para>
    /// </summary>
    private static List<(string Name, string Body)> Thunks()
    {
        var found = new List<(string, string)>();
        foreach (string path in EntryPaths())
            found.AddRange(ThunksIn(File.ReadAllText(path)));
        return found;
    }

    /// <summary>The <c>[UnmanagedCallersOnly]</c> methods of one entry file.</summary>
    private static List<(string Name, string Body)> ThunksIn(string text)
    {
        var lines = text.Split('\n');
        var found = new List<(string, string)>();

        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("[UnmanagedCallersOnly", StringComparison.Ordinal))
                continue;

            // The signature is within a few lines of the attribute; the body
            // begins at the first '{' or '=>' at or after it.
            for (int j = i + 1; j < Math.Min(i + 6, lines.Length); j++)
            {
                var m = Regex.Match(lines[j], @"\bstatic\b[^(]*?\b(\w+)\s*\(");
                if (!m.Success)
                    continue;

                string name = m.Groups[1].Value;

                // Expression body on the signature line, or a brace/arrow on one
                // of the lines after it.
                int arrow = lines[j].IndexOf("=>", StringComparison.Ordinal);
                if (arrow >= 0)
                {
                    found.Add((name, lines[j][arrow..]));
                    break;
                }

                var body = new System.Text.StringBuilder();
                for (int k = j; k < Math.Min(j + 6, lines.Length); k++)
                {
                    int brace = lines[k].IndexOf('{');
                    if (brace < 0)
                        continue;

                    // The brace and everything after it, plus the next few lines,
                    // which is more than enough to see a first statement.
                    body.Append(lines[k][brace..]).Append('\n');
                    for (int n = k + 1; n < Math.Min(k + 4, lines.Length); n++)
                        body.Append(lines[n]).Append('\n');
                    break;
                }

                found.Add((name, body.ToString()));
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// The failure this whole file exists to prevent is an audit that reads
    /// nothing and reports green — a renamed file, a moved directory, a changed
    /// attribute spelling. 150 is well under the count and well over anything a
    /// broken parse would produce.
    /// </summary>
    [Fact]
    public void TheAuditFindsThunksToRead()
    {
        // Thunks leave Entry.cs for managed-built bindings in plan_engineapi_vtables
        // Phase E, so the floor is on the two together: what the audit reads.
        var thunks = Thunks();
        var bound = BindingOps();

        Assert.True(thunks.Count + bound.Count > 100,   // 150 until E5 retired thirteen with no area
            $"the audit parsed entries to read ({thunks.Count} thunks in {string.Join(", ", EntryPaths())}, {bound.Count} binding ops)");
        Assert.NotEmpty(bound);
    }

    /// <summary>
    /// The ops a managed-built binding counts (plan_engineapi_vtables E): each
    /// slot method opens with <c>Enter(CrossingOp.X)</c>, the thunk's count
    /// moved with its body.
    /// </summary>
    private static List<string> BindingOps()
    {
        var ops = new List<string>();
        // plan_client_vtables SI/CE: the bindings live in each side's interop assembly.
        foreach (var (dir, sub) in new[] { ("server", "managed"), ("server", "interop"), ("shared", "managed"),
                                          ("client", "interop") })
        {
            string root = RepoLayout.Repo("src", dir, sub);
            foreach (string file in Directory.EnumerateFiles(root, "*Binding.cs", SearchOption.AllDirectories))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\b(?:Enter|[Cc]rossings\.Count)\(CrossingOp\.(\w+)\)"))
                    ops.Add(m.Groups[1].Value);
        }
        return ops;
    }

    /// <summary>
    /// One increment per thunk, and no strays anywhere else in the file: the
    /// per-thunk rules (SS0070/SS0071, at build time) cannot see a second call
    /// of a thunk's own op further down its body, and a thunk that counted
    /// itself twice would report exactly double.
    /// </summary>
    [Fact]
    public void EntryHasExactlyOneCountPerCountedThunk()
    {
        int calls = EntryPaths().Sum(p => Regex.Matches(File.ReadAllText(p), @"\bCrossingOp\.\w").Count);
        int counted = Thunks().Count(t => !Exempt.Contains(t.Name));

        Assert.Equal(counted, calls);
    }

    /// <summary>
    /// The enum and the thunk list are one list kept in two files, so this is
    /// what makes "in the same commit" true rather than hoped for. A thunk added
    /// without its op does not compile; an op left behind by a deleted thunk
    /// compiles fine and reports a permanent zero, which is why the equality is
    /// stated in both directions.
    /// </summary>
    [Fact]
    public void CrossingOpHasOneMemberPerCountedThunk()
    {
        // An op is counted by exactly one entry: an Entry.cs thunk, or a
        // managed-built binding's slot method that replaced one.
        var counted = Thunks().Select(t => t.Name).Where(n => !Exempt.Contains(n))
                              .Concat(BindingOps())
                              .OrderBy(n => n, StringComparer.Ordinal);
        var ops = Enum.GetNames<CrossingOp>().OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(ops, counted);
    }

    /// <summary>
    /// The reader of the counters is not one of the things counted. A live gate
    /// polling the report once a frame would otherwise find one crossing a frame
    /// that nothing in the game does, and every budget would be a budget on the
    /// instrument.
    ///
    /// <para>
    /// The reader left Entry.cs for the client's managed-built
    /// <c>DiagnosticsClientBinding</c> (plan_client_vtables CE9a), as the
    /// server's did for <c>DiagnosticsBinding</c> (E14), so this reads the
    /// binding methods: each must exist and count nothing. The body runs from
    /// the signature to the next member, comments stripped.
    /// </para>
    /// </summary>
    [Fact]
    public void CrossingsReportIsNotCounted()
    {
        foreach (var (side, file) in new[] { ("client", "DiagnosticsClientBinding.cs"), ("server", "DiagnosticsBinding.cs") })
        {
            string path = RepoLayout.Repo("src", side, "interop", file);
            Assert.True(File.Exists(path), $"{path} was not found; a scan that read nothing would pass");
            string text = Regex.Replace(File.ReadAllText(path), @"//[^\n]*", "");

            var body = Regex.Match(text, @"public int CrossingsReport\(byte\* a0, int a1, int a2\)\s*\{(.*?)\n    \}", RegexOptions.Singleline);
            Assert.True(body.Success, $"{file} has no CrossingsReport method to read");
            Assert.Contains("crossings.WriteReport(", body.Groups[1].Value);
            Assert.DoesNotContain("rossings.Count", body.Groups[1].Value);
            Assert.DoesNotContain("Enter(", body.Groups[1].Value);
        }
    }

    /// <summary>
    /// The native console command exists and reaches the slot. Its body cannot
    /// be run from here — it needs an engine — but a command deleted or renamed
    /// leaves the whole native half of this lane unreachable with nothing else
    /// noticing.
    /// </summary>
    [Fact]
    public void TheNativeConsoleCommandsExist()
    {
        string text = File.ReadAllText(
            Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "game", "shared", "shell_diagnostics.cpp"));
        // The SERVER's command is a diagnostic: its body is in
        // shell_diagnostics_debug.cpp, compiled into a debug server.so only.
        string debug = File.ReadAllText(
            Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "game", "shared", "shell_diagnostics_debug.cpp"));

        Assert.Contains("SS_NATIVE_COMMAND( cs_sv_crossings )", debug);
        Assert.Contains("cs_cl_crossings", text);
        Assert.Contains("CrossingsReport", text);
    }

    /// <summary>
    /// The entry files whose <c>[UnmanagedCallersOnly]</c> methods count a
    /// crossing: each side's own entry (plan_side_entries SE2), which the
    /// Bootstrap's ServerEntry and ClientEntry reach through function pointers
    /// once they have made the side's context. The Bootstrap names no Abi type,
    /// so it counts nothing itself.
    /// </summary>
    private static string[] EntryPaths()
    {
        var paths = new[]
        {
            RepoLayout.Repo("src", "server", "interop", "ServerSideEntry.cs"),
            RepoLayout.Repo("src", "client", "interop", "ClientSideEntry.cs"),
        };
        foreach (string p in paths)
            Assert.True(File.Exists(p), $"{p} was not found; an audit that read nothing would pass");
        return paths;
    }
}
