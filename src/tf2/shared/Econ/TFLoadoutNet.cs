using SourceSharp.Abi;

namespace TF2.Shared.Econ;

/// <summary>
/// The loadout screen's ask: the client's one-byte request for a class's
/// loadout, sent as a SourceSharp net message
/// (<c>IEngineNetworking.SendServerMessage</c>) and claimed by
/// <c>TF2.Server.Econ.TFLoadoutServer</c>. Not a registry declaration —
/// client-to-server messages name their own key.
/// </summary>
public static class LoadoutAsk
{
    /// <summary>The key the message travels under.</summary>
    public const string Key = "TF2:loadout";

    public static byte[] Write(byte tfClass) => [tfClass];

    /// <summary>False for anything that is not exactly one class byte.</summary>
    public static bool TryRead(ReadOnlySpan<byte> payload, out byte tfClass)
    {
        if (payload.Length != 1)
        {
            tfClass = 0;
            return false;
        }

        tfClass = payload[0];
        return true;
    }
}

/// <summary>
/// The loadout screen's reply: a class's visible slots and what is equipped
/// in each, plus the items the player owns that the class can use — what
/// stock's <c>CClassLoadoutPanel</c> reads out of the GC. One TF2 user
/// message payload: <c>byte</c> and <c>uint</c> scalars, arrays under count
/// bytes.
/// </summary>
public partial struct LoadoutReply
{
    /// <summary>The most equipped slots a reply carries: the spy's and the engineer's eight.</summary>
    public const int MaxSlots = 8;

    /// <summary>The most owned items a reply carries; the server cuts the rest and says so.</summary>
    public const int MaxOwned = 13;

    /// <summary>
    /// The equipped defindex of a slot with nothing in it (nothing equipped
    /// and no base item). Never 0: the schema has a defindex-0 placeholder.
    /// </summary>
    public const uint NoItem = uint.MaxValue;

    public partial byte Class { get; set; }

    [UserMessageArray(MaxCount = MaxSlots)]
    public partial byte[] EquippedSlots { get; set; }

    [UserMessageArray(MaxCount = MaxSlots)]
    public partial uint[] EquippedDefindexes { get; set; }

    [UserMessageArray(MaxCount = MaxOwned)]
    public partial uint[] OwnedDefindexes { get; set; }

    [UserMessageArray(MaxCount = MaxOwned)]
    public partial byte[] OwnedSlots { get; set; }
}

/// <summary>What the reply's arrival tells the screen's game half.</summary>
public interface ITFLoadoutReplyHook
{
    void OnLoadoutReply(LoadoutReply reply);
}

/// <summary>The <c>TF2:loadout</c> reply's receiving half.</summary>
[UserMessage("loadout")]
public sealed class LoadoutReplyMessage : IUserMessage<LoadoutReply>
{
    private readonly IHooks<ITFLoadoutReplyHook>? _hooks;

    /// <summary>A handler whose reply goes nowhere: the no-container fallback's (and the server's).</summary>
    public LoadoutReplyMessage()
    {
    }

    /// <summary>The client's container's: the hook chain the reply dispatches to.</summary>
    [Inject]
    public LoadoutReplyMessage(IHooks<ITFLoadoutReplyHook>? hooks) => _hooks = hooks;

    public void OnReceived(LoadoutReply message) =>
        _hooks?.Each(nameof(ITFLoadoutReplyHook.OnLoadoutReply), hook => hook.OnLoadoutReply(message));
}
