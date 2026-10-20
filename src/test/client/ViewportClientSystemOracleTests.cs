using SourceSharp.Client.ClientSystems;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed viewport system (<see cref="ViewportClientSystem"/>) against
/// stock's CViewportClientSystem, run offline from the pristine
/// vgui_game_viewport.cpp by src/test/oracle/native/viewport_oracle_test.cpp
/// (<c>Fixtures/viewport-stock.tsv</c>, README beside it). Each <c>S</c> row is
/// asked of the managed system through <see cref="ManagedClientSystems"/>, as
/// the native row's adapter asks it, and every row stock wrote after it must
/// be the row written here. Stock's OnSave, OnRestore, SafeRemoveIfDesired and
/// LevelShutdownPreClearSteamAPIContext are not stages of this client's walk
/// (CClientSystem has none of them); stock's bodies for them are empty, so the
/// golden holds nothing after them either.
/// </summary>
public class ViewportClientSystemOracleTests
{
    private sealed class Recorder(List<string> log) : IClientModeLayout
    {
        public void Layout() => log.Add("L\tLayout");
    }

    private static List<string> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "viewport-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock viewport system is missing: " + path);
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
        Assert.True(rows.Count(r => r.StartsWith("S\t", StringComparison.Ordinal)) >= 13, "the oracle parsed");
        return rows;
    }

    private static readonly Dictionary<string, ClientSystemStage> Walked = new(StringComparer.Ordinal)
    {
        ["Init"] = ClientSystemStage.Init,
        ["PostInit"] = ClientSystemStage.PostInit,
        ["Shutdown"] = ClientSystemStage.Shutdown,
        ["LevelInitPreEntity"] = ClientSystemStage.LevelInitPreEntity,
        ["LevelInitPostEntity"] = ClientSystemStage.LevelInitPostEntity,
        ["LevelShutdownPreEntity"] = ClientSystemStage.LevelShutdownPreEntity,
        ["LevelShutdownPostEntity"] = ClientSystemStage.LevelShutdownPostEntity,
    };

    [Fact]
    public void EveryStageDoesWhatStocksDid()
    {
        var log = new List<string>();
        var system = new ViewportClientSystem(new Recorder(log));
        var systems = new ManagedClientSystems();
        systems.Register(system);

        List<string> stock = Stock();
        int asked = 0, held = 0;
        for (int i = 0; i < stock.Count;)
        {
            string s = stock[i++];
            Assert.StartsWith("S\t", s);
            var expected = new List<string>();
            while (i < stock.Count && !stock[i].StartsWith("S\t", StringComparison.Ordinal))
                expected.Add(stock[i++]);

            log.Clear();
            string stage = s[2..];
            if (stage == "Name")
                log.Add("R\t" + system.Name);
            else if (stage == "IsPerFrame")
                log.Add("R\t0");    // a managed row's adapter is a CClientSystem, never per-frame
            else if (Walked.TryGetValue(stage, out ClientSystemStage walked))
            {
                int answer = systems.Stage(system.Name, (int)walked);
                Assert.NotEqual(-1, answer);
                if (walked == ClientSystemStage.Init)
                    log.Add("R\t" + answer);
                asked++;
            }
            Assert.True(expected.SequenceEqual(log), $"stage {stage}: stock {string.Join(" | ", expected)}, managed {string.Join(" | ", log)}");
            held += expected.Count;
        }
        // The stimulus reached the subject: the seven walked stages were asked, and rows held.
        Assert.True(asked >= 8 && held >= 6, $"{asked} stages asked, {held} rows held");
    }

    [Fact]
    public void AnUnknownRowOrStageIsRefused()
    {
        var systems = new ManagedClientSystems();
        systems.Register(new ViewportClientSystem(new Recorder([])));
        Assert.Equal(-1, systems.Stage("CNoSuchSystem", (int)ClientSystemStage.Init));
        Assert.Equal(-1, systems.Stage("CViewportClientSystem", 99));
        Assert.Equal(1, systems.Stage("CViewportClientSystem", (int)ClientSystemStage.Init));
    }
}
