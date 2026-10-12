using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's Steam auth-ticket binding (SteamUserTickets) obeys the rule
/// SteamGameServerFlat does on the server: libsteam_api.so is only ever the
/// copy the process already loaded, never a second one (a second Steam
/// context). One fact, because the library, once loaded, stays loaded in the
/// test process, so the two halves must run in this order. (Main's
/// SteamGameServerFlatTests held both halves; the side split puts this one
/// with the client.)
/// </summary>
public class SteamUserTicketsTests
{
    private const string Library = "libsteam_api.so";

    private static bool Mapped()
        => File.ReadLines("/proc/self/maps").Any(l => l.EndsWith("/" + Library, StringComparison.Ordinal));

    [Fact]
    public void ItBindsTheLoadedCopyAndNeverLoadsOne()
    {
        string library = Path.Combine(RepoLayout.Repo("bin", "debug", "bin", "linux64"), Library);
        Assert.True(File.Exists(library), library + " is not there: run make debug in this worktree");
        Assert.Equal(Library, SteamUserTickets.Library);

        // THE REFUSAL, shown first: nothing in the test process loads the
        // library, so nothing binds, and asking does not load it.
        Assert.False(Mapped(), "something in the test process loaded libsteam_api.so first");
        var unloaded = new SteamUserTickets(new RecordingLog());
        Assert.Null(unloaded.Create(90071992547409920UL, out string why));
        Assert.Contains("RTLD_NOLOAD", why);
        Assert.False(Mapped(), "asking for a ticket loaded libsteam_api.so");

        // THE POSITIVE ARM: once the process has the library, every ticket
        // export binds (SteamAPI_SteamUser_v021 and the three functions): what
        // is left is that this process never initialised Steam.
        NativeLibrary.Load(library);
        var bound = new SteamUserTickets(new RecordingLog());
        Assert.Null(bound.Create(90071992547409920UL, out string boundWhy));
        Assert.DoesNotContain("does not export", boundWhy);
        Assert.Contains("no user", boundWhy);
    }
}
