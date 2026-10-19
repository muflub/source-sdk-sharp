//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Sounds;

/// <summary>
/// What the client's sound emitter reads of the client: the facts stock's
/// <c>CLIENT_DLL</c> arms of SoundEmitterSystem.cpp asked of globals, ConVars,
/// the engine and the entity list.
/// </summary>
public interface IClientSoundContext
{
    /// <summary><c>gpGlobals->maxClients</c>.</summary>
    int MaxClients { get; }

    /// <summary><c>closecaption</c>: the ConVar the engine resolves by name.</summary>
    bool CloseCaptionOn { get; }

    /// <summary><c>engine->GetLocalPlayer()</c>: the viewer's slot, below 1 when there is none.</summary>
    int LocalPlayerSlot { get; }

    /// <summary><c>sv_soundemitter_trace</c>, which the client declares too (FCVAR_REPLICATED).</summary>
    bool TraceOn { get; }

    /// <summary>
    /// The model name of the entity at an index ("" for one with no model),
    /// or null when no entity is there: what <c>GetActorGender</c> reads.
    /// </summary>
    string? SpeakerModel(int entIndex);

    /// <summary>Stock's owner walk: is the entity at the index, or anything it is owned by, a player?</summary>
    bool FromPlayer(int entIndex);
}

/// <summary>
/// The HUD's close-caption element, which stock's client dispatched a
/// caption to directly (<c>GET_HUDELEMENT( CHudCloseCaption )</c>).
/// </summary>
public interface ICloseCaptionHud
{
    /// <summary>Whether the element exists: <c>GET_HUDELEMENT</c> answered non-NULL.</summary>
    bool Present { get; }

    /// <summary><c>CHudCloseCaption::ProcessCaption( token, duration, fromplayer )</c>.</summary>
    void ProcessCaption(string token, float duration, bool fromPlayer);
}

/// <summary>
/// The caption element this client has: none. The HUD elements left with the
/// rest of the stock HUD and nothing managed shows captions yet, so stock's
/// <c>GET_HUDELEMENT</c> answers NULL and a caption goes nowhere -- as it did
/// from sound_emitter.cpp, whose lookup found nothing either.
/// </summary>
public sealed class NoCloseCaptionHud : ICloseCaptionHud
{
    public bool Present => false;

    public void ProcessCaption(string token, float duration, bool fromPlayer)
    {
    }
}

/// <summary>
/// A client recipient filter (stock's <c>C_RecipientFilter</c>): player
/// slots and the reliable flag. A client filter only ever holds the viewer.
/// </summary>
public sealed class ClientRecipients : IRecipientList
{
    private readonly List<int> _slots = [];

    public bool IsReliable { get; private set; }

    public bool IsInitMessage { get; private set; }

    public int Count => _slots.Count;

    public int IndexAt(int slot) => slot < 0 || slot >= _slots.Count ? -1 : _slots[slot];

    /// <summary>
    /// <c>CLocalPlayerFilter</c>: the viewer, when there is one
    /// (local_recipient.h's rule, with no prediction rules in force).
    /// </summary>
    public static ClientRecipients LocalPlayer(int localSlot)
    {
        var filter = new ClientRecipients();
        if (localSlot >= 1)
            filter._slots.Add(localSlot);
        return filter;
    }

    /// <summary>A filter native built and handed over: its slots, its flags.</summary>
    public static ClientRecipients Of(ReadOnlySpan<int> slots, bool reliable, bool initMessage)
    {
        var filter = new ClientRecipients { IsReliable = reliable, IsInitMessage = initMessage };
        foreach (int slot in slots)
            filter._slots.Add(slot);
        return filter;
    }

    /// <summary><c>MakeReliable()</c>.</summary>
    public ClientRecipients MakeReliable()
    {
        IsReliable = true;
        return this;
    }
}

/// <summary>
/// The client's sound emitter: the shared <see cref="SoundEmitterCore"/> --
/// stock's <c>CSoundEmitterSystem</c> emit path, which both DLLs compiled --
/// with the client's arms of it, as SoundEmitterSystem.cpp compiled them under
/// <c>CLIENT_DLL</c> and sound_emitter.cpp ran them until this port.
///
/// <para>
/// <b>The client's arms</b>: the trace says "(cl)"; captions are for a game
/// of one player with <c>closecaption</c> on; a filter's copy keeps its
/// players only while <c>closecaption</c> is on (stock's
/// <c>RemoveRecipientsIfNotCloseCaptioning</c>); the token is lowered,
/// truncated to 255 characters and its "\n" escapes made newlines
/// (<c>Hack_FixEscapeChars</c>); the caption goes straight to the HUD's
/// element; "every player" is the viewer.
/// </para>
/// </summary>
public sealed class ClientSoundEmitter : SoundEmitterCore
{
    private readonly IClientSoundContext _context;
    private readonly ICloseCaptionHud _hud;

    public ClientSoundEmitter(ISoundEmitterEngine engine, IGameClock clock, IClientSoundContext context,
                              ICloseCaptionHud hud, Action<string> msg, Action<string> devMsg)
        : base(engine, clock, (context ?? throw new ArgumentNullException(nameof(context))).SpeakerModel,
               () => context.TraceOn, msg, devMsg)
    {
        _context = context;
        _hud = hud;
    }

    /// <summary>A filter of the viewer alone: <c>CLocalPlayerFilter</c>.</summary>
    public ClientRecipients LocalPlayer() => ClientRecipients.LocalPlayer(_context.LocalPlayerSlot);

    protected override string TracePrefix => "(cl)";

    // "No close captions in multiplayer..."
    protected override bool CaptionsWanted
    {
        get
        {
            int maxClients = _context.MaxClients;
            return !(maxClients > 1 || (maxClients == 1 && !_context.CloseCaptionOn));
        }
    }

    protected override bool AnyoneCaptioned(IRecipientList filter) => _context.CloseCaptionOn && filter.Count > 0;

    protected override bool FromPlayer(int entIndex) => _context.FromPlayer(entIndex);

    protected override void DeliverCaption(IRecipientList filter, int entIndex, string token, float duration,
                                           bool fromPlayer, bool warnIfMissing)
    {
        // char lowercase[ 256 ]: Q_strncpy keeps 255 characters, then Q_strlower.
        string lowercase = SoundScriptFiles.AsciiLower(token.Length > 255 ? token[..255] : token);
        if (lowercase.Contains('\\'))
            lowercase = FixEscapeChars(lowercase);

        // The copy, and the players who don't want captions removed: on the
        // client that is everyone, when closecaption is off. Anyone left?
        if (!_context.CloseCaptionOn || filter.Count <= 0)
            return;

        // Direct dispatch.
        if (_hud.Present)
            _hud.ProcessCaption(lowercase, duration, fromPlayer);
    }

    protected override IRecipientList AllPlayersReliable() => LocalPlayer().MakeReliable();

    /// <summary>
    /// <c>Hack_FixEscapeChars</c>: the two characters "\n" become a newline;
    /// every other backslash stays as it is.
    /// </summary>
    internal static string FixEscapeChars(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == 'n')
            {
                builder.Append('\n');
                i++;
            }
            else
            {
                builder.Append(text[i]);
            }
        }

        return builder.ToString();
    }
}
