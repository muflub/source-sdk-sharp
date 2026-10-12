using System.Diagnostics;

namespace SourceSharp.Abi;

/// <summary>
/// One half of the build: what was loaded, and where it came from.
/// </summary>
/// <param name="Stamp">Hash and time, over the bytes that were loaded.</param>
/// <param name="Source">Branch, commit and dirtiness, as of the build.</param>
/// <param name="Path">The file or directory it was read from. Empty when unknown.</param>
/// <param name="Replaced">
/// The file has already been replaced on disk since it was mapped. Only native
/// can know this about itself; see <see cref="MappedModule"/>.
/// </param>
public readonly record struct BuildHalf(
    BuildStamp Stamp, GitProvenance Source, string Path, bool Replaced)
{
    public static readonly BuildHalf Unknown
        = new(BuildStamp.Unknown, GitProvenance.Unknown, string.Empty, false);

    /// <summary>Hash, time and branch, in the order a reader wants them.</summary>
    public string Describe()
        => Stamp.IsKnown ? $"{Stamp.Describe()} {Source.Describe()}" : "unknown";

    public override string ToString() => Describe();
}

/// <summary>
/// WHICH BUILD THIS SIDE IS RUNNING, for each half.
///
/// <para>
/// TWO STAMPS, NOT ONE, and that is the whole design. The native half and the
/// managed half of this mod are built separately and <c>cs_sv_reload</c>
/// replaces the managed one without restarting the process — so "the build" is
/// not a single thing, and a single number would have to either hide a reload or
/// lie about the native half. Seeing that managed is from 20:31 while native is
/// from 19:04 is the useful reading, not a defect in the reporting.
/// </para>
///
/// <para>
/// One per side, held by the side's <see cref="SideState"/> (statics to zero),
/// which the Bootstrap keeps for the side root's life. That placement is load
/// bearing: the native stamp is taken once, at first load, and must survive
/// every managed reload afterwards, because the native code in this process
/// cannot change. State in a side assembly would be recreated by a reload and
/// would restamp native from whatever is on disk by then — which is precisely
/// the lie this exists to prevent. Its readers (the HUD, the dev API, IGameBuild)
/// are handed their own side's by the composition.
/// </para>
/// </summary>
public sealed class BuildInfo
{
    private BuildHalf _managed = BuildHalf.Unknown;
    private BuildHalf _native = BuildHalf.Unknown;

    /// <summary>The managed assemblies this side loaded, as of its last load.</summary>
    public BuildHalf Managed => _managed;

    /// <summary>The .so this side is running, as of process start.</summary>
    public BuildHalf Native => _native;

    /// <summary>
    /// Records the managed half, and the files it was made of.
    ///
    /// THE FILE LIST IS NOT DECORATION. Freshness is checked against exactly the
    /// assemblies THIS SIDE loaded, not against the directory: the server loads
    /// four and the client three, and rebuilding only the client's would
    /// otherwise tell the server it was running stale code because something in
    /// its directory had a newer date. That false positive was live for about
    /// three minutes and is the reason this takes a list.
    /// </summary>
    public void SetManaged(BuildHalf half, IReadOnlyCollection<string> files)
    {
        _managed = half;
        _managedFiles = files;

        // A new stamp is a new answer; the cached freshness beside it described
        // the half this one replaced.
        _checkedAtMs = -1;
    }

    private IReadOnlyCollection<string> _managedFiles = Array.Empty<string>();

    /// <summary>
    /// Records the native half. Called once; a second call is ignored,
    /// because native cannot be reloaded and a later reading would describe a
    /// file this process is not running.
    /// </summary>
    public void SetNativeOnce(BuildHalf half)
    {
        if (!_native.Stamp.IsKnown)
            _native = half;
    }

    //-------------------------------------------------------------------------
    // Is what is on disk newer than what is loaded?
    //
    // This is the question behind the whole feature. It is answered by comparing
    // the time recorded AT LOAD against the files as they are now, which is why
    // the stamp keeps a time at all rather than reading the files on demand.
    //-------------------------------------------------------------------------

    /// <summary>
    /// How often disk is looked at again. The HUD asks every frame and the dev
    /// UI asks every second; neither wants a stat call behind it, and a rebuild
    /// noticed two seconds late is noticed.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Stopwatch _since = Stopwatch.StartNew();

    /// <summary>
    /// When this side last looked, in milliseconds since this instance woke up.
    ///
    /// Negative means never, rather than a TimeSpan far in the past: subtracting
    /// TimeSpan.MinValue from anything overflows, which is a throw on the first
    /// poll of /status and not a late refresh. Caught by the route's own test.
    /// </summary>
    private long _checkedAtMs = -1;
    private BuildFreshness _managedFresh = BuildFreshness.Unknown;
    private BuildFreshness _nativeFresh = BuildFreshness.Unknown;

    /// <summary>Whether the managed half on disk is newer than the one running.</summary>
    public BuildFreshness ManagedFreshness
    {
        get
        {
            Refresh();
            return _managedFresh;
        }
    }

    /// <summary>Whether the native half on disk is newer than the one running.</summary>
    public BuildFreshness NativeFreshness
    {
        get
        {
            Refresh();
            return _nativeFresh;
        }
    }

    private void Refresh()
    {
        long now = _since.ElapsedMilliseconds;

        if (_checkedAtMs >= 0 && now - _checkedAtMs < (long)Interval.TotalMilliseconds)
            return;

        _checkedAtMs = now;

        _managedFresh = _managed.Stamp.Against(NewestOnDisk(_managed.Path, _managedFiles));

        // A mapping the kernel calls deleted is stale whatever the timestamps
        // say, and it is the only reading available once the file it names has
        // been replaced by a different inode.
        _nativeFresh = _native.Replaced && _native.Stamp.IsKnown
            ? BuildFreshness.Stale
            : _native.Stamp.Against(WrittenOnDisk(_native.Path));
    }

    private static DateTime? NewestOnDisk(string directory, IReadOnlyCollection<string> files)
    {
        if (directory.Length == 0 || files.Count == 0)
            return null;

        try
        {
            DateTime newest = default;

            foreach (string file in files)
            {
                string path = Path.Combine(directory, file);
                if (!File.Exists(path))
                    continue;

                var written = File.GetLastWriteTimeUtc(path);
                if (written > newest)
                    newest = written;
            }

            return newest == default ? null : newest;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DateTime? WrittenOnDisk(string path)
    {
        if (path.Length == 0)
            return null;

        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
