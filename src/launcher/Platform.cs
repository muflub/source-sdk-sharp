using System.Runtime.InteropServices;

namespace SourceSharp.Launcher;

/// <summary>
/// What differs between the platforms the engine runs on, as the launcher
/// needs it: where the engine keeps its libraries and which Steam API library
/// it ships (Valve's bin/linux64 and bin/x64 layouts), how a role's
/// executable is named there, which environment variable the loader reads the
/// library path from, and where instance game dirs are made. One record per
/// platform; <see cref="Current"/> is this process's. The Linux values are the
/// ones the launcher was written with and every existing fact pins; Windows'
/// are the same things spelled for its loader. No macOS: Valve no longer
/// supports the Source SDK 2013 games there.
/// </summary>
internal sealed record LauncherPlatform(
    string Name,
    string BinDir,
    string SteamLibrary,
    string LibraryPathVariable,
    char PathListSeparator,
    bool HasProcFs)
{
    /// <summary>bin/linux64, libsteam_api.so, LD_LIBRARY_PATH, /tmp/ssi-&lt;user&gt;.</summary>
    public static readonly LauncherPlatform Linux = new("linux", "linux64", "libsteam_api.so", "LD_LIBRARY_PATH", ':', true);

    /// <summary>
    /// bin/x64, steam_api64.dll, and PATH: Windows' loader searches it for a
    /// DLL's dependencies, which is what LD_LIBRARY_PATH does for hl2.sh.
    /// </summary>
    public static readonly LauncherPlatform Windows = new("windows", "x64", "steam_api64.dll", "PATH", ';', false);

    /// <summary>The platform this launcher is running on.</summary>
    public static LauncherPlatform Current =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Windows : Linux;

    /// <summary>
    /// The executable a role names in mods.yaml, which spells the Linux build
    /// (hl2_linux64, srcds_linux64), as this platform's engine ships it:
    /// Valve's launcher_main.vpc names one binary per platform from the same
    /// stem (hl2_win64.exe). A name that is not the Linux spelling
    /// is taken as given, so a mod can name a platform's own executable.
    /// </summary>
    public string ExeName(string exe)
    {
        const string linux = "_linux64";
        if (Name == "linux" || !exe.EndsWith(linux, StringComparison.Ordinal))
            return exe;
        string stem = exe[..^linux.Length];
        return stem + "_win64.exe";
    }

    /// <summary>
    /// The engine library directory of an install: &lt;install&gt;/bin/&lt;BinDir&gt;.
    /// </summary>
    public string LibraryDir(string install) => Path.Combine(install, "bin", BinDir);

    /// <summary>
    /// Where instance game dirs are made when <see cref="Launcher.InstanceRootVariable"/>
    /// does not say: /tmp/ssi-&lt;user&gt; where there is a /tmp (short and fixed,
    /// never $TMPDIR: the game dir is -game, and libtier0 refuses a command
    /// line whose arguments add up to more than 512 bytes), and the user's
    /// temporary directory on Windows, which has no /tmp.
    /// </summary>
    public string DefaultInstanceBase(string userName) =>
        Name == "windows"
            ? Path.Combine(Path.GetTempPath(), "ssi-" + userName)
            : "/tmp/ssi-" + userName;
}
