using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Launcher;

/// <summary>
/// Steam through libsteam_api.so's flat C exports -- no vtable walking. Every
/// export named here is in the tracked library (checked with nm -D). The
/// messages are the native launcher's, string for string.
/// </summary>
internal sealed unsafe class NativeSteam : ISteam
{
    public SteamAnswer FindInstallDir(string libraryPath, uint modAppId, uint engineAppId)
    {
        if (!NativeLibrary.TryLoad(libraryPath, out IntPtr lib))
        {
            // dlerror() is gone by the time TryLoad returns; Load's exception
            // carries it, which is the one thing that says WHY.
            string why;
            try
            {
                NativeLibrary.Free(NativeLibrary.Load(libraryPath));
                why = "unknown error";
            }
            catch (DllNotFoundException e)
            {
                why = e.Message;
            }

            return SteamAnswer.Failed($"Not able to load the Steam API.\n\nTried: {libraryPath}\n\n{why}");
        }

        try
        {
            // Before SteamAPI_Init, which reads it: the app Steam treats this as.
            try
            {
                File.WriteAllText("steam_appid.txt", $"{modAppId}\n");
            }
            catch (IOException)
            {
                // The native launcher ignored a failed fopen too; Init then says.
            }

            if (!NativeLibrary.TryGetExport(lib, "SteamAPI_Init", out IntPtr init))
                return SteamAnswer.Failed("SteamAPI_Init was not available!");

            if (((delegate* unmanaged<byte>)init)() == 0)
                return SteamAnswer.Failed("SteamAPI_Init failed!");

            try
            {
                if (!NativeLibrary.TryGetExport(lib, "SteamAPI_SteamApps_v008", out IntPtr appsFn) ||
                    !NativeLibrary.TryGetExport(lib, "SteamAPI_ISteamApps_BIsAppInstalled", out IntPtr installedFn) ||
                    !NativeLibrary.TryGetExport(lib, "SteamAPI_ISteamApps_GetAppInstallDir", out IntPtr dirFn))
                {
                    return SteamAnswer.Failed("ISteamApps not present!");
                }

                IntPtr apps = ((delegate* unmanaged<IntPtr>)appsFn)();
                if (apps == IntPtr.Zero)
                    return SteamAnswer.Failed("ISteamApps not present!");

                uint length = 0;
                byte* buf = stackalloc byte[4096];
                if (((delegate* unmanaged<IntPtr, uint, byte>)installedFn)(apps, engineAppId) != 0)
                    length = ((delegate* unmanaged<IntPtr, uint, byte*, uint, uint>)dirFn)(apps, engineAppId, buf, 4096);

                if (length == 0)
                {
                    return SteamAnswer.Failed(
                        $"{EngineName(engineAppId)} ({engineAppId}) must be installed to launch this mod.");
                }

                return SteamAnswer.Found(Marshal.PtrToStringUTF8((IntPtr)buf)!);
            }
            finally
            {
                if (NativeLibrary.TryGetExport(lib, "SteamAPI_Shutdown", out IntPtr shutdown))
                    ((delegate* unmanaged<void>)shutdown)();
            }
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    internal static string EngineName(uint appId) =>
        appId == Mods.ModsConfig.Sdk2013MpAppId ? "Source SDK 2013 Multiplayer" : "Steam app";
}

/// <summary>Starts the engine with the launcher's own stdio, and waits.</summary>
internal sealed class ProcessRunner : IProcessRunner
{
    public GameExit Run(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env, string workingDir)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDir,
        };
        foreach (string a in args)
            psi.ArgumentList.Add(a);

        psi.Environment.Clear();
        foreach (var (k, v) in env)
            psi.Environment[k] = v;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"could not start {exe}");

        // Ctrl-C reaches the whole process group, so the game gets it itself;
        // the launcher must not die first and orphan it. SIGTERM aimed at the
        // launcher alone is passed on.
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => c.Cancel = true);
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c =>
        {
            c.Cancel = true;
            try { process.Kill(); } catch (InvalidOperationException) { }
        });

        int pid = process.Id;
        process.WaitForExit();
        return new GameExit(process.ExitCode, pid);
    }
}

/// <summary>
/// `ulimit -n N`: both soft and hard, as bash's ulimit sets them. POSIX only:
/// Windows has no per-process file-descriptor limit to raise (the CRT's
/// handle table grows on its own), so there it is a no-op that succeeds.
/// </summary>
internal sealed partial class Rlimit : IRlimit
{
    private const int RLIMIT_NOFILE = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct RLimit
    {
        public ulong Cur;
        public ulong Max;
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int setrlimit(int resource, in RLimit rlim);

    public string? SetNoFile(int limit)
    {
        if (OperatingSystem.IsWindows())
            return null;
        var r = new RLimit { Cur = (ulong)limit, Max = (ulong)limit };
        return setrlimit(RLIMIT_NOFILE, in r) == 0
            ? null
            : Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError());
    }
}

/// <summary>
/// SDL_ShowSimpleMessageBox from the engine install's libSDL2 (or the
/// system's), as the native launcher's MessageBox macro was. On Windows the
/// system's own MessageBox: the engine's SDL2.dll is not always beside it
/// there, and user32 always is.
/// </summary>
internal sealed unsafe partial class SdlMessageBox : IMessageBox
{
    private const uint SDL_MESSAGEBOX_ERROR = 0x10;
    private const uint MB_OK = 0x0;
    private const uint MB_ICONERROR = 0x10;

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public bool Show(string title, string text, string? engineInstallDir)
    {
        if (OperatingSystem.IsWindows())
            return MessageBoxW(IntPtr.Zero, text, title, MB_OK | MB_ICONERROR) != 0;

        // The engine's own SDL2, by the name its platform gives it; else the
        // system's. macOS: libSDL2-2.0.0.dylib.
        string sdl = OperatingSystem.IsMacOS() ? "libSDL2-2.0.0.dylib" : "libSDL2-2.0.so.0";
        string binDir = OperatingSystem.IsMacOS() ? "osx64" : "linux64";
        IntPtr lib = IntPtr.Zero;
        if (engineInstallDir is not null)
            NativeLibrary.TryLoad(Path.Combine(engineInstallDir, "bin", binDir, sdl), out lib);
        if (lib == IntPtr.Zero && !NativeLibrary.TryLoad(sdl, out lib))
            return false;

        try
        {
            if (!NativeLibrary.TryGetExport(lib, "SDL_ShowSimpleMessageBox", out IntPtr fn))
                return false;

            byte[] t = Encoding.UTF8.GetBytes(title + "\0");
            byte[] m = Encoding.UTF8.GetBytes(text + "\0");
            fixed (byte* pt = t, pm = m)
                return ((delegate* unmanaged<uint, byte*, byte*, IntPtr, int>)fn)(SDL_MESSAGEBOX_ERROR, pt, pm, IntPtr.Zero) == 0;
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }
}
