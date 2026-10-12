namespace Debug.Api;

/// <summary>
/// The directories the environment may move: the per-process socket's
/// (<c>DEBUG_API_DIR</c>) and the maps' (<c>SOURCESHARP_MAPS</c>). Read ONCE,
/// where the server is composed (<see cref="FromEnvironment"/>), and handed to
/// it: the server reads no environment variable, so a test hands it its own
/// directories instead of moving a variable every other server in the process
/// would read.
/// </summary>
/// <param name="SocketDirectory">Where the per-process socket is made.</param>
/// <param name="MapDirectory">The maps directory named outright, or null to deduce it from the mod's layout.</param>
internal sealed record DebugApiDirectories(string SocketDirectory, string? MapDirectory)
{
    /// <summary>Nothing overridden: the shared socket directory, maps deduced.</summary>
    internal static DebugApiDirectories Default => new(DebugApiServer.DefaultSocketDirectory, null);

    /// <summary>The process environment's answer: the composition root's call.</summary>
    internal static DebugApiDirectories FromEnvironment() => From(Environment.GetEnvironmentVariable);

    /// <summary>
    /// The answer of an environment given as a lookup. An exported-but-empty
    /// variable is what a shell leaves behind when an expansion produced
    /// nothing: it reads as unset, for both.
    /// </summary>
    internal static DebugApiDirectories From(Func<string, string?> environment)
    {
        string? socket = environment(DebugApiServer.SocketDirectoryVar);
        string? maps = environment(MapSource.DirectoryVariable);
        return new(string.IsNullOrEmpty(socket) ? DebugApiServer.DefaultSocketDirectory : socket,
                   string.IsNullOrWhiteSpace(maps) ? null : maps);
    }
}
