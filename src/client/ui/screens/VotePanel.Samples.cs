using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>A vote at a fixed count, for the samples and the facts.</summary>
public sealed class FakeVote : IVoteSource
{
    public bool Showing { get; set; } = true;
    public bool Decided { get; set; }
    public bool Passed { get; set; }
    public string Title { get; set; } = "#SS_UI_Vote_KickTitle";
    public string? ResultSubject { get; set; }
    public string Target { get; set; } = "xX_Headshot_Xx";
    public string Caller { get; set; } = "Alyx";
    public int Yes { get; set; } = 3;
    public int No { get; set; } = 1;
    public int Voters { get; set; } = 8;
    public int SecondsLeft { get; set; } = 11;
    public bool Voted { get; set; }
    public string Key { get; set; } = "O";
    public string? Refusal { get; set; }
    public string RefusalReason { get; set; } = "";
    public IReadOnlyList<string> Options { get; set; } = [];
    public int[] Counts { get; set; } = [];
    public int CountOf(int option) => option >= 1 && option <= Counts.Length ? Counts[option - 1] : 0;
}

internal static class VotePanelSamples
{
    private static UiScreen Build(UiSampleServices s, FakeVote vote) => s.Add<IVoteSource>(vote).Create<VotePanel>();

    [UiSample("open, yet to vote")]
    private static UiScreen Open(UiSampleServices s) => Build(s, new FakeVote());

    [UiSample("open, voted")]
    private static UiScreen Voted(UiSampleServices s) => Build(s, new FakeVote { Voted = true, Yes = 5, No = 2 });

    [UiSample("passed")]
    private static UiScreen Passed(UiSampleServices s) => Build(s, new FakeVote { Decided = true, Passed = true, Yes = 6, No = 1 });

    [UiSample("failed")]
    private static UiScreen Failed(UiSampleServices s) => Build(s, new FakeVote { Decided = true, Yes = 2, No = 5 });

    [UiSample("refused")]
    private static UiScreen Refused(UiSampleServices s) => Build(s, new FakeVote { Showing = false, Refusal = "#SS_UI_Vote_Refused_Immune" });

    [UiSample("refused, with the server's reason")]
    private static UiScreen RefusedWhy(UiSampleServices s)
        => Build(s, new FakeVote { Showing = false, Refusal = "#SS_UI_Vote_Refused_NotNow", RefusalReason = "dm_lockdown has no item_teamflag" });

    [UiSample("next map, a choice of four")]
    private static UiScreen NextMap(UiSampleServices s)
        => Build(s, new FakeVote
        {
            Title = "#SS_UI_Vote_NextMapTitle", Target = "", Caller = "", Voters = 6, Yes = 2, No = 1,
            Options = ["dm_lockdown", "ss_ai_tdm", "dm_overwatch", "dm_resistance"], Counts = [2, 1, 0, 2],
        });

    [UiSample("next map, decided")]
    private static UiScreen NextMapDecided(UiSampleServices s)
        => Build(s, new FakeVote
        {
            Title = "#SS_UI_Vote_NextMapTitle", Target = "", Caller = "", Voters = 6, Decided = true, Passed = true, Yes = 3, No = 1,
            Options = ["dm_lockdown", "ss_ai_tdm", "dm_overwatch"], Counts = [3, 1, 1], Voted = true,
        });
}
