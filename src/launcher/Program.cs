using System.Collections;
using SourceSharp.Launcher;

// source#. Its directory is the game directory, bin/<config>: mods.yaml and
// bin/linux64/libsteam_api.so are found beside it. Environment.ProcessPath is
// the resolved /proc/self/exe, exact whatever the stack holds (the native
// launcher's unterminated readlink was the bug exepath.h existed to fix).
string launcherDir = Path.GetDirectoryName(Environment.ProcessPath)
    ?? throw new InvalidOperationException("no process path");

var env = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
    env[(string)e.Key] = (string?)e.Value ?? "";

var launcher = new Launcher(
    new NativeSteam(), new ProcessRunner(), new Rlimit(), new SdlMessageBox(),
    Console.Out, Console.Error, LauncherPlatform.Current);
return launcher.Run(args, launcherDir, env);
