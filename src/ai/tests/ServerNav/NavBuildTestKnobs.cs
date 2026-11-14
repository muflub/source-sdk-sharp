using SourceSharp.Ai.Server.Nav;
using SourceSharp.Tests;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>The build's knobs for a fact: off, or on at a stated thread count and timeout.</summary>
internal static class NavBuildTestKnobs
{
    public static NavBuildKnobs Off() => Of("0");

    public static NavBuildKnobs Of(string build, int threads = 2, int timeoutSeconds = 600, int maxLeaves = NavBuildSettings.DefaultMaxLeaves)
        => new(DeclaredConVars.Holding<AiNavBuildConVar>(build),
               DeclaredConVars.Holding<AiNavBuildThreadsConVar>(threads.ToString(System.Globalization.CultureInfo.InvariantCulture)),
               DeclaredConVars.Holding<AiNavBuildTimeoutConVar>(timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)),
               DeclaredConVars.Holding<AiNavBuildMaxLeavesConVar>(maxLeaves.ToString(System.Globalization.CultureInfo.InvariantCulture)),
               DeclaredConVars.Fresh<AiNavBuildCheckConVar>());
}
