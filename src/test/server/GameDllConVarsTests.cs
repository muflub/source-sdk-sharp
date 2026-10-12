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
/// What is left of the <c>GameDllConVars.cs</c> checks once the seventeen
/// ConVars' comparison against <c>game/server/game.cpp</c> moved to
/// SourceSharp.OracleTests (GameDllConVarsOracleTests): neither DLL compiles
/// game.cpp any more, so the farm no longer links it and the oracle reads the
/// pristine upstream clone instead. This half reads only the shim.
/// </summary>
public class GameDllConVarsTests
{
    // ------------------------------------------------------------------
    // The two things that are NOT a one-to-one copy, said out loud.
    // ------------------------------------------------------------------

    [Fact]
    public void MapCycleFilesStockCallbackFiredIntoAnEmptyOverride()
    {
        // The reason MapCycleFileChangedCallback has no managed OnChanged. Its
        // whole body was GameRules()->ResetMapCycleTimeStamp(), and the only
        // gamerules this build has declares that as an empty override. If a
        // real body ever appears there, this Fact fails and the callback has to
        // be reconsidered -- which is the point of asserting it rather than
        // writing it in a comment.
        var header = File.ReadAllText(Path.Combine(
            RepoLayout.SdkTree, "src/sourcesharp/game/shared/shell_gamerules.h"));

        Assert.Contains("ResetMapCycleTimeStamp( void ) OVERRIDE { return; }", header);
    }
}
