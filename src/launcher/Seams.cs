namespace SourceSharp.Launcher;

// The four things the launcher does to the outside world, each behind an
// interface so every branch of Launcher.Run is a unit test with fakes: Steam,
// starting the engine, the file-descriptor limit, and the error dialog.

/// <summary>What asking Steam for the engine install came to.</summary>
internal readonly record struct SteamAnswer(string? InstallDir, string? Error)
{
    public static SteamAnswer Found(string dir) => new(dir, null);
    public static SteamAnswer Failed(string error) => new(null, error);
}

internal interface ISteam
{
    /// <summary>
    /// Loads <paramref name="libraryPath"/>, writes steam_appid.txt with
    /// <paramref name="modAppId"/> in the working directory, initialises Steam,
    /// and asks where <paramref name="engineAppId"/> is installed. Shuts Steam
    /// down again before returning, as the native launcher did.
    /// </summary>
    SteamAnswer FindInstallDir(string libraryPath, uint modAppId, uint engineAppId);
}

/// <summary>How a run of the engine ended: its exit code, and the pid it ran as.</summary>
internal readonly record struct GameExit(int Code, int Pid);

internal interface IProcessRunner
{
    /// <summary>Runs to completion with the launcher's stdio; returns how it ended.</summary>
    GameExit Run(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env, string workingDir);
}

internal interface IRlimit
{
    /// <summary>Sets RLIMIT_NOFILE (soft and hard, as `ulimit -n` does). Null on success, else why not.</summary>
    string? SetNoFile(int limit);
}

internal interface IMessageBox
{
    /// <summary>Shows an error dialog; false when none could be shown.</summary>
    bool Show(string title, string text, string? engineInstallDir);
}
