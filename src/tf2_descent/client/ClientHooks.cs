using TF2.Descent.Shared;
using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui;
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
        registry.AddHooks<ITownHook>();
    }
}

/// <summary>
/// Notices and the instance line, on the HUD's text channels. The instance
/// line is the arrival's (plan_descent_mine_levels E4): the town, or a level's
/// act and name, shown once per arrival; the server sends the instance again
/// on a re-join, which shows nothing more until the level ends.
/// <para>
/// Tokens are read from the mod's language file through the UI's
/// <see cref="IUiLocalisation"/> (the engine's tables load none of the mod's
/// file), asked of the container when a line is drawn, as the core's ChatFeed
/// does: the UI context registers it after the mod is composed, and a UI
/// reload replaces it. With none, a token draws as itself.
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class HudNotices(IHudMessages hud, IGameClock clock, IServiceLocator services)
    : INoticeHook, IInstanceHook, IClientLevelHook, IOrderedHook
{
    /// <summary>Channels 0-5 belong to game_text; TF2Descent takes the last two.</summary>
    public const int NoticeChannel = 4, InstanceChannel = 5;

    /// <summary>
    /// Beside the mod's other level hook (LevelDownloads at 10): the client
    /// dispatches level hooks over the whole container, so each order is unique.
    /// </summary>
    public int HookOrder => 11;

    /// <summary>The instance this level's arrival line was shown for; null until one is, and again once the level ends.</summary>
    private (string Kind, int Depth)? _arrived;

    public void OnNotice(string text, string arg) =>
        hud.Show(Line(NoticeChannel, -1f, 0.35f, NoticeText(text, arg), hold: 5f), clock.Now);

    /// <summary>
    /// What a notice says: a <c>#Token</c> through the mod's language file
    /// with its <c>%s1</c> made <paramref name="arg"/>, a token the file lacks
    /// as itself (so the miss is seen), plain text as it was sent.
    /// </summary>
    public string NoticeText(string text, string arg)
    {
        if (text.Length == 0 || text[0] != '#')
            return text;

        return Find(text).Replace("%s1", arg, StringComparison.Ordinal);
    }

    public void OnInstance(string kind, int depth)
    {
        if (_arrived == (kind, depth))
            return;

        _arrived = (kind, depth);
        hud.Show(Line(InstanceChannel, 0.02f, 0.02f, InstanceText(kind, depth), hold: 6f), clock.Now);
    }

    /// <summary>The level is going: the next instance is a new arrival.</summary>
    public void OnLevelShutdown() => _arrived = null;

    /// <summary>
    /// What the arrival line says: the town; a level's act on one line and
    /// its depth and name on the next (<see cref="LevelNames"/>); a depth the
    /// mine has no name for as the bare depth.
    /// </summary>
    public string InstanceText(string kind, int depth)
    {
        if (string.Equals(kind, "hub", StringComparison.Ordinal))
            return "Town";

        if (LevelNames.ActToken(depth) is not { } act || LevelNames.LevelToken(depth) is not { } level)
            return $"Depth {depth}";

        string second = Find(LevelNames.ArrivalDepthToken)
            .Replace("%s1", depth.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%s2", Find(level), StringComparison.Ordinal);
        return Find(act) + "\n" + second;
    }

    /// <summary>The token's text in the mod's language file, or the token itself when the file lacks it.</summary>
    private string Find(string token) => services.Resolve<IUiLocalisation>()?.Find(token) ?? token;

    private static HudMessageArgs Line(int channel, float x, float y, string text, float hold) =>
        new(channel, x, y, new Color32(255, 220, 160, 255), new Color32(255, 255, 255, 255),
            Effect: 0, FadeIn: 0.2f, FadeOut: 0.5f, HoldTime: hold, FxTime: 0f, Text: text);
}
