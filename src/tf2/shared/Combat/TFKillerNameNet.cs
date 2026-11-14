using SourceSharp.Abi;

namespace TF2.Shared.Combat;

/// <summary>
/// The name of a killer that is no player, sent with a <c>player_death</c>:
/// the dead player's user id, a localisation token the client resolves (stock
/// names its own non-player killers by token, <c>#TF_HALLOWEEN_*_DEATHCAM_NAME</c>,
/// in <c>CTFHudDeathNotice::OnGameEvent</c>, tf_hud_deathnotice.cpp:1068-1118),
/// and the team whose colour the name takes (stock's bosses take
/// <c>TF_TEAM_HALLOWEEN</c>). TF's <c>player_death</c> has no key for it, so
/// it rides a reliable message the server sends just before the event, in the
/// same reliable stream.
/// </summary>
public partial struct TFKillerName
{
    /// <summary>The longest token the message carries; longer is cut.</summary>
    public const int MaxToken = 64;

    /// <summary>The dead player's user id (<c>player_death</c>'s <c>userid</c>).</summary>
    public partial int Victim { get; set; }

    /// <summary>The team whose colour the killer's name takes.</summary>
    public partial byte Team { get; set; }

    /// <summary>A <c>#Token</c> of a language file the client holds.</summary>
    [UserMessageString(MaxLength = MaxToken)]
    public partial string Token { get; set; }
}

/// <summary>What the killer name's arrival tells the client (the kill feeds' namer).</summary>
public interface ITFKillerNameHook
{
    void OnKillerName(TFKillerName name);
}

/// <summary>The killer name of a death no player scored, to every client.</summary>
[UserMessage(Name)]
public sealed class TFKillerNameMessage : IUserMessage<TFKillerName>
{
    public const string Name = "killer_name";

    private readonly IHooks<ITFKillerNameHook>? _hooks;

    /// <summary>A handler whose names go nowhere: the no-container fallback's (and the server's).</summary>
    public TFKillerNameMessage()
    {
    }

    /// <summary>The client's container's: the hook chain the name dispatches to.</summary>
    [Inject]
    public TFKillerNameMessage(IHooks<ITFKillerNameHook>? hooks) => _hooks = hooks;

    public void OnReceived(TFKillerName message) =>
        _hooks?.Each(nameof(ITFKillerNameHook.OnKillerName), hook => hook.OnKillerName(message));
}
