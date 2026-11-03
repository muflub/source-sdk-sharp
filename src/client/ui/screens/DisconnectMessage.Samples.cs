using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>The disconnect dialog's samples: a short reason and a long one.</summary>
internal static class DisconnectMessageSamples
{
    private sealed class Fake(string body) : IDisconnectMessageSource
    {
        public bool Showing => true;
        public string Body => body;
        public void Close() { }
    }

    [UiSample("a short reason")]
    private static UiScreen Short(UiSampleServices s)
        => s.Add<IDisconnectMessageSource>(new Fake("Server shutting down")).Create<DisconnectMessage>();

    [UiSample("a long reason")]
    private static UiScreen Long(UiSampleServices s)
        => s.Add<IDisconnectMessageSource>(new Fake(
            "This Steam account has been used to log in from another computer. To continue using Steam, you need to log in again."))
            .Create<DisconnectMessage>();
}
