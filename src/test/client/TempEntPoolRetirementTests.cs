using SourceSharp.Abi;
using System.Text.RegularExpressions;

using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The retirement gate for the dead tempent-pool primitives (docs/todo.md's
/// "Dead tempent pool code" row): the four native entry points
/// <c>SourceSharpTePool_Reserve/Bind/Release/EjectBrass</c> and their managed
/// callbacks existed only for <c>CTempEnts::TempEntAlloc(org, model)</c> /
/// <c>TempEntAllocHigh</c> / <c>EjectBrass</c> in Valve's
/// <c>c_te_legacytempents.cpp</c> — a translation unit this build compiles into
/// no target (the census: <c>grep -c legacytempents farm/debug/build/build.ninja</c>
/// is 0; the fork keeps the file for the stock build alone). The live spawn path
/// is the managed one — <c>ReserveNamed</c> → <c>SourceSharp_FxTempEntCreateNamed</c>
/// → <c>SourceSharpTePool_CreateWithModel</c> — and the live shell flow is
/// <c>CTempEnts::EjectBrass</c> → <c>IManagedFxClient::TempEntEjectBrass</c>.
///
/// <para>
/// Everything below reads CODE with comments stripped, the
/// <c>TempEntsClientAreaTests</c> convention: this lane's banners name every
/// construct they moved, so a scan reading them would find the four names in
/// files that do not use them. Each census fact carries a positive arm — a
/// construct the same walk must still find — so an empty scan cannot pass.
/// </para>
/// </summary>
public unsafe class TempEntPoolRetirementTests
{
    private static readonly string[] DeadExports =
    [
        "SourceSharpTePool_Reserve",
        "SourceSharpTePool_Bind",
        "SourceSharpTePool_Release",
        "SourceSharpTePool_EjectBrass",
    ];

    /// <summary>The pool's entry points the live paths reach; the census that
    /// deletes the four above must not silently delete one of these.</summary>
    private static readonly string[] LiveNativeEntries =
    [
        "SourceSharpTePool_Update",
        "SourceSharpTePool_Clear",
        "SourceSharpTePool_Find",
        "SourceSharpTePool_CreateWithModel",
    ];

    private static string Code(params string[] path)
    {
        string file = RepoLayout.Repo(path);
        Assert.True(File.Exists(file), $"{string.Join('/', path)} was not found; a scan that read nothing would pass");
        string text = File.ReadAllText(file);
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\n]*", "");
    }

    private static string Native(string file) => Code("src", "client", "native", file);

    /// <summary>
    /// The pool file defines none of the four any more, and still defines the
    /// four live entry points — the positive arm that tells a vanished file
    /// from a retired export.
    /// </summary>
    [Fact]
    public void ThePoolFileDefinesNoneOfTheDeadExports()
    {
        string code = Native("shell_te_pool_client.cpp");

        foreach (string live in LiveNativeEntries)
            Assert.Contains($"{live}(", code);

        foreach (string dead in DeadExports)
            Assert.DoesNotContain($"{dead}(", code);
    }

    /// <summary>
    /// No product code — C++ or C# — names the four: no definition, no call, no
    /// declaration, no binding. Tests are excluded (they may name what they
    /// refuse); docs are excluded (prose is not a caller). The positive arms:
    /// the same walks still find the live CreateWithModel in the pool file and
    /// a known call in a sibling file, so a walk that read nothing cannot pass.
    /// </summary>
    [Fact]
    public void NoProductCodeNamesTheDeadExports()
    {
        string root = RepoLayout.Repo();

        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file);
            if (ext is not (".cs" or ".cpp" or ".h" or ".hpp" or ".inc"))
                continue;
            // Spelled with '/' on every platform: the filters and Code() split on it.
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.StartsWith("src/test", StringComparison.Ordinal)
                || rel.Contains("/obj/", StringComparison.Ordinal)
                || rel.Contains("/bin/", StringComparison.Ordinal))
                continue;

            string code = Code(rel.Split('/'));
            foreach (string dead in DeadExports)
                if (code.Contains($"{dead}("))
                    offenders.Add($"{rel}: {dead}");
        }

        Assert.Empty(offenders);

        // The walk reaches the subject: the live half of the same file, and a
        // live call in a sibling file.
        Assert.Contains("SourceSharpTePool_CreateWithModel(", Native("shell_te_pool_client.cpp"));
        Assert.Contains("SourceSharpTePool_Update(", Native("shell_legacy_tempents_client.cpp"));
    }

    /// <summary>
    /// The fork's <c>c_te_legacytempents.cpp</c> — the only code that ever
    /// called the four — reaches no link line: the debug build's Ninja graph
    /// compiles it into no target. A missing graph is a failure, never a pass
    /// (a census run before any build proves nothing); its positive arm is a
    /// pool thunk the same graph DOES compile.
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheForkStockTempentsTusCompileIntoNoTarget()
    {
        string ninja = RepoLayout.Sdk("build", "build.ninja");
        Assert.True(File.Exists(ninja),
            $"{ninja} was not found; run a build before trusting this census");
        string graph = File.ReadAllText(ninja);

        Assert.Contains("shell_te_pool_client.cpp", graph);   // positive arm
        Assert.DoesNotContain("c_te_legacytempents.cpp", graph);
    }

    /// <summary>
    /// The two callbacks the retired exports invoked left
    /// <c>IManagedTempEntsClient</c> in the serialised ABI pass
    /// (docs/abi-retirements.md, 2026-10-20): no row in
    /// docs/managed-interfaces.tsv, no slot in the table native reads, no
    /// member in the contract managed builds. A slot that came back would be a
    /// second native path into a body nothing audits. The positive arms are the
    /// live Update slot of the same interface, in the same files.
    /// </summary>
    [Fact]
    public void TheRetiredCallbacksAreGoneFromTheArea()
    {
        string tsv = File.ReadAllText(RepoLayout.Repo("docs", "managed-interfaces.tsv"));
        string header = Code("src", "shared", "native", "imanaged_areas_client.h");
        string contract = Code("src", "client", "interop", "ManagedAreas.g.cs");

        foreach (string slot in new[] { "OnTempEntPoolAlloc", "OnTempEntEjectBrass" })
        {
            Assert.DoesNotContain("IManagedTempEntsClient\t" + slot + "\t", tsv);
            Assert.DoesNotContain("( *" + slot + " )(", header);
            Assert.DoesNotContain(" " + slot + "(", contract);
        }

        Assert.Contains("IManagedTempEntsClient\tOnTempEntPoolUpdate\t", tsv);
        Assert.Contains("( *OnTempEntPoolUpdate )(", header);
        Assert.Contains(" OnTempEntPoolUpdate(", contract);
    }

    /// <summary>
    /// The two ops died with the only call sites of the callbacks that counted
    /// them (<c>CrossingsAuditTests.CrossingOpHasOneMemberPerCountedThunk</c>
    /// pins enum == counted-set in BOTH directions; an op whose thunk is gone
    /// "compiles fine and reports a permanent zero"). The tail renumbered once.
    /// The positive arms: Update/Clear (the live callbacks) keep their ops.
    /// </summary>
    [Fact]
    public void TheDeadOpsAreGoneFromCrossingOp()
    {
        Assert.True(Enum.IsDefined(SourceSharp.Abi.CrossingOp.ClientTempEntPoolUpdate));
        Assert.True(Enum.IsDefined(SourceSharp.Abi.CrossingOp.ClientTempEntPoolClear));

        string code = Code("src", "abi", "Crossings.cs");
        Assert.DoesNotContain("ClientTempEntPoolAlloc", code);
        Assert.DoesNotContain("ClientTempEntEjectBrass", code);
        Assert.Contains("ClientTempEntPoolUpdate", code);   // positive arm
    }

    /// <summary>
    /// The module surface the exports' callbacks forwarded to is gone with
    /// them: no <c>IClientModule.TempEntPoolAlloc</c>, no
    /// <c>IClientModule.TempEntEjectBrass</c>. The live spawn entry points
    /// (<see cref="SourceSharp.Client.TempEnts.ClientTempEntPool"/> spawning
    /// through <c>TempEntPoolCreate</c>/<c>TempEntPoolWriteSpawn</c>) and the
    /// live Update/Clear forwarders stay.
    /// </summary>
    [Fact]
    public void TheModuleSurfaceTheExportsForwardedToIsGone()
    {
        string iface = Code("src", "client", "interop", "IClientModule.cs");
        string module = Code("src", "client", "managed", "ClientModule.cs");

        Assert.DoesNotContain("TempEntPoolAlloc", iface);
        Assert.DoesNotContain("TempEntEjectBrass", iface);
        Assert.DoesNotContain("TempEntPoolAlloc", module);
        Assert.DoesNotContain("TempEntEjectBrass(", module);

        // Positive arms: the live members of the same files.
        Assert.Contains("TempEntPoolUpdate", iface);
        Assert.Contains("TempEntPoolClear", iface);
        Assert.Contains("TempEntPoolUpdate", module);
        Assert.Contains("TempEntPoolClear", module);
    }

    /// <summary>Every call is a test failure by construction: a retired slot
    /// must never forward at all.</summary>
    private sealed class FaultOnCallClientModule : IClientModule
    {
        public readonly List<string> Ran = [];

        private void Ran_(string name) => Ran.Add(name);

        public void DispatchTempEntity(int handle, ReadOnlySpan<byte> blob) => Ran_(nameof(DispatchTempEntity));
        public bool ForwardTempEntity(int id, ReadOnlySpan<float> floats, ReadOnlySpan<int> ints)
        { Ran_(nameof(ForwardTempEntity)); return false; }
        public void TempEntPoolUpdate(float frameTime) => Ran_(nameof(TempEntPoolUpdate));
        public void TempEntPoolClear() => Ran_(nameof(TempEntPoolClear));
        public int ImpactEffectParams(int effect, int material, float scale, bool noFlecks, Span<float> block)
        { Ran_(nameof(ImpactEffectParams)); return 0; }
        public int ImpactChoice(ReadOnlySpan<int> input, Span<int> output, Span<byte> particleName, Span<byte> decalName)
        { Ran_(nameof(ImpactChoice)); return 0; }

        public void DispatchUserMessage(int index, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public float GetJumpScale(in JumpRequest request) => throw new NotSupportedException();
        public float GetFrictionScale(int playerIndex, float friction, float speed, bool ducking, int groundMaterial) => throw new NotSupportedException();
        public float GetMaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial) => throw new NotSupportedException();
        public bool StandaloneCreate(EntityHandle entity, string className) => throw new NotSupportedException();
        public void StandaloneSpawn(EntityHandle entity) => throw new NotSupportedException();
        public int StandaloneDraw(EntityHandle entity) => throw new NotSupportedException();
        public void StandaloneDrawBatch(ReadOnlySpan<uint> handles, Span<int> results) => throw new NotSupportedException();
        public void StandaloneRemoved(EntityHandle entity) => throw new NotSupportedException();
        public void StandaloneDataChanged(EntityHandle entity, ReadOnlySpan<byte> blob, int updateType) => throw new NotSupportedException();
        public void OnStage(ClientFrameStage stage) => throw new NotSupportedException();
        public void InvalidateMdlCache() => throw new NotSupportedException();
    }
}
