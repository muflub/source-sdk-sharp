using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE GATE ON THE ONE THING THIS DESIGN GAVE UP. A forward hands its arguments
/// across positionally — <c>f[0..2]</c> is an origin because both sides agree it
/// is — so a slot in the wrong place is a WRONG VALUE rather than a compile
/// error. Forty-two typed ABI slots would not have had that problem; one slot
/// and a disk-parity test is the trade, and this is the test.
///
/// <para>
/// It reads the packing out of each <c>shell_te_forwards*.cpp</c> and the unpacking
/// out of <c>TeForwardDispatch.cs</c>, and compares them SLOT BY SLOT — the same
/// shape as the declaration parity tests, which read stock's own <c>.cpp</c> and
/// compare prop by prop.
/// </para>
///
/// <para>
/// WHAT IT CANNOT CATCH, said plainly: two scalars of the same kind swapped.
/// Knowing that <c>f[3]</c> is the radius and <c>f[4]</c> the time means knowing
/// which argument becomes which field, which is the thing being checked — a test
/// that knew it would be a second copy of the mapping and would agree with the
/// first by construction. What it does catch is a slot COUNT that disagrees, a
/// slot nothing reads, a read past the end, and a vector landing anywhere but on
/// its own three floats. The round-trip tests in
/// <see cref="TeForwardDispatchTests"/> cover values; this covers shape.
/// </para>
/// </summary>
public class TeForwardSignatureTests
{
    /// <summary>A slot's kind, which is all that can be compared without the mapping.</summary>
    private enum Slot { Scalar, VectorX, VectorY, VectorZ }

    [Fact]
    public void EveryForwardPacksTheSlotsItsDispatcherArmReads()
    {
        string? anyForward = FindRelative(Path.Combine(
            "src", "sourcesharp", "game", "server", "shell_te_forwards.cpp"));

        string? dispatch = FindRelative(Path.Combine(
            "src", "sourcesharp", "managed", "SourceSharp.Shared", "TempEnts",
            "TeForwardDispatch.cs"));

        RequireSources(anyForward, dispatch);

        string game = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(anyForward)!, ".."));
        var arms = ArmsOf(File.ReadAllText(dispatch));

        // THE FLOOR MOVED WITH ITS SUBJECT. It was 30 while there were 42 ids
        // across seventeen forwarder files; plan_shim.md Phase 1f deleted the
        // twenty-four ids nothing reached and collapsed the files into two, so the
        // population is 18 (20 bodies — TE_Decal and TE_DispatchEffect each pack
        // for one id from two places). A floor above the population is a fact that
        // can only fail, which is not a stricter check but a broken one.
        // The server gc cull (2026-10-01) deleted four forwards nothing called and
        // reserved their ids: 14 ids, 16 bodies then. plan_server_native_v2 T4b
        // reserved three more (TE_Dust, TE_MetalSparks, TE_MuzzleFlash, whose only
        // callers were ss::IEffects methods no server TU called): 11 ids then. T4d
        // reserved TE_Explosion and TE_LargeFunnel (their callers were slots
        // EffectsOverTempEnts had replaced): 9 ids then. T4f reserved TE_BSPDecal
        // and TE_ProjectDecal (the PaintDecal slot, which EffectsOverTempEnts
        // answers itself now; TE_Bubbles stays, the client forwards it): 7 ids now.
        Assert.True(arms.Count >= 7,
            $"only {arms.Count} dispatcher arm(s) were parsed out of TeForwardDispatch.cs. "
          + "The arm pattern stopped matching rather than the arms going away — with an "
          + "empty arm table every comparison below is vacuous and this fact would be "
          + "green having compared nothing");

        var wrong = new List<string>();
        int checkedForwards = 0;

        foreach (string side in new[] { "server", "client" })
        {
            string dir = Path.Combine(game, side);

            if (!Directory.Exists(dir))
                continue;

            foreach (string file in Directory.GetFiles(dir, "shell_te_forwards*.cpp").Order())
            {
                string source = File.ReadAllText(file);
                string name = Path.GetFileName(file);

                foreach ((string id, List<Slot> floats, List<Slot> ints) in PackingsIn(source))
                {
                    string managed = ManagedNameOf(id);

                    if (!arms.TryGetValue(managed, out (List<Slot> F, List<Slot> I) arm))
                    {
                        // An arm that reads its slots with a LOOP is skipped on
                        // both sides -- TeBeamSpline walks sixteen points by
                        // index, so the slots it touches are not literals to
                        // compare. Its bytes are pinned by its own golden test.
                        if (!s_Looping.Contains(managed))
                            wrong.Add($"{name} forwards {id} and TeForwardDispatch has no "
                                    + $"{managed} arm");

                        continue;
                    }

                    checkedForwards++;

                    if (floats.Count != arm.F.Count)
                        wrong.Add($"{name} {id}: packs {floats.Count} float(s), the arm reads "
                                + $"{arm.F.Count}");
                    else if (!floats.SequenceEqual(arm.F))
                        wrong.Add($"{name} {id}: the float slots are laid out differently — "
                                + $"packed [{string.Join(",", floats)}], read [{string.Join(",", arm.F)}]");

                    if (ints.Count != arm.I.Count)
                        wrong.Add($"{name} {id}: packs {ints.Count} int(s), the arm reads "
                                + $"{arm.I.Count}");
                }
            }
        }

        // THE FLOOR MOVED WITH ITS SUBJECT. It was 40 while there were 42 ids
        // across seventeen forwarder files; plan_shim.md Phase 1f deleted the
        // twenty-four ids nothing reached and collapsed the files into two, so the
        // population is 18 (20 bodies — TE_Decal and TE_DispatchEffect each pack
        // for one id from two places). A floor above the population is a fact that
        // can only fail, which is not a stricter check but a broken one.
        // The server gc cull (2026-10-01) deleted four forwards nothing called and
        // reserved their ids: 14 ids, 16 bodies then. plan_server_native_v2 T4b
        // reserved three more (TE_Dust, TE_MetalSparks, TE_MuzzleFlash, whose only
        // callers were ss::IEffects methods no server TU called): 11 ids then. T4d
        // reserved TE_Explosion and TE_LargeFunnel (their callers were slots
        // EffectsOverTempEnts had replaced): 9 ids then. T4f reserved TE_BSPDecal
        // and TE_ProjectDecal (the PaintDecal slot, which EffectsOverTempEnts
        // answers itself now; TE_Bubbles stays, the client forwards it): 7 ids now.
        Assert.True(checkedForwards >= 8,
            $"only {checkedForwards} forward(s) were matched to an arm, which is far fewer than "
          + "there are — the packing or the arm pattern stopped matching rather than agreeing");

        Assert.True(wrong.Count == 0,
            $"{wrong.Count} forward(s) disagree with the arm that unpacks them:\n  "
          + string.Join("\n  ", wrong.Take(12))
          + (wrong.Count > 12 ? $"\n  ... and {wrong.Count - 12} more" : ""));
    }

    /// <summary>
    /// EVERY ARM IS REACHED BY SOMETHING. An arm nothing forwards is dead code
    /// that looks like coverage, and the switch would still compile.
    /// </summary>
    [Fact]
    public void EveryDispatcherArmIsForwardedBySomeNativeEntryPoint()
    {
        string? anyForward = FindRelative(Path.Combine(
            "src", "sourcesharp", "game", "server", "shell_te_forwards.cpp"));

        string? dispatch = FindRelative(Path.Combine(
            "src", "sourcesharp", "managed", "SourceSharp.Shared", "TempEnts",
            "TeForwardDispatch.cs"));

        RequireSources(anyForward, dispatch);

        string game = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(anyForward)!, ".."));

        var forwarded = new HashSet<string>(StringComparer.Ordinal);

        foreach (string side in new[] { "server", "client" })
        {
            string dir = Path.Combine(game, side);

            if (!Directory.Exists(dir))
                continue;

            foreach (string file in Directory.GetFiles(dir, "shell_te_forwards*.cpp"))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(file),
                             @"SourceSharpTe_Forward\(\s*(SOURCESHARP_TE_FWD_\w+)"))
                {
                    forwarded.Add(ManagedNameOf(m.Groups[1].Value));
                }
            }
        }

        // A FORWARD SET THAT CAME BACK EMPTY WOULD MAKE EVERY ARM AN ORPHAN,
        // which fails loudly — but a set that came back nearly empty would
        // report a handful of orphans that are not orphans, and a set the
        // regex stopped matching entirely alongside an arm table that also
        // stopped matching would report ZERO orphans and pass. Pin the floor
        // on both sides so neither can be silently empty.
        // THE FLOOR MOVED WITH ITS SUBJECT. It was 30 while there were 42 ids
        // across seventeen forwarder files; plan_shim.md Phase 1f deleted the
        // twenty-four ids nothing reached and collapsed the files into two, so the
        // population is 18 (20 bodies — TE_Decal and TE_DispatchEffect each pack
        // for one id from two places). A floor above the population is a fact that
        // can only fail, which is not a stricter check but a broken one.
        // The server gc cull (2026-10-01) deleted four forwards nothing called and
        // reserved their ids: 14 ids, 16 bodies then. plan_server_native_v2 T4b
        // reserved three more (TE_Dust, TE_MetalSparks, TE_MuzzleFlash, whose only
        // callers were ss::IEffects methods no server TU called): 11 ids then. T4d
        // reserved TE_Explosion and TE_LargeFunnel (their callers were slots
        // EffectsOverTempEnts had replaced): 9 ids then. T4f reserved TE_BSPDecal
        // and TE_ProjectDecal (the PaintDecal slot, which EffectsOverTempEnts
        // answers itself now; TE_Bubbles stays, the client forwards it): 7 ids now.
        Assert.True(forwarded.Count >= 7,
            $"only {forwarded.Count} native forward id(s) were found across the "
          + "shell_te_fwd_*.cpp. The call pattern stopped matching rather than the "
          + "forwards going away, and an empty forward set makes this fact vacuous");

        var arms = ArmsOf(File.ReadAllText(dispatch));

        Assert.True(arms.Count >= 7,
            $"only {arms.Count} dispatcher arm(s) were parsed out of TeForwardDispatch.cs. "
          + "With an empty arm table there is nothing to be an orphan and this fact would "
          + "be green having checked nothing");

        var orphans = arms.Keys
                          .Where(a => !forwarded.Contains(a))
                          .Order(StringComparer.Ordinal)
                          .ToList();

        Assert.True(orphans.Count == 0,
            $"{orphans.Count} dispatcher arm(s) no native forward reaches: "
          + string.Join(", ", orphans)
          + ". An arm nothing calls is dead code that reads as coverage");
    }

    /// <summary>
    /// THE SOURCES ARE A PRECONDITION, NOT A CONDITION.
    ///
    /// <para>
    /// Both facts used to open with
    /// <c>if (anyForward is null || dispatch is null) { Assert.True(true, "… (SKIPPED)"); return; }</c>.
    /// Measured on 2026-09-01 by moving <c>shell_te_forwards.cpp</c> out of the
    /// tree and re-running: <c>Failed: 0, Passed: 2, Skipped: 0</c>. Not
    /// skipped — <b>passed</b>. Both facts reported success with the file they
    /// exist to read absent from the disk, and a green run therefore proved
    /// nothing about the forwards at all. xUnit's own skip is an attribute or
    /// <c>Assert.Skip</c>; an <c>Assert.True(true)</c> is neither a skip nor a
    /// check, it is coverage-shaped silence.
    /// </para>
    ///
    /// <para>
    /// Unreadable sources now fail, and the failure says which path was looked
    /// for and where the walk started, because "not found" from a test's
    /// working directory is nearly always a harness question rather than a
    /// source question.
    /// </para>
    /// </summary>
    private static void RequireSources([NotNull] string? anyForward,
                                       [NotNull] string? dispatch)
    {
        Assert.True(anyForward is not null,
            "src/sourcesharp/game/server/shell_te_forwards.cpp was not found in "
          + $"{RepoLayout.SdkTree}. This test reads the packing out of the forwards; "
          + "without them it can compare nothing, and a pass would be a pass over an empty "
          + "set");

        Assert.True(dispatch is not null,
            "src/sourcesharp/managed/SourceSharp.Shared/TempEnts/TeForwardDispatch.cs was not "
          + $"found in {RepoLayout.SdkTree}. This test reads the unpacking "
          + "out of it; without it there is nothing to compare the packing against");
    }

    // ---------------------------------------------------------------------
    // Reading the two sides.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The float and int slots each converted forward packs, taken from the two
    /// array initialisers it builds before the call.
    /// </summary>
    private static IEnumerable<(string Id, List<Slot> Floats, List<Slot> Ints)>
        PackingsIn(string source)
    {
        foreach (Match call in Regex.Matches(source,
                     @"SourceSharpTe_Forward\(\s*(SOURCESHARP_TE_FWD_\w+)"))
        {
            // The arrays are the ones immediately above this call, inside the
            // same function: search back to the opening brace of the body.
            int start = source.LastIndexOf("\n{", call.Index, StringComparison.Ordinal);

            if (start < 0)
                continue;

            string body = source[start..call.Index];

            yield return (call.Groups[1].Value,
                          SlotsOf(ArrayBody(body, "float")),
                          SlotsOf(ArrayBody(body, "int")));
        }
    }

    /// <summary>The elements of <c>const float f[] = { … };</c>, or empty.</summary>
    private static string[] ArrayBody(string body, string type)
    {
        Match m = Regex.Match(body, @"(?:const\s+)?" + type + @"\s+\w+\s*\[[^\]]*\]\s*=\s*\{(.*?)\};",
                              RegexOptions.Singleline);

        if (!m.Success)
            return [];

        // TOP-LEVEL COMMAS ONLY. An element can contain a call --
        // MIN( count, BUBBLE_TRAIL_MAX_COUNT ) is one -- and a plain Split
        // turns that single element into two, which reads as a forward packing
        // one more int than its arm consumes. That is the same mistake the
        // generator made reading these files, in the test that checks it.
        var elements = new List<string>();
        int depth = 0, from = 0;
        string text = m.Groups[1].Value;

        for (int j = 0; j < text.Length; j++)
        {
            if (text[j] == '(') depth++;
            else if (text[j] == ')') depth--;
            else if (text[j] == ',' && depth == 0)
            {
                elements.Add(text[from..j]);
                from = j + 1;
            }
        }

        elements.Add(text[from..]);

        return elements.Select(e => Regex.Replace(e, @"//[^\n]*", "").Trim())
                       .Where(e => e.Length > 0)
                       .ToArray();
    }

    /// <summary>
    /// Each element as a slot kind. A vector is three consecutive elements
    /// reading .x, .y and .z of one thing — the shape that must not move,
    /// because a vector on the wrong slots is an effect somewhere else entirely.
    /// </summary>
    private static List<Slot> SlotsOf(string[] elements)
    {
        var kinds = new List<Slot>();

        for (int i = 0; i < elements.Length; i++)
        {
            bool triple = i + 2 < elements.Length
                       && Ends(elements[i], 'x') && Ends(elements[i + 1], 'y')
                       && Ends(elements[i + 2], 'z');

            if (triple)
            {
                kinds.Add(Slot.VectorX);
                kinds.Add(Slot.VectorY);
                kinds.Add(Slot.VectorZ);
                i += 2;
            }
            else
            {
                kinds.Add(Slot.Scalar);
            }
        }

        return kinds;

        // `pos ? pos->x : 0.0f`, `origin->x`, `vel.x` — the component is the
        // last identifier character before any trailing ternary.
        static bool Ends(string e, char c)
            => Regex.IsMatch(e, @"[.>]" + c + @"\b");
    }

    /// <summary>
    /// Each arm's slot reads, from the initialiser it returns:
    /// <c>new Vector(f[0], f[1], f[2])</c> is three, <c>f[3]</c> is one.
    /// </summary>
    private static Dictionary<string, (List<Slot> F, List<Slot> I)> ArmsOf(string dispatcher)
    {
        var arms = new Dictionary<string, (List<Slot>, List<Slot>)>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(dispatcher,
                     @"private static TempEntState\? (\w+)\(ReadOnlySpan<float> f, "
                   + @"ReadOnlySpan<int> i\)\s*\n\s*\{(.*?)\n    \}",
                     RegexOptions.Singleline))
        {
            string body = m.Groups[2].Value;

            var floats = new SortedDictionary<int, Slot>();
            var ints = new SortedDictionary<int, Slot>();

            foreach (Match v in Regex.Matches(body,
                         @"new Vector\(f\[(\d+)\], f\[(\d+)\], f\[(\d+)\]\)"))
            {
                floats[int.Parse(v.Groups[1].Value)] = Slot.VectorX;
                floats[int.Parse(v.Groups[2].Value)] = Slot.VectorY;
                floats[int.Parse(v.Groups[3].Value)] = Slot.VectorZ;
            }

            foreach (Match s in Regex.Matches(body, @"f\[(\d+)\]"))
                floats.TryAdd(int.Parse(s.Groups[1].Value), Slot.Scalar);

            foreach (Match s in Regex.Matches(body, @"i\[(\d+)\]"))
                ints.TryAdd(int.Parse(s.Groups[1].Value), Slot.Scalar);

            // A LOOP READS A RUN THIS CANNOT ENUMERATE. TeBeamSpline walks its
            // sixteen points with an index, so the slots it touches are not
            // literals; it is covered by its own byte tests instead.
            if (body.Contains("for (", StringComparison.Ordinal))
            {
                s_Looping.Add(m.Groups[1].Value);
                continue;
            }

            arms[m.Groups[1].Value] = (floats.Values.ToList(), ints.Values.ToList());
        }

        return arms;
    }

    /// <summary>
    /// The managed name for a native id, READ FROM THE HEADER rather than
    /// derived from the id.
    ///
    /// <para>
    /// Deriving it lowercases what it does not know is an acronym, so
    /// SOURCESHARP_TE_FWD_BSP_DECAL becomes "BspDecal" and the real value is
    /// <c>BSPDecal</c> — a mismatch reported as a missing arm. The header line
    /// carries both names for exactly this reason; see TempEntForwardIdTests,
    /// which holds the two enums against each other.
    /// </para>
    /// </summary>
    private static string ManagedNameOf(string nativeId)
    {
        if (s_Names.Count == 0 && FindRelative(Path.Combine(
                "src", "sourcesharp", "game", "shared", "tempent_forwards.h")) is { } header)
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(header),
                         @"(SOURCESHARP_TE_FWD_\w+)\s*=\s*\d+,\s*//\s*\w+\s*->\s*(\w+)"))
            {
                s_Names[m.Groups[1].Value] = m.Groups[2].Value;
            }
        }

        return s_Names.TryGetValue(nativeId, out string? managed) ? managed : nativeId;
    }

    private static readonly Dictionary<string, string> s_Names = new(StringComparer.Ordinal);

    private static readonly HashSet<string> s_Looping = new(StringComparer.Ordinal);

    /// <summary>
    /// The path under THIS checkout's root, or null.
    ///
    /// <para>
    /// The walk used to run all the way to <c>/</c>, taking the first match it
    /// met. Measured on 2026-09-01 in a worktree under
    /// <c>&lt;repo&gt;/.claude/worktrees/</c>: with
    /// <c>shell_te_forwards.cpp</c> moved out of the worktree entirely, both facts
    /// still passed — the walk had climbed past the worktree root into the
    /// PARENT checkout and read that repository's copy instead. A parity test
    /// that silently compares one tree's C++ against another tree's C# agrees
    /// with itself for as long as the two trees happen to match, and says
    /// nothing on the day they do not.
    /// </para>
    ///
    /// <para>
    /// So the path is looked up in exactly one tree, the compat farm
    /// (<see cref="RepoLayout.SdkTree"/>), and nowhere else.
    /// </para>
    /// </summary>
    private static string? FindRelative(string relative)
    {
        string candidate = RepoLayout.Sdk(relative);

        return File.Exists(candidate) ? candidate : null;
    }
}
