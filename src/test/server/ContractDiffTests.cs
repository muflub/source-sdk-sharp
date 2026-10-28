using System.Text.RegularExpressions;

using SourceSharp.Abi;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// LAYER 0 OF <c>plan_test.md</c> — THE CONTRACT DIFF: the half that needs no
/// stock source.
///
/// <para>
/// The diff itself — every managed entity's inputs, outputs and map keys held
/// against the <c>DEFINE_*</c> declarations scraped out of stock's
/// <c>src/game</c>, pinned by <see cref="ContractBaseline"/> — is an oracle and
/// lives in SourceSharp.OracleTests (ContractDiffOracleTests, with the scraper
/// StockContract). The stock datadescs it reads are exactly the files the port
/// stopped compiling, so the farm no longer links them and the oracle reads
/// the pristine upstream clone instead.
/// </para>
///
/// <para>
/// What stays here reads only the managed registry, the baseline file and the
/// port's own tree: the managed base's universal answers, the arguments on
/// every universal gap and the counts they claim, and the open-defect and
/// refusal ratchets with their witnesses.
/// </para>
/// </summary>
public class ContractDiffTests
{
    private static HashSet<string> Intersect(IEnumerable<IEnumerable<string>> sets)
    {
        HashSet<string>? acc = null;

        foreach (var s in sets)
        {
            var here = new HashSet<string>(s, StringComparer.OrdinalIgnoreCase);

            if (acc is null)
                acc = here;
            else
                acc.IntersectWith(here);
        }

        return acc ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheManagedBaseAnswersOnlyKill()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ContractDiffTests).Assembly);
        registry.Services = Services();

        var managed = registry.DescribeClasses();

        // The subtraction above rests on this. If the managed base grew ANOTHER
        // universally-answered input, every per-class comparison would change
        // shape and this says so before the baseline diff does.
        //
        // IT WAS "Kill AND NOTHING ELSE" AND IT IS TWO NOW. AddOutput joined it
        // deliberately -- ScriptedEntity.OnAddOutput, which un-refuses the entry
        // ContractBaseline.Universal used to carry, because the connection table
        // it needs is in managed code since step 2a. The number this fact pins
        // is the point: a THIRD one appearing is still a change nobody asked
        // for, and this still says so.
        //
        // FOUR SINCE plan_physics_managed A38 (2026-10-05): CBaseEntity's
        // DisableDamageForces and EnableDamageForces, the EFL_NO_DAMAGE_FORCES
        // pair, are ScriptedEntity's now that damage pushes physics objects
        // (A30); their universal gaps are closed.
        var everyInput = Intersect(managed.Select(c => c.Inputs));

        Assert.True(everyInput.SetEquals(new[] { "Kill", "AddOutput", "DisableDamageForces", "EnableDamageForces" }),
                    $"the inputs every scripted entity answers are Kill, AddOutput and the damage-forces pair, and nothing else ({string.Join(",", everyInput.OrderBy(x => x))})");
    }

    [Fact]
    public void NothingDeliberateIsAlsoInTheBaseline()
    {
        // The two files answer the same question, so an entry in both means one
        // of them is being read and the other is not.
        var both = ContractBaseline.ClassGaps
            .Where(line => ContractBaseline.IsDeliberate(Kind(line), Leaf(line)))
            .ToArray();

        foreach (string line in both.Take(20))
            Assert.Fail($"in the baseline AND excused by a deliberate rule: {line}");

        Assert.True(both.Length == 0, "a gap is either argued or written down, never both");
    }

    //-------------------------------------------------------------------------
    // STEP 8b: EVERY UNIVERSAL GAP IS ARGUED, AND THE ARGUMENT IS CHECKED.
    //
    // The list is generated; the arguments are not. Without these facts the
    // arguments are prose beside a machine-written list, which is the exact
    // shape — a needle and its subject able to drift apart — that this tree has
    // now found 24 times.
    //-------------------------------------------------------------------------

    /// <summary>The twelve groups, and the only twelve an entry may name.</summary>
    private static readonly string[] KnownGroups =
    {
        ContractBaseline.GroupParenting,
        ContractBaseline.GroupRenderState,
        ContractBaseline.GroupVscriptAbsent,
        ContractBaseline.GroupResponseSystemAbsent,
        ContractBaseline.GroupUserIo,
        ContractBaseline.GroupDamage,
        ContractBaseline.GroupTeam,
        ContractBaseline.GroupMotion,
        ContractBaseline.GroupIdentity,
        ContractBaseline.GroupThink,
        ContractBaseline.GroupUse,
        ContractBaseline.GroupDispatchEffect,
    };

    [Fact]
    public void EveryUniversalGapCarriesAnArgument()
    {
        // NAMED, not counted, and the TODO arm is what makes the regeneration
        // path safe: SOURCESHARP_DUMP_CONTRACT prints a skeleton for a new
        // entry, and a skeleton pasted in unedited fails here rather than
        // silently joining 66 argued ones.
        foreach (var g in ContractBaseline.UniversalArgued)
        {
            Assert.True(!g.Reason.Contains("TODO", StringComparison.Ordinal),
                        $"{g.Line} was pasted from the dump and never argued");

            Assert.True(g.Reason.Length >= 80,
                        $"{g.Line}'s reason is too short to be an argument ({g.Reason.Length} chars)");
        }
    }

    [Fact]
    public void EveryUniversalGapNamesOneOfTheTwelveGroups()
    {
        foreach (var g in ContractBaseline.UniversalArgued)
            Assert.True(KnownGroups.Contains(g.Group, StringComparer.Ordinal),
                        $"{g.Line} names a group that does not exist: '{g.Group}'");
    }

    [Fact]
    public void NoUniversalGroupIsDead()
    {
        // The same rule EveryDeliberateRuleStillMatchesSomething applies to the
        // allow-list: a group nothing is in is a mechanism that stopped being a
        // gap, and its constant should go with the last entry that used it.
        foreach (string group in KnownGroups)
            Assert.True(ContractBaseline.UniversalArgued.Any(g => g.Group == group),
                        $"no universal gap is in the '{group}' group any more; delete the constant");
    }

    [Fact]
    public void EveryUniversalGapsAnsweredByCountIsWhatTheBuildMeasures()
    {
        // THE FACT THAT MAKES THE LIST HONEST. Universal is computed as stock's
        // root contract MINUS what EVERY managed class answers — which is the
        // managed BASE's contract, NOT "what no managed class answers", the
        // sentence this file carried until step 8b. Twelve entries are answered
        // by between 1 and 33 classes each. Pinning that count by EQUALITY means
        // an entry cannot drift from 0 to 31, or from 31 back to 0, without the
        // file being edited to say so.
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ContractDiffTests).Assembly);
        registry.Services = Services();

        var managed = registry.DescribeClasses();

        foreach (var g in ContractBaseline.UniversalArgued)
        {
            string kind = Kind(g.Line);
            string name = g.Line.Split('|')[1];

            int measured = managed.Count(c => Side(c, kind).Contains(name, StringComparer.OrdinalIgnoreCase));

            Assert.True(measured == g.AnsweredBy,
                        $"{g.Line} says AnsweredBy {g.AnsweredBy}; {measured} managed classes answer it");
        }
    }

    [Fact]
    public void SomeUniversalGapsAreAnsweredBySomeManagedClasses()
    {
        // The fact above would pass on an all-zero list, which is precisely the
        // false picture the old comment painted. This one says the list is NOT
        // "what nothing answers" and never was, and it goes red the day somebody
        // redefines Universal to mean that without saying so.
        var partly = ContractBaseline.UniversalArgued.Where(g => g.AnsweredBy > 0).ToArray();

        Assert.True(partly.Length > 0,
                    "Universal is what the managed BASE does not answer, so some entries are answered by individual classes");

        // And each of those stands in for the classes that do NOT answer it, so
        // its argument has to acknowledge the ones that do rather than read as
        // "nothing here answers this".
        foreach (var g in partly)
            Assert.True(g.Reason.Contains("managed class", StringComparison.OrdinalIgnoreCase)
                     || g.Reason.Contains("classes", StringComparison.OrdinalIgnoreCase),
                        $"{g.Line} is answered by {g.AnsweredBy} classes and its reason does not mention them");
    }

    private static IEnumerable<string> Side(ScriptedClassInfo c, string kind) => kind switch
    {
        "input" => c.Inputs,
        "output" => c.Outputs,
        _ => c.Vars.Select(v => v.Name),
    };

    //-------------------------------------------------------------------------
    // STEP 8b: OPEN DEFECTS ARE NOT REFUSALS, AND EACH ONE'S WITNESS IS CHECKED.
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryOpenDefectNamesAFileThatExists()
    {
        string root = RepoLayout.SdkTree;

        foreach (var d in ContractBaseline.OpenDefects)
            Assert.True(File.Exists(Path.Combine(root, d.File)),
                        $"{d.Subject}: names {d.File}, which is not in the tree");
    }

    [Fact]
    public void EveryOpenDefectsWitnessIsStillInTheCode()
    {
        // THE RATCHET. The witness is present BECAUSE the defect is open, so
        // when somebody fixes it the fact goes red and the entry has to go. A
        // list of bugs that outlives the bugs is a list nobody reads.
        //
        // Comment-stripped, because the same tree has twice shipped a check
        // whose needle lived only in a comment — 6d's `/*nHitboxSet=*/0` and the
        // relay list's replacement comment.
        string root = RepoLayout.SdkTree;

        foreach (var d in ContractBaseline.OpenDefects.Where(x => x.Code.Length > 0))
        {
            string code = AbiParityTests.CodeOnly(File.ReadAllText(Path.Combine(root, d.File)));

            Assert.True(code.Contains(d.Code, StringComparison.Ordinal),
                        $"{d.Subject}: '{d.Code}' is no longer in {d.File}; if it is fixed, delete the entry");
        }
    }

    [Fact]
    public void AnOpenDefectWithNoWitnessSaysSoOutLoud()
    {
        // VACUOUS RIGHT NOW, AND SAYING SO IS THE POINT OF THIS COMMENT.
        //
        // ent_fire was the only entry that ever used the allowance: its walk is
        // inside engine.so and the shape that defeated it is this plan working
        // as designed, so nothing in this tree changed when it was fixed --
        // which is also why this list went on carrying it for four waves after
        // step 8e-entfire closed it. Lane loose-ends ran the witness
        // (tools/8e-entfire-gate, 13 of 13 PASS) and removed the row, so every
        // entry left has a real code witness and
        // EveryOpenDefectsWitnessIsStillInTheCode now covers ALL of them.
        //
        // THE RULE STAYS ANYWAY, and is not turned into "there are none": the
        // next unratchetable defect must still say why it is unratchetable
        // rather than leave an empty field somebody reads as an oversight. A
        // fact that currently examines nothing is worth keeping when what it
        // guards is the shape of a FUTURE entry; what is not worth keeping is
        // one that examines nothing and does not say so.
        foreach (var d in ContractBaseline.OpenDefects.Where(x => x.Code.Length == 0))
            Assert.True(d.Mechanism.Contains("no tree-local witness", StringComparison.OrdinalIgnoreCase),
                        $"{d.Subject}: has no witness and does not say why");
    }

    [Fact]
    public void EveryOpenDefectNamesAnOwnerOrSaysItHasNone()
    {
        foreach (var d in ContractBaseline.OpenDefects)
            Assert.True(d.Owner.Length > 0, $"{d.Subject}: no owner field at all");
    }

    /// <summary>
    /// THE ASSERTION, INVERTED. Lane <c>defect-teleport</c> closed the last
    /// entry on the list, so the row's fact is now that it is GONE.
    ///
    /// <para>
    /// A row that simply disappears from the tests makes a closed gap and a
    /// broken wire look identical again, which is what <c>defect-skybox</c> and
    /// <c>loose-ends</c> both landed the inversion to stop. Putting the row back
    /// fails this.
    /// </para>
    ///
    /// <para>
    /// <b>WITH A POSITIVE CONTROL</b>, and the control is not decoration: the
    /// list is EMPTY, so an absence check over it cannot fail for any reason at
    /// all — including "the predicate never matches anything". The control asks
    /// the same predicate of <see cref="ContractBaseline.Refusals"/>, which is
    /// not empty and does carry three subjects with "gib" in them, so a matcher
    /// that had stopped matching is a red fact rather than a green one.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTeleportDefectHasLeftTheOpenDefectsList()
    {
        static bool Mentions(string subject, string word)
            => subject.Contains(word, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(ContractBaseline.OpenDefects,
                              d => Mentions(d.Subject, "teleport"));

        // THE POSITIVE CONTROL: the same question, of a list that answers.
        Assert.Contains(ContractBaseline.Refusals, r => Mentions(r.Subject, "gib"));
    }

    /// <summary>
    /// AND THE LIST IS EMPTY, for the first time since step 8b created it.
    ///
    /// <para>
    /// Written down as a fact rather than left as a property of a list nobody
    /// asserts, because the empty list is what makes every OTHER fact about
    /// <c>OpenDefects</c> vacuous — <see cref="EveryOpenDefectsWitnessIsStillInTheCode"/>
    /// and its three siblings all iterate it and all currently examine nothing.
    /// They stay, because what they guard is the shape of a FUTURE entry; this
    /// fact is what says out loud that they are guarding nothing today, which is
    /// the rule <see cref="AnOpenDefectWithNoWitnessSaysSoOutLoud"/> already
    /// states about itself.
    /// </para>
    ///
    /// <para>
    /// <b>THIS FACT IS EXPECTED TO GO RED.</b> The next lane that finds a defect
    /// nobody chose adds it to the list and deletes this fact, and that is the
    /// list working. What it must not be is a bug filed as a refusal instead —
    /// see <see cref="NoOpenDefectIsAlsoFiledAsARefusal"/>.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePortHasNoOpenDefectsLeftOnThisList()
    {
        Assert.Empty(ContractBaseline.OpenDefects);

        // Non-vacuity: the record type and the list are still real, and the
        // sibling list they are kept apart from is not empty either. An
        // OpenDefects that had been deleted rather than emptied would pass the
        // line above and mean nothing.
        Assert.NotEmpty(ContractBaseline.Refusals);
        Assert.NotNull(ContractBaseline.OpenDefects);
    }

    [Fact]
    public void NoOpenDefectIsAlsoFiledAsARefusal()
    {
        // A bug filed as a decision reads as "we chose this", which is the
        // laundering plan_entity.md §7 exists to stop. The sound of a shell-less
        // entity used to be the one entry that legitimately sat on the Refusals
        // side of this line -- step 4b measured it, argued it and wrote down
        // what would close it -- and step 8e-sound closed it, so it is on
        // neither list now.
        foreach (var d in ContractBaseline.OpenDefects)
            foreach (var r in ContractBaseline.Refusals)
                Assert.True(!string.Equals(d.Subject, r.Subject, StringComparison.OrdinalIgnoreCase),
                            $"'{d.Subject}' is in both OpenDefects and Refusals");
    }

    //-------------------------------------------------------------------------
    // STEP 8e-ratchets: A REFUSAL HAS A WITNESS TOO.
    //
    // Until this step Refusals had NO ratchet. Deliberate had
    // EveryDeliberateRuleStillMatchesSomething; OpenDefects got a witness check
    // at 8b; Refusals was read only by PhysicsShellContractTests and
    // OverlayContractTests, and every one of those facts asks what the ENTRY
    // SAYS -- that the reason contains "DURING the solve", that the citation
    // names a plan section. All of them pass whether or not the thing being
    // refused still exists, so a refusal whose subject was deleted would have
    // gone on excusing nothing forever.
    //
    // 8b audited all nine by hand and reported none stale. The first run of the
    // facts below found one anyway: the AI-node-overlay entry argued itself on
    // shell_ai_absent.cpp defining g_pAINetworkManager, and step 4f had deleted
    // that stub. A hand audit reads the sentence; a witness reads the tree.
    //
    // BOTH DIRECTIONS, because a refusal can be lifted by deleting code or by
    // adding it:
    //
    //   Code   is present because the refusal is live. Lifting it deletes the
    //          code, so the fact goes red.
    //   Lifted is absent because the refusal is live. Lifting it writes that
    //          code, so the fact goes red.
    //
    // COMMENT-STRIPPED, both of them. This tree has now shipped two checks that
    // read prose as code -- 6d's hitbox needle, which lived only inside the
    // comment the test stripped and so never had a subject at all, and
    // tools/entity-gates counting a paragraph as a cast because scan_sites was a
    // raw grep. Every witness file here CONTAINS a comment explaining its own
    // refusal, so an unstripped read would be green on the prose alone.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Where a refusal's witness file is. Every witness is SdkTree-relative
    /// except the CMake source lists (<see cref="CMakeGraph.ListFile"/>), which
    /// live in this repository and not in the farm: the "no AI" refusal's
    /// witness is server_sourcesharp's list, which replaced server_csharp.vpc
    /// at Phase 6b. Only those exact paths are repo-rooted, so a typo cannot
    /// fall through to the wrong tree.
    /// </summary>
    private static string WitnessPath(string root, string file) =>
        CMakeListFiles.Contains(file) ? RepoLayout.Repo(file) : Path.Combine(root, file);

    private static readonly HashSet<string> CMakeListFiles = new(StringComparer.Ordinal)
    {
        CMakeGraph.ListFile(CMakeGraph.Server),
        CMakeGraph.ListFile(CMakeGraph.Client),
        CMakeGraph.ListFile(CMakeGraph.Host),
    };

    /// <summary>
    /// A witness file's CODE: C/C# comments stripped, or, for a CMake list,
    /// '#' comments -- so a needle living only in a comment is never a witness.
    /// </summary>
    private static string WitnessCode(string root, string file)
    {
        string text = File.ReadAllText(WitnessPath(root, file));

        return CMakeListFiles.Contains(file)
            ? Regex.Replace(text, "#[^\n]*", "")
            : AbiParityTests.CodeOnly(text);
    }

    [Fact]
    public void EveryRefusalNamesAFileThatExists()
    {
        string root = RepoLayout.SdkTree;

        foreach (var r in ContractBaseline.Refusals)
            Assert.True(File.Exists(WitnessPath(root, r.File)),
                        $"{r.Subject}: names {r.File}, which is not in the tree");
    }

    [Fact]
    public void EveryRefusalsWitnessIsStillInTheCode()
    {
        // THE RATCHET, first half. The needle is in the tree BECAUSE the
        // refusal is live, so implementing the refused thing takes it out and
        // this fact forces the entry out with it.
        string root = RepoLayout.SdkTree;

        foreach (var r in ContractBaseline.Refusals.Where(x => x.Code.Length > 0))
        {
            string code = WitnessCode(root, r.File);

            Assert.True(code.Contains(r.Code, StringComparison.Ordinal),
                        $"{r.Subject}: '{r.Code}' is no longer in {r.File}; if the refusal is lifted, delete the entry");
        }
    }

    [Fact]
    public void WhatWouldLiftARefusalIsNotInItsWitnessFile()
    {
        // THE RATCHET, second half, and the one the first half cannot do. A
        // refusal lifted by ADDING a case, an arm or a term leaves the old code
        // where it was -- the motion controller's "unknown kind" default arm
        // survives a fifth kind being added beside the four, and a substring
        // needle misses added text by construction.
        string root = RepoLayout.SdkTree;

        foreach (var r in ContractBaseline.Refusals.Where(x => x.Lifted.Length > 0))
        {
            string code = WitnessCode(root, r.File);

            Assert.True(!code.Contains(r.Lifted, StringComparison.Ordinal),
                        $"{r.Subject}: '{r.Lifted}' is in {r.File} now, which is what lifting this refusal looks like; if it is lifted, delete the entry");
        }
    }

    [Fact]
    public void EveryLiftedNeedleNamesSomethingThatIsRealSomewhereElse()
    {
        // WHAT STOPS THE ABSENCE CHECK BEING DECORATION. "This file does not
        // contain the string X" passes for any X nobody would ever write, which
        // is the floor-that-cannot-be-violated shape. Requiring the needle to
        // occur somewhere ELSE in the tree gives it a subject: it has to be an
        // idiom this codebase actually uses.
        //
        // Read RAW rather than comment-stripped, deliberately and in the
        // opposite direction from the two facts above: what is being established
        // here is that the string is a real thing people write about and write,
        // not that some particular occurrence is live code. A stricter read
        // would make this fact fail for a reason that says nothing about the
        // refusal.
        //
        // THIS FACT COULD NOT FAIL WHEN IT WAS FIRST WRITTEN, and it was killed
        // by its own mutation: replacing a needle with
        // "UTIL_TraceLineThatNobodyWouldEverWrite" left it GREEN, because
        // ContractBaseline.cs is itself under src/ and holds every needle as a
        // string literal. The search was finding the declaration and calling it
        // corroboration. THE WHOLE TEST PROJECT IS EXCLUDED now, not just that
        // one file: a needle whose only other occurrence is in a test is not an
        // idiom this codebase writes, which is the entire claim being made.
        string root = RepoLayout.SdkTree;
        // Every test project's sources (plan_side_isolation P6 split one into four).
        string[] tests = SourceFileNamesExistTests.TestDirectories(root)
                                                  .Select(d => Path.GetFullPath(d) + Path.DirectorySeparatorChar)
                                                  .ToArray();

        var needles = ContractBaseline.Refusals
                                      .Where(x => x.Lifted.Length > 0)
                                      .ToList();

        Assert.NotEmpty(needles);

        foreach (var r in needles)
        {
            string own = Path.GetFullPath(WitnessPath(root, r.File));
            bool found = false;

            foreach (string f in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*",
                                                          SearchOption.AllDirectories))
            {
                if (!f.EndsWith(".cpp", StringComparison.Ordinal)
                 && !f.EndsWith(".h", StringComparison.Ordinal)
                 && !f.EndsWith(".cs", StringComparison.Ordinal))
                    continue;

                string full = Path.GetFullPath(f);

                if (string.Equals(full, own, StringComparison.Ordinal)
                 || tests.Any(t => full.StartsWith(t, StringComparison.Ordinal)))
                    continue;

                if (File.ReadAllText(f).Contains(r.Lifted, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            Assert.True(found,
                        $"{r.Subject}: '{r.Lifted}' occurs nowhere else in src/, so requiring its absence from {r.File} guards nothing");
        }
    }

    [Fact]
    public void ARefusalWithNoWitnessSaysSoOutLoud()
    {
        // The same allowance OpenDefects has, on the same terms: a refusal whose
        // subject leaves no trace in this tree may be unratchetable, but it has
        // to say so rather than leave an empty field a reader takes for an
        // oversight. No entry uses this today -- all eight have a witness -- and
        // the fact is here so that the next one that cannot is forced to argue
        // it.
        foreach (var r in ContractBaseline.Refusals.Where(x => x.Code.Length == 0))
            Assert.True(r.Reason.Contains("no tree-local witness", StringComparison.OrdinalIgnoreCase),
                        $"{r.Subject}: has no witness and does not say why");
    }

    [Fact]
    public void NoTwoRefusalsShareASubject()
    {
        // Refusals is appended to by many lanes, at the HEAD by convention, and
        // two lanes recording the same refusal in different words would leave
        // one of them un-retired when the other is lifted.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in ContractBaseline.Refusals)
            Assert.True(seen.Add(r.Subject), $"'{r.Subject}' is recorded twice");
    }

    /// <summary>
    /// <b>The soundscape transport exists end to end</b> — plan_entity.md step
    /// 8e-sound, and this fact REPLACED
    /// <c>ThePlayerSoundscapeSlotIsStillAnEmptyBody</c>.
    ///
    /// <para>
    /// That one was the OpenDefects witness: it read
    /// <c>SourceSharp_PlayerSetSoundscape</c>'s body structurally and required
    /// it to be EMPTY, so that giving the slot a body would force the entry out
    /// of the list. It fired, which is the ratchet working. What replaced it has
    /// to pin the opposite, and the shape matters: four separate links, each
    /// checked where it lives, because the defect was never a broken function —
    /// it was two correct halves with no wire between them, and a check on
    /// either half alone was green throughout.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSoundscapePushTravelsFromTheServersChoiceToTheClientsEar()
    {
        string root = RepoLayout.SdkTree;

        // LINK 1 -- the server sends a message instead of calling the dead slot.
        string decide = File.ReadAllText(Path.Combine(root, "src", "sourcesharp", "managed",
            "SourceSharp.Server", "Entities", "Soundscape", "Soundscapes.cs"));

        Assert.Contains("_messages.Send(SoundscapeMessage.PayloadOf(", decide, StringComparison.Ordinal);

        Assert.DoesNotContain("_sound.PlayerSetSoundscape", decide, StringComparison.Ordinal);

        // LINK 2 -- the message is declared in the SHARED assembly, which is
        // what makes the wire index the same number on both sides.
        string declare = File.ReadAllText(Path.Combine(root, "src", "sourcesharp", "managed",
            "SourceSharp.Shared", "UserMessages.cs"));

        Assert.Contains("[UserMessage(\"Soundscape\")]", declare, StringComparison.Ordinal);

        // LINK 3 -- and it was APPENDED, because the registration order IS the
        // protocol: a message inserted anywhere else shifts the wire index of
        // every message after it. The one declared after it since is
        // SsReconnect (plan_host_sdk), appended in turn.
        int at = declare.IndexOf("[UserMessage(\"Soundscape\")]", StringComparison.Ordinal);
        int next = declare.IndexOf("[UserMessage(", at + 1, StringComparison.Ordinal);
        Assert.True(next < 0 || declare.IndexOf("[UserMessage(Name, Size = 0)]\npublic sealed class ReconnectMessage", next, StringComparison.Ordinal) == next,
                    "the Soundscape message is the last one declared, so it took the next " +
                    "wire index and moved nobody else's");

        // LINK 4 -- the client's landing site has a caller. Since sound cluster
        // 17 (lane c2-sims) the client soundscape system is managed
        // (ClientSoundscapes), so the push lands without crossing: the client
        // module registers the handler's sink as ClientSoundscapes.Push.
        string land = File.ReadAllText(Path.Combine(root, "src", "sourcesharp", "managed",
            "SourceSharp.Client", "ClientModule.cs"));

        Assert.Contains("_services.AddInstance<SoundscapeMessage.SoundscapeSink>(\n" +
                        "            (index, entIndex, bits, positions) => _soundscapes.Push(index, entIndex, bits, positions));",
                        land.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>And 7e's printed refusal is gone with its subject</b> — the same
    /// closure read from the other direction.
    ///
    /// <para>
    /// Step 7e wrote the line so that wiring the transport would stop it: "a
    /// check that fails in the useful direction". The honest response to that
    /// signal is deletion rather than a new condition — a refusal kept alive
    /// under different terms is a sentence that goes on excusing something
    /// nobody is refusing — and this fact is what stops it coming back.
    /// </para>
    ///
    /// <para>
    /// Read RAW rather than comment-stripped on purpose. The file still explains
    /// what the refusal said and why it went, so a stripped read would pass
    /// while a live <c>Warning()</c> sat beside the prose. Raw catches both.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSoundscapeRefusalIsGoneFromTheClientEntirely()
    {
        string root = RepoLayout.SdkTree;
        // The client soundscape system is managed since sound cluster 17
        // (lane c2-sims); its native shell_soundscape_client.cpp is deleted.
        string raw = File.ReadAllText(Path.Combine(root, "src", "sourcesharp", "managed",
            "SourceSharp.Client", "Soundscapes", "ClientSoundscapes.cs"));

        Assert.Contains("public sealed class ClientSoundscapes", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("[SourceSharp] soundscapes:", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("SoundscapeReportRefusal", raw, StringComparison.Ordinal);
    }

    private static string Kind(string line) => line.Split('|')[0];

    private static string Leaf(string line) => line.Split('|')[2];
}
