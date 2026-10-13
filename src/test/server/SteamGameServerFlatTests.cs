using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_novalve.md P2.5: the server's Steam game server is C#'s, through
/// libsteam_api.so's flat API, and the library is only ever the one the
/// process already loaded -- never a second copy, which would be a second
/// Steam context. One fact, because the library, once loaded, stays loaded in
/// the test process, so the two halves must run in this order.
/// </summary>
public class SteamGameServerFlatTests
{
    // Whether the library is in the process, read independently of the
    // subject's own lookup: the kernel's map on Linux, the loader's module
    // list on Windows.
    private static bool Mapped()
        => OperatingSystem.IsWindows()
            ? System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>()
                .Any(m => string.Equals(m.ModuleName, SteamGameServerFlat.Library, StringComparison.OrdinalIgnoreCase))
            : File.ReadLines("/proc/self/maps").Any(l => l.EndsWith("/" + SteamGameServerFlat.Library, StringComparison.Ordinal));

    [Fact]
    public void ItBindsTheLoadedCopyAndNeverLoadsOne()
    {
        string library = Path.Combine(RepoLayout.Repo("bin", "debug", "bin", OperatingSystem.IsWindows() ? "x64" : "linux64"), SteamGameServerFlat.Library);
        Assert.True(File.Exists(library), library + " is not there: run make debug in this worktree");

        var before = new SteamGameServerFlat();

        // THE REFUSAL, shown first: nothing in the test process loads the
        // library, so nothing binds, and asking does not load it.
        Assert.False(Mapped(), "something in the test process loaded the Steam API first");
        Assert.False(before.Reacquire());
        Assert.Equal(IntPtr.Zero, before.LibraryHandle);
        Assert.Contains(LoadedLibrary.NoLoad, before.WhyUnbound);
        Assert.False(Mapped(), "asking for the game server loaded the Steam API");
        Assert.False(before.LoggedOn());

        // THE POSITIVE ARM: once the process has the library, the handle C#
        // resolves IS that library's, and every export it needs is there.
        IntPtr loaded = NativeLibrary.Load(library);
        var after = new SteamGameServerFlat();

        Assert.Equal(loaded, after.LibraryHandle);
        Assert.Equal(string.Empty, after.WhyUnbound);
    }
}
