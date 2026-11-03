using System.Text.Json;

using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Vgui;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's <c>gameui</c> route (plan_gameui_managed.md §4): the engine's
/// GameUI calls as they reached managed, each kept call's outcome by the
/// host's ruling, and the calls no host took.
/// </summary>
public class ClientGameUiRouteTests
{
    private static JsonDocument Read(GameUiCallLog log, GameUiLoadMeter? loads = null)
    {
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(gameUi: new ClientDebugGameUi(log, loads ?? new GameUiLoadMeter())));
        return JsonDocument.Parse(client.Answer(DebugChannelQuery.GameUi));
    }

    [Fact]
    public void A_session_reads_back_with_each_call_s_outcome()
    {
        var log = new GameUiCallLog();
        log.Record(GameUiMethod.Initialize, false, "0x10");
        log.Record(GameUiMethod.RunFrame, true);
        log.Record(GameUiMethod.OnGameUIActivated, true);
        log.Record(GameUiMethod.ConsoleSetParent, true, "0x20");
        using var doc = Read(log);
        var root = doc.RootElement;
        Assert.Equal(4, root.GetProperty("calls").GetInt64());
        Assert.Equal(1, root.GetProperty("unruled").GetInt64());
        Assert.Equal(1, root.GetProperty("counts").GetProperty("RunFrame").GetInt64());
        var recent = root.GetProperty("recent");
        Assert.Equal(3, recent.GetArrayLength());
        Assert.Equal(("Initialize", "0x10", "unhosted"), Row(recent[0]));
        Assert.Equal(("OnGameUIActivated", null, "handled"), Row(recent[1]));
        Assert.Equal(("ConsoleSetParent", "0x20", "inert"), Row(recent[2]));
        Assert.Equal(4, recent[2].GetProperty("sequence").GetInt64());
    }

    [Fact]
    public void The_last_load_s_paint_count_reads_back_and_is_null_before_one()
    {
        using (var none = Read(new GameUiCallLog()))
            Assert.Equal(JsonValueKind.Null, none.RootElement.GetProperty("lastLoad").ValueKind);
        var loads = new GameUiLoadMeter();
        loads.Started(true, 10);
        loads.Updated(true);
        loads.Updated(false);
        loads.Finished(14);
        using var doc = Read(new GameUiCallLog(), loads);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("loads").GetInt32());
        var last = root.GetProperty("lastLoad");
        Assert.True(last.GetProperty("dialog").GetBoolean());
        Assert.Equal(2, last.GetProperty("updates").GetInt32());
        Assert.Equal(1, last.GetProperty("repaints").GetInt32());
        Assert.Equal(4, last.GetProperty("frames").GetInt64());
    }

    private static (string?, string?, string?) Row(JsonElement e)
        => (e.GetProperty("method").GetString(), e.GetProperty("arguments").GetString(), e.GetProperty("outcome").GetString());

    /// <summary>
    /// The route's "inert" is the host's ruling, so it must name exactly the
    /// methods whose bodies do nothing: an empty body or a constant answer is
    /// at most two IL bytes (<c>nop; ret</c>, <c>ldc.i4.0; ret</c>), and any
    /// call into the UI is more.
    /// </summary>
    [Fact]
    public void The_inert_ruling_names_exactly_the_host_s_empty_bodies()
    {
        int inert = 0;
        for (var m = GameUiMethod.Initialize; m <= GameUiMethod.ConsoleSetParent; m++)
        {
            var method = typeof(GameUiHost).GetMethod(m.ToString());
            Assert.True(method is not null, $"GameUiHost has no {m}");
            int il = method.GetMethodBody()!.GetILAsByteArray()!.Length;
            Assert.True(GameUiHost.AnswersInertly(m) == il <= 2, $"{m}: {il} IL bytes, ruled {(GameUiHost.AnswersInertly(m) ? "inert" : "handled")}");
            if (il <= 2)
                inert++;
        }
        Assert.True(inert > 0 && inert < (int)GameUiMethod.ConsoleSetParent + 1, $"{inert} inert");
    }
}
