using System.Text;
using SourceSharp.Abi;
using SourceSharp.Bootstrap;
using SourceSharp.Shared.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The load facts the Bootstrap hands a side's entry (plan_side_entries SE2):
/// one writer (<see cref="LoadFacts"/>), one reader (<see cref="SideFacts"/>),
/// two blocks (Load's and Stamped's, the integrator's ruling A), and these
/// facts between them.
/// </summary>
public sealed class LoadFactsTests
{
    private static readonly DateTime Written = new(2026, 10, 2, 9, 30, 15, DateTimeKind.Utc);

    private static byte[] LoadBlock(string modId = "tf2") => LoadFacts.WriteLoad(
        "/game/bin/managed", modId, "/game/mods/tf2",
        ["SourceSharp.Server", "TF2.Server"], ["SourceSharp.Client", "TF2.Client"]);

    private static byte[] StampedBlock() => LoadFacts.WriteStamped(
        [new LoadedFile("SourceSharp.Server.dll", "ab12", Written),
         new LoadedFile("SourceSharp.Abi.dll", "cd34", Written.AddSeconds(1))]);

    private static ReadOnlySpan<byte> Text(byte[] block) => block.AsSpan(0, block.Length - 1);

    [Fact]
    public void The_hosted_mods_browser_block_reaches_IModInfo_unchanged()
    {
        // mods.yaml -> Bootstrap's fact -> the side's reader -> what IModInfo answers.
        var mod = SourceSharp.Mods.ModsConfig.Load(RepoLayout.Repo("game", "mods.yaml")).Select("tf2_descent");
        var b = mod.Browser!;
        byte[] block = LoadFacts.WriteLoad("/m", mod.Id, "/g", [], [], b);

        SideFacts facts = SideFacts.ParseLoad(Text(block));
        var data = new Dictionary<string, object?> { [ModInfo.Keys.Browser] = facts.BrowserJson };
        var read = new ModInfo(k => data.GetValueOrDefault(k)).Browser!;

        Assert.Equal((b.Name, b.Description, b.Tags, b.Region, b.MaxPlayers, b.Map),
                     (read.Name, read.Description, read.Tags, read.Region, read.MaxPlayers, read.Map));
        Assert.Equal(b.Rules, read.Rules);
        Assert.NotEmpty(read.Rules);
    }

    [Fact]
    public void A_mod_without_a_browser_block_has_none_on_the_side()
    {
        SideFacts facts = SideFacts.ParseLoad(Text(LoadBlock()));
        var data = new Dictionary<string, object?> { [ModInfo.Keys.Browser] = "" };

        Assert.Null(facts.BrowserJson);
        Assert.Null(new ModInfo(k => data.GetValueOrDefault(k)).Browser);
        Assert.Null(new ModInfo(_ => null).Browser);
    }

    [Fact]
    public void A_browser_block_with_tabs_and_newlines_fits_one_fact()
    {
        var b = new SourceSharp.Mods.BrowserSettings("a\tb", "line1\nline2", "", "", 0, "", new Dictionary<string, string> { ["k"] = "\"q\"" });

        var read = SourceSharp.Abi.ModBrowser.Parse(SideFacts.ParseLoad(Text(LoadFacts.WriteLoad("/m", "x", "/g", [], [], b))).BrowserJson!);

        Assert.Equal("a\tb", read.Name);
        Assert.Equal("line1\nline2", read.Description);
        Assert.Equal("\"q\"", read.Rules["k"]);
    }

    [Fact]
    public void A_browser_fact_that_is_not_the_bootstraps_json_is_refused_at_load()
    {
        byte[] good = LoadBlock();
        string text = Encoding.UTF8.GetString(Text(good)) + "browser\t{\"name\":1}\n";

        Assert.Throws<FormatException>(() => SideFacts.ParseLoad(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void EveryLoadFactTheBootstrapWritesTheSideReadsBack()
    {
        SideFacts facts = SideFacts.ParseLoad(Text(LoadBlock()));

        Assert.Equal("/game/bin/managed", facts.ManagedDir);
        Assert.Equal("tf2", facts.ModId);
        Assert.Equal("/game/mods/tf2", facts.GameDir);
        Assert.Equal(["SourceSharp.Server", "TF2.Server"], facts.ServerAssemblies);
        Assert.Equal(["SourceSharp.Client", "TF2.Client"], facts.ClientAssemblies);
    }

    [Fact]
    public void EveryLoadedRowTheBootstrapWritesTheSideReadsBack()
    {
        Assert.Equal(
            [new LoadedAssembly("SourceSharp.Server.dll", "ab12", Written),
             new LoadedAssembly("SourceSharp.Abi.dll", "cd34", Written.AddSeconds(1))],
            SideFacts.ParseLoaded(Text(StampedBlock())));
    }

    [Fact]
    public unsafe void EachBlockIsReadUpToItsNul()
    {
        byte[] load = LoadBlock("test");
        byte[] stamped = StampedBlock();
        Assert.Equal(0, load[^1]);
        Assert.Equal(0, stamped[^1]);

        fixed (byte* p = load)
            Assert.Equal("test", SideFacts.ReadLoad(p).ModId);
        fixed (byte* p = stamped)
            Assert.Equal(2, SideFacts.ReadLoaded(p).Count);
    }

    // ---- the mod's UI assemblies and language files (stage 0.9) ----

    [Fact]
    public void The_mods_ui_assemblies_and_language_files_reach_IModInfo_through_the_facts()
    {
        // mods.yaml's lists -> Bootstrap's WriteLoad -> the side's ParseLoad ->
        // ModInfo.Publish's AppContext data -> what IModInfo answers. Order and
        // spacing survive, which is the whole of what the UI loader reads.
        byte[] block = LoadFacts.WriteLoad("/m", "tf2", "/g", [], [], null,
                                           ["TF2.Client.Ui", "TF2.Descent.Client.Ui"],
                                           ["resource/tf_english.txt", "resource/tf_items_english.txt"]);

        SideFacts facts = SideFacts.ParseLoad(Text(block));
        Assert.Equal(["TF2.Client.Ui", "TF2.Descent.Client.Ui"], facts.ModClientUiAssemblies);

        var data = new Dictionary<string, object?>
        {
            [ModInfo.Keys.ClientUiAssemblies] = string.Join(';', facts.ModClientUiAssemblies),
            [ModInfo.Keys.Localisation] = string.Join(';', facts.ModLocalisation),
        };
        var info = new ModInfo(k => data.GetValueOrDefault(k));
        Assert.Equal(["TF2.Client.Ui", "TF2.Descent.Client.Ui"], info.UiAssemblies);
        Assert.Equal(["resource/tf_english.txt", "resource/tf_items_english.txt"], info.Localisation);
    }

    [Fact]
    public void A_mod_that_lists_neither_writes_empty_rows_and_the_side_reads_none()
    {
        // The empty answer is an empty list, not an absent key: Publish always
        // sets both, so a reload of another mod cannot inherit this one's.
        SideFacts facts = SideFacts.ParseLoad(Text(LoadBlock()));
        Assert.Empty(facts.ModClientUiAssemblies);
        Assert.Empty(facts.ModLocalisation);

        var data = new Dictionary<string, object?>
        {
            [ModInfo.Keys.ClientUiAssemblies] = "",
            [ModInfo.Keys.Localisation] = "",
        };
        var info = new ModInfo(k => data.GetValueOrDefault(k));
        Assert.Empty(info.UiAssemblies);
        Assert.Empty(info.Localisation);
    }

    [Fact]
    public void A_load_block_from_a_Bootstrap_without_the_rows_reads_as_none_not_an_error()
    {
        // The reader's rule is "a missing or unknown key is refused", and these
        // two are the exception the optionality is spelled by: a side loaded by
        // a Bootstrap that predates stage 0.9 has the rows absent, and the mod
        // simply has no UI assemblies.
        string without = Encoding.UTF8.GetString(Text(LoadBlock()))
            .Split('\n').Where(l => !l.StartsWith("client_ui_assemblies\t", StringComparison.Ordinal)
                                  && !l.StartsWith("localisation\t", StringComparison.Ordinal))
            .Aggregate((a, b) => a + "\n" + b);
        SideFacts facts = SideFacts.ParseLoad(Encoding.UTF8.GetBytes(without));
        Assert.Equal("tf2", facts.ModId);            // the block still parsed
        Assert.Empty(facts.ModClientUiAssemblies);
        Assert.Empty(facts.ModLocalisation);
    }

    [Fact]
    public void An_unknown_fact_row_is_still_refused()
    {
        // The negative control on the arm above: only the two new keys joined
        // the accepted set.
        string text = Encoding.UTF8.GetString(Text(LoadBlock())) + "ui_assemblies\tTF2.Client.Ui\n";
        Assert.Throws<FormatException>(() => SideFacts.ParseLoad(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void TheBootstrapsHashIsTheStampsHash()
    {
        byte[] content = Encoding.UTF8.GetBytes("SourceSharp.Abi.dll, as loaded");

        string hash = LoadFacts.HashOf(content);
        Assert.Equal(64, hash.Length);
        Assert.Equal(BuildStampBuilder.HashOf(content), hash);
    }

    [Fact]
    public void AValueHoldingATabIsRefusedByTheWriter()
    {
        Assert.Throws<ArgumentException>(() => LoadFacts.WriteLoad("/a\tb", "test", "/g", [], []));
    }

    [Fact]
    public void AMissingFactIsRefusedByTheReader()
    {
        string text = Encoding.UTF8.GetString(Text(LoadBlock()));
        Assert.Equal("tf2", SideFacts.ParseLoad(Encoding.UTF8.GetBytes(text)).ModId);   // the control

        string without = string.Join('\n', text.Split('\n').Where(l => !l.StartsWith("game_dir\t", StringComparison.Ordinal)));
        Assert.NotEqual(text, without);
        var e = Assert.Throws<FormatException>(() => SideFacts.ParseLoad(Encoding.UTF8.GetBytes(without)));
        Assert.Contains("game_dir", e.Message);
    }

    [Fact]
    public void LoadsBlockRefusesALoadedRow()
    {
        // Load runs before the context has finished reading: a loaded row there
        // would be a stamp of the assemblies read so far, not of the build.
        string load = Encoding.UTF8.GetString(Text(LoadBlock()));
        string stamped = Encoding.UTF8.GetString(Text(StampedBlock()));

        var e = Assert.Throws<FormatException>(() => SideFacts.ParseLoad(Encoding.UTF8.GetBytes(load + stamped)));
        Assert.Contains("loaded", e.Message);
    }

    [Fact]
    public void StampedsBlockRefusesAnythingButLoadedRows()
    {
        string load = Encoding.UTF8.GetString(Text(LoadBlock()));
        string stamped = Encoding.UTF8.GetString(Text(StampedBlock()));
        Assert.Equal(2, SideFacts.ParseLoaded(Encoding.UTF8.GetBytes(stamped)).Count);   // the control

        var e = Assert.Throws<FormatException>(() => SideFacts.ParseLoaded(Encoding.UTF8.GetBytes(stamped + load)));
        Assert.Contains("managed_dir", e.Message);
    }
}
