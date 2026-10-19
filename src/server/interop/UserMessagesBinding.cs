using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The close caption a sound carries: what native's EmitCloseCaption forwards
/// once it has looked up the facts only it can (the recipients, the wave's
/// duration, the speaker's gender). Implemented by SourceSharp.Server's
/// CloseCaptions, which decides and sends.
/// </summary>
public interface ICloseCaptions
{
    /// <summary>
    /// Whether a caption would be sent at all just now. The SoundEmitter asks
    /// before it looks up a caption's facts (a wave's duration costs a file
    /// read), so the rule is decided here and the emitter only skips the work.
    /// </summary>
    bool Wanted { get; }

    /// <summary>Sends the caption to each recipient slot, or to none; answers how many sends went out.</summary>
    int Emit(ReadOnlySpan<int> recipients, bool reliable, string token, float duration,
             bool fromPlayer, bool warnIfMissing, int gender);
}

/// <summary>
/// The server's user messages, managed-built (plan_side_isolation §2.2a): the
/// engine's GetUserMessageInfo forwards here and the registry answers. No
/// native code names a message any more: every message is a C# payload, and
/// the close caption, which sound_emitter.cpp forwarded here, is the managed
/// SoundEmitter's call to <see cref="ICloseCaptions"/> since
/// plan_server_native_v2 T4a.
/// </summary>
public sealed unsafe class UserMessagesBinding(IUserMessageCatalog registry) : IManagedUserMessages
{
    /// <summary>The message's key and size at engine id <paramref name="a0"/>; 0 for an id the registry does not hold.</summary>
    public byte GetUserMessageInfo(int a0, byte* a1, int a2, void* a3)
    {
        if (!registry.TryDescribe(a0, out string key, out int size) || a1 == null || a2 <= 0)
            return 0;

        // NUL-terminated and truncated to the engine's buffer, as Q_strncpy
        // left it: the key's bytes, never more than a2 - 1 of them.
        var name = new Span<byte>(a1, a2);
        int written = Truncate(key, name[..^1]);
        name[written] = 0;

        if (a3 != null)
            *(int*)a3 = size;

        return 1;
    }

    private static int Truncate(string text, Span<byte> into)
    {
        int n = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            int width = rune.Utf8SequenceLength;
            if (n + width > into.Length)
                break;
            n += rune.EncodeToUtf8(into[n..]);
        }
        return n;
    }
}
