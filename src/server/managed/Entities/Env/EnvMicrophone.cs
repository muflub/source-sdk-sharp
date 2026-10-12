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
/// env_microphone: a meter, not a speaker. It samples how loud the world is at a
/// point and puts the number on an output five times a second.
///
/// <para>
/// What it listens to is NOT the sounds a player hears. It reads
/// <c>CSoundEnt</c>, the world's record of things having happened loudly
/// somewhere — a shot, a bullet striking a wall, a body landing hard — which is
/// the list the AI used to read and which nothing managed could see at all
/// until <c>SoundEntQuery</c>. That is why this entity needed a subsystem
/// rather than a class: the sounds have no names, no channels and no listeners,
/// only a place, a loudness and a kind.
/// </para>
///
/// <para>
/// <b>The list is managed now</b> — plan_gamedll_after.md Phase A3, second
/// pair — so <c>SoundEntQuery</c> is <see cref="IServerSoundEnt.Query"/> and
/// there is no boundary crossing left on this path at all. The read used to be
/// the hottest traced call in <c>shell_sound.cpp</c>: 19,241 lines in one
/// session, one per AI think.
/// </para>
///
/// <para>
/// <b>The speaker half is not carried over</b>, and it is separable from this
/// one by a keyvalue rather than by a judgement — stock only joins the list of
/// routing microphones when <c>SpeakerName</c> is set. Recorded in
/// docs/divergences.md with the measurement behind it; a map that sets the key
/// is told so rather than left wondering.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvMicrophone)]
public sealed class EnvMicrophone : ScriptedEntity
{
    // WHAT IT TAKES CHANGED IN Phase A3's SECOND PAIR, and the change is the
    // finding rather than the plumbing: it used to take IEngineSound, because
    // the list it reads was on the far side of the boundary and reaching it
    // meant an engine slot. game/server/soundent.cpp has left
    // server_csharp.vpc, so the list is IServerSoundEnt and this is an ordinary
    // service dependency.
    public EnvMicrophone(IServerSoundEnt sounds)
    {
        Sounds = sounds;
    }

    /// <summary>The world's record of what recently made a noise.</summary>
    private readonly IServerSoundEnt Sounds;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>
    /// The boxes a mapper ticks on this entity, and nothing else's — these are
    /// env_microphone's spawnflags, numbered from one upward the way every
    /// entity's are.
    ///
    /// <para>
    /// Deliberately a separate type from <see cref="SoundType"/>, which the
    /// mapping below turns these into. They look alike and are not: these are a
    /// checkbox list this entity owns, those are the engine's own SOUND_* bits,
    /// and they do not line up — <see cref="Explosion"/> is bit six here and a
    /// CONTEXT bit twenty places up there. One enum covering both would make
    /// that mismatch invisible, which is the bug it exists to prevent.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        Combat = 0b0000_0001,
        World = 0b0000_0010,
        Player = 0b0000_0100,
        BulletImpact = 0b0000_1000,

        /// <summary>Part of the speaker half, which is not carried over. Named so the number is not a mystery.</summary>
        SwallowRouted = 0b0001_0000,

        Explosion = 0b0010_0000,

        /// <summary>Also the speaker half's. See the class comment.</summary>
        IgnoreNonAttenuated = 0b0100_0000,
    }

    /// <summary>
    /// How often the meter reads, and it is stock's rate. Not a free choice: the
    /// smoothing below is a fraction applied per sample, so changing the rate
    /// changes how fast every existing map's microphone settles.
    /// </summary>
    private const float SampleInterval = 0.1f;

    /// <summary>
    /// Below this, the level is taken as arrived rather than smoothed toward.
    ///
    /// Stock's, and its comment says why it exists: without it the exponential
    /// smoothing never quite reaches zero, so the output goes on firing tiny
    /// changes long after the room has gone quiet.
    /// </summary>
    private const float SettleEpsilon = 0.005f;

    /// <summary>How many entries one query may bring back. The list is short and bounded.</summary>
    private const int MaxHeard = 64;

    /// <summary>What a map may set on this entity. See <see cref="EnvMicrophoneVars"/>.</summary>
    [ScriptVars] public EnvMicrophoneVars Vars = new();

    /// <summary>The last reading, 0..1. A map wires this to anything that takes a number.</summary>
    public readonly Output SoundLevel = new();

    /// <summary>Fired on any sample where something was heard at all.</summary>
    public readonly Output OnHeardSound = new();

    private float _level;

    /// <summary>
    /// Which CSoundEnt kinds this microphone keeps. <see cref="SoundType.None"/>
    /// hears nothing, which is stock.
    /// </summary>
    private SoundType _mask;

    public override void OnSpawn()
    {
        // The map may write engine bits into SoundMask directly, and stock ORs
        // the two sources rather than choosing between them. The cast is the one
        // place the raw keyvalue becomes a typed mask.
        _mask = (SoundType)Vars.SoundMask;

        if (Flags.HasFlag(SpawnFlag.Combat)) _mask |= SoundType.Combat;
        if (Flags.HasFlag(SpawnFlag.World)) _mask |= SoundType.World;
        if (Flags.HasFlag(SpawnFlag.Player)) _mask |= SoundType.Player;
        if (Flags.HasFlag(SpawnFlag.BulletImpact)) _mask |= SoundType.BulletImpact;
        if (Flags.HasFlag(SpawnFlag.Explosion)) _mask |= SoundType.ContextExplosion;

        // Zero is a divide by zero in Hears, and one is the identity — which is
        // what a map that left the key alone means.
        if (Vars.Sensitivity == 0f)
            Vars.Sensitivity = 1f;
        else if (Vars.Sensitivity > 10f)
            Vars.Sensitivity = 10f;

        Vars.SmoothFactor = Math.Clamp(Vars.SmoothFactor, 0f, 0.9f);

        if (Vars.SpeakerName.Length != 0)
        {
            Log.Warning(
                $"[SourceSharp] env_microphone '{Name}': SpeakerName '{Vars.SpeakerName}' is not carried over. " +
                 "This entity measures sound and fires its SoundLevel output; it does not re-play what it " +
                 "hears through another entity. See docs/divergences.md.\n");
        }

        if (!Vars.Disabled)
            SetNextThink(SampleInterval);
    }

    [ScriptedEvent("Enable")]
    private void OnEnable(InputData input)
    {
        if (!Vars.Disabled)
            return;

        Vars.Disabled = false;
        SetNextThink(SampleInterval);
    }

    [ScriptedEvent("Disable")]
    private void OnDisable(InputData input)
    {
        Vars.Disabled = true;
        StopThinking();
    }

    /// <summary>
    /// Declared and refused rather than left unhandled, because an input that
    /// falls through looks to a mapper exactly like one that worked.
    /// </summary>
    [ScriptedEvent("SetSpeakerName")]
    private void OnSetSpeakerName(InputData input)
    {
        Log.Warning($"[SourceSharp] env_microphone '{Name}': SetSpeakerName '{input.Value}' does nothing here. " +
                        "See docs/divergences.md.\n");
    }

    public override void OnThink()
    {
        Span<HeardSound> heard = stackalloc HeardSound[MaxHeard];

        int count = Sounds.Query(_mask, heard);

        // The loudest that reaches here is the reading; the rest are discarded,
        // which is why the query answers all at once rather than offering a
        // cursor.
        float loudest = 0f;
        bool anything = false;

        Vector at = MeasurePoint();

        for (int i = 0; i < count; i++)
        {
            if (!Hears(heard[i], at, out float volume) || volume <= loudest)
                continue;

            loudest = volume;
            anything = true;
        }

        if (anything)
            OnHeardSound.Fire();

        UpdateLevel(loudest);

        SetNextThink(SampleInterval);
    }

    /// <summary>
    /// Moves the reading toward what was just heard, and states it.
    ///
    /// Split out and pure so the settling behaviour can be driven in a test:
    /// the epsilon is what stops the output firing forever as the smoothed value
    /// creeps toward zero, and getting it wrong is invisible until a map wired
    /// to this fires a thousand times.
    /// </summary>
    internal static float Smooth(float current, float target, float factor)
    {
        if (target == current)
            return current;

        if (Math.Abs(target - current) < SettleEpsilon)
            return target;

        return (target * (1f - factor)) + (current * factor);
    }

    private void UpdateLevel(float target)
    {
        float next = Smooth(_level, target, Vars.SmoothFactor);

        if (next == _level)
            return;

        _level = next;
        SoundLevel.Fire(_level);
    }

    /// <summary>
    /// Where the reading is taken. The mapper's target if they named one that
    /// exists, otherwise here.
    ///
    /// Resolved per sample rather than once, because the point of naming another
    /// entity is usually that it MOVES — a microphone on a lift measures where
    /// the lift is. Stock resolves it in Activate and keeps the handle, which
    /// amounts to the same thing for a handle it re-reads the origin from.
    /// </summary>
    private Vector MeasurePoint()
    {
        if (Vars.MeasureTarget.Length == 0)
            return Origin;

        var target = Entities.FindByName(Vars.MeasureTarget);

        // Stock's own comment says a missing measure target is the 90% case and
        // stops warning about it. Measuring here is the same answer.
        return Entities.IsValid(target) ? Entities.GetOrigin(target) : Origin;
    }

    /// <summary>
    /// Whether one recorded noise reaches this microphone, and how loudly.
    ///
    /// The volume is not a gain: <c>CSoundEnt</c> stores roughly how far a noise
    /// carries, so the test is a distance against that reach scaled by the
    /// sensitivity, and what comes back is how much of the reach is left.
    /// </summary>
    private bool Hears(in HeardSound sound, Vector at, out float volume)
    {
        volume = 0f;

        if (Vars.Disabled)
            return false;

        // A filter names WHOSE noises count. A sound with no owner — a world
        // noise, a bullet striking a wall with nobody credited — cannot pass a
        // filter that asks about an entity, which is stock: it culls a sound
        // with no owner only when a filter is set.
        if (Vars.ListenFilter.Length != 0)
        {
            var filter = FindByName<FilterBase>(Vars.ListenFilter);

            if (filter is not null && (!Entities.IsValid(sound.Owner) || !filter.Passes(sound.Owner)))
                return false;
        }

        float distance = (sound.Origin - at).Length;

        // Right on top of it. Guarded before the division rather than after,
        // which is stock, and it is also the only way a zero-volume sound is
        // ever heard.
        if (distance == 0f)
        {
            volume = 1f;
            return true;
        }

        if (Vars.MaxRange != 0f && distance > Vars.MaxRange)
            return false;

        float reach = sound.Volume * Vars.Sensitivity;

        if (distance > reach)
            return false;

        volume = Math.Clamp(1f - (distance / reach), 0f, 1f);
        return true;
    }
}

/// <summary>
/// The map keys <see cref="EnvMicrophone"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvMicrophoneVars
{
    [VarName("StartDisabled")]
    public bool Disabled { get; set; }

    /// <summary>
    /// Extra sound kinds beyond the spawnflags. A map may write the bit set
    /// directly, and stock ORs the two rather than choosing.
    /// </summary>
    public int SoundMask { get; set; }

    /// <summary>0 deaf, 1 default, 10 the most. Divides a distance, so zero would divide by zero.</summary>
    public float Sensitivity { get; set; } = 1f;

    /// <summary>0 none, 0.9 the most. How much of the last reading is kept.</summary>
    public float SmoothFactor { get; set; }

    /// <summary>Beyond this, nothing is heard whatever its loudness. Zero means no limit.</summary>
    public float MaxRange { get; set; }

    /// <summary>A filter naming whose sounds count. See <see cref="Hears"/>.</summary>
    public string ListenFilter { get; set; } = string.Empty;

    /// <summary>Another entity to measure at, rather than here.</summary>
    [VarName("target")]
    public string MeasureTarget { get; set; } = string.Empty;

    /// <summary>See the class comment: this half is not carried over.</summary>
    public string SpeakerName { get; set; } = string.Empty;
}
