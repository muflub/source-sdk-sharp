using SourceSharp.Abi;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client;

/// <summary>
/// WHICH BUILD THIS GAME IS RUNNING, in the bottom right corner.
///
/// <para>
/// This exists because of a specific hour that was wasted: somebody read a pane
/// in a running game, reported what it said, and was right about what was on
/// screen and wrong about the code — their game had been started before the
/// change landed, and nothing anywhere could say which build it was. Two lines
/// in a corner answer that without asking anybody anything.
/// </para>
///
/// <para>
/// BOTH HALVES, because they legitimately diverge. A hot reload replaces the
/// managed half in place while native stays as it was, and the reading
/// "n 19:04 … m 20:31" is the useful one — collapsing them into one number
/// would have to hide the reload or lie about native. ONE LINE, compact: a
/// four-character hash per half and the source once when both halves share it,
/// so the corner stays small in a screenshot.
/// </para>
///
/// <para>
/// THE DEBUG LINE top right, when the HUD supplies one: the engine clock and
/// where the view is, so a screenshot can be matched to a log or a trace.
/// </para>
///
/// <para>
/// ON BY DEFAULT, behind <c>cs_cl_build_stamp</c>. Off by default would be the
/// polite choice and it is the wrong one: the failure this prevents is somebody
/// looking at a screen and not thinking to ask which build it is, and a stamp
/// they would have had to turn on first is a stamp that would not have been
/// there. It is small, dim, and in a corner nothing else uses; the convar is for
/// screenshots, and it archives so turning it off sticks.
/// </para>
/// </summary>
public sealed class HudBuildStamp : IBuildStampSource
{
    /// <param name="screen">Where the two lines are drawn.</param>
    /// <param name="build">
    /// This side's build stamps (statics to zero): its <see cref="SideState"/>'s,
    /// handed in by the composition, and the default answer of the four readers
    /// below.
    /// </param>
    public HudBuildStamp(IEngineHud screen, BuildInfo build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _ = screen;
        Native = _ => build.Native;
        Managed = _ => build.Managed;
        NativeFreshness = _ => build.NativeFreshness;
        ManagedFreshness = _ => build.ManagedFreshness;
    }

    /// <summary>
    /// Whether to draw. A delegate rather than the convar itself because the HUD
    /// is built before the convars are registered — and because a test can then
    /// drive both answers without binding one.
    /// </summary>
    public Func<bool> Visible { get; set; } = static () => true;

    /// <summary>
    /// Where the stamps come from: the side's own <see cref="BuildInfo"/> by
    /// default, and replaceable so a test can drive any half without writing
    /// one.
    /// </summary>
    public Func<Side, BuildHalf> Native { get; set; }

    /// <inheritdoc cref="Native"/>
    public Func<Side, BuildHalf> Managed { get; set; }

    /// <inheritdoc cref="Native"/>
    public Func<Side, BuildFreshness> NativeFreshness { get; set; }

    /// <inheritdoc cref="Native"/>
    public Func<Side, BuildFreshness> ManagedFreshness { get; set; }

    /// <summary>How much of each half's content hash is shown.</summary>
    public const int HashChars = 4;

    /// <summary>
    /// The debug line drawn in the top right, or null for none. Supplied by the
    /// HUD; a delegate so a test can drive it.
    /// </summary>
    public Func<string?> DebugLine { get; set; } = static () => null;

    /// <summary>
    /// One half: tag, warning, local build time, short hash —
    /// <c>n 19:04:12 1a0e</c>. The warning goes before the stamp, so a line too
    /// wide for the window loses the tail rather than the advice.
    /// </summary>
    public static string Segment(string tag, BuildHalf half, BuildFreshness freshness)
    {
        string hash = half.Stamp.Hash.Length > HashChars
            ? half.Stamp.Hash[..HashChars]
            : half.Stamp.Hash;

        return $"{tag}{Warning(tag, freshness)} {half.Stamp.BuiltUtc.ToLocalTime():HH:mm:ss} {hash}";
    }

    /// <summary>
    /// What the stale warning says for half <paramref name="tag"/>: native cannot
    /// be reloaded into a running process, managed can.
    /// </summary>
    public static string Warning(string tag, BuildFreshness freshness)
        => freshness == BuildFreshness.Stale
            ? (tag == "n" ? " REBUILT-restart" : " REBUILT-cs_cl_reload")
            : string.Empty;

    /// <summary>
    /// The whole stamp: <c>n 19:04:12 1a0e · m 20:31:40 4337 · branch@commit</c>,
    /// the source once when the halves share it and per half when they do not.
    /// A half never stamped is left out — a corner reading "unknown" looks like
    /// a reading. Empty when neither is known.
    /// </summary>
    public static string Line(BuildHalf native, BuildFreshness nativeFresh,
                              BuildHalf managed, BuildFreshness managedFresh)
    {
        var parts = new List<string>(3);
        bool shared = native.Stamp.IsKnown && managed.Stamp.IsKnown && native.Source == managed.Source;

        if (native.Stamp.IsKnown)
            parts.Add(Segment("n", native, nativeFresh) + (shared ? string.Empty : $" {native.Source.Describe()}"));

        if (managed.Stamp.IsKnown)
            parts.Add(Segment("m", managed, managedFresh) + (shared ? string.Empty : $" {managed.Source.Describe()}"));

        if (shared)
            parts.Add(native.Source.Describe());

        return string.Join(" · ", parts);
    }

    // ---- the UI's view (plan_client_ui U2): IBuildStampSource ----

    /// <summary>How often the debug line is re-read: it formats a string, so not every frame.</summary>
    public const double DebugLineSeconds = 0.25;

    private BuildHalf _native, _managed;
    private BuildFreshness _nativeFresh, _managedFresh;
    private string _line = string.Empty;
    private bool _lineBuilt;
    private string _debug = string.Empty;
    private double _debugAt = double.NegativeInfinity;

    /// <summary>
    /// Once a frame, before the UI describes the HUD: the stamp's line is
    /// rebuilt only when a half or its freshness changed, and the debug line
    /// at most every <see cref="DebugLineSeconds"/>, so a frame allocates nothing.
    /// </summary>
    public void Refresh(double now)
    {
        var native = Native(Side.Client);
        var managed = Managed(Side.Client);
        var nativeFresh = NativeFreshness(Side.Client);
        var managedFresh = ManagedFreshness(Side.Client);
        if (!_lineBuilt || native != _native || managed != _managed
            || nativeFresh != _nativeFresh || managedFresh != _managedFresh)
        {
            (_native, _managed, _nativeFresh, _managedFresh) = (native, managed, nativeFresh, managedFresh);
            _line = Line(native, nativeFresh, managed, managedFresh);
            _lineBuilt = true;
        }

        if (now < _debugAt || now - _debugAt >= DebugLineSeconds)
        {
            _debugAt = now;
            _debug = DebugLine() ?? string.Empty;
        }
    }

    bool IBuildStampSource.Visible => Visible();

    string IBuildStampSource.Line => _line;

    bool IBuildStampSource.Stale => _nativeFresh == BuildFreshness.Stale || _managedFresh == BuildFreshness.Stale;

    string IBuildStampSource.DebugLine => _debug;
}
