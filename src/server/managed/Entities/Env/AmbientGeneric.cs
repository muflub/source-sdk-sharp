//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// ambient_generic: the mod's only general-purpose sound emitter, and the
/// single most-placed entity in dm_lockdown that C# did not own — twenty-one of
/// them, naming twenty-one soundscript entries.
///
/// <para>
/// What reaches the engine is not <c>EmitSound</c>, and establishing that was
/// the first thing worth doing here. It is not <c>CSoundEnvelopeController</c>
/// either — that is a separate system the SDK's own doors and elevators use, and
/// this class never touches it. It is <c>UTIL_EmitAmbientSound</c>, which the
/// engine keys by (entity index, sound name) so that a SECOND call carrying
/// <c>SND_CHANGE_PITCH</c> retunes the sound already playing rather than
/// starting another copy of it. Every envelope, LFO, fade and spin-up in
/// <see cref="AmbientModulation"/> is built out of that one property, and none
/// of them can be expressed by starting sounds. That is why this needed an ABI
/// slot of its own rather than reusing <c>EntityEmitSound</c>.
/// </para>
///
/// <para>
/// The arithmetic lives next door in <see cref="AmbientModulation"/>, pure and
/// tested. What is here is the entity: which sound, how far it carries, whether
/// it loops, whether it starts silent, and what the seven inputs mean.
/// </para>
///
/// <para>
/// <b>Effect rather than Logical</b>, and the reason is an entity INDEX. The
/// engine identifies a playing ambient by (entity index, sound name) — that
/// tuple is the channel — so an entity with no index cannot be retuned or
/// stopped independently of any other ambient playing the same sound. A Logical
/// entity is server-only and has no edict, so it has no index: dm_lockdown's
/// three <c>guard_crash_sound</c> entities all name
/// <c>e3_bugbait.alg_crashwall</c>, and on index 0 they would be one channel as
/// far as <c>S_AlterChannel</c> is concerned. Effect is the kind that is
/// networked without carrying a model or collision, which is exactly the shape
/// of stock's <c>CPointEntity</c>-derived ambient. The cost is that it is
/// PVS-transmitted where stock suppressed that with SetTransmit; recorded in
/// docs/divergences.md.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.AmbientGeneric, ScriptedEntityKind.Effect)]
public sealed class AmbientGeneric : ScriptedEntity
{
    public AmbientGeneric(IEnginePrecache precache, IEngineSound sound, IRandomSeeds seeds)
    {
        Precache = precache;
        Sound = sound;
        _lfo = new Random(seeds.Next());
    }

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>making sounds, and what is heard.</summary>
    private readonly IEngineSound Sound;
    /// <summary>Plays everywhere, at full volume, with no falloff at all.</summary>
    private const int FlagEverywhere = 1;

    /// <summary>Does not start with the map. The usual setting for a triggered noise.</summary>
    private const int FlagStartSilent = 16;

    /// <summary>One shot per trigger rather than a loop that has to be turned off.</summary>
    private const int FlagNotLooping = 32;

    /// <summary>What a map may set on this entity. See <see cref="AmbientGenericVars"/>.</summary>
    [ScriptVars] public AmbientGenericVars Vars = new();

    private readonly AmbientModulation _modulation = new();

    /// <summary>
    /// The dB level the radius works out to, and the one number the engine's
    /// falloff is driven by. <c>SNDLVL_NONE</c> when the sound plays everywhere.
    /// </summary>
    private int _soundLevel;

    /// <summary>True only while a LOOPING sound is running. A one-shot is never active.</summary>
    private bool _active;

    private bool _looping;

    /// <summary>
    /// The entity index the sound is attached to, remembered rather than looked
    /// up each time.
    ///
    /// <para>
    /// It has to outlive the source entity. A stop must reach the same index the
    /// sound STARTED on or the engine has nothing to match, and by the time a
    /// map removes the thing a sound was coming from, asking it for its index is
    /// too late.
    /// </para>
    /// </summary>
    private int _soundSourceIndex = -1;

    private Vector _soundSourceOrigin;

    /// <summary>Whether the source name has been looked for yet. Distinct from having found it.</summary>
    private bool _sourceResolved;

    /// <summary>
    /// The engine's reference distance for the dB curve. 36 units is a stock
    /// constant, and every radius in every map is tuned against it.
    /// </summary>
    private const float ReferenceDistance = 36.0f;

    /// <summary>
    /// A radius, as a soundlevel in dB.
    ///
    /// 40dB at the reference distance, falling 20dB per decade beyond it — the
    /// inverse-square law written the way the mixer wants it. Zero means "no
    /// level", which the engine reads as no falloff at all, and that is also
    /// what the Everywhere flag asks for.
    /// </summary>
    public static int ComputeSoundLevel(float radius, bool playEverywhere)
    {
        if (radius <= 0f || playEverywhere)
            return 0;

        double loss = 20.0 * Math.Log10(radius / ReferenceDistance);
        return (int)(40 + loss);
    }

    public override void OnSpawn()
    {
        _soundLevel = ComputeSoundLevel(Vars.Radius, (SpawnFlags & FlagEverywhere) != 0);

        if (Vars.Sound.Length == 0)
        {
            // Stock warns and removes itself. Kept, because an ambient with no
            // sound is a map bug and a silent one is indistinguishable from a
            // sound that failed to play.
            Log.Warning($"[SourceSharp] ambient_generic '{Name}': no sound named; removing\n");
            Remove();
            return;
        }

        _looping = (SpawnFlags & FlagNotLooping) == 0;

        // A sentence is looked up in the sentence table rather than precached.
        if (Vars.Sound[0] != '!')
            Precache.PrecacheSound(Vars.Sound);

        // Starts with the map unless told otherwise, and only a loop can: a
        // one-shot that "started" would just have finished.
        _active = (SpawnFlags & FlagStartSilent) == 0 && _looping;

        _modulation.Init(Vars.Health);

        if (!_active || _modulation.Volume <= 0)
            return;

        // Stock does this from Activate rather than Spawn, because it has to
        // resolve SourceEntityName first and that name may belong to an entity
        // further down the map file. Doing it here instead is deliberate and
        // costs only that case: BOTH run inside the window where the engine still
        // accepts signon writes (ServerActivate is called before
        // sv.allowsignonwrites is cleared), so SND_SPAWNING still puts the loop
        // in the signon buffer and a client that joins later still hears it.
        //
        // Where a source entity IS named, the start is deferred by one frame so
        // the name can resolve — see SourceIndex. That trades the signon buffer
        // for a correct position, which is the right way round: a sound in the
        // wrong place is worse than a sound broadcast a frame late.
        if (Vars.SourceEntityName.Length != 0)
        {
            SetNextThink(0f);
            return;
        }

        // Pitch and volume are forced onto the opening update. Without them the
        // engine takes the sound's own, and every later retune has nothing to
        // change from.
        Send(SoundUpdate.Spawning | SoundUpdate.ChangePitch | SoundUpdate.ChangeVolume);
        SetNextThink(0.1f);
    }

    public override bool OnKeyValue(string key, string value)
        => _modulation.ApplyKey(key, value);

    // --- inputs ---

    [ScriptedEvent("PlaySound")]
    private void OnPlaySound(InputData input)
    {
        if (_active)
            return;

        // Stopped first, deliberately: a non-looping sound may still be running
        // from a previous trigger, and starting a second copy on the same
        // channel is what makes a repeatedly-triggered ambient stack up.
        Send(SoundUpdate.Stop);
        Toggle();
    }

    [ScriptedEvent("StopSound")]
    private void OnStopSound(InputData input)
    {
        if (_active)
            Toggle();
    }

    [ScriptedEvent("ToggleSound")]
    private void OnToggleSound(InputData input) => Toggle();

    [ScriptedEvent("Use")]
    private void OnUsed(InputData input) => Toggle();

    /// <summary>Retunes the sound that is already playing. 0..255, 100 as recorded.</summary>
    [ScriptedEvent("Pitch")]
    private void OnPitch(InputData input)
    {
        _modulation.Pitch = Math.Clamp((int)input.Number, 0, 255);
        Send(SoundUpdate.ChangePitch);
    }

    /// <summary>Retunes the volume. 0..10 from the map, 0..100 inside.</summary>
    [ScriptedEvent("Volume")]
    private void OnVolume(InputData input)
    {
        _modulation.Volume = Math.Clamp((int)Math.Round(input.Number * 10f), 0, 100);
        _modulation.VolumeFraction = _modulation.Volume << 8;
        Send(SoundUpdate.ChangeVolume);
    }

    /// <summary>Fades up over the given seconds, cancelling any fade out.</summary>
    [ScriptedEvent("FadeIn")]
    private void OnFadeIn(InputData input)
    {
        _modulation.FadeOut = 0;
        _modulation.FadeIn = RampSeconds(input.Number);
        SetNextThink(0.1f);
    }

    /// <summary>Fades down over the given seconds, cancelling any fade in.</summary>
    [ScriptedEvent("FadeOut")]
    private void OnFadeOut(InputData input)
    {
        _modulation.FadeIn = 0;
        _modulation.FadeOut = RampSeconds(input.Number);
        SetNextThink(0.1f);
    }

    /// <summary>
    /// Seconds, as the 8.8 fixed-point step taken five times a second. Zero
    /// seconds is zero steps, which reads as "no fade" rather than an instant
    /// one — that is stock, and it is why the input has no way to say "now".
    /// </summary>
    private static int RampSeconds(float seconds)
    {
        int n = (int)Math.Clamp(seconds, 0f, 100f);
        return n > 0 ? (100 << 8) / (n * AmbientModulation.UpdateRate) : 0;
    }

    // --- the running sound ---

    public override void OnThink()
    {
        // The first think after a deferred start. Only reached when a source
        // entity was named; see OnSpawn.
        if (_active && _soundSourceIndex < 0)
        {
            Send(SoundUpdate.ChangePitch | SoundUpdate.ChangeVolume);
            SetNextThink(0.1f);
            return;
        }

        AmbientStep step = _modulation.Step(_nextLfoValue ??= NextLfoValue);

        if (step.Silence)
        {
            Send(SoundUpdate.Stop);
            return;
        }

        if (step.Changed)
        {
            Sound.EmitAmbientSound(SourceIndex(), SourceOrigin(), Vars.Sound,
                                    step.Volume * 0.01f, _soundLevel,
                                    (int)step.Flags, AmbientModulation.Sendable(step.Pitch));
        }

        if (step.KeepThinking)
            SetNextThink(AmbientModulation.ThinkDelay);
    }

    // This sound's own LFO stream, seeded explicitly and differently per
    // instance from the side's IRandomSeeds: a static one shared by every
    // ambient_generic was mutable global state.
    private readonly Random _lfo;

    private int NextLfoValue() => _lfo.Next(0, 256);

    // Built once rather than per think: an instance method group is a new
    // delegate each time it is named, where the static one it replaced was
    // cached by the compiler.
    private Func<int>? _nextLfoValue;

    /// <summary>
    /// Turns the sound on or off, which for a counted spin-up means neither.
    ///
    /// <para>
    /// Only a LOOPING sound is ever marked active. A one-shot plays once per
    /// trigger and is stopped first, so re-triggering restarts it rather than
    /// layering.
    /// </para>
    /// </summary>
    private void Toggle()
    {
        if (_active)
        {
            // A counted spin-up never switches off: each trigger takes the pitch
            // one step closer to the top instead. Once the steps are used up,
            // further triggers do nothing at all — which is stock, and is what
            // makes this the "wind it up" ambient rather than a toggle.
            if (_modulation.CountedSpinUp != 0)
            {
                if (_modulation.AdvanceCountedSpinUp())
                    SetNextThink(0.1f);

                return;
            }

            _active = false;

            if (_modulation.BeginRampDown())
                SetNextThink(0.1f);
            else
                Send(SoundUpdate.Stop);

            return;
        }

        if (_looping)
        {
            _active = true;
        }
        else
        {
            // Stopped before starting, so a long one-shot still running from the
            // last trigger is replaced rather than doubled.
            Send(SoundUpdate.Stop);
        }

        _modulation.Init(Vars.Health);

        Send(SoundUpdate.None);
        SetNextThink(0.1f);
    }

    public override void OnRemoved()
    {
        // A loop outlives its entity otherwise: the engine has no idea the
        // thing that started it has gone, and the noise runs until the map
        // changes.
        if (_active)
            Send(SoundUpdate.Stop);
    }

    /// <summary>
    /// One update to the ambient channel. A stop carries no volume, level or
    /// pitch, because the engine reads none of them for one.
    /// </summary>
    private void Send(SoundUpdate flags)
    {
        int index = SourceIndex();

        if (index < 0)
            return;

        if (flags == SoundUpdate.Stop)
        {
            Sound.EmitAmbientSound(index, SourceOrigin(), Vars.Sound, 0f, 0, (int)flags, 0);
            return;
        }

        Sound.EmitAmbientSound(index, SourceOrigin(), Vars.Sound,
                                _modulation.Volume * 0.01f, _soundLevel, (int)flags,
                                _modulation.Pitch);
    }

    /// <summary>
    /// Which entity the sound comes from, resolved once and then remembered.
    ///
    /// <para>
    /// Remembered rather than re-resolved because the index has to outlive the
    /// entity: a stop must reach the index the sound started on, and a source a
    /// map removes would otherwise take the ability to silence the loop with it.
    /// </para>
    ///
    /// <para>
    /// A name that finds nothing falls back to this entity, which is stock — a
    /// misspelled SourceEntityName leaves the sound where the mapper drew it
    /// rather than making it disappear.
    /// </para>
    /// </summary>
    private int SourceIndex()
    {
        if (_soundSourceIndex >= 0)
        {
            // A moving source has to be followed. Re-read every send rather than
            // cached, because a sound attached to a lift is meant to travel with
            // it and the client is told where it is on each update.
            if (Entities.IsValid(_sourceHandle))
                _soundSourceOrigin = Entities.GetOrigin(_sourceHandle);

            return _soundSourceIndex;
        }

        if (!_sourceResolved)
        {
            _sourceResolved = true;

            if (Vars.SourceEntityName.Length != 0)
            {
                var found = Entities.FindByName(Vars.SourceEntityName);

                if (Entities.IsValid(found))
                {
                    _sourceHandle = found;
                    _soundSourceIndex = Entities.GetIndex(found);
                    _soundSourceOrigin = Entities.GetOrigin(found);

                    if (_soundSourceIndex >= 0)
                        return _soundSourceIndex;
                }
                else
                {
                    Log.Warning($"[SourceSharp] ambient_generic '{Name}': no entity named " +
                                   $"'{Vars.SourceEntityName}'; playing from here instead\n");
                }
            }
        }

        _sourceHandle = Handle;
        _soundSourceIndex = Entities.GetIndex(Handle);
        _soundSourceOrigin = Origin;

        return _soundSourceIndex;
    }

    private EntityHandle _sourceHandle;

    private Vector SourceOrigin() => _soundSourceOrigin;
}

/// <summary>
/// The map keys <see cref="AmbientGeneric"/> declares.
/// </summary>
[ScriptVars]
public partial class AmbientGenericVars
{
    /// <summary>
    /// The sound: a soundscript entry ("k_lab.ambient_powergenerators"), a path
    /// to a wave, or a "!SENTENCE" name. Spelled "message" because that is the
    /// key Hammer writes, from a time when an entity had a handful of general
    /// fields and reused whichever was free.
    /// </summary>
    [VarName("message")]
    public string Sound { get; set; } = string.Empty;

    /// <summary>
    /// How far it carries, in units. Turned into a dB soundlevel at spawn, which
    /// is what the engine actually wants; a radius is what a mapper can judge.
    /// </summary>
    public float Radius { get; set; }

    /// <summary>
    /// Another entity the sound should come from, so it can move. Resolved
    /// lazily rather than at spawn — see <see cref="SourceIndex"/>.
    /// </summary>
    public string SourceEntityName { get; set; } = string.Empty;

    /// <summary>
    /// Where a mapper types the volume, 0..10. Hammer labels this field Health.
    /// Read once, at <see cref="AmbientModulation.Init"/>, exactly as stock does.
    /// </summary>
    public int Health { get; set; }
}
