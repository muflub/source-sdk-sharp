using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The vote on now, for everyone, over play: what it would do and who called it,
/// the yes and no counts growing live out of everyone who can vote, the time left,
/// the key that opens the ballot until this player has voted, and then the result.
/// Takes no input: the ballot is the radial menu, so a vote never grabs the mouse.
/// </summary>
public sealed class VotePanel(IVoteSource vote) : UiScreen
{
    private const float Width = 320;

    public override string Name => "votepanel";

    public override bool OpenByDefault => true;

    public override bool WantsMouse => false;

    public override int Order => 85;

    public override void Describe(ref UiFrame ui)
    {
        if (vote.Refusal is { } refusal && !vote.Showing)
        {
            using (ui.Panel().At(UiAnchor.TopRight).Inset(Space.L).Width(Size.Du(Width)).Layered().Key("vote-refused"))
            {
                ui.Text(refusal, Text.Body, Tone.Text).Wrap(Width - 2f * (float)Space.L);
                if (vote.RefusalReason.Length > 0)
                    ui.Data(vote.RefusalReason, Text.Caption, Tone.TextMuted).Wrap(Width - 2f * (float)Space.L);
            }
            return;
        }
        if (!vote.Showing)
            return;

        using (ui.Panel().At(UiAnchor.TopRight).Inset(Space.L).Width(Size.Du(Width)).Layered().Key("vote"))
        {
            ui.Text(vote.Decided ? vote.Passed ? "#SS_UI_Vote_Passed" : "#SS_UI_Vote_Failed" : vote.Title,
                    Text.Label, vote.Decided ? vote.Passed ? Tone.Accent : Tone.Text : Tone.TextMuted);
            if (vote.Decided && vote.ResultSubject is { } subject)
                ui.Text(subject, Text.Caption, Tone.TextMuted);
            // A decided choice names its winner, as a decided Yes/No names what it changed.
            if (vote.Decided && vote.Passed && vote.Options.Count > 0 && Leader(vote) is >= 0 and var won)
                ui.Data(vote.Options[won], Text.Heading, Tone.Text).Width(Size.Fill).Elide();
            if (vote.Target.Length > 0)
                (vote.Target.StartsWith('#') ? ui.Text(vote.Target, Text.Heading, Tone.Text) : ui.Data(vote.Target, Text.Heading, Tone.Text))
                    .Width(Size.Fill).Elide();
            // A ballot the server called (the end-of-map vote) has no caller to name.
            if (vote.Caller.Length > 0)
            {
                using (ui.Row(Space.XS).Width(Size.Fill))
                {
                    ui.Text("#SS_UI_Vote_CalledBy", Text.Caption, Tone.TextMuted);
                    ui.Data(vote.Caller, Text.Caption, Tone.TextMuted).Width(Size.Fill).Elide();
                }
            }
            if (vote.Options.Count > 0)
            {
                // A choice: each option numbered as the ballot ring numbers it, the leader in the accent.
                int lead = Leader(vote);
                for (int i = 0; i < vote.Options.Count; i++)
                    Choice(ref ui, i, vote.Options[i], vote.CountOf(i + 1), vote.Voters, i == lead ? Tone.Accent : Tone.TextMuted);
            }
            else
            {
                Count(ref ui, "#SS_UI_Vote_Yes", vote.Yes, vote.Voters, Tone.Accent, "yes");
                Count(ref ui, "#SS_UI_Vote_No", vote.No, vote.Voters, Tone.Danger, "no");
            }
            if (!vote.Decided)
            {
                using (ui.Row(Space.S).Width(Size.Fill))
                {
                    if (!vote.Voted && vote.Key.Length > 0)
                    {
                        ui.Text("#SS_UI_Vote_Press", Text.Label, Tone.TextMuted);
                        ui.Data(vote.Key, Text.Label, Tone.Accent);
                        ui.Text("#SS_UI_Vote_ToVote", Text.Label, Tone.TextMuted);
                    }
                    ui.Flex();
                    ui.Text($"{vote.SecondsLeft}s", Text.Label, Tone.TextMuted);
                }
            }
        }
    }

    /// <summary>The option with the most votes, the first of a tie (stock's ProcessResults); -1 with none cast.</summary>
    public static int Leader(IVoteSource vote)
    {
        int lead = -1, most = 0;
        for (int i = 0; i < vote.Options.Count; i++)
        {
            int count = vote.CountOf(i + 1);
            if (count > most)
                (lead, most) = (i, count);
        }
        return lead;
    }

    /// <summary>One choice of a multiple-choice ballot: its number on the ballot ring, its name, its count.</summary>
    private static void Choice(ref UiFrame ui, int index, string label, int count, int of, Tone tone)
    {
        using (ui.Column(Space.XS, Align.Stretch).Width(Size.Fill).Key(index))
        {
            using (ui.Row(Space.S).Width(Size.Fill))
            {
                ui.Number(index + 1, Text.Caption, Tone.TextMuted);
                ui.Data(label, Text.Caption, Tone.Text).Width(Size.Fill).Elide();
                ui.Text($"{count} / {of}", Text.Caption, Tone.Text);
            }
            Bar(ref ui, count, of, tone);
        }
    }

    private static void Count(ref UiFrame ui, string label, int count, int of, Tone tone, string key)
    {
        using (ui.Column(Space.XS, Align.Stretch).Width(Size.Fill).Key(key))
        {
            using (ui.Row(Space.S).Width(Size.Fill))
            {
                ui.Text(label, Text.Caption, Tone.Text);
                ui.Flex();
                ui.Text($"{count} / {of}", Text.Caption, Tone.Text);
            }
            Bar(ref ui, count, of, tone);
        }
    }

    private static void Bar(ref UiFrame ui, int count, int of, Tone tone)
    {
        using (ui.Box().Size(Size.Fill, Size.Du(8)).Fill(Tone.SurfaceRaised).Round(Radius.S))
            ui.Rect(tone, Size.Du(of > 0 ? (Width - 2f * (float)Space.L) * Math.Clamp(count, 0, of) / of : 0f), Size.Fill).Round(Radius.S);
    }
}
