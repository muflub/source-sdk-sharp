using System.Text.Json;

using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// /events' server-side filters, /events/head, and the eventsFrom every write
/// reply carries (debug-api lane, item 2).
/// </summary>
public class DebugApiEventFilterTests
{
    private static DebugApiServer Seeded()
    {
        var server = NeverPumped.Server();
        var log = server.EventsForTest;
        log.Add(EntityEventKind.Spawned, "prop_physics", "", "appeared", 1f, 40, 0x10028u);   // seq 0
        log.Add(EntityEventKind.Output, "trigger_multiple", "OnStartTouch", "fired", 2f, 41, 0x20029u); // seq 1
        log.Add(EntityEventKind.Output, "trigger_multiple", "OnEndTouch", "fired", 3f, 41, 0x20029u);   // seq 2
        log.Add(EntityEventKind.Removed, "prop_physics", "", "gone", 4f, -1, 0x10028u);     // seq 3
        log.Add(EntityEventKind.Output, "func_door", "OnOpen", "fired", 5f, 50, 0x30032u);  // seq 4
        return server;
    }

    private static long[] Seqs(DebugApiServer server, string path)
    {
        using var doc = JsonDocument.Parse(DebugApiGolden.Get(server, path).Body);
        return doc.RootElement.GetProperty("events").EnumerateArray()
                  .Select(e => e.GetProperty("seq").GetInt64()).ToArray();
    }

    private static long Field(DebugApiServer server, string path, string name)
    {
        using var doc = JsonDocument.Parse(DebugApiGolden.Get(server, path).Body);
        return doc.RootElement.GetProperty(name).GetInt64();
    }

    [Fact]
    public void A_kind_filter_answers_only_that_kind()
    {
        using var server = Seeded();
        Assert.Equal([1, 2, 4], Seqs(server, "/events?kind=output"));
    }

    [Fact]
    public void Several_kinds_are_comma_separated()
    {
        using var server = Seeded();
        Assert.Equal([0, 3], Seqs(server, "/events?kind=spawned,removed"));
    }

    [Fact]
    public void A_name_filter_is_a_case_insensitive_glob()
    {
        using var server = Seeded();
        Assert.Equal([1], Seqs(server, "/events?name=onstart*"));
    }

    [Fact]
    public void A_classname_filter_is_a_glob()
    {
        using var server = Seeded();
        Assert.Equal([1, 2], Seqs(server, "/events?classname=trigger_*"));
    }

    [Fact]
    public void A_handle_filter_answers_only_that_entity()
    {
        using var server = Seeded();
        Assert.Equal([0, 3], Seqs(server, $"/events?handle={0x10028u}"));
    }

    [Fact]
    public void An_index_filter_answers_only_that_index()
    {
        using var server = Seeded();
        Assert.Equal([4], Seqs(server, "/events?index=50"));
    }

    [Fact]
    public void Since_and_a_filter_combine()
    {
        using var server = Seeded();
        Assert.Equal([2, 4], Seqs(server, "/events?kind=output&since=2"));
    }

    [Fact]
    public void An_unknown_kind_is_an_error_and_not_an_empty_answer()
    {
        using var server = Seeded();
        Assert.Contains("\"error\":\"no such event kind 'outptu'",
                        DebugApiGolden.Get(server, "/events?kind=outptu").Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_read_cut_short_by_max_resumes_at_the_first_match_it_did_not_answer()
    {
        using var server = Seeded();
        Assert.Equal(2, Field(server, "/events?kind=output&max=1", "next"));
    }

    [Fact]
    public void A_filtered_read_that_saw_everything_resumes_at_the_head()
    {
        using var server = Seeded();
        Assert.Equal(5, Field(server, "/events?kind=removed", "next"));
    }

    [Fact]
    public void Head_answers_each_rings_next_sequence_with_no_events_read()
    {
        using var server = Seeded();
        Assert.Equal(5, Field(server, "/events/head", "events"));
    }

    [Fact]
    public void A_write_reply_names_where_its_own_events_start()
    {
        using var server = Seeded();
        long from = Field(server, "/entity/damage?target=nothing_has_this_name", "eventsFrom");
        Assert.Equal(5, from);
    }

    [Theory]
    [InlineData("a*", "abc", true)]
    [InlineData("a*c", "abxc", true)]
    [InlineData("a?c", "abc", true)]
    [InlineData("a?c", "ac", false)]
    [InlineData("ABC", "abc", true)]
    [InlineData("ss_livegate_*", "ss_livegate_crate_1", true)]
    [InlineData("ss_livegate_*", "ss_other", false)]
    [InlineData("*", "", true)]
    public void Glob_matches(string pattern, string text, bool expected)
        => Assert.Equal(expected, Glob.Matches(pattern, text));
}
