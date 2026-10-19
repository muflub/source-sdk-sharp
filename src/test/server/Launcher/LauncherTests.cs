using SourceSharp.Launcher;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// source#: every branch of Launcher.Run, with Steam, the spawner, the fd
/// limit and the dialog replaced by fakes. One behaviour per fact.
/// </summary>
public sealed class LauncherTests : IDisposable
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

    private sealed class FakeRunner(params int[] exitCodes) : IProcessRunner
    {
        public List<(string Exe, List<string> Args, Dictionary<string, string> Env, string Cwd)> Runs { get; } = [];

        /// <summary>What the game does before it exits, given its pid and environment.</summary>
        public Action<int, IReadOnlyDictionary<string, string>>? OnRun { get; init; }

        public GameExit Run(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env, string workingDir)
        {
            Runs.Add((exe, [.. args], new Dictionary<string, string>(env), workingDir));
            int pid = 4000 + Runs.Count;
            OnRun?.Invoke(pid, env);
            return new GameExit(exitCodes[Math.Min(Runs.Count - 1, exitCodes.Length - 1)], pid);
        }
    }

    private sealed class FakeRlimit(string? error = null) : IRlimit
    {
        public List<int> Set { get; } = [];

        public string? SetNoFile(int limit)
        {
            Set.Add(limit);
            return error;
        }
    }

    private sealed class FakeBox : IMessageBox
    {
        public List<string> Shown { get; } = [];

        public bool Show(string title, string text, string? engineInstallDir)
        {
            Shown.Add(text);
            return true;
        }
    }

    private readonly string _root = Directory.CreateTempSubdirectory("source#-test").FullName;
    private readonly string _game;
    private readonly string _install;
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private readonly FakeRlimit _rlimit;
    private readonly FakeBox _box = new();

    public LauncherTests()
    {
        _game = Path.Combine(_root, "bin", "debug");
        _install = Path.Combine(_root, "steam", "Source SDK Base 2013 Multiplayer");
        Directory.CreateDirectory(Path.Combine(_game, "mods", "test"));
        Directory.CreateDirectory(_install);
        File.WriteAllText(Path.Combine(_game, "mods", "test", "gameinfo.txt"), "\"GameInfo\" {}\n");
        File.WriteAllText(Path.Combine(_install, "hl2_linux64"), "");
        WriteMods("""
            default_mod: test
            mods:
              test:
                game_dir: mods/test
                steam_appid: 777
                engine:
                  client:
                    args: [-mod-arg]
                    env:
                      MOD_VAR: yes
            """);
        _rlimit = new FakeRlimit();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteMods(string yaml) => File.WriteAllText(Path.Combine(_game, "mods.yaml"), yaml);

    private string ModsYaml => Path.Combine(_game, "mods.yaml");

    private int Run(FakeSteam steam, FakeRunner runner, params string[] argv) =>
        Run(steam, runner, new Dictionary<string, string>(), argv);

    private int Run(FakeSteam steam, FakeRunner runner, Dictionary<string, string> env, params string[] argv) =>
        new Launcher.Launcher(steam, runner, _rlimit, _box, _out, _err).Run(argv, _game, env);

    private FakeSteam Found() => new(SteamAnswer.Found(_install));

    [Fact]
    public void Resolve_writes_the_browser_block_for_the_dedicated_role_only()
    {
        WriteMods("""
            mods:
              hosted:
                game_dir: mods/test
                engine:
                  dedicated:
                    args: [-console]
                browser:
                  name: "N {players}"
            """);
        var runner = new FakeRunner(0);

        Assert.Equal(0, Run(Found(), runner, "-mod", "hosted", "-resolve", "-dedicated"));
        string dedicated = _out.ToString();
        _out.GetStringBuilder().Clear();
        Assert.Equal(0, Run(Found(), runner, "-mod", "hosted", "-resolve"));

        Assert.Contains("browser:\n  name: \"N {players}\"\n", dedicated);
        Assert.DoesNotContain("browser:", _out.ToString());
        Assert.Contains("fd_limit:", _out.ToString());
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public void WithoutGameTheModsAbsoluteGameDirComesFirst()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, "+map", "dm_lockdown");
        Assert.Equal(["-game", Path.Combine(_game, "mods", "test")], runner.Runs[0].Args.Take(2));
    }

    [Fact]
    public void ArgsAreGameThenTheModsThenTheUsersInOrder()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, "-novid", "+map", "x");
        Assert.Equal(["-game", Path.Combine(_game, "mods", "test"), "-mod-arg", "-novid", "+map", "x"], runner.Runs[0].Args);
    }

    [Fact]
    public void BackgroundNamesTheWindowClassTheKWinRuleMatches()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, "-background", "+map", "x");
        Assert.Equal("sourcesharp-bg", runner.Runs[0].Env["SDL_VIDEO_X11_WMCLASS"]);
    }

    [Fact]
    public void BackgroundTurnsOffTheNoFocusSleepBeforeTheUsersArgs()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, "-novid", "-background", "+map", "x");
        Assert.Equal(["-game", Path.Combine(_game, "mods", "test"), "-mod-arg", "+engine_no_focus_sleep", "0", "-novid", "+map", "x"],
            runner.Runs[0].Args);
    }

    [Fact]
    public void WithoutBackgroundTheWindowClassAndFocusSleepAreLeftAlone()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, "-novid");
        Assert.False(runner.Runs[0].Env.ContainsKey("SDL_VIDEO_X11_WMCLASS"));
        Assert.Equal(["-game", Path.Combine(_game, "mods", "test"), "-mod-arg", "-novid"], runner.Runs[0].Args);
    }

    [Fact]
    public void BackgroundKeepsAWindowClassTheUserSet()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, new Dictionary<string, string> { ["SDL_VIDEO_X11_WMCLASS"] = "mine" }, "-background");
        Assert.Equal("mine", runner.Runs[0].Env["SDL_VIDEO_X11_WMCLASS"]);
    }

    [Fact]
    public void AUserGameIsNotOverridden()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, "-game", "/elsewhere");
        Assert.Equal(["-mod-arg", "-game", "/elsewhere"], runner.Runs[0].Args);
    }

    [Fact]
    public void TheModFlagIsTheLaunchersAndNeverReachesTheEngine()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, "-mod", "test", "-novid");
        // Both words go: the flag and its id.
        Assert.Equal(["-game", Path.Combine(_game, "mods", "test"), "-mod-arg", "-novid"], runner.Runs[0].Args);
    }

    [Fact]
    public void ASecondModIsStartedWithItsOwnGameDir()
    {
        WriteMods("""
            default_mod: test
            mods:
              test:
                game_dir: mods/test
              other:
                game_dir: mods/other
            """);
        Directory.CreateDirectory(Path.Combine(_game, "mods", "other"));
        File.WriteAllText(Path.Combine(_game, "mods", "other", "gameinfo.txt"), "");
        var runner = new FakeRunner(0);
        Run(Found(), runner, "-mod", "other");
        Assert.Equal(Path.Combine(_game, "mods", "other"), runner.Runs[0].Args[1]);
    }

    [Fact]
    public void ModWithoutAnIdIsAnError()
    {
        var runner = new FakeRunner(0);
        Assert.Equal(1, Run(Found(), runner, "-mod"));
        Assert.Contains("-mod needs a mod id", _err.ToString());
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public void AnUnknownModIsAnErrorListingTheKnownOnes()
    {
        Assert.Equal(1, Run(Found(), new FakeRunner(0), "-mod", "nope"));
        Assert.Contains("unknown mod 'nope' (known: test)", _err.ToString());
    }

    [Fact]
    public void AGameDirWithoutGameinfoIsAnError()
    {
        File.Delete(Path.Combine(_game, "mods", "test", "gameinfo.txt"));
        Assert.Equal(1, Run(Found(), new FakeRunner(0)));
        Assert.Contains("has no gameinfo.txt", _err.ToString());
    }

    [Fact]
    public void AnInvalidModsYamlFailsWithTheParsersMessage()
    {
        WriteMods("mods:\n  test:\n    game_dir: mods/test\n    bogus: 1\n");
        Assert.Equal(1, Run(Found(), new FakeRunner(0)));
        Assert.Contains("bogus", _err.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SteamIsAskedForTheEngineAppWithTheModsAppId()
    {
        var steam = Found();
        Run(steam, new FakeRunner(0));
        Assert.Equal((Path.Combine(_game, "bin", "linux64", "libsteam_api.so"), 777u, 243750u), steam.Calls[0]);
    }

    [Fact]
    public void ASteamFailureIsReportedAndNothingStarts()
    {
        var runner = new FakeRunner(0);
        int rc = Run(new FakeSteam(SteamAnswer.Failed("SteamAPI_Init failed!")), runner);
        Assert.Equal(1, rc);
        Assert.Equal("[Source Mod Launcher] SteamAPI_Init failed!\n", _err.ToString().Replace("\r", ""));
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public void AnErrorShowsADialogByDefault()
    {
        Run(new FakeSteam(SteamAnswer.Failed("boom")), new FakeRunner(0));
        Assert.Equal(["boom"], _box.Shown);
    }

    [Fact]
    public void NoMessageBoxKeepsErrorsToStderr()
    {
        Run(new FakeSteam(SteamAnswer.Failed("boom")), new FakeRunner(0), "-nomessagebox");
        Assert.Empty(_box.Shown);
        Assert.Contains("boom", _err.ToString());
    }

    [Fact]
    public void AMissingEngineExecutableIsReportedNotSwallowed()
    {
        File.Delete(Path.Combine(_install, "hl2_linux64"));
        var runner = new FakeRunner(0);
        Assert.Equal(1, Run(Found(), runner));
        Assert.Contains("Tried to run: " + Path.Combine(_install, "hl2_linux64"), _err.ToString());
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public void TheGameRunsFromTheEngineInstall()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner);
        Assert.Equal(Path.Combine(_install, "hl2_linux64"), runner.Runs[0].Exe);
        Assert.Equal(_install, runner.Runs[0].Cwd);
    }

    [Fact]
    public void ExitCode42RestartsTheGame()
    {
        var runner = new FakeRunner(42, 42, 0);
        Assert.Equal(0, Run(Found(), runner));
        Assert.Equal(3, runner.Runs.Count);
    }

    [Fact]
    public void AnyOtherExitCodeIsReturnedAsIs()
    {
        var runner = new FakeRunner(7);
        Assert.Equal(7, Run(Found(), runner));
        Assert.Single(runner.Runs);
    }

    // The game stopping itself: fatal_report.cpp writes assert-<pid>.txt into
    // $SOURCESHARP_ASSERT_DIR and exits 253.
    private static FakeRunner FatalGame(string report) => new(Launcher.Launcher.FatalExitCode)
    {
        OnRun = (pid, env) => File.WriteAllText(
            Path.Combine(env[Launcher.Launcher.AssertDirVariable], $"assert-{pid}.txt"), report),
    };

    [Fact]
    public void TheFatalExitCodeIsTheOneTheGameExitsWith()
    {
        string header = File.ReadAllText(RepoLayout.Repo("src", "shared", "native", "fatal_report.h"));
        Assert.Contains($"enum {{ kSourceSharpFatalExitCode = {Launcher.Launcher.FatalExitCode} }};", header);
    }

    [Fact]
    public void AssertReportsGoToTheModDir()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner);
        Assert.Equal(Path.Combine(_game, "mods", "test"), runner.Runs[0].Env[Launcher.Launcher.AssertDirVariable]);
    }

    [Fact]
    public void AnAssertDirTheUserSetIsKept()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, new Dictionary<string, string> { [Launcher.Launcher.AssertDirVariable] = "/mine" });
        Assert.Equal("/mine", runner.Runs[0].Env[Launcher.Launcher.AssertDirVariable]);
    }

    [Fact]
    public void AFatalExitShowsTheGamesReportInADialog()
    {
        Run(Found(), FatalGame("[SourceSharp] FATAL ASSERT: x > 0\n"));
        string shown = Assert.Single(_box.Shown);
        Assert.StartsWith("[SourceSharp] FATAL ASSERT: x > 0\n", shown);
        Assert.EndsWith($"saved in {Path.Combine(_game, "mods", "test", "assert-4001.txt")}", shown);
    }

    [Fact]
    public void AFatalExitWithNoMessageBoxLeavesTheReportOnDiskAndSaysWhere()
    {
        Run(Found(), FatalGame("report"), "-nomessagebox");
        string path = Path.Combine(_game, "mods", "test", "assert-4001.txt");
        Assert.Empty(_box.Shown);
        Assert.Equal("report", File.ReadAllText(path));
        Assert.Contains($"report: {path}", _err.ToString());
    }

    [Fact]
    public void AFatalExitIsReturnedAndNotRestarted()
    {
        var runner = FatalGame("report");
        Assert.Equal(253, Run(Found(), runner, "-nomessagebox"));
        Assert.Single(runner.Runs);
    }

    [Fact]
    public void AFatalExitWithNoReportFileSaysSoAndStillShowsADialog()
    {
        Run(Found(), new FakeRunner(Launcher.Launcher.FatalExitCode));
        Assert.Contains("no " + Path.Combine(_game, "mods", "test", "assert-4001.txt"), _err.ToString());
        Assert.StartsWith("The game stopped on a fatal error.", Assert.Single(_box.Shown));
    }

    // The runner hands the child exactly the environment it is given, and
    // PowerShell does not start without the system's (SystemRoot, TEMP, ...).
    private static Dictionary<string, string> WindowsEnvironmentWith(string name, string value)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            env[(string)e.Key] = (string?)e.Value ?? "";
        env[name] = value;
        return env;
    }

    [Fact]
    public void TheRealRunnerReportsTheChildsPidAndItsFatalCode()
    {
        // The shell is the child itself, so its own pid ($$, $PID) is the pid
        // the game's report is named by. Windows has no /bin/sh; PowerShell
        // is on every Windows host and can name its own pid, which cmd cannot.
        GameExit exit = OperatingSystem.IsWindows()
            ? new ProcessRunner().Run(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoProfile", "-Command", "Set-Content -Path (Join-Path $env:DIR \"assert-$PID.txt\") -Value r; exit 253"],
                WindowsEnvironmentWith("DIR", _root), _root)
            : new ProcessRunner().Run("/bin/sh", ["-c", "echo r > \"$DIR/assert-$$.txt\"; exit 253"],
                new Dictionary<string, string> { ["DIR"] = _root, ["PATH"] = "/usr/bin:/bin" }, _root);
        Assert.Equal(Launcher.Launcher.FatalExitCode, exit.Code);
        Assert.True(File.Exists(Path.Combine(_root, $"assert-{exit.Pid}.txt")), $"no assert-{exit.Pid}.txt");
    }

    [Fact]
    public void AnOrdinaryFailingExitShowsNoDialog()
    {
        Run(Found(), new FakeRunner(7));
        Assert.Empty(_box.Shown);
        Assert.Equal("", _err.ToString());
    }

    [Fact]
    public void ALongReportIsCutInTheDialogButNotOnDisk()
    {
        string report = new('x', 10_000);
        Run(Found(), FatalGame(report));
        Assert.StartsWith(new string('x', 4000) + "\n...", Assert.Single(_box.Shown));
        Assert.Equal(report, File.ReadAllText(Path.Combine(_game, "mods", "test", "assert-4001.txt")));
    }

    [Fact]
    public void LdLibraryPathLeadsWithTheEnginesBinLinux64()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, new Dictionary<string, string> { ["LD_LIBRARY_PATH"] = "/old" });
        Assert.Equal(Path.Combine(_install, "bin", "linux64") + ":/old", runner.Runs[0].Env["LD_LIBRARY_PATH"]);
    }

    [Fact]
    public void ThreadedGlOptimisationsAreOn()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner);
        Assert.Equal("1", runner.Runs[0].Env["__GL_THREADED_OPTIMIZATIONS"]);
    }

    [Fact]
    public void PathmatchIsEnabledOnlyWhenTheInstallHasPathmatchInf()
    {
        var without = new FakeRunner(0);
        Run(Found(), without);
        File.WriteAllText(Path.Combine(_install, "pathmatch.inf"), "");
        var with = new FakeRunner(0);
        Run(Found(), with);
        Assert.False(without.Runs[0].Env.ContainsKey("ENABLE_PATHMATCH"));
        Assert.Equal("1", with.Runs[0].Env["ENABLE_PATHMATCH"]);
    }

    [Fact]
    public void BootstrapIsToldWhichFileAndWhichMod()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner);
        Assert.Equal(ModsYaml, runner.Runs[0].Env["SOURCESHARP_MODS_YAML"]);
        Assert.Equal("test", runner.Runs[0].Env["SOURCESHARP_MOD"]);
    }

    [Fact]
    public void TheModsOwnEnvironmentIsPassedOn()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner);
        Assert.Equal("yes", runner.Runs[0].Env["MOD_VAR"]);
    }

    [Fact]
    public void TheFdLimitIsSet()
    {
        Run(Found(), new FakeRunner(0));
        Assert.Equal([2048], _rlimit.Set);
    }

    [Fact]
    public void AnFdLimitFailureWarnsAndStillStarts()
    {
        var runner = new FakeRunner(0);
        new Launcher.Launcher(Found(), runner, new FakeRlimit("Operation not permitted"), _box, _out, _err)
            .Run([], _game, new Dictionary<string, string>());
        Assert.Contains("open-file limit", _err.ToString());
        Assert.Single(runner.Runs);
    }

    [Fact]
    public void GdbGetsTheGameAsItsProgram()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, new Dictionary<string, string> { ["GAME_DEBUGGER"] = "gdb" }, "-novid");
        Assert.Equal("gdb", runner.Runs[0].Exe);
        Assert.Equal("--args", runner.Runs[0].Args[0]);
        Assert.Equal(Path.Combine(_install, "hl2_linux64"), runner.Runs[0].Args[1]);
    }

    [Fact]
    public void AnyOtherDebuggerIsACommandPrefix()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, new Dictionary<string, string> { ["GAME_DEBUGGER"] = "strace -f -o s.log" });
        Assert.Equal("strace", runner.Runs[0].Exe);
        Assert.Equal(["-f", "-o", "s.log", Path.Combine(_install, "hl2_linux64")], runner.Runs[0].Args.Take(4));
    }

    [Fact]
    public void HelpListsTheModsWithoutAskingSteam()
    {
        var steam = Found();
        Assert.Equal(0, Run(steam, new FakeRunner(0), "-help"));
        Assert.Contains("test", _out.ToString());
        Assert.Contains("(default)", _out.ToString());
        Assert.Empty(steam.Calls);
    }

    [Fact]
    public void ThePublishedLauncherFindsItsOwnDirectoryExactly()
    {
        // What exepath_test held the native launcher to: its directory is exact
        // whatever is on the stack (the native one read an unterminated
        // readlink and dlopen'd from a directory that never existed). Run the
        // real source# through a symlink from an unrelated cwd: it must still
        // name the mods.yaml beside the binary, not beside the link or the cwd.
        // Windows names the binary source#.exe, and starts a file by a name
        // without an extension only as name.exe, so the link carries it too.
        string ext = OperatingSystem.IsWindows() ? ".exe" : "";
        string exe = RepoLayout.Repo("bin", RepoLayout.Config, "source#" + ext);
        Assert.True(File.Exists(exe), $"{exe} is not built: make {RepoLayout.Config}");
        string link = Path.Combine(_root, "elsewhere", "launch" + ext);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.CreateSymbolicLink(link, exe);

        var psi = new System.Diagnostics.ProcessStartInfo(link, "-help")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        Assert.Equal(0, p.ExitCode);
        Assert.Contains($"Mods ({Path.Combine(Path.GetDirectoryName(exe)!, "mods.yaml")}):", output);
    }

    [Fact]
    public void AMissingSteamLibraryNamesThePathTried()
    {
        // The real Steam binding, pointed at a library that is not there.
        string lib = Path.Combine(_root, "bin", "linux64", "libsteam_api.so");
        var answer = new NativeSteam().FindInstallDir(lib, 243750, 243750);
        Assert.Null(answer.InstallDir);
        Assert.StartsWith($"Not able to load the Steam API.\n\nTried: {lib}\n\n", answer.Error);
    }

    //-------------------------------------------------------------------------
    // The dedicated engine's view without spaces (plan_host_sdk P0)
    //-------------------------------------------------------------------------

    [Fact]
    public void The_engine_view_links_every_entry_and_the_exe_and_prunes_stale_links()
    {
        string install = Path.Combine(_root, "Engine With Spaces");
        string exeHome = Path.Combine(_root, "Exe Home");
        Directory.CreateDirectory(Path.Combine(install, "bin"));
        Directory.CreateDirectory(Path.Combine(install, "hl2"));
        Directory.CreateDirectory(exeHome);
        string exe = Path.Combine(exeHome, "srcds_linux64");
        File.WriteAllText(exe, "");
        string view = Path.Combine(_root, "engines", "dedicated");
        Directory.CreateDirectory(view);
        File.CreateSymbolicLink(Path.Combine(view, "gone"), Path.Combine(install, "gone"));

        Launcher.Launcher.EngineView(view, install, exe);

        Assert.Equal(Path.Combine(install, "bin"), new FileInfo(Path.Combine(view, "bin")).LinkTarget);
        Assert.Equal(Path.Combine(install, "hl2"), new FileInfo(Path.Combine(view, "hl2")).LinkTarget);
        Assert.Equal(exe, new FileInfo(Path.Combine(view, "srcds_linux64")).LinkTarget);
        Assert.False(File.Exists(Path.Combine(view, "gone")) || new FileInfo(Path.Combine(view, "gone")).LinkTarget is not null);
        Assert.DoesNotContain(' ', view);
    }

    [Fact]
    public void The_engine_view_refuses_to_touch_a_real_file()
    {
        string install = Path.Combine(_root, "Engine With Spaces");
        Directory.CreateDirectory(install);
        string view = Path.Combine(_root, "engines", "dedicated");
        Directory.CreateDirectory(view);
        File.WriteAllText(Path.Combine(view, "real.txt"), "keep me");

        Assert.Throws<IOException>(() => Launcher.Launcher.EngineView(view, install, Path.Combine(install, "x")));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(view, "real.txt")));
    }

    // -instance: a game that is one of several started from one tree. The engine
    // rewrites videoconfig_linux.cfg in place 33 times at start-up (O_TRUNC, no
    // rename; strace 2026-10-05), so games sharing the mod dir read each other's
    // half-written file. Each -instance game gets a game dir of its own.

    private string SharedMod => Path.Combine(_game, "mods", "test");

    private Dictionary<string, string> TmpEnv()
    {
        string tmp = Path.Combine(_root, "tmp");
        Directory.CreateDirectory(tmp);
        return new Dictionary<string, string> { ["TMPDIR"] = tmp, [Launcher.Launcher.InstanceRootVariable] = InstanceRoot };
    }

    private string InstanceRoot => Path.Combine(_root, "ssi");

    private void SeedSharedMod()
    {
        Directory.CreateDirectory(Path.Combine(SharedMod, "cfg"));
        Directory.CreateDirectory(Path.Combine(SharedMod, "maps"));
        File.WriteAllText(Path.Combine(SharedMod, "videoconfig_linux.cfg"), "\"videoconfig\" { \"shared\" \"1\" }\n");
        File.WriteAllText(Path.Combine(SharedMod, "cfg", "config.cfg"), "shared config\n");
        File.WriteAllText(Path.Combine(SharedMod, "cfg", "settings.scr"), "settings\n");
    }

    private static string GameDirOf(List<string> args) => args[args.IndexOf("-game") + 1];

    [Fact]
    public void Two_instances_get_distinct_writable_videoconfig_paths()
    {
        // The second launch runs while the first game is still up: one launcher
        // process here, so the two share a pid and must still not share a dir.
        SeedSharedMod();
        var env = TmpEnv();
        FakeRunner inner = new(0);
        FakeRunner outer = null!;
        outer = new FakeRunner(0) { OnRun = (_, _) => Run(Found(), inner, env, "-instance", "+map", "x") };
        Run(Found(), outer, env, "-instance", "+map", "x");

        string a = GameDirOf(outer.Runs[0].Args);
        string b = GameDirOf(inner.Runs[0].Args);
        Assert.NotEqual(a, b);
        Assert.NotEqual(SharedMod, a);
        Assert.NotEqual(SharedMod, b);
    }

    [Fact]
    public void An_instance_writes_its_videoconfig_and_config_cfg_to_private_copies()
    {
        SeedSharedMod();
        string? video = null, config = null;
        FakeRunner runner = null!;
        runner = new FakeRunner(0)
        {
            OnRun = (_, _) =>
            {
                string dir = GameDirOf(runner.Runs[^1].Args);
                video = File.ReadAllText(Path.Combine(dir, "videoconfig_linux.cfg"));
                config = File.ReadAllText(Path.Combine(dir, "cfg", "config.cfg"));
                // What the engine does: truncate and rewrite in place.
                File.WriteAllText(Path.Combine(dir, "videoconfig_linux.cfg"), "");
                File.WriteAllText(Path.Combine(dir, "cfg", "config.cfg"), "");
            },
        };

        Run(Found(), runner, TmpEnv(), "-instance");

        Assert.Equal("\"videoconfig\" { \"shared\" \"1\" }\n", video);
        Assert.Equal("shared config\n", config);
        Assert.Equal("\"videoconfig\" { \"shared\" \"1\" }\n", File.ReadAllText(Path.Combine(SharedMod, "videoconfig_linux.cfg")));
        Assert.Equal("shared config\n", File.ReadAllText(Path.Combine(SharedMod, "cfg", "config.cfg")));
    }

    [Fact]
    public void An_instance_game_dir_keeps_the_mods_directory_name()
    {
        // COM_CheckGameDirectory: a client refuses a server whose game dir's
        // last component differs from its own.
        SeedSharedMod();
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-instance");
        Assert.Equal("test", Path.GetFileName(GameDirOf(runner.Runs[0].Args)));
    }

    [Fact]
    public void An_instance_game_dir_lives_under_the_instance_root_and_not_under_TMPDIR()
    {
        // The game dir is -game, so its length is the engine's: a long TMPDIR
        // (an agent's scratchpad) once pushed the command line past 512.
        SeedSharedMod();
        var runner = new FakeRunner(0);
        var env = TmpEnv();
        Run(Found(), runner, env, "-instance");
        string dir = GameDirOf(runner.Runs[0].Args);
        Assert.StartsWith(InstanceRoot + Path.DirectorySeparatorChar, dir);
        Assert.DoesNotContain(env["TMPDIR"], dir);
    }

    [Fact]
    public void An_instance_game_dir_is_the_root_then_the_launchers_pid_then_the_mod_dir_name()
    {
        SeedSharedMod();
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-instance");
        Assert.Equal(Path.Combine(InstanceRoot, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "test"),
                     GameDirOf(runner.Runs[0].Args));
    }

    [Fact]
    public void The_default_instance_root_is_short_fixed_and_ignores_TMPDIR()
    {
        var env = new Dictionary<string, string> { ["TMPDIR"] = "/" + new string('t', 200) };
        string root = Launcher.Launcher.InstanceBase(env);
        Assert.Equal("/tmp/ssi-" + Environment.UserName, root);
    }

    [Fact]
    public void The_launcher_directory_is_the_binarys_whatever_link_started_it()
    {
        string binDir = Path.Combine(_root, "bin");
        Directory.CreateDirectory(binDir);
        string binary = Path.Combine(binDir, "source#");
        File.WriteAllText(binary, "");
        string link = Path.Combine(_root, "elsewhere", "launch");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.CreateSymbolicLink(link, binary);

        Assert.Equal(binDir, Launcher.Launcher.LauncherDirectory(link));
        Assert.Equal(binDir, Launcher.Launcher.LauncherDirectory(binary));
    }

    [Fact]
    public void A_launcher_pinned_to_linux_still_sees_a_live_pid_on_any_host()
    {
        // The Linux platform reads /proc; on a host without one (Windows) it
        // must fall back to the process table rather than read every pid gone.
        var launcher = new Launcher.Launcher(Found(), new FakeRunner(0), _rlimit, _box, _out, _err);
        Assert.True(launcher.ProcessIsRunning(Environment.ProcessId));
        Assert.False(launcher.ProcessIsRunning(int.MaxValue));
    }

    [Fact]
    public void The_process_table_reading_sees_this_process_and_not_a_free_pid()
    {
        Assert.True(Launcher.Launcher.IsRunning(Environment.ProcessId, readProc: false));
        Assert.False(Launcher.Launcher.IsRunning(int.MaxValue, readProc: false));
    }

    [LinuxOnlyFact("reads /proc, which only Linux has")]
    public void The_proc_reading_sees_this_process_and_not_a_free_pid()
    {
        Assert.True(Launcher.Launcher.IsRunning(Environment.ProcessId, readProc: true));
        Assert.False(Launcher.Launcher.IsRunning(int.MaxValue, readProc: true));
    }

    [Fact]
    public void The_instance_root_variable_overrides_the_default()
    {
        var env = new Dictionary<string, string> { [Launcher.Launcher.InstanceRootVariable] = "/x/y" };
        // Canonicalised: unchanged on Linux, drive-rooted on Windows.
        Assert.Equal(Path.GetFullPath("/x/y"), Launcher.Launcher.InstanceBase(env));
    }

    [Fact]
    public void A_stale_instance_of_a_dead_launcher_is_swept_at_start()
    {
        SeedSharedMod();
        // Above pid_max (4194304 at most on Linux): never a live process.
        string stale = Path.Combine(InstanceRoot, "999999999", "test");
        Directory.CreateDirectory(Path.Combine(stale, "cfg"));
        File.WriteAllText(Path.Combine(stale, "cfg", "config.cfg"), "old");
        Run(Found(), new FakeRunner(0), TmpEnv(), "-instance");
        Assert.False(Directory.Exists(Path.Combine(InstanceRoot, "999999999")));
    }

    [Fact]
    public void An_instance_mirrors_the_sibling_mod_dirs_so_relative_gameinfo_mounts_resolve()
    {
        // TF2's gameinfo mounts `|gameinfo_path|../test` and
        // `../../content/tf`; relocated into <root>/<mod>, those paths leave
        // the mirror unless its neighbourhood is mirrored too.
        SeedSharedMod();
        string sibling = Path.Combine(_game, "mods", "tf2");
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "gameinfo.txt"), "\"GameInfo\" {}\n");
        Directory.CreateDirectory(Path.Combine(_game, "content", "tf"));
        File.WriteAllText(Path.Combine(_game, "content", "tf", "tf2_misc_dir.vpk"), "x");

        FakeRunner runner = null!;
        runner = new FakeRunner(0)
        {
            // The instance dir is removed when the game exits, so the mirror
            // is inspected while the (fake) game is "up": ../tf2 from the
            // mirrored test dir must reach the sibling mod dir, and
            // ../../content from it the game dir's content.
            OnRun = (_, _) =>
            {
                string gameDir = GameDirOf(runner.Runs[^1].Args);        // <root>/<pid>/test
                string tf2ViaRelative = Path.GetFullPath(Path.Combine(gameDir, "..", "tf2"));
                Assert.True(Directory.Exists(tf2ViaRelative), $"{tf2ViaRelative} does not exist");
                Assert.Equal("\"GameInfo\" {}\n",
                             File.ReadAllText(Path.Combine(tf2ViaRelative, "gameinfo.txt")));
                string contentViaRelative = Path.GetFullPath(Path.Combine(gameDir, "..", "..", "content"));
                Assert.True(Directory.Exists(contentViaRelative), $"{contentViaRelative} does not exist");
                Assert.Equal("x", File.ReadAllText(Path.Combine(contentViaRelative, "tf", "tf2_misc_dir.vpk")));
            },
        };
        Run(Found(), runner, TmpEnv(), "-instance");
    }

    [Fact]
    public void An_instance_mirror_repairs_a_broken_content_link_and_never_touches_a_real_directory()
    {
        SeedSharedMod();
        Directory.CreateDirectory(Path.Combine(_game, "content", "tf"));
        string contentLink = Path.Combine(InstanceRoot, "content");

        // a broken link left by a deleted tree is repaired
        Directory.CreateDirectory(InstanceRoot);
        File.CreateSymbolicLink(contentLink, Path.Combine(_root, "gone-tree", "content"));
        Run(Found(), new FakeRunner(0), TmpEnv(), "-instance");
        Assert.Equal(Path.Combine(_game, "content"),
                     Path.GetFullPath(new FileInfo(contentLink).LinkTarget!));
        File.Delete(contentLink);

        // a real directory that is not ours is left alone
        Directory.CreateDirectory(contentLink);
        File.WriteAllText(Path.Combine(contentLink, "user.cfg"), "keep");
        Run(Found(), new FakeRunner(0), TmpEnv(), "-instance");
        Assert.Equal("keep", File.ReadAllText(Path.Combine(contentLink, "user.cfg")));
    }

    [Fact]
    public void A_stale_instance_of_a_dead_launcher_with_a_suffix_is_swept_at_start()
    {
        SeedSharedMod();
        Directory.CreateDirectory(Path.Combine(InstanceRoot, "999999999.2", "test"));
        Run(Found(), new FakeRunner(0), TmpEnv(), "-instance");
        Assert.False(Directory.Exists(Path.Combine(InstanceRoot, "999999999.2")));
    }

    [Fact]
    public void Sweeping_a_stale_instance_removes_its_links_and_never_what_they_point_at()
    {
        SeedSharedMod();
        string stale = Path.Combine(InstanceRoot, "999999999", "test");
        Directory.CreateDirectory(stale);
        File.CreateSymbolicLink(Path.Combine(stale, "maps"), Path.Combine(SharedMod, "maps"));
        File.WriteAllText(Path.Combine(SharedMod, "maps", "m.bsp"), "map");
        Run(Found(), new FakeRunner(0), TmpEnv(), "-instance");
        Assert.False(Directory.Exists(stale));
        Assert.Equal("map", File.ReadAllText(Path.Combine(SharedMod, "maps", "m.bsp")));
    }

    [Fact]
    public void The_instance_of_a_live_launcher_is_never_swept()
    {
        SeedSharedMod();
        // This test process is alive on every host (pid 1 is not a process on
        // Windows). The sweep runs before the launcher picks its own name, so a
        // sweep that misread this pid would remove the directory first.
        string live = Path.Combine(InstanceRoot, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "test");
        Directory.CreateDirectory(live);
        Run(Found(), new FakeRunner(0), TmpEnv(), "-instance");
        Assert.True(Directory.Exists(live));
    }

    [Fact]
    public void A_non_instance_entry_in_the_instance_root_is_never_swept()
    {
        SeedSharedMod();
        string other = Path.Combine(InstanceRoot, "notes");
        Directory.CreateDirectory(other);
        Run(Found(), new FakeRunner(0), TmpEnv(), "-instance");
        Assert.True(Directory.Exists(other));
    }

    [Fact]
    public void A_launch_without_instance_sweeps_nothing()
    {
        SeedSharedMod();
        Directory.CreateDirectory(Path.Combine(InstanceRoot, "999999999"));
        Run(Found(), new FakeRunner(0), TmpEnv(), "-novid");
        Assert.True(Directory.Exists(Path.Combine(InstanceRoot, "999999999")));
    }

    [Fact]
    public void An_instance_reads_the_shared_content_through_links()
    {
        SeedSharedMod();
        string? maps = null, settings = null, gameinfo = null;
        FakeRunner runner = null!;
        runner = new FakeRunner(0)
        {
            OnRun = (_, _) =>
            {
                string dir = GameDirOf(runner.Runs[^1].Args);
                maps = new FileInfo(Path.Combine(dir, "maps")).LinkTarget;
                settings = new FileInfo(Path.Combine(dir, "cfg", "settings.scr")).LinkTarget;
                gameinfo = new FileInfo(Path.Combine(dir, "gameinfo.txt")).LinkTarget;
            },
        };

        Run(Found(), runner, TmpEnv(), "-instance");

        Assert.Equal(Path.Combine(SharedMod, "maps"), maps);
        Assert.Equal(Path.Combine(SharedMod, "cfg", "settings.scr"), settings);
        Assert.Equal(Path.Combine(SharedMod, "gameinfo.txt"), gameinfo);
    }

    [Fact]
    public void An_instance_writes_console_log_and_its_con_logfile_to_the_shared_mod_dir()
    {
        // Tools read bin/<config>/mods/test/console.log and the +con_logfile
        // name they chose there; an instance's logs must still land there.
        SeedSharedMod();
        FakeRunner runner = null!;
        runner = new FakeRunner(0)
        {
            OnRun = (_, _) =>
            {
                string dir = GameDirOf(runner.Runs[^1].Args);
                File.AppendAllText(Path.Combine(dir, "console.log"), "condebug line\n");
                File.AppendAllText(Path.Combine(dir, "mine.log"), "logfile line\n");
            },
        };

        Run(Found(), runner, TmpEnv(), "-instance", "-condebug", "+con_logfile", "mine.log");

        Assert.Equal("condebug line\n", File.ReadAllText(Path.Combine(SharedMod, "console.log")));
        Assert.Equal("logfile line\n", File.ReadAllText(Path.Combine(SharedMod, "mine.log")));
    }

    [Fact]
    public void Files_the_engine_creates_in_an_instance_stay_private()
    {
        SeedSharedMod();
        FakeRunner runner = null!;
        runner = new FakeRunner(0)
        {
            OnRun = (_, _) =>
            {
                string dir = GameDirOf(runner.Runs[^1].Args);
                Directory.CreateDirectory(Path.Combine(dir, "download"));
                File.WriteAllText(Path.Combine(dir, "demoheader.tmp"), "x");
            },
        };

        Run(Found(), runner, TmpEnv(), "-instance");

        Assert.False(Directory.Exists(Path.Combine(SharedMod, "download")));
        Assert.False(File.Exists(Path.Combine(SharedMod, "demoheader.tmp")));
    }

    [Fact]
    public void An_instance_game_dir_is_removed_when_the_game_exits_and_the_shared_content_survives()
    {
        SeedSharedMod();
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-instance");

        Assert.False(Directory.Exists(GameDirOf(runner.Runs[0].Args)));
        Assert.True(File.Exists(Path.Combine(SharedMod, "cfg", "settings.scr")));
        Assert.True(Directory.Exists(Path.Combine(SharedMod, "maps")));
        Assert.True(File.Exists(Path.Combine(SharedMod, "gameinfo.txt")));
    }

    [Fact]
    public void A_restarted_instance_keeps_its_game_dir()
    {
        SeedSharedMod();
        var runner = new FakeRunner(42, 0);
        Run(Found(), runner, TmpEnv(), "-instance");

        Assert.Equal(2, runner.Runs.Count);
        Assert.Equal(GameDirOf(runner.Runs[0].Args), GameDirOf(runner.Runs[1].Args));
    }

    [Fact]
    public void The_instance_flag_is_the_launchers_and_never_reaches_the_engine()
    {
        SeedSharedMod();
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-instance", "-novid");
        Assert.DoesNotContain("-instance", runner.Runs[0].Args);
    }

    [Fact]
    public void Without_instance_the_game_dir_is_the_shared_mod_dir()
    {
        SeedSharedMod();
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-novid");
        Assert.Equal(SharedMod, GameDirOf(runner.Runs[0].Args));
    }

    [Fact]
    public void An_instance_overlays_a_game_dir_the_user_named()
    {
        SeedSharedMod();
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-instance", "-game", SharedMod);
        string dir = GameDirOf(runner.Runs[0].Args);
        Assert.NotEqual(SharedMod, dir);
        Assert.Equal("test", Path.GetFileName(dir));
        Assert.Single(runner.Runs[0].Args, a => a == "-game");
    }

    [Fact]
    public void An_instance_names_its_game_dir_to_managed_code()
    {
        // Managed code (LevelHop's pool/maps, MapPrefetcher's download/maps)
        // writes under the mod's GameDir, which the engine must then read.
        SeedSharedMod();
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-instance");
        Assert.Equal(GameDirOf(runner.Runs[0].Args), runner.Runs[0].Env["SOURCESHARP_GAME_DIR"]);
    }

    [Fact]
    public void A_game_dir_variable_is_never_inherited()
    {
        SeedSharedMod();
        var runner = new FakeRunner(0);
        var env = TmpEnv();
        env["SOURCESHARP_GAME_DIR"] = "/some/other/instance/test";
        Run(Found(), runner, env, "-novid");
        Assert.False(runner.Runs[0].Env.ContainsKey("SOURCESHARP_GAME_DIR"));
    }

    // THE ENGINE'S COMMAND-LINE LIMIT. libtier0's BuildCmdLine sums strlen of
    // every argv entry, argv[0] included and no separators, and exits with
    // "command line too long, 512 max" past 512 (probed 2026-10-05: 512 runs,
    // 513 is refused; launcher.so and dedicated_srv.so both call it).

    private static int Bytes(string exe, IEnumerable<string> args) =>
        System.Text.Encoding.UTF8.GetByteCount(exe) + args.Sum(a => System.Text.Encoding.UTF8.GetByteCount(a));

    /// <summary>A -pad argument that brings this launch's engine command line to exactly <paramref name="total"/> bytes.</summary>
    private string PadTo(int total, params string[] rest)
    {
        var probe = new FakeRunner(0);
        Run(Found(), probe, TmpEnv(), [.. rest, "-p"]);
        int now = Bytes(probe.Runs[0].Exe, probe.Runs[0].Args);
        _out.GetStringBuilder().Clear();
        return "-p" + new string('x', total - now);
    }

    [Fact]
    public void The_engine_command_line_length_counts_every_argument_and_the_executable_without_separators()
    {
        Assert.Equal(3 + 2 + 4, Launcher.Launcher.EngineCommandLineLength("exe", ["-a", "+b c"]));
    }

    [Fact]
    public void The_engine_command_line_length_counts_bytes_not_characters()
    {
        Assert.Equal(2, Launcher.Launcher.EngineCommandLineLength("\u00e9", []));
    }

    [Fact]
    public void A_launch_at_the_engines_limit_runs()
    {
        string pad = PadTo(512, "-novid");
        var runner = new FakeRunner(0);
        Assert.Equal(0, Run(Found(), runner, TmpEnv(), "-novid", pad));
        Assert.Equal(512, Bytes(runner.Runs[0].Exe, runner.Runs[0].Args));
    }

    [Fact]
    public void A_launch_over_the_engines_limit_is_refused_and_never_started()
    {
        string pad = PadTo(513, "-novid");
        var runner = new FakeRunner(0);
        Assert.Equal(1, Run(Found(), runner, TmpEnv(), "-novid", pad));
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public void A_refused_launch_names_the_length_and_the_limit()
    {
        string pad = PadTo(513, "-novid");
        Run(Found(), new FakeRunner(0), TmpEnv(), "-novid", pad);
        Assert.Contains("513 bytes", _err.ToString());
        Assert.Contains("512", _err.ToString());
    }

    [Fact]
    public void A_refused_launch_names_its_longest_argument()
    {
        string pad = PadTo(600, "-novid");
        Run(Found(), new FakeRunner(0), TmpEnv(), "-novid", pad);
        Assert.Contains($"{pad.Length} {pad[..20]}", _err.ToString());
    }

    [Fact]
    public void A_refused_launch_shows_the_dialog_too()
    {
        string pad = PadTo(600, "-novid");
        Run(Found(), new FakeRunner(0), TmpEnv(), "-novid", pad);
        Assert.Single(_box.Shown);
    }

    [Fact]
    public void A_refused_instance_launch_leaves_no_instance_dir()
    {
        SeedSharedMod();
        string pad = PadTo(600, "-instance");
        Run(Found(), new FakeRunner(0), TmpEnv(), "-instance", pad);
        Assert.True(!Directory.Exists(InstanceRoot) || !Directory.EnumerateFileSystemEntries(InstanceRoot).Any());
    }

    [Fact]
    public void A_launch_reports_its_engine_command_line_length()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-novid");
        int bytes = Bytes(runner.Runs[0].Exe, runner.Runs[0].Args);
        Assert.Contains($"engine command line: {bytes} of 512 bytes", _out.ToString());
    }

    [Fact]
    public void Under_a_debugger_the_engines_own_argv_is_measured_not_the_debuggers()
    {
        string pad = PadTo(512, "-novid");
        var runner = new FakeRunner(0);
        var env = TmpEnv();
        env["GAME_DEBUGGER"] = "gdb -q -ex run";
        Assert.Equal(0, Run(Found(), runner, env, "-novid", pad));
        Assert.Equal("gdb", runner.Runs[0].Exe);
    }

    // SourceSharp's own flags (the debug API's, -assertlog, the host link's)
    // are read by managed and native code and never by the engine, so they go
    // to SOURCESHARP_ARGS instead of the engine's 512-byte command line.

    private static string? LaunchArgs(FakeRunner runner) =>
        runner.Runs[0].Env.TryGetValue("SOURCESHARP_ARGS", out string? v) ? v : null;

    [Fact]
    public void Our_own_flags_never_reach_the_engine()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-debugapi", "-debugapisocket", "-assertlog", "-novid");
        Assert.DoesNotContain("-debugapi", runner.Runs[0].Args);
        Assert.DoesNotContain("-debugapisocket", runner.Runs[0].Args);
        Assert.DoesNotContain("-assertlog", runner.Runs[0].Args);
    }

    [Fact]
    public void Our_own_flags_go_to_the_launch_args_variable_in_order()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-debugapi", "-novid", "-debugapisocket", "-assertlog");
        Assert.Equal("-debugapi\n-debugapisocket\n-assertlog", LaunchArgs(runner));
    }

    [Fact]
    public void Engine_flags_stay_on_the_engines_command_line()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-debugapi", "-novid", "-condebug", "-tickrate", "100");
        Assert.Equal(["-novid", "-condebug", "-tickrate", "100"], runner.Runs[0].Args.Skip(3));
    }

    [Fact]
    public void Each_of_our_switches_moves()
    {
        string[] switches = ["-debugapi", "-debugapisocket", "-debugui", "-debuguiserve", "-debuguipublic", "-assertlog", "-assertstack", "-unhosted"];
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), switches);
        Assert.Equal(string.Join("\n", switches), LaunchArgs(runner));
        Assert.Empty(runner.Runs[0].Args.Intersect(switches));
    }

    [Fact]
    public void Each_of_our_options_moves_with_its_value()
    {
        string[] options = ["-debugapiport", "27100", "-debuguiport", "0", "-debuguihost", "10.0.0.5", "-hostlocal", "/x/host.json"];
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), options);
        Assert.Equal(string.Join("\n", options), LaunchArgs(runner));
        Assert.Empty(runner.Runs[0].Args.Intersect(options));
    }

    [Fact]
    public void An_option_followed_by_another_flag_moves_alone()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-debuguiport", "-novid");
        Assert.Equal("-debuguiport", LaunchArgs(runner));
        Assert.Contains("-novid", runner.Runs[0].Args);
    }

    [Fact]
    public void Our_flags_are_matched_ignoring_case_as_their_readers_do()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-DebugAPI");
        Assert.Equal("-DebugAPI", LaunchArgs(runner));
    }

    [Fact]
    public void An_argument_holding_a_newline_stays_on_the_command_line()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-hostlocal", "a\nb");
        Assert.Equal("-hostlocal", LaunchArgs(runner));
        Assert.Contains("a\nb", runner.Runs[0].Args);
    }

    [Fact]
    public void An_inherited_launch_args_variable_is_never_passed_on()
    {
        var runner = new FakeRunner(0);
        var env = TmpEnv();
        env["SOURCESHARP_ARGS"] = "-debugapi";
        Run(Found(), runner, env, "-novid");
        Assert.Null(LaunchArgs(runner));
    }

    // An instance's console commands (+sv_lan 1, +fps_max 60, ...) go into a
    // cfg of its own, run with one +exec: the engine's 512-byte command line
    // then carries "+exec sslaunch" for all of them. stuffcmds turns each
    // "+name value" into the line "name value", which is what the cfg holds.

    /// <summary>Runs an instance launch and returns its engine args and its cfg files' text, read while the game "runs".</summary>
    private (List<string> Args, Dictionary<string, string> Cfgs) InstanceLaunch(params string[] argv)
    {
        SeedSharedMod();
        var cfgs = new Dictionary<string, string>();
        FakeRunner runner = null!;
        runner = new FakeRunner(0)
        {
            OnRun = (_, _) =>
            {
                string cfg = Path.Combine(GameDirOf(runner.Runs[^1].Args), "cfg");
                foreach (string f in Directory.EnumerateFiles(cfg, "sslaunch*.cfg"))
                    cfgs[Path.GetFileName(f)] = File.ReadAllText(f);
            },
        };
        Run(Found(), runner, TmpEnv(), ["-instance", .. argv]);
        return (runner.Runs[0].Args, cfgs);
    }

    [Fact]
    public void An_instances_console_commands_go_to_its_launch_cfg()
    {
        var (_, cfgs) = InstanceLaunch("+sv_lan", "1", "+fps_max", "60");
        Assert.Equal("sv_lan 1\nfps_max 60\n", cfgs["sslaunch.cfg"]);
    }

    [Fact]
    public void An_instances_console_commands_leave_the_engines_command_line()
    {
        var (args, _) = InstanceLaunch("+sv_lan", "1", "+fps_max", "60");
        Assert.DoesNotContain("+sv_lan", args);
        Assert.DoesNotContain("+fps_max", args);
        Assert.Equal(["+exec", "sslaunch"], args.Skip(3));
    }

    [Fact]
    public void The_exec_takes_the_place_of_the_first_command_and_map_stays_after_it()
    {
        // +map is read by name (engine.so, launcher.so): it stays, and runs
        // after the settings as before.
        var (args, _) = InstanceLaunch("-novid", "+sv_lan", "1", "+map", "dm_lockdown");
        Assert.Equal(["-novid", "+exec", "sslaunch", "+map", "dm_lockdown"], args.Skip(3));
    }

    [Fact]
    public void Commands_after_a_kept_one_go_to_a_second_cfg_so_the_order_holds()
    {
        var (args, cfgs) = InstanceLaunch("+a", "1", "+map", "x", "+con_logfile", "l.log");
        Assert.Equal(["+exec", "sslaunch", "+map", "x", "+exec", "sslaunch2"], args.Skip(3));
        Assert.Equal("a 1\n", cfgs["sslaunch.cfg"]);
        Assert.Equal("con_logfile l.log\n", cfgs["sslaunch2.cfg"]);
    }

    [Fact]
    public void A_command_followed_by_a_flag_has_no_value_as_stuffcmds_reads_it()
    {
        var (args, cfgs) = InstanceLaunch("+fps_max", "-1");
        Assert.Equal("fps_max\n", cfgs["sslaunch.cfg"]);
        Assert.Contains("-1", args);
    }

    [Fact]
    public void A_last_command_has_no_value()
    {
        var (_, cfgs) = InstanceLaunch("+quit");
        Assert.Equal("quit\n", cfgs["sslaunch.cfg"]);
    }

    [Fact]
    public void A_word_after_a_commands_value_stays_on_the_command_line()
    {
        var (args, cfgs) = InstanceLaunch("+fps_max", "60", "extra");
        Assert.Equal("fps_max 60\n", cfgs["sslaunch.cfg"]);
        Assert.Equal(["+exec", "sslaunch", "extra"], args.Skip(3));
    }

    [Fact]
    public void A_flags_value_stays_with_its_flag()
    {
        var (args, _) = InstanceLaunch("-w", "640", "+fps_max", "60");
        Assert.Equal(["-w", "640", "+exec", "sslaunch"], args.Skip(3));
    }

    [Fact]
    public void Commands_the_engine_reads_by_name_stay_on_the_command_line()
    {
        string[] kept = ["+map", "+connect", "+maxplayers", "+port", "+playdemo", "+timedemo", "+sv_pure", "+load", "+mat_dxlevel", "+mat_hdr_level"];
        foreach (string k in kept)
        {
            var (args, cfgs) = InstanceLaunch(k, "v");
            Assert.True(args.Skip(3).SequenceEqual([k, "v"]), k);
            Assert.Empty(cfgs);
        }
    }

    [Fact]
    public void Commands_read_by_name_are_matched_ignoring_case()
    {
        var (args, _) = InstanceLaunch("+MAP", "x");
        Assert.Equal(["+MAP", "x"], args.Skip(3));
    }

    [Fact]
    public void A_launch_cfg_in_the_tree_is_never_written_through()
    {
        SeedSharedMod();
        File.WriteAllText(Path.Combine(SharedMod, "cfg", "sslaunch.cfg"), "tree's own\n");
        var (_, cfgs) = InstanceLaunch("+sv_lan", "1");
        Assert.Equal("sv_lan 1\n", cfgs["sslaunch.cfg"]);
        Assert.Equal("tree's own\n", File.ReadAllText(Path.Combine(SharedMod, "cfg", "sslaunch.cfg")));
    }

    [Fact]
    public void A_con_logfile_moved_to_the_cfg_still_lands_in_the_shared_mod_dir()
    {
        SeedSharedMod();
        FakeRunner runner = null!;
        runner = new FakeRunner(0)
        {
            OnRun = (_, _) => File.AppendAllText(Path.Combine(GameDirOf(runner.Runs[^1].Args), "mine.log"), "logfile line\n"),
        };
        Run(Found(), runner, TmpEnv(), "-instance", "+con_logfile", "mine.log");
        Assert.DoesNotContain("+con_logfile", runner.Runs[0].Args);
        Assert.Equal("logfile line\n", File.ReadAllText(Path.Combine(SharedMod, "mine.log")));
    }

    // The debug API's /console follows the file +con_logfile names, and it
    // found the name on the engine's command line. An instance moves the
    // command into cfg/sslaunch.cfg, so /console followed console.log, a file
    // that game never wrote (3f-gatebisect's join check, 10 of 10 rounds red,
    // 2026-10-05). The launcher, which saw the argument, names the file.
    [Fact]
    public void An_instance_names_its_con_logfile_to_the_game()
    {
        SeedSharedMod();
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-instance", "-condebug", "+con_logfile", "mine.log");
        runner.Runs[0].Env.TryGetValue("SOURCESHARP_CONSOLE_LOG", out string? named);
        Assert.Equal(Path.Combine(GameDirOf(runner.Runs[0].Args), "mine.log"), named);
    }

    [Fact]
    public void The_last_con_logfile_is_the_one_named()
    {
        SeedSharedMod();
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-instance", "+con_logfile", "a.log", "+con_logfile", "b.log");
        Assert.Equal(Path.Combine(GameDirOf(runner.Runs[0].Args), "b.log"),
                     runner.Runs[0].Env[Launcher.Launcher.ConsoleLogVariable]);
    }

    [Fact]
    public void A_rooted_con_logfile_is_named_as_given()
    {
        SeedSharedMod();
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-instance", "+con_logfile", "/abs/x.log");
        Assert.Equal("/abs/x.log", runner.Runs[0].Env[Launcher.Launcher.ConsoleLogVariable]);
    }

    [Fact]
    public void Without_instance_a_con_logfile_is_named_in_the_mod_dir()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "+con_logfile", "mine.log");
        Assert.Equal(Path.Combine(GameDirOf(runner.Runs[0].Args), "mine.log"),
                     runner.Runs[0].Env[Launcher.Launcher.ConsoleLogVariable]);
    }

    [Fact]
    public void Without_a_con_logfile_no_console_log_is_named()
    {
        SeedSharedMod();
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "-instance", "-condebug");
        Assert.False(runner.Runs[0].Env.ContainsKey(Launcher.Launcher.ConsoleLogVariable));
    }

    [Fact]
    public void A_console_log_the_user_named_is_kept()
    {
        SeedSharedMod();
        var runner = new FakeRunner(0);
        Dictionary<string, string> env = TmpEnv();
        env[Launcher.Launcher.ConsoleLogVariable] = "/mine.log";
        Run(Found(), runner, env, "-instance", "+con_logfile", "x.log");
        Assert.Equal("/mine.log", runner.Runs[0].Env[Launcher.Launcher.ConsoleLogVariable]);
    }

    [Fact]
    public void The_launcher_names_the_console_log_in_the_variable_the_debug_api_reads()
    {
        Assert.Equal(Debug.Api.ConsoleTail.PathVariable, Launcher.Launcher.ConsoleLogVariable);
    }

    [Fact]
    public void Without_instance_console_commands_stay_on_the_command_line()
    {
        var runner = new FakeRunner(0);
        Run(Found(), runner, TmpEnv(), "+sv_lan", "1");
        Assert.Equal(["+sv_lan", "1"], runner.Runs[0].Args.Skip(3));
    }

    [Fact]
    public void A_command_holding_a_newline_stays_on_the_command_line()
    {
        var (args, cfgs) = InstanceLaunch("+say", "a\nb");
        Assert.Equal(["+say", "a\nb"], args.Skip(3));
        Assert.Empty(cfgs);
    }

    [Fact]
    public void Material_system_convars_stay_on_the_command_line()
    {
        // the engine's material-system setup reads "+mat_<name>" by name to override the video
        // config before any cfg runs, and the material system's convars take
        // their command-line value at registration.
        var (args, cfgs) = InstanceLaunch("+mat_queue_mode", "0");
        Assert.Equal(["+mat_queue_mode", "0"], args.Skip(3));
        Assert.Empty(cfgs);
    }
}
