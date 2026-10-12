using SourceSharp.Launcher;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// source# on the three platforms: what <see cref="LauncherPlatform"/> spells
/// differently, and that the launcher reads it. The Linux facts in
/// LauncherTests pin the Linux spellings through the default; these pin the
/// Windows and macOS ones, and that Linux's are the defaults.
/// </summary>
public sealed class LauncherPlatformTests : IDisposable
{
    private sealed class FakeSteam(SteamAnswer answer) : ISteam
    {
        public List<(string Lib, uint Mod, uint Engine)> Calls { get; } = [];

        public SteamAnswer FindInstallDir(string libraryPath, uint modAppId, uint engineAppId)
        {
            Calls.Add((libraryPath, modAppId, engineAppId));
            return answer;
        }
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public List<(string Exe, List<string> Args, Dictionary<string, string> Env, string Cwd)> Runs { get; } = [];

        public GameExit Run(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env, string workingDir)
        {
            Runs.Add((exe, [.. args], new Dictionary<string, string>(env), workingDir));
            return new GameExit(0, 4001);
        }
    }

    private sealed class NoRlimit : IRlimit
    {
        public string? SetNoFile(int limit) => null;
    }

    private sealed class NoBox : IMessageBox
    {
        public bool Show(string title, string text, string? engineInstallDir) => true;
    }

    private readonly string _root = Directory.CreateTempSubdirectory("source#-platform-test").FullName;
    private readonly string _game;
    private readonly string _install;
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public LauncherPlatformTests()
    {
        _game = Path.Combine(_root, "bin", "debug");
        _install = Path.Combine(_root, "steam", "Source SDK Base 2013 Multiplayer");
        Directory.CreateDirectory(Path.Combine(_game, "mods", "test"));
        Directory.CreateDirectory(_install);
        File.WriteAllText(Path.Combine(_game, "mods", "test", "gameinfo.txt"), "\"GameInfo\" {}\n");
        File.WriteAllText(Path.Combine(_game, "mods.yaml"), "default_mod: test\nmods:\n  test:\n    game_dir: mods/test\n    steam_appid: 777\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private int Run(LauncherPlatform platform, FakeSteam steam, FakeRunner runner, Dictionary<string, string>? env = null, params string[] argv) =>
        new Launcher.Launcher(steam, runner, new NoRlimit(), new NoBox(), _out, _err, platform).Run(argv, _game, env ?? []);

    // --- the record ---------------------------------------------------------

    [Fact]
    public void Linux_is_the_default_platform_of_a_launcher_that_names_none()
    {
        var launcher = new Launcher.Launcher(new FakeSteam(SteamAnswer.Found(_install)), new FakeRunner(), new NoRlimit(), new NoBox(), _out, _err);
        Assert.Same(LauncherPlatform.Linux, launcher.Platform);
    }

    [Fact]
    public void Each_platform_names_its_engine_library_directory_and_steam_library()
    {
        Assert.Equal(Path.Combine("inst", "bin", "linux64"), LauncherPlatform.Linux.LibraryDir("inst"));
        Assert.Equal(Path.Combine("inst", "bin", "x64"), LauncherPlatform.Windows.LibraryDir("inst"));
        Assert.Equal(Path.Combine("inst", "bin", "osx64"), LauncherPlatform.MacOS.LibraryDir("inst"));
        Assert.Equal("libsteam_api.so", LauncherPlatform.Linux.SteamLibrary);
        Assert.Equal("steam_api64.dll", LauncherPlatform.Windows.SteamLibrary);
        Assert.Null(LauncherPlatform.MacOS.SteamLibrary);
    }

    [Theory]
    [InlineData("hl2_linux64", "hl2_linux64", "hl2_win64.exe", "hl2_osx")]
    [InlineData("srcds_linux64", "srcds_linux64", "srcds_win64.exe", "srcds_osx")]
    [InlineData("mymod_win64.exe", "mymod_win64.exe", "mymod_win64.exe", "mymod_win64.exe")]
    [InlineData("hl2", "hl2", "hl2", "hl2")]
    public void The_roles_executable_is_renamed_from_the_linux_spelling_per_platform(string exe, string linux, string windows, string osx)
    {
        Assert.Equal(linux, LauncherPlatform.Linux.ExeName(exe));
        Assert.Equal(windows, LauncherPlatform.Windows.ExeName(exe));
        Assert.Equal(osx, LauncherPlatform.MacOS.ExeName(exe));
    }

    [Fact]
    public void The_library_path_variable_and_separator_are_the_loaders()
    {
        Assert.Equal(("LD_LIBRARY_PATH", ':'), (LauncherPlatform.Linux.LibraryPathVariable, LauncherPlatform.Linux.PathListSeparator));
        Assert.Equal(("PATH", ';'), (LauncherPlatform.Windows.LibraryPathVariable, LauncherPlatform.Windows.PathListSeparator));
        Assert.Equal(("DYLD_LIBRARY_PATH", ':'), (LauncherPlatform.MacOS.LibraryPathVariable, LauncherPlatform.MacOS.PathListSeparator));
    }

    [Fact]
    public void The_default_instance_base_is_tmp_where_there_is_one_and_the_temp_dir_on_windows()
    {
        Assert.Equal("/tmp/ssi-me", LauncherPlatform.Linux.DefaultInstanceBase("me"));
        Assert.Equal("/tmp/ssi-me", LauncherPlatform.MacOS.DefaultInstanceBase("me"));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "ssi-me"), LauncherPlatform.Windows.DefaultInstanceBase("me"));
        Assert.Equal(LauncherPlatform.Windows.DefaultInstanceBase(Environment.UserName),
            Launcher.Launcher.InstanceBase(new Dictionary<string, string>(), LauncherPlatform.Windows));
    }

    [Fact]
    public void Current_is_this_process_platform()
    {
        LauncherPlatform expected = OperatingSystem.IsWindows() ? LauncherPlatform.Windows
            : OperatingSystem.IsMacOS() ? LauncherPlatform.MacOS : LauncherPlatform.Linux;
        Assert.Same(expected, LauncherPlatform.Current);
    }

    // --- the launcher reading it ----------------------------------------------

    [Fact]
    public void On_windows_steam_is_asked_through_bin_x64_steam_api64_and_the_win64_exe_runs()
    {
        File.WriteAllText(Path.Combine(_install, "hl2_win64.exe"), "");
        var steam = new FakeSteam(SteamAnswer.Found(_install));
        var runner = new FakeRunner();
        Assert.Equal(0, Run(LauncherPlatform.Windows, steam, runner));
        Assert.Equal((Path.Combine(_game, "bin", "x64", "steam_api64.dll"), 777u, 243750u), steam.Calls[0]);
        Assert.Equal(Path.Combine(_install, "hl2_win64.exe"), runner.Runs[0].Exe);
    }

    [Fact]
    public void On_windows_the_engines_bin_x64_leads_PATH_and_hl2sh_linux_variables_are_not_set()
    {
        File.WriteAllText(Path.Combine(_install, "hl2_win64.exe"), "");
        var runner = new FakeRunner();
        Run(LauncherPlatform.Windows, new FakeSteam(SteamAnswer.Found(_install)), runner,
            new Dictionary<string, string> { ["PATH"] = @"C:\old" });
        var env = runner.Runs[0].Env;
        Assert.Equal(Path.Combine(_install, "bin", "x64") + @";C:\old", env["PATH"]);
        Assert.False(env.ContainsKey("LD_LIBRARY_PATH"));
        Assert.False(env.ContainsKey("__GL_THREADED_OPTIMIZATIONS"));
    }

    [Fact]
    public void On_macos_without_an_engine_directory_the_launcher_says_how_to_name_one()
    {
        var steam = new FakeSteam(SteamAnswer.Found(_install));
        var runner = new FakeRunner();
        Assert.Equal(1, Run(LauncherPlatform.MacOS, steam, runner));
        Assert.Empty(steam.Calls);
        Assert.Empty(runner.Runs);
        Assert.Contains("-engine <dir>", _err.ToString());
    }

    [Fact]
    public void On_macos_a_named_engine_directory_runs_the_osx_exe_with_DYLD_LIBRARY_PATH()
    {
        File.WriteAllText(Path.Combine(_install, "hl2_osx"), "");
        var runner = new FakeRunner();
        Assert.Equal(0, Run(LauncherPlatform.MacOS, new FakeSteam(SteamAnswer.Failed("never asked")), runner,
            new Dictionary<string, string>(), "-engine", _install));
        Assert.Equal(Path.Combine(_install, "hl2_osx"), runner.Runs[0].Exe);
        Assert.Equal(Path.Combine(_install, "bin", "osx64") + ":", runner.Runs[0].Env["DYLD_LIBRARY_PATH"]);
    }

    [Fact]
    public void A_dedicated_install_with_a_space_gets_no_link_view_off_linux()
    {
        File.WriteAllText(Path.Combine(_install, "srcds_win64.exe"), "");
        // The dedicated role as game/mods.yaml's defaults give it: the engine
        // app's install holds the libraries, 243750's the executable.
        File.WriteAllText(Path.Combine(_game, "mods.yaml"),
            "default_mod: test\nmods:\n  test:\n    game_dir: mods/test\n    steam_appid: 777\n"
            + "    engine:\n      dedicated:\n        appid: 244310\n        exe: srcds_linux64\n        exe_appid: 243750\n");
        var runner = new FakeRunner();
        int rc = Run(LauncherPlatform.Windows, new FakeSteam(SteamAnswer.Found(_install)), runner,
            new Dictionary<string, string>(), "-dedicated");
        Assert.True(rc == 0, _err.ToString());
        Assert.Equal(Path.Combine(_install, "srcds_win64.exe"), runner.Runs[0].Exe);
        Assert.False(Directory.Exists(Path.Combine(_game, "engines", "dedicated")));
    }

    [Fact]
    public void A_running_process_is_seen_without_proc_through_the_process_table()
    {
        var launcher = new Launcher.Launcher(new FakeSteam(SteamAnswer.Found(_install)), new FakeRunner(), new NoRlimit(), new NoBox(), _out, _err, LauncherPlatform.Windows);
        Assert.True(launcher.ProcessIsRunning(Environment.ProcessId));
        Assert.False(launcher.ProcessIsRunning(int.MaxValue - 7));
    }
}
