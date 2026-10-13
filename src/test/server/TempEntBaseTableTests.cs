using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Phase 7 step 7b: <c>DT_BaseTempEntity</c> is the managed declaration's, on
/// both sides, and <c>CBaseTempEntity</c> is a registered class on neither.
///
/// <para>
/// THE FAILURE THIS GUARDS AGAINST IS A JOIN THAT STOPS WITH NOTHING IN ANY LOG.
/// plan 2E: a ServerClass with no matching ClientClass makes the client begin
/// instantiating and stop — lane CP saw exactly that for
/// <c>CTEParticleSystem</c>, as <c>Client missing DT class CTEParticleSystem</c>
/// followed by <c>InitFastCopy - missing client class 47</c> and a stack
/// overflow. The safe states are BOTH sides registering the name or NEITHER, and
/// the unsafe one is a build where somebody changed one side.
/// </para>
///
/// <para>
/// 7b moved this class from "native on the client, absent on the server" to
/// "absent on both", by taking <c>c_basetempentity.cpp</c> out of
/// <c>client_csharp.vpc</c>. <c>game/shared/basetempentity.cpp</c> had already
/// left <c>server_csharp.vpc</c> several phases earlier. So the facts below are
/// a pair: the declaration abstains, AND neither DLL compiles a stock TU that would
/// register it anyway. Either one alone is satisfied by a build that is broken
/// in the other direction.
/// </para>
///
/// <para>
/// These read the BUILD, not the port, for the same reason
/// <see cref="TempEntSchemaTests"/> reads stock's <c>te_*.cpp</c>: a check that
/// compares the port with the port is not a comparison. What the build actually
/// compiles is <see cref="CMakeGraph.CompiledSources"/> of
/// <c>server_sourcesharp</c> and <c>client_sourcesharp</c> (compile_commands.json,
/// equal to each target's CMakeLists list; the two vpc scripts until VPC was
/// replaced).
/// </para>
/// </summary>
public class TempEntBaseTableTests
{
    /// <summary>
    /// The declaration abstains from claiming a class, and says so with
    /// <c>RegistersClass = false</c>.
    /// </summary>
    [Fact]
    public void TheBaseDeclarationRegistersNoClass()
    {
        TeClass declared = TeClassBuilder.Build(typeof(TeBaseTempEntity));

        Assert.False(declared.RegistersClass,
            "CBaseTempEntity claims no ServerClass and no ClientClass. Flipping this to "
          + "true registers the name on BOTH sides at once; leaving it false registers it "
          + "on neither. Both are safe. What is not safe is one side.");
    }

    /// <summary>It is still the empty table stock declares, under stock's two names.</summary>
    [Fact]
    public void TheBaseDeclarationIsStocksEmptyTableUnderStocksNames()
    {
        TeClass declared = TeClassBuilder.Build(typeof(TeBaseTempEntity));

        Assert.Equal("CBaseTempEntity", declared.ClassName);
        Assert.Equal("DT_BaseTempEntity", declared.TableName);
        Assert.Empty(declared.Fields);
    }

    /// <summary>
    /// The CLIENT no longer compiles a native <c>DT_BaseTempEntity</c>.
    ///
    /// <para>
    /// While it did, <c>shell_tempents_client.cpp</c>'s
    /// <c>FindStockTableNamed</c> refused to build the managed table and every
    /// derived managed table nested stock's instead (proved on a live join by
    /// lane CD). With the TU evicted that refusal does not fire and the managed
    /// table is the one in the process.
    /// </para>
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheClientCompilesNoStockBaseTempEntityTu()
    {
        Assert.DoesNotContain(CMakeGraph.CompiledSources(CMakeGraph.Client),
            s => s.Contains("c_basetempentity.cpp", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The SERVER half of the same pair, which has been true for longer.</summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheServerCompilesNoStockBaseTempEntityTu()
    {
        Assert.DoesNotContain(CMakeGraph.CompiledSources(CMakeGraph.Server),
            s => s.Contains("basetempentity.cpp", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The two build facts above are absences, and an absence over an empty list
    /// is satisfied by nothing at all. This is the reading that says they had a
    /// list to look in. (CMakeGraph also throws on a missing, empty or stale
    /// build.)
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void BothSourceListsWereActuallyRead()
    {
        var client = CMakeGraph.CompiledSources(CMakeGraph.Client);
        var server = CMakeGraph.CompiledSources(CMakeGraph.Server);

        // Each still compiles a temp-entity TU, so "no basetempentity" is a
        // statement about this subject rather than about a list that has lost
        // its files.
        Assert.Contains("sourcesharp/game/client/shell_tempents_client.cpp", client);
        Assert.Contains("sourcesharp/game/server/shell_tempents.cpp", server);
    }

    /// <summary>
    /// Every temp entity that names <c>DT_BaseTempEntity</c> as its base gets it
    /// from this declaration, and there are many of them — so a change here is
    /// not a change to one class.
    /// </summary>
    [Fact]
    public void ManyDeclarationsNestTheBaseTable()
    {
        IEnumerable<Type> declared = typeof(TeBaseTempEntity).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<TempEntityAttribute>() is not null);

        int nesting = declared
            .Select(t => TeClassBuilder.Build(t))
            .Count(c => c.BaseTable == "DT_BaseTempEntity");

        Assert.True(nesting >= 20,
            $"{nesting} declarations nest DT_BaseTempEntity. The base moved last for this "
          + "reason: it is the table every one of them addresses through.");
    }
}
