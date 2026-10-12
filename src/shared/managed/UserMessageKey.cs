using System.Reflection;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// The key a user message is registered and sent under
/// (plan_side_isolation §2.2).
///
/// <para>
/// A stock message keeps its bare name (<c>Shake</c>): Valve's code hooks and
/// sends it by that name. Every other message is <c>&lt;module&gt;:&lt;id&gt;</c>,
/// so an id only has to be unique within its own module. The module is the
/// declaring assembly's name without its side suffix: SourceSharp.Shared,
/// SourceSharp.Server and SourceSharp.Client are all <c>SourceSharp</c>, so a
/// shared declaration has one key whichever side's assembly compiled it.
/// </para>
/// </summary>
public static class UserMessageKey
{
    /// <summary>The engine's limit on a key read back through a string table, less its terminator.</summary>
    public const int MaxKeyBytes = 255;

    /// <summary>
    /// Every name Valve's code registers or hooks: hl2_usermessages.cpp's 28,
    /// haptic_msgs.cpp's six, and SendAudio and SavedConvar, which stock client
    /// code hooks although this build registers neither on the server.
    /// </summary>
    public static readonly IReadOnlySet<string> StockNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "SPHapWeapEvent", "HapDmg", "HapPunch", "HapSetDrag", "HapSetConst", "HapMeleeContact",
        "Geiger", "Train", "HudText", "SayText", "SayText2", "TextMsg", "HudMsg", "ResetHUD",
        "GameTitle", "ItemPickup", "ShowMenu", "Shake", "Fade", "VGUIMenu", "Rumble", "Battery",
        "Damage", "VoiceMask", "RequestState", "CloseCaption", "HintText", "KeyHintText",
        "SquadMemberDied", "AmmoDenied", "CreditsMsg", "LogoTimeMsg", "AchievementEvent",
        "UpdateJalopyRadar", "SendAudio", "SavedConvar",
    };

    /// <summary>The key <paramref name="type"/> is registered under. Throws on a declaration the rules refuse.</summary>
    public static string Of(Type type)
    {
        var attr = type.GetCustomAttribute<UserMessageAttribute>()
                   ?? throw new InvalidOperationException($"{type.FullName} is not a [UserMessage]");

        return Of(type, attr);
    }

    /// <inheritdoc cref="Of(Type)"/>
    public static string Of<TMessage>() => Of(typeof(TMessage));

    internal static string Of(Type type, UserMessageAttribute attr)
    {
        string id = attr.Name;

        if (id.Length == 0 || id.Any(c => c <= ' ' || c > '~' || c == ':'))
            throw new InvalidOperationException(
                $"[UserMessage(\"{id}\")] on {type.FullName}: an id is printable ASCII with no ':' " +
                "and no whitespace (the module and ':' are added to it)");

        if (attr.Stock)
        {
            if (!StockNames.Contains(id))
                throw new InvalidOperationException(
                    $"[UserMessage(\"{id}\", Stock = true)] on {type.FullName}: '{id}' is not a name " +
                    "Valve's code uses. Only a stock message keeps a bare name; drop Stock and it is " +
                    "keyed under its module.");
            return id;
        }

        string key = LoadedAssemblies.ModuleOf(type.Assembly) + ":" + id;

        if (Encoding.UTF8.GetByteCount(key) > MaxKeyBytes)
            throw new InvalidOperationException(
                $"user message key '{key}' on {type.FullName} is longer than {MaxKeyBytes} bytes");

        return key;
    }
}
