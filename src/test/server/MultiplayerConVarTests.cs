using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The fifteen ConVars <c>MultiplayerConVars.cs</c> took over from
/// <c>game/shared/multiplay_gamerules.cpp</c>: the facts that need only the
/// managed declarations and the build's compiled sources.
///
/// <para>
/// The comparison against the .cpp itself (defaults, help text, flags, the
/// client/server split, mp_chattime's bounds) is an oracle and lives in
/// SourceSharp.OracleTests (MultiplayerConVarOracleTests): neither DLL compiles
/// that file any more, so the farm no longer links it.
/// <see cref="NextLevelTakesTheNonCstrikeArm"/> stays here, because it reads only
/// the managed help text.
/// </para>
/// </summary>
public class MultiplayerConVarTests
{

    /// <summary>
    /// The ConVars DECLARED IN a given assembly, by console name.
    ///
    /// <para>
    /// The filter on the declaring assembly is load-bearing IN THIS PROCESS and
    /// nowhere else. <c>ConVarRegistry.RegisterAll</c> walks out through an
    /// anchor's references and then through its load context, which at runtime
    /// gives the server exactly Server + Shared + Abi — server.so never loads
    /// SourceSharp.Client. In the TEST process both sides are loaded in the
    /// default context, so asking the Server assembly hands back the client's
    /// four as well and <c>mp_chattime</c> arrives twice.
    /// </para>
    ///
    /// <para>
    /// So it is filtered here rather than "fixed" in the registry: the registry
    /// is behaving correctly for the process it is designed for, and a duplicate
    /// here is this test's environment rather than a defect in the port.
    /// </para>
    /// </summary>
    private static Dictionary<string, ConVarBase> DeclaredIn(System.Reflection.Assembly assembly)
    {
        var registry = ConVarRegistry.For(TestServices.UnattachedConsole());
        registry.RegisterAll(assembly);

        var byName = new Dictionary<string, ConVarBase>();
        foreach (var c in registry.All)
        {
            if (c.GetType().Assembly != assembly)
                continue;

            Assert.True(byName.TryAdd(c.Name, c),
                assembly.GetName().Name + " declares '" + c.Name + "' twice; Source "
                + "refuses a duplicate name from one DLL, so one of them would not exist");
        }
        return byName;
    }

    private static Dictionary<string, ConVarBase> Managed() =>
        DeclaredIn(typeof(MpChatTimeConVar).Assembly);

    /// <summary>
    /// <c>nextlevel</c> has two help strings and this build takes the second.
    /// The first transcription took the first, which describes different
    /// behaviour — a changelevel at the end of the ROUND rather than at the next
    /// changelevel.
    /// </summary>
    [Fact]
    public void NextLevelTakesTheNonCstrikeArm()
    {
        var mine = Managed();
        Assert.True(mine.TryGetValue("nextlevel", out var c), "nextlevel is not declared");

        Assert.Contains("during the next changelevel", c!.Help, StringComparison.Ordinal);
        Assert.DoesNotContain("at the end of the round", c.Help, StringComparison.Ordinal);
    }

    /// <summary>
    /// ...and the .cpp really is out of the SERVER build, which is what makes
    /// all of the above necessary rather than duplicative.
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheServerNoLongerCompilesTheCppThatDeclaredThem()
    {
        // server_sourcesharp's compiled sources (compile_commands.json, checked
        // against src/server/native/CMakeLists.txt); a missing or stale build throws.
        IReadOnlySet<string> server = CMakeGraph.CompiledSources(CMakeGraph.Server);

        // Proof of read: a shared file the server does compile.
        Assert.Contains("sourcesharp/game/shared/shell_movement.cpp", server);

        Assert.DoesNotContain(server, s => s.Contains("multiplay_gamerules.cpp", StringComparison.Ordinal));
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheClientNoLongerCompilesTheCppEither()
    {
        // client_sourcesharp's compiled sources: compile_commands.json, which
        // CMakeGraph requires to equal the set(client_sourcesharp_sources ...)
        // list in src/client/native/CMakeLists.txt exactly. "Out of the build"
        // is ABSENCE from that list -- the form client_csharp.vpc reached when
        // it was flattened (plan_vpc_client.md, phase A10 of
        // plan_gamedll_after.md), and the only form a CMake source list has.
        IReadOnlySet<string> client = CMakeGraph.CompiledSources(CMakeGraph.Client);

        // The positive control first: an absence assertion over a file that
        // could not be read, or whose listed path has moved, passes for the
        // wrong reason.
        //
        // IT USED TO NAME gamerules.cpp AND THE FLIP REMOVED THAT FILE from the
        // client's list, which made this control fail while the assertion below
        // was still perfectly true. A control has to name something the build
        // still has, so it named a shared file the armed client does compile.
        //
        // AND IT HAS HAPPENED A SECOND TIME. The replacement was
        // takedamageinfo.cpp, which plan_shim.md Phase 1d evicted from both
        // vpcs as an empty translation unit -- 0 strong symbols, 0 importers on
        // either side. The control became usercmd.cpp, which the shim rulings
        // mark KEEP on both sides rather than MOVE or DELETE.
        //
        // A THIRD TIME, AND IT IS THE PATH THAT MOVED RATHER THAN THE FILE.
        // KEEP was right -- usercmd.cpp is still compiled into both DLLs -- but
        // plan_shim.md Phase 2d RELOCATED it to sourcesharp/game/shared,
        // because Phase 2's exit condition was zero $SRCDIR\game rows in the
        // server script and a file that stays native moves under sourcesharp/
        // with its reason letter rather than staying put with an exception
        // beside it. So the control follows the row. The assertion below it is
        // still untouched, and this is now the third distinct way a positive
        // control over a file list has gone stale: evicted, flipped, relocated.
        // A FOURTH TIME: usercmd.cpp left both DLLs when the client's codec
        // moved to C# (plan_client_novalve C9). The control is now
        // tier_connect.cpp, which both DLLs compile by design.
        Assert.Contains("sourcesharp/game/shared/tier_connect.cpp", client);

        Assert.DoesNotContain(client, s => s.Contains("multiplay_gamerules.cpp", StringComparison.Ordinal));
    }
}
