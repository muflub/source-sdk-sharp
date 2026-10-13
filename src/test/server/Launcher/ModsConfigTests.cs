using SourceSharp.Mods;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// mods.yaml: what the launcher and Bootstrap accept, and what they refuse.
/// Every refusal is a message, never a silent default.
/// </summary>
public class ModsConfigTests
{
    // ---- the containment rule, under both platforms' spellings ----------
    //
    // ModsConfig.Inside refused every mod on Windows: it compared with a literal
    // '/', and GetFullPath answers with backslashes there. IsWithin takes the
    // separator and the comparison, so the Windows case is held here on any OS.

    [Fact]
    public void A_path_under_the_root_is_within_it_with_forward_slashes()
        => Assert.True(ModsConfig.IsWithin("/games/sharp", "/games/sharp/mods/test", '/', StringComparison.Ordinal));

    [Fact]
    public void A_path_under_the_root_is_within_it_with_backslashes()
        => Assert.True(ModsConfig.IsWithin(@"D:\a\game", @"D:\a\game\mods\test", '\\', StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void The_root_itself_is_within_it()
        => Assert.True(ModsConfig.IsWithin(@"D:\a\game\", @"D:\a\game", '\\', StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void A_sibling_sharing_the_roots_prefix_is_not_within_it()
        => Assert.False(ModsConfig.IsWithin(@"D:\a\game", @"D:\a\gameplay\mods", '\\', StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void A_parent_of_the_root_is_not_within_it()
        => Assert.False(ModsConfig.IsWithin("/games/sharp", "/games", '/', StringComparison.Ordinal));

    [Fact]
    public void Windows_drive_letter_case_does_not_matter()
        => Assert.True(ModsConfig.IsWithin(@"d:\a\game", @"D:\A\Game\mods", '\\', StringComparison.OrdinalIgnoreCase));

    private const string Path = "/games/sharp/bin/debug/mods.yaml";

    private const string Minimal = """
        default_mod: test
        mods:
          test:
            game_dir: mods/test
        """;

    private static ModsConfigException Refused(string yaml) =>
        Assert.Throws<ModsConfigException>(() => ModsConfig.Parse(yaml, Path));

    [Fact]
    public void TheDefaultModIsSelectedWhenNoIdIsGiven()
    {
        Assert.Equal("test", ModsConfig.Parse(Minimal, Path).Select(null).Id);
    }

    [Fact]
    public void GameDirIsResolvedAgainstTheFilesDirectory()
    {
        Assert.Equal("/games/sharp/bin/debug/mods/test", ModsConfig.Parse(Minimal, Path).Select(null).GameDir);
    }

    [Fact]
    public void A_game_running_in_an_instance_dir_has_that_GameDir()
    {
        var mod = ModsConfig.Parse(Minimal, Path).Select(null).RunningIn("/tmp/x/sourcesharp-instance-1/test");
        Assert.Equal("/tmp/x/sourcesharp-instance-1/test", mod.GameDir);
    }

    [Fact]
    public void A_game_with_no_instance_dir_keeps_the_mods_GameDir()
    {
        var mod = ModsConfig.Parse(Minimal, Path).Select(null);
        Assert.Same(mod, mod.RunningIn(null));
        Assert.Same(mod, mod.RunningIn(""));
    }

    [Fact]
    public void An_instance_dir_leaves_the_managed_dir_where_it_was()
    {
        var mod = ModsConfig.Parse(Minimal, Path).Select(null);
        Assert.Equal(mod.ManagedDir, mod.RunningIn("/tmp/x/sourcesharp-instance-1/test").ManagedDir);
    }

    [Fact]
    public void ManagedDirDefaultsToTheGameDirsBinManaged()
    {
        Assert.Equal("/games/sharp/bin/debug/mods/test/bin/managed",
            ModsConfig.Parse(Minimal, Path).Select(null).ManagedDir);
    }

    [Fact]
    public void EngineDefaultsAreWhatHl2ShDid()
    {
        var e = ModsConfig.Parse(Minimal, Path).Select(null).Client;
        Assert.Equal((243750u, "hl2_linux64", 243750u, 42, 2048), (e.AppId, e.Exe, e.ExeAppId, e.RestartExitCode, e.FdLimit));
        Assert.Empty(e.Args);
        Assert.Empty(e.Env);
    }

    [Fact]
    public void SteamAppIdDefaultsToTheEngineApp()
    {
        Assert.Equal(243750u, ModsConfig.Parse(Minimal, Path).Select(null).SteamAppId);
    }

    [Fact]
    public void AssembliesKeepTheirOrderPerSide()
    {
        var mod = ModsConfig.Parse("""
            mods:
              test:
                game_dir: mods/test
                assemblies:
                  server: [SourceSharp.Server, Extra.Server]
                  client: [SourceSharp.Client]
            """, Path).Select("test");
        Assert.Equal(["SourceSharp.Server", "Extra.Server"], mod.AssembliesFor(server: true));
        Assert.Equal(["SourceSharp.Client"], mod.AssembliesFor(server: false));
    }

    [Fact]
    public void AnUnknownKeyIsRefused()
    {
        var e = Refused("""
            mods:
              test:
                game_dir: mods/test
                gamedir: typo
            """);
        Assert.Contains("gamedir", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADefaultThatNamesNoModIsRefused()
    {
        var e = Refused("""
            default_mod: nope
            mods:
              test:
                game_dir: mods/test
            """);
        Assert.Contains("default_mod 'nope' names no mod", e.Message);
    }

    [Fact]
    public void AModWithoutGameDirIsRefused()
    {
        var e = Refused("""
            mods:
              test:
                title: no dir
            """);
        Assert.Contains("mod 'test' has no game_dir", e.Message);
    }

    [Fact]
    public void AGameDirThatClimbsOutIsRefused()
    {
        var e = Refused("""
            mods:
              test:
                game_dir: ../../elsewhere
            """);
        Assert.Contains("game_dir '../../elsewhere' leaves", e.Message);
    }

    [Fact]
    public void AnAbsoluteGameDirIsRefused()
    {
        var e = Refused("""
            mods:
              test:
                game_dir: /abs/test
            """);
        Assert.Contains("game_dir must be relative", e.Message);
    }

    [Fact]
    public void AManagedDirThatClimbsOutOfTheGameDirIsRefused()
    {
        var e = Refused("""
            mods:
              test:
                game_dir: mods/test
                managed_dir: ../../../x
            """);
        Assert.Contains("managed_dir '../../../x' leaves", e.Message);
    }

    [Fact]
    public void AnEngineExeWithAPathIsRefused()
    {
        var e = Refused("""
            mods:
              test:
                game_dir: mods/test
                engine:
                  client:
                    exe: bin/hl2_linux64
            """);
        Assert.Contains("engine.client.exe is a file name", e.Message);
    }

    [Fact]
    public void AFileWithNoModsIsRefused()
    {
        Assert.Contains("no mods", Refused("default_mod: test\n").Message);
    }

    [Fact]
    public void AnUnknownIdListsTheKnownOnes()
    {
        var e = Assert.Throws<ModsConfigException>(() => ModsConfig.Parse(Minimal, Path).Select("other"));
        Assert.Equal("unknown mod 'other' (known: test)", e.Message);
    }

    [Fact]
    public void NoDefaultAndNoIdIsAnErrorNotAGuess()
    {
        var config = ModsConfig.Parse("""
            mods:
              a:
                game_dir: mods/a
              b:
                game_dir: mods/b
            """, Path);
        Assert.Contains("-mod <id> is required", Assert.Throws<ModsConfigException>(() => config.Select(null)).Message);
    }

    [Fact]
    public void TheRepositorysModsYamlLoads()
    {
        // The real file, as setup links it into every game directory.
        var config = ModsConfig.Load(RepoLayout.Repo("game", "mods.yaml"));
        var mod = config.Select(null);
        Assert.Equal("test", mod.Id);
        Assert.Equal(["SourceSharp.Server"], mod.ServerAssemblies);
        Assert.Equal(["SourceSharp.Client"], mod.ClientAssemblies);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("tf2")]
    [InlineData("tf2_descent")]
    public void EveryRepositoryModsClientSkipsTheIntroVideo(string id)
    {
        // A player's launch is the client role's args, not the Makefile's
        // RUN_ARGS, so -novid has to be in mods.yaml for a plain `source#` run
        // to skip the intro video.
        var mod = ModsConfig.Load(RepoLayout.Repo("game", "mods.yaml")).Select(id);
        Assert.Contains("-novid", mod.Client.Args);
    }

    //-------------------------------------------------------------------------
    // Defaults and overrides (plan_host_sdk §2.5)
    //-------------------------------------------------------------------------

    private const string WithDefaults = """
        default_mod: a
        defaults:
          title: "{id} game"
          game_dir: mods/{id}
          content: game/mod_{id}
          assemblies:
            server: [Core.Server]
            client: [Core.Client]
          engine:
            client:
              fd_limit: 1000
              args: [-shared]
              env: {A: "1", B: "2"}
            dedicated:
              appid: 244310
              exe: srcds_linux64
              exe_appid: 243750
              args: [-console]
        mods:
          a: {}
          b:
            title: Bee
            assemblies:
              server: [Core.Server, B.Server]
            engine:
              client:
                args: [-b]
                env: {B: "two", C: "3"}
        """;

    [Fact]
    public void A_mod_takes_every_setting_it_does_not_give_from_defaults()
    {
        var a = ModsConfig.Parse(WithDefaults, Path).Select("a");

        Assert.Equal("a game", a.Title);
        Assert.Equal("/games/sharp/bin/debug/mods/a", a.GameDir);
        Assert.Equal("game/mod_a", a.Content);
        Assert.Equal(["Core.Server"], a.ServerAssemblies);
        Assert.Equal(1000, a.Client.FdLimit);
        Assert.Equal(["-shared"], a.Client.Args);
    }

    [Fact]
    public void Lists_replace_scalars_override_and_maps_merge_key_by_key()
    {
        var b = ModsConfig.Parse(WithDefaults, Path).Select("b");

        Assert.Equal("Bee", b.Title);
        Assert.Equal(["Core.Server", "B.Server"], b.ServerAssemblies);   // the whole list, restated
        Assert.Equal(["Core.Client"], b.ClientAssemblies);              // the sibling key still inherited
        Assert.Equal(["-b"], b.Client.Args);                            // replaced, not appended
        Assert.Equal(new Dictionary<string, string> { ["A"] = "1", ["B"] = "two", ["C"] = "3" }, b.Client.Env);
    }

    [Fact]
    public void The_dedicated_role_is_its_own_and_names_where_its_exe_lives()
    {
        var a = ModsConfig.Parse(WithDefaults, Path).Select("a");

        var d = a.Engine(dedicated: true);
        Assert.Equal((244310u, "srcds_linux64", 243750u), (d.AppId, d.Exe, d.ExeAppId));
        Assert.Equal(["-console"], d.Args);
        Assert.Same(a.Client, a.Engine(dedicated: false));
    }

    [Fact]
    public void A_mod_without_a_dedicated_role_says_so_when_one_is_asked_for()
    {
        var e = Assert.Throws<ModsConfigException>(() => ModsConfig.Parse(Minimal, Path).Select(null).Engine(dedicated: true));
        Assert.Contains("no engine.dedicated", e.Message);
    }

    [Fact]
    public void An_unknown_key_in_defaults_is_refused()
    {
        var e = Refused("""
            defaults:
              gamedir: mods/x
            mods:
              a:
                game_dir: mods/a
            """);
        Assert.Contains("gamedir", e.Message);
    }

    [Fact]
    public void Game_dir_from_neither_the_mod_nor_defaults_is_refused_naming_both()
    {
        var e = Refused("""
            defaults:
              title: t
            mods:
              a: {}
            """);
        Assert.Contains("mod 'a' has no game_dir (neither the mod nor defaults", e.Message);
    }

    [Fact]
    public void The_test_mod_resolves_exactly_as_it_did_before_defaults()
    {
        // The values game/mods.yaml gave the test mod at 42d6d80, when every
        // setting was written in the mod's own entry: moving them into
        // defaults must change none.
        var test = ModsConfig.Load(RepoLayout.Repo("game", "mods.yaml")).Select("test");
        string dir = System.IO.Path.GetDirectoryName(RepoLayout.Repo("game", "mods.yaml"))!;

        Assert.Equal("SourceSharp test mod", test.Title);
        Assert.Equal(System.IO.Path.Combine(dir, "mods/test"), test.GameDir);
        Assert.Equal("game/mod_test", test.Content);
        Assert.Equal(243750u, test.SteamAppId);
        Assert.Equal((243750u, "hl2_linux64", 42, 2048), (test.Client.AppId, test.Client.Exe, test.Client.RestartExitCode, test.Client.FdLimit));
        // The one deliberate change since 42d6d80: -novid, so a player's plain
        // launch skips the intro video (EveryRepositoryModsClientSkipsTheIntroVideo).
        Assert.Equal(["-novid"], test.Client.Args);
        Assert.Empty(test.Client.Env);
        Assert.Equal(["SourceSharp.Server"], test.ServerAssemblies);
        Assert.Equal(["SourceSharp.Client"], test.ClientAssemblies);
        Assert.Equal(System.IO.Path.Combine(dir, "mods/test/bin/managed"), test.ManagedDir);
    }

    [Fact]
    public void The_tf2_descent_mod_loads_its_own_assemblies_and_the_default_is_still_test()
    {
        var config = ModsConfig.Load(RepoLayout.Repo("game", "mods.yaml"));
        var tf2_descent = config.Select("tf2_descent");

        Assert.Equal("test", config.DefaultId);
        // TF2Descent builds on the TF2 mod: TF2's assemblies load after the
        // core's and before TF2Descent's own.
        Assert.Equal(["SourceSharp.Server", "TF2.Server", "TF2.Descent.Server"], tf2_descent.ServerAssemblies);
        Assert.Equal(["SourceSharp.Client", "TF2.Client", "TF2.Descent.Client"], tf2_descent.ClientAssemblies);
        Assert.Equal("srcds_linux64", tf2_descent.Engine(dedicated: true).Exe);
        Assert.Equal("game/mod_tf2_descent", tf2_descent.Content);
    }

    [Fact]
    public void The_tf2_mod_loads_TF2s_assemblies_from_its_own_content()
    {
        var tf2 = ModsConfig.Load(RepoLayout.Repo("game", "mods.yaml")).Select("tf2");

        Assert.Equal("TF2", tf2.Title);
        Assert.Equal("game/mod_tf2", tf2.Content);
        Assert.Equal(["SourceSharp.Server", "TF2.Server"], tf2.ServerAssemblies);
        Assert.Equal(["SourceSharp.Client", "TF2.Client"], tf2.ClientAssemblies);
        Assert.True(File.Exists(RepoLayout.Repo("game", "mod_tf2", "gameinfo.txt")));
    }

    //-------------------------------------------------------------------------
    // The pod's resolved file (source# -resolve), shape agreed with the host
    //-------------------------------------------------------------------------

    [Fact]
    public void Resolve_writes_the_agreed_flat_shape_byte_for_byte()
    {
        var d = ModsConfig.Parse(WithDefaults, Path).Select("a").Engine(dedicated: true);

        Assert.Equal(
            "# generated by source# -resolve; do not edit\n" +
            "fd_limit: 2048\n" +
            "restart_exit_code: 42\n" +
            "args:\n" +
            "  - -console\n" +
            "env: {}\n",
            Launcher.Launcher.Resolved(d));
    }

    [Fact]
    public void Resolve_refuses_a_value_the_pods_reader_would_misparse()
    {
        var role = new EngineSettings(1, "x", 1, 42, 10, ["a: b"], new Dictionary<string, string>());

        Assert.Throws<ModsConfigException>(() => Launcher.Launcher.Resolved(role));
    }

    //-------------------------------------------------------------------------
    // The browser block (host plan_steam_auth S-18)
    //-------------------------------------------------------------------------

    private const string WithBrowser = """
        defaults:
          game_dir: mods/{id}
          browser:
            name: "Default"
            region: "eu"
            rules:
              base: "{players}"
          engine:
            dedicated:
              args: [-console]
        mods:
          hosted:
            browser:
              name: "Descent ({levels_open} open)"
              description: "TF2 Descent"
              tags: "rpg,coop"
              map: "depth {deepest}"
              rules:
                players_by_level: "{players_by_level}"
          plain:
            game_dir: mods/plain
        """;

    [Fact]
    public void A_browser_block_merges_field_by_field_and_its_rules_key_by_key()
    {
        var b = ModsConfig.Parse(WithBrowser, Path).Select("hosted").Browser!;

        Assert.Equal("Descent ({levels_open} open)", b.Name);
        Assert.Equal("eu", b.Region);
        Assert.Equal(0, b.MaxPlayers);
        Assert.Equal("depth {deepest}", b.Map);
        Assert.Equal(new Dictionary<string, string> { ["base"] = "{players}", ["players_by_level"] = "{players_by_level}" }, b.Rules);
    }

    [Theory]
    [InlineData("name: \"{levels}\"", "{levels}")]
    [InlineData("map: \"depth {id}\"", "{id}")]
    [InlineData("rules: {x: \"{player}\"}", "{player}")]
    public void A_browser_template_naming_a_key_the_host_does_not_publish_is_refused(string line, string key)
    {
        string yaml = $$"""
            mods:
              m:
                game_dir: mods/m
                browser:
                  name: "n"
                  {{line}}
            """;

        var e = Refused(yaml);

        Assert.Contains(key, e.Message);
        Assert.Contains("{players_by_level}", e.Message);
    }

    [Fact]
    public void A_browser_block_with_every_published_key_loads()
    {
        string all = string.Join(" ", ModsConfig.BrowserKeys.Select(k => "{" + k + "}"));
        string yaml = $$"""
            mods:
              m:
                game_dir: mods/m
                browser:
                  name: "{{all}}"
            """;

        Assert.Equal(all, ModsConfig.Parse(yaml, Path).Select("m").Browser!.Name);
        Assert.Equal(7, ModsConfig.BrowserKeys.Count);
    }

    [Fact]
    public void A_browser_block_without_a_name_is_refused() =>
        Assert.Contains("browser has no name", Refused("""
            mods:
              m:
                game_dir: mods/m
                browser: {tags: "x"}
            """).Message);

    [Fact]
    public void Resolve_ends_a_dedicated_role_with_the_browser_block_byte_for_byte()
    {
        var mod = ModsConfig.Parse(WithBrowser, Path).Select("hosted");

        Assert.Equal(
            "# generated by source# -resolve; do not edit\n" +
            "fd_limit: 2048\n" +
            "restart_exit_code: 42\n" +
            "args:\n" +
            "  - -console\n" +
            "env: {}\n" +
            "browser:\n" +
            "  name: \"Descent ({levels_open} open)\"\n" +
            "  description: \"TF2 Descent\"\n" +
            "  tags: \"rpg,coop\"\n" +
            "  region: \"eu\"\n" +
            "  max_players: 0\n" +
            "  map: \"depth {deepest}\"\n" +
            "  rules:\n" +
            "    base: \"{players}\"\n" +
            "    players_by_level: \"{players_by_level}\"\n",
            Launcher.Launcher.Resolved(mod.Engine(dedicated: true), mod.Browser));
    }

    [Fact]
    public void The_resolved_browser_block_reads_back_as_the_same_values()
    {
        // Quotes and backslashes in a template survive a YAML reader.
        var b = new BrowserSettings("a \"q\" \\ {players}", "", "", "", 3, "", new Dictionary<string, string>());
        string text = Launcher.Launcher.Resolved(new EngineSettings(1, "x", 1, 42, 10, [], new Dictionary<string, string>()), b);

        var yaml = new YamlDotNet.RepresentationModel.YamlStream();
        yaml.Load(new StringReader(text));
        var browser = (YamlDotNet.RepresentationModel.YamlMappingNode)((YamlDotNet.RepresentationModel.YamlMappingNode)yaml.Documents[0].RootNode)["browser"];

        Assert.Equal(b.Name, browser["name"].ToString());
        Assert.Equal("3", browser["max_players"].ToString());
        Assert.Contains("  rules: {}\n", text);
    }

    [Fact]
    public void The_hosted_tf2_descent_mod_has_a_browser_block_and_the_test_mod_none()
    {
        var config = ModsConfig.Load(RepoLayout.Repo("game", "mods.yaml"));

        var browser = config.Select("tf2_descent").Browser;
        Assert.NotNull(browser);
        Assert.Contains("{levels_open}", browser.Name);
        Assert.Null(config.Select("test").Browser);
    }

    private static string[] Strings(System.Text.Json.JsonElement list) =>
        list.EnumerateArray().Select(v => v.GetString()!).ToArray();

    //-------------------------------------------------------------------------
    // tools/setup's port of the merge
    //-------------------------------------------------------------------------

    [Fact]
    public void Tools_setup_resolves_every_mod_as_this_loader_does()
    {
        var dir = Directory.CreateTempSubdirectory("mods-parity-").FullName;
        try
        {
            string yaml = System.IO.Path.Combine(dir, "mods.yaml");
            File.WriteAllText(yaml, WithDefaults);

            var psi = new System.Diagnostics.ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add(RepoLayout.Repo("tools", "setup"));
            psi.ArgumentList.Add("--mods-resolved");
            psi.ArgumentList.Add(yaml);
            using var p = System.Diagnostics.Process.Start(psi)!;
            string json = p.StandardOutput.ReadToEnd();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, err);

            var python = System.Text.Json.JsonDocument.Parse(json).RootElement;
            var csharp = ModsConfig.Load(yaml);
            Assert.Equal(csharp.Mods.Keys.Order(), python.EnumerateObject().Select(o => o.Name).Order());
            foreach (var (id, mod) in csharp.Mods)
            {
                var py = python.GetProperty(id);
                Assert.Equal(mod.Title, py.GetProperty("title").GetString());
                Assert.Equal(mod.Content, py.GetProperty("content").GetString());
                Assert.Equal(mod.GameDir, System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, py.GetProperty("game_dir").GetString()!)));

                // The nested half of the merge: lists replace, maps merge key by key.
                var asm = py.GetProperty("assemblies");
                Assert.Equal(mod.ServerAssemblies, Strings(asm.GetProperty("server")));
                Assert.Equal(mod.ClientAssemblies, Strings(asm.GetProperty("client")));
                foreach (var (role, settings) in new[] { ("client", mod.Client), ("dedicated", mod.Dedicated!) })
                {
                    var r = py.GetProperty("engine").GetProperty(role);
                    Assert.Equal(settings.Args, Strings(r.GetProperty("args")));
                    var env = r.TryGetProperty("env", out var e)
                        ? e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!)
                        : new Dictionary<string, string>();
                    Assert.Equal(settings.Env.OrderBy(p => p.Key), env.OrderBy(p => p.Key));
                }
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
