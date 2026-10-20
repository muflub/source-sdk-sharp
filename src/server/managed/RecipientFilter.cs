//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Who a sound reaches, decided here: stock's <c>CRecipientFilter</c>
/// (recipientfilter.cpp) as the managed SoundEmitter needs it
/// (plan_server_native_v2 T4a). A list of player slots and the two flags the
/// engine asks for; the engine reads it through the native
/// <c>IRecipientFilter</c> libss_fwd_server builds over
/// <see cref="RecipientFilterSink"/>.
/// </summary>
public sealed class RecipientFilter : IRecipientList
{
    private readonly List<int> _recipients = [];
    private readonly int _maxClients;

    /// <summary>An empty filter for a server of <paramref name="maxClients"/> slots.</summary>
    public RecipientFilter(int maxClients) => _maxClients = maxClients;

    /// <inheritdoc/>
    public bool IsReliable { get; private set; }

    /// <inheritdoc/>
    public bool IsInitMessage { get; private set; }

    /// <inheritdoc/>
    public int Count => _recipients.Count;

    /// <summary>The slots, in the order they were added.</summary>
    public IReadOnlyList<int> Slots => _recipients;

    /// <inheritdoc/>
    public int IndexAt(int slot) => slot < 0 || slot >= _recipients.Count ? -1 : _recipients[slot];

    /// <summary><c>MakeReliable</c>.</summary>
    public RecipientFilter MakeReliable()
    {
        IsReliable = true;
        return this;
    }

    /// <summary>
    /// <c>AddRecipientByPlayerIndex</c>: a slot in 1..maxClients joins once;
    /// anything else, or a slot already listed, is ignored.
    /// </summary>
    public void Add(int playerIndex)
    {
        if (playerIndex < 1 || playerIndex > _maxClients)
            return;

        if (!_recipients.Contains(playerIndex))
            _recipients.Add(playerIndex);
    }

    /// <summary><c>RemoveRecipientByPlayerIndex</c>.</summary>
    public void Remove(int playerIndex) => _recipients.Remove(playerIndex);

    /// <summary>
    /// <c>AddPlayersFromBitMask</c>: bit N of the engine's
    /// CBitVec&lt;ABSOLUTE_PLAYER_LIMIT&gt; is player N + 1.
    /// </summary>
    public void AddFromBits(ReadOnlySpan<uint> playerBits)
    {
        for (int word = 0; word < playerBits.Length; word++)
        {
            for (int bit = 0; bit < 32; bit++)
            {
                if ((playerBits[word] & (1u << bit)) != 0)
                    Add(word * 32 + bit + 1);
            }
        }
    }

    /// <summary>
    /// <c>CopyFrom</c>: the flags and the list. The emitter copies before
    /// removing anyone, so a filter a sound patch owns is never emptied by a
    /// caption.
    /// </summary>
    public RecipientFilter Copy()
    {
        var copy = new RecipientFilter(_maxClients) { IsReliable = IsReliable, IsInitMessage = IsInitMessage };
        copy._recipients.AddRange(_recipients);
        return copy;
    }
}

/// <summary>
/// The players a sound can reach, as the recipient rules ask about them: the
/// server's slot count, who is connected, where each listens from, and the
/// closecaption setting each client reported.
/// </summary>
public interface ISoundListeners
{
    /// <summary><c>gpGlobals->maxClients</c>.</summary>
    int MaxClients { get; }

    /// <summary>Whether slot <paramref name="playerIndex"/> holds a connected player.</summary>
    bool IsConnected(int playerIndex);

    /// <summary>The player's ears (its eye position), or false for a slot with no player.</summary>
    bool TryEars(int playerIndex, out Vector ears);

    /// <summary>The client's <c>closecaption</c> convar as it reported it, or null.</summary>
    string? CloseCaptionSetting(int playerIndex);
}

/// <summary>
/// The filters stock's constructors build (recipientfilter.cpp):
/// <c>CRecipientFilter::AddAllPlayers</c>, <c>AddRecipientsByPAS</c>,
/// <c>CPASAttenuationFilter</c> and its <c>Filter</c>, and
/// <c>RemoveRecipientsIfNotCloseCaptioning</c>.
/// </summary>
public sealed class SoundRecipients(IServerSoundEmitterEngine engine, ISoundListeners listeners)
{
    /// <summary>SOUND_NORMAL_CLIP_DIST (const.h).</summary>
    public const float NormalClipDistance = 1000.0f;

    /// <summary>ATTN_NORM (soundflags.h).</summary>
    public const float AttnNorm = 0.8f;

    /// <summary>An empty filter for this server.</summary>
    public RecipientFilter Empty() => new(listeners.MaxClients);

    /// <summary><c>SNDLVL_TO_ATTN</c>: 20 / (level - 50) above 50 dB, 4 at or below.</summary>
    public static float SoundLevelToAttenuation(int soundLevel)
        => soundLevel > 50 ? 20.0f / (soundLevel - 50) : 4.0f;

    /// <summary><c>AddAllPlayers</c>: every connected slot, 1..maxClients.</summary>
    public RecipientFilter AllPlayers()
    {
        var filter = Empty();
        AddAllPlayers(filter);
        return filter;
    }

    private void AddAllPlayers(RecipientFilter filter)
    {
        for (int i = 1; i <= listeners.MaxClients; i++)
        {
            if (listeners.IsConnected(i))
                filter.Add(i);
        }
    }

    /// <summary>
    /// <c>CPASFilter( origin )</c>: everyone on a one-player server, otherwise
    /// the players whose PAS holds the point, as the engine reckons it.
    /// </summary>
    public RecipientFilter Pas(Vector origin) => Multicast(true, origin);

    /// <summary>
    /// <c>CPVSFilter( origin )</c>: AddRecipientsByPVS, the same rule over the
    /// PVS (plan_server_native_v2 T4b: the temp entities' PVS sends).
    /// </summary>
    public RecipientFilter Pvs(Vector origin) => Multicast(false, origin);

    private RecipientFilter Multicast(bool usePas, Vector origin)
    {
        var filter = Empty();

        if (listeners.MaxClients == 1)
        {
            AddAllPlayers(filter);
            return filter;
        }

        Span<uint> bits = stackalloc uint[IFwdEngineServer.PlayerBitWords];
        engine.MulticastRecipients(usePas, origin, bits);
        filter.AddFromBits(bits);
        return filter;
    }

    /// <summary><c>CPASAttenuationFilter( origin, attenuation )</c>.</summary>
    public RecipientFilter PasAttenuation(Vector origin, float attenuation = AttnNorm)
    {
        RecipientFilter filter = Pas(origin);
        Attenuate(filter, origin, attenuation);
        return filter;
    }

    /// <summary><c>CPASAttenuationFilter( origin, soundlevel )</c>.</summary>
    public RecipientFilter PasAttenuation(Vector origin, int soundLevel)
        => PasAttenuation(origin, SoundLevelToAttenuation(soundLevel));

    /// <summary>
    /// <c>CPASAttenuationFilter( origin, lookupSound )</c>: the script's
    /// soundlevel decides how far it carries.
    /// </summary>
    public RecipientFilter PasAttenuation(Vector origin, string lookupSound)
        => PasAttenuation(origin, SoundLevelToAttenuation(engine.LookupSoundLevel(lookupSound)));

    /// <summary>
    /// <c>CPASAttenuationFilter::Filter</c>: drops the listed players farther
    /// from <paramref name="origin"/> than 2 * SOUND_NORMAL_CLIP_DIST /
    /// attenuation. Nothing is cropped on a one-player server or for an
    /// attenuation of 0 or less; a slot whose player cannot say where it
    /// listens from is kept, as stock's fall-through kept it.
    /// </summary>
    public void Attenuate(RecipientFilter filter, Vector origin, float attenuation)
    {
        if (listeners.MaxClients == 1)
            return;

        if (attenuation <= 0)
            return;

        float reach = 2 * NormalClipDistance / attenuation;

        for (int i = filter.Count - 1; i >= 0; i--)
        {
            int index = filter.IndexAt(i);
            if (!listeners.TryEars(index, out Vector ears))
                continue;

            if ((ears - origin).Length > reach)
                filter.Remove(index);
        }
    }

    /// <summary>
    /// <c>RemoveRecipientsIfNotCloseCaptioning</c>: a client that reported
    /// closecaption 0 is dropped; one that has not reported is kept
    /// (the rule closecaption_recipient.h held natively: "" and "0" mean opposite things).
    /// </summary>
    public void RemoveIfNotCloseCaptioning(RecipientFilter filter)
    {
        for (int i = filter.Count - 1; i >= 0; i--)
        {
            int index = filter.IndexAt(i);
            if (ShouldRemoveForCaptions(listeners.CloseCaptionSetting(index)))
                filter.Remove(index);
        }
    }

    /// <summary>The closecaption rule (closecaption_recipient.h's, before T4a): empty or missing keeps, atoi == 0 removes.</summary>
    public static bool ShouldRemoveForCaptions(string? value)
        => !string.IsNullOrEmpty(value) && Atoi(value) == 0;

    /// <summary>C's atoi: leading whitespace, a sign, then digits; 0 for none.</summary>
    private static int Atoi(string text)
    {
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;

        bool negative = false;
        if (i < text.Length && (text[i] == '-' || text[i] == '+'))
            negative = text[i++] == '-';

        long value = 0;
        while (i < text.Length && text[i] >= '0' && text[i] <= '9' && value < int.MaxValue)
            value = value * 10 + (text[i++] - '0');

        return (int)(negative ? -value : value);
    }
}
