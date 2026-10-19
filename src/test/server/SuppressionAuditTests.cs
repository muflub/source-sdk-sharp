using System.Reflection;
using System.Text.RegularExpressions;
using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Guards the one rule behind [SuppressGCTransition] that the compiler will
/// never check for us.
///
/// Suppressing the transition means the calling thread stays in COOPERATIVE GC
/// mode for the whole native call. That is what makes the call cheap, and it is
/// also what makes it dangerous: while we are in there the runtime cannot start
/// a collection, cannot suspend the thread, and cannot let managed code run
/// again. So a suppressed native function must resolve a handle, read or write
/// a field, and return — nothing else.
///
/// Nothing about that survives on its own. The functions live in C++, the
/// attribute lives in C#, and a suppressed function is one Warning() away from
/// being wrong in a way that shows up as a hang or a heap corruption a hundred
/// frames later. So the audit runs every build: it reads the native definition
/// of every suppressed slot and fails on anything that logs, allocates, blocks,
/// or can reach back into managed code.
///
/// Removing a Warning() to silence this test is the fix. Removing the attribute
/// is also the fix. Adding the function name to an exclusion list is not.
/// </summary>
public class SuppressionAuditTests
{
    /// <summary>
    /// What may not appear in the body of a suppressed native function.
    ///
    /// Three separate reasons, kept in one list because the verdict is the same:
    ///
    /// Logging and asserts (Msg, Warning, SOURCESHARP_TRACE, Assert) do file and
    /// console I/O, which can block for milliseconds on a flushed stream.
    ///
    /// Allocation (new, malloc, strdup) can take the allocator's lock, and a
    /// lock held by a thread the runtime cannot suspend is a deadlock against
    /// any other thread trying to collect.
    ///
    /// And anything that spawns, removes, damages or fires an input at an entity
    /// (CreateEntityByName, UTIL_Remove, AcceptInput, FireOutput) can reach a
    /// native hook that calls BACK into managed — re-entering the runtime from a
    /// frame that suppressed the transition, which is explicitly not allowed.
    ///
    /// MOVING one is in that last group and was not, which cost a session: this
    /// audit reads a suppressed function's own body, and
    /// SourceSharp_EntitySetOrigin's body is a single call to SetAbsOrigin. The
    /// re-entry is two levels below it — SetAbsOrigin composes through
    /// SetAbsTransform, which ends in TouchTriggers, which tells a managed
    /// trigger what walked into it. So the setters are named here even though
    /// they look like the field writes this rule permits.
    /// </summary>
    private static readonly Regex Forbidden = new(
        @"\b(Msg|Warning|DevMsg|DevWarning|ConMsg|Error|Assert|AssertMsg|"
        + @"SOURCESHARP_TRACE|SOURCESHARP_TRACE_MSG|SOURCESHARP_TRACE_HOT|SOURCESHARP_STUB|SOURCESHARP_STUB_NAMED|"
        + @"malloc|calloc|realloc|free|strdup|Q_strdup|"
        + @"CreateEntityByName|DispatchSpawn|UTIL_Remove|AcceptInput|FireOutput|"
        + @"SetAbsOrigin|SetAbsAngles|SetAbsTransform|SetLocalOrigin|SetLocalAngles|Teleport|TouchTriggers|"
        // PARENTING, for exactly the reason the setters above are named, one
        // level deeper. This audit reads a suppressed function's own body, and
        // SourceSharp_EntitySetLocalTransform's body does not contain
        // SetAbsTransform — it calls StandaloneHierarchy_SetLocal, which calls
        // SetAbsTransform, which ends in TouchTriggers, which calls back into
        // managed. Linking a parent is worse still: it composes the child
        // immediately, so one call can move a whole subtree and touch a trigger
        // with each one.
        + @"StandaloneHierarchy_SetParent|StandaloneHierarchy_SetParentEx|"
        + @"StandaloneHierarchy_ClearParent|StandaloneHierarchy_SetLocal|"
        + @"StandaloneHierarchy_ParentMoved|StandaloneHierarchy_RunFrame|"
        + @"SourceSharp_GetManagedApi|ThreadSleep|Plat_FloatTime)\b|\bnew\s",
        RegexOptions.Compiled);

    //-------------------------------------------------------------------------
    // The audit, done once. Reading every .cpp under the game shims is the
    // expensive half and all three facts want the same answer.
    //-------------------------------------------------------------------------

    /// <summary>What the audit found: the slots with no native body, and the ones that misbehave.</summary>
    private sealed record Audit(
        IReadOnlyList<string> Suppressed,
        string? GameDir,
        IReadOnlyList<string> Undefined,
        IReadOnlyList<string> Offenders);

    private static readonly Lazy<Audit> Result = new(Run);

    private static Audit Run()
    {
        var suppressed = SuppressedSlots();
        string? gameDir = FindGameDir();

        if (gameDir is null)
            return new Audit(suppressed, null, Array.Empty<string>(), Array.Empty<string>());

        var bodies = NativeBodies(gameDir);

        var undefined = new List<string>();
        var offenders = new List<string>();

        var retired = RetiredSlots(gameDir);

        foreach (string slot in suppressed)
        {
            if (!bodies.TryGetValue("SourceSharp_" + slot, out var found))
            {
                // A RETIRED SLOT HAS NO BODY BY DEFINITION, and that is the one
                // way of having no body that is not this audit's business. Its
                // field stays in the ABI table until the pass that renumbers
                // once and removes every marked slot together, so "the C++ was
                // renamed and this audit quietly stopped covering it" — the
                // thing the check below is for — cannot be what happened.
                //
                // Read out of the NATIVE HEADER rather than from a list in this
                // file, because docs/abi-retirements.md says in as many words
                // that the marker in sourcesharp.h decides whether a slot is
                // pending and that the ledger table is not the worklist. A list
                // here would be a third copy to keep in step, and it would go
                // stale in the safe-looking direction: a slot un-retired and
                // re-implemented would stay waived for ever.
                if (!retired.Contains(slot))
                    undefined.Add(slot);

                continue;
            }

            foreach ((string path, string body) in found)
            {
                var bad = Forbidden.Matches(body)
                    .Select(m => m.Value.Trim())
                    .Distinct()
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList();

                if (bad.Count > 0)
                    offenders.Add($"{slot} ({Path.GetFileName(path)}): {string.Join(", ", bad)}");
            }
        }

        return new Audit(suppressed, gameDir, undefined, offenders);
    }

    [Fact]
    public void TheEngineApiHasSuppressedSlotsToAudit()
    {
        // A count of zero would pass every check below while auditing nothing,
        // which is the failure mode this whole file exists to prevent. If the
        // attributes are ever stripped — or GetModifiedFieldType stops
        // reporting them — this is the check that notices.
        var audit = Result.Value;

        Assert.True(audit.Suppressed.Count > 0, $"engine API has suppressed slots to audit ({audit.Suppressed.Count} found)");
    }

    [Fact]
    public void EverySuppressedSlotHasANativeDefinitionToAudit()
    {
        // A suppressed slot with no native definition is not automatically a
        // bug — the server half of the table is NULL on the client and the
        // other way round — but a slot with no definition on EITHER side means
        // the C++ was renamed and this audit has quietly stopped covering it.
        var audit = Result.Value;

        if (audit.GameDir is null)
        {
            // Skipped rather than failed, for the same reason AbiParityTests
            // skips: the tests must still run from a directory with no source
            // tree beside it. Asserted on the CONDITION rather than on `true`,
            // so a skip is visible as a skip and cannot be confused for a pass
            // of the real check.
            Assert.True(FindGameDir() is null, "native sources found for suppression audit (skipped, not found)");
            return;
        }

        Assert.True(audit.Undefined.Count == 0, $"every suppressed slot has a native definition to audit"
            + (audit.Undefined.Count > 0 ? $" — missing: {string.Join(", ", audit.Undefined)}" : ""));
    }

    /// <summary>
    /// The waiver above reads the native header, and this is the check that it
    /// reads it rather than returning an empty set for ever.
    ///
    /// <para>
    /// An empty set would make the audit STRICTER, not weaker — every
    /// suppressed slot would have to have a body — so this is not guarding
    /// against a silent pass. It guards against a silent FAILURE: the marker's
    /// format drifting would red
    /// <see cref="EverySuppressedSlotHasANativeDefinitionToAudit"/> with a
    /// message about a renamed function, which is the wrong diagnosis and would
    /// send the next person looking in the C++.
    /// </para>
    ///
    /// <para>
    /// It asserts on the SHAPE of what comes back and not on a fixed list. A
    /// lane retiring a slot must not have to edit this test, and the pass that
    /// removes every marked field must not have to either: when the header
    /// carries no markers, both halves of this are vacuously true and the
    /// audit falls back to its original rule.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRetirementWaiverReadsTheNativeHeader()
    {
        var audit = Result.Value;

        if (audit.GameDir is null)
        {
            Assert.True(FindGameDir() is null, "native sources found for retirement waiver (skipped, not found)");
            return;
        }

        var retired = RetiredSlots(audit.GameDir);

        // Every marked name has to BE a slot. A marker naming something that is
        // not a field is a typo, and a typo waives nothing while looking like
        // it does.
        // BOTH TABLES, and the single-table version of this was wrong rather than
        // strict. A retirement marker names a slot, and a slot lives in either
        // the engine API (managed calls native; a shim-area method since
        // plan_client_vtables CC-last) or SourceSharpManagedApi (native calls
        // managed). Lane 3e retired thirteen movement and rules
        // slots that are ManagedApi entries -- GetFallDamage, IsTeamplay, the
        // MoveAccelerate family -- and this test read them as naming nothing,
        // which reported a correct tree as a broken one.
        // Since plan_client_vtables CC-last the engine half is the slot table:
        // the struct is gone and each of its slots is a shim-area method.
        // Since plan_client_vtables CE-last there is no ManagedApi struct: a
        // marker can only name a slot-table slot.
        var fields = EngineApiAreasTests.Rows().Where(r => r.Struct == "EngineApi").Select(r => r.Slot)
            .ToHashSet(StringComparer.Ordinal);

        var unknown = retired.Where(n => !fields.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.True(unknown.Count == 0,
            "every retirement marker in host/sourcesharp.h names a slot-table slot"
            + (unknown.Count > 0 ? $" — these name nothing: {string.Join(", ", unknown)}" : ""));
    }

    [Fact]
    public void NoSuppressedNativeFunctionLogsAllocatesBlocksOrReEntersManaged()
    {
        var audit = Result.Value;

        if (audit.GameDir is null)
        {
            Assert.True(FindGameDir() is null, "native sources found for suppression audit (skipped, not found)");
            return;
        }

        Assert.True(audit.Offenders.Count == 0, $"no suppressed native function logs, allocates, blocks or re-enters managed"
            + (audit.Offenders.Count > 0 ? $" — {string.Join("; ", audit.Offenders)}" : ""));
    }

    /// <summary>
    /// Names of the engine slots that carry [SuppressGCTransition].
    ///
    /// Read from metadata rather than by parsing source, so what is audited is
    /// what actually compiled: each slot-table row's field in its area's
    /// forwarder struct (<c>Shim&lt;X&gt;Fwd</c>), which is what the call
    /// goes through since plan_forwarders_everywhere.md (and an area method
    /// since plan_client_vtables CC-last deleted the engine API struct). It has to go through GetModifiedFieldType (see
    /// <see cref="EngineApiAreasTests.IsSuppressed"/>): the convention is a
    /// custom modifier on the signature, and plain FieldType strips those —
    /// asking the unmodified type reports no conventions at all and the audit
    /// would silently cover nothing.
    /// </summary>
    private static List<string> SuppressedSlots()
        => EngineApiAreasTests.Rows()
               .Where(r => r.Struct == "EngineApi")
               .Where(r => EngineApiAreasTests.Layout(r.Interface)?.GetField(r.Slot) is { } f
                        && EngineApiAreasTests.IsSuppressed(f))
               .Select(r => r.Slot)
               .Distinct()
               .ToList();

    /// <summary>
    /// Slots marked retired in the native ABI header, awaiting the pass that
    /// removes them all at once.
    ///
    /// <para>
    /// The marker's form is fixed by the protocol in
    /// <c>docs/abi-retirements.md</c>: the line directly above the field carries
    /// the retirement token, the slot name, the lane and a reason, in
    /// <c>host/sourcesharp.h</c> (whose managed mirror went with the struct at
    /// plan_client_vtables CE-last).
    /// </para>
    ///
    /// <para>
    /// An empty answer is not an error: a tree with nothing retired is the
    /// normal state, and the audit is no weaker for it — every suppressed slot
    /// then has to have a body, which is the original rule.
    /// </para>
    /// </summary>
    private static HashSet<string> RetiredSlots(string gameDir)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        // gameDir is <...>/sourcesharp/game; the header is its sibling's child.
        var parent = Directory.GetParent(gameDir);

        if (parent is null)
            return names;

        string header = Path.Combine(parent.FullName, "host", "sourcesharp.h");

        if (!File.Exists(header))
            return names;

        foreach (Match m in Regex.Matches(File.ReadAllText(header), RetirementMarker))
            names.Add(m.Groups[1].Value);

        return names;
    }

    /// <summary>
    /// The retirement marker, assembled rather than written out, so that this
    /// file does not add itself to the grep the final ABI pass runs to find its
    /// worklist. The addendum to the phase brief asks for exactly that.
    /// </summary>
    private static readonly string RetirementMarker =
        @"//\s*SS_" + @"RETIRED\s+(\w+)\b";

    /// <summary>The native game shims, in the compat farm.</summary>
    private static string? FindGameDir()
    {
        string game = RepoLayout.Sdk("src", "sourcesharp", "game");

        return Directory.Exists(game) ? game : null;
    }

    /// <summary>
    /// Every SourceSharp_* function definition under the game shims, as name to
    /// (file, body). Declarations are skipped — only a definition has a body,
    /// and the shims declare most of these twice.
    /// </summary>
    private static Dictionary<string, List<(string Path, string Body)>> NativeBodies(string gameDir)
    {
        var found = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);

        // A return type, the name, an argument list, then a brace on its own
        // line — which is this tree's style for every definition and none of
        // its declarations.
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
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0)
                return src[open..(i + 1)];
        }

        return string.Empty;
    }
}
