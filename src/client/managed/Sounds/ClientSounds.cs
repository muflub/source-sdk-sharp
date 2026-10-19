//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Sounds;

/// <summary>
/// A sound from an entity, to the local player: what an animation event and
/// the predicted move helper play (they called sound_emitter.cpp's
/// GameSound_Emit through the shim's ClientEmitSound).
/// </summary>
public interface IClientEntitySounds
{
    /// <summary>
    /// <c>GameSound_Emit( CLocalPlayerFilter, entindex, soundname[, origin] )</c>
    /// from <paramref name="entity"/>'s index, or from the world when the
    /// handle names nothing with one.
    /// </summary>
    void EmitFrom(EntityHandle entity, string soundName, Vector? origin = null);
}

/// <summary>
/// What the client's sound emitter reads of the running client
/// (<see cref="IClientSoundContext"/>): the globals, closecaption, the
/// viewer, the trace ConVar and the speakers' models.
/// </summary>
public sealed class ClientSoundContext(Func<int> maxClients, Func<bool> closeCaption, Func<int> localPlayerSlot,
                                       Func<bool> traceOn, Func<int, string?> speakerModel) : IClientSoundContext
{
    public int MaxClients => maxClients();

    public bool CloseCaptionOn => closeCaption();

    public int LocalPlayerSlot => localPlayerSlot();

    public bool TraceOn => traceOn();

    public string? SpeakerModel(int entIndex) => speakerModel(entIndex);

    /// <summary>
    /// Stock's owner walk climbed SDK entities to a CBasePlayer, and this
    /// client has neither (sound_emitter.cpp answered false on every path).
    /// What it set, a caption's fromplayer, goes to a caption element this
    /// client does not have (<see cref="NoCloseCaptionHud"/>), so the answer is
    /// false, as before.
    /// </summary>
    public bool FromPlayer(int entIndex) => false;
}

/// <summary>
/// The client's sounds, managed: every caller sound_emitter.cpp's client emit
/// path had, over the one <see cref="ClientSoundEmitter"/> and its system.
/// The managed callers ask it directly (the animation events and the move
/// helper, the physics impacts, the tempents' and effects' scripts, the
/// tracers, the soundscapes); native's last callers (a bullet impact, a sound
/// patch, a broadcast, the CSoundEmitterSystem row) reach it across
/// IManagedSoundEmitterClient (<see cref="IClientSoundEmitterSink"/>).
///
/// <para>
/// Built with the module and bound when the side's managed root is, which is
/// when the native recipient filter's block can be built; until then a sound
/// plays nothing, as a missing shim's did, and the row's Init is refused.
/// </para>
/// </summary>
public sealed class ClientSounds(IEngineEntities entities)
    : IClientEntitySounds, IFxGameSounds, IClientImpactSoundEmitter, IFxTracerSound, ISoundscapeSounds,
      IClientSoundEmitterSink
{
    /// <summary>CHAN_STATIC (soundflags.h).</summary>
    public const int ChanStatic = 6;

    private ClientSoundEmitter? _emitter;
    private ClientSoundEmitterSystem? _system;
    private IClientEngineSound? _engine;

    /// <summary>The emitter, once bound.</summary>
    public ClientSoundEmitter? Emitter => _emitter;

    /// <summary>Binds the emitter, its system and the engine's direct sound calls.</summary>
    public void Bind(ClientSoundEmitter emitter, ClientSoundEmitterSystem system, IClientEngineSound engine)
    {
        _emitter = emitter ?? throw new ArgumentNullException(nameof(emitter));
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    //-------------------------------------------------------------------------
    // The managed callers
    //-------------------------------------------------------------------------

    /// <inheritdoc/>
    public void EmitFrom(EntityHandle entity, string soundName, Vector? origin = null)
    {
        if (_emitter is not { } emitter || string.IsNullOrEmpty(soundName))
            return;

        // SOUND_FROM_WORLD when the handle does not resolve to an index: a
        // sound with an origin is a sound whether or not the thing that asked
        // for it is still there.
        int index = entities.GetIndex(entity);
        if (index < 0)
            index = SoundEmitterCore.SoundFromWorld;

        emitter.Emit(emitter.LocalPlayer(), index, soundName, origin);
    }

    /// <inheritdoc/>
    public bool Parameters(string script, out GameSoundParams parameters)
    {
        parameters = default;
        if (_emitter?.Parameters(script) is not { } p)
            return false;

        parameters = new GameSoundParams(p.Name, p.Volume, p.Pitch, p.PitchLow, p.PitchHigh, p.SoundLevel, p.Channel);
        return true;
    }

    /// <inheritdoc/>
    public void EmitAtPosition(in GameSoundParams parameters, float volume, int pitch, in Vector origin)
    {
        if (_emitter is not { } emitter)
            return;

        emitter.Emit(emitter.LocalPlayer(), SoundEmitterCore.SoundFromWorld, new EmitSoundParams
        {
            Channel = parameters.Channel,
            SoundName = parameters.Wave,
            Volume = volume,
            SoundLevel = parameters.SoundLevel,
            Pitch = pitch,
            Origin = origin,
        });
    }

    /// <inheritdoc/>
    public bool EmitImpactSound(string soundName, Vector origin, int channel, float volume)
    {
        if (_emitter is not { } emitter || string.IsNullOrEmpty(soundName))
            return false;

        if (emitter.Parameters(soundName) is not { } p)
            return false;

        // From the world (entity 0), as stock does it: "If this entity gets
        // deleted, the sound comes out at the world origin". The client's
        // attenuation filter is the local player whatever the origin.
        emitter.Emit(emitter.LocalPlayer(), SoundEmitterCore.SoundFromWorld, new EmitSoundParams
        {
            Channel = channel,
            SoundName = p.Name,
            Volume = p.Volume * volume,
            SoundLevel = p.SoundLevel,
            Pitch = p.Pitch,
            Origin = origin,
        });
        return true;
    }

    /// <summary><c>SNDLVL_TO_ATTN</c> (soundflags.h): 20 / (level - 50) above 50 dB, 4 at or below.</summary>
    public static float SoundLevelToAttenuation(int soundLevel)
        => soundLevel > 50 ? 20.0f / (soundLevel - 50) : 4.0f;

    /// <inheritdoc/>
    public bool EmitTracerSound(string soundName, Vector start, Vector direction)
    {
        if (_emitter is not { } emitter || _engine is not { } engine || string.IsNullOrEmpty(soundName))
            return false;

        if (emitter.Parameters(soundName) is not { } p)
            return false;

        // FX_TracerSound's emit: the attenuation overload, along the shot.
        engine.EmitSoundAttenuation(emitter.LocalPlayer(), SoundEmitterCore.SoundFromWorld, ChanStatic, p.Name, p.Volume,
                                    SoundLevelToAttenuation(p.SoundLevel), 0, p.Pitch, 0, start, direction);
        return true;
    }

    /// <inheritdoc/>
    public void EmitAmbient(string wave, float volume, int pitch, int flags)
        => _engine?.EmitAmbientSound(wave, volume, pitch, flags);

    /// <inheritdoc/>
    public void EmitWorld(string wave, float volume, int soundLevel, int pitch, int flags, Vector origin)
    {
        if (_emitter is not { } emitter)
            return;

        emitter.Emit(emitter.LocalPlayer(), SoundEmitterCore.SoundFromWorld, new EmitSoundParams
        {
            Channel = ChanStatic,
            SoundName = wave,
            Volume = volume,
            SoundLevel = soundLevel,
            Flags = flags,
            Pitch = pitch,
            Origin = origin,
        });
    }

    /// <inheritdoc/>
    public void StopWorld(string wave) => _emitter?.Stop(SoundEmitterCore.SoundFromWorld, ChanStatic, wave);

    //-------------------------------------------------------------------------
    // Native's last callers (IManagedSoundEmitterClient)
    //-------------------------------------------------------------------------

    /// <inheritdoc/>
    public bool SystemInit() => _system?.Init() ?? false;

    /// <inheritdoc/>
    public void SystemShutdown() => _system?.Shutdown();

    /// <inheritdoc/>
    public void LevelInitPreEntity() => _system?.LevelInitPreEntity();

    /// <inheritdoc/>
    public void LevelShutdownPostEntity() => _system?.LevelShutdownPostEntity();

    /// <inheritdoc/>
    public float? Emit(ReadOnlySpan<int> slots, bool reliable, bool initMessage, int entIndex,
                       in ClientSoundEmitRequest request, ref short handle)
    {
        if (_emitter is not { } emitter)
            return null;

        var ep = new EmitSoundParams
        {
            Channel = request.Channel,
            SoundName = request.SoundName,
            Volume = request.Volume,
            SoundLevel = request.SoundLevel,
            Flags = request.Flags,
            Pitch = request.Pitch,
            SpecialDsp = request.SpecialDsp,
            Origin = request.Origin,
            SoundTime = request.SoundTime,
            WantDuration = request.WantDuration,
            Duration = float.NaN,
            EmitCloseCaption = request.EmitCloseCaption,
            WarnOnMissingCloseCaption = request.WarnOnMissingCloseCaption,
            WarnOnDirectWaveReference = request.WarnOnDirectWaveReference,
            SpeakerEntity = request.SpeakerEntity,
            ScriptHandle = handle,
        };

        ClientRecipients recipients = ClientRecipients.Of(slots, reliable, initMessage);
        if (request.ByHandle)
        {
            emitter.EmitByHandle(recipients, entIndex, ep, ref handle);
        }
        else
        {
            emitter.Emit(recipients, entIndex, ep);
            handle = ep.ScriptHandle;
        }

        // Written only when the emit wrote it (stock's m_pflSoundDuration).
        return ep.WantDuration && !float.IsNaN(ep.Duration) ? ep.Duration : null;
    }

    /// <inheritdoc/>
    public void Stop(int entIndex, int channel, string? sample) => _emitter?.Stop(entIndex, channel, sample);

    /// <inheritdoc/>
    public void EmitCloseCaption(ReadOnlySpan<int> slots, bool reliable, bool initMessage, int entIndex, string token,
                                 float duration, bool warnIfMissing)
        => _emitter?.EmitCloseCaption(ClientRecipients.Of(slots, reliable, initMessage), entIndex, token, duration,
                                      warnIfMissing);
}
