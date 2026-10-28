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
/// The server's sound members over the managed <see cref="SoundEmitter"/>
/// (plan_server_native_v2 T4a): what shell_sound.cpp's five slots,
/// shell_soundenvelope.cpp's patches and the standalone player's move helper
/// did natively, decided here now. The entity is asked through the engine
/// areas; the sound goes out through the emitter.
/// </summary>
public sealed class ServerSounds : IServerSoundRoute, IMoveSoundSink
{
    private readonly SoundEmitter _emitter;
    private readonly IEngineEntities _entities;
    private readonly Action<string> _warning;
    private readonly ServerSoundPatches _patches;
    private readonly SoundEmitterSystem? _system;
    private bool _saidUnresolved;

    /// <param name="onPatchCreated">
    /// The managed sound controller's <c>Create</c>: a new patch's handle is
    /// told to it as CShellSoundController::Track told it.
    /// </param>
    /// <param name="system">
    /// The sound emitter system every <see cref="PrecacheSound"/> goes to; null
    /// precaches nothing (the module has said why).
    /// </param>
    public ServerSounds(SoundEmitter emitter, IEngineEntities entities, Action<int> onPatchCreated,
                        Action<string> warning, SoundEmitterSystem? system = null)
    {
        _emitter = emitter;
        _entities = entities;
        _warning = warning;
        _system = system;
        _patches = new ServerSoundPatches(emitter, entities, onPatchCreated);
    }

    /// <summary>
    /// shell_sound.cpp's <c>SourceSharp_PrecacheSound</c>, which every managed
    /// entity's precache reached: <see cref="SoundEmitterSystem.PrecacheSound"/>.
    /// </summary>
    public void PrecacheSound(string soundName) => _system?.PrecacheSound(soundName);

    /// <summary>The patches, for the facts.</summary>
    public ServerSoundPatches Patches => _patches;

    /// <summary>
    /// <c>SourceSharpEntity_t::SoundSourceIndex</c>: the entity's index when it
    /// has one the client knows, else SOUND_FROM_WORLD -- what
    /// <c>CBaseEntity::EmitSound</c> passed for itself.
    /// </summary>
    public int SoundSourceIndex(EntityHandle entity)
    {
        int index = _entities.GetIndex(entity);
        return index > 0 ? index : SoundEmitter.SoundFromWorld;
    }

    /// <summary>
    /// shell_sound.cpp's <c>SourceSharp_EntityEmitSound</c>: a handle that
    /// resolves to nothing is said once and plays nothing; otherwise the PAS
    /// of the entity's centre, narrowed by the script's level, hears it, from
    /// the entity's index -- or from its origin when it has no index the
    /// client knows.
    /// </summary>
    public void EntityEmitSound(EntityHandle entity, string soundName)
    {
        if (string.IsNullOrEmpty(soundName))
            return;

        if (!_entities.IsValid(entity))
        {
            SayUnresolved(entity, soundName);
            return;
        }

        int index = SoundSourceIndex(entity);
        RecipientFilter filter = _emitter.Recipients.PasAttenuation(_entities.GetWorldSpaceCenter(entity), soundName);

        _emitter.Emit(filter, index, new EmitSoundParams
        {
            SoundName = soundName,
            Origin = index == SoundEmitter.SoundFromWorld ? _entities.GetOrigin(entity) : null,
            WarnOnDirectWaveReference = true,
        });
    }

    /// <summary>
    /// <c>CBaseCombatWeapon::WeaponSound</c>'s server arm for a weapon in an
    /// owner's hands: the PAS of the owner, narrowed by the script's level,
    /// under the prediction rules (<c>filter.UsePredictionRules()</c>) -- the
    /// player predicting the command is not sent the sound it played for
    /// itself -- from the owner's index. The trace line is printed whoever
    /// is left, as stock's is. (No hl2mp weapon script sets
    /// <c>play_to_owner_only</c>, stock's other arm.)
    /// </summary>
    public void EmitPredicted(EntityHandle owner, string soundName, PredictionSuppression prediction)
    {
        if (string.IsNullOrEmpty(soundName))
            return;

        if (!_entities.IsValid(owner))
        {
            SayUnresolved(owner, soundName);
            return;
        }

        int index = SoundSourceIndex(owner);
        RecipientFilter filter = _emitter.Recipients.PasAttenuation(_entities.GetWorldSpaceCenter(owner), soundName);
        prediction.UsePredictionRules(filter);

        _emitter.Emit(filter, index, new EmitSoundParams
        {
            SoundName = soundName,
            Origin = index == SoundEmitter.SoundFromWorld ? _entities.GetOrigin(owner) : null,
            WarnOnDirectWaveReference = true,
        });
    }

    private void SayUnresolved(EntityHandle entity, string soundName)
    {
        if (_saidUnresolved)
            return;

        _saidUnresolved = true;

        if (entity != EntityHandle.None && entity.IsInvisibleRange)
            _warning($"[SourceSharp] a shell-less entity asked to emit '{soundName}' and reached the SERVER "
                     + "emitter to do it. That is a regression: Engine.EntityEmitSound is supposed to answer a "
                     + "shell-less handle on its side and route it to EmitSoundAtPosition. plan_entity.md step "
                     + "8e-sound. Said once.\n");
        else
            _warning($"[SourceSharp] '{soundName}' was emitted from a handle that resolves to nothing -- the "
                     + "entity is gone, or its slot was reused. Said once.\n");
    }

    /// <summary>
    /// <c>SourceSharp_EntityStopSound</c>: on the index the sound started
    /// on, and SOUND_FROM_WORLD for a handle that resolves to nothing (where a
    /// shell-less entity's emit went).
    /// </summary>
    public void EntityStopSound(EntityHandle entity, string soundName)
    {
        if (string.IsNullOrEmpty(soundName))
            return;

        int index = _entities.IsValid(entity) ? SoundSourceIndex(entity) : SoundEmitter.SoundFromWorld;
        _emitter.Stop(index, soundName);
    }

    /// <summary>
    /// <c>SourceSharp_EmitSoundAtPosition</c>: from the world, at the point,
    /// to the PAS of the point at ATTN_NORM.
    /// </summary>
    public void EmitSoundAtPosition(string soundName, Vector origin)
    {
        if (string.IsNullOrEmpty(soundName))
            return;

        _emitter.Emit(_emitter.Recipients.PasAttenuation(origin), SoundEmitter.SoundFromWorld, new EmitSoundParams
        {
            SoundName = soundName,
            Origin = origin,
            Volume = SoundEmitter.VolNorm,
            Pitch = SoundEmitter.PitchNorm,
        });
    }

    /// <summary>
    /// <c>SourceSharp_EmitWorldSoundScript</c>, the body of vphysics_sound.h's
    /// PlayImpactSounds / PlayBreakSounds: the script's wave at its own level
    /// and pitch and <paramref name="volume"/> times its volume, from the
    /// world; false when the script has no parameters.
    /// </summary>
    public bool EmitWorldSoundScript(string soundName, Vector origin, int channel, float volume)
    {
        if (string.IsNullOrEmpty(soundName) || _emitter.Parameters(soundName) is not { } parameters)
            return false;

        _emitter.Emit(_emitter.Recipients.PasAttenuation(origin, parameters.SoundLevel), SoundEmitter.SoundFromWorld,
                      new EmitSoundParams
                      {
                          Channel = channel,
                          SoundName = parameters.Name,
                          Volume = parameters.Volume * volume,
                          SoundLevel = parameters.SoundLevel,
                          Pitch = parameters.Pitch,
                          Origin = origin,
                      });
        return true;
    }

    /// <summary><c>SourceSharp_EmitAmbientSound</c>: UTIL_EmitAmbientSound.</summary>
    public void EmitAmbientSound(int entIndex, Vector origin, string soundName, float volume, int soundLevel,
                                 int flags, int pitch)
    {
        if (string.IsNullOrEmpty(soundName))
            return;

        _emitter.UtilEmitAmbient(entIndex, origin, soundName, volume, soundLevel, flags, pitch);
    }

    /// <summary>
    /// The standalone player's <c>IMoveHelper::StartSound</c> with its
    /// parameters: the PAS of the point, unnarrowed, from the player.
    /// </summary>
    public void MoveSound(int entIndex, Vector origin, int channel, string sample, float volume, int soundLevel,
                          int flags, int pitch)
        => _emitter.Emit(_emitter.Recipients.Pas(origin), entIndex, new EmitSoundParams
        {
            Channel = channel,
            SoundName = sample,
            Volume = volume,
            SoundLevel = soundLevel,
            Flags = flags,
            Pitch = pitch,
            Origin = origin,
        });

    /// <summary>
    /// <c>IMoveHelper::StartSound( origin, soundname )</c>: the PAS of the
    /// point hears the script, from the player (GameSound_Emit with no origin).
    /// </summary>
    public void MoveSoundScript(int entIndex, Vector origin, string soundName)
        => _emitter.Emit(_emitter.Recipients.Pas(origin), entIndex, soundName);

    // ---- the patches ----

    public int SoundPatchCreate(EntityHandle entity, int channel, string soundName, int soundLevel)
    {
        if (string.IsNullOrEmpty(soundName) || !_entities.IsValid(entity))
            return 0;

        RecipientFilter filter = _emitter.Recipients.PasAttenuation(_entities.GetOrigin(entity), soundLevel);
        return _patches.Create(filter, entity, SoundSourceIndex(entity), channel, soundName, soundLevel);
    }

    public void SoundPatchEmit(int patch, float volume01, float pitch, int flags, float startTime, bool emitCaption)
        => _patches.Emit(patch, volume01, pitch, flags, startTime, emitCaption);

    public void SoundPatchStop(int patch) => _patches.Stop(patch);

    public void SoundPatchState(int patch, out int entIndex, out bool filterActive)
        => _patches.State(patch, out entIndex, out filterActive);

    public void SoundPatchFree(int patch) => _patches.Free(patch);
}

/// <summary>
/// The server's sound patches (stock's <c>CSoundPatch</c>, soundenvelope.cpp,
/// as shell_soundenvelope.cpp had reduced it): a patch is a recipient filter,
/// the entity it rides, a script resolved once to its wave, volume, level and
/// channel, and whether it is playing. The ramps are the managed sound
/// controller's; this is what it emits through. Handles count up from 1 and
/// are never reused, so a stale one resolves to nothing.
/// </summary>
public sealed class ServerSoundPatches(SoundEmitter emitter, IEngineEntities entities, Action<int> onCreated)
{
    private sealed class Patch
    {
        public required RecipientFilter Filter;
        public EntityHandle Source;
        public int Channel;
        public required string SoundName;
        public required string ScriptName;
        public int SoundLevel;
        public float ScriptVolume = 1.0f;
        public bool Playing;
        public float CaptionDuration = SoundEmitter.DefaultCaptionDuration;
    }

    private readonly Dictionary<int, Patch> _patches = [];
    private int _nextHandle = 1;

    /// <summary>How many patches are held.</summary>
    public int Count => _patches.Count;

    /// <summary>
    /// <c>SoundCreate</c> + <c>CSoundPatch::Init</c>: a script (not a
    /// .wav/.mp3) with parameters gives the patch its wave, volume, level and
    /// channel; anything else plays as named at full script volume and the
    /// caller's level. The patch rides the entity at
    /// <paramref name="entIndex"/> -- none for SOUND_FROM_WORLD, which, as
    /// stock's lookup of the world's slot did, leaves it silent.
    /// </summary>
    public int Create(RecipientFilter filter, EntityHandle entity, int entIndex, int channel, string soundName,
                      int soundLevel)
    {
        var patch = new Patch
        {
            Filter = filter.Copy(),
            Source = entIndex > 0 ? entity : EntityHandle.None,
            Channel = channel,
            SoundName = soundName,
            ScriptName = soundName,
            SoundLevel = soundLevel,
        };

        bool isWave = soundName.Contains(".wav", StringComparison.OrdinalIgnoreCase)
                      || soundName.Contains(".mp3", StringComparison.OrdinalIgnoreCase);
        if (!isWave && emitter.Parameters(soundName) is { } parameters)
        {
            patch.ScriptVolume = parameters.Volume;
            patch.SoundName = parameters.Name;
            patch.SoundLevel = parameters.SoundLevel;
            patch.Channel = parameters.Channel;
        }

        int handle = _nextHandle++;
        _patches[handle] = patch;
        onCreated(handle);
        return handle;
    }

    /// <summary>
    /// <c>CSoundPatch::EntIndex</c>: the index of the entity it rides, or -1
    /// when it rides none or that entity has gone.
    /// </summary>
    private int EntIndex(Patch patch)
    {
        if (patch.Source == EntityHandle.None || !entities.IsValid(patch.Source))
            return -1;

        int entry = patch.Source.EntryIndex;
        return entry < EntityHandle.FirstInvisibleEntry ? entry : -1;
    }

    /// <summary>
    /// <c>CSoundPatch::Update</c>'s and <c>StartSound</c>'s emit: the
    /// envelope's volume times the script's, the envelope's pitch, and on a
    /// start the caption (warned about when missing) and the playing mark.
    /// Nothing for a patch whose entity is gone or that nobody hears.
    /// </summary>
    public void Emit(int handle, float volume01, float pitch, int flags, float startTime, bool emitCaption)
    {
        if (!_patches.TryGetValue(handle, out Patch? patch))
            return;

        int index = EntIndex(patch);
        if (index < 0 || patch.Filter.Count == 0)
            return;

        var ep = new EmitSoundParams
        {
            Channel = patch.Channel,
            SoundName = patch.SoundName,
            Volume = patch.ScriptVolume * volume01,
            SoundLevel = patch.SoundLevel,
            Flags = flags,
            Pitch = (int)pitch,
        };

        if (emitCaption)
        {
            ep.EmitCloseCaption = false;
            if (startTime != 0.0f)
                ep.SoundTime = startTime;
        }

        emitter.Emit(patch.Filter, index, ep);

        if (emitCaption)
        {
            emitter.EmitCloseCaption(patch.Filter, index, patch.ScriptName, patch.CaptionDuration, true);
            patch.Playing = true;
        }
    }

    /// <summary>
    /// <c>CSoundPatch::Shutdown</c>: a playing patch stopped on its entity's
    /// index. A patch that never started is not stopped: the engine warns
    /// about that, and rightly.
    /// </summary>
    public void Stop(int handle)
    {
        if (!_patches.TryGetValue(handle, out Patch? patch) || !patch.Playing)
            return;

        int index = EntIndex(patch);
        if (index >= 0)
            emitter.Stop(index, patch.Channel, patch.SoundName);

        patch.Playing = false;
    }

    /// <summary>The frame pass's two questions: the patch's index (-1 when gone) and whether anyone hears it.</summary>
    public void State(int handle, out int entIndex, out bool filterActive)
    {
        _patches.TryGetValue(handle, out Patch? patch);
        entIndex = patch is null ? -1 : EntIndex(patch);
        filterActive = patch is not null && patch.Filter.Count > 0;
    }

    /// <summary>The patch forgotten; a handle it does not know is nothing.</summary>
    public void Free(int handle) => _patches.Remove(handle);
}

/// <summary>
/// The engine's players as the recipient rules ask about them: the server's
/// slots, who has a player, where it listens (the standalone player's ears,
/// CServerGameClients::ClientEarPosition's arm), and the closecaption value
/// each client reported (GetClientConVarValue).
/// </summary>
public sealed class ServerSoundListeners(IEngineEnvironment environment, IEnginePlayers players,
                                         IFwdEngineServer server) : ISoundListeners
{
    public int MaxClients => environment.Globals.IsAvailable ? environment.Globals.MaxClients : 0;

    public bool IsConnected(int playerIndex) => players.GetHandle(playerIndex) != EntityHandle.None;

    public bool TryEars(int playerIndex, out Vector ears) => players.TryStandalonePlayerEarPosition(playerIndex, out ears);

    public string? CloseCaptionSetting(int playerIndex) => server.ClientConVarValue(playerIndex, "closecaption");
}
