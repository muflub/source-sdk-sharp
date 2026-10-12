//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// Sound envelopes — <c>CSoundControllerImp</c>, in C#.
///
/// <para>
/// Both sides' (sound cluster 17, lane c2-sims). The server's
/// <c>SoundController</c> is this with its frame and level placement; the
/// client's is this ticked once a frame by the client module. On each side a
/// shim (<c>shell_soundenvelope.cpp</c> on the server,
/// <c>shell_soundenvelope_client.cpp</c> on the client) answers
/// <c>CSoundEnvelopeController::GetController()</c> and forwards here, so the
/// compiled call sites (the two sides' <c>physics.cpp</c> friction sounds) are
/// unedited. The client half is held bit for bit to stock's client controller
/// by <c>SoundEnvelopeOracleTests</c> (<c>Fixtures/soundenvelope-stock.tsv</c>).
/// </para>
///
/// <para>
/// <b>An ENVELOPE is a value approaching a target at a rate.</b> That is the
/// whole of what moved: volume and pitch each ramp toward a target over a
/// number of seconds, and a queue schedules further ramps ahead of time. None
/// of it touches the engine. What did NOT move is everything that does: a
/// recipient filter, an entity handle, a sound-script lookup and an
/// <c>EmitSound_t</c> all stayed in the shim, and a patch crosses as an
/// <c>int</c> handle so that a pointer to one is never seen here.
/// </para>
///
/// <para>
/// <b>Every rule below is stock's, including the odd ones.</b> The rate floor
/// of 0.1 that makes a very small ramp take longer than asked; the volume clamp
/// at 1.0 on the target and not on the current value; the "force update" flag
/// that makes ONE more emit happen after a ramp has arrived; and
/// <c>SND_CHANGE_VOL</c> being set on every emit whatever changed, because the
/// envelope is a MULTIPLIER on the script's volume and an emit without it would
/// use the script's number raw.
/// </para>
/// </summary>
public class SoundEnvelopeController : ISoundPatchRamps
{
    /// <summary><c>soundcommands_t</c> — soundenvelope.h.</summary>
    public const int CommandChangeVolume = 0;

    /// <inheritdoc cref="CommandChangeVolume"/>
    public const int CommandChangePitch = 1;

    /// <inheritdoc cref="CommandChangeVolume"/>
    public const int CommandStop = 2;

    /// <inheritdoc cref="CommandChangeVolume"/>
    public const int CommandDestroy = 3;

    /// <summary>
    /// <c>SND_CHANGE_VOL</c> — <c>public/soundflags.h</c>'s <c>SoundFlags_t</c>.
    ///
    /// <para>
    /// <b>BIT ZERO, and this was wrong first.</b> It was written as
    /// <c>1 &lt;&lt; 6</c> from memory, which is <c>SND_SPEAKER</c> — "being
    /// played again by a microphone through a speaker". Every ramp would have
    /// gone out flagged as a speaker replay and without the volume-change flag
    /// the engine needs, so the engine would have used the sound script's raw
    /// volume and the whole envelope would have been silent. It compiled, and
    /// <see cref="TheSoundFlagsMatchSoundflagsH"/> is what caught it.
    /// </para>
    /// </summary>
    public const int SndChangeVolume = 1 << 0;

    /// <summary>
    /// <c>SND_CHANGE_PITCH</c>. Bit one, for
    /// <see cref="SndChangeVolume"/>'s reason — <c>1 &lt;&lt; 7</c> is
    /// <c>SND_SHOULDPAUSE</c>.
    /// </summary>
    public const int SndChangePitch = 1 << 1;

    /// <summary>
    /// One ramp — stock's <c>CSoundEnvelope</c>.
    ///
    /// <para>
    /// A class rather than a struct, because a patch holds two of them and the
    /// code that moves one is written as <c>envelope.SetTarget(...)</c>. Stock's
    /// is a member object and this is the same object.
    /// </para>
    /// </summary>
    public sealed class Envelope
    {
        private float _current;
        private float _target;
        private float _rate;
        private bool _forceUpdate;

        /// <summary>Where the ramp is now.</summary>
        public float Value => _current;

        /// <summary>
        /// <c>CSoundEnvelope::SetTarget</c>: reach <paramref name="target"/>
        /// <paramref name="deltaTime"/> seconds from now.
        ///
        /// <para>
        /// <b>The rate has a FLOOR of 0.1 and that is stock's</b>, not a
        /// rounding guard: a ramp of 0.01 over ten seconds would be asked to
        /// move at 0.001 a second and is moved at 0.1 instead, so it arrives
        /// early. Reproduced rather than tidied — a mod tuned against stock's
        /// timing is tuned against this.
        /// </para>
        ///
        /// <para>
        /// A zero delta or a zero time is an INSTANT set, which is the path
        /// <c>Play</c> and every <c>ChangeVolume(x, 0)</c> take.
        /// </para>
        /// </summary>
        public void SetTarget(float target, float deltaTime)
        {
            float deltaValue = target - _current;

            if (deltaValue != 0f && deltaTime > 0f)
            {
                _target = target;
                _rate = MathF.Max(0.1f, MathF.Abs(deltaValue / deltaTime));
            }
            else
            {
                if (target != _current)
                    _forceUpdate = true;

                SetValue(target);
            }
        }

        /// <summary>
        /// <c>CSoundEnvelope::SetValue</c> — arrive now.
        ///
        /// <para>
        /// The force-update flag is set when the TARGET moves, not when the
        /// current value does, and the difference is real: a ramp already
        /// heading somewhere that is told to arrive there instantly has moved
        /// its current value and not its target, and stock does not force an
        /// emit for that.
        /// </para>
        /// </summary>
        public void SetValue(float value)
        {
            if (_target != value)
                _forceUpdate = true;

            _current = _target = value;
            _rate = 0f;
        }

        /// <summary>
        /// <c>CSoundEnvelope::ShouldUpdate</c>. <b>It CONSUMES the force
        /// flag</b>, which is why it is not a property and why calling it twice
        /// in one frame is a bug: the second call would answer false and the
        /// emit that the flag existed to cause would not happen.
        /// </summary>
        public bool ShouldUpdate()
        {
            if (_forceUpdate)
            {
                _forceUpdate = false;
                return true;
            }

            return _current != _target;
        }

        /// <summary><c>CSoundEnvelope::Update</c> — Approach, at the rate.</summary>
        public void Update(float deltaTime)
        {
            float speed = _rate * deltaTime;
            float delta = _target - _current;

            if (delta > speed)
                _current += speed;
            else if (delta < -speed)
                _current -= speed;
            else
                _current = _target;
        }
    }

    /// <summary>
    /// The managed half of one <c>CSoundPatch</c>. The native half — the
    /// filter, the entity, the script — is the shim's, and this never holds a
    /// pointer to it.
    /// </summary>
    private sealed class Patch
    {
        public readonly Envelope Pitch = new();
        public readonly Envelope Volume = new();

        /// <summary>The <c>SND_*</c> set accumulated since the last emit.</summary>
        public int Flags;

        /// <summary>When a fade-out should stop it. Zero is "never".</summary>
        public float ShutdownTime;

        /// <summary>Whether <c>StartSound</c> has been sent for it.</summary>
        public bool IsPlaying;
    }

    /// <summary>
    /// One queued ramp. Stock's <c>SoundCommand_t</c>.
    /// </summary>
    private readonly struct Command
    {
        public Command(int patch, float time, int command, float deltaTime, float value)
        {
            PatchHandle = patch;
            Time = time;
            Command_ = command;
            DeltaTime = deltaTime;
            Value = value;
        }

        public int PatchHandle { get; }
        public float Time { get; }
        public int Command_ { get; }
        public float DeltaTime { get; }
        public float Value { get; }
    }

    /// <summary>
    /// The four things only the shim can do to a patch: emit it, stop it, say
    /// whether its entity and its listeners are still there, and free it.
    /// </summary>
    private readonly IEngineSound _patches;

    private readonly IGameClock _clock;

    /// <summary>
    /// <c>RandomFloat</c>, for the envelope points. <see cref="IEngineHost"/>
    /// rather than <c>System.Random</c> because it is the ENGINE's stream:
    /// stock's envelope expansion draws from the shared server random, and a
    /// second stream here would desynchronise anything that counts draws.
    /// </summary>
    private readonly IEngineHost _host;

    /// <summary>Where the two envelope complaints go. Injected, so a test can read them.</summary>
    private readonly IEngineConsole _console;

    /// <summary>Every live patch, by handle.</summary>
    private readonly Dictionary<int, Patch> _known = new();

    /// <summary>
    /// The PLAYING patches, in the order they started. Stock's
    /// <c>m_soundList</c>, and it is not the same set as <see cref="_known"/>:
    /// a patch exists from <c>SoundCreate</c> and joins this list at
    /// <c>Play</c>.
    /// </summary>
    private readonly List<int> _playing = new();

    /// <summary>
    /// The queue, kept sorted by time. A list rather than a heap because stock's
    /// priority queue is walked from the head every frame and cleared per patch
    /// by a linear scan — the operations are "take the earliest" and "remove
    /// everything belonging to X", and the second is what decides the shape.
    /// </summary>
    private readonly List<Command> _commands = new();

    private float _lastTime;

    public SoundEnvelopeController(IEngineSound patches, IGameClock clock, IEngineHost host,
                           IEngineConsole console)
    {
        _patches = patches;
        _clock = clock;
        _host = host;
        _console = console;
    }

    // ---- the shim's half ---------------------------------------------------

    /// <summary>How many patches exist. For tests and the live meter.</summary>
    public int KnownCount => _known.Count;

    /// <summary>How many are in the playing list.</summary>
    public int PlayingCount => _playing.Count;

    /// <summary>How many ramps are queued ahead.</summary>
    public int QueuedCount => _commands.Count;

    /// <summary>A patch has been built natively and is now tracked here.</summary>
    public void Create(int patch) => _known[patch] = new Patch();

    /// <summary>
    /// <c>SoundDestroy</c>'s managed half: shut it down, drop its queue, unlist
    /// it, forget it. The native object is freed by the shim afterwards.
    /// </summary>
    public void Destroy(int patch)
    {
        Shutdown(patch);
        _known.Remove(patch);
    }

    /// <summary>
    /// <c>CSoundControllerImp::Play</c>. Reset the shutdown time, set both ramps
    /// instantly, then either clear the queue (already playing) or list it and
    /// start it.
    /// </summary>
    public void Play(int patch, float volume, float pitch, float startTime)
    {
        if (!_known.TryGetValue(patch, out var p))
            return;

        // pSound->Reset() -- clears the shutdown time and nothing else.
        p.ShutdownTime = 0f;

        ChangeVolume(patch, volume, 0f);
        ChangePitch(patch, pitch, 0f);

        if (p.IsPlaying)
        {
            // Stock: a patch told to Play again keeps playing and loses its
            // queued ramps, rather than restarting.
            CommandClear(patch);
            return;
        }

        _playing.Add(patch);
        p.IsPlaying = true;

        // StartSound: SND_CHANGE_VOL plus the base flags, and a caption. The
        // shim ORs the base flags in, because they are its own.
        _patches.SoundPatchEmit(patch, p.Volume.Value, p.Pitch.Value, SndChangeVolume,
                                startTime, emitCaption: true);

        p.Flags = 0;
    }

    /// <summary><c>CSoundPatch::ChangePitch</c>.</summary>
    public void ChangePitch(int patch, float target, float deltaTime)
    {
        if (!_known.TryGetValue(patch, out var p))
            return;

        p.Flags |= SndChangePitch;
        p.Pitch.SetTarget(target, deltaTime);
    }

    /// <summary>
    /// <c>CSoundPatch::ChangeVolume</c>. <b>The TARGET is clamped to 1, not the
    /// current value</b> — a patch already above one ramps down to one rather
    /// than being snapped there, which is stock's and is observable.
    /// </summary>
    public void ChangeVolume(int patch, float target, float deltaTime)
    {
        if (!_known.TryGetValue(patch, out var p))
            return;

        p.Flags |= SndChangeVolume;

        if (target > 1.0f)
            target = 1.0f;

        p.Volume.SetTarget(target, deltaTime);
    }

    /// <summary>
    /// <c>CSoundControllerImp::SoundFadeOut</c>'s managed half. The
    /// zero-time-and-destroy case never reaches here: it is a destroy, and a
    /// destroy frees the native object, so the shim keeps it.
    /// </summary>
    public void FadeOut(int patch, float deltaTime, bool destroyOnFadeout)
    {
        if (!_known.TryGetValue(patch, out var p))
            return;

        ChangeVolume(patch, 0f, deltaTime);

        if (destroyOnFadeout)
        {
            // A queued DESTROY rather than a shutdown time. The two are not
            // interchangeable: the shutdown time STOPS the sound and leaves the
            // patch, and the command frees it.
            CommandAdd(patch, deltaTime, CommandDestroy, 0f, 0f);
        }
        else
        {
            p.ShutdownTime = _clock.Now + deltaTime;
        }
    }

    /// <summary>
    /// <c>CSoundControllerImp::Shutdown( patch )</c> — stop it now, drop its
    /// queued ramps, unlist it. The patch survives; <see cref="Destroy"/> is
    /// what forgets it.
    /// </summary>
    public void Shutdown(int patch)
    {
        if (!_known.TryGetValue(patch, out var p))
            return;

        StopPatch(patch, p);
        CommandClear(patch);
        _playing.Remove(patch);
    }

    /// <summary><c>CSoundControllerImp::CommandAdd</c>.</summary>
    public void CommandAdd(int patch, float executeDeltaTime, int command,
                           float commandTime, float value)
    {
        Insert(new Command(patch, _clock.Now + executeDeltaTime, command,
                           commandTime, value));
    }

    /// <summary>
    /// <c>CSoundControllerImp::CommandClear</c> — every queued ramp belonging to
    /// one patch.
    /// </summary>
    public void CommandClear(int patch) => _commands.RemoveAll(c => c.PatchHandle == patch);

    /// <summary>
    /// <c>CSoundControllerImp::SoundPlayEnvelope</c>: expand a list of points
    /// into queued ramps, and answer the total duration.
    ///
    /// <para>
    /// <b>A point may say "keep the last one" with -1</b>, in BOTH its
    /// amplitude and its duration, and independently. Stock complains only when
    /// the FIRST point does it — there is no last one to keep — and then carries
    /// on with zero, which is reproduced.
    /// </para>
    ///
    /// <para>
    /// <b>The test is <c>min != -1 || max != -1</c>, not a range check.</b> A
    /// point saying <c>(-1, 5)</c> is a random pick between -1 and 5, and stock
    /// takes it as one.
    /// </para>
    /// </summary>
    /// <param name="points">
    /// Four floats per point — amplitudeMin, amplitudeMax, durationMin,
    /// durationMax — which is <c>envelopePoint_t</c>'s layout flattened by the
    /// shim.
    /// </param>
    public float PlayEnvelope(int patch, int command, ReadOnlySpan<float> points, int count)
    {
        float amplitude = 0f;
        float duration = 0f;
        float total = 0f;

        CommandClear(patch);

        for (int i = 0; i < count; i++)
        {
            float amplitudeMin = points[(i * 4) + 0];
            float amplitudeMax = points[(i * 4) + 1];
            float durationMin = points[(i * 4) + 2];
            float durationMax = points[(i * 4) + 3];

            if (amplitudeMin != -1.0f || amplitudeMax != -1.0f)
                amplitude = _host.RandomFloat(amplitudeMin, amplitudeMax);
            else if (i == 0)
                _console.Msg("Invalid starting amplitude value in envelope!  (Cannot be -1)\n");

            if (durationMin != -1.0f || durationMax != -1.0f)
                duration = _host.RandomFloat(durationMin, durationMax);
            else if (i == 0)
                _console.Msg("Invalid starting duration value in envelope! (Cannot be -1)\n");

            // The command executes at the RUNNING total, so the points play in
            // sequence, and it carries its own duration as the ramp time.
            CommandAdd(patch, total, command, duration, amplitude);

            total += duration;
        }

        return total;
    }

    /// <summary>
    /// <c>CSoundControllerImp::SystemReset</c> — shut every live patch down and
    /// empty both lists.
    ///
    /// <para>
    /// The KNOWN set is not cleared, and that is deliberate rather than an
    /// oversight: the native objects still exist and the shim still holds them,
    /// so forgetting them here would make a later <c>SoundDestroy</c> — which
    /// the shim performs on its own list — reach a handle this side has never
    /// heard of. Stock has the same asymmetry for the same reason: it purges
    /// <c>m_soundList</c> and the patches themselves belong to their entities.
    /// </para>
    /// </summary>
    public void SystemReset()
    {
        for (int i = _playing.Count - 1; i >= 0; i--)
        {
            if (_known.TryGetValue(_playing[i], out var p))
                StopPatch(_playing[i], p);
        }

        _playing.Clear();
        _commands.Clear();
    }

    /// <summary>The envelope's current pitch. <c>SoundGetPitch</c>.</summary>
    public float GetPitch(int patch)
        => _known.TryGetValue(patch, out var p) ? p.Pitch.Value : 0f;

    /// <summary>The envelope's current volume. <c>SoundGetVolume</c>.</summary>
    public float GetVolume(int patch)
        => _known.TryGetValue(patch, out var p) ? p.Volume.Value : 0f;

    // ---- the frame ---------------------------------------------------------

    /// <summary>
    /// <c>CSoundControllerImp::SystemUpdate</c>: run the ramps due, move the
    /// live ones, and drop the ones that have ended.
    /// </summary>
    public void SystemUpdate()
    {
        float now = _clock.Now;
        float deltaTime = now - _lastTime;

        // "handle clock resets" -- stock's comment. A map change moves curtime
        // backwards and a negative delta would run every ramp in reverse.
        if (deltaTime < 0f)
            deltaTime = 0f;

        _lastTime = now;

        // Commands are sorted by time, so the head is the earliest and the walk
        // stops at the first one that is not due yet.
        while (_commands.Count > 0 && now >= _commands[0].Time)
        {
            Command cmd = _commands[0];
            _commands.RemoveAt(0);
            Process(cmd);
        }

        // END TO BEGINNING, so a patch removed inside the loop does not move the
        // ones not yet visited. Stock's own note.
        for (int i = _playing.Count - 1; i >= 0; i--)
        {
            int handle = _playing[i];

            if (!_known.TryGetValue(handle, out var p) || !UpdatePatch(handle, p, now, deltaTime))
            {
                // pNode->Reset() then FastRemove. Reset clears the shutdown
                // time, so a patch played again later is not immediately shut
                // down by the time that ended it.
                if (p is not null)
                    p.ShutdownTime = 0f;

                _playing.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// <c>CSoundPatch::Update</c>: false when the patch should leave the list.
    /// </summary>
    private bool UpdatePatch(int handle, Patch p, float now, float deltaTime)
    {
        if (p.ShutdownTime != 0f && now > p.ShutdownTime)
        {
            StopPatch(handle, p);
            return false;
        }

        _patches.SoundPatchState(handle, out int entIndex, out bool filterActive);

        if (entIndex < 0)
        {
            // Stock's DevWarning and stock's FIXME with it: nothing owns the
            // patch any more, so leaving the list is the only thing that can be
            // done about it here.
            _console.Warning($"[SourceSharp/SoundController] patch {handle} has no entity; dropping it\n");
            return false;
        }

        if (p.Pitch.ShouldUpdate())
        {
            p.Pitch.Update(deltaTime);
            p.Flags |= SndChangePitch;
        }
        else
        {
            p.Flags &= ~SndChangePitch;
        }

        if (p.Volume.ShouldUpdate())
        {
            p.Volume.Update(deltaTime);
            p.Flags |= SndChangeVolume;
        }
        else
        {
            p.Flags &= ~SndChangeVolume;
        }

        if (p.Flags != 0 && filterActive)
        {
            // SND_CHANGE_VOL ALWAYS, whatever changed. Stock's comment says why
            // and it is not defensive: the envelope's volume is a MULTIPLIER on
            // the one in the sound script, so an emit without this flag makes
            // the engine use the script's number raw and the ramp vanishes for
            // that tick.
            _patches.SoundPatchEmit(handle, p.Volume.Value, p.Pitch.Value,
                                    p.Flags | SndChangeVolume, 0f, emitCaption: false);

            p.Flags = 0;
        }

        return true;
    }

    /// <summary><c>CSoundControllerImp::ProcessCommand</c>.</summary>
    private void Process(in Command cmd)
    {
        switch (cmd.Command_)
        {
            case CommandChangeVolume:
                ChangeVolume(cmd.PatchHandle, cmd.Value, cmd.DeltaTime);
                break;

            case CommandChangePitch:
                ChangePitch(cmd.PatchHandle, cmd.Value, cmd.DeltaTime);
                break;

            case CommandStop:
                if (_known.TryGetValue(cmd.PatchHandle, out var stopping))
                    StopPatch(cmd.PatchHandle, stopping);
                break;

            case CommandDestroy:
                // Stock removes it from the list, shuts it down and DELETES the
                // patch. The delete is native, so what happens here is the
                // first two and a request; the shim frees the object.
                Shutdown(cmd.PatchHandle);
                _known.Remove(cmd.PatchHandle);
                _patches.SoundPatchFree(cmd.PatchHandle);
                break;
        }
    }

    private void StopPatch(int handle, Patch p)
    {
        if (!p.IsPlaying)
            return;

        _patches.SoundPatchStop(handle);
        p.IsPlaying = false;
    }

    /// <summary>
    /// Insert keeping the list sorted by time. Stock uses a priority queue whose
    /// less-function is "a GREATER time means LESS priority", i.e. earliest
    /// first; this is that order, and ties keep insertion order — which
    /// <see cref="PlayEnvelope"/> depends on, because a zero-duration point
    /// queues a command at the same time as the one before it.
    /// </summary>
    private void Insert(in Command cmd)
    {
        int at = _commands.Count;

        while (at > 0 && _commands[at - 1].Time > cmd.Time)
            at--;

        _commands.Insert(at, cmd);
    }
}
