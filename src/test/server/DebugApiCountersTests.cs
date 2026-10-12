using System.Text.Json;

using Debug.Api;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interface;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>/counters: counter groups as JSON numbers (debug-api lane, item 3).</summary>
public class DebugApiCountersTests
{
    private static DebugApiServer Serve(Func<IReadOnlyList<DebugCounter>?> physics)
        => DebugApiServer.ForTest(TestDebugHooks.Server(
            world: new FakeDebugWorld(),
            counterGroups: new DebugCounterGroups(
            [
                ("physics", "cs_sv_physics_events", physics),
                ("playershadow", "cs_sv_playershadow", () => [new("snaps", 4)]),
            ])));

    private static JsonElement Read(DebugApiServer server, string path)
    {
        using var doc = JsonDocument.Parse(DebugApiGolden.Pumped(server, path).Body);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void A_group_is_served_as_numbers_by_name()
    {
        using var server = Serve(() => [new("responded", 12), new("shadowMoves", 3)]);
        var group = Read(server, "/counters?group=physics").GetProperty("groups")[0];
        Assert.Equal(12, group.GetProperty("counters").GetProperty("responded").GetInt64());
        Assert.Equal("cs_sv_physics_events", group.GetProperty("readout").GetString());
    }

    [Fact]
    public void No_group_named_serves_every_group()
    {
        using var server = Serve(() => []);
        Assert.Equal(2, Read(server, "/counters").GetProperty("groups").GetArrayLength());
    }

    [Fact]
    public void An_absent_source_is_served_as_unavailable_not_as_zeroes()
    {
        using var server = Serve(() => null);
        var group = Read(server, "/counters?group=physics").GetProperty("groups")[0];
        Assert.False(group.GetProperty("available").GetBoolean());
    }

    [Fact]
    public void An_unknown_group_is_an_error_listing_the_known_ones()
    {
        using var server = Serve(() => []);
        var e = Read(server, "/counters?group=nope");
        Assert.Equal("no such counter group", e.GetProperty("error").GetString());
        Assert.Equal(2, e.GetProperty("expected").GetArrayLength());
    }

    private sealed class FakeDiagnostics(int[] counts) : IEngineDiagnostics
    {
        public int PlayerShadowCounts(Span<int> into)
        {
            int n = Math.Min(into.Length, counts.Length);
            counts.AsSpan(0, n).CopyTo(into);
            return n;
        }

        public int ExitWatchArm(string recordPath) => 0;
        public int ExitWatchRead(Span<int> into) => 0;
        public int ExitWatchDisarm() => 0;
    }

    private static PlayerShadowBodies Bodies()
        => new(new FakeShadowNative(), new FakeShadowCalls(), () => true, _ => { });

    [Fact]
    public void The_playershadow_group_names_its_counters_in_their_order()
    {
        // The bodies' counters (plan_physics_bones_native_zero 2.5), then native's two.
        var counters = DebugCounterGroups.PlayerShadow(Bodies(), new FakeDiagnostics([7, 8]))!;
        Assert.Equal(["enabled", "built", "updates", "contacts", "touches", "touchOverflows", "snaps", "live", "outlived"],
                     counters.Select(c => c.Name));
        Assert.Equal(1, counters.Single(c => c.Name == "enabled").Value);
        Assert.Equal(7, counters.Single(c => c.Name == "live").Value);
        Assert.Equal(8, counters.Single(c => c.Name == "outlived").Value);
    }

    [Fact]
    public void The_playershadow_group_is_unavailable_when_native_answers_nothing()
        => Assert.Null(DebugCounterGroups.PlayerShadow(Bodies(), new FakeDiagnostics([])));

    [Fact]
    public void The_think_group_counts_bookings_by_class_and_context()
    {
        var a = new SourceSharp.Abi.EntityHandle(0x10001u);
        var b = new SourceSharp.Abi.EntityHandle(0x10002u);
        var counters = DebugCounterGroups.Thinks(
            [new(a, null, 1f, 1), new(b, null, 2f, 2), new(b, "Fade", 3f, 3)],
            h => h == a ? "func_door" : "prop_physics", now: 0.5f);
        Assert.Equal(3, counters.Single(c => c.Name == "total").Value);
        Assert.Equal(1, counters.Single(c => c.Name == "booked.prop_physics:Fade").Value);
        Assert.Equal(1, counters.Single(c => c.Name == "booked.func_door").Value);
    }

    [Fact]
    public void The_think_group_counts_what_comes_due_within_each_window()
    {
        var e = new SourceSharp.Abi.EntityHandle(0x10001u);
        var counters = DebugCounterGroups.Thinks(
            [new(e, null, 10.5f, 1), new(e, "a", 12f, 2), new(e, "b", 40f, 3)], _ => "item_ammo_pistol", now: 10f);
        Assert.Equal(1, counters.Single(c => c.Name == "dueWithin.1").Value);
        Assert.Equal(2, counters.Single(c => c.Name == "dueWithin.2").Value);
        Assert.Equal(2, counters.Single(c => c.Name == "dueWithin.10").Value);
        Assert.Equal(3, counters.Single(c => c.Name == "dueWithin.30").Value);
    }

    [Fact]
    public void The_think_group_names_which_classes_have_a_booking_due_within_a_second()
    {
        // The live gate's hot-reload check compares two reads of the heap: a
        // class booked within 1s is in both reads, so it is a recurring
        // thinker that must survive the reload. A class whose bookings are
        // all further out may have run its one-shot in between, and is
        // subtracted from the expected total instead of named. Without the
        // per-class due signal the check cannot tell the two apart, and a
        // lost recurring booking hides behind other classes refilling the
        // total (measured live-gate --steps hot-reload, 2026-10-06).
        var e = new SourceSharp.Abi.EntityHandle(0x10001u);
        var counters = DebugCounterGroups.Thinks(
            [new(e, null, 10.5f, 1), new(e, null, 10.9f, 2), new(e, "a", 12f, 3)],
            _ => "keyframe_rope", now: 10f);
        Assert.Equal(2, counters.Single(c => c.Name == "due1.keyframe_rope").Value);
        Assert.DoesNotContain(counters, c => c.Name == "due1.keyframe_rope:a");
    }
    [Fact]
    public void The_think_group_says_when_each_booking_of_a_class_comes_due()
    {
        // The hot-reload check needs per booking, not per window, how far out
        // it was: a booking due inside the reload's span may have run and
        // gone (item_ammo_pistol's one-shot respawn, which read as lost 1 in
        // 5-10 runs), and every booking due after the span must still be
        // there. Milliseconds from now, ascending per class, never negative.
        var e = new SourceSharp.Abi.EntityHandle(0x10001u);
        var counters = DebugCounterGroups.Thinks(
            [new(e, null, 30f, 1), new(e, null, 10.25f, 2), new(e, "a", 9f, 3)],
            _ => "item_ammo_pistol", now: 10f);
        Assert.Equal(250, counters.Single(c => c.Name == "dueMs.item_ammo_pistol.0").Value);
        Assert.Equal(20000, counters.Single(c => c.Name == "dueMs.item_ammo_pistol.1").Value);
        Assert.Equal(0, counters.Single(c => c.Name == "dueMs.item_ammo_pistol:a.0").Value);
    }
}
