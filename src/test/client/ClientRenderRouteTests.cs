using System.Text.Json;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's <c>render</c> and <c>render/compare</c> routes (plan_client_novalve
/// C7, §4.4): the last plan, and the compare -- a frozen stock frame against
/// the next plan frame, and where their calls part (C7 step 3: the live stock
/// path is gone).
/// </summary>
public class ClientRenderRouteTests
{
    private sealed class Console(string? cheats) : IEngineConsole
    {
        public void ExecuteServerCommand(string command) => throw new NotSupportedException();
        public string? FindConVarValue(string name) => name == "sv_cheats" ? cheats : null;
        public string GetConVarValue(int id) => throw new NotSupportedException();
        public void Msg(string message) { }
        public int RegisterConCommand(string name, string help, ConVarFlags flags) => throw new NotSupportedException();
        public int RegisterConVar(string name, string defaultValue, string help, int flags, float? min = null, float? max = null) => throw new NotSupportedException();
        public void SendClientCommand(EntityHandle player, string command) => throw new NotSupportedException();
        public void SetConVarValue(int id, string value) => throw new NotSupportedException();
        public void SetConVarValueByName(string name, string value) => throw new NotSupportedException();
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out NativeConsoleEntry entry) { entry = default; return false; }
        public void Warning(string message) { }
    }

    private const string StockFrame = "frame\tpath=0 framecount=10 rect=0,0,64,64\nrender.SceneBegin\nrender.DrawLights\nrender.SceneEnd\n";
    private const string PlanFrame = "frame\tpath=1 framecount=11 rect=0,0,64,64\nplan\tops=3\nrender.SceneBegin\nrender.DrawLights\nrender.SceneEnd\n";
    private const string PlanFrameWithoutLights = "frame\tpath=1 framecount=11 rect=0,0,64,64\nplan\tops=2\nrender.SceneBegin\nrender.SceneEnd\n";

    private static RenderFrameOut Frame(RenderPlanHost host, int n)
    {
        var input = new RenderFrameIn { FrameCount = n };
        var answer = default(RenderFrameOut);
        host.Frame(in input, ref answer);
        return answer;
    }

    // A frozen frame on disk, as render-stock's recordings are.
    private static string Frozen(string text)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ss-frozen-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, text);
        return path;
    }

    // Drives a compare the way View_Render does: the plan frame, its text.
    private static void RunCompare(RenderPlanHost host, string plan)
    {
        RenderFrameOut b = Frame(host, 11);
        Assert.Equal((1, 1), (b.Path, b.Trace));
        host.TraceDone(b.TraceTag, plan);
    }

    private static string Answer(ClientDebugRender route, DebugChannelQuery query, string argument = "")
    {
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(render: route));
        return client.Answer(query, argument);
    }

    [Fact]
    public void A_compare_needs_cheats()
    {
        var host = new RenderPlanHost();
        var route = new ClientDebugRender(host, new Console("0"));
        using var doc = JsonDocument.Parse(Answer(route, DebugChannelQuery.RenderCompare));
        Assert.False(doc.RootElement.GetProperty("accepted").GetBoolean());
        Assert.Equal(0, Frame(host, 1).Trace);
    }

    [Fact]
    public void Two_frames_the_same_compare_equal()
    {
        var host = new RenderPlanHost();
        var route = new ClientDebugRender(host, new Console("1"));
        Assert.True(route.CompareFrozen(Frozen(StockFrame), false).Accepted);
        RunCompare(host, PlanFrame);
        var c = route.Read().Compare;
        Assert.Equal(("done", true, -1, 3, 3), (c.State, c.Equal, c.Index, c.StockCalls, c.PlanCalls));
        Assert.Equal("ops=3", c.PlanNote);
    }

    [Fact]
    public void Frames_that_part_say_where()
    {
        var host = new RenderPlanHost();
        var route = new ClientDebugRender(host, new Console("1"));
        Assert.True(route.CompareFrozen(Frozen(StockFrame), true).Accepted);
        RunCompare(host, PlanFrameWithoutLights);
        var c = route.Read().Compare;
        Assert.Equal(("done", false, 1), (c.State, c.Equal, c.Index));
        Assert.Equal("render.DrawLights", c.Stock);
        Assert.Equal("render.SceneEnd", c.Plan);
        Assert.True(c.Mutated);
    }

    [Fact]
    public void A_mutated_compare_drops_DrawLights_from_the_plan_frame_only()
    {
        var host = new RenderPlanHost();
        var route = new ClientDebugRender(host, new Console("1"));
        var input = default(RenderInputs);
        input.View.Width = 64;
        input.View.Height = 64;
        var ops = new RenderOp[512];

        int plain = host.BuildPlan(input, ops);
        Assert.Contains(ops.Take(plain), op => op.Kind == (int)RenderOpKind.DrawLights);

        Assert.True(route.CompareFrozen(Frozen(StockFrame), true).Accepted);
        Frame(host, 11);
        int mutated = host.BuildPlan(input, ops);
        Assert.Equal(plain - 1, mutated);
        Assert.DoesNotContain(ops.Take(mutated), op => op.Kind == (int)RenderOpKind.DrawLights);
    }

    [Fact]
    public void The_render_answer_renders_exactly()
    {
        var host = new RenderPlanHost();
        var route = new ClientDebugRender(host, new Console("1"));
        route.CompareFrozen(Frozen(StockFrame), true);
        RunCompare(host, PlanFrameWithoutLights);
        Assert.Equal(DebugApiClientGoldenTests.Golden("render"), Answer(route, DebugChannelQuery.Render));
    }

    [Fact]
    public void Render_times_are_kept_and_reset_on_request()
    {
        var host = new RenderPlanHost();
        var route = new ClientDebugRender(host, new Console("1"));
        var answer = default(RenderFrameOut);
        foreach (float ms in new[] { 3.0f, 5.0f, 0f })
        {
            var input = new RenderFrameIn { LastPath = 1, LastRenderMs = ms };
            host.Frame(in input, ref answer);
        }
        var r = route.Read(resetTimes: true);
        Assert.Equal((2L, 4.0, 5.0), (r.Plan.Frames, r.Plan.MsAvg, r.Plan.MsMax));
        Assert.Equal(0L, route.Read().Plan.Frames);
    }

    // The live stock compare left with the stock path: the route says how to ask now.
    [Fact]
    public void A_stock_compare_is_refused_and_says_how_to_ask()
    {
        var host = new RenderPlanHost();
        var route = new ClientDebugRender(host, new Console("1"));
        using var doc = JsonDocument.Parse(Answer(route, DebugChannelQuery.RenderCompare, "1"));
        Assert.False(doc.RootElement.GetProperty("accepted").GetBoolean());
        Assert.Contains("frozen:", doc.RootElement.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreadable_frozen_frame_is_refused()
    {
        var host = new RenderPlanHost();
        var route = new ClientDebugRender(host, new Console("1"));
        Assert.False(route.CompareFrozen("/nonexistent/frozen.txt", false).Accepted);
        Assert.Equal(0, Frame(host, 1).Trace);
    }
}
