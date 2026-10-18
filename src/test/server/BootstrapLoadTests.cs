using SourceSharp.Bootstrap;
using SourceSharp.Shared.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The Bootstrap's half of a side's load (plan_side_entries SE2), driven over a
/// real load context and a fake side entry.
/// </summary>
public sealed unsafe class BootstrapLoadTests(SideRootPair roots) : IClassFixture<SideRootPair>
{
    private static string TestBin => Path.GetDirectoryName(typeof(BootstrapLoadTests).Assembly.Location)!;

    /// <summary>
    /// A side entry whose Load reads one more assembly into its context, as a
    /// module's start-up does, and whose Stamped keeps what it was told.
    /// </summary>
    private sealed class InitialisingEntry(GameLoadContext context, string loadsDuringInit) : ISideEntry
    {
        public List<string> Calls { get; } = new();
        public IReadOnlyList<LoadedAssembly> Stamp { get; private set; } = [];

        public int Load(void* pShimRoot, void* pShimRootTable, void** ppManagedRoot, nint* pVtables, nint* pSession, byte* pFacts)
        {
            Calls.Add("Load");
            SideFacts.ReadLoad(pFacts);
            Assert.NotNull(context.LoadFromManagedDir(loadsDuringInit));
            *pVtables = 11;
            *pSession = 22;
            return 0;
        }

        public int Stamped(nint session, byte* pFacts)
        {
            Calls.Add("Stamped");
            Assert.Equal(22, session);
            Stamp = SideFacts.ReadLoaded(pFacts);
            return 0;
        }

        public int ReloadCheck(nint session, byte* pWhy, int nWhy) => 0;

        public int Unload(nint session) => 0;
    }

    private static byte[] Facts() => LoadFacts.WriteLoad(TestBin, "test", "/g", ["SourceSharp.Server"], ["SourceSharp.Client"]);

    [Fact]
    public void TheStampCountsEveryAssemblyTheContextReadDuringInitialisation()
    {
        var context = new GameLoadContext("BootstrapLoadTests", TestBin, roots.Server);
        try
        {
            Assert.NotNull(context.LoadFromManagedDir("SourceSharp.Mods"));
            Assert.DoesNotContain(context.Loaded, f => f.Name == "SourceSharp.Server.dll");

            var entry = new InitialisingEntry(context, "SourceSharp.Server");
            var abi = new LoadedFile("SourceSharp.Abi.dll", "abi0", DateTime.UnixEpoch);
            nint vtables = 0;

            int rc = Entry.LoadAndStamp(entry, context, abi, Facts(), null, null, null, ref vtables, out nint session);

            Assert.Equal(0, rc);
            Assert.Equal(["Load", "Stamped"], entry.Calls);
            Assert.Equal(11, vtables);
            Assert.Equal(22, session);
            Assert.Equal(["SourceSharp.Abi.dll", "SourceSharp.Mods.dll", "SourceSharp.Server.dll"],
                         entry.Stamp.Select(a => a.Name).Order(StringComparer.Ordinal));
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>A side entry whose Load fails.</summary>
    private sealed class RefusingEntry : ISideEntry
    {
        public bool Stamped { get; private set; }
        public int Load(void* pShimRoot, void* pShimRootTable, void** ppManagedRoot, nint* pVtables, nint* pSession, byte* pFacts)
        {
            *pSession = 33;
            return -4;
        }
        int ISideEntry.Stamped(nint session, byte* pFacts) { Stamped = true; return 0; }
        public int ReloadCheck(nint session, byte* pWhy, int nWhy) => 0;
        public int Unload(nint session) => 0;
    }

    [Fact]
    public void AFailedLoadIsNotStampedAndKeepsItsSession()
    {
        var context = new GameLoadContext("BootstrapLoadTests", TestBin, roots.Server);
        try
        {
            var entry = new RefusingEntry();
            nint vtables = 0;

            int rc = Entry.LoadAndStamp(entry, context, null, Facts(), null, null, null, ref vtables, out nint session);

            Assert.Equal(-4, rc);
            Assert.Equal(33, session);
            Assert.False(entry.Stamped);
        }
        finally
        {
            context.Unload();
        }
    }
}
