using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_crossings.md Phase X5: whether a handle still names an entity,
/// answered from a record of what managed was TOLD instead of polled 38.578
/// times a server frame.
///
/// <para>
/// <b>What these facts are about is the CLOSURE and the FALLBACK, not the
/// dictionary.</b> Push-on-event makes staleness impossible by construction, so
/// the two things that can go wrong are different: a lifetime path nobody
/// pushed from, and a hierarchy the mirror answers for without being entitled
/// to. The first half of this file pins the mirror's rules — above all that a
/// handle it was never told about is NOT claimed — and the second half pins the
/// C++ lifetime census AS FACTS, so a second construction site or a removal
/// push that stops reaching its override is a red test rather than an entity
/// that managed believes in for the rest of the map.
/// </para>
///
/// <para>
/// <b>Every fact that reaches <c>EntityIsValid</c> asks an <see cref="EngineSide"/>
/// of its own</b> with a mirror of its own attached (plan_abi_hotreload D8), so
/// nothing here is process state and the file runs in parallel.
/// </para>
/// </summary>
public class LivenessMirrorTests
{
    private static EntityHandle H(uint value) => new(value);

    /// <summary>
    /// Handles chosen well away from anything another test class might use, and
    /// away from entry 0. See <see cref="LivenessMirror.Refuses"/>.
    /// </summary>
    private static readonly EntityHandle Prop = H(0x0BAD_0001u);
    private static readonly EntityHandle Other = H(0x0BAD_0002u);

    //-------------------------------------------------------------------------
    // The mirror's rules.
    //-------------------------------------------------------------------------

    [Fact]
    public void ASpawnPushClaimsTheHandleAlive()
    {
        var mirror = new LivenessMirror();

        mirror.NoteCreated(Prop);

        Assert.True(mirror.TryLookup(Prop, out bool alive) && alive,
            "OnScriptedSpawn is the creation push, and a handle it named is one managed has been "
            + "told about rather than one it must ask about");
    }

    [Fact]
    public void ARemovalPushClaimsTheHandleGone()
    {
        var mirror = new LivenessMirror();

        mirror.NoteCreated(Prop);
        mirror.NoteRemoved(Prop);

        Assert.True(mirror.TryLookup(Prop, out bool alive) && !alive,
            "OnScriptedRemoved is the removal push — A12 said in the row's own why column that "
            + "managed is TOLD of every death");
    }

    /// <summary>
    /// THE FACT THE WHOLE PHASE TURNS ON.
    ///
    /// <para>
    /// X0 measured 83.0% of the 38.578 calls a frame on handles that no longer
    /// resolve. If removal erased the record, every one of those would fall
    /// through to the slot and the phase would win 17% of its own row.
    /// </para>
    /// </summary>
    [Fact]
    public void ADeadRecordIsKeptRatherThanDropped()
    {
        var mirror = new LivenessMirror();

        mirror.NoteCreated(Prop);
        mirror.NoteRemoved(Prop);

        Assert.True(mirror.RecordCount == 1,
            "the record of a death is KEPT — 83.0% of this row's calls are on handles that have "
            + "already died, and dropping the record would send every one of them to the slot");
    }

    /// <summary>
    /// THE pBASE ARM'S FALLBACK, AS A FACT.
    ///
    /// <para>
    /// <c>EntityIsValid</c> answers for <c>pBase || pStandalone || pWorld</c>
    /// and only the standalone arm pushes. X0 measured <c>pBase</c> at 0 in
    /// every clean window — a full enumeration of 1006 entities returned
    /// exactly one non-standalone, worldspawn — but the arm still COMPILES, so
    /// the mirror needs a real fallback rather than a claim that it is empty.
    /// </para>
    /// </summary>
    [Fact]
    public void AHandleTheMirrorWasNeverToldAboutIsNotClaimed()
    {
        var mirror = new LivenessMirror();

        mirror.NoteCreated(Prop);

        Assert.True(!mirror.TryLookup(Other, out _),
            "the pBase arm still compiles, so an unheard-of handle is DECLINED and crosses — a "
            + "missed push costs a crossing, never a wrong answer");
    }

    /// <summary>
    /// THE WORLD, HANDLED DELIBERATELY RATHER THAN LEFT TO FALL THROUGH.
    ///
    /// <para>
    /// <c>CStandaloneEntity::ManagedHandle</c>
    /// (<c>shell_standalone_base.cpp:818</c>) returns <b>0</b> — not the invalid
    /// sentinel — for a handle that is not valid, so a push really can carry 0.
    /// Native's resolver returns early only for <c>INVALID_EHANDLE_INDEX</c>;
    /// handle 0 is entry 0, serial 0, which it looks up for real and which is
    /// where worldspawn lives. A <c>false</c> recorded there would answer "the
    /// world is gone" to every later reader.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWorldsSlotIsNeverRecorded()
    {
        var mirror = new LivenessMirror();

        mirror.NoteCreated(H(0u));
        mirror.NoteRemoved(H(0u));

        Assert.True(mirror.RecordCount == 0,
            "ManagedHandle returns 0 for an invalid handle and entry 0 is where worldspawn lives, "
            + "so a push carrying 0 must not become a record");
    }

    [Fact]
    public void TheWorldsSlotIsNeverAnswered()
    {
        var mirror = new LivenessMirror();

        // Pushed FIRST, and both ways. An empty mirror declines handle 0 for
        // the wrong reason — there is simply no record — so a fact that only
        // asked an empty one would pass however the refusal was written. This
        // one goes red the moment the guard stops holding.
        mirror.NoteCreated(H(0u));
        mirror.NoteRemoved(H(0u));

        Assert.True(!mirror.TryLookup(H(0u), out _),
            "CWorld is not on the standalone hierarchy and never pushes; it is answered by the "
            + "slot, deliberately — a liveness record for the world is exactly the record that "
            + "would have hidden the CWorld::Release() that was {} for six phases");
    }

    [Fact]
    public void TheInvalidSentinelIsNeverRecorded()
    {
        var mirror = new LivenessMirror();

        mirror.NoteCreated(H(LivenessMirror.InvalidHandle));
        mirror.NoteRemoved(H(LivenessMirror.InvalidHandle));

        Assert.True(mirror.RecordCount == 0,
            "INVALID_EHANDLE_INDEX names no entity, so a push carrying it must not become a "
            + "record — the WRITE side refuses it");
    }

    /// <summary>
    /// THE FIX THE CENSUS FOUND, and it is native's own first line rather than a
    /// guess.
    ///
    /// <para>
    /// <c>SourceSharp_ResolveEntity</c> opens with
    /// <c>if ( hEntity == INVALID_EHANDLE_INDEX ) return ent;</c> — all three
    /// arms NULL, so <c>IsValid()</c> is false whatever the entity list holds.
    /// <see cref="EntityHandle.None"/> IS that value, and managed code polls
    /// <c>.IsValid</c> on defaulted and cleared handles constantly.
    /// </para>
    ///
    /// <para>
    /// <b>Measured, not reasoned into existence.</b> With this DECLINED rather
    /// than answered, the reroute left 32.00 crossings a server frame standing
    /// and the mirror's own counters read <c>answered=1524 sentinel/refused=6432
    /// unknown=0</c> over three seconds: 81% of every lookup was this one value
    /// crossing the boundary to be told what the first line of the callee
    /// already knew.
    /// </para>
    /// </summary>
    [Fact]
    public void TheInvalidSentinelIsAnsweredFalseWithoutCrossing()
    {
        var mirror = new LivenessMirror();

        Assert.True(mirror.TryLookup(H(LivenessMirror.InvalidHandle), out bool alive),
            "the sentinel is ANSWERED, not declined — this is native's own early return moved "
            + "one side of the boundary, and it was 81% of every lookup");

        Assert.True(!alive, "and the answer is false, which is what that early return produces");
    }

    [Fact]
    public void HandleZeroIsDeclinedRatherThanAnswered()
    {
        var mirror = new LivenessMirror();

        Assert.True(!mirror.TryLookup(H(0u), out _),
            "handle 0 is NOT the sentinel: native looks it up for real, as entry 0 with serial 0, "
            + "and entry 0 is worldspawn's. Whether that resolves turns on a serial this side "
            + "cannot see, so it crosses");
    }

    [Fact]
    public void ARemovalOfAHandleNeverSpawnedStillRecordsItGone()
    {
        var mirror = new LivenessMirror();

        mirror.NoteRemoved(Prop);

        Assert.True(mirror.TryLookup(Prop, out bool alive) && !alive,
            "an entity created and freed without ever spawning still announces its death, and "
            + "'gone' is the right answer whether or not this mirror saw it arrive");
    }

    [Fact]
    public void ARepeatedSpawnPushDoesNotDoubleCountTheLiving()
    {
        var mirror = new LivenessMirror();

        mirror.NoteCreated(Prop);
        mirror.NoteCreated(Prop);

        Assert.True(mirror.LiveCount == 1 && mirror.RecordCount == 1,
            "the pushes are idempotent, so a count read off this mirror is a population and not "
            + "a tally of how many times the engine mentioned something");
    }

    [Fact]
    public void ARepeatedRemovalPushDoesNotDriveTheLiveCountNegative()
    {
        var mirror = new LivenessMirror();

        mirror.NoteCreated(Prop);
        mirror.NoteRemoved(Prop);
        mirror.NoteRemoved(Prop);

        Assert.True(mirror.LiveCount == 0,
            "a second death for the same handle is not a second entity leaving — a negative live "
            + "count would be a plausible-looking number rather than an error");
    }

    [Fact]
    public void LiveCountTracksSpawnsAndRemovals()
    {
        var mirror = new LivenessMirror();

        mirror.NoteCreated(Prop);
        mirror.NoteCreated(Other);
        mirror.NoteRemoved(Prop);

        Assert.True(mirror.LiveCount == 1 && mirror.RecordCount == 2,
            "two entities arrived and one left: one alive, and two records because the death is "
            + "kept");
    }

    [Fact]
    public void LiveHandlesEnumeratesOnlyTheLivingOnes()
    {
        var mirror = new LivenessMirror();

        mirror.NoteCreated(Prop);
        mirror.NoteCreated(Other);
        mirror.NoteRemoved(Prop);

        var live = new List<uint>(mirror.LiveHandles());

        Assert.True(live.Count == 1 && live[0] == Other.Value,
            "the divergence gate's second pass walks this, and a dead handle in it would be asked "
            + "of the slot and reported as a divergence that is not one");
    }

    [Fact]
    public void ALevelChangeEmptiesTheMirror()
    {
        var mirror = new LivenessMirror();

        mirror.NoteCreated(Prop);
        mirror.NoteRemoved(Other);
        mirror.Clear();

        Assert.True(mirror.RecordCount == 0 && mirror.LiveCount == 0,
            "every handle in the mirror belongs to the level that is going");
    }

    [Fact]
    public void EvictionNeverDropsALiveRecord()
    {
        var mirror = new LivenessMirror();

        mirror.NoteCreated(Prop);

        // Enough deaths to push the dictionary past its bound several times.
        for (uint i = 0; i < LivenessMirror.MaxRecords + 64; i++)
            mirror.NoteRemoved(H(0x1000_0000u + i));

        Assert.True(mirror.EvictedCount > 0, "the bound was really crossed, so this measures something");
        Assert.True(mirror.TryLookup(Prop, out bool alive) && alive,
            "eviction drops the oldest DEAD records only — evicting a live one would make the "
            + "reroute answer 'gone' for something standing in front of the player");
    }

    [Fact]
    public void AnEvictedHandleFallsThroughRatherThanAnsweringWrongly()
    {
        var mirror = new LivenessMirror();
        var first = H(0x1000_0000u);

        mirror.NoteRemoved(first);

        for (uint i = 1; i < LivenessMirror.MaxRecords + 64; i++)
            mirror.NoteRemoved(H(0x1000_0000u + i));

        Assert.True(!mirror.TryLookup(first, out _),
            "an evicted record is DECLINED, not answered — the cost of the bound is a crossing, "
            + "which is the direction every refusal in this class falls");
    }

    //-------------------------------------------------------------------------
    // The reroute: the side gate, and the read/write separation.
    //
    // These attach a mirror of their own to an EngineSide of their own, as the
    // server composition attaches its own (plan_abi_hotreload D7), because
    // EntityIsValid reads the attached one. A fresh side is uninitialised, so
    // the SLOT answers false -- which is what lets these facts tell the two
    // paths apart at all: a mirror saying TRUE where the slot says FALSE.
    //-------------------------------------------------------------------------

    private static void OnTheSide(Side side, Action<EngineSide, LivenessMirror> body)
    {
        var engine = TestSides.Create(side);
        var mirror = new LivenessMirror();
        using var attached = engine.AttachLiveness(mirror);
        body(engine, mirror);
    }

    [Fact]
    public void LivenessIsAnsweredFromTheMirrorOnTheServer()
    {
        OnTheSide(Side.Server, (engine, mirror) =>
        {
            mirror.NoteCreated(Prop);

            Assert.True(engine.EntityIsValid(Prop),
                "EntityIsValid reads the record the spawn push wrote — this is the 38.578 "
                + "crossings a server frame, the largest single row on that side");
        });
    }

    [Fact]
    public void ADeadHandleIsAnsweredFromTheMirrorWithoutCrossing()
    {
        OnTheSide(Side.Server, (engine, mirror) =>
        {
            mirror.NoteCreated(Prop);
            mirror.NoteRemoved(Prop);

            Assert.True(!engine.EntityIsValid(Prop), "a dead handle reads as gone");

            // AND that the mirror is what said so. In a test process the slot
            // answers false too, so the visible answer alone cannot tell the
            // two paths apart — a fact that stopped at the line above would
            // pass just as well with the record dropped and the crossing back.
            Assert.True(mirror.TryLookup(Prop, out bool alive) && !alive,
                "and this is the 83.0% — a handle managed kept after its entity died, answered "
                + "from the record of the death instead of by asking the engine's handle table");
        });
    }

    [Fact]
    public void LivenessFallsBackToTheSlotWhenTheRerouteIsSwitchedOff()
    {
        OnTheSide(Side.Server, (engine, mirror) =>
        {
            mirror.Rerouted = false;
            mirror.NoteCreated(Prop);

            Assert.True(!engine.EntityIsValid(Prop),
                "with the reroute off the read goes to the ABI slot, which in a process with no "
                + "engine answers false — this is the census's 'before' arm and it must really differ");
        });
    }

    /// <summary>
    /// THE REGRESSION FACT FOR THE BUG X2/X3's DIVERGENCE GATE ACTUALLY FOUND.
    ///
    /// <para>
    /// There, the write was briefly gated on <c>Rerouted</c> as well as on the
    /// side, so taking the census's "before" arm stopped feeding the mirror and
    /// the census control corrupted the mirror it was measuring — 18 behind and
    /// never recovering, because nothing in a push mirror ever resynchronises.
    /// </para>
    /// </summary>
    [Fact]
    public void APushIsRecordedEvenWithTheRerouteSwitchedOff()
    {
        OnTheSide(Side.Server, (engine, mirror) =>
        {
            mirror.Rerouted = false;
            mirror.NoteCreated(Prop);
            mirror.NoteRemoved(Other);

            Assert.True(mirror.RecordCount == 2,
                "the reroute is a READING control — a write it could switch off would leave the "
                + "mirror permanently wrong by however many lifetimes passed while it was off");
        });
    }

    [Fact]
    public void TheClientIsNotAnsweredFromTheMirror()
    {
        OnTheSide(Side.Client, (engine, mirror) =>
        {
            mirror.NoteCreated(Prop);

            Assert.True(!engine.EntityIsValid(Prop),
                "EntityIsValid is wired only under #ifndef CLIENT_DLL and only the server pushes, "
                + "but this is a listen server sharing one Engine and handle VALUES are per side "
                + "— an ungated mirror would answer a client handle out of a server entity's record");
        });
    }

    /// <summary>
    /// Two server sides in one process each answer out of their OWN mirror
    /// (plan_abi_hotreload D8): two game instances side by side must not see
    /// each other's entities. The mirror was reached through the static
    /// <c>Engine</c>'s one server side, which both would have shared.
    /// </summary>
    [Fact]
    public void TwoServerSidesEachAnswerFromTheirOwnMirror()
    {
        var first = TestSides.Create(Side.Server);
        var second = TestSides.Create(Side.Server);
        var firstMirror = new LivenessMirror();
        var secondMirror = new LivenessMirror();
        using var firstAttached = first.AttachLiveness(firstMirror);
        using var secondAttached = second.AttachLiveness(secondMirror);

        firstMirror.NoteCreated(Prop);

        Assert.True(first.EntityIsValid(Prop), "the side whose mirror was told answers alive");
        Assert.True(!second.EntityIsValid(Prop), "and the other side, told nothing, does not");
    }

    //-------------------------------------------------------------------------
    // The divergence gate: what cs_sv_liveness_check reports, and what it does not.
    //-------------------------------------------------------------------------

    private static List<LivenessComparison> Compare(bool claimed, bool mirrorAlive, bool slotAlive)
    {
        var rows = new List<LivenessComparison>();

        LivenessCheckCommand.Compare(7u, "ss_prop", claimed, mirrorAlive, slotAlive, rows.Add);

        return rows;
    }

    [Fact]
    public void AgreementReportsNothing()
    {
        Assert.True(Compare(claimed: true, mirrorAlive: true, slotAlive: true).Count == 0,
            "a mirror that equals its slot reports nothing at all");
    }

    [Fact]
    public void AnUnclaimedHandleIsNotADivergence()
    {
        Assert.True(Compare(claimed: false, mirrorAlive: false, slotAlive: true).Count == 0,
            "a live pBase entity or worldspawn is a FALL-THROUGH, not an error: the mirror never "
            + "claimed it and the slot answered it correctly at the cost of a crossing");
    }

    /// <summary>
    /// THE DANGEROUS DIRECTION, and the one a gate that only walked the world's
    /// live entities could not see — a freed entity is not there to be walked.
    /// </summary>
    [Fact]
    public void AMissedRemovalPushIsReported()
    {
        var rows = Compare(claimed: true, mirrorAlive: true, slotAlive: false);

        Assert.True(rows.Count == 1 && rows[0].Mirror == "alive" && rows[0].Slot == "gone",
            "a lifetime path that freed an entity without pushing leaves the mirror claiming it "
            + "forever — managed would go on addressing something that is gone");
    }

    [Fact]
    public void ASpuriousRemovalIsReported()
    {
        var rows = Compare(claimed: true, mirrorAlive: false, slotAlive: true);

        Assert.True(rows.Count == 1 && rows[0].Mirror == "gone" && rows[0].Slot == "alive",
            "the visible half of a divergence: managed stops believing in an entity the engine is "
            + "still simulating, and it silently drops out of every managed system at once");
    }

    //-------------------------------------------------------------------------
    // THE LIFETIME CENSUS, AS FACTS.
    //
    // This is the half of the mitigation a grep in a commit message cannot do.
    // A push mirror is exactly as correct as the closure of its push set, and
    // that set is in C++ where no managed compiler is watching it. A second
    // construction site added next month is a red test here; without these it
    // is an entity managed believes in for the rest of the map and a fortnight
    // of looking for the reason.
    //-------------------------------------------------------------------------

    /// <summary>
    /// EVERY server shell translation unit, comments stripped.
    ///
    /// <para>
    /// The census facts that used to name one file each are swept over the
    /// directory instead. A per-file check answers "is the push still in the
    /// file I read", which is not the question — the question is "is there a
    /// second push anywhere", and a push moved into a sibling satisfies the
    /// first and violates the second. It is also what made the old shape
    /// deletable-and-still-green: the file went, the lookup returned null, and
    /// the fact passed.
    /// </para>
    /// </summary>
    private static IEnumerable<(string File, string Code)> ServerShellSources()
    {
        string? one = RepoLayout.FindSdkFile(Path.Combine("game", "server", "shell_dispatch.cpp"));

        Assert.True(one is not null,
            "the server shell directory is in the compat farm — the attribute on the caller has "
            + "already established that, so a null here means the layout moved mid-run");

        string dir = Path.GetDirectoryName(one!)!;

        foreach (string path in Directory.EnumerateFiles(dir, "shell_*.cpp")
                                         .OrderBy(p => p, StringComparer.Ordinal))
        {
            yield return (Path.GetFileName(path), StripComments(File.ReadAllText(path)));
        }
    }

    /// <summary>
    /// The same, for the MANAGED half of the push set. Step 2c moved the removal
    /// record out of C++, so the census has to be able to read both sides.
    /// </summary>
    private static string? ManagedServerCode(string file)
    {
        // The directory and registry left Abi for SourceSharp.Server in P1 run 3 (plan M3).
        string? path = RepoLayout.FindSdkFile(Path.Combine("managed", "SourceSharp.Server", file));

        return path is null ? null : StripComments(File.ReadAllText(path));
    }

    private static string StripComments(string code)
    {
        code = Regex.Replace(code, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        code = Regex.Replace(code, @"//[^\n]*", " ");
        return Regex.Replace(code, "\"(\\\\.|[^\"\\\\])*\"", "\"\"");
    }

    /// <summary>
    /// <b>THE FACT THAT STOOD HERE HAD NO SUBJECT LEFT.</b>
    ///
    /// <para>
    /// <c>TheRemovalPushReachesABodyFromTheBaseDestructor</c> asserted that
    /// <c>CStandaloneEntity::OnManagedRemoved</c> was declared out of line and
    /// that its body pushed <c>ENTITY_OP_REMOVED</c>. Step 2c DELETED
    /// <c>OnManagedRemoved</c> — <c>shell_standalone.h</c> says so in as many
    /// words — and step 1f deleted <c>shell_standalone_scripted.cpp</c>, the
    /// file it read the body out of. It went on passing anyway, because its
    /// not-found arm was <c>Assert.True(true, "…skipped…")</c>: it reported a
    /// pass for every run after the deletion while examining nothing.
    /// </para>
    ///
    /// <para>
    /// It is deleted rather than repointed because the property it asserted is
    /// now the opposite of the truth, and the truth is already asserted twice:
    /// by <see cref="TheDestructorNoLongerPushesARemoval"/> here and by
    /// <c>standalone_system_test.cpp</c> natively.
    /// <c>SourceFileNamesExistTests</c> is what would have caught it the day 1f
    /// landed.
    /// </para>
    /// </summary>
    /// <summary>
    /// THE REMOVAL PUSH IS MADE FROM NO NATIVE PLACE AT ALL — step 2c.
    ///
    /// <para>
    /// This fact used to read <c>shell_standalone_scripted.cpp</c> and assert
    /// EXACTLY ONE <c>ENTITY_OP_REMOVED</c> push. Both halves have moved: 1f
    /// deleted that file, and 2c moved the record into the directory's removal
    /// pass, so the number the census has to hold is now ZERO — one inbound
    /// crossing per entity removed, gone. Left as it was, it read a file that
    /// no longer exists, took its not-found arm, and passed.
    /// </para>
    ///
    /// <para>
    /// Repointed at the WHOLE server shell directory rather than at the one
    /// file that used to carry the push, because "no push anywhere" is the
    /// property and a per-file check can be satisfied by moving the push to a
    /// file the check does not read. Anchored on the OP and not on the local
    /// holding the API pointer, for the reason the old fact recorded: written
    /// as <c>pApi-&gt;OnScriptedRemoved(</c> it stayed green under a planted
    /// second push through a pointer named <c>pProbe</c>.
    /// </para>
    /// </summary>
    [RepoSourceFact("src/sourcesharp/game/server/shell_dispatch.cpp")]
    public void TheRemovalPushIsMadeFromNoNativePlaceAtAll()
    {
        var pushes = new List<string>();

        foreach ((string file, string code) in ServerShellSources())
        {
            int count = Regex.Matches(
                code, @"DispatchOp\(\s*this,\s*ENTITY_OP_REMOVED\s*,").Count;

            if (count > 0)
                pushes.Add($"{file} x{count}");
        }

        Assert.True(pushes.Count == 0,
            "step 2c took the removal push out of C++ and gave the record to the directory's "
            + "removal pass. A native push back is a SECOND writer of one record: the two would "
            + "not have to agree and the one that ran second would win silently. Found: "
            + string.Join(", ", pushes));
    }

    /// <summary>
    /// One spawn, one push — and the file it is in has MOVED.
    ///
    /// <para>
    /// The push is <c>shell_dispatch.cpp</c>'s now; 1f deleted
    /// <c>shell_standalone_scripted.cpp</c>, which this fact still named. The
    /// property is unchanged and still worth holding, so this one is repointed
    /// rather than deleted — swept over the whole shell directory, so a second
    /// push added in a file this fact did not think to read is still red.
    /// </para>
    /// </summary>
    [RepoSourceFact("src/sourcesharp/game/server/shell_dispatch.cpp")]
    public void TheSpawnPushIsMadeFromExactlyOnePlace()
    {
        // The op, not the local. See the note on the removal push above.
        int pushes = ServerShellSources().Sum(
            s => Regex.Matches(s.Code, @"DispatchOp\(\s*this,\s*ENTITY_OP_SPAWN\s*,").Count);

        Assert.True(pushes == 1,
            "one spawn, one push. A second push site is a second place the managed instance can "
            + $"be told it exists, and the two would not have to agree; found {pushes}");
    }

    /// <summary>
    /// THE ORDERING X0 NAMED: there is no invalidate-without-record.
    ///
    /// <para>
    /// <b>IT IS NOT A DESTRUCTOR ORDERING ANY MORE.</b> Until
    /// <c>plan_entity.md</c> step 2c the push was <c>OnManagedRemoved()</c> in
    /// <c>~CStandaloneEntity</c> and this fact read that destructor for it,
    /// asserting the push came before <c>BlockHandle().Term()</c>: reversed, the
    /// handle would already be dead when the push carried it,
    /// <c>ManagedHandle</c> would answer 0, and every death would silently stop
    /// being recorded. Step 2c deleted <c>OnManagedRemoved</c> — one inbound
    /// crossing per entity removed — and the record is written by managed code,
    /// so the SAME PROPERTY now lives in two places and this fact reads both.
    /// </para>
    ///
    /// <para>
    /// <b>The property, unchanged:</b> the mirror is told while the handle still
    /// resolves. <c>EntityDirectory.FreeRemoved</c> calls
    /// <see cref="EntityDirectory.RemovalNotify"/> for the whole batch BEFORE it
    /// unbinds any of it and before it asks for a single shell to be freed, and
    /// the registry's notify is what writes the record.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRecordIsWrittenWhileTheHandleStillResolves()
    {
        string? directory = ManagedServerCode("EntityDirectory.cs");
        string? registry = ManagedServerCode("ScriptedEntity.cs");

        if (directory is null || registry is null)
        {
            Assert.Fail("the directory and registry are found for the lifetime census: a missing input is a failure");
            return;
        }

        int free = directory.IndexOf("public int FreeRemoved()", StringComparison.Ordinal);
        Assert.True(free > 0, "the free pass is where the census expects it");

        int notify = directory.IndexOf("RemovalNotify(_batch[i])", free, StringComparison.Ordinal);
        int unbind = directory.IndexOf("Unbind(_batch[i])", free, StringComparison.Ordinal);
        int destroy = directory.IndexOf("ShellDestroyer?.Invoke", free, StringComparison.Ordinal);

        Assert.True(notify > 0 && unbind > 0 && destroy > 0,
            "the notify, the unbind and the destroy are all in the free pass");

        Assert.True(notify < unbind,
            "the entity is told while it still resolves. Reversed, the registry's notify would "
            + "resolve nothing, OnRemoved would never run and the mirror would be written for an "
            + "entity nothing could look up");

        Assert.True(unbind < destroy,
            "and the shell is freed last of all, so nothing the notify does can touch memory the "
            + "allocator has already taken back");

        Assert.True(registry.Contains("_liveness.NoteRemoved(handle);", StringComparison.Ordinal),
            "and the record itself is written by the registry's removal notify, which is the one "
            + "thing left that is told about every death");
    }

    //-------------------------------------------------------------------------
    // plan_abi_hotreload D7: the mirror is the server composition's, so a hot
    // reload carries its records to the next one as bytes.
    //-------------------------------------------------------------------------

    [Fact]
    public void ASavedMirrorRestoresItsLiveAndDeadRecords()
    {
        var saved = new LivenessMirror();
        saved.NoteCreated(Prop);
        saved.NoteCreated(Other);
        saved.NoteRemoved(Other);

        var restored = new LivenessMirror();

        Assert.Equal(2, restored.Restore(saved.Save()));
        Assert.True(restored.TryLookup(Prop, out bool propAlive) && propAlive, "the live record is live");
        Assert.True(restored.TryLookup(Other, out bool otherAlive) && !otherAlive, "the death is kept");
        Assert.Equal((2, 1), (restored.RecordCount, restored.LiveCount));
    }

    [Fact]
    public void TheRestoredDeadEvictInTheOrderTheyDied()
    {
        var saved = new LivenessMirror();
        for (uint i = 1; i <= LivenessMirror.MaxRecords; i++)
        {
            saved.NoteCreated(H(0x0100_0000u + i));
            saved.NoteRemoved(H(0x0100_0000u + i));
        }

        var restored = new LivenessMirror();
        restored.Restore(saved.Save());
        restored.NoteRemoved(H(0x0200_0001u));

        Assert.False(restored.TryLookup(H(0x0100_0001u), out _), "the first to die is the first evicted");
        Assert.True(restored.TryLookup(H(0x0100_0002u), out _), "and only it");
    }

    [Fact]
    public void TheLivenessRerouteSettingTravelsWithTheRecords()
    {
        var saved = new LivenessMirror { Rerouted = false };
        var restored = new LivenessMirror();

        Assert.True(restored.Rerouted, "a fresh mirror is rerouted (the setup took effect below)");
        restored.Restore(saved.Save());

        Assert.False(restored.Rerouted, "an operator's cs_sv_liveness_reroute 0 survives the reload");
    }

    [Fact]
    public void ALivenessRecordAlreadyInTheNewMirrorWins()
    {
        var saved = new LivenessMirror();
        saved.NoteCreated(Prop);

        var restored = new LivenessMirror();
        restored.NoteCreated(Prop);
        restored.NoteRemoved(Prop);

        Assert.Equal(0, restored.Restore(saved.Save()));
        Assert.True(restored.TryLookup(Prop, out bool alive) && !alive, "the newer death is kept");
        Assert.Equal(0, restored.LiveCount);
    }

    [Fact]
    public void ATruncatedLivenessSaveIsRefusedAndAdoptsNothing()
    {
        var saved = new LivenessMirror();
        saved.NoteCreated(Prop);
        saved.NoteCreated(Other);
        saved.NoteRemoved(Other);
        byte[] bytes = saved.Save();

        var restored = new LivenessMirror();

        Assert.Equal(2, new LivenessMirror().Restore(bytes));
        Assert.Equal(-1, restored.Restore(bytes.AsSpan(0, bytes.Length - 1)));
        Assert.Equal(0, restored.RecordCount);
    }
    [Fact]
    public void ACarriedDeathDoesNotKillALiveRecordInTheNewMirror()
    {
        var saved = new LivenessMirror();
        saved.NoteCreated(Prop);
        saved.NoteRemoved(Prop);

        var restored = new LivenessMirror();
        restored.NoteCreated(Prop);

        Assert.Equal(0, restored.Restore(saved.Save()));
        Assert.True(restored.TryLookup(Prop, out bool alive) && alive, "the newer spawn is kept");
        Assert.Equal(1, restored.LiveCount);
    }

    //-------------------------------------------------------------------------
    // The attachment: how a composition's mirror reaches EntityIsValid.
    //-------------------------------------------------------------------------

    [Fact]
    public void DisposingAnAttachmentPutsBackTheMirrorBeforeIt()
    {
        var side = TestSides.Create(Side.Server);
        var first = new LivenessMirror();
        var second = new LivenessMirror();

        using var a = side.AttachLiveness(first);
        var b = side.AttachLiveness(second);
        Assert.Same(second, side.Liveness.Mirror);

        b.Dispose();

        Assert.Same(first, side.Liveness.Mirror);
    }

    [Fact]
    public void DisposingAnOlderAttachmentLeavesTheNewerOneAttached()
    {
        // A reload's order: the new module composes before nothing else, but a
        // module disposed late must not take away its successor's mirror.
        var side = TestSides.Create(Side.Server);
        var older = side.AttachLiveness(new LivenessMirror());
        var newer = new LivenessMirror();
        using var b = side.AttachLiveness(newer);

        older.Dispose();

        Assert.Same(newer, side.Liveness.Mirror);
    }
}
