using System.Diagnostics;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Which engine interface names each side serves to managed code
/// (shell_vtable_interfaces.cpp's SourceSharpServes), read from what each
/// side actually compiles: the TU preprocessed with that target's own compile
/// command, so a row inside the wrong #ifdef is seen as absent. Each side its
/// own string-table container: the client's is what reads the InfoPanel's
/// "motd" (plan_client_ui, VGUIMenu info type 1).
/// </summary>
public class ServedInterfaceTests
{
    private const string Tu = "shell_vtable_interfaces.cpp";

    /// <summary>SourceSharpServes's body as <paramref name="target"/> compiles it.</summary>
    private static string ServedBy(string target)
    {
        var entry = CMakeGraph.TargetEntries(target).Single(e => e.File.EndsWith("/" + Tu, StringComparison.Ordinal));
        var args = new List<string>();
        var words = ShellWords(entry.Command);
        for (int i = 0; i < words.Count; i++)
        {
            if (words[i] is "-o" or "-MF" or "-MT" or "-MQ") { i++; continue; }
            if (words[i] is "-c" or "-MD" or "-MMD") continue;
            args.Add(words[i]);
        }
        var start = new ProcessStartInfo(args[0]) { WorkingDirectory = CMakeGraph.BuildDir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in args.Skip(1).Append("-E").Append("-P"))
            start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        string text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"{target}'s preprocess of {Tu} failed: {p.StandardError.ReadToEnd()}");

        int at = text.IndexOf("bool SourceSharpServes(", StringComparison.Ordinal);
        int open = text.IndexOf('{', at);
        int end = text.IndexOf("\n}\n", open, StringComparison.Ordinal);
        Assert.True(at >= 0 && open > at && end > open, $"no SourceSharpServes body in {target}'s {Tu}");
        return text[open..end];
    }

    // A compile command as the shell splits it: quotes group, a backslash escapes.
    private static List<string> ShellWords(string command)
    {
        var words = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool any = false;
        char quote = '\0';
        for (int i = 0; i < command.Length; i++)
        {
            char c = command[i];
            if (quote == '\'')
            {
                if (c == '\'') quote = '\0'; else cur.Append(c);
            }
            else if (c == '\\' && i + 1 < command.Length)
            {
                cur.Append(command[++i]);
                any = true;
            }
            else if (quote == '"')
            {
                if (c == '"') quote = '\0'; else cur.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                any = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (any || cur.Length > 0) words.Add(cur.ToString());
                cur.Clear();
                any = false;
            }
            else
            {
                cur.Append(c);
            }
        }
        if (any || cur.Length > 0) words.Add(cur.ToString());
        return words;
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void The_client_serves_its_own_string_table_container_and_not_the_servers()
    {
        string served = ServedBy("client_sourcesharp");
        Assert.Contains("\"VEngineClient014\"", served);      // the scan reads the right body
        Assert.Contains("\"VEngineClientStringTable001\"", served);
        Assert.DoesNotContain("\"VEngineServerStringTable001\"", served);
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void The_client_serves_its_vphysics_for_the_managed_ragdolls()
    {
        string served = ServedBy("client_sourcesharp");
        Assert.Contains("\"VPhysicsSurfaceProps001\"", served);
        Assert.Contains("\"VPhysics031\"", served);
        Assert.Contains("\"VPhysicsCollision007\"", served);
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void The_server_serves_the_surface_table_and_not_the_clients_vphysics()
    {
        string served = ServedBy("server_sourcesharp");
        Assert.Contains("\"VPhysicsSurfaceProps001\"", served);
        Assert.DoesNotContain("\"VPhysics031\"", served);
    }

    /// <summary>
    /// The server serves the game event manager ServerGameEvents fires
    /// through. Unserved from bd894daf1 to 01862e849, the server fired no game
    /// event: the live gate's kill-feed, changename, frag-credit and round-end
    /// steps all failed (bisected by b6).
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void The_server_serves_the_game_event_manager()
        => Assert.Contains("\"GAMEEVENTSMANAGER002\"", ServedBy("server_sourcesharp"));

    /// <summary>
    /// The server serves the engine sound interface the managed SoundEmitter
    /// emits through (plan_server_native_v2 T4a); unserved, no sound plays.
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void The_server_serves_the_engine_sound_interface()
        => Assert.Contains("\"IEngineSoundServer003\"", ServedBy("server_sourcesharp"));

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void The_server_serves_its_own_string_table_container_and_not_the_clients()
    {
        string served = ServedBy("server_sourcesharp");
        Assert.Contains("\"VEngineServer023\"", served);
        Assert.Contains("\"VEngineServerStringTable001\"", served);
        Assert.DoesNotContain("\"VEngineClientStringTable001\"", served);
    }

    /// <summary>
    /// Each side serves its own engine trace, by its own name: the movement
    /// loop's point contents (GameMovement.CheckWater, through EngineBound.Trace)
    /// acquires EngineTraceServer003 on the server and EngineTraceClient003 on
    /// the client. Unserved, the acquisition throws on every tick, on both sides
    /// (IManagedPlayerCommands.MoveProcessMovement and
    /// IManagedPredictionClient.ClientPredictionUpdate, live on dm_lockdown at
    /// 668496d40 to e814cd6d3).
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void Each_side_serves_its_own_engine_trace_and_not_the_others()
    {
        string server = ServedBy("server_sourcesharp");
        Assert.Contains($"\"{SourceSharp.Abi.EngineTrace.ServerInterfaceVersion}\"", server);
        Assert.DoesNotContain($"\"{SourceSharp.Abi.EngineTrace.ClientInterfaceVersion}\"", server);
        string client = ServedBy("client_sourcesharp");
        Assert.Contains($"\"{SourceSharp.Abi.EngineTrace.ClientInterfaceVersion}\"", client);
        Assert.DoesNotContain($"\"{SourceSharp.Abi.EngineTrace.ServerInterfaceVersion}\"", client);
    }

    /// <summary>
    /// Every name the server's managed physics acquires from the physics
    /// factory is one the server serves: a name it does not is answered
    /// "factory does not know that interface", and the player shadow
    /// (plan_physics_bones_native_zero 2.5) then builds no body at all -- a
    /// push that moves nothing, with no error anywhere but one warning line.
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void The_server_serves_every_physics_table_its_managed_side_acquires()
    {
        string served = ServedBy("server_sourcesharp");
        Assert.Contains($"\"{SourceSharp.Abi.PhysicsSurfaceProps.InterfaceVersion}\"", served);
        Assert.Contains($"\"{SourceSharp.Abi.PhysicsCollision.InterfaceVersion}\"", served);
    }

    /// <summary>
    /// The world build's static props (plan_physics_bones_native_zero 2.6):
    /// managed asks IStaticPropMgrServer for their physics, as stock's
    /// PhysCreateWorld did. A name the server does not serve builds no world
    /// at all -- every crate falls through the floor.
    /// </summary>
    /// <summary>
    /// The client's surface sound precache (plan_physics_bones_native_zero 2.6)
    /// writes each script's index from the sound emitter's table; a name the
    /// client does not serve leaves every handle invalid.
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void Both_sides_serve_the_sound_emitter()
    {
        Assert.Contains($"\"{SourceSharp.Abi.SoundEmitterBase.InterfaceVersion}\"", ServedBy("client_sourcesharp"));
        Assert.Contains($"\"{SourceSharp.Abi.SoundEmitterBase.InterfaceVersion}\"", ServedBy("server_sourcesharp"));
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void The_server_serves_the_static_prop_manager_for_the_world_build()
    {
        Assert.Contains("\"StaticPropMgrServer002\"", ServedBy("server_sourcesharp"));
        Assert.DoesNotContain("\"StaticPropMgrServer002\"", ServedBy("client_sourcesharp"));
    }

    /// <summary>
    /// The debug overlay, which EngineDebugOverlay (shared interop) and the
    /// client's FlashlightSlots (r_flashlightvisualizetrace) acquire. engine.so
    /// registers one object under the name for both sides. A name a side does
    /// not serve is refused as an unknown interface, and every overlay call is
    /// then silently a no-op.
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void Both_sides_serve_the_debug_overlay()
    {
        Assert.Contains($"\"{SourceSharp.Abi.EngineDebugOverlay.InterfaceVersion}\"", ServedBy("client_sourcesharp"));
        Assert.Contains($"\"{SourceSharp.Abi.EngineDebugOverlay.InterfaceVersion}\"", ServedBy("server_sourcesharp"));
    }

    /// <summary>
    /// The client's static props (StaticPropMgrClient, the client's physics
    /// world): each side serves its own manager and not the other's.
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void The_client_serves_its_own_static_prop_manager_and_not_the_servers()
    {
        Assert.Contains("\"StaticPropMgrClient004\"", ServedBy("client_sourcesharp"));
        Assert.DoesNotContain("\"StaticPropMgrClient004\"", ServedBy("server_sourcesharp"));
    }

    // Names a managed file acquires on purpose from a side that refuses them:
    // the answer it wants is the refusal.
    private static readonly Dictionary<string, string> Refused = new()
    {
        ["src/server/interop/ShimProbes.cs VEngineClient014"] =
            "the server's probe that IVEngineClient is NOT served to server-side managed code",
    };

    /// <summary>
    /// EVERY ENGINE NAME MANAGED ACQUIRES IS ONE ITS SIDE SERVES. A name
    /// Forwarders.TryAcquire asks for through an engine factory is refused by
    /// SourceSharpServes unless that side's arm lists it, and a fold's facts
    /// cannot see the refusal: FakeShimRoot answers any name. GAMEEVENTSMANAGER002,
    /// IEngineSoundServer003, VDebugOverlay003, StaticPropMgrClient004 and both
    /// EngineTrace names each went unserved that way (2026-10-19). Every
    /// interface-version literal in a runtime file that calls
    /// Forwarders.TryAcquire must be in the preprocessed body of each side that
    /// file compiles into: src/server is the server's, src/client the client's,
    /// src/shared both's, except a name that says Server or Client is that
    /// side's alone. Our own internal factory's names (SourceSharp*, SS*) are
    /// not engine names and are not asked of it.
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void Every_engine_name_managed_acquires_is_served_by_its_side()
    {
        string server = ServedBy("server_sourcesharp");
        string client = ServedBy("client_sourcesharp");
        var version = new System.Text.RegularExpressions.Regex("\"([A-Za-z_]+[0-9]{3})\"");
        var missing = new List<string>();
        int checkedNames = 0;

        foreach (string path in Directory.EnumerateFiles(RepoLayout.Repo("src"), "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(RepoLayout.Repo(), path).Replace('\\', '/');
            if (rel.StartsWith("src/test/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal))
                continue;
            string text = File.ReadAllText(path);
            if (!text.Contains("Forwarders.TryAcquire", StringComparison.Ordinal))
                continue;

            bool serverFile = rel.StartsWith("src/server/", StringComparison.Ordinal);
            bool clientFile = rel.StartsWith("src/client/", StringComparison.Ordinal);
            foreach (string name in version.Matches(text).Select(m => m.Groups[1].Value).Distinct())
            {
                if (name.StartsWith("SourceSharp", StringComparison.Ordinal) || name.StartsWith("SS", StringComparison.Ordinal)
                    || Refused.ContainsKey($"{rel} {name}"))
                    continue;

                bool forServer = serverFile || (!clientFile && !name.Contains("Client", StringComparison.Ordinal));
                bool forClient = clientFile || (!serverFile && !name.Contains("Server", StringComparison.Ordinal));
                checkedNames++;
                if (forServer && !server.Contains($"\"{name}\"", StringComparison.Ordinal))
                    missing.Add($"{rel}: {name} (server)");
                if (forClient && !client.Contains($"\"{name}\"", StringComparison.Ordinal))
                    missing.Add($"{rel}: {name} (client)");
            }
        }

        Assert.True(checkedNames >= 20, $"the scan reached the acquiring files ({checkedNames} names)");
        Assert.True(missing.Count == 0, "acquired but not served by SourceSharpServes:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_refusal_exemption_still_names_an_acquisition()
    {
        foreach (string key in Refused.Keys)
        {
            string[] parts = key.Split(' ');
            string text = File.ReadAllText(RepoLayout.Repo(parts[0]));
            Assert.Contains($"\"{parts[1]}\"", text);
            Assert.Contains("Forwarders.TryAcquire", text);
        }
    }
}

