namespace SourceSharp.Shared;

/// <summary>This process' own command line: see <see cref="LocalCommandLine"/>.</summary>
public interface ILocalCommandLine
{
    /// <summary>
    /// This process' arguments, argv[0] included, exactly as it was started,
    /// then the flags the launcher moved into <see cref="ProcessCommandLine.LaunchArgsVariable"/>.
    /// </summary>
    IReadOnlyList<string> Args { get; }
}

/// <summary>
/// The command line as this process was started with it, read once and kept.
/// One per module that registers it; it was a static cache on
/// <see cref="LocalCommandLine"/>.
/// </summary>
public sealed class ProcessCommandLine : ILocalCommandLine
{
    /// <summary>
    /// This process' arguments, argv[0] included, exactly as it was started.
    ///
    /// <para>
    /// <b>FROM <c>/proc/self/cmdline</c>, AND <c>Environment.GetCommandLineArgs</c>
    /// IS THE FALLBACK RATHER THAN THE ANSWER.</b> This runtime is not the
    /// process' entry point: <c>sourcesharp.cpp</c> loads hostfxr into a game
    /// that is already running and starts CoreCLR as a COMPONENT, which is never
    /// handed a command line — so <c>GetCommandLineArgs</c> answers the
    /// executable path and nothing else. A live run is what said so: the server
    /// logged "offered client 1 the debug channel", the client logged nothing,
    /// and <c>-debugui</c> was on the command line the whole time.
    /// </para>
    ///
    /// <para>
    /// <c>/proc/self/cmdline</c> is NUL-separated argv and is the process' own,
    /// whoever started the runtime. Linux-only, which this mod is. The fallback
    /// is there so a test host or another platform gets something rather than
    /// throwing.
    /// </para>
    ///
    /// <para>
    /// Cached: a command line does not change while a process runs, and this is
    /// read on a path that runs at every level load.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Args => _args ??= WithLaunchArgs(Read(), Environment.GetEnvironmentVariable(LaunchArgsVariable));

    private string[]? _args;

    /// <summary>
    /// SOURCESHARP_ARGS: SourceSharp's own flags, one per line, which the
    /// launcher (src/launcher/Launcher.cs) moves off the engine's command line
    /// because libtier0 refuses one over 512 bytes. They read as if they
    /// followed argv; native code (ss_command_line.cpp) reads them the same way.
    /// </summary>
    public const string LaunchArgsVariable = "SOURCESHARP_ARGS";

    /// <summary><paramref name="argv"/>, then each non-empty line of <paramref name="launchArgs"/>.</summary>
    public static string[] WithLaunchArgs(string[] argv, string? launchArgs) =>
        string.IsNullOrEmpty(launchArgs)
            ? argv
            : [.. argv, .. launchArgs.Split('\n', StringSplitOptions.RemoveEmptyEntries)];

    private static string[] Read()
    {
        try
        {
            if (File.Exists("/proc/self/cmdline"))
            {
                string[] parts = File.ReadAllText("/proc/self/cmdline")
                                     .Split('\0', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 0)
                    return parts;
            }
        }
        catch
        {
            // Not readable here. The fallback below is still an answer.
        }

        return Environment.GetCommandLineArgs();
    }
}
