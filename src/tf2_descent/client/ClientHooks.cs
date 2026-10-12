using TF2.Descent.Shared;
using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;

namespace TF2.Descent.Client;

/// <summary>The dispatch for each of TF2.Descent.Shared's client hook interfaces.</summary>
[SideService(Side.Client)]
public sealed class TF2DescentClientRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry)
    {
        registry.AddHooks<INoticeHook>();
        registry.AddHooks<IInstanceHook>();
        registry.AddHooks<INextLevelHook>();
        registry.AddHooks<IReauthHook>();
    }
}

/// <summary>Notices and the instance line, on the HUD's text channels.</summary>
[SideService(Side.Client)]
public sealed class HudNotices(IHudMessages hud, IGameClock clock) : INoticeHook, IInstanceHook
{
    /// <summary>Channels 0-5 belong to game_text; TF2Descent takes the last two.</summary>
    public const int NoticeChannel = 4, InstanceChannel = 5;

    public void OnNotice(string text) =>
        hud.Show(Line(NoticeChannel, -1f, 0.35f, text, hold: 5f), clock.Now);

    public void OnInstance(string kind, int depth) =>
        hud.Show(Line(InstanceChannel, 0.02f, 0.02f, InstanceText(kind, depth), hold: 6f), clock.Now);

    /// <summary>What the instance line says: the town, or the depth.</summary>
    public static string InstanceText(string kind, int depth) =>
        string.Equals(kind, "hub", StringComparison.Ordinal) ? "Town" : $"Depth {depth}";

    private static HudMessageArgs Line(int channel, float x, float y, string text, float hold) =>
        new(channel, x, y, new Color32(255, 220, 160, 255), new Color32(255, 255, 255, 255),
            Effect: 0, FadeIn: 0.2f, FadeOut: 0.5f, HoldTime: hold, FxTime: 0f, Text: text);
}
