//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>EmitSound_t</c> (shareddefs.h): one emit's parameters, with stock's
/// constructor's defaults. <see cref="Duration"/> is written when
/// <see cref="WantDuration"/> asks for it (stock's <c>m_pflSoundDuration</c>),
/// and <see cref="ScriptHandle"/> is filled in by the first emit that looks
/// the script up (stock's mutable <c>m_hSoundScriptHandle</c>).
/// </summary>
public sealed class EmitSoundParams
{
    public int Channel;
    public string? SoundName;
    public float Volume = SoundEmitterCore.VolNorm;
    public int SoundLevel;
    public int Flags;
    public int Pitch = SoundEmitterCore.PitchNorm;
    public int SpecialDsp;
    public Vector? Origin;
    public float SoundTime;
    public bool WantDuration;
    public float Duration;
    public bool EmitCloseCaption = true;
    public bool WarnOnMissingCloseCaption;
    public bool WarnOnDirectWaveReference;
    public int SpeakerEntity = -1;
    public short ScriptHandle = SoundEmitterCore.InvalidHandle;
}

/// <summary>
/// The sound emitter both sides share: stock's <c>CSoundEmitterSystem</c>
/// emit path (game/shared/SoundEmitterSystem.cpp, which both DLLs compiled)
/// and the free functions that fronted it -- <c>EmitSound</c>,
/// <c>EmitSoundByHandle</c>, the two <c>StopSound</c>s and
/// <c>StopSoundByHandle</c>, the two <c>EmitAmbientSound</c>s, both
/// <c>EmitCloseCaption</c>s, and <c>GameSound_Emit</c> / <c>GameSound_Stop</c>
/// / <c>UTIL_EmitAmbientSound</c> / <c>GameSound_GetParameters</c>. Which
/// sound plays, from where, to whom and with what caption is decided here;
/// <see cref="ISoundEmitterEngine"/> only carries each call out.
///
/// <para>
/// What the two DLLs compiled differently is the side's (stock's
/// <c>CLIENT_DLL</c> arms): the trace's prefix, whether captions are wanted
/// and who may read them, the owner walk that marks a caption as a player's,
/// where a caption goes (a user message, the HUD), and who "every player" is.
/// The server's <c>CEnvMicrophone</c> hook and its <c>_DEBUG</c>
/// "was not precached" line are not here (docs/todo.md).
/// </para>
///
/// <para>
/// <b>Kept from sound_emitter.cpp</b>, which differed from stock on purpose:
/// the speaker's gender comes from the entity at the index whichever
/// hierarchy it is on; the script trace counts the recipients. <b>Dropped</b>:
/// the engine's list of the positions it played at, which nothing on either
/// side reads.
/// </para>
/// </summary>
public abstract class SoundEmitterCore
{
    /// <summary>VOL_NORM, PITCH_NORM (soundflags.h).</summary>
    public const float VolNorm = 1.0f;
    public const int PitchNorm = 100;

    /// <summary>SND_CHANGE_VOL, SND_CHANGE_PITCH, SND_STOP (soundflags.h).</summary>
    public const int SndChangeVol = 1 << 0, SndChangePitch = 1 << 1, SndStop = 1 << 2;

    /// <summary>SOUNDEMITTER_INVALID_HANDLE.</summary>
    public const short InvalidHandle = -1;

    /// <summary>GENDER_NONE.</summary>
    public const int GenderNone = 0;

    /// <summary>SOUND_FROM_WORLD (soundflags.h).</summary>
    public const int SoundFromWorld = 0;

    /// <summary>What stock's EmitCloseCaption falls back to when a caption's wave has no duration to read.</summary>
    public const float DefaultCaptionDuration = 2.0f;

    private readonly ISoundEmitterEngine _engine;
    private readonly IGameClock _clock;
    private readonly Func<int, string?> _speakerModel;
    private readonly Func<bool> _traceOn;
    private readonly Action<string> _msg;
    private readonly Action<string> _devMsg;

    // WaveTrace's "shown once" set: a wave named directly is reported once
    // per emitter (stock's per-process static).
    private readonly HashSet<string> _directWaves = new(StringComparer.Ordinal);

    /// <param name="engine">Every engine call the emitter makes.</param>
    /// <param name="clock"><c>gpGlobals->curtime</c>, which a script's delay counts from.</param>
    /// <param name="speakerModel">
    /// The model name of the entity at an index ("" for one with no model),
    /// or null when there is none: what <c>GetActorGender</c> reads the
    /// speaker's gender from.
    /// </param>
    /// <param name="traceOn"><c>sv_soundemitter_trace</c>.</param>
    /// <param name="msg">The console (<c>Msg</c>).</param>
    /// <param name="devMsg">The developer console (<c>DevMsg</c>).</param>
    protected SoundEmitterCore(ISoundEmitterEngine engine, IGameClock clock, Func<int, string?> speakerModel,
                               Func<bool> traceOn, Action<string> msg, Action<string> devMsg)
    {
        _engine = engine;
        _clock = clock;
        _speakerModel = speakerModel;
        _traceOn = traceOn;
        _msg = msg;
        _devMsg = devMsg;
    }

    //-------------------------------------------------------------------------
    // The side's arms
    //-------------------------------------------------------------------------

    /// <summary><c>CBaseEntity::IsServer() ? "(sv)" : "(cl)"</c>.</summary>
    protected abstract string TracePrefix { get; }

    /// <summary>
    /// Whether a caption would be shown at all just now: both
    /// <c>EmitCloseCaption</c>s return first when it is not, before any
    /// caption's facts are looked up.
    /// </summary>
    protected abstract bool CaptionsWanted { get; }

    /// <summary>
    /// The params overload's question: does anyone in a copy of
    /// <paramref name="filter"/> still want captions once those who turned
    /// them off are removed? The filter itself is never changed.
    /// </summary>
    protected abstract bool AnyoneCaptioned(IRecipientList filter);

    /// <summary>
    /// Stock's owner walk: is the entity at <paramref name="entIndex"/>, or
    /// anything up its owner chain, a player? It sets the caption's
    /// <c>fromplayer</c>.
    /// </summary>
    protected abstract bool FromPlayer(int entIndex);

    /// <summary>
    /// The token overload's tail, once the duration is known: the side's
    /// copy of the filter, its removals, and the caption sent (a user
    /// message) or shown (the HUD).
    /// </summary>
    protected abstract void DeliverCaption(IRecipientList filter, int entIndex, string token, float duration,
                                           bool fromPlayer, bool warnIfMissing);

    /// <summary><c>CRecipientFilter filter; filter.AddAllPlayers(); filter.MakeReliable();</c></summary>
    protected abstract IRecipientList AllPlayersReliable();

    /// <summary>The engine calls, for a side's own arms.</summary>
    protected ISoundEmitterEngine Engine => _engine;

    /// <summary>The model of the speaker at an index, as the gender is read from it.</summary>
    protected string? SpeakerModel(int entIndex) => _speakerModel(entIndex);

    /// <summary>A name the engine plays as it is: a .wav, a .mp3 (either case) or a "!sentence".</summary>
    public static bool IsRawWave(string name)
        => name.Contains(".wav", StringComparison.OrdinalIgnoreCase)
           || name.Contains(".mp3", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith('!');

    private static bool IsWaveFile(string name)
        => name.Contains(".wav", StringComparison.OrdinalIgnoreCase)
           || name.Contains(".mp3", StringComparison.OrdinalIgnoreCase);

    //-------------------------------------------------------------------------
    // GameSound_Emit
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>GameSound_Emit( filter, entindex, soundname, origin, soundtime )</c>:
    /// stock goes by the HANDLE overload here, so the name is always looked up
    /// as a script -- a .wav named this way plays nothing.
    /// </summary>
    public void Emit(IRecipientList filter, int entIndex, string? soundName, Vector? origin = null,
                     float soundTime = 0.0f)
    {
        if (soundName is null)
            return;

        var ep = new EmitSoundParams
        {
            SoundName = soundName,
            SoundTime = soundTime,
            Origin = origin,
            WarnOnDirectWaveReference = true,
        };
        short handle = ep.ScriptHandle;
        EmitByHandle(filter, entIndex, ep, ref handle);
        ep.ScriptHandle = handle;
    }

    /// <summary>
    /// <c>GameSound_Emit( filter, entindex, soundname, handle, origin,
    /// soundtime )</c>: the same, with the caller's handle, which the first
    /// lookup fills in.
    /// </summary>
    public void EmitByHandle(IRecipientList filter, int entIndex, string soundName, ref short handle,
                             Vector? origin = null, float soundTime = 0.0f)
    {
        var ep = new EmitSoundParams
        {
            SoundName = soundName,
            SoundTime = soundTime,
            Origin = origin,
            WarnOnDirectWaveReference = true,
        };
        EmitByHandle(filter, entIndex, ep, ref handle);
    }

    /// <summary>
    /// <c>CSoundEmitterSystem::EmitSound</c>: a raw wave goes to the engine
    /// as given; a script is resolved to its handle and emitted by it.
    /// </summary>
    public void Emit(IRecipientList filter, int entIndex, EmitSoundParams ep)
    {
        if (ep.SoundName is { } name && IsRawWave(name))
        {
            if (ep.WarnOnDirectWaveReference && name.Contains(".wav", StringComparison.OrdinalIgnoreCase))
                WaveTrace(name, "Emitsound");

            _engine.EmitSound(filter, entIndex, ep.Channel, name, ep.Volume, ep.SoundLevel, ep.Flags, ep.Pitch,
                              ep.SpecialDsp, ep.Origin, ep.SoundTime, ep.SpeakerEntity);
            if (ep.WantDuration)
                ep.Duration = _engine.SoundDuration(name);

            Trace($"EmitSound:  Raw wave emitted '{name}' (ent {entIndex})\n");
            return;
        }

        if (ep.ScriptHandle == InvalidHandle)
            ep.ScriptHandle = (short)_engine.SoundIndex(ep.SoundName ?? string.Empty);

        if (ep.ScriptHandle == -1)
            return;

        short handle = ep.ScriptHandle;
        EmitByHandle(filter, entIndex, ep, ref handle);
        ep.ScriptHandle = handle;
    }

    /// <summary>
    /// <c>CSoundEmitterSystem::EmitSoundByHandle</c>: the script's
    /// parameters for the speaker's gender, the caller's pitch and volume
    /// when its flags say so, the script's delay as a start time, the emit,
    /// the trace, and the caption -- not for a change of pitch or volume.
    /// </summary>
    public void EmitByHandle(IRecipientList filter, int entIndex, EmitSoundParams ep, ref short handle)
    {
        int gender = GenderNone;
        if (_speakerModel(entIndex) is { } model)
            gender = _engine.ActorGender(model);

        var parameters = SoundParametersLayout.Stock();
        if (!_engine.ParametersForSoundEx(ep.SoundName ?? string.Empty, ref handle, ref parameters, gender, true))
            return;

        string wave = parameters.Name;
        if (wave.Length == 0)
            return;

        if ((ep.Flags & SndChangePitch) != 0)
            parameters.Pitch = ep.Pitch;

        if ((ep.Flags & SndChangeVol) != 0)
            parameters.Volume = ep.Volume;

        float st = ep.SoundTime;
        if (st == 0 && parameters.DelayMsec != 0)
            st = _clock.Now + parameters.DelayMsec / 1000.0f;

        _engine.EmitSound(filter, entIndex, parameters.Channel, wave, parameters.Volume, parameters.SoundLevel,
                          ep.Flags, parameters.Pitch, ep.SpecialDsp, ep.Origin, st, ep.SpeakerEntity);
        if (ep.WantDuration)
            ep.Duration = _engine.SoundDuration(wave);

        // The recipient count is in the trace (plan_entity.md step 4b): an
        // empty filter emits as readily as a full one, and is the failure.
        Trace($"EmitSound:  '{ep.SoundName}' emitted as '{wave}' (ent {entIndex}, {filter.Count} recipients)\n");

        // Don't caption modulations to the sound.
        if ((ep.Flags & (SndChangePitch | SndChangeVol)) == 0)
            EmitCloseCaption(filter, entIndex, wave, ep);
    }

    //-------------------------------------------------------------------------
    // Close captions
    //-------------------------------------------------------------------------

    /// <summary><c>EmitCloseCaption( filter, entindex, params, ep )</c>.</summary>
    private void EmitCloseCaption(IRecipientList filter, int entIndex, string wave, EmitSoundParams ep)
    {
        if (!CaptionsWanted)
            return;

        if (!ep.EmitCloseCaption)
            return;

        // A copy, or a filter a sound patch owns would be emptied for good.
        if (!AnyoneCaptioned(filter))
            return;

        float duration = ep.WantDuration ? ep.Duration : _engine.SoundDuration(wave);

        EmitCloseCaption(filter, entIndex, FromPlayer(entIndex), ep.SoundName ?? string.Empty, duration,
                         ep.WarnOnMissingCloseCaption);
    }

    /// <summary>
    /// <c>GameSound_EmitCloseCaption( filter, entindex, token, origins,
    /// duration, warnifmissing )</c>: the owner walk, then the system's
    /// token overload.
    /// </summary>
    public void EmitCloseCaption(IRecipientList filter, int entIndex, string token, float duration,
                                 bool warnIfMissing)
        => EmitCloseCaption(filter, entIndex, FromPlayer(entIndex), token, duration, warnIfMissing);

    /// <summary>
    /// <c>EmitCloseCaption( filter, entindex, fromplayer, token, originlist,
    /// duration, warnifmissing )</c>: a negative duration is the wave's, or
    /// 2 s without one; the rest is the side's.
    /// </summary>
    private void EmitCloseCaption(IRecipientList filter, int entIndex, bool fromPlayer, string token, float duration,
                                  bool warnIfMissing)
    {
        if (!CaptionsWanted)
            return;

        if (duration < 0.0f)
        {
            string? wav = _engine.WavFileForSound(token, GenderNone);
            duration = wav is not null ? _engine.SoundDuration(wav) : DefaultCaptionDuration;
        }

        DeliverCaption(filter, entIndex, token, duration, fromPlayer, warnIfMissing);
    }

    //-------------------------------------------------------------------------
    // GameSound_Stop
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>GameSound_Stop( entindex, channel, sample )</c>: a raw wave is
    /// stopped as named; anything else is looked up as a script and every
    /// wave of it stopped.
    /// </summary>
    public void Stop(int entIndex, int channel, string? sample)
    {
        if (sample is not null && IsRawWave(sample))
        {
            _engine.StopSound(entIndex, channel, sample);
            Trace($"StopSound:  Raw wave stopped '{sample}' (ent {entIndex})\n");
            return;
        }

        Stop(entIndex, sample ?? string.Empty);
    }

    /// <summary><c>GameSound_Stop( entindex, soundname )</c>: the script's handle, then every wave.</summary>
    public void Stop(int entIndex, string soundName)
    {
        short handle = (short)_engine.SoundIndex(soundName);
        if (handle == InvalidHandle)
            return;

        StopByHandle(entIndex, soundName, ref handle);
    }

    /// <summary>
    /// <c>StopSoundByHandle</c>: stock's HACK, stop every wave in the
    /// script's rndwave list on the script's channel, since any of them may be
    /// the one playing.
    /// </summary>
    public void StopByHandle(int entIndex, string soundName, ref short handle)
    {
        if (handle == InvalidHandle)
            handle = (short)_engine.SoundIndex(soundName);

        if (handle == InvalidHandle)
            return;

        if (_engine.Entry(handle) is not { } entry)
            return;

        foreach (string wave in entry.Waves)
        {
            _engine.StopSound(entIndex, entry.Channel, wave);
            Trace($"StopSound:  '{soundName}' stopped as '{wave}' (ent {entIndex})\n");
        }
    }

    //-------------------------------------------------------------------------
    // Ambient sounds
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>UTIL_EmitAmbientSound</c>, answering the sound's duration (0 when
    /// nothing played). Every duration is looked up, as when a caller always
    /// passes stock's <c>duration</c> pointer.
    /// </summary>
    public float UtilEmitAmbient(int entIndex, Vector origin, string? sample, float volume, int soundLevel,
                                 int flags, int pitch, float soundTime = 0.0f)
        => UtilEmitAmbient(entIndex, origin, sample, volume, soundLevel, flags, pitch, soundTime, true,
                           out float duration)
            ? duration
            : 0.0f;

    /// <summary>
    /// <c>UTIL_EmitAmbientSound</c>: a "!SENTENCE" goes by the sentence's
    /// index ("!N") or not at all; anything else is
    /// <see cref="EmitAmbient(int, Vector, string, float, int, int, int, float, bool, out float)"/>.
    /// <paramref name="wantDuration"/> is stock's non-null <c>duration</c>;
    /// true when <paramref name="duration"/> was written.
    /// </summary>
    public bool UtilEmitAmbient(int entIndex, Vector origin, string? sample, float volume, int soundLevel,
                                int flags, int pitch, float soundTime, bool wantDuration, out float duration)
    {
        duration = 0.0f;
        if (sample is not null && sample.StartsWith('!'))
        {
            int sentence = _engine.SentenceIndexFromName(sample[1..]);
            if (sentence < 0)
                return false;

            string name = "!" + sentence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _engine.EmitAmbientSound(entIndex, origin, name, volume, soundLevel, flags, pitch, soundTime);
            bool written = false;
            if (wantDuration)
            {
                duration = _engine.SoundDuration(name);
                written = true;
            }

            Trace($"UTIL_EmitAmbientSound:  Sentence emitted '{name}' (ent {entIndex})\n");
            return written;
        }

        return EmitAmbient(entIndex, origin, sample, volume, soundLevel, flags, pitch, soundTime, wantDuration,
                           out duration);
    }

    /// <summary>
    /// <c>EmitAmbientSound</c> with a soundlevel, answering the sound's
    /// duration (0 when nothing played).
    /// </summary>
    public float EmitAmbient(int entIndex, Vector origin, string? sample, float volume, int soundLevel, int flags,
                             int pitch, float soundTime = 0.0f)
        => EmitAmbient(entIndex, origin, sample, volume, soundLevel, flags, pitch, soundTime, true, out float duration)
            ? duration
            : 0.0f;

    /// <summary>
    /// <c>EmitAmbientSound</c> with a soundlevel: a .wav or .mp3 is played as
    /// given, at the caller's level; anything else is a script, at its own.
    /// </summary>
    public bool EmitAmbient(int entIndex, Vector origin, string? sample, float volume, int soundLevel, int flags,
                            int pitch, float soundTime, bool wantDuration, out float duration)
    {
        duration = 0.0f;
        if (sample is not null && IsWaveFile(sample))
        {
            _engine.EmitAmbientSound(entIndex, origin, sample, volume, soundLevel, flags, pitch, soundTime);
            bool written = false;
            if (wantDuration)
            {
                duration = _engine.SoundDuration(sample);
                written = true;
            }

            Trace($"EmitAmbientSound:  Raw wave emitted '{sample}' (ent {entIndex})\n");
            return written;
        }

        return EmitAmbientScript(entIndex, origin, sample ?? string.Empty, volume, flags, pitch, soundTime,
                                 wantDuration, out duration);
    }

    /// <summary>
    /// The script overload: its parameters with no gender, the caller's pitch
    /// and volume when the flags say so, the side's ambient emit, and a
    /// caption to every player (reliably) at the start of the sound -- not on
    /// a change or a stop.
    /// </summary>
    private bool EmitAmbientScript(int entIndex, Vector origin, string soundName, float volume, int flags, int pitch,
                                   float soundTime, bool wantDuration, out float duration)
    {
        duration = 0.0f;
        var parameters = SoundParametersLayout.Stock();
        if (!_engine.ParametersForSound(soundName, ref parameters, GenderNone))
            return false;

        if ((flags & SndChangePitch) != 0)
            parameters.Pitch = pitch;

        if ((flags & SndChangeVol) != 0)
            parameters.Volume = volume;

        string wave = parameters.Name;
        _engine.EmitAmbientSound(entIndex, origin, wave, parameters.Volume, parameters.SoundLevel, flags,
                                 parameters.Pitch, soundTime);

        bool needsCaption = (flags & (SndStop | SndChangeVol | SndChangePitch)) == 0;

        float soundDuration = 0.0f;
        if (wantDuration || needsCaption)
        {
            soundDuration = _engine.SoundDuration(wave);
            if (wantDuration)
                duration = soundDuration;
        }

        Trace($"EmitAmbientSound:  '{soundName}' emitted as '{wave}' (ent {entIndex})\n");

        if (needsCaption)
            EmitCloseCaption(AllPlayersReliable(), entIndex, false, soundName, soundDuration, false);

        return wantDuration;
    }

    //-------------------------------------------------------------------------
    // GameSound_GetParameters
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>GameSound_GetParameters( name, params, actormodel )</c>: the
    /// script's parameters for the actor's gender, or null.
    /// </summary>
    public SoundParametersLayout? Parameters(string soundName, string? actorModel = null)
    {
        var parameters = SoundParametersLayout.Stock();
        return _engine.ParametersForSound(soundName, ref parameters, _engine.ActorGender(actorModel))
            ? parameters
            : null;
    }

    //-------------------------------------------------------------------------
    // Diagnostics
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>TraceEmitSound</c>: under <c>sv_soundemitter_trace</c>, the side's
    /// prefix and the line, as stock printed it from a 256-byte buffer.
    /// tools/7e-particle-sound-gate parses these lines.
    /// </summary>
    protected void Trace(string line)
    {
        if (!_traceOn())
            return;

        _msg(TracePrefix + " " + (line.Length > 255 ? line[..255] : line));
    }

    /// <summary><c>WaveTrace</c>: once per wave, on the developer console.</summary>
    private void WaveTrace(string wave, string function)
    {
        if (_directWaves.Add(wave))
            _devMsg($"{function} directly referenced wave {wave} (should use game_sounds.txt system instead)\n");
    }
}
