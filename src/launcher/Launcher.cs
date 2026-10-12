using SourceSharp.Mods;

namespace SourceSharp.Launcher;

/// <summary>
/// source#: find the engine install through Steam, then start the engine on
/// the mod mods.yaml names -- the native launcher's job and Valve's hl2.sh's
/// job in one place.
///
/// <para>hl2.sh is Valve's, ships with the Steam install and is in neither
/// repo; it disappeared once under a Steam update and the launcher then exec'd
/// nothing. Everything it did is done here: LD_LIBRARY_PATH, the fd limit,
/// __GL_THREADED_OPTIMIZATIONS, ENABLE_PATHMATCH, the working directory, the
/// GAME_DEBUGGER wrapper, and restarting while the game exits with the restart
/// code. That loop is why this spawns and waits rather than exec'ing; the other
/// reason is the fatal report the game leaves when it stops itself
/// (<see cref="FatalExitCode"/>), which is shown or pointed at here.</para>
/// </summary>
internal sealed class Launcher(
    ISteam steam,
    IProcessRunner runner,
    IRlimit rlimit,
    IMessageBox messageBox,
    TextWriter stdout,
    TextWriter stderr)
{
    public const string Prefix = "[Source Mod Launcher]";

    /// <summary>
    /// The code the game exits with when it stopped itself on a failed assert
    /// or Error(): -3, kSourceSharpFatalExitCode in fatal_report.h. It has
    /// already written the report to stderr and to assert-&lt;pid&gt;.txt in
    /// <see cref="AssertDirVariable"/>.
    /// </summary>
    public const int FatalExitCode = 253;

    /// <summary>Where the game writes assert-&lt;pid&gt;.txt: the mod dir, beside console.log, unless the user set it.</summary>
    public const string AssertDirVariable = "SOURCESHARP_ASSERT_DIR";

    /// <summary>
    /// -background: a game an agent or a gate started, which must not take the
    /// user's desktop focus. The launcher names its window's X11 class
    /// <see cref="BackgroundWindowClass"/> (SDL2 reads
    /// <see cref="WindowClassVariable"/> when it creates the window), and
    /// tools/kwin-bg-rule's KWin rule matches that class: never activated,
    /// kept below, out of the taskbar. Never minimised: SDL2 spins forever in
    /// SDL_SetWindowBordered on a window minimised during start-up.
    /// </summary>
    public const string BackgroundFlag = "-background";

    /// <summary>The environment variable SDL2's X11 backend takes the window class from.</summary>
    public const string WindowClassVariable = "SDL_VIDEO_X11_WMCLASS";

    /// <summary>The window class of a background game: what tools/kwin-bg-rule matches.</summary>
    public const string BackgroundWindowClass = "sourcesharp-bg";

    /// <summary>
    /// What a background game also needs on its command line. A game without
    /// focus sleeps engine_no_focus_sleep (50) ms a frame: 19 fps measured
    /// against 59 with 0 (2026-10-04, dm_lockdown, fps_max 60), and every gate
    /// would then run at a third of its usual frame rate. Placed before the
    /// user's arguments, so an explicit +engine_no_focus_sleep still wins.
    /// </summary>
    public static readonly string[] BackgroundEngineArgs = ["+engine_no_focus_sleep", "0"];

    /// <summary>
    /// -instance: a game that is one of several started from one tree. It runs
    /// on a game directory of its own (<see cref="InstanceGameDir"/>) under
    /// <see cref="InstanceBase"/>, removed when the launcher exits, so no two
    /// such games share a file the engine writes.
    ///
    /// <para>Why: the engine rewrites videoconfig_linux.cfg in the mod dir in
    /// place, 33 times during start-up (open O_TRUNC, write, close; no rename;
    /// strace of one game, 2026-10-05), and reads it 37 times in between. Games
    /// started together read each other's half-written file: hundreds of
    /// "KeyValues Error: LoadFromBuffer: missing {" lines, and 26 of 160 servers
    /// that never reached their dev API socket (docs/todo.md, 2026-10-05).
    /// cfg/config.cfg is rewritten the same way at shutdown.</para>
    ///
    /// <para>The user's own game (make run) leaves it off: its settings are meant
    /// to persist in the tree's mod dir.</para>
    /// </summary>
    public const string InstanceFlag = "-instance";

    /// <summary>
    /// The files in a mod dir the engine rewrites in place: an instance gets a
    /// private copy of each, seeded from the tree's (strace, 2026-10-05).
    /// </summary>
    internal static readonly string[] InstancePrivateFiles = ["videoconfig_linux.cfg", Path.Combine("cfg", "config.cfg")];

    /// <summary>
    /// What the engine creates in a mod dir as it runs (strace, 2026-10-05):
    /// never linked into an instance, so an instance creates its own.
    /// </summary>
    internal static readonly string[] InstanceFreshEntries =
        ["demoheader.tmp", "download", "downloadlists", "sound_workshop", "save", "screenshots"];

    /// <summary>
    /// The most the engine accepts: libtier0's BuildCmdLine compares the sum of
    /// its arguments' lengths (<see cref="EngineCommandLineLength"/>) with 0x200
    /// and exits with "command line too long, 512 max" past it. Both roles: the
    /// client's launcher.so and the dedicated server's dedicated_srv.so call it
    /// (libtier0.so and libtier0_srv.so, 2026-10-05). Probed: a sum of 512 runs,
    /// 513 is refused.
    /// </summary>
    public const int EngineCommandLineLimit = 512;

    /// <summary>
    /// Where SourceSharp's own flags go instead of the engine's command line,
    /// one argument per line: the managed host (ProcessCommandLine) and native
    /// code (ss_command_line.cpp) read it as if it followed argv. Never
    /// inherited: only the launcher that moved the flags names them.
    /// </summary>
    public const string LaunchArgsVariable = "SOURCESHARP_ARGS";

    /// <summary>
    /// SourceSharp's switches: read by our code, never by the engine (none is in
    /// any engine binary's strings, 2026-10-05). Readers: -debugapi
    /// (DebugApiLoader), -debugapisocket (DebugApiServer), -debugui
    /// (DebugApiClientLoader), -debuguiserve and -debuguipublic
    /// (DebugChannelListener), -assertlog and -assertstack (native
    /// shell_diagnostics.cpp), -unhosted (tf2_descent HostLink).
    /// </summary>
    internal static readonly string[] OurSwitches =
        ["-debugapi", "-debugapisocket", "-debugui", "-debuguiserve", "-debuguipublic", "-assertlog", "-assertstack", "-unhosted"];

    /// <summary>
    /// SourceSharp's options, each with the value after it: -debugapiport
    /// (DebugApiServer), -debuguiport (DebugChannelListener), -debuguihost
    /// (DebugChannelClient), -hostlocal (tf2_descent HostLink).
    /// </summary>
    internal static readonly string[] OurOptions = ["-debugapiport", "-debuguiport", "-debuguihost", "-hostlocal"];

    // A dialog is a summary; the whole report is on disk and on stderr.
    private const int MaxDialogChars = 4000;

    private bool _noMessageBox;
    private string? _engineInstallDir;

    /// <summary>
    /// Runs the launcher. <paramref name="launcherDir"/> is the directory of
    /// the launcher binary (bin/&lt;config&gt;): mods.yaml and
    /// bin/linux64/libsteam_api.so are found beside it.
    /// </summary>
    public int Run(IReadOnlyList<string> argv, string launcherDir, IReadOnlyDictionary<string, string> environment)
    {
        // Before anything that can fail: -nomessagebox decides how errors look.
        _noMessageBox = argv.Contains("-nomessagebox");

        if (argv.Contains("-help") || argv.Contains("--help"))
            return Help(launcherDir);

        if (!TakeModId(argv, out string? modId, out List<string> userArgs, out string? argError))
            return Error(argError!);

        // -dedicated picks the mod's engine.dedicated; -engine <dir> (or
        // SOURCESHARP_ENGINE_DIR) names the engine directory instead of asking
        // Steam; -resolve prints the mod's settings for that role and exits
        // (plan_host_sdk §2.5).
        bool dedicated = Take(userArgs, "-dedicated");
        bool resolve = Take(userArgs, "-resolve");
        bool background = Take(userArgs, BackgroundFlag);
        bool instance = Take(userArgs, InstanceFlag);
        if (!TakeValue(userArgs, "-engine", out string? engineDir, out argError))
            return Error(argError!);
        if (engineDir is null && environment.TryGetValue("SOURCESHARP_ENGINE_DIR", out string? fromEnv) && fromEnv.Length > 0)
            engineDir = fromEnv;

        ModsConfig config;
        Mod mod;
        try
        {
            config = ModsConfig.Load(Path.Combine(launcherDir, "mods.yaml"));
            mod = config.Select(modId);
        }
        catch (ModsConfigException e)
        {
            return Error(e.Message);
        }

        EngineSettings role;
        try
        {
            role = mod.Engine(dedicated);
        }
        catch (ModsConfigException e)
        {
            return Error(e.Message);
        }

        if (resolve)
        {
            try
            {
                stdout.Write(Resolved(role, dedicated ? mod.Browser : null));
            }
            catch (ModsConfigException e)
            {
                return Error(e.Message);
            }

            return 0;
        }

        if (!File.Exists(Path.Combine(mod.GameDir, "gameinfo.txt")))
            return Error($"Mod '{mod.Id}' has no gameinfo.txt in {mod.GameDir}.");

        string steamLibrary = Path.Combine(launcherDir, "bin", "linux64", "libsteam_api.so");
        string install;
        if (engineDir is not null)
        {
            install = Path.GetFullPath(engineDir);
        }
        else
        {
            SteamAnswer answer = steam.FindInstallDir(steamLibrary, mod.SteamAppId, role.AppId);
            if (answer.InstallDir is null)
                return Error(answer.Error!);
            install = answer.InstallDir;
        }

        _engineInstallDir = install;

        // The executable is in the engine directory, or in the install of the
        // app that ships it (exe_appid): the dedicated server's srcds_linux64.
        string exeInstall = install;
        if (!File.Exists(Path.Combine(install, role.Exe)) && role.ExeAppId != role.AppId)
        {
            SteamAnswer exeAnswer = steam.FindInstallDir(steamLibrary, mod.SteamAppId, role.ExeAppId);
            if (exeAnswer.InstallDir is null)
                return Error(exeAnswer.Error!);
            exeInstall = exeAnswer.InstallDir;
        }

        string exe = Path.Combine(exeInstall, role.Exe);
        if (!File.Exists(exe))
            return Error($"Could not start the engine.\n\nTried to run: {exe}\n\nNo such file or directory");

        // THE DEDICATED ENGINE CUTS ITS OWN PATH AT A SPACE: it derives its base
        // directory (|all_source_engine_paths|) from argv[0], and Steam installs
        // are named "Source SDK Base 2013 ...". So a dedicated run goes through a
        // view without spaces: links to the engine directory's entries and to the
        // executable, run from there (plan_host_sdk P0: BASE_PATH came out as
        // steamapps/common/ and no HL2 content mounted). A pod's /opt/srcds has
        // no space, which is why the host never met this.
        string libraryInstall = exeInstall;
        if (dedicated && (install.Contains(' ') || exeInstall.Contains(' ')))
        {
            string view = Path.Combine(launcherDir, "engines", "dedicated");
            try
            {
                EngineView(view, install, exe);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Error($"Could not build the engine view {view}: {e.Message}");
            }

            install = view;
            exe = Path.Combine(view, role.Exe);
        }

        if (background)
            userArgs.InsertRange(0, BackgroundEngineArgs);
        List<string> args = EngineArgs(mod, role, userArgs);

        string? instanceRoot = null;
        string? instanceGameDir = null;
        string? conLogFile = null;
        if (instance)
        {
            int at = args.IndexOf("-game");
            if (at < 0 || at + 1 >= args.Count)
                return Error("-game needs a directory after it.");
            string shared = Path.GetFullPath(args[at + 1]);
            try
            {
                instanceRoot = NewInstanceRoot(environment);
                args[at + 1] = InstanceGameDir(shared, instanceRoot, LogNames(args));
                instanceGameDir = args[at + 1];
                conLogFile = ConLogFilePath(args, instanceGameDir);
                MoveCommandsToCfg(args, Path.Combine(instanceGameDir, "cfg"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (instanceRoot is not null)
                    RemoveInstance(instanceRoot);
                return Error($"Could not build the instance game directory under {instanceRoot}: {e.Message}");
            }
        }
        else if (args.IndexOf("-game") is int gameAt and >= 0 && gameAt + 1 < args.Count)
        {
            conLogFile = ConLogFilePath(args, Path.GetFullPath(args[gameAt + 1]));
        }

        List<string> ours = TakeOurFlags(args);
        Dictionary<string, string> env = GameEnvironment(environment, install, libraryInstall, role, mod, config.Path);
        env.Remove(LaunchArgsVariable);
        if (ours.Count > 0)
            env[LaunchArgsVariable] = string.Join('\n', ours);
        // Never inherited: only the launcher that built the directory names it.
        env.Remove(Mod.GameDirVariable);
        if (instanceGameDir is not null)
            env[Mod.GameDirVariable] = instanceGameDir;
        if (conLogFile is not null && (!env.TryGetValue(ConsoleLogVariable, out string? userLog) || userLog.Length == 0))
            env[ConsoleLogVariable] = conLogFile;
        if (background && (!env.TryGetValue(WindowClassVariable, out string? windowClass) || windowClass.Length == 0))
            env[WindowClassVariable] = BackgroundWindowClass;

        string? limitError = rlimit.SetNoFile(role.FdLimit);
        if (limitError is not null)
            stderr.WriteLine($"{Prefix} could not set the open-file limit to {role.FdLimit}: {limitError}");

        string? tooLong = CommandLineTooLong(exe, args);
        if (tooLong is not null)
        {
            if (instanceRoot is not null)
                RemoveInstance(instanceRoot);
            return Error(tooLong);
        }

        (string runExe, List<string> runArgs) = WithDebugger(environment, exe, args);

        GameExit exit;
        try
        {
            do
            {
                exit = runner.Run(runExe, runArgs, env, install);
            }
            while (exit.Code == role.RestartExitCode);
        }
        finally
        {
            if (instanceRoot is not null)
                RemoveInstance(instanceRoot);
        }

        if (exit.Code == FatalExitCode)
            ReportFatal(env[AssertDirVariable], exit.Pid);

        return exit.Code;
    }

    /// <summary>
    /// The game stopped itself and wrote assert-&lt;pid&gt;.txt: say where, and
    /// show it unless -nomessagebox. Under GAME_DEBUGGER the pid is the
    /// debugger's, so the file may not be there; the report is still on stderr.
    /// </summary>
    private void ReportFatal(string assertDir, int pid)
    {
        string path = Path.Combine(assertDir, $"assert-{pid}.txt");
        string? report = null;
        try
        {
            report = File.ReadAllText(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        if (report is null)
        {
            stderr.WriteLine($"{Prefix} the game stopped on a fatal error (exit {FatalExitCode}); no {path}, the report is on its stderr above.");
            if (!_noMessageBox)
                messageBox.Show("Fatal Error", $"The game stopped on a fatal error.\n\nNo report at {path}; see the game's output.", _engineInstallDir);
            return;
        }

        stderr.WriteLine($"{Prefix} the game stopped on a fatal error (exit {FatalExitCode}); report: {path}");
        if (!_noMessageBox)
        {
            string text = report.Length > MaxDialogChars ? report[..MaxDialogChars] + "\n..." : report;
            messageBox.Show("Fatal Error", $"{text}\n\nThe report is saved in {path}", _engineInstallDir);
        }
    }

    /// <summary>
    /// -game &lt;abs game_dir&gt; unless the user gave -game, then the mod's own
    /// args, then the user's, in their original order.
    /// </summary>
    internal List<string> EngineArgs(Mod mod, EngineSettings role, List<string> userArgs)
    {
        var args = new List<string>();
        if (!userArgs.Contains("-game"))
        {
            stdout.WriteLine($"{Prefix} Launching default game: {mod.Id}");
            args.Add("-game");
            args.Add(mod.GameDir);
        }

        args.AddRange(role.Args);
        args.AddRange(userArgs);
        return args;
    }

    /// <summary>hl2.sh's environment, the mod's own, where Bootstrap finds mods.yaml, and where a fatal report goes.</summary>
    internal static Dictionary<string, string> GameEnvironment(
        IReadOnlyDictionary<string, string> current, string install, string exeInstall, EngineSettings role, Mod mod, string modsYaml)
    {
        var env = new Dictionary<string, string>(current, StringComparer.Ordinal);

        // hl2.sh: export LD_LIBRARY_PATH="${GAMEROOT}/bin/$plat_dir":$LD_LIBRARY_PATH
        // -- including the trailing colon when it was empty, kept for parity.
        current.TryGetValue("LD_LIBRARY_PATH", out string? ld);
        //
        // Then, when the executable comes from another install (exe_appid), that
        // install's libraries after the engine's: the dedicated server takes its
        // libsteam_api, filesystem_stdio, libtier0 and libvstdlib from 243750.
        string libraries = Path.Combine(install, "bin", "linux64");
        if (!string.Equals(Path.GetFullPath(exeInstall), Path.GetFullPath(install), StringComparison.Ordinal))
            libraries += ":" + Path.Combine(exeInstall, "bin", "linux64");
        env["LD_LIBRARY_PATH"] = libraries + ":" + (ld ?? "");
        env["__GL_THREADED_OPTIMIZATIONS"] = "1";
        if (File.Exists(Path.Combine(install, "pathmatch.inf")))
            env["ENABLE_PATHMATCH"] = "1";

        foreach (var (k, v) in role.Env)
            env[k] = v;

        if (!env.TryGetValue(AssertDirVariable, out string? assertDir) || string.IsNullOrEmpty(assertDir))
            env[AssertDirVariable] = mod.GameDir;

        env["SOURCESHARP_MODS_YAML"] = modsYaml;
        env["SOURCESHARP_MOD"] = mod.Id;
        return env;
    }

    /// <summary>
    /// GAME_DEBUGGER, as hl2.sh read it: gdb and cgdb get the game as their
    /// program (--args), anything else (strace -f -o x) is a command prefix.
    /// </summary>
    internal static (string Exe, List<string> Args) WithDebugger(
        IReadOnlyDictionary<string, string> env, string exe, List<string> args)
    {
        if (!env.TryGetValue("GAME_DEBUGGER", out string? dbg) || string.IsNullOrWhiteSpace(dbg))
            return (exe, args);

        string[] words = dbg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var list = new List<string>(words.Skip(1));
        if (words[0] is "gdb" or "cgdb")
            list.Add("--args");
        list.Add(exe);
        list.AddRange(args);
        return (words[0], list);
    }

    /// <summary>Strips -mod &lt;id&gt; from argv: it is the launcher's, not the engine's.</summary>
    internal static bool TakeModId(IReadOnlyList<string> argv, out string? modId, out List<string> rest, out string? error)
    {
        modId = null;
        error = null;
        rest = [];
        for (int i = 0; i < argv.Count; i++)
        {
            if (argv[i] == "-mod")
            {
                if (i + 1 >= argv.Count || argv[i + 1].StartsWith('-'))
                {
                    error = "-mod needs a mod id after it.";
                    return false;
                }

                modId = argv[++i];
                continue;
            }

            rest.Add(argv[i]);
        }

        return true;
    }

    /// <summary>
    /// Makes <paramref name="view"/> a directory of symbolic links: one per
    /// entry of <paramref name="install"/>, and the executable by its own name.
    /// Links it holds that no longer belong are removed; anything in it that is
    /// not a link is refused, so a mistake never deletes real files.
    /// </summary>
    internal static void EngineView(string view, string install, string exe)
    {
        Directory.CreateDirectory(view);
        var want = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in Directory.EnumerateFileSystemEntries(install))
            want[Path.GetFileName(entry)] = entry;
        want[Path.GetFileName(exe)] = exe;

        foreach (var present in Directory.EnumerateFileSystemEntries(view))
        {
            var info = new FileInfo(present);
            if (info.LinkTarget is null)
                throw new IOException($"{present} is not a link: the engine view holds only links");

            string name = Path.GetFileName(present);
            if (!want.TryGetValue(name, out var target) || info.LinkTarget != target)
                File.Delete(present);
        }

        foreach (var (name, target) in want)
        {
            string link = Path.Combine(view, name);
            if (new FileInfo(link).LinkTarget is null && !Directory.Exists(link) && !File.Exists(link))
                File.CreateSymbolicLink(link, target);
        }
    }

    /// <summary>Names the directory instances are made in, instead of <see cref="InstanceBase"/>'s default.</summary>
    public const string InstanceRootVariable = "SOURCESHARP_INSTANCE_ROOT";

    /// <summary>
    /// Where instance game dirs are made: /tmp/ssi-&lt;user&gt;, or
    /// <see cref="InstanceRootVariable"/>. Short and fixed, and never $TMPDIR:
    /// the game dir is -game, and libtier0 refuses a command line whose
    /// arguments add up to more than 512 bytes (<see cref="EngineCommandLineLimit"/>).
    /// A tool's TMPDIR under an agent's scratchpad is over 100 characters.
    /// </summary>
    internal static string InstanceBase(IReadOnlyDictionary<string, string> environment) =>
        environment.TryGetValue(InstanceRootVariable, out string? r) && r.Length > 0
            ? Path.GetFullPath(r)
            : "/tmp/ssi-" + Environment.UserName;

    /// <summary>
    /// A new, empty directory for one instance's game dir: &lt;base&gt;/&lt;this
    /// launcher's pid&gt;, or &lt;pid&gt;.&lt;n&gt; when that is taken. Instances
    /// left by launchers that are gone (killed with their process group, say)
    /// are removed first.
    /// </summary>
    internal string NewInstanceRoot(IReadOnlyDictionary<string, string> environment)
    {
        string bas = InstanceBase(environment);
        Directory.CreateDirectory(bas);
        SweepStaleInstances(bas);
        string pid = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < 100; i++)
        {
            string root = Path.Combine(bas, i == 0 ? pid : $"{pid}.{i}");
            if (Directory.Exists(root) || File.Exists(root))
                continue;
            Directory.CreateDirectory(root);
            return root;
        }

        throw new IOException($"no free instance directory name in {bas}");
    }

    /// <summary>
    /// Removes each &lt;pid&gt; or &lt;pid&gt;.&lt;n&gt; directory in
    /// <paramref name="bas"/> whose launcher is no longer running. Anything else
    /// there is left alone. Errors are reported, never thrown.
    /// </summary>
    internal void SweepStaleInstances(string bas)
    {
        foreach (string entry in Directory.EnumerateDirectories(bas))
        {
            string name = Path.GetFileName(entry);
            int dot = name.IndexOf('.');
            string pidText = dot < 0 ? name : name[..dot];
            if (dot >= 0 && !uint.TryParse(name[(dot + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
                continue;
            if (!int.TryParse(pidText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int pid) || pid <= 0)
                continue;
            if (Directory.Exists($"/proc/{pid}") || new FileInfo(entry).LinkTarget is not null)
                continue;
            RemoveInstance(entry);
        }
    }

    /// <summary>
    /// The console logs a game writes into its mod dir, as its arguments name
    /// them: console.log (-condebug) and the <c>+con_logfile</c> file. Tools
    /// read both from the tree's mod dir, so an instance links them there.
    /// </summary>
    internal static List<string> LogNames(IReadOnlyList<string> args)
    {
        var names = new List<string> { "console.log" };
        for (int i = 0; i + 1 < args.Count; i++)
        {
            string name = args[i + 1];
            if (args[i] == "+con_logfile" && !name.StartsWith('-') && !name.StartsWith('+') && !name.Contains('/'))
                names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// Names the file the game's console log is written to, for the debug
    /// API's /console (Debug.Api.ConsoleTail reads it first). Set from the
    /// last <c>+con_logfile</c>, never inherited over one the user set.
    /// </summary>
    public const string ConsoleLogVariable = "SOURCESHARP_CONSOLE_LOG";

    /// <summary>
    /// The file the last <c>+con_logfile</c> in <paramref name="args"/> moves
    /// the console log to, as the engine opens it (relative to
    /// <paramref name="gameDir"/>), or null when there is none.
    ///
    /// <para>Published because /console used to find the name on the engine's
    /// command line, and an instance moves it into cfg/sslaunch.cfg: /console
    /// then followed console.log, which that game never wrote.</para>
    /// </summary>
    internal static string? ConLogFilePath(IReadOnlyList<string> args, string gameDir)
    {
        string? name = null;
        for (int i = 0; i + 1 < args.Count; i++)
        {
            string value = args[i + 1];
            if (args[i] == "+con_logfile" && value.Length > 0 && !value.StartsWith('-') && !value.StartsWith('+'))
                name = value;
        }

        return name is null ? null : Path.IsPathRooted(name) ? name : Path.Combine(gameDir, name);
    }

    /// <summary>
    /// Builds <paramref name="root"/>/&lt;name of <paramref name="shared"/>&gt;: a
    /// game dir that reads the tree's mod dir and writes its own files. The
    /// last path component stays the mod dir's name, which the engine compares
    /// between a client and its server (COM_CheckGameDirectory).
    ///
    /// <list type="bullet">
    /// <item><see cref="InstancePrivateFiles"/>: private copies of the tree's
    /// (none when the tree has none; the engine then writes its own).</item>
    /// <item>cfg: a real directory of links to the tree's cfg files, so
    /// config.cfg can be private beside them.</item>
    /// <item><paramref name="logNames"/>: links to the tree's mod dir, existing
    /// or not, so the log lands where the tools read it.</item>
    /// <item><see cref="InstanceFreshEntries"/>: left out.</item>
    /// <item>everything else in the tree's mod dir: a link to it.</item>
    /// </list>
    /// </summary>
    internal static string InstanceGameDir(string shared, string root, IReadOnlyCollection<string> logNames)
    {
        string dir = Path.Combine(root, Path.GetFileName(Path.TrimEndingDirectorySeparator(shared)));
        Directory.CreateDirectory(dir);
        var privates = new HashSet<string>(InstancePrivateFiles, StringComparer.Ordinal);
        var skip = new HashSet<string>(InstanceFreshEntries, StringComparer.Ordinal);
        foreach (string log in logNames)
            skip.Add(log);

        foreach (string entry in Directory.EnumerateFileSystemEntries(shared))
        {
            string name = Path.GetFileName(entry);
            if (skip.Contains(name) || privates.Contains(name))
                continue;
            if (name == "cfg" && Directory.Exists(entry))
            {
                string cfg = Path.Combine(dir, "cfg");
                Directory.CreateDirectory(cfg);
                foreach (string file in Directory.EnumerateFileSystemEntries(entry))
                {
                    if (!privates.Contains(Path.Combine("cfg", Path.GetFileName(file))))
                        File.CreateSymbolicLink(Path.Combine(cfg, Path.GetFileName(file)), file);
                }

                continue;
            }

            File.CreateSymbolicLink(Path.Combine(dir, name), entry);
        }

        foreach (string file in privates)
        {
            string from = Path.Combine(shared, file);
            if (File.Exists(from))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, file))!);
                File.Copy(from, Path.Combine(dir, file));
            }
        }

        foreach (string log in logNames)
            File.CreateSymbolicLink(Path.Combine(dir, log), Path.Combine(shared, log));

        return dir;
    }

    /// <summary>
    /// Removes an instance's directory: its links (never what they point at),
    /// and the files and directories the game made. Errors are reported, never
    /// thrown: the game has already ended.
    /// </summary>
    internal void RemoveInstance(string root)
    {
        try
        {
            RemoveTree(root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"{Prefix} could not remove the instance game directory {root}: {e.Message}");
        }
    }

    private static void RemoveTree(string dir)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(dir))
        {
            var info = new FileInfo(entry);
            if (info.LinkTarget is not null || !Directory.Exists(entry))
                File.Delete(entry);
            else
                RemoveTree(entry);
        }

        Directory.Delete(dir);
    }

    /// <summary>
    /// The engine's own measure of its command line: the byte lengths of
    /// argv[0] and every argument, added up with no separators. libtier0's
    /// BuildCmdLine computes exactly this before anything else runs and exits
    /// past <see cref="EngineCommandLineLimit"/>.
    /// </summary>
    internal static int EngineCommandLineLength(string exe, IReadOnlyList<string> args) =>
        System.Text.Encoding.UTF8.GetByteCount(exe) + args.Sum(a => System.Text.Encoding.UTF8.GetByteCount(a));

    /// <summary>
    /// Refuses, loudly, a command line the engine would refuse quietly: it
    /// prints one line to its own stdout and exits -1, before any log, so a
    /// tool sees only a game that never opened its socket.
    /// </summary>
    private string? CommandLineTooLong(string exe, IReadOnlyList<string> args)
    {
        int length = EngineCommandLineLength(exe, args);
        if (length <= EngineCommandLineLimit)
        {
            stdout.WriteLine($"{Prefix} engine command line: {length} of {EngineCommandLineLimit} bytes");
            return null;
        }

        var longest = args.Prepend(exe)
                          .Select(a => (Bytes: System.Text.Encoding.UTF8.GetByteCount(a), Text: a))
                          .OrderByDescending(a => a.Bytes)
                          .Take(3)
                          .Select(a => $"  {a.Bytes} {a.Text}");
        return $"The engine command line is {length} bytes; the engine refuses more than {EngineCommandLineLimit} " +
               "(libtier0 adds up every argument's length, the executable's path included, and exits with " +
               "\"command line too long\"). The longest arguments:\n" + string.Join("\n", longest);
    }

    /// <summary>
    /// Takes <see cref="OurSwitches"/> and <see cref="OurOptions"/> (with the
    /// value after one, unless that is another parameter, as their readers take
    /// it) out of <paramref name="args"/>, in order. An argument holding a
    /// newline cannot be one line of <see cref="LaunchArgsVariable"/> and stays.
    /// </summary>
    internal static List<string> TakeOurFlags(List<string> args)
    {
        var moved = new List<string>();
        var kept = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            bool option = OurOptions.Contains(a, StringComparer.OrdinalIgnoreCase);
            if (!option && !OurSwitches.Contains(a, StringComparer.OrdinalIgnoreCase))
            {
                kept.Add(a);
                continue;
            }

            moved.Add(a);
            if (option && i + 1 < args.Count && !args[i + 1].StartsWith('-') && !args[i + 1].StartsWith('+') && !args[i + 1].Contains('\n'))
                moved.Add(args[++i]);
        }

        args.Clear();
        args.AddRange(kept);
        return moved;
    }

    /// <summary>
    /// The + parameters the engine reads by name through its command line
    /// (CommandLine()->ParmValue / FindParm), so moving one into a cfg would
    /// change what it does: every "+word" literal in engine.so and launcher.so
    /// (strings, 2026-10-05; +map in both: it decides whether a background map
    /// loads). They stay on the command line, in place.
    /// </summary>
    internal static readonly string[] CommandsReadByName =
    [
        "+map", "+connect", "+maxplayers", "+port", "+playdemo", "+timedemo", "+timedemoquit", "+sv_pure",
        "+host_timer_spin_ms", "+load", "+mat_texture_list", "+showbudget", "+showbudget_texture",
        "+showbudget_texture_global", "+showvprof", "+vgui_drawtree", "+voicerecord", "+demoui2",
        "+mat_dxlevel", "+mat_hdr_level",
    ];

    /// <summary>The launch cfg's name, as <c>+exec</c> takes it; a second one is sslaunch2, and so on.</summary>
    public const string LaunchCfg = "sslaunch";

    /// <summary>
    /// Moves an instance's console commands out of <paramref name="args"/> into
    /// &lt;<paramref name="cfgDir"/>&gt;/sslaunch.cfg, in their place a single
    /// <c>+exec sslaunch</c>. the engine's stuffcmds turns each "+name"
    /// into the line "name value", the value being the next argument unless that
    /// starts with '-' or '+'; the cfg holds the same lines, and exec runs them
    /// at the same point. <see cref="CommandsReadByName"/> stay, and a command
    /// after one of them starts another cfg (sslaunch2), so the order holds.
    ///
    /// <para>So do "+mat_*": the engine's material-system setup reads them by a built name to
    /// override the video config before any cfg runs. Every other convar is
    /// read by a built name only at registration (ICvar::GetCommandLineValue,
    /// stock game.cpp and shell_gamedll.cpp), which gives a game-DLL convar its
    /// value at load rather than at stuffcmds: both come before the first map.</para>
    ///
    /// <para>Not <c>@file</c>: the shipped tier0 expands it after the 512-byte
    /// check, but CreateCmdLine has already wrapped each argument in quotes, so
    /// the whole file becomes ONE parameter ("+fps_max 33 +echo x" set fps_max
    /// to "33 +echo x -insecure"; probed 2026-10-05), and the quote it leaves
    /// open breaks the arguments after it (+map then failed).</para>
    /// </summary>
    internal static void MoveCommandsToCfg(List<string> args, string cfgDir)
    {
        var result = new List<string>();
        var cfgs = new List<System.Text.StringBuilder>();
        System.Text.StringBuilder? current = null;
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            bool hasValue = i + 1 < args.Count && !args[i + 1].StartsWith('-') && !args[i + 1].StartsWith('+');
            if (a.StartsWith('-'))
            {
                // A flag eats its value, as stuffcmds skips it.
                result.Add(a);
                if (hasValue)
                    result.Add(args[++i]);
                continue;
            }

            bool movable = a.Length > 1 && a.StartsWith('+')
                           && !CommandsReadByName.Contains(a, StringComparer.OrdinalIgnoreCase)
                           && !a.StartsWith("+mat_", StringComparison.OrdinalIgnoreCase)
                           && !a.Contains('\n') && !(hasValue && args[i + 1].Contains('\n'));
            if (!movable)
            {
                if (a.StartsWith('+'))
                {
                    current = null;
                    result.Add(a);
                    if (hasValue)
                        result.Add(args[++i]);
                }
                else
                {
                    result.Add(a);
                }

                continue;
            }

            if (current is null)
            {
                current = new System.Text.StringBuilder();
                cfgs.Add(current);
                result.Add("+exec");
                result.Add(cfgs.Count == 1 ? LaunchCfg : LaunchCfg + cfgs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            current.Append(a[1..]);
            if (hasValue)
                current.Append(' ').Append(args[++i]);
            current.Append('\n');
        }

        if (cfgs.Count == 0)
            return;

        Directory.CreateDirectory(cfgDir);
        for (int n = 0; n < cfgs.Count; n++)
        {
            string path = Path.Combine(cfgDir, (n == 0 ? LaunchCfg : LaunchCfg + (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)) + ".cfg");
            // A link to a tree's file of that name: never write through it.
            if (new FileInfo(path).LinkTarget is not null)
                File.Delete(path);
            File.WriteAllText(path, cfgs[n].ToString());
        }

        args.Clear();
        args.AddRange(result);
    }

    /// <summary>A switch: removed from <paramref name="args"/> if present.</summary>
    internal static bool Take(List<string> args, string flag) => args.Remove(flag);

    /// <summary>An option with a value: both removed from <paramref name="args"/>.</summary>
    internal static bool TakeValue(List<string> args, string option, out string? value, out string? error)
    {
        value = null;
        error = null;
        int at = args.IndexOf(option);
        if (at < 0)
            return true;

        if (at + 1 >= args.Count || args[at + 1].StartsWith('-'))
        {
            error = $"{option} needs a value after it.";
            return false;
        }

        value = args[at + 1];
        args.RemoveRange(at, 2);
        return true;
    }

    /// <summary>
    /// One role's resolved settings as the flat YAML a POSIX sh reads without a
    /// YAML library (the host's pod entrypoint; shape agreed 2026-09-30): plain
    /// scalars only, keys in this order, block lists, and <c>[]</c> / <c>{}</c>
    /// when empty. A value that would need quoting is refused rather than
    /// written in a form the reader would misparse.
    ///
    /// <para>A hosted mod's dedicated role ends with its <c>browser</c> block
    /// (host plan_steam_auth S-18), the one nested block: its strings are
    /// templates, so <c>{</c> and spaces are common, and they are written
    /// double-quoted; a control character is refused.</para>
    /// </summary>
    internal static string Resolved(EngineSettings role, BrowserSettings? browser = null)
    {
        static string Plain(string what, string value)
        {
            bool bad = value.Length == 0 || value.Contains('\n') || value.Contains('\r') || value.Contains(": ")
                       || value.Contains(" #") || value.StartsWith('#') || value.Contains('"') || value.Contains('\'')
                       || value != value.Trim();
            if (bad)
                throw new ModsConfigException($"{what} '{value}' is not a plain YAML scalar the pod's reader can take");
            return value;
        }

        var w = new System.Text.StringBuilder();
        w.Append("# generated by source# -resolve; do not edit\n");
        w.Append($"fd_limit: {role.FdLimit}\n");
        w.Append($"restart_exit_code: {role.RestartExitCode}\n");
        if (role.Args.Count == 0)
        {
            w.Append("args: []\n");
        }
        else
        {
            w.Append("args:\n");
            foreach (var a in role.Args)
                w.Append($"  - {Plain("an arg", a)}\n");
        }

        if (role.Env.Count == 0)
        {
            w.Append("env: {}\n");
        }
        else
        {
            w.Append("env:\n");
            foreach (var (k, v) in role.Env.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (k.StartsWith('-') || k.StartsWith('+'))
                    throw new ModsConfigException($"env key '{k}' starts with {k[0]}");
                w.Append($"  {Plain("an env key", k)}: {Plain("an env value", v)}\n");
            }
        }

        if (browser is not null)
            Browser(w, browser);

        return w.ToString();
    }

    private static void Browser(System.Text.StringBuilder w, BrowserSettings b)
    {
        static string Quoted(string what, string value)
        {
            if (value.Any(char.IsControl))
                throw new ModsConfigException($"browser {what} holds a control character");
            return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        w.Append("browser:\n");
        w.Append($"  name: {Quoted("name", b.Name)}\n");
        w.Append($"  description: {Quoted("description", b.Description)}\n");
        w.Append($"  tags: {Quoted("tags", b.Tags)}\n");
        w.Append($"  region: {Quoted("region", b.Region)}\n");
        w.Append($"  max_players: {b.MaxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n");
        w.Append($"  map: {Quoted("map", b.Map)}\n");
        if (b.Rules.Count == 0)
        {
            w.Append("  rules: {}\n");
            return;
        }

        w.Append("  rules:\n");
        foreach (var (k, v) in b.Rules.OrderBy(p => p.Key, StringComparer.Ordinal))
            w.Append($"    {k}: {Quoted("rule " + k, v)}\n");
    }

    private int Help(string launcherDir)
    {
        stdout.WriteLine("source# [-mod <id>] [-dedicated] [-engine <dir>] [-resolve] [-background] [-instance] [engine arguments...]");
        stdout.WriteLine();
        stdout.WriteLine("Starts Source SDK Base 2013 Multiplayer on a mod listed in mods.yaml,");
        stdout.WriteLine("beside this launcher. With no -mod, the file's default. With no -game,");
        stdout.WriteLine("the mod's game_dir. -nomessagebox reports errors on stderr only.");
        stdout.WriteLine("-dedicated runs the mod's dedicated server; -engine <dir> (or");
        stdout.WriteLine("SOURCESHARP_ENGINE_DIR) names the engine directory instead of asking");
        stdout.WriteLine("Steam; -resolve prints the role's resolved settings and exits.");
        stdout.WriteLine("-background starts a game that does not take the desktop focus");
        stdout.WriteLine("(window class sourcesharp-bg, for tools/kwin-bg-rule's KWin rule).");
        stdout.WriteLine("-instance runs on a game directory of its own under /tmp/ssi-<user>, so");
        stdout.WriteLine("several games started from one tree never share a file the engine");
        stdout.WriteLine("writes; console.log and +con_logfile still land in the mod dir.");
        stdout.WriteLine("Its console commands (+name value) go to cfg/sslaunch.cfg there, run");
        stdout.WriteLine("with one +exec; those the engine reads by name (+map, +connect, ...) stay.");
        stdout.WriteLine("SourceSharp's own flags (-debugapi, -assertlog, -debuguiport <n>, ...)");
        stdout.WriteLine("reach the game through SOURCESHARP_ARGS, not the engine's command line,");
        stdout.WriteLine("which the engine refuses past 512 bytes.");
        try
        {
            var config = ModsConfig.Load(Path.Combine(launcherDir, "mods.yaml"));
            stdout.WriteLine();
            stdout.WriteLine($"Mods ({config.Path}):");
            foreach (var mod in config.Mods.Values.OrderBy(m => m.Id, StringComparer.Ordinal))
                stdout.WriteLine($"  {mod.Id,-12} {mod.Title}{(mod.Id == config.DefaultId ? "  (default)" : "")}");
        }
        catch (ModsConfigException e)
        {
            stdout.WriteLine();
            stdout.WriteLine($"mods.yaml: {e.Message}");
        }

        return 0;
    }

    /// <summary>
    /// Every failure: stderr always, so unattended runs see it, and a dialog
    /// unless -nomessagebox -- the engine's own switch for "nobody is there".
    /// </summary>
    private int Error(string text)
    {
        stderr.WriteLine($"{Prefix} {text}");
        if (!_noMessageBox)
            messageBox.Show("Launcher Error", text, _engineInstallDir);
        return 1;
    }
}
