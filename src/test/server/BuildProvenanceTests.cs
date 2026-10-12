/*
 * Where a build came from: branch, commit, and whether the tree was dirty.
 *
 * The rule this is built around is that A BUILD WITH UNCOMMITTED WORK MUST NOT
 * PRESENT AS A CLEAN NAMED COMMIT. Every way of not knowing is provoked here,
 * because each of them renders as something, and "clean" is the one thing none
 * of them may render as.
 */

using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

public class BuildProvenanceTests
{
    [Fact]
    public void TheOrdinaryCaseIsBranchAndCommit()
    {
        var git = GitProvenance.Parse(
            "branch=example-trunk-branch\ncommit=45e801f2\ndirty=0\nbuilt=2026-08-18T20:53:01Z\n");

        Assert.True(git.Branch == "example-trunk-branch", "the branch is read");
        Assert.True(git.Commit == "45e801f2", "the commit is read");
        Assert.True(git.Dirty == false, "and a clean tree is known to be clean");
        Assert.True(git.Describe() == "example-trunk-branch@45e801f2",
            "a clean build reads as its branch and commit and nothing else");
    }

    [Fact]
    public void UncommittedWorkIsSaidOutLoud()
    {
        var git = GitProvenance.Parse("branch=example-trunk-branch\ncommit=45e801f2\ndirty=1\n");

        Assert.True(git.Describe() == "example-trunk-branch@45e801f2+dirty",
            "a build containing uncommitted work says so, because it is not that commit");
    }

    [Fact]
    public void ADetachedHeadIsNamedRatherThanLeftBlank()
    {
        // Normal here: an agent resets to a sha before it starts work.
        var git = GitProvenance.Parse("branch=\ncommit=45e801f2\ndirty=0\n");

        Assert.True(git.Describe() == "(detached)@45e801f2",
            "no branch is a state with a name, not an empty string");
    }

    [Fact]
    public void AWorktreeBranchIsTheBranchThatIsReported()
    {
        // The distinction that matters when several games are up: each was built
        // in its own worktree, and each must stamp its own branch.
        var agent = GitProvenance.Parse("branch=example-feature-a1b2c3\ncommit=45e801f2\ndirty=1\n");
        var trunk = GitProvenance.Parse("branch=example-trunk-branch\ncommit=45e801f2\ndirty=0\n");

        Assert.True(agent.Describe() != trunk.Describe(),
            "two games built from the same commit on different branches do not read alike");
    }

    //-------------------------------------------------------------------------
    // Not knowing. Three different kinds of it, none of which may read as clean.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnUnaskableGitLeavesDirtinessUnknownRatherThanClean()
    {
        var git = GitProvenance.Parse("branch=example-trunk-branch\ncommit=45e801f2\n");

        Assert.True(git.Dirty is null, "a missing dirty field is not a clean tree");
        Assert.True(git.Describe().EndsWith("+?"),
            "and renders as a question rather than silently as clean");
    }

    [Fact]
    public void AnUnreadableDirtyFieldIsAlsoUnknown()
    {
        var git = GitProvenance.Parse("branch=b\ncommit=c\ndirty=maybe\n");

        Assert.True(git.Dirty is null, "anything that is not 0 or 1 is not an answer");
        Assert.True(git.Describe() == "b@c+?", "and does not read as clean");
    }

    [Fact]
    public void NoStampFileAtAllReadsAsUnknown()
    {
        Assert.True(!GitProvenance.Unknown.IsKnown, "nothing known is nothing known");
        Assert.True(GitProvenance.Unknown.Describe() == "unknown",
            "a build with no provenance says so rather than naming a branch it guessed");

        var missing = GitProvenance.Read("/nonexistent/sourcesharp.build");
        Assert.True(missing == GitProvenance.Unknown, "a missing file is unknown, not a throw");
    }

    [Fact]
    public void TheFileMayGrowWithoutBreakingAnOlderGame()
    {
        var git = GitProvenance.Parse(
            "branch=b\ncommit=c\ndirty=1\nbuilt=2026-08-18T20:53:01Z\nsomethingnew=42\n\n");

        Assert.True(git.Describe() == "b@c+dirty", "unknown keys and blank lines are ignored");
    }

    [Fact]
    public void ALineWithNoValueIsNotAKey()
    {
        var git = GitProvenance.Parse("=nonsense\nbranch=b\ncommit=c\ndirty=0\n");

        Assert.True(git.Describe() == "b@c", "a malformed line is skipped rather than read as a field");
    }

    //-------------------------------------------------------------------------
    // Which file the native half is running from.
    //-------------------------------------------------------------------------

    private static readonly string[] Maps =
    {
        "55b0e4a00000-55b0e4a01000 r--p 00000000 08:02 1234567  /usr/bin/hl2_linux",
        "7f8e00000000-7f8e00021000 rw-p 00000000 00:00 0 ",
        "7f8e10000000-7f8e10a00000 r-xp 00000000 08:02 7654321  /mod/bin/linux64/server.so",
        "7f8e20000000-7f8e21000000 r-xp 00000000 08:02 7654322  /mod/bin/linux64/client.so",
        "7ffd00000000-7ffd00022000 rw-p 00000000 00:00 0                          [stack]",
    };

    [Fact]
    public void TheRunningLibraryIsFoundByName()
    {
        var server = ProcMaps.Locate(Maps, "server.so");
        var client = ProcMaps.Locate(Maps, "client.so");

        Assert.True(server?.Path == "/mod/bin/linux64/server.so", "the server library is located");
        Assert.True(client?.Path == "/mod/bin/linux64/client.so", "and the client one separately");
        Assert.True(server?.Replaced == false, "a mapping of a file still on disk is not replaced");
    }

    [Fact]
    public void AnonymousAndSpecialMappingsAreNotLibraries()
    {
        Assert.True(ProcMaps.Locate(Maps, "[stack]") is null,
            "the stack is not a file, whatever column it occupies");
        Assert.True(ProcMaps.Locate(Maps, "nothing.so") is null,
            "and a library that is not mapped is not found");
    }

    [Fact]
    public void AReplacedLibraryIsReportedAsReplaced()
    {
        // What a rebuild looks like from inside a process that is still running
        // the old inode: the build renames a new file over the old one on
        // purpose, so the running pages survive and the mapping goes stale.
        string[] afterRebuild =
        {
            "7f8e10000000-7f8e10a00000 r-xp 00000000 08:02 7654321  /mod/bin/linux64/server.so (deleted)",
        };

        var server = ProcMaps.Locate(afterRebuild, "server.so");

        Assert.True(server?.Path == "/mod/bin/linux64/server.so", "the path is still the path");
        Assert.True(server?.Replaced == true, "and the game is told it is running a file that has gone");
    }

    //-------------------------------------------------------------------------
    // A SPACE IN THE PATH, which is not a hypothetical.
    //
    // Every engine module this process loads lives under
    // steamapps/common/Source SDK Base 2013 Multiplayer/, and the parser used
    // to split on every space and take the sixth field -- so engine.so came
    // back as ".../steamapps/common/Source" and matched nothing. The defect was
    // invisible for as long as the only thing asked about was the mod's own
    // .so, which has no space in its path, and it surfaced the first time
    // anything asked where the ENGINE was.
    //-------------------------------------------------------------------------

    private const string SpacedPath =
        "/opt/games/.steam/steamapps/common/Source SDK Base 2013 Multiplayer/bin/linux64/engine.so";

    private static readonly string[] SpacedMaps =
    {
        "7f8e30000000-7f8e31000000 r-xp 00000000 08:02 7654323  " + SpacedPath,
    };

    [Fact]
    public void ALibraryUnderAPathWithSpacesIsFound()
    {
        var engine = ProcMaps.Locate(SpacedMaps, "engine.so");

        Assert.True(engine is not null, "engine.so is mapped and has to be found");
        Assert.True(engine?.Path == SpacedPath, "and the whole path comes back, spaces included");
    }

    [Fact]
    public void ALibraryUnderAPathWithSpacesIsStillSeenAsReplaced()
    {
        string[] afterRebuild = { SpacedMaps[0] + " (deleted)" };

        var engine = ProcMaps.Locate(afterRebuild, "engine.so");

        Assert.True(engine?.Path == SpacedPath, "the marker is not read as part of the filename");
        Assert.True(engine?.Replaced == true, "and it is still reported as replaced");
    }

    // The marker is stripped as a SUFFIX after a space, so a file whose own
    // name ends in those characters keeps them. Without this the fix above
    // would have traded one silently-wrong path for another.
    [Fact]
    public void AFileWhoseNameEndsInTheMarkerTextIsNotTreatedAsDeleted()
    {
        string[] maps =
        {
            "7f8e40000000-7f8e41000000 r-xp 00000000 08:02 7654324  /mod/bin/lib (deleted).so",
        };

        var module = ProcMaps.Locate(maps, "lib (deleted).so");

        Assert.True(module?.Path == "/mod/bin/lib (deleted).so", "the name is left alone");
        Assert.True(module?.Replaced == false, "and nothing was deleted");
    }
}
