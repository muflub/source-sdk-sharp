using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

public static class ChatInputSamples
{
    private sealed record Line(bool TeamOnly, string Input, int Caret) : IChatInputSource;

    [UiSample("say, typing")]
    private static UiScreen Say(UiSampleServices s) => s.Add<IChatInputSource>(new Line(false, "gg, that crossbow shot", 22)).Create<ChatInput>();

    [UiSample("say to the team, empty")]
    private static UiScreen Team(UiSampleServices s) => s.Add<IChatInputSource>(new Line(true, "", 0)).Create<ChatInput>();
}
