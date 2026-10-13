using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// A native library this process ALREADY loaded, found without ever loading
/// it: the engine's copy of the Steam API, or nothing.
///
/// <para>
/// WHY NEVER LOAD: a second copy of the Steam API is a second Steam context
/// talking to nobody. The launcher loads the library by path before the
/// engine does, so the one copy there is the engine's, and every binding
/// (the server's game server, the client's tickets, client API and
/// controller) asks for that copy or gets nothing.
/// </para>
///
/// <para>
/// HOW, per platform: on Linux <c>dlopen(RTLD_NOW | RTLD_NOLOAD)</c>, which
/// answers a loaded object by its soname and fails rather than loading; on
/// Windows <c>GetModuleHandleW</c>, which answers a module already in the
/// process by its file name and never loads one. The bindings were written
/// against the first alone (libdl.so.2 and libsteam_api.so), so on Windows
/// every one of them threw DllNotFoundException instead of binding.
/// </para>
/// </summary>
public static class LoadedLibrary
{
    /// <summary>The Steam API's file name on this platform: the name the launcher (LauncherPlatform) loads it by.</summary>
    public static string SteamApi => SteamApiFor(OperatingSystem.IsWindows());

    /// <summary>Either platform's name, so both are testable.</summary>
    public static string SteamApiFor(bool windows) => windows ? "steam_api64.dll" : "libsteam_api.so";

    /// <summary>
    /// How this platform looks a library up without loading it, for the
    /// reason a binding gives when the library is not there.
    /// </summary>
    public static string NoLoad => NoLoadFor(OperatingSystem.IsWindows());

    /// <summary>Either platform's mechanism, so both are testable.</summary>
    public static string NoLoadFor(bool windows) => windows ? "GetModuleHandle" : "RTLD_NOLOAD";

    /// <summary>The handle of the copy of <paramref name="file"/> this process already loaded, or zero. Never loads it.</summary>
    public static IntPtr Open(string file)
        => OperatingSystem.IsWindows() ? GetModuleHandleW(file) : DlOpen(file, RTLD_NOW | RTLD_NOLOAD);

    private const int RTLD_NOW = 2;
    private const int RTLD_NOLOAD = 4;

    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern IntPtr DlOpen(string file, int mode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr GetModuleHandleW(string moduleName);
}
