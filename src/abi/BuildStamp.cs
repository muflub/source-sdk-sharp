using System.Security.Cryptography;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// Whether the build that is RUNNING is still the build that is on disk.
/// </summary>
public enum BuildFreshness
{
    /// <summary>Nothing to compare — no stamp, or the files could not be read.</summary>
    Unknown,

    /// <summary>What is loaded is what was built.</summary>
    Current,

    /// <summary>Something was built after this was loaded. The game is running old code.</summary>
    Stale,
}

/// <summary>
/// Which build one half of the game is running.
///
/// <para>
/// The question this answers is "is this game running the code I just built?",
/// and it exists because that question has cost real time here: a running game
/// showed every entity on one net table, which was true of the build it was
/// running and had been false for an hour, and nothing on screen or in the dev
/// UI could say which build that was.
/// </para>
///
/// <para>
/// THE HASH IS OF CONTENT, not of a commit. A git sha is the wrong answer in
/// this tree: the normal state is a dirty working tree, and a build made from
/// it is not the commit it claims. "clean sha abc1234" over a build containing
/// uncommitted work is worse than no stamp at all, because it is confidently
/// wrong. A hash over the bytes that were loaded cannot be wrong about what was
/// loaded — dirty, stashed, rebased or detached.
/// </para>
///
/// <para>
/// THE TIME IS WHEN THOSE BYTES WERE WRITTEN, taken from the files as they were
/// read. Not a compile-time constant: <c>__DATE__</c> in C++ is baked per
/// translation unit and goes stale the moment a build is incremental, and
/// ccache is told here to ignore it outright (CCACHE_SLOPPINESS=time_macros),
/// so a constant would be a stamp that lies after exactly the builds this is for.
/// </para>
/// </summary>
public readonly record struct BuildStamp(string Hash, DateTime BuiltUtc, int Parts)
{
    /// <summary>Nothing has been stamped. Says so rather than reading as a build.</summary>
    public static readonly BuildStamp Unknown = new(string.Empty, default, 0);

    /// <summary>Whether this stamp came from anything at all.</summary>
    public bool IsKnown => Parts > 0 && Hash.Length > 0;

    /// <summary>
    /// Local time, because the reader is comparing it against when THEY pressed
    /// build. UTC would be correct and useless.
    /// </summary>
    public string Describe() => IsKnown ? $"{BuiltUtc.ToLocalTime():HH:mm:ss} {Hash}" : "unknown";

    public override string ToString() => Describe();

    /// <summary>
    /// Milliseconds since the epoch, or 0 when there is no stamp. The form the
    /// dev API already uses for times, so the page formats it in the reader's
    /// own zone rather than parsing one out of a string.
    /// </summary>
    public long BuiltAtUnixMs
        => IsKnown ? new DateTimeOffset(DateTime.SpecifyKind(BuiltUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds() : 0;

    /// <summary>
    /// A filesystem's write time is not infinitely precise and a build writes
    /// several files over some seconds. A second of slack keeps the ordinary
    /// case from reading as stale.
    /// </summary>
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether what is on disk now is newer than what was loaded.
    ///
    /// This is the whole point of keeping the load time rather than reading the
    /// files on demand: after a rebuild the files on disk change and the running
    /// code does not, and only a stamp taken at load can tell the reader that.
    /// </summary>
    public BuildFreshness Against(DateTime? diskWrittenUtc)
    {
        if (!IsKnown || diskWrittenUtc is not { } disk || disk == default)
            return BuildFreshness.Unknown;

        return disk > BuiltUtc + Slack ? BuildFreshness.Stale : BuildFreshness.Current;
    }
}

/// <summary>
/// Accumulates the files that make up one half of a build and reduces them to
/// one short hash and one time.
///
/// <para>
/// Order-independent on purpose: the assemblies of a side load in whatever order
/// the first type reference in each demands, and a stamp that changed because a
/// convar was read before a command would be noise indistinguishable from a real
/// change. Parts are sorted by name before they are combined.
/// </para>
/// </summary>
public sealed class BuildStampBuilder
{
    private readonly SortedDictionary<string, string> _parts = new(StringComparer.Ordinal);
    private DateTime _newestUtc;

    /// <summary>How many files have gone in.</summary>
    public int Count => _parts.Count;

    /// <summary>The names, in the order they are combined in.</summary>
    public IReadOnlyCollection<string> Names => _parts.Keys;

    /// <summary>Adds a file already in memory. Re-adding a name replaces it.</summary>
    public BuildStampBuilder Add(string name, ReadOnlySpan<byte> content, DateTime writtenUtc)
        => Add(name, HashOf(content), writtenUtc);

    /// <summary>Adds a file whose content hash is already known.</summary>
    public BuildStampBuilder Add(string name, string contentHash, DateTime writtenUtc)
    {
        _parts[name] = contentHash;

        if (writtenUtc > _newestUtc)
            _newestUtc = writtenUtc;

        return this;
    }

    /// <summary>
    /// Reads a file and adds it. False rather than a throw when it is not there:
    /// a stamp missing a part is worth having, and a load that failed because a
    /// debugging aid could not read a file would be indefensible.
    /// </summary>
    public bool AddFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            Add(Path.GetFileName(path), bytes, File.GetLastWriteTimeUtc(path));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The stamp. <see cref="BuildStamp.Unknown"/> when nothing went in, which
    /// is a different thing from a stamp over zero bytes.
    /// </summary>
    public BuildStamp Build()
    {
        if (_parts.Count == 0)
            return BuildStamp.Unknown;

        var combined = new StringBuilder();

        foreach (var part in _parts)
            combined.Append(part.Key).Append('\0').Append(part.Value).Append('\n');

        return new BuildStamp(Short(Encoding.UTF8.GetBytes(combined.ToString())), _newestUtc, _parts.Count);
    }

    /// <summary>A file's content hash, full length.</summary>
    public static string HashOf(ReadOnlySpan<byte> content)
        => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>
    /// Eight hex characters. Long enough that two builds colliding is not a
    /// thing that happens, short enough to sit in a HUD corner and be read out
    /// loud over a call — which is how this one gets used.
    /// </summary>
    private static string Short(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..8];
}
