using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// STEP 8f-movetypes' PREDICTION SIDE: the three arms it ported are answered by
/// the same C# on both sides, so parity is a question about their INPUTS, not
/// about their arithmetic.
///
/// <para>
/// The server fills <c>MoveState</c> from the crossing
/// (<c>CGameMovement::SourceSharpProcessMovement</c>); the client's replay
/// fills its own from the networked player state
/// (<c>ClientPlayerPrediction.SetupMove</c>). A field the server reads and the
/// client leaves at its default is a divergence that appears ONLY under
/// prediction and ONLY on the arm that reads it — which is the failure mode
/// this whole design exists to prevent, and the reason these facts read the two
/// sources rather than stating a conclusion.
/// </para>
///
/// <para>
/// The three arms read exactly three fields the walking path did not:
/// <c>MoveType</c> (which selects the arm), <c>MoveCollide</c> (which
/// <c>PerformFlyCollisionResolution</c> switches on) and <c>ObserverMode</c>
/// (which <c>FullObserverMove</c> switches on). The observer mode was the hole
/// and is now populated from the networked state.
/// </para>
/// </summary>
public class MoveTypePredictionParityTests
{
    private const string ClientReplay =
        "src/sourcesharp/managed/SourceSharp.Client/ClientPlayerPrediction.cs";
    private const string StandalonePlayerHeader =
        "src/sourcesharp/game/server/shell_standalone_player.h";
    private const string StandalonePlayerCpp =
        "src/sourcesharp/game/server/shell_standalone_player.cpp";
    private const string Movement =
        "src/sourcesharp/managed/SourceSharp.Shared.Interop/Movement.cs";

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(RepoLayout.SdkTree, relative));

    /// <summary>
    /// <c>MoveType</c> — the field that picks the arm — IS populated on the
    /// client's replay, from the networked player state. Without this the
    /// client would replay every command as <c>MOVETYPE_WALK</c> whatever the
    /// server ran, and the two sides would disagree on the very first flying
    /// command.
    /// </summary>
    [Fact]
    public void TheClientReplayPopulatesTheMoveTypeThatSelectsTheArm()
    {
        Assert.Contains("_move.MoveType = _working.MoveType;", Read(ClientReplay), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>MoveCollide</c> is NOT populated on the client's replay — and it is
    /// not a divergence, because the server's value is a constant. The
    /// standalone player constructs it <c>MOVECOLLIDE_DEFAULT</c> and every
    /// write to it in the whole shim writes <c>MOVECOLLIDE_DEFAULT</c> back, so
    /// the server's crossing always carries 0 and the client's default is 0.
    ///
    /// <para>
    /// Read out of the sources rather than asserted, so that the day something
    /// gives a player <c>MOVECOLLIDE_FLY_BOUNCE</c> — which is what would make
    /// a flier bounce instead of stopping — this goes red and the field has to
    /// go on the wire.
    /// </para>
    /// </summary>
    [Fact]
    public void TheServersMoveCollideIsAlwaysTheDefaultSoNotSendingItIsSafe()
    {
        Assert.Contains("m_MoveCollide( MOVECOLLIDE_DEFAULT )", Read(StandalonePlayerCpp), StringComparison.Ordinal);

        // The third source was stock's game/shared/gamemovement.cpp, whose
        // writes were all MOVECOLLIDE_DEFAULT. Neither DLL compiles it now (the
        // movement is managed) and the farm no longer links it; the one
        // compiled writer left is the shim's hand-back of the managed movement
        // state, which is the `state.nMoveCollide` form accepted below.
        string[] sources =
        {
            Read(StandalonePlayerCpp), Read(StandalonePlayerHeader),
            Read("src/sourcesharp/game/shared/shell_movement.cpp"),
        };

        var writes = sources
            .SelectMany(s => s.Split('\n'))
            .Where(l => l.Contains("MvSetMoveCollide(", StringComparison.Ordinal))
            .Where(l => !l.Contains("virtual", StringComparison.Ordinal))
            .Select(l => l.Trim())
            .ToList();

        Assert.NotEmpty(writes);
        Assert.All(writes, l => Assert.True(
            l.Contains("MOVECOLLIDE_DEFAULT", StringComparison.Ordinal) ||
            l.Contains("state.nMoveCollide", StringComparison.Ordinal),
            "a write to the player's move collide that is not the default: " + l));
    }

    /// <summary>
    /// <c>ObserverMode</c> IS populated on the client's replay, from the
    /// networked <c>m_iObserverMode</c>. This was the open hole while nothing
    /// set <c>MOVETYPE_OBSERVER</c>; the managed observer (lane player-msgs)
    /// sets it, and the replay now runs <c>FullObserverMove</c> in the
    /// server's mode (behaviour: <c>ClientPlayerPredictionTests.
    /// AnObserverIsReplayedInItsNetworkedObserverMode</c>).
    /// </summary>
    [Fact]
    public void TheClientReplayPopulatesTheObserverMode()
    {
        Assert.Contains("_move.ObserverMode = _working.ObserverMode;", Read(ClientReplay), StringComparison.Ordinal);
    }

    /// <summary>
    /// The measurement the whole step rests on: NO compiled translation unit
    /// writes any of the three move types onto a player. Every mention across
    /// the sources server.so and client.so compile is a reader or a
    /// <c>switch</c> case.
    ///
    /// <para>
    /// It is what makes removing <c>gamemovement.cpp</c> safe — the stock
    /// bodies being deleted have never run for a player in this build — and it
    /// is what makes the live gate need a probe. Resolved against the build's
    /// compiled set (<see cref="CMakeGraph.GameSources"/>), which replaced the
    /// parsed <c>$File</c> rows of the two vpcs.
    /// </para>
    /// </summary>
    [Fact]
    public void NoCompiledSourceSetsAnyOfTheThreeMoveTypesOnAPlayer()
    {
        string root = RepoLayout.SdkTree;

        // CMakeGraph throws on a missing, empty or stale build, and every file
        // it names must be read, so "nothing found" cannot be a list or a path
        // that went unread. The sibling fact below proves the scan can see.
        foreach (string relative in CompiledSourceFiles())
        {
            string path = Path.Combine(root, relative);
            Assert.True(File.Exists(path), path + " is compiled into the game DLLs but is not there");

            foreach (string line in File.ReadAllLines(path))
            {
                if (!line.Contains("MOVETYPE_OBSERVER", StringComparison.Ordinal) &&
                    !line.Contains("MOVETYPE_FLY", StringComparison.Ordinal))
                    continue;

                Assert.False(
                    line.Contains("SetMoveType(", StringComparison.Ordinal) ||
                    line.Contains("SetMoveType (", StringComparison.Ordinal),
                    relative + " sets one of the three ported move types: " + line.Trim());
            }
        }
    }

    /// <summary>
    /// ...and the scan is not one that cannot fail: the SAME scan finds the
    /// writers of the move types that ARE set in this build, so a parser that
    /// silently read nothing would be caught here rather than reported as a
    /// clean bill above.
    /// </summary>
    [Fact]
    public void TheSameScanDoesFindTheMoveTypesThisBuildDoesSet()
    {
        string root = RepoLayout.SdkTree;
        int setters = 0;
        int constants = 0;

        foreach (string relative in CompiledSourceFiles())
        {
            string path = Path.Combine(root, relative);
            Assert.True(File.Exists(path), path + " is compiled into the game DLLs but is not there");

            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Contains("SetMoveType(", StringComparison.Ordinal) ||
                    line.Contains("SetMoveType (", StringComparison.Ordinal))
                    setters++;

                if (line.Contains("MOVETYPE_", StringComparison.Ordinal))
                    constants++;
            }
        }

        // THE TWO HALVES ARE COUNTED SEPARATELY, and that is the change lane
        // 3c-entityshell made to this fact.
        //
        // It used to require at least one line carrying BOTH halves of the
        // sibling's predicate — a SetMoveType call and a MOVETYPE_ constant —
        // and the floor was 5, then 1. The survivor at 1 was the native noclip
        // branch in shell_standalone_player.cpp:
        //
        //     pPlayer->MvSetMoveType( bNoclip ? MOVETYPE_NOCLIP : MOVETYPE_WALK );
        //
        // noclip is a managed [ConCommand] now, over a PlayerSetMoveType
        // crossing whose body takes the move type as an ARGUMENT — so the call
        // and the constant are no longer on one line anywhere in the compiled
        // set, and the conjunction went to 0. THAT IS NOT A REGRESSION IN THE
        // SCAN and it is not a return of the defect: the build has one fewer
        // native move-type decision than it had, which is the direction this
        // whole class is about.
        //
        // But a fact whose subject has left cannot be repaired by lowering its
        // floor to 0 — that is precisely the check that cannot fail. So each
        // half is now proved on its own, which is strictly stronger: the
        // sibling's predicate is `SetMoveType(...) && MOVETYPE_...`, and a
        // parser blind to EITHER half would give it a clean bill for the wrong
        // reason. Requiring one line with both only ever tested them together.
        Assert.True(setters >= 1,
                    "the scan found no SetMoveType call at all in the compiled set, so the "
                    + "first half of its sibling's predicate is untested and that fact's "
                    + "clean bill could be a parser failure rather than a finding");

        Assert.True(constants >= 1,
                    "the scan found no MOVETYPE_ constant at all in the compiled set, so the "
                    + "second half of its sibling's predicate is untested for the same reason");
    }

    /// <summary>
    /// The three arms are on the one <c>GameMovement</c> class the client's
    /// replay calls, so a flying or observing command runs the SAME C# on both
    /// sides. This is the structural half of prediction parity, and it is
    /// pinned because the alternative — a client-side copy of the three arms —
    /// is the two-copies bug the whole port exists to remove.
    /// </summary>
    [Fact]
    public void TheClientReplayRunsTheSameGameMovementLoopTheServerCrossingDoes()
    {
        string client = Read(ClientReplay);
        Assert.Contains("_movement.ClientLoop(", client, StringComparison.Ordinal);
        Assert.Contains("loop.ProcessMovement(_move)", client, StringComparison.Ordinal);

        string movement = Read(Movement);
        Assert.Contains("private void FullObserverMove()", movement, StringComparison.Ordinal);
        Assert.Contains("private void FullTossMove()", movement, StringComparison.Ordinal);
        Assert.Contains("private void PerformFlyCollisionResolution(in MoveTrace pm)", movement,
                        StringComparison.Ordinal);
    }

    /// <summary>
    /// Every source server.so and client.so compile, as an SdkTree-relative
    /// path (<c>src/game/server/player.cpp</c>). It was every <c>$File</c> row
    /// of the two vpcs until VPC was replaced.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<string> CompiledSourceFiles() =>
        CMakeGraph.GameSources().Select(rel => "src/" + rel);
}
