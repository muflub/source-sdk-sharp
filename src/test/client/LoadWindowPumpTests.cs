using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_map_load P6: the client pumps the window from the load steps it owns, so
/// a long load answers the compositor's ping. The engine itself pumps only
/// between frames.
/// </summary>
public class LoadWindowPumpTests
{
    private static string Shell()
    {
        string path = Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "game", "client", "shell_clientdll.cpp");
        Assert.True(File.Exists(path), $"{path} was not found; a scan that read nothing would pass");
        return File.ReadAllText(path);
    }

    [Fact]
    public void The_pump_is_the_launchers_SDL_never_a_second_copy()
    {
        string text = Shell();
        Assert.Contains("dlopen( \"libSDL2-2.0.so.0\", RTLD_NOW | RTLD_NOLOAD )", text);
        Assert.Contains("m_pfnPumpEvents = (void ( * )())dlsym( hSDL, \"SDL_PumpEvents\" );", text);
    }

    [Fact]
    public void The_pump_runs_only_on_the_main_thread()
        => Assert.Contains("void PumpWindowDuringLoad() { if ( m_pfnPumpEvents && ThreadInMainThread() ) m_pfnPumpEvents(); }", Shell());

    [Fact]
    public void Both_level_init_edges_pump_first()
    {
        string text = Shell();
        Assert.Matches(new Regex(@"void CShellClientDLL::LevelInitPreEntity\( char const\* pMapName \)\s*\{(\s*//[^\n]*)*\s*PumpWindowDuringLoad\(\);"), text);
        Assert.Matches(new Regex(@"void CShellClientDLL::LevelInitPostEntity\( \)\s*\{\s*PumpWindowDuringLoad\(\);"), text);
    }
}
