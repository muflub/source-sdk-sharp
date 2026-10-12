using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>The message of the day's samples: a short welcome and a long rules text that scrolls.</summary>
internal static class MotdSamples
{
    private sealed class Fake(string title, string text) : IMotdSource
    {
        public string Title => title;

        public string Text => text;
    }

    [UiSample("short welcome")]
    private static UiScreen Short(UiSampleServices s)
        => s.Add<IMotdSource>(new Fake("SourceSharp Deathmatch", "Welcome to dm_lockdown. Be nice, have fun.\nType !help in chat for commands.")).Create<Motd>();

    [UiSample("long rules, scrolling")]
    private static UiScreen Long(UiSampleServices s)
        => s.Add<IMotdSource>(new Fake("Server rules", string.Join("\n", Enumerable.Range(1, 30).Select(i =>
            $"{i}. No spawn camping, no exploiting the map, and keep the chat civil. Rule {i} applies on every map.")))).Create<Motd>();
}
