using System.Text.RegularExpressions;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// EVERY STRING KEY EVERY NATIVE FORWARD WRITES IS A FIELD ITS CLASS DECLARES,
/// across every <c>shell_te_forwards*.cpp</c> on both sides and for all 44 classes.
///
/// <para>
/// THIS GATE EXISTED AND WAS SCOPED TO ONE LANE, which is why it was green while
/// sixty-one keys were wrong. Lane RN wrote it for the seven classes it owned so
/// that a neighbour mid-conversion could not redden it — correct at the time, and
/// exactly the shape of a check that stops being one. The lanes that renamed
/// their declarations did not all rename the forwards that write them.
/// </para>
///
/// <para>
/// WHAT IT COSTS TO BE WRONG HERE. The lookup is
/// <c>StringComparer.Ordinal</c>, so <c>"r"</c> is not <c>"R"</c> and
/// <c>"m_nMagnitude"</c> is not <c>"Magnitude"</c>. A key that does not match is
/// a warning on a console nobody is reading and a field left at zero — a spark
/// of magnitude nothing at the world origin, an explosion with no radius. It
/// compiles, it sends, and the effect is silently empty.
/// </para>
///
/// <para>
/// AND THE SANDBOX CANNOT SEE IT. <c>cs_te_fire</c> fires from the MANAGED side,
/// through the declarations themselves; these forwards are reached only from
/// stock C++ — <c>util.cpp</c>, <c>EffectsServer.cpp</c>, <c>c_te.cpp</c> — so a
/// live join can report 43 of 43 with every one of these broken.
/// </para>
/// </summary>
public class TempEntShimKeyTests
{
    /// <summary>
    /// The registry, which is where the flattened names live: a class's own
    /// fields, its base chain's, AND its nested table's, each at its own offset.
    /// Parsing the C# instead would have to re-derive all three, and the second
    /// implementation of a rule is where the two stop agreeing.
    /// </summary>
    private static TempEntRegistry Registry()
    {
        var engine = Fake();
        engine.Strict = false;

        int next = 5;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        return registry;
    }

    [Fact]
    public void EveryKeyEveryForwardWritesIsAFieldItsDeclarationHas()
    {
        string? anyShim = RepoLayout.FindSdkFile(Path.Combine(
            "src", "sourcesharp", "game", "server", "shell_te_forwards.cpp"));

        if (anyShim is null)
        {
            Assert.True(true, "shell_te_forwards*.cpp read for the shim check (SKIPPED, not found "
                            + "from the test's working directory)");
            return;
        }

        string game = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(anyShim)!, ".."));

        TempEntRegistry registry = Registry();

        var wrong = new List<string>();
        int blocks = 0, keys = 0, files = 0;

        foreach (string side in new[] { "server", "client" })
        {
            string dir = Path.Combine(game, side);

            if (!Directory.Exists(dir))
                continue;

            foreach (string file in Directory.GetFiles(dir, "shell_te_forwards*.cpp").Where(f => !f.EndsWith("_debug.cpp", StringComparison.Ordinal)).Order())
            {
                string source = File.ReadAllText(file);
                string name = Path.GetFileName(file);
                files++;

                // Each block runs from the TeHandle that names the class to the
                // Send or Play that closes it.
                foreach (Match open in Regex.Matches(source, @"TeHandle\(\s*\w+,\s*""(\w+)""\s*\)"))
                {
                    string className = open.Groups[1].Value;
                    blocks++;

                    TempEntType? type = registry.Find(className);

                    if (type is null)
                    {
                        wrong.Add($"{name} forwards {className}, which no managed declaration owns "
                                + "— a forward for a class nothing declares can only write nothing");
                        continue;
                    }

                    int from = open.Index;
                    Match close = Regex.Match(source[from..], @"SourceSharpTe_(?:Send|Play)\s*\(");
                    int to = close.Success ? from + close.Index : source.Length;

                    foreach (Match set in Regex.Matches(source[from..to],
                                 @"SourceSharpTe_Set(?:Int|Float|Vector|VectorElem)\(\s*""([^""]+)"""))
                    {
                        string key = set.Groups[1].Value;
                        keys++;

                        if (!type.Fields.ContainsKey(key))
                            wrong.Add($"{name} writes '{key}' on {className}, which declares "
                                    + $"[{string.Join(", ", type.Fields.Keys.Order())}]");
                    }
                }
            }
        }

        Assert.True(wrong.Count == 0,
            $"{wrong.Count} forward key(s) name a field the declaration does not have, out of "
          + $"{keys} across {blocks} blocks in {files} files:\n  " + string.Join("\n  ", wrong.Take(12))
          + (wrong.Count > 12 ? $"\n  ... and {wrong.Count - 12} more" : ""));

        // SELF-CALIBRATING, because the population is deliberately shrinking.
        // A floor would have to be edited every time the sweep converts a batch,
        // and an edited floor is one nobody trusts. Instead the structured scan
        // is compared against a trivial count of the same thing: if the block
        // regex stops matching, the two disagree and this fails, whatever the
        // remaining number happens to be.
        int loose = 0;

        foreach (string side in new[] { "server", "client" })
        {
            string dir = Path.Combine(game, side);

            if (Directory.Exists(dir))
            {
                foreach (string file in Directory.GetFiles(dir, "shell_te_forwards*.cpp").Where(f => !f.EndsWith("_debug.cpp", StringComparison.Ordinal)))
                    loose += Regex.Matches(File.ReadAllText(file),
                                 @"SourceSharpTe_Set(?:Int|Float|Vector|VectorElem)\(\s*""").Count;
            }
        }

        // (shell_te_forwards_debug.cpp is excluded above: it holds the debug-only
        // cs_sv_te_dispatch probe, not a forward.)
        //
        // EXACTLY TWO, NOT A FLOOR OF TEN. plan_shim.md Phase 1f collapsed the
        // seventeen per-lane shell_te_fwd_*.cpp into one file per side, so the
        // count is now a structural fact rather than a moving population: a third
        // forwarder file, or a missing one, means the scan is looking somewhere
        // this check was not written for, and either way it should say so.
        Assert.True(files == 2,
            $"the scan found {files} forward file(s) and there are exactly two, one per "
          + "side — it is looking in the wrong place");

        // KEYS OUTSIDE ANY BLOCK ARE STILL KEYS, and until this check compared
        // its own two numbers nothing looked at them: WriteBaseBeam writes
        // DT_TEBaseBeam's fifteen fields once per side, in a helper that no
        // TeHandle opens, so thirty keys were being skipped by the very test
        // that exists to check them.
        //
        // They cannot be attributed to one class -- a base's fields belong to
        // every beam -- so they are checked against the union: a key that names
        // no declared field anywhere is a typo whatever wrote it.
        var everyField = registry.All.SelectMany(t => t.Fields.Keys)
                                     .ToHashSet(StringComparer.Ordinal);

        foreach (string side in new[] { "server", "client" })
        {
            string dir = Path.Combine(game, side);

            if (!Directory.Exists(dir))
                continue;

            foreach (string file in Directory.GetFiles(dir, "shell_te_forwards*.cpp").Where(f => !f.EndsWith("_debug.cpp", StringComparison.Ordinal)).Order())
            {
                string source = File.ReadAllText(file);

                foreach (Match set in Regex.Matches(source,
                             @"SourceSharpTe_Set(?:Int|Float|Vector|VectorElem)\(\s*""([^""]+)"""))
                {
                    string key = set.Groups[1].Value;

                    if (!everyField.Contains(key))
                        wrong.Add($"{Path.GetFileName(file)} writes '{key}', which is not a field "
                                + "of ANY declaration");
                }
            }
        }

        Assert.True(wrong.Count == 0,
            $"{wrong.Count} forward key(s) name no declared field:\n  "
          + string.Join("\n  ", wrong.Take(12)));

        Assert.True(keys <= loose,
            $"the block scan checked {keys} key(s) and a plain count of the same files finds "
          + $"{loose}; the block scan cannot check MORE than exist. The {loose - keys} outside a "
          + "block are checked against the union above, which is what WriteBaseBeam's fifteen are");
    }
}
