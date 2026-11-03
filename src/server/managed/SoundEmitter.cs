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
/// The server's sound emitter (plan_server_native_v2 T4a): the shared
/// <see cref="SoundEmitterCore"/> -- stock's <c>CSoundEmitterSystem</c> emit
/// path, which both DLLs compiled -- with the server's arms of it.
///
/// <para>
/// <b>The server's arms</b>: the trace says "(sv)"; captions are wanted by
/// <see cref="ICloseCaptions"/>'s rule, go to the recipients still asking
/// for them (<see cref="SoundRecipients.RemoveIfNotCloseCaptioning"/>) as the
/// CloseCaption user message with the speaker's gender, and are never a
/// player's (stock's owner walk reached only SDK players, none of which exist
/// here); "every player" is every connected slot.
/// </para>
/// </summary>
public sealed class SoundEmitter : SoundEmitterCore
{
    private readonly SoundRecipients _recipients;
    private readonly ICloseCaptions _captions;
    private readonly Func<IReadOnlyList<ISoundEmitHook>>? _hooks;
    private readonly Func<int, Vector?>? _sourceOrigin;
    private readonly Action<string> _warn;

    /// <param name="speakerModel">
    /// The model name of the entity at an index, or null when there is none:
    /// what <c>GetActorGender</c> reads the speaker's gender from.
    /// </param>
    /// <param name="traceOn"><c>sv_soundemitter_trace</c>.</param>
    /// <param name="msg">The console (<c>Msg</c>).</param>
    /// <param name="devMsg">The developer console (<c>DevMsg</c>).</param>
    /// <param name="hooks">The registered <see cref="ISoundEmitHook"/>s, asked when a sound goes out.</param>
    /// <param name="sourceOrigin">Where the entity at an index is, for a sound that names no point; null for none.</param>
    public SoundEmitter(ISoundEmitterEngine engine, SoundRecipients recipients, ICloseCaptions captions,
                        IGameClock clock, Func<int, string?> speakerModel, Func<bool> traceOn,
                        Action<string> msg, Action<string> devMsg,
                        Func<IReadOnlyList<ISoundEmitHook>>? hooks = null, Func<int, Vector?>? sourceOrigin = null)
        : base(engine, clock, speakerModel, traceOn, msg, devMsg)
    {
        _recipients = recipients;
        _captions = captions;
        _hooks = hooks;
        _sourceOrigin = sourceOrigin;
        _warn = msg;
    }

    /// <summary>Hands every <see cref="ISoundEmitHook"/> the sound with its final recipients (plan_hl2mp_ai §4.2).</summary>
    protected override void Emitted(IRecipientList filter, int entIndex, string soundName, int soundLevel, Vector? origin)
    {
        if (_hooks?.Invoke() is not { Count: > 0 } hooks)
            return;

        Span<int> slots = filter.Count <= 128 ? stackalloc int[filter.Count] : new int[filter.Count];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = filter.IndexAt(i);
        Vector at = origin ?? _sourceOrigin?.Invoke(entIndex) ?? Vector.Zero;
        var sound = new EmittedSound(at, soundLevel, soundName, entIndex, slots);
        foreach (ISoundEmitHook hook in hooks)
        {
            try
            {
                hook.OnEmitted(in sound);
            }
            catch (Exception e)
            {
                _warn($"[SourceSharp] ISoundEmitHook.OnEmitted on {hook.GetType().FullName} threw: {e}\n");
            }
        }
    }

    /// <summary>The recipient rules this emitter's callers build their filters with.</summary>
    public SoundRecipients Recipients => _recipients;

    protected override string TracePrefix => "(sv)";

    protected override bool CaptionsWanted => _captions.Wanted;

    protected override bool AnyoneCaptioned(IRecipientList filter)
    {
        RecipientFilter copy = CopyOf(filter);
        _recipients.RemoveIfNotCloseCaptioning(copy);
        return copy.Count > 0;
    }

    protected override bool FromPlayer(int entIndex) => false;

    protected override void DeliverCaption(IRecipientList filter, int entIndex, string token, float duration,
                                           bool fromPlayer, bool warnIfMissing)
    {
        RecipientFilter copy = CopyOf(filter);
        _recipients.RemoveIfNotCloseCaptioning(copy);

        int gender = GenderNone;
        if (SpeakerModel(entIndex) is { } model)
            gender = Engine.ActorGender(model);

        int[] slots = new int[copy.Count];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = copy.IndexAt(i);

        _captions.Emit(slots, copy.IsReliable, token, duration, fromPlayer, warnIfMissing, gender);
    }

    protected override IRecipientList AllPlayersReliable() => _recipients.AllPlayers().MakeReliable();

    // The server's filters are RecipientFilters; anything else is read into one.
    private RecipientFilter CopyOf(IRecipientList filter)
    {
        if (filter is RecipientFilter own)
            return own.Copy();

        RecipientFilter copy = _recipients.Empty();
        for (int i = 0; i < filter.Count; i++)
            copy.Add(filter.IndexAt(i));
        if (filter.IsReliable)
            copy.MakeReliable();
        return copy;
    }
}
