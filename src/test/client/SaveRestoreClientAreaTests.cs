using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_client_vtables CE7: the client's hot-reload save/restore and save-game
/// entity block reach managed code through the managed-built
/// IManagedSaveRestoreClient, not the ManagedApi table. Source reads strip
/// comments, so a commented-out call cannot stand in for a live one.
/// </summary>
public class SaveRestoreClientAreaTests
{
    private static string Code(params string[] path)
    {
        string file = RepoLayout.Repo(path);
        Assert.True(File.Exists(file), $"{string.Join('/', path)} was not found; a scan that read nothing would pass");
        string text = File.ReadAllText(file);
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\n]*", "");
    }

    private static string ReloadFunction()
    {
        string system = Code("src", "shared", "native", "sourcesharp_gamesystem.cpp");
        int body = system.IndexOf("static void SourceSharp_ReloadManagedSide( void )\n{", StringComparison.Ordinal);
        Assert.True(body >= 0, "SourceSharp_ReloadManagedSide was not found");
        int end = system.IndexOf("\n}\n", body, StringComparison.Ordinal);
        Assert.True(end > body, "SourceSharp_ReloadManagedSide has no end");
        return system[body..end];
    }

    [Fact]
    public void TheReloadReadsTheClientsAreaAtEachCallAndAnswersZeroWithoutIt()
    {
        string fn = ReloadFunction();

        // Read at the call (the holder is patched in place across the reload),
        // never cached from before it: one read for the save, one for the restore.
        Assert.Equal(2, Regex.Matches(fn, @"\bManagedSaveRestoreClient\(\)").Count);
        Assert.Single(Regex.Matches(fn, @"IManagedSaveRestoreClient \*pSaveArea = ManagedSaveRestoreClient\(\);"));
        Assert.Single(Regex.Matches(fn, @"IManagedSaveRestoreClient \*pRestoreArea = ManagedSaveRestoreClient\(\);"));
        Assert.Single(Regex.Matches(fn, @"const int nSaved = pSaveArea \? pSaveArea->ReloadSave\(\) : 0;"));
        Assert.Single(Regex.Matches(fn, @"const int nRestored = pRestoreArea \? pRestoreArea->ReloadRestore\(\) : 0;"));
    }

    [Fact]
    public void TheRestoreAreaIsReadAfterTheNewBuildLoads()
    {
        string fn = ReloadFunction();

        int unload = fn.IndexOf("SourceSharp_UnloadMySide();", StringComparison.Ordinal);
        int restore = fn.IndexOf("*pRestoreArea = ManagedSaveRestoreClient();", StringComparison.Ordinal);
        int save = fn.IndexOf("*pSaveArea = ManagedSaveRestoreClient();", StringComparison.Ordinal);
        Assert.True(unload >= 0 && restore >= 0 && save >= 0, "a reload landmark was not found");
        Assert.True(save < unload, "the save's area is read before the old build unloads");
        Assert.True(restore > unload, "the restore's area is read after the new build loads, not cached from the old one");
    }

    [Fact]
    public void NoNativeCodeReadsTheTableSlots()
    {
        var slot = new Regex(@"(g_ManagedApi\.|pApi->)(ReloadSave|ReloadRestore|SaveCollectEntities|SaveRestoreEntities)\b");
        string[] files =
        {
            Code("src", "shared", "native", "sourcesharp_gamesystem.cpp"),
            // The server's save cluster forwards from here since
            // plan_server_native_v2 T4c (shell_savegame.cpp is client.so's).
            Code("src", "server", "native", "shell_gamedll.cpp"),
        };

        Assert.Contains("ManagedSaveRestoreClient()", files[0]);
        Assert.Contains("ManagedSaveRestore()", files[1]);
        foreach (string text in files)
            Assert.DoesNotMatch(slot, text);
    }

    [Fact]
    public void TheBootstrapFillsNoneOfTheSlots()
    {
        string entry = Code("src", "shared", "interop", "SideLoad.cs");

        Assert.Contains("public static int Load(Side side, CrossingOp op, void* pShimRoot,", entry);
        foreach (string name in new[] { "ReloadSave", "ReloadRestore", "SaveCollectEntities", "SaveRestoreEntities" })
            Assert.DoesNotMatch(new Regex(@"pOut->" + name + @"\s*="), entry);
    }

    /// <summary>
    /// The save game's entity block is the server's alone: the client's two
    /// slots had no native caller (their call site was under #ifndef
    /// CLIENT_DLL) and are not rows of the client's area.
    /// </summary>
    [Fact]
    public void TheClientsAreaHasNoSaveGameSlots()
    {
        string rows = File.ReadAllText(RepoLayout.Repo("docs", "managed-interfaces.tsv"));

        Assert.Contains("IManagedSaveRestoreClient\tReloadSave\t", rows);
        Assert.DoesNotContain("IManagedSaveRestoreClient\tSaveCollectEntities\t", rows);
        Assert.DoesNotContain("IManagedSaveRestoreClient\tSaveRestoreEntities\t", rows);
    }

    [Fact]
    public void TheClientRegistersTheBinding()
    {
        string module = Code("src", "client", "managed", "ClientModule.cs");

        // The save game's session over the engine client's allocator, the
        // registered one, which asks the engine at SaveInit (the client save codec).
        Assert.Matches(new Regex(@"_bindings\.Add\(""IManagedSaveRestoreClient"",\s*v => ManagedAreaFactory\.Create\(v, new SaveRestoreClientBinding\(_engine\.Crossings, Log, this,\s*new SaveBlockSession\(Log, _services\.Require<ISaveMemory>\(\), traceBlocks: false\)\)\)\);"),
                       module);
    }

    private sealed class Module(bool fault) : ISideModule
    {
        public void Initialize(Side side) { }
        public void Frame(float frameTime) { }
        public void LevelInit(string mapName) { }
        public void LevelShutdown() { }
        public void ConCommandDispatch(int commandId, string args) { }
        public void Shutdown() { }
        public void DispatchGameEvent(string eventName, IntPtr handle) { }

        private int Answer(int value) => fault ? throw new InvalidOperationException("fault") : value;
        public int ReloadSave() => Answer(7);
        public int ReloadRestore() => Answer(5);
        public int SaveCollect(Span<byte> destination) => Answer(11);
        public int SaveRestore(ReadOnlySpan<byte> source) => Answer(13);
    }

    // The save game's session, which these reload facts never reach.
    private static SaveBlockSession NoSession() => new(new RecordingLog(), new DefaultForwarders.NoEngineClient(), traceBlocks: false);

    [Fact]
    public unsafe void EachSlotAnswersTheModulesValue()
    {
        var binding = new SaveRestoreClientBinding(new Crossings(), new RecordingLog(), new Module(fault: false), NoSession());

        Assert.Equal(7, binding.ReloadSave());
        Assert.Equal(5, binding.ReloadRestore());
    }

    [Fact]
    public unsafe void AFaultAnswersMinusOneNotZero()
    {
        // 0 is "nothing saved", a legitimate answer; the deleted thunks answered
        // -1 for a fault and the generated catch would answer 0.
        var binding = new SaveRestoreClientBinding(new Crossings(), new RecordingLog(), new Module(fault: true), NoSession());

        Assert.Equal(-1, binding.ReloadSave());
        Assert.Equal(-1, binding.ReloadRestore());
    }
}
