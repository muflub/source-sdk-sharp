using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

internal static class CentrePrintSamples
{
    [UiSample("short message")]
    private static UiScreen Short(UiSampleServices s) => s.Add<ICentrePrintSource>(new FakeHudText { CenterPrint = "Round starts in 10 seconds" }).Create<CentrePrint>();

    [UiSample("long message, wrapped")]
    private static UiScreen Long(UiSampleServices s) => s.Add<ICentrePrintSource>(new FakeHudText
    {
        CenterPrint = "The Combine have locked down the facility. Reach the control room and restore power before the lockdown completes.",
    }).Create<CentrePrint>();

    [UiSample("busy chat")]
    private static UiScreen Busy(UiSampleServices s) => s.Add<IChatSource>(new FakeChat
    {
        Lines =
        [
            new ChatLine("Alyx", 3, "anyone near the generator?", false, 0.5f),
            new ChatLine("barney.calhoun", 3, "on my way, cover the stairs", true, 0.7f),
            new ChatLine("Overwatch", 2, "gg", false, 0.85f),
            new ChatLine("Dr. Kleiner", 3, "Has anyone seen Lamarr? She was here a moment ago, I am quite sure of it.", false, 1f),
            new ChatLine("Gordon", 2, "...", false, 1f),
        ],
    }).Create<Chat>();
}
