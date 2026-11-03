using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// A fact over a map's per-tree <c>.nav3d</c> (plan_hl2mp_ai Phase 2 gate):
/// skips, visibly, only when this tree has not built the map's navigation
/// (<c>make map MAP=&lt;map&gt;</c>), the one absence the plan allows.
/// A nav without its bsp, or a stamp that does not describe the nav, does
/// not skip: the fact reads them and goes red.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PerTreeNavFactAttribute : FactAttribute
{
    public PerTreeNavFactAttribute(string map)
    {
        if (!File.Exists(PerTreeNav.NavPath(map)))
            Skip = $"this tree has no {PerTreeNav.NavPath(map)}: `make map MAP={map}` builds it";
    }
}

/// <summary>Where this tree keeps a map and its navigation: the test mod's maps/, in the configuration these facts were built for.</summary>
internal static class PerTreeNav
{
    /// <summary>debug or release: the directory above the test assembly's own (bin/&lt;config&gt;/ai-tests).</summary>
    public static string Config => Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd('/', '\\')))!;

    public static string MapsDir => RepoLayout.Repo("bin", Config, "mods", "test", "maps");

    public static string NavPath(string map) => Path.Combine(MapsDir, map + ".nav3d");

    public static string BspPath(string map) => Path.Combine(MapsDir, map + ".bsp");
}
