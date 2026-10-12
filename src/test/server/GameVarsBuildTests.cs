using System;
using System.Collections.Generic;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The build half of GameVars.cs's port: the server compiles none of
/// gamevars_shared.cpp, so the managed declarations are the only ones. The
/// stock comparison is the oracle's (GameVarsOracleTests).
/// </summary>
public class GameVarsBuildTests
{
    // ------------------------------------------------------------------
    // The trap that made this port necessary at all.
    // ------------------------------------------------------------------

    [Fact]
    public void TheServerNoLongerCompilesTheCppItselfSoBothWouldBeADuplicate()
    {
        // server_sourcesharp's compiled sources (compile_commands.json, checked
        // against src/server/native/CMakeLists.txt). A missing or stale build
        // throws rather than letting the absence below pass.
        IReadOnlySet<string> server = CMakeGraph.CompiledSources(CMakeGraph.Server);

        // Proof of read: a shared file the server does compile.
        Assert.Contains("sourcesharp/game/shared/shell_movement.cpp", server);

        Assert.DoesNotContain(server, s => s.Contains("/gamevars_shared.", StringComparison.Ordinal));   // the file left both DLLs (C8)
    }
}
