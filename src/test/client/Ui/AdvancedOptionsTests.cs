using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>The advanced options: stock's keyboard Advanced dialog, a tab of the options window.</summary>
public class AdvancedOptionsTests
{
    private readonly MemoryConVarStore _convars = new MemoryConVarStore().With("con_enable", "0").With("hud_fastswitch", "2");
    private readonly UiContext _ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), new UiFontLibrary(), UiSamplesTests.Language());
    private readonly FakeAdminKey _key = new();
    private readonly AdvancedOptions _form;

    public AdvancedOptionsTests()
    {
        _form = new AdvancedOptions(new UiWindows(new MemoryWindowStore()), _convars, _key);
        _ctx.RecordNames = true;
        UiTestKit.Frame(_ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => _form.Describe(ref ui), 1.0);
    }

    private sealed class FakeAdminKey : IAdminKeySource
    {
        public bool HasKey { get; set; }

        public string PublicKey => HasKey ? "PUBLICKEY" : "";

        public string PublicKeyFile => HasKey ? "/key.pem.pub" : "";

        public string? Problem { get; set; }

        public void Generate() => HasKey = true;

        public void Set(string pathOrKey) => HasKey = true;
    }

    private List<string> Names()
    {
        UiTestKit.Frame(_ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => _form.Describe(ref ui), 1.0);
        return UiTestKit.Elements(_ctx).Select(e => UiTestKit.NameOf(_ctx, e)).ToList();
    }

    [Fact]
    public void With_no_admin_key_it_offers_to_generate_one()
    {
        var names = Names();
        Assert.Contains("adminkeygen", names);
        Assert.Contains("adminkeytake", names);
    }

    [Fact]
    public void With_an_admin_key_it_shows_the_public_half_and_no_generate()
    {
        _key.HasKey = true;
        var names = Names();
        Assert.Contains(names, n => n.Contains("PUBLICKEY", StringComparison.Ordinal));
        Assert.DoesNotContain("adminkeygen", names);
        Assert.Contains("adminkeytake", names);
    }

    [Fact]
    public void A_key_that_would_not_read_says_so()
    {
        _key.Problem = "#SS_UI_Admin_KeyBad";
        Assert.Contains(Names(), n => n.Contains("adminkeyproblem", StringComparison.Ordinal));
    }

    [Fact]
    public void It_loads_each_setting_as_on_for_any_non_zero_value()
        => Assert.Equal((false, true), (_form.Working.DeveloperConsole, _form.Working.FastSwitch));

    [Fact]
    public void Enabling_the_developer_console_writes_con_enable_on_apply_and_nothing_else()
    {
        _form.Working.DeveloperConsole = true;
        Assert.Empty(_convars.Writes);
        Assert.True(_form.Apply());
        Assert.Equal([("con_enable", "1")], _convars.Writes);
    }

    [Fact]
    public void Its_rows_say_what_they_set()
    {
        var names = UiTestKit.Elements(_ctx).Select(e => UiTestKit.NameOf(_ctx, e)).ToList();
        Assert.Contains(names, n => n.Contains("Developer console (~)", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("Fast weapon switch", StringComparison.Ordinal));
    }

    [Fact]
    public void The_options_window_has_an_advanced_tab()
    {
        var options = OptionsSamples.Build(new UiSampleServices());
        Assert.Contains(options.Aliases, a => a == "advancedoptions");
    }
}
