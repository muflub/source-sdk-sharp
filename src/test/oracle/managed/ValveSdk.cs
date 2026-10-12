namespace SourceSharp.OracleTests;

/// <summary>
/// Where an oracle reads Valve's code: the pristine upstream clone at the
/// commit in <c>valve-sdk.lock</c>.
///
/// <para>It is <c>$SOURCESHARP_VALVE_SDK</c> when set (the Makefile exports
/// it), else the path <c>make setup</c> recorded in
/// <c>valve-sdk.lock.local</c>. There is no fallback to the fork or the farm:
/// the farm holds only what the game compiles, and the fork is our edited
/// copy. A missing clone, a missing file in it, or a clone off the lock
/// THROWS, so an oracle can never pass by reading nothing.</para>
/// </summary>
internal static class ValveSdk
{
    private const string Marker = "SourceSharp.slnx";

    private static readonly Lazy<string> RootLazy = new(FindRoot);
    private static readonly Lazy<string> RepoLazy = new(FindRepo);

    /// <summary>This repository's root (holds SourceSharp.slnx).</summary>
    public static string RepoRoot => RepoLazy.Value;

    /// <summary>The clone's root directory.</summary>
    public static string Root => RootLazy.Value;

    /// <summary>The commit valve-sdk.lock pins.</summary>
    public static string Lock =>
        File.ReadAllText(Path.Combine(RepoRoot, "valve-sdk.lock")).Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries)[0];

    /// <summary>A file of the clone, by its path from the clone's root
    /// (<c>src/game/server/te_sparks.cpp</c>); throws when it is not there.</summary>
    public static string Find(string relative)
    {
        string path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"the pristine Valve SDK at {Root} has no {relative}: an oracle's reference must exist", path);
        return path;
    }

    /// <summary>A file's text, as <see cref="Find"/> finds it.</summary>
    public static string Read(string relative) => File.ReadAllText(Find(relative));

    /// <summary>A file under <c>src/game/server</c> (the usual place).</summary>
    public static string Server(string fileName) => Find("src/game/server/" + fileName);

    /// <summary>The clone's checked-out commit, read from its .git.</summary>
    public static string Head()
    {
        string git = Path.Combine(Root, ".git");
        string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
        if (!head.StartsWith("ref: ", StringComparison.Ordinal))
            return head;
        string refPath = Path.Combine(git, head[5..]);
        if (File.Exists(refPath))
            return File.ReadAllText(refPath).Trim();
        foreach (string line in File.ReadAllLines(Path.Combine(git, "packed-refs")))
        {
            if (line.EndsWith(" " + head[5..], StringComparison.Ordinal))
                return line[..40];
        }

        throw new InvalidOperationException($"cannot resolve {head} in {git}");
    }

    private static string FindRepo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, Marker)))
                return dir.FullName;
        }

        throw new InvalidOperationException(
            $"no {Marker} above {AppContext.BaseDirectory}: the oracle tests run from bin/<config>/oracle-tests of this repo");
    }

    private static string FindRoot()
    {
        string? env = Environment.GetEnvironmentVariable("SOURCESHARP_VALVE_SDK");
        string local = Path.Combine(RepoRoot, "valve-sdk.lock.local");
        string? root = !string.IsNullOrEmpty(env) ? env
            : File.Exists(local) ? File.ReadAllText(local).Trim() : null;
        if (root is null)
        {
            throw new InvalidOperationException(
                "no pristine Valve SDK: $SOURCESHARP_VALVE_SDK is unset and there is no "
                + "valve-sdk.lock.local. Run `make setup` (it clones upstream at valve-sdk.lock).");
        }

        if (!Directory.Exists(Path.Combine(root, "src", "game")))
        {
            throw new InvalidOperationException(
                $"the pristine Valve SDK {root} is missing or not a source-sdk-2013 checkout: run `make setup`");
        }

        return root;
    }
}
