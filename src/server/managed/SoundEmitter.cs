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
    public float Volume = SoundEmitter.VolNorm;
    public int SoundLevel;
    public int Flags;
    public int Pitch = SoundEmitter.PitchNorm;
    public int SpecialDsp;
    public Vector? Origin;
    public float SoundTime;
    public bool WantDuration;
    public float Duration;
    public bool EmitCloseCaption = true;
    public bool WarnOnMissingCloseCaption;
    public bool WarnOnDirectWaveReference;
    public int SpeakerEntity = -1;
    public short ScriptHandle = SoundEmitter.InvalidHandle;
}

/// <summary>
/// The server's sound emitter, managed (plan_server_native_v2 T4a): stock's
/// <c>CSoundEmitterSystem</c> emit path (SoundEmitterSystem.cpp) and the free
/// functions that fronted it -- <c>EmitSound</c>, <c>EmitSoundByHandle</c>,
/// the two <c>StopSound</c>s and <c>StopSoundByHandle</c>, the two
/// <c>EmitAmbientSound</c>s, <c>EmitCloseCaption</c>, and
/// <c>GameSound_Emit</c> / <c>GameSound_Stop</c> / <c>UTIL_EmitAmbientSound</c>.
/// Which sound plays, from where, to whom and with what caption is decided
/// here; <see cref="ISoundEmitterEngine"/> only carries each call out.
///
/// <para>
/// <b>Kept from sound_emitter.cpp's server arm</b>, which differed from stock
/// on purpose: the speaker's gender comes from the entity at the index
/// whichever hierarchy it is on; the trace line counts the recipients;
/// <c>fromplayer</c> is false (stock's owner walk reached only SDK players,
/// none of which exist here); and the caption goes through
/// <see cref="ICloseCaptions"/>. <b>Dropped</b>: the engine's list of the
/// positions it played at (nothing on the server reads it) and the
/// <c>_DEBUG</c>-only "was not precached" line.
/// </para>
/// </summary>
public sealed class SoundEmitter
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
    private readonly SoundRecipients _recipients;
    private readonly ICloseCaptions _captions;
    private readonly IGameClock _clock;
    private readonly Func<int, string?> _speakerModel;
    private readonly Func<bool> _traceOn;
    private readonly Action<string> _msg;
    private readonly Action<string> _devMsg;

    // WaveTrace's "shown once" set: a wave named directly is reported once
    // per emitter (stock's per-process static).
    private readonly HashSet<string> _directWaves = new(StringComparer.Ordinal);

    /// <param name="speakerModel">
    /// The model name of the entity at an index, or null when there is none:
    /// what <c>GetActorGender</c> reads the speaker's gender from.
    /// </param>
    /// <param name="traceOn"><c>sv_soundemitter_trace</c>.</param>
    /// <param name="msg">The console (<c>Msg</c>).</param>
    /// <param name="devMsg">The developer console (<c>DevMsg</c>).</param>
    public SoundEmitter(ISoundEmitterEngine engine, SoundRecipients recipients, ICloseCaptions captions,
                        IGameClock clock, Func<int, string?> speakerModel, Func<bool> traceOn,
                        Action<string> msg, Action<string> devMsg)
    {
        _engine = engine;
        _recipients = recipients;
        _captions = captions;
        _clock = clock;
        _speakerModel = speakerModel;
        _traceOn = traceOn;
        _msg = msg;
        _devMsg = devMsg;
    }

    /// <summary>The recipient rules this emitter's callers build their filters with.</summary>
    public SoundRecipients Recipients => _recipients;

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
    /// a script or a wave, warned about when it names a wave directly.
    /// </summary>
    public void Emit(RecipientFilter filter, int entIndex, string? soundName, Vector? origin = null,
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
    /// <c>CSoundEmitterSystem::EmitSound</c>: a raw wave goes to the engine
    /// as given; a script is resolved to its handle and emitted by it.
    /// </summary>
    public void Emit(RecipientFilter filter, int entIndex, EmitSoundParams ep)
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
    public void EmitByHandle(RecipientFilter filter, int entIndex, EmitSoundParams ep, ref short handle)
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
    private void EmitCloseCaption(RecipientFilter filter, int entIndex, string wave, EmitSoundParams ep)
    {
        if (!_captions.Wanted)
            return;

        if (!ep.EmitCloseCaption)
            return;

        // A copy, or a filter a sound patch owns would be emptied for good.
        RecipientFilter copy = filter.Copy();
        _recipients.RemoveIfNotCloseCaptioning(copy);
        if (copy.Count <= 0)
            return;

        float duration = ep.WantDuration ? ep.Duration : _engine.SoundDuration(wave);

        EmitCloseCaption(filter, entIndex, ep.SoundName ?? string.Empty, duration, ep.WarnOnMissingCloseCaption);
    }

    /// <summary>
    /// <c>EmitCloseCaption( filter, entindex, fromplayer = false, token,
    /// originlist, duration, warnifmissing )</c>, which
    /// <c>GameSound_EmitCloseCaption</c> fronted: a negative duration is the
    /// wave's, or 2 s without one; the recipients who turned captions off are
    /// dropped from a copy; the speaker's gender rides along.
    /// </summary>
    public void EmitCloseCaption(RecipientFilter filter, int entIndex, string token, float duration,
                                 bool warnIfMissing)
    {
        if (!_captions.Wanted)
            return;

        if (duration < 0.0f)
        {
            string? wav = _engine.WavFileForSound(token, GenderNone);
            duration = wav is not null ? _engine.SoundDuration(wav) : DefaultCaptionDuration;
        }

        RecipientFilter copy = filter.Copy();
        _recipients.RemoveIfNotCloseCaptioning(copy);

        int gender = GenderNone;
        if (_speakerModel(entIndex) is { } model)
            gender = _engine.ActorGender(model);

        int[] slots = new int[copy.Count];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = copy.IndexAt(i);

        _captions.Emit(slots, copy.IsReliable, token, duration, fromPlayer: false, warnIfMissing, gender);
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
    /// <c>UTIL_EmitAmbientSound</c>: a "!SENTENCE" goes by the sentence's
    /// index ("!N") or not at all; anything else is
    /// <see cref="EmitAmbient(int, Vector, string, float, int, int, int, float)"/>.
    /// Answers the sound's duration (0 when nothing played).
    /// </summary>
    public float UtilEmitAmbient(int entIndex, Vector origin, string? sample, float volume, int soundLevel,
                                 int flags, int pitch, float soundTime = 0.0f)
    {
        if (sample is not null && sample.StartsWith('!'))
        {
            int sentence = _engine.SentenceIndexFromName(sample[1..]);
            if (sentence < 0)
                return 0.0f;

            string name = "!" + sentence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _engine.EmitAmbientSound(entIndex, origin, name, volume, soundLevel, flags, pitch, soundTime);
            float duration = _engine.SoundDuration(name);
            Trace($"UTIL_EmitAmbientSound:  Sentence emitted '{name}' (ent {entIndex})\n");
            return duration;
        }

        return EmitAmbient(entIndex, origin, sample, volume, soundLevel, flags, pitch, soundTime);
    }

    /// <summary>
    /// <c>EmitAmbientSound</c> with a soundlevel: a .wav or .mp3 is played as
    /// given, at the caller's level; anything else is a script, at its own.
    /// </summary>
    public float EmitAmbient(int entIndex, Vector origin, string? sample, float volume, int soundLevel, int flags,
                             int pitch, float soundTime = 0.0f)
    {
        if (sample is not null && IsWaveFile(sample))
        {
            _engine.EmitAmbientSound(entIndex, origin, sample, volume, soundLevel, flags, pitch, soundTime);
            float duration = _engine.SoundDuration(sample);
            Trace($"EmitAmbientSound:  Raw wave emitted '{sample}' (ent {entIndex})\n");
            return duration;
        }

        return EmitAmbientScript(entIndex, origin, sample ?? string.Empty, volume, flags, pitch, soundTime);
    }

    /// <summary>
    /// The script overload: its parameters with no gender, the caller's pitch
    /// and volume when the flags say so, the engine's ambient emit, and a
    /// caption to every player (reliably) at the start of the sound -- not on
    /// a change or a stop.
    /// </summary>
    private float EmitAmbientScript(int entIndex, Vector origin, string soundName, float volume, int flags, int pitch,
                                    float soundTime)
    {
        var parameters = SoundParametersLayout.Stock();
        if (!_engine.ParametersForSound(soundName, ref parameters, GenderNone))
            return 0.0f;

        if ((flags & SndChangePitch) != 0)
            parameters.Pitch = pitch;

        if ((flags & SndChangeVol) != 0)
            parameters.Volume = volume;

        string wave = parameters.Name;
        _engine.EmitAmbientSound(entIndex, origin, wave, parameters.Volume, parameters.SoundLevel, flags,
                                 parameters.Pitch, soundTime);

        bool needsCaption = (flags & (SndStop | SndChangeVol | SndChangePitch)) == 0;
        float duration = _engine.SoundDuration(wave);

        Trace($"EmitAmbientSound:  '{soundName}' emitted as '{wave}' (ent {entIndex})\n");

        if (needsCaption)
            EmitCloseCaption(_recipients.AllPlayers().MakeReliable(), entIndex, soundName, duration, false);

        return duration;
    }

    //-------------------------------------------------------------------------
    // GameSound_GetParameters
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>GameSound_GetParameters( name, params, NULL )</c>: the script's
    /// parameters with no actor's gender, or null.
    /// </summary>
    public SoundParametersLayout? Parameters(string soundName)
    {
        var parameters = SoundParametersLayout.Stock();
        return _engine.ParametersForSound(soundName, ref parameters, _engine.ActorGender(null))
            ? parameters
            : null;
    }

    //-------------------------------------------------------------------------
    // Diagnostics
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>TraceEmitSound</c>: under <c>sv_soundemitter_trace</c>, "(sv) "
    /// and the line, as stock printed it from a 256-byte buffer.
    /// tools/7e-particle-sound-gate parses these lines.
    /// </summary>
    private void Trace(string line)
    {
        if (!_traceOn())
            return;

        _msg("(sv) " + (line.Length > 255 ? line[..255] : line));
    }

    /// <summary><c>WaveTrace</c>: once per wave, on the developer console.</summary>
    private void WaveTrace(string wave, string function)
    {
        if (_directWaves.Add(wave))
            _devMsg($"{function} directly referenced wave {wave} (should use game_sounds.txt system instead)\n");
    }
}
