using System.Diagnostics;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Which engine interface names each side serves to managed code
/// (shell_vtable_interfaces.cpp's SourceSharpEntriesFor), read from what each
/// side actually compiles: the TU preprocessed with that target's own compile
/// command, so a row inside the wrong #ifdef is seen as absent. Each side its
/// own string-table container: the client's is what reads the InfoPanel's
/// "motd" (plan_client_ui, VGUIMenu info type 1).
/// </summary>
public class ServedInterfaceTests
{
    private const string Tu = "shell_vtable_interfaces.cpp";

    /// <summary>SourceSharpEntriesFor's body as <paramref name="target"/> compiles it.</summary>
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

        int at = text.IndexOf("int SourceSharpEntriesFor(", StringComparison.Ordinal);
        int open = text.IndexOf('{', at);
        int end = text.IndexOf("\n}\n", open, StringComparison.Ordinal);
        Assert.True(at >= 0 && open > at && end > open, $"no SourceSharpEntriesFor body in {target}'s {Tu}");
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

    [Fact]
    public void The_client_serves_its_own_string_table_container_and_not_the_servers()
    {
        string served = ServedBy("client_sourcesharp");
        Assert.Contains("\"VEngineClient014\"", served);      // the scan reads the right body
        Assert.Contains("\"VEngineClientStringTable001\"", served);
        Assert.DoesNotContain("\"VEngineServerStringTable001\"", served);
    }

    [Fact]
    public void The_client_serves_its_vphysics_for_the_managed_ragdolls()
    {
        string served = ServedBy("client_sourcesharp");
        Assert.Contains("\"VPhysicsSurfaceProps001\"", served);
        Assert.Contains("\"VPhysics031\"", served);
        Assert.Contains("\"VPhysicsCollision007\"", served);
    }

    [Fact]
    public void The_server_serves_the_surface_table_and_not_the_clients_vphysics()
    {
        string served = ServedBy("server_sourcesharp");
        Assert.Contains("\"VPhysicsSurfaceProps001\"", served);
        Assert.DoesNotContain("\"VPhysics031\"", served);
    }

    [Fact]
    public void The_server_serves_its_own_string_table_container_and_not_the_clients()
    {
        string served = ServedBy("server_sourcesharp");
        Assert.Contains("\"VEngineServer023\"", served);
        Assert.Contains("\"VEngineServerStringTable001\"", served);
        Assert.DoesNotContain("\"VEngineClientStringTable001\"", served);
    }

    /// <summary>
    /// Every name the server's managed physics acquires from the physics
    /// factory is one the server serves: a name it does not is answered
    /// "factory does not know that interface", and the player shadow
    /// (plan_physics_bones_native_zero 2.5) then builds no body at all -- a
    /// push that moves nothing, with no error anywhere but one warning line.
    /// </summary>
    [Fact]
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
    [Fact]
    public void Both_sides_serve_the_sound_emitter()
    {
        Assert.Contains($"\"{SourceSharp.Abi.SoundEmitterBase.InterfaceVersion}\"", ServedBy("client_sourcesharp"));
        Assert.Contains($"\"{SourceSharp.Abi.SoundEmitterBase.InterfaceVersion}\"", ServedBy("server_sourcesharp"));
    }

    [Fact]
    public void The_server_serves_the_static_prop_manager_for_the_world_build()
    {
        Assert.Contains("\"StaticPropMgrServer002\"", ServedBy("server_sourcesharp"));
        Assert.DoesNotContain("\"StaticPropMgrServer002\"", ServedBy("client_sourcesharp"));
    }
}
