namespace SourceSharp.OracleTests;

/// <summary>
/// The reference every oracle here reads is the one valve-sdk.lock pins. A
/// clone that moved (a pull, a checkout of master) would silently change what
/// "stock" means for every test in this project.
/// </summary>
public sealed class ValveSdkTests
{
    [Fact]
    public void TheCloneIsCheckedOutAtTheLockedUpstreamCommit()
    {
        string lockSha = ValveSdk.Lock;
        Assert.Matches("^[0-9a-f]{40}$", lockSha);
        Assert.Equal(lockSha, ValveSdk.Head());
    }

    [Fact]
    public void TheCloneIsNotTheFarm()
    {
        // The farm's ROOT may be this clone (both are stock); the farm itself,
        // which links our own files in beside Valve's, never is.
        string root = Path.GetFullPath(ValveSdk.Root);
        string repo = Path.GetFullPath(ValveSdk.RepoRoot);
        Assert.False(root.StartsWith(repo + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            $"{root} is inside this repository (the farm?)");
    }

    [Fact]
    public void AMissingFileIsAFailureNotAnEmptyRead()
    {
        Assert.Throws<FileNotFoundException>(() => ValveSdk.Read("src/game/server/no_such_file.cpp"));
    }
}
