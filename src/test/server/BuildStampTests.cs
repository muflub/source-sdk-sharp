/*
 * The build stamp: which build the game is running.
 *
 * What is checked here is everything that could make the stamp LIE, because a
 * stamp that does not move when the code moves — or that moves when it does not
 * — is worse than no stamp. Each rule below is provoked in both directions
 * rather than only in the one that passes.
 */

using System.Text;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

public class BuildStampTests
{
    private static readonly DateTime Noon = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    //-------------------------------------------------------------------------
    // The hash: it must move with content, and only with content.
    //-------------------------------------------------------------------------

    [Fact]
    public void ADifferentBuildHashesDifferently()
    {
        var before = new BuildStampBuilder().Add("a.dll", Bytes("one"), Noon).Build();
        var after  = new BuildStampBuilder().Add("a.dll", Bytes("two"), Noon).Build();

        Assert.True(before.Hash != after.Hash, "changing what was built changes the hash");
    }

    [Fact]
    public void TheSameBuildHashesTheSame()
    {
        var once  = new BuildStampBuilder().Add("a.dll", Bytes("one"), Noon).Build();
        var again = new BuildStampBuilder().Add("a.dll", Bytes("one"), Noon).Build();

        Assert.True(once.Hash == again.Hash, "the same bytes stamp the same way twice");
    }

    [Fact]
    public void TheOrderAssembliesLoadInDoesNotChangeTheHash()
    {
        // Assemblies load on first type reference, so the order varies with what
        // the module happens to touch first. A hash that moved with it would be
        // noise nobody could tell from a real change.
        var forwards = new BuildStampBuilder()
            .Add("a.dll", Bytes("one"), Noon)
            .Add("b.dll", Bytes("two"), Noon)
            .Build();

        var backwards = new BuildStampBuilder()
            .Add("b.dll", Bytes("two"), Noon)
            .Add("a.dll", Bytes("one"), Noon)
            .Build();

        Assert.True(forwards.Hash == backwards.Hash, "load order does not change the stamp");
    }

    [Fact]
    public void WhichFileChangedIsPartOfTheHash()
    {
        // Two assemblies swapping contents is a real change, and a hash over the
        // contents alone would call it the same build.
        var one = new BuildStampBuilder()
            .Add("a.dll", Bytes("one"), Noon)
            .Add("b.dll", Bytes("two"), Noon)
            .Build();

        var swapped = new BuildStampBuilder()
            .Add("a.dll", Bytes("two"), Noon)
            .Add("b.dll", Bytes("one"), Noon)
            .Build();

        Assert.True(one.Hash != swapped.Hash, "the same bytes in different files is a different build");
    }

    [Fact]
    public void AnExtraAssemblyChangesTheStamp()
    {
        var lean = new BuildStampBuilder().Add("a.dll", Bytes("one"), Noon).Build();

        var fuller = new BuildStampBuilder()
            .Add("a.dll", Bytes("one"), Noon)
            .Add("b.dll", Bytes("two"), Noon)
            .Build();

        Assert.True(lean.Hash != fuller.Hash, "loading one more assembly is a different build");
        Assert.True(fuller.Parts == 2, "the stamp says how many files went into it");
    }

    [Fact]
    public void TheStampIsShortEnoughToReadOut()
    {
        var stamp = new BuildStampBuilder().Add("a.dll", Bytes("one"), Noon).Build();

        Assert.True(stamp.Hash.Length == 8, "a stamp is eight hex characters");
        Assert.True(stamp.Hash.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')),
            "and lower-case hex, so two of them compare by eye");
    }

    //-------------------------------------------------------------------------
    // The time: newest wins, and nothing is a time.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheBuildTimeIsTheNewestFileInIt()
    {
        var stamp = new BuildStampBuilder()
            .Add("old.dll", Bytes("one"), Noon)
            .Add("new.dll", Bytes("two"), Noon.AddMinutes(5))
            .Add("mid.dll", Bytes("three"), Noon.AddMinutes(2))
            .Build();

        Assert.True(stamp.BuiltUtc == Noon.AddMinutes(5),
            "an incremental build is dated by the file it actually rewrote");
    }

    [Fact]
    public void NothingStampedIsNotABuild()
    {
        var stamp = new BuildStampBuilder().Build();

        Assert.True(!stamp.IsKnown, "a stamp over no files is not a build");
        Assert.True(stamp.Describe() == "unknown", "and says so rather than reading as one");
        Assert.True(stamp.BuiltAtUnixMs == 0, "with no time to report");
    }

    [Fact]
    public void AStampReadsAsATimeAndAHash()
    {
        var stamp = new BuildStamp("deadbeef", Noon, 3);

        // Local time on purpose: the reader is comparing this against when they
        // pressed build, not against UTC.
        Assert.True(stamp.Describe() == $"{Noon.ToLocalTime():HH:mm:ss} deadbeef",
            "a stamp reads as the time it was built and the hash of what was built");
        Assert.True(stamp.BuiltAtUnixMs == 1787054400000, "and carries the time as epoch milliseconds");
    }

    //-------------------------------------------------------------------------
    // Freshness: has anything been built since this was loaded?
    //-------------------------------------------------------------------------

    [Fact]
    public void ARebuildSinceLoadReadsAsStale()
    {
        var loaded = new BuildStamp("deadbeef", Noon, 1);

        Assert.True(loaded.Against(Noon.AddMinutes(1)) == BuildFreshness.Stale,
            "a newer build on disk means the game is running old code");
    }

    [Fact]
    public void NothingBuiltSinceLoadReadsAsCurrent()
    {
        var loaded = new BuildStamp("deadbeef", Noon, 1);

        Assert.True(loaded.Against(Noon) == BuildFreshness.Current,
            "the same files on disk means this is the build that was made");
        Assert.True(loaded.Against(Noon.AddMinutes(-5)) == BuildFreshness.Current,
            "and an older file on disk is not a rebuild either");
    }

    [Fact]
    public void ASecondOfSlackKeepsAnOrdinaryBuildFromReadingAsStale()
    {
        var loaded = new BuildStamp("deadbeef", Noon, 1);

        Assert.True(loaded.Against(Noon.AddMilliseconds(900)) == BuildFreshness.Current,
            "a build writes several files over a moment, and that is not a rebuild");
        Assert.True(loaded.Against(Noon.AddMilliseconds(1100)) == BuildFreshness.Stale,
            "past the slack it is");
    }

    [Fact]
    public void NotKnowingIsNotTheSameAsBeingCurrent()
    {
        Assert.True(BuildStamp.Unknown.Against(Noon) == BuildFreshness.Unknown,
            "with no stamp there is nothing to compare");
        Assert.True(new BuildStamp("deadbeef", Noon, 1).Against(null) == BuildFreshness.Unknown,
            "and with no files on disk there is nothing to compare against");
    }

    //-------------------------------------------------------------------------
    // The half: stamp plus provenance, as both surfaces read it.
    //-------------------------------------------------------------------------

    [Fact]
    public void AHalfReadsAsTimeHashAndBranch()
    {
        var half = new BuildHalf(
            new BuildStamp("deadbeef", Noon, 2),
            new GitProvenance("example-trunk-branch", "45e801f2", false),
            "/mod/bin/managed", false);

        Assert.True(half.Describe()
            == $"{Noon.ToLocalTime():HH:mm:ss} deadbeef example-trunk-branch@45e801f2",
            "a half reads as when, what, and where from");
    }

    [Fact]
    public void AHalfWithNoStampSaysSoRatherThanShowingABranchAlone()
    {
        var half = new BuildHalf(
            BuildStamp.Unknown,
            new GitProvenance("example-trunk-branch", "45e801f2", false),
            string.Empty, false);

        Assert.True(half.Describe() == "unknown",
            "a branch with nothing loaded behind it is not a build to report");
    }
}
