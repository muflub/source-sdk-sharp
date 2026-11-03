using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>A vote at a fixed count, for the samples and the facts.</summary>
public sealed class FakeVote : IVoteSource
{
    public bool Showing { get; set; } = true;
    public bool Decided { get; set; }
    public bool Passed { get; set; }
    public string Title { get; set; } = "#SS_UI_Vote_KickTitle";
    public string Target { get; set; } = "xX_Headshot_Xx";
    public string Caller { get; set; } = "Alyx";
    public int Yes { get; set; } = 3;
    public int No { get; set; } = 1;
    public int Voters { get; set; } = 8;
    public int SecondsLeft { get; set; } = 11;
    public bool Voted { get; set; }
    public string Key { get; set; } = "O";
    public string? Refusal { get; set; }
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
}
