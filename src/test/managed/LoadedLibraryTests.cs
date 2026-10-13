using System.Diagnostics;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="LoadedLibrary"/>: the loaded copy of a library or nothing, never
/// a load, on whichever platform the tests run, and each platform's names.
/// </summary>
public class LoadedLibraryTests
{
    [Fact]
    public void TheSteamApiIsTheLaunchersNameOnEachPlatform()
    {
        Assert.Equal("libsteam_api.so", LoadedLibrary.SteamApiFor(windows: false));
        Assert.Equal("steam_api64.dll", LoadedLibrary.SteamApiFor(windows: true));
    }

    [Fact]
    public void TheSteamApiOfThisProcessIsItsPlatformsName()
        => Assert.Equal(LoadedLibrary.SteamApiFor(OperatingSystem.IsWindows()), LoadedLibrary.SteamApi);

    [Fact]
    public void EachPlatformNamesItsOwnNoLoadMechanism()
    {
        Assert.Equal("RTLD_NOLOAD", LoadedLibrary.NoLoadFor(windows: false));
        Assert.Equal("GetModuleHandle", LoadedLibrary.NoLoadFor(windows: true));
        Assert.Equal(LoadedLibrary.NoLoadFor(OperatingSystem.IsWindows()), LoadedLibrary.NoLoad);
    }

    [Fact]
    public void ALibraryTheProcessHasIsFound()
    {
        // Loaded into every process of each platform before any managed code.
        string present = OperatingSystem.IsWindows() ? "kernel32.dll" : "libc.so.6";

        Assert.NotEqual(IntPtr.Zero, LoadedLibrary.Open(present));
    }

    [Fact]
    public void ALibraryTheProcessDoesNotHaveIsNotLoaded()
    {
        // A real library on each platform that no test process loads: asking
        // for it must answer nothing AND leave it out of the process.
        string absent = OperatingSystem.IsWindows() ? "xpsservices.dll" : "libanl.so.1";

        Assert.False(InProcess(absent), absent + " was already in the test process; pick another");
        Assert.Equal(IntPtr.Zero, LoadedLibrary.Open(absent));
        Assert.False(InProcess(absent), "asking for " + absent + " loaded it");
    }

    private static bool InProcess(string file)
        => Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Any(m => string.Equals(m.ModuleName, file, StringComparison.OrdinalIgnoreCase));
}
