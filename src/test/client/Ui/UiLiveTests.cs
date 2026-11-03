using System.Text.Json;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

public readonly record struct MockRow(string Name, int Score, bool Ready);

public enum MockMode { Lobby, Playing, Over }

/// <summary>A data interface with every shape the mock-ups read: scalars, an index-and-count pair, an out, a list, an enum.</summary>
public interface IMockSource
{
    string Title { get; }

    int RowCount { get; }

    MockRow Row(int i);

    bool TryRow(int i, out MockRow row);

    IReadOnlyList<string> Tags { get; }

    MockMode Mode { get; }

    float Remaining { get; }

    void Join();
}

/// <summary>An interface with a ref struct parameter, which a dispatch proxy cannot carry.</summary>
public interface IMockPage
{
    string Name { get; }

    void Describe(ref UiFrame ui);
}

public sealed class FakeMockSource : IMockSource
{
    private readonly MockRow[] _rows = [new("Alyx", 10, true), new("Barney", 4, false)];
    public int Joined;

    public string Title => "Fake title";
    public int RowCount => _rows.Length;
    public MockRow Row(int i) => _rows[i];

    public bool TryRow(int i, out MockRow row)
    {
        row = _rows[i];
        return true;
    }

    public IReadOnlyList<string> Tags => ["red", "blue"];
    public MockMode Mode => MockMode.Playing;
    public float Remaining => float.PositiveInfinity;
    public void Join() => Joined++;
}

/// <summary>
/// The live mock-ups (UiMockSources, UiLive): what a screen reads is listed,
/// edits are laid over the fakes, and every segment of the UI assemblies can
/// be drawn on request.
/// </summary>
public class UiLiveTests
{
    private static readonly Lazy<UiLive> Live = new(() => LiveRun.Create(
        new PreviewOptions(RepoLayout.RepoRoot, Path.GetTempPath(), false, true, null)));

    private static IMockSource Mock(UiMockSources mocks, IMockSource? fake = null)
        => (IMockSource)mocks.Wrap(typeof(IMockSource), fake ?? new FakeMockSource());

    private static UiMockSources Edits(string json)
        => new(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!);

    private static UiMockLeaf Leaf(UiMockSources mocks, string key)
        => mocks.Leaves.SingleOrDefault(l => l.Key == key) ?? throw new Xunit.Sdk.XunitException(
            $"no leaf {key}; read: {string.Join(", ", mocks.Leaves.Select(l => l.Key))}");

    [Fact]
    public void A_read_answers_the_fake_and_lists_the_value_with_its_type()
    {
        var mocks = Edits("{}");
        Assert.Equal("Fake title", Mock(mocks).Title);
        var leaf = Leaf(mocks, "IMockSource.Title");
        Assert.Equal(("string", "Fake title", false, "IMockSource"), (leaf.Type, (string?)leaf.Value, leaf.Changed, leaf.Source));
    }

    [Fact]
    public void An_edit_replaces_the_fake_s_answer_and_is_marked_changed()
    {
        var mocks = Edits("""{"IMockSource.Title":"Edited"}""");
        Assert.Equal("Edited", Mock(mocks).Title);
        Assert.True(Leaf(mocks, "IMockSource.Title").Changed);
    }

    [Fact]
    public void An_edit_to_one_field_of_a_record_keeps_its_other_fields()
    {
        var mocks = Edits("""{"IMockSource.Row(1).Score":99}""");
        Assert.Equal(new MockRow("Barney", 99, false), Mock(mocks).Row(1));
        Assert.Equal("int", Leaf(mocks, "IMockSource.Row(1).Score").Type);
        Assert.False(Leaf(mocks, "IMockSource.Row(1).Name").Changed);
    }

    [Fact]
    public void An_index_past_the_fake_s_list_answers_the_member_s_last_good_answer()
    {
        var mocks = Edits("""{"IMockSource.RowCount":3}""");
        var source = Mock(mocks);
        Assert.Equal(3, source.RowCount);
        source.Row(0);
        source.Row(1);
        Assert.Equal(new MockRow("Barney", 4, false), source.Row(2));
        Assert.Equal("Barney", Leaf(mocks, "IMockSource.Row(2).Name").Value);
    }

    [Fact]
    public void An_out_parameter_s_fields_are_leaves_and_take_edits()
    {
        var mocks = Edits("""{"IMockSource.TryRow(0).row.Ready":false}""");
        Assert.True(Mock(mocks).TryRow(0, out var row));
        Assert.Equal(new MockRow("Alyx", 10, false), row);
        Assert.True(Leaf(mocks, "IMockSource.TryRow(0).row.Ready").Changed);
    }

    [Fact]
    public void A_list_s_count_and_items_are_leaves_and_a_raised_count_repeats_the_last_item()
    {
        var mocks = Edits("""{"IMockSource.Tags.Count":3,"IMockSource.Tags[0]":"green"}""");
        Assert.Equal(["green", "blue", "blue"], Mock(mocks).Tags);
        Assert.Equal(3, Leaf(mocks, "IMockSource.Tags.Count").Value);
    }

    [Fact]
    public void An_enum_leaf_names_its_values_and_takes_an_edit_by_name()
    {
        var mocks = Edits("""{"IMockSource.Mode":"Over"}""");
        Assert.Equal(MockMode.Over, Mock(mocks).Mode);
        var leaf = Leaf(mocks, "IMockSource.Mode");
        Assert.Equal(("enum", "Over"), (leaf.Type, (string?)leaf.Value));
        Assert.Equal(["Lobby", "Playing", "Over"], leaf.Options!);
    }

    [Fact]
    public void An_edit_that_does_not_convert_keeps_the_fake_s_answer()
    {
        var mocks = Edits("""{"IMockSource.RowCount":"many"}""");
        Assert.Equal(2, Mock(mocks).RowCount);
        Assert.False(Leaf(mocks, "IMockSource.RowCount").Changed);
    }

    [Fact]
    public void An_integer_edit_out_of_range_is_clamped_to_the_type()
    {
        Assert.True(UiMockSources.TryConvert(JsonDocument.Parse("1e12").RootElement, typeof(int), out var v));
        Assert.Equal(int.MaxValue, v);
    }

    [Fact]
    public void A_float_JSON_cannot_hold_is_listed_as_its_text()
    {
        var mocks = Edits("{}");
        Mock(mocks).Remaining.ToString();
        Assert.Equal("Infinity", Leaf(mocks, "IMockSource.Remaining").Value);
    }

    [Fact]
    public void An_action_reaches_the_fake_and_is_no_leaf()
    {
        var fake = new FakeMockSource();
        var mocks = Edits("{}");
        Mock(mocks, fake).Join();
        Assert.Equal(1, fake.Joined);
        Assert.Empty(mocks.Leaves);
    }

    [Fact]
    public void An_interface_with_a_ref_struct_parameter_is_refused_rather_than_proxied()
    {
        var mocks = new UiMockSources(new Dictionary<string, JsonElement>(), fakes: false);
        Assert.Throws<InvalidOperationException>(() => mocks.Wrap(typeof(IMockPage), null));
    }

    [Fact]
    public void Without_fakes_every_read_is_made_up()
    {
        var mocks = new UiMockSources(new Dictionary<string, JsonElement>(), fakes: false);
        var source = Mock(mocks);
        Assert.Equal(("Title", 3, true), (source.Title, source.RowCount, source.TryRow(0, out var row)));
        Assert.Equal("Name", row.Name);
        Assert.Equal(3, source.Tags.Count);
    }

    //-------------------------------------------------------------------------
    // UiLive over the real UI assemblies.

    private static JsonElement Ask(string request) => JsonDocument.Parse(Live.Value.Answer(request)).RootElement;

    private static JsonElement Render(string segment, string preset, string values = "{}", int w = 1280, int h = 720)
        => Ask($$"""{"op":"render","segment":"{{segment}}","preset":"{{preset}}","w":{{w}},"h":{{h}},"values":{{values}}}""");

    [Fact]
    public void Every_screen_and_panel_type_in_the_UI_assemblies_is_a_segment()
    {
        var types = PreviewRun.ScreenAssemblies().SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.ContainsGenericParameters && !IsFrame(t) && (typeof(UiScreen).IsAssignableFrom(t) || IsPanel(t)))
            .ToList();
        var listed = Live.Value.Segments.Select(s => s.Type).Where(t => t is not null).ToHashSet();
        Assert.True(types.Count > 50, $"{types.Count} types: the scan saw nothing");
        var missing = types.Where(t => !listed.Contains(t)).Select(t => t.FullName).ToList();
        Assert.True(missing.Count == 0, "not segments: " + string.Join(", ", missing));
    }

    private static bool IsFrame(Type t)
        => t == typeof(UiPanelPreview) || t == typeof(UiScreenGroup) || t == typeof(UiPartPreview)
           || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(UiPanelPreview<>));

    private static bool IsPanel(Type t)
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
            if (b == typeof(UiPanel) || (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(UiPanel<>)))
                return true;
        return false;
    }

    [Fact]
    public void The_list_names_each_segment_s_presets_sizes_and_fonts()
    {
        var list = Ask("""{"op":"list"}""");
        var board = list.GetProperty("segments").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "screen:scoreboard");
        var presets = board.GetProperty("presets").EnumerateArray().Select(p => p.GetProperty("slug").GetString()).ToList();
        Assert.Equal("Scoreboard", board.GetProperty("name").GetString());
        Assert.Equal("12-players-two-teams", presets[0]);
        Assert.Equal(UiSegment.Auto, presets[^1]);
        Assert.Contains(list.GetProperty("sizes").EnumerateArray(), s => s.GetProperty("name").GetString() == "1920x1080");
        Assert.True(list.GetProperty("fonts").GetArrayLength() > 0);
    }

    [Fact]
    public void A_render_draws_the_edit_and_lists_it_changed()
    {
        var r = Render("screen:scoreboard", "12-players-two-teams", """{"IScoreboardSource.ServerName":"Mock Edit Server"}""");
        Assert.Contains("Mock Edit Server", r.GetProperty("svg").GetString());
        var leaf = r.GetProperty("values").EnumerateArray().Single(v => v.GetProperty("key").GetString() == "IScoreboardSource.ServerName");
        Assert.True(leaf.GetProperty("changed").GetBoolean());
        Assert.True(r.GetProperty("elements").GetArrayLength() > 10);
    }

    [Fact]
    public void A_render_lays_out_at_the_asked_size()
    {
        var root = Render("screen:scoreboard", "12-players-two-teams", w: 1024, h: 768).GetProperty("elements")[0];
        Assert.Equal((1024f, 768f), (root.GetProperty("w").GetSingle(), root.GetProperty("h").GetSingle()));
    }

    [Fact]
    public void A_panel_s_props_are_leaves_that_take_edits()
    {
        var r = Render("panel:SourceSharp.Client.Ui.Toolkit.HudMeter", UiSegment.Auto);
        Assert.False(r.TryGetProperty("error", out var e), e.ToString());
        Assert.Contains(r.GetProperty("values").EnumerateArray(), v => v.GetProperty("source").GetString() == "props");
    }

    [Fact]
    public void An_unknown_segment_or_op_answers_an_error()
    {
        Assert.Contains("no segment", Render("screen:nothing", "x").GetProperty("error").GetString());
        Assert.Contains("unknown op", Ask("""{"op":"dance"}""").GetProperty("error").GetString());
    }

    /// <summary>The tab lists every segment, so each must draw with its default data, a sample or made up.</summary>
    [Fact]
    public void Every_segment_draws_with_its_default_preset()
    {
        var failed = new List<string>();
        foreach (var s in Live.Value.Segments)
        {
            var r = Render(s.Id, s.Presets.First());
            if (r.TryGetProperty("error", out var e))
                failed.Add($"{s.Id}: {e.GetString()}");
        }
        Assert.True(Live.Value.Segments.Count > 100, $"{Live.Value.Segments.Count} segments");
        Assert.True(failed.Count == 0, string.Join("\n", failed));
    }
}
