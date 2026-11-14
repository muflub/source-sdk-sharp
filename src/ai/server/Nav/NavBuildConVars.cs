using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// <c>ai_nav_build</c> (plan_bots_teams_tf2 E.3, ruling Q10): 1, the default,
/// builds a map's navigation in the server's own process when the map has no
/// usable <c>.nav3d</c>; 0 refuses bots on such a map, as before.
/// </summary>
[ConVar("ai_nav_build", "1", Help = "1: a map with no usable .nav3d has one built in the server's process (bots join when it is built); 0: no bots on such a map")]
public sealed class AiNavBuildConVar : BoolConVar { }

/// <summary><c>ai_nav_build_threads</c>: the build's threads, at below-normal priority; 0 is half the processors.</summary>
[ConVar("ai_nav_build_threads", "0", Help = "Threads a navigation build uses, at below-normal priority; 0 = half the processors", Min = 0, Max = 64)]
public sealed class AiNavBuildThreadsConVar : IntConVar { }

/// <summary><c>ai_nav_build_timeout</c>: a build that takes longer is stopped and fails (retried once).</summary>
[ConVar("ai_nav_build_timeout", "600", Help = "Seconds a navigation build may take before it is stopped", Min = 1, Max = 7200)]
public sealed class AiNavBuildTimeoutConVar : IntConVar { }

/// <summary><c>ai_nav_build_max_leaves</c>: a built navigation with more leaves is refused (about 25 times dm_lockdown's).</summary>
[ConVar("ai_nav_build_max_leaves", "4000000", Help = "A built navigation with more leaves than this is refused", Min = 1)]
public sealed class AiNavBuildMaxLeavesConVar : IntConVar { }

/// <summary><c>ai_nav_build_check</c>: 1 runs the floor check (ssmap nav3d -check's) after a build, on the build's threads; it reports and never refuses.</summary>
[ConVar("ai_nav_build_check", "0", Help = "1: check every standable place of a built navigation for a floor under it, after the build (reports, never refuses)")]
public sealed class AiNavBuildCheckConVar : BoolConVar { }

/// <summary>The build's knobs as one service: whether to build, and how.</summary>
[SideService(Side.Server)]
public sealed class NavBuildKnobs(AiNavBuildConVar build, AiNavBuildThreadsConVar threads, AiNavBuildTimeoutConVar timeout,
                                  AiNavBuildMaxLeavesConVar leaves, AiNavBuildCheckConVar check)
{
    public bool Enabled => build.Value;

    public NavBuildSettings Settings => new(threads.Value > 0 ? threads.Value : NavBuildSettings.DefaultThreads,
                                            TimeSpan.FromSeconds(Math.Max(1, timeout.Value)),
                                            leaves.Value > 0 ? leaves.Value : NavBuildSettings.DefaultMaxLeaves, check.Value);
}
