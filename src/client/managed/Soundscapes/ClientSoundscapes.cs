//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;
using SourceSharp.Shared.Soundscapes;

namespace SourceSharp.Client.Soundscapes;

/// <summary>
/// The client soundscape system, stock's <c>C_SoundscapeSystem</c>
/// (<c>game/client/c_soundscape.cpp</c>, then
/// <c>shell_soundscape_client.cpp</c>): which loops and random one-shots a
/// soundscape plays, where, how loud, and the crossfade between two of them.
/// Every engine call it makes goes out through <see cref="ISoundscapeAudio"/>.
///
/// <para>
/// <b>The scripts come from the shared <see cref="SoundscapeLibrary"/></b>,
/// the traversal the server indexes by. The client no longer keeps a second
/// parse, so the index a <c>Soundscape</c> push carries means the same
/// soundscape on both sides by construction.
/// </para>
///
/// <para>
/// <b>Draws.</b> Stock draws from two streams: <c>random-&gt;</c> (the wave
/// pick and the random position) and vstdlib's <c>RandomFloat</c> (inside
/// <c>RandomInterval</c>). Both are <see cref="IEngineHost"/>'s here, in stock's
/// order, which <c>soundscape-stock.tsv</c> records.
/// </para>
/// </summary>
public sealed class ClientSoundscapes
{
    /// <summary><c>MAX_SOUNDSCAPE_RECURSION</c>.</summary>
    private const int MaxRecursion = 8;

    /// <summary><c>DEFAULT_SOUND_RADIUS</c>.</summary>
    public const float DefaultSoundRadius = 36.0f;

    private const int SndChangeVol = 1 << 0;
    private const int SndStop = 1 << 2;
    private const int PitchNorm = 100;

    /// <summary>stock's <c>NUM_AUDIO_LOCAL_SOUNDS</c>.</summary>
    public const int LocalSoundCount = 8;

    private sealed class LoopingSound
    {
        public Vector Position;
        public string Wave = "";
        public float VolumeTarget;
        public float VolumeCurrent;
        public int SoundLevel;
        public int Pitch;
        public int Id;
        public bool IsAmbient;
    }

    private sealed class RandomSound
    {
        public Vector Position;
        public float NextPlayTime;
        public Interval Time;
        public Interval Volume;
        public Interval Pitch;
        public Interval SoundLevel;
        public float MasterVolume;
        public IReadOnlyList<KeyValuesText> Waves = [];
        public bool IsAmbient;
        public bool IsRandom;

        public RandomSound Copy() => (RandomSound)MemberwiseClone();
    }

    private struct SubParams
    {
        public int RecurseLevel;
        public float MasterVolume;
        public int StartingPosition;
        public int PositionOverride;
        public int AmbientPositionOverride;
        public bool AllowDsp;
        public bool WroteSoundMixer;
        public bool WroteDspVolume;
    }

    private readonly ISoundscapeAudio _audio;
    private readonly ISoundscapeSounds _sounds;
    private readonly IGameClock _clock;
    private readonly IEngineHost _host;
    private readonly IEngineConsole _console;

    private SoundscapeLibrary _library = new();

    private readonly List<LoopingSound> _looping = new();
    private readonly List<RandomSound> _random = new();
    private readonly Vector[] _localSound = new Vector[LocalSoundCount];

    private int _soundscapeIndex = -1;
    private int _entIndex;
    private int _localBits;

    private float _nextRandomTime;
    private int _loopingSoundId;

    // ZERO, not -1, and that is stock's: C_SoundscapeSystem's constructor
    // sets only m_nRestoreFrame, the object is a zero-initialised global, and
    // Init() never touches this. So until the first push (which writes -1)
    // the system believes soundscape 0 is forced, and Update() rebuilds the
    // forced box (radius 0, all four corners at the listener) every frame.
    private int _forcedIndex;
    private float _forcedRadius;

    private int _frameCount;
    private int _restoreFrame = -1;
    private int _pushes;

    /// <summary>Built by the client's container.</summary>
    public ClientSoundscapes(ISoundscapeAudio audio, ISoundscapeSounds sounds, IGameClock clock, IEngineHost host,
                             IEngineConsole console)
    {
        _audio = audio;
        _sounds = sounds;
        _clock = clock;
        _host = host;
        _console = console;
    }

    /// <summary>The soundscapes this side knows, in index order.</summary>
    public SoundscapeLibrary Library => _library;

    /// <summary>How many loops are running (the report's "looping").</summary>
    public int LoopingCount => _looping.Count;

    /// <summary>How many random one-shot sets are queued (the report's "random").</summary>
    public int RandomCount => _random.Count;

    /// <summary><c>m_loopingSoundId</c>: bumped by every new soundscape.</summary>
    public int LoopingSoundId => _loopingSoundId;

    /// <summary>How many pushes have reached <see cref="Push"/>.</summary>
    public int Pushes => _pushes;

    /// <summary>The forced soundscape, or -1.</summary>
    public int ForcedIndex => _forcedIndex;

    /// <summary><c>GetCurrentSoundscape</c>: the forced one if any, else the pushed one.</summary>
    public int CurrentSoundscape => _forcedIndex >= 0 ? _forcedIndex : _soundscapeIndex;

    /// <summary>The pushed parameters: index, entity and local bits.</summary>
    public (int Index, int EntIndex, int LocalBits) Params => (_soundscapeIndex, _entIndex, _localBits);

    //-------------------------------------------------------------------------
    // Lifecycle
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>Init</c>: take the scripts. Stock parsed them here; the library is
    /// parsed once per level for both sides and handed in.
    /// </summary>
    public void Init(SoundscapeLibrary library)
    {
        _loopingSoundId = 0;
        _library = library;
    }

    /// <summary>
    /// <c>LevelInitPreEntity</c>: <c>Shutdown</c>, <c>Init</c>, and under
    /// <c>-makereslists</c> touch every wave the soundscapes name so the
    /// reslist build records them.
    /// </summary>
    public void LevelInit(SoundscapeLibrary library)
    {
        Shutdown();
        Init(library);

        if (!_audio.MakingResLists)
            return;

        foreach (var soundscape in _library.All)
        {
            foreach (var key in soundscape.Script.Children)
            {
                if (Is(key, "playlooping"))
                {
                    foreach (var sub in key.Children)
                    {
                        if (Is(sub, "wave"))
                            _audio.TouchSoundFile(sub.AsString() ?? "");
                    }
                }
                else if (Is(key, "playrandom"))
                {
                    foreach (var sub in key.Children)
                    {
                        if (!Is(sub, "rndwave"))
                            continue;
                        foreach (var wave in sub.Children)
                            _audio.TouchSoundFile(wave.AsString() ?? "");
                    }
                }
            }
        }
    }

    /// <summary>
    /// <c>Shutdown</c>: stop every loop, forget the queues and the scripts,
    /// and reset the pushed parameters (index -1, entity 0).
    /// </summary>
    public void Shutdown()
    {
        for (int i = _looping.Count - 1; i >= 0; --i)
            StopLoopingSound(_looping[i]);

        _looping.Clear();
        _random.Clear();
        _library = new SoundscapeLibrary();
        _entIndex = 0;
        _soundscapeIndex = -1;
    }

    /// <summary>
    /// <c>OnStopAllSounds</c> and <c>LevelShutdownPostEntity</c>: forget
    /// everything without stopping it (the engine already has).
    /// </summary>
    public void OnStopAllSounds()
    {
        _entIndex = 0;
        _soundscapeIndex = -1;
        _looping.Clear();
        _random.Clear();
    }

    /// <summary>
    /// <c>OnRestore</c>: the frame a save is restored on, when
    /// <c>suppress_on_restore</c> sounds are not restarted. Nothing in a
    /// multiplayer client restores a save; the arm is stock's and is kept.
    /// </summary>
    public void OnRestore() => _restoreFrame = _frameCount;

    private bool IsBeingRestored => _frameCount == _restoreFrame;

    //-------------------------------------------------------------------------
    // The frame
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>Update( frametime )</c>, once a frame: rebuild the forced box,
    /// fade the loops, play what random sounds are due.
    /// </summary>
    /// <param name="frameTime">The frame's length, as the system's Update got it.</param>
    /// <param name="fadeTime"><c>soundscape_fadetime</c>.</param>
    public void Update(float frameTime, float fadeTime)
    {
        if (_forcedIndex >= 0)
        {
            _audio.MainView(out Vector origin, out Vector forward, out Vector right);

            // put the sound origins at the corners of a box around the player
            _localSound[0] = origin + (_forcedRadius * (forward - right));
            _localSound[1] = origin + (_forcedRadius * (forward + right));
            _localSound[2] = origin + (_forcedRadius * (Neg(forward) - right));
            _localSound[3] = origin + (_forcedRadius * (Neg(forward) + right));
            _localBits = 0x0007;
        }

        UpdateLoopingSounds(frameTime, fadeTime);
        UpdateRandomSounds(_clock.Now);
        _frameCount++;
    }

    private void UpdateLoopingSounds(float frameTime, float fadeTime)
    {
        float amount = frameTime;
        if (fadeTime > 0)
            amount = (float)(amount * (1.0 / fadeTime));   // 1.0 is a double in stock

        int fadeCount = _looping.Count;
        while (fadeCount > 0)
        {
            fadeCount--;
            LoopingSound sound = _looping[fadeCount];

            if (sound.VolumeCurrent != sound.VolumeTarget)
            {
                sound.VolumeCurrent = Approach(sound.VolumeTarget, sound.VolumeCurrent, amount);
                if (sound.VolumeTarget == 0 && sound.VolumeCurrent == 0)
                {
                    // sound is done, remove from list.
                    StopLoopingSound(sound);
                    FastRemove(_looping, fadeCount);
                }
                else
                {
                    // tell the engine about the new volume
                    UpdateLoopingSound(sound);
                }
            }
        }
    }

    // Vector's unary minus: each component negated (so +0 becomes -0, as stock's).
    private static Vector Neg(Vector v) => new(-v.X, -v.Y, -v.Z);

    private static float Approach(float target, float value, float speed)
    {
        float delta = target - value;

        if (delta > speed)
            value += speed;
        else if (delta < -speed)
            value -= speed;
        else
            value = target;

        return value;
    }

    // CUtlVector::FastRemove: the last element moves into the hole.
    private static void FastRemove<T>(List<T> list, int index)
    {
        int last = list.Count - 1;
        list[index] = list[last];
        list.RemoveAt(last);
    }

    //-------------------------------------------------------------------------
    // Pushes, forces and stops
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>Soundscape_Update( audioparams_t )</c>: the server's choice arriving
    /// through the <c>Soundscape</c> user message. Positions are indexed by
    /// bit, eight of them.
    /// </summary>
    public void Push(int soundscapeIndex, int entIndex, int localBits, ReadOnlySpan<Vector> localPositions)
    {
        _pushes++;

        if (_soundscapeIndex == soundscapeIndex && _entIndex == entIndex)
            return;

        // A PUSH THIS SIDE CANNOT HONOUR IS REFUSED, NOT LATCHED (plan_entity
        // 8e-sound): stock wrote m_params first, so the dedupe above answered
        // "already current" for that index forever and the map went silent.
        // The leave form, entIndex 0, is valid and latched.
        if (entIndex > 0 && _library.ByIndex(soundscapeIndex) is null)
        {
            _console.Warning($"[SourceSharp] soundscape push named index {soundscapeIndex} and this side has " +
                             $"parsed {_library.Count}; refused rather than remembered, so a later push of " +
                             "the same index is not deduped away. plan_entity.md 8e-sound.\n");
            return;
        }

        _soundscapeIndex = soundscapeIndex;
        _entIndex = entIndex;
        _localBits = localBits;
        for (int i = 0; i < LocalSoundCount; i++)
            _localSound[i] = i < localPositions.Length ? localPositions[i] : default;

        _forcedIndex = -1;

        if (entIndex > 0)
        {
            DevReportSoundscapeName(soundscapeIndex);
            StartNewSoundscape(_library.ByIndex(soundscapeIndex)!.Script);
        }
    }

    /// <summary>
    /// <c>cl_soundscape_flush</c>: keep the pushed parameters, shut down,
    /// take <paramref name="library"/>, and push the parameters back.
    /// </summary>
    public void Flush(SoundscapeLibrary library)
    {
        int index = _soundscapeIndex, ent = _entIndex, bits = _localBits;
        var positions = (Vector[])_localSound.Clone();

        Shutdown();
        Init(library);
        Push(index, ent, bits, positions);
    }

    /// <summary><c>playsoundscape name [radius]</c>.</summary>
    public void Force(string name, float radius)
    {
        int index = _library.IndexOf(name);
        if (index >= 0)
        {
            _forcedIndex = index;
            _forcedRadius = radius;
            StartNewSoundscape(_library.ByIndex(index)!.Script);
        }
        else
        {
            DevWarning($"Can't find soundscape {name}\n");
        }
    }

    /// <summary><c>stopsoundscape</c>: fade out everything and start nothing.</summary>
    public void Stop() => StartNewSoundscape(null);

    /// <summary><c>DevReportSoundscapeName</c>.</summary>
    public void DevReportSoundscapeName(int index)
    {
        string name = index >= 0 && index < _library.Count ? _library.ByIndex(index)!.Name : "none";
        DevMsg($"Soundscape: {name}\n");
    }

    /// <summary>
    /// <c>StartNewSoundscape</c>: the leading edge of a soundscape. Every loop
    /// fades towards zero (or stops at once when <paramref name="soundscape"/>
    /// is null), the random queue is emptied, and the script is walked.
    /// </summary>
    private void StartNewSoundscape(KeyValuesText? soundscape)
    {
        for (int i = _looping.Count - 1; i >= 0; --i)
        {
            _looping[i].VolumeTarget = 0;
            if (soundscape is null)
                _looping[i].VolumeCurrent = 0;   // cancelling: stop immediately
        }

        _loopingSoundId++;

        _random.Clear();
        _nextRandomTime = _clock.Now;

        if (soundscape is null)
            return;

        var p = new SubParams
        {
            AllowDsp = true,
            MasterVolume = 1.0f,
            StartingPosition = 0,
            RecurseLevel = 0,
            PositionOverride = -1,
            AmbientPositionOverride = -1,
        };
        StartSubSoundscape(soundscape, ref p);

        if (!p.WroteDspVolume)
            _audio.RevertConVar("dsp_volume");
        if (!p.WroteSoundMixer)
            _audio.RevertConVar("snd_soundmixer");
    }

    private void StartSubSoundscape(KeyValuesText soundscape, ref SubParams p)
    {
        foreach (var key in soundscape.Children)
        {
            if (Is(key, "dsp"))
            {
                if (p.AllowDsp)
                    _audio.SetRoomType(key.AsInt());
            }
            else if (Is(key, "dsp_player"))
            {
                if (p.AllowDsp)
                    _audio.SetPlayerDsp(key.AsInt());
            }
            else if (Is(key, "playlooping"))
            {
                ProcessPlayLooping(key, p);
            }
            else if (Is(key, "playrandom"))
            {
                ProcessPlayRandom(key, p);
            }
            else if (Is(key, "playsoundscape"))
            {
                ProcessPlaySoundscape(key, ref p);
            }
            else if (Is(key, "Soundmixer"))
            {
                if (p.AllowDsp)
                {
                    // Unconditional: CanSetSoundMixer could not be false in
                    // this build (shell_soundscape_client.cpp's header).
                    _audio.SetConVar("snd_soundmixer", key.AsString() ?? "");
                    p.WroteSoundMixer = true;
                }
            }
            else if (Is(key, "dsp_volume"))
            {
                if (p.AllowDsp)
                {
                    _audio.SetConVar("dsp_volume", FloatText(key.AsFloat()));
                    p.WroteDspVolume = true;
                }
            }
            else
            {
                DevMsg($"Soundscape {soundscape.Name}:Unknown command {key.Name}\n");
            }
        }
    }

    private void ProcessPlayLooping(KeyValuesText ambient, in SubParams p)
    {
        float volume = 0;
        int soundLevel = AttnToSndlvl(0.8f);   // ATTN_NORM
        string? wave = null;
        int pitch = PitchNorm;
        int positionIndex = -1;
        bool suppress = false;

        foreach (var key in ambient.Children)
        {
            if (Is(key, "volume"))
                volume = p.MasterVolume * RandomInterval(ReadInterval(key));
            else if (Is(key, "pitch"))
                pitch = (int)RandomInterval(ReadInterval(key));
            else if (Is(key, "wave"))
                wave = key.AsString();
            else if (Is(key, "position"))
                positionIndex = p.StartingPosition + key.AsInt();
            else if (Is(key, "attenuation"))
                soundLevel = DrawnAttnToSndlvl(ReadInterval(key));
            else if (Is(key, "soundlevel"))
                soundLevel = ReadSoundLevel(key);
            else if (Is(key, "suppress_on_restore"))
                suppress = Atoi(key.AsString()) != 0;
            else
                DevMsg($"Ambient {ambient.Name}:Unknown command {key.Name}\n");
        }

        if (positionIndex < 0)
            positionIndex = p.AmbientPositionOverride;
        else if (p.PositionOverride >= 0)
            positionIndex = p.PositionOverride;

        // Sound is marked "suppress_on_restore" so don't restart it
        if (IsBeingRestored && suppress)
            return;

        if (volume != 0 && wave is not null)
        {
            if (positionIndex < 0)
            {
                AddLoopingSound(wave, true, volume, SoundLevelText.Normal, pitch, default);
            }
            else
            {
                if (positionIndex > 31 || (_localBits & (1 << positionIndex)) == 0)
                    return;   // suppress sounds if the position isn't available

                AddLoopingSound(wave, false, volume, soundLevel, pitch, _localSound[positionIndex]);
            }
        }
    }

    private void ProcessPlayRandom(KeyValuesText playRandom, in SubParams p)
    {
        var sound = new RandomSound { MasterVolume = p.MasterVolume };
        int positionIndex = -1;
        bool suppress = false;
        bool randomPosition = false;
        int waveCount = 0;

        foreach (var key in playRandom.Children)
        {
            if (Is(key, "volume"))
            {
                sound.Volume = ReadInterval(key);
            }
            else if (Is(key, "pitch"))
            {
                sound.Pitch = ReadInterval(key);
            }
            else if (Is(key, "attenuation"))
            {
                Interval atten = ReadInterval(key);
                float start = AttnToSndlvl(atten.Start);
                sound.SoundLevel = new Interval(start, AttnToSndlvl(atten.Start + atten.Range) - start);
            }
            else if (Is(key, "soundlevel"))
            {
                // A named level is fixed; anything else is kept as an
                // interval and drawn at each play, not now.
                string text = key.AsString() ?? "";
                sound.SoundLevel = IsNamedSoundLevel(text)
                    ? new Interval(SoundLevelText.Parse(text, m => DevMsg(m)), 0)
                    : ReadInterval(key);
            }
            else if (Is(key, "time"))
            {
                sound.Time = ReadInterval(key);
            }
            else if (Is(key, "rndwave"))
            {
                sound.Waves = key.Children;
                waveCount = key.Children.Count;
            }
            else if (Is(key, "position"))
            {
                if (string.Equals(key.AsString(), "random", StringComparison.OrdinalIgnoreCase))
                    randomPosition = true;
                else
                    positionIndex = p.StartingPosition + key.AsInt();
            }
            else if (Is(key, "suppress_on_restore"))
            {
                suppress = Atoi(key.AsString()) != 0;
            }
            else
            {
                DevMsg($"Random Sound {playRandom.Name}:Unknown command {key.Name}\n");
            }
        }

        if (positionIndex < 0)
        {
            positionIndex = p.AmbientPositionOverride;
        }
        else if (p.PositionOverride >= 0)
        {
            positionIndex = p.PositionOverride;
            randomPosition = false;   // override trumps random position
        }

        if (IsBeingRestored && suppress)
            return;

        if (waveCount == 0)
            return;

        if (positionIndex < 0 && !randomPosition)
        {
            sound.IsAmbient = true;
            AddRandomSound(sound);
            return;
        }

        sound.IsAmbient = false;
        if (randomPosition)
        {
            sound.IsRandom = true;
        }
        else
        {
            if (positionIndex > 31 || (_localBits & (1 << positionIndex)) == 0)
                return;   // suppress sounds if the position isn't available

            sound.Position = _localSound[positionIndex];
        }

        AddRandomSound(sound);
    }

    private void ProcessPlaySoundscape(KeyValuesText playSoundscape, ref SubParams paramsIn)
    {
        SubParams sub = paramsIn;

        // sub-soundscapes NEVER set the DSP effects
        sub.AllowDsp = false;
        sub.RecurseLevel++;
        if (sub.RecurseLevel > MaxRecursion)
        {
            DevMsg("Error!  Soundscape recursion overrun!\n");
            return;
        }

        string? name = null;
        foreach (var key in playSoundscape.Children)
        {
            if (Is(key, "volume"))
            {
                sub.MasterVolume = paramsIn.MasterVolume * RandomInterval(ReadInterval(key));
            }
            else if (Is(key, "position"))
            {
                sub.StartingPosition = paramsIn.StartingPosition + key.AsInt();
            }
            else if (Is(key, "positionoverride"))
            {
                if (paramsIn.PositionOverride < 0)
                {
                    sub.PositionOverride = paramsIn.StartingPosition + key.AsInt();
                    // positionoverride makes a whole soundscape come from a
                    // point, so ambients default there too.
                    sub.AmbientPositionOverride = paramsIn.StartingPosition + key.AsInt();
                }
            }
            else if (Is(key, "ambientpositionoverride"))
            {
                if (paramsIn.AmbientPositionOverride < 0)
                    sub.AmbientPositionOverride = paramsIn.StartingPosition + key.AsInt();
            }
            else if (Is(key, "name"))
            {
                name = key.AsString();
            }
            else if (Is(key, "soundlevel"))
            {
                DevMsg("soundlevel not supported on sub-soundscapes\n");
            }
            else
            {
                DevMsg($"Playsoundscape {name ?? playSoundscape.Name}:Unknown command {key.Name}\n");
            }
        }

        if (name is null)
            return;

        Soundscape? target = _library.ByIndex(_library.IndexOf(name));
        if (target is not null)
            StartSubSoundscape(target.Script, ref sub);
        else
            DevMsg($"Trying to play unknown soundscape {name}\n");

        // Stock passes subParams by reference into StartSubSoundscape and
        // throws it away: what a sub-soundscape writes (the mixer and DSP
        // volume flags) never reaches the caller's params. Reproduced.
    }

    //-------------------------------------------------------------------------
    // Loops
    //-------------------------------------------------------------------------

    // NOTE: reuses an existing entry (fading from its current volume) where it
    // can, which prevents pops.
    private void AddLoopingSound(string wave, bool isAmbient, float volume, int soundLevel, int pitch, Vector position)
    {
        LoopingSound? slot = null;
        bool forceUpdate = false;

        for (int i = _looping.Count - 1; i >= 0; i--)
        {
            LoopingSound sound = _looping[i];

            // NOTE: Will always restart/crossfade positional sounds
            if (sound.Id != _loopingSoundId
                && sound.Pitch == pitch
                && string.Equals(wave, sound.Wave, StringComparison.OrdinalIgnoreCase))
            {
                if (isAmbient && sound.IsAmbient)
                {
                    slot = sound;   // ambient sounds can reuse the slots
                    break;
                }

                if (isAmbient == sound.IsAmbient)
                {
                    if (VectorsAreEqual(position, sound.Position, 0.1f))
                    {
                        slot = sound;
                        break;
                    }

                    // Fading one positional sound out and another in sends
                    // alternating commands about the same (world, static,
                    // wave) sound, which can make it vanish; stock stops the
                    // old one at once and updates the new one immediately.
                    StopLoopingSound(sound);
                    slot = sound;
                    forceUpdate = true;
                    break;
                }
            }
        }

        if (slot is null)
        {
            slot = new LoopingSound();
            _looping.Add(slot);
            if (isAmbient)
            {
                // start at 0 and fade in
                _sounds.EmitAmbient(wave, 0, pitch, 0);
                slot.VolumeCurrent = 0.0f;
            }
            else
            {
                // non-ambients at 0 volume are culled, so start at 0.05
                _sounds.EmitWorld(wave, 0.05f, soundLevel, pitch, 0, position);
                slot.VolumeCurrent = 0.05f;
            }
        }

        slot.Wave = wave;
        slot.VolumeTarget = volume;
        slot.Pitch = pitch;
        slot.Id = _loopingSoundId;
        slot.IsAmbient = isAmbient;
        slot.Position = position;
        slot.SoundLevel = soundLevel;

        if (forceUpdate)
            UpdateLoopingSound(slot);
    }

    private static bool VectorsAreEqual(Vector a, Vector b, float tolerance)
        => MathF.Abs(a.X - b.X) <= tolerance
        && MathF.Abs(a.Y - b.Y) <= tolerance
        && MathF.Abs(a.Z - b.Z) <= tolerance;

    private void StopLoopingSound(LoopingSound sound)
    {
        if (sound.IsAmbient)
            _sounds.EmitAmbient(sound.Wave, 0, 0, SndStop);
        else
            _sounds.StopWorld(sound.Wave);
    }

    private void UpdateLoopingSound(LoopingSound sound)
    {
        if (sound.IsAmbient)
            _sounds.EmitAmbient(sound.Wave, sound.VolumeCurrent, sound.Pitch, SndChangeVol);
        else
            _sounds.EmitWorld(sound.Wave, sound.VolumeCurrent, sound.SoundLevel, sound.Pitch, SndChangeVol, sound.Position);
    }

    //-------------------------------------------------------------------------
    // Random one-shots
    //-------------------------------------------------------------------------

    private void AddRandomSound(RandomSound sound)
    {
        RandomSound added = sound.Copy();
        added.NextPlayTime = (float)(_clock.Now + (0.5 * RandomInterval(sound.Time)));
        _random.Add(added);
    }

    private void PlayRandomSound(RandomSound sound)
    {
        int waveId = _host.RandomInt(0, sound.Waves.Count - 1);
        if (waveId < 0 || waveId >= sound.Waves.Count)
            return;

        string wave = sound.Waves[waveId].AsString() ?? "";

        if (sound.IsAmbient)
        {
            // Argument order is clang's, left to right: volume, then pitch.
            float volume = sound.MasterVolume * RandomInterval(sound.Volume);
            int pitch = (int)RandomInterval(sound.Pitch);
            _sounds.EmitAmbient(wave, volume, pitch, 0);
        }
        else
        {
            float volume = sound.MasterVolume * RandomInterval(sound.Volume);
            int soundLevel = (int)RandomInterval(sound.SoundLevel);
            int pitch = (int)RandomInterval(sound.Pitch);
            if (sound.IsRandom)
                sound.Position = GenerateRandomSoundPosition();

            _sounds.EmitWorld(wave, volume, soundLevel, pitch, 0, sound.Position);
        }
    }

    private Vector GenerateRandomSoundPosition()
    {
        // Degrees drawn and handed to SinCos as RADIANS: stock's, kept.
        float angle = _host.RandomFloat(-180, 180);
        float sin = MathF.Sin(angle);
        float cos = MathF.Cos(angle);

        _audio.MainView(out Vector origin, out Vector forward, out Vector right);
        return origin + (DefaultSoundRadius * ((cos * right) + (sin * forward)));
    }

    private void UpdateRandomSounds(float gameTime)
    {
        if (gameTime < _nextRandomTime)
            return;

        _nextRandomTime = gameTime + 3600;   // check again in an hour

        for (int i = _random.Count - 1; i >= 0; i--)
        {
            RandomSound sound = _random[i];
            if (gameTime >= sound.NextPlayTime)
            {
                PlayRandomSound(sound);
                sound.NextPlayTime = gameTime + RandomInterval(sound.Time);
            }

            if (sound.NextPlayTime < _nextRandomTime)
                _nextRandomTime = sound.NextPlayTime;
        }
    }

    //-------------------------------------------------------------------------
    // Small stock helpers
    //-------------------------------------------------------------------------

    private static bool Is(KeyValuesText key, string name)
        => string.Equals(key.Name, name, StringComparison.OrdinalIgnoreCase);

    private static Interval ReadInterval(KeyValuesText key) => Interval.Parse(key.AsString());

    private float RandomInterval(Interval interval) => interval.Draw(_host.RandomFloat);

    // ATTN_TO_SNDLVL: (int)( a ? 50 + 20 / a : 0 ), in float.
    private static int AttnToSndlvl(float attenuation)
        => attenuation != 0 ? (int)(50f + (20f / attenuation)) : 0;

    // ATTN_TO_SNDLVL( RandomInterval( ... ) ): the macro names its argument
    // twice, so stock draws once for the zero test and AGAIN for the value
    // (soundscape-stock.tsv records both draws). Reproduced.
    private int DrawnAttnToSndlvl(Interval attenuation)
        => RandomInterval(attenuation) != 0 ? (int)(50f + (20f / RandomInterval(attenuation))) : 0;

    private static bool IsNamedSoundLevel(string text)
        => text.StartsWith("SNDLVL_", StringComparison.OrdinalIgnoreCase);

    // "SNDLVL_..." through TextToSoundLevel, anything else an interval drawn now.
    private int ReadSoundLevel(KeyValuesText key)
    {
        string text = key.AsString() ?? "";
        return IsNamedSoundLevel(text)
            ? SoundLevelText.Parse(text, m => DevMsg(m))
            : (int)RandomInterval(Interval.Parse(text));
    }

    private static int Atoi(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return 0;

        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;

        bool negative = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
            negative = s[i++] == '-';

        long value = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i]) && value <= int.MaxValue)
            value = (value * 10) + (s[i++] - '0');

        return (int)(negative ? -value : value);
    }

    private static string FloatText(float value)
        => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    // DevMsg( 1, ... ) and DevMsg( ... ) both print at developer >= 1.
    private void DevMsg(string message)
    {
        if (Developer() >= 1)
            _console.Msg(message);
    }

    private void DevWarning(string message)
    {
        if (Developer() >= 1)
            _console.Warning(message);
    }

    private int Developer()
        => int.TryParse(_console.FindConVarValue("developer"), out int level) ? level : 0;
}
