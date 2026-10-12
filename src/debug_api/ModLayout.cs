using System.Reflection;

namespace Debug.Api;

/// <summary>
/// Where this mod's files are.
///
/// Everything this API reads off disk — the map it is looking at, the console
/// log it follows — lives in the mod directory, and nothing loaded into the game
/// can find that by asking the runtime. The load context reads each assembly
/// into memory so the files stay unlocked, so Assembly.Location is empty for
/// every assembly in it, and a path built from "" resolves against the working
/// directory: the Steam install, three levels away from this mod and holding
/// neither the maps nor the log.
///
/// The symptom was a debugger that worked perfectly in tests and showed an empty
/// map list and an empty console feed in the game it was written for.
///
/// So the bootstrap says. It is loaded by the host from a path and does have a
/// Location, and it publishes the directory before any module starts.
///
/// <para>
/// An instance over ONE side's host (plan_abi_hotreload D8), handed the host
/// by the server that owns it, rather than a static reading the active side's.
/// </para>
/// </summary>
internal sealed class ModLayout
{
    private readonly Func<string> _managedDirectory;

    /// <param name="managedDirectory">Where the host says the managed assemblies are; empty when it does not know.</param>
    private readonly Func<string?> _gameDirectory;

    /// <param name="managedDirectory">Where the managed assemblies are.</param>
    /// <param name="gameDirectory">
    /// The engine's <c>-game</c> directory, when the command line names one.
    /// </param>
    public ModLayout(Func<string> managedDirectory, Func<string?>? gameDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(managedDirectory);
        _managedDirectory = managedDirectory;
        _gameDirectory = gameDirectory ?? (() => null);
    }

    /// <summary>
    /// The directory holding the managed assemblies.
    ///
    /// The bootstrap's answer first; then the runtime's own base directory,
    /// which is right for the tests and the harness, where this assembly is
    /// loaded normally and there is no bootstrap at all.
    /// </summary>
    public string ManagedDirectory
    {
        get
        {
            string spoken = _managedDirectory();

            if (!string.IsNullOrEmpty(spoken))
                return spoken;

            string? here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            return string.IsNullOrEmpty(here) ? AppContext.BaseDirectory : here;
        }
    }

    /// <summary>
    /// The mod directory: two levels up, since the assemblies sit in
    /// &lt;mod&gt;/bin/managed.
    ///
    /// Empty when the layout is not that — a test host, or anything else that
    /// does not look like an installed mod. Empty rather than a guess, so that
    /// what reads it can say "I do not know where to look" instead of reporting
    /// a path that was never going to work.
    /// </summary>
    /// <summary>
    /// The mod's directory: the engine's <c>-game</c> when it names one that
    /// exists, else two above the managed directory.
    ///
    /// <para>
    /// <c>-game</c> FIRST, because the deduction stopped being true: since the
    /// launcher reads mods.yaml, the managed assemblies are the game
    /// directory's shared <c>bin/managed</c> and the mod is
    /// <c>mods/&lt;id&gt;</c> beside it, so two levels up was <c>bin/&lt;config&gt;</c>
    /// -- and /console followed a console.log that never existed (measured
    /// 2026-10-04: <c>following:false</c> in a game started with -condebug).
    /// </para>
    /// </summary>
    public string ModDirectory
        => _gameDirectory() is { Length: > 0 } game && Path.IsPathRooted(game) && Directory.Exists(game)
            ? game
            : Above(ManagedDirectory, 2);

    /// <summary>The directory <paramref name="levels"/> above a path, or "".</summary>
    internal static string Above(string path, int levels)
    {
        string? at = path;

        for (int i = 0; i < levels && !string.IsNullOrEmpty(at); i++)
            at = Path.GetDirectoryName(at);

        return at ?? string.Empty;
    }
}
