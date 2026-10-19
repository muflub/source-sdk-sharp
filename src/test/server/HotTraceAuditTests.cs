using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Pins the rule <c>SOURCESHARP_TRACE_HOT</c> exists for, because nothing else
/// can: a bare <c>SOURCESHARP_TRACE()</c> put back on a per-frame path compiles,
/// links, runs, and passes every other test in this suite.
///
/// <para>
/// What it costs when it goes unnoticed was measured rather than argued.
/// <c>SourceSharp_TraceLine</c> carried a bare trace on a path Phase A11's
/// census reads at 1.573 crossings per server frame, and the shipped default of
/// <c>sv_sourcesharp_verbose</c> is 1, at which a bare trace logs EVERY call. A
/// 2.5-minute <c>dm_lockdown</c> session wrote 33,134 console lines and 26,015
/// of them — 78 % — were that one function. The same session after the demotion
/// writes 3,728. Phase A11 could not use the trace as an instrument at all,
/// because reading it perturbed what it measured.
/// </para>
///
/// <para>
/// Two rules, and the second is the one that survives the next person. The
/// first names the three slots this pass demoted. The second reads A11's own
/// census and demands the same of ANY slot measured on a per-frame path — so a
/// slot that becomes hot later, or a new slot that is born hot, is covered
/// without anyone remembering to add it here.
/// </para>
///
/// <para>
/// Adding a slot to an exclusion list is not the fix. Writing
/// <c>SOURCESHARP_TRACE_HOT()</c> is the fix, and so is an explicit
/// <c>SourceSharp_GetVerbosity()</c> gate for the two sites that want to be
/// quieter still than the hot rule.
/// </para>
/// </summary>
public class HotTraceAuditTests
{
    /// <summary>
    /// The slots this pass demoted, with the measurement that decided each.
    ///
    /// These are pinned by name as well as by census rate because the census is
    /// a snapshot: a later census taken on a map with no breakables would read
    /// the two break slots at zero and quietly stop covering them, and the
    /// reason they are hot is their CALLER — a recursive precache walk and a
    /// <c>for</c> over the piece list — which no per-frame number can express.
    /// </summary>
    private static readonly string[] Demoted =
    {
        "SourceSharp_TraceLine",             // 1.573/frame; 26,015 lines a session
        "SourceSharp_EntityTraceHull",       // PlayerShadowRecovery's fit test; 4,314 lines a session
        // SourceSharp_PrecacheDecal (0.612/frame load) left with its slot:
        // plan_server_native_v2 T4d folded it onto IVEngineServer (DecalPrecache).
        "SourceSharp_PrecacheModel",         // 0.405/frame load; 197 lines a map load
    };

    /// <summary>
    /// Above this many boundary crossings per frame, a bare trace is a line in
    /// every session of every ordinary player and the site must be gated.
    ///
    /// A quarter of a crossing per frame is 16 lines a second at 66 Hz, which
    /// is already more than the whole rest of the log put together: the
    /// surviving traces after this pass total 765 lines in a session of 3,728.
    /// </summary>
    private const double HotPerFrame = 0.25;

    /// <summary>A bare trace: the macro, not preceded by a verbosity test.</summary>
    private static readonly Regex BareTrace =
        new(@"\bSOURCESHARP_TRACE\s*\(\s*\)", RegexOptions.Compiled);

    private static readonly Regex HotTrace =
        new(@"\bSOURCESHARP_TRACE_HOT\s*\(\s*\)", RegexOptions.Compiled);

    private static readonly Regex VerbosityGate =
        new(@"SourceSharp_GetVerbosity\s*\(\s*\)\s*>", RegexOptions.Compiled);

    /// <summary>
    /// A trace that a hand-written verbosity test already guards — the shape two
    /// sites use deliberately, because they want to be quieter than
    /// <c>_HOT</c>: <c>shell_input.cpp</c>'s CreateMove and the client game
    /// system's <c>Update</c> both gate at <c>&gt; 2</c>, and letting them out
    /// at verbosity 2 would bury the session someone turned verbosity 2 on to
    /// read.
    /// </summary>
    private static readonly Regex GatedTrace =
        new(@"SourceSharp_GetVerbosity\s*\(\s*\)\s*>\s*\d+\s*\)\s*(?://[^\n]*)?\s*SOURCESHARP_TRACE\s*\(\s*\)",
            RegexOptions.Compiled);

    /// <summary>
    /// Whether this body logs on every call at the shipped default verbosity.
    ///
    /// The gated sites are removed FIRST rather than the body being tested for a
    /// gate anywhere in it: a function with one properly gated trace and one
    /// bare one is exactly the regression this audit is for, and "does the body
    /// mention GetVerbosity" would call it clean.
    /// </summary>
    private static bool LogsOnEveryCall(string body)
        => BareTrace.IsMatch(GatedTrace.Replace(body, string.Empty));

    [Fact]
    public void TheAuditFindsBodiesToRead()
    {
        string repo = RepoLayout.SdkTree;

        var bodies = NativeBodies(Path.Combine(repo, "src", "sourcesharp", "game"));

        // The failure this whole file exists to prevent is a check that reads
        // nothing and reports green. If the definition regex, the directory
        // layout or the naming convention moves, this is what notices.
        Assert.True(bodies.Count > 100,
            $"the audit parsed native function bodies to read ({bodies.Count} found)");

        var missing = Demoted.Where(d => !bodies.ContainsKey(d)).ToList();

        Assert.True(missing.Count == 0,
            "every demoted slot still has a native definition to audit"
            + (missing.Count > 0 ? $" — missing: {string.Join(", ", missing)}" : ""));
    }

    [Fact]
    public void TheDemotedSlotsCarryNoBareTrace()
    {
        string repo = RepoLayout.SdkTree;

        var bodies = NativeBodies(Path.Combine(repo, "src", "sourcesharp", "game"));
        var offenders = new List<string>();

        foreach (string slot in Demoted)
        {
            if (!bodies.TryGetValue(slot, out var found))
                continue;   // TheAuditFindsBodiesToRead is what fails on this

            foreach ((string path, string body) in found)
                if (LogsOnEveryCall(body))
                    offenders.Add($"{slot} ({Path.GetFileName(path)})");
        }

        Assert.True(offenders.Count == 0,
            "no slot demoted for log spam carries a bare SOURCESHARP_TRACE again"
            + (offenders.Count > 0 ? $" — {string.Join(", ", offenders)}" : ""));
    }

    [Fact]
    public void TheDemotedSlotsStillTraceAtVerbosityTwo()
    {
        string repo = RepoLayout.SdkTree;

        var bodies = NativeBodies(Path.Combine(repo, "src", "sourcesharp", "game"));
        var silent = new List<string>();

        // Demoting is not deleting. The call is still worth seeing when the
        // question is "is this being called at all" — it has to be asked for.
        foreach (string slot in Demoted)
        {
            if (!bodies.TryGetValue(slot, out var found))
                continue;

            foreach ((string path, string body) in found)
                if (!HotTrace.IsMatch(body))
                    silent.Add($"{slot} ({Path.GetFileName(path)})");
        }

        Assert.True(silent.Count == 0,
            "every demoted slot still traces at verbosity 2, through SOURCESHARP_TRACE_HOT"
            + (silent.Count > 0 ? $" — {string.Join(", ", silent)}" : ""));
    }

    /// <summary>
    /// Every slot A11's census measured hot — in either window — must not log
    /// on every call.
    ///
    /// <para>The two name-list facts above cover only the three slots A11's
    /// single session found hot. This is the general rule: the census names
    /// what is hot TODAY, and a future slot promoted into a per-frame caller
    /// is caught by the next <c>tools/slot-census</c> run without anyone
    /// remembering to add it to a hand-written list. The rule that would not
    /// fail on anything is why this file was demoted to a name list in the
    /// first place — the helper had no input committed, so no fact could call
    /// it (docs/todo.md, 2026-10-04). The census is now a committed artifact
    /// like <c>tools/live-gate-budget.tsv</c>: a missing, empty or unreadable
    /// census is a FAILURE, never a pass over an empty list.</para>
    ///
    /// <para>The positive controls are the census itself naming at least one
    /// hot slot, and every hot slot having an auditable native definition —
    /// without those, a census that stopped being produced, a slot renamed
    /// out of the definitions, or a scan that read no files would all report
    /// green while covering nothing.</para>
    /// </summary>
    [Fact]
    public void TheCensusHotSlotsCarryNoBareTrace()
    {
        string census = CensusTsv();

        Assert.True(File.Exists(census),
            $"the slot census is this fact's input and it is missing ({census}); "
            + "regenerate with `tools/slot-census` and commit it");

        var hot = HotSlots(census);

        Assert.True(hot.Count > 0,
            $"the census names at least one hot slot (0 of them above {HotPerFrame}/frame) — "
            + "an all-zero census checks nothing; rerun tools/slot-census on a real run");

        string repo = RepoLayout.SdkTree;

        var bodies = NativeBodies(Path.Combine(repo, "src", "sourcesharp", "game"));
        var offenders = new List<string>();
        var undefined = new List<string>();

        foreach (string slot in hot.Keys)
        {
            if (!bodies.TryGetValue(slot, out var found))
            {
                undefined.Add(slot);
                continue;
            }

            foreach ((string path, string body) in found)
                if (LogsOnEveryCall(body))
                    offenders.Add($"{slot} ({Path.GetFileName(path)})");
        }

        // A hot trace line is printed by the function named in it, so every
        // hot name must be a definition this audit can read. A census naming
        // something with no body means the census, the naming convention or
        // the definition scan has moved — which is exactly the failure
        // TheAuditFindsBodiesToRead was written to notice, for the census's
        // half of this rule.
        Assert.True(undefined.Count == 0,
            "every hot slot in the census has an auditable native definition"
            + (undefined.Count > 0 ? $" — no body found for: {string.Join(", ", undefined)}" : ""));

        Assert.True(offenders.Count == 0,
            "no slot the census measured hot logs on every call at the shipped default verbosity"
            + (offenders.Count > 0 ? $" — {string.Join(", ", offenders)}" : ""));
    }

    [Fact]
    public void TheHotMacroIsStillTheQuietOne()
    {
        string repo = RepoLayout.SdkTree;

        string header = Path.Combine(repo, "src", "sourcesharp", "game", "shared", "shell_diagnostics.h");
        Assert.True(File.Exists(header), $"shell_diagnostics.h is where the macros live ({header})");

        string src = File.ReadAllText(header);
        int at = src.IndexOf("#define SOURCESHARP_TRACE_HOT", StringComparison.Ordinal);

        Assert.True(at >= 0, "shell_diagnostics.h defines SOURCESHARP_TRACE_HOT");

        // The whole demotion rests on this one comparison. An edit that made the
        // macro unconditional would put all three slots back in every player's
        // log and no other test in this tree would notice.
        string body = src.Substring(at, Math.Min(400, src.Length - at));

        Assert.True(VerbosityGate.IsMatch(body),
            "SOURCESHARP_TRACE_HOT still tests SourceSharp_GetVerbosity() before logging");

        Assert.True(body.Contains("> 1", StringComparison.Ordinal),
            "SOURCESHARP_TRACE_HOT still holds its lines back until verbosity 2");
    }

    /// <summary>
    /// The committed census: <c>tools/slot-census.tsv</c>, beside the tool
    /// that writes it, the same shape as <c>tools/live-gate-budget.tsv</c>.
    /// </summary>
    private static string CensusTsv() => RepoLayout.Repo("tools", "slot-census.tsv");

    /// <summary>
    /// Slots A11's census measured above <see cref="HotPerFrame"/> lines per
    /// frame, in either of its two windows.
    ///
    /// <para>Columns: <c>direction</c>, <c>slot</c>, a spare, then the rest
    /// and load rates (the format <c>tools/slot-census</c> writes; its header
    /// names them). A comment or short row is not data. A rate that will not
    /// parse is skipped, not a crash: the file is read as written, and the
    /// census's own self-test is what guarantees its shape — this helper only
    /// has to never invent a hot slot.</para>
    /// </summary>
    private static Dictionary<string, double> HotSlots(string tsv)
    {
        var hot = new Dictionary<string, double>(StringComparer.Ordinal);

        if (!File.Exists(tsv))
            return hot;

        foreach (string line in File.ReadLines(tsv))
        {
            string[] f = line.Split('\t');

            if (f.Length < 5 || f[0] == "direction" || f[0].StartsWith('#'))
                continue;

            if (!double.TryParse(f[3], out double rest) || !double.TryParse(f[4], out double load))
                continue;

            double rate = Math.Max(rest, load);

            if (rate < HotPerFrame)
                continue;

            hot[f[1]] = Math.Max(hot.TryGetValue(f[1], out double had) ? had : 0.0, rate);
        }

        return hot;
    }

    /// <summary>
    /// Every <c>SourceSharp_*</c> definition under the game shims, as name to
    /// (file, body). The same shape SuppressionAuditTests reads, and for the
    /// same reason: only a definition has a body, and the shims declare most of
    /// these twice.
    /// </summary>
    private static Dictionary<string, List<(string Path, string Body)>> NativeBodies(string gameDir)
    {
        var found = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);

        if (!Directory.Exists(gameDir))
            return found;

        var definition = new Regex(
            @"(?m)^[A-Za-z_][\w \*&:<>,]*?\b(SourceSharp_\w+)\s*\([^;{]*?\)\s*(?://[^\n]*)?\r?\n\{",
            RegexOptions.Compiled);

        foreach (string path in Directory.EnumerateFiles(gameDir, "*.cpp", SearchOption.AllDirectories))
        {
            string src = File.ReadAllText(path);

            foreach (Match m in definition.Matches(src))
            {
                string body = BracedBody(src, m.Index + m.Length - 1);

                if (body.Length == 0)
                    continue;

                if (!found.TryGetValue(m.Groups[1].Value, out var list))
                    found[m.Groups[1].Value] = list = new List<(string, string)>();

                list.Add((path, body));
            }
        }

        return found;
    }

    /// <summary>The braced block starting at <paramref name="open"/>, braces included.</summary>
    private static string BracedBody(string src, int open)
    {
        int depth = 0;

        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{')
                depth++;
            else if (src[i] == '}' && --depth == 0)
                return src.Substring(open, i - open + 1);
        }

        return string.Empty;
    }
}
