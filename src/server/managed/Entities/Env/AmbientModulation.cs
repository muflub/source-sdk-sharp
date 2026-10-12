//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;

namespace SourceSharp.Server;

/// <summary>
/// Which SND_* bits an ambient update carries. Mirrors soundflags.h, which the
/// engine reads on the other side of the ABI.
/// </summary>
[Flags]
public enum SoundUpdate
{
    None = 0,
    ChangeVolume = 1 << 0,
    ChangePitch = 1 << 1,
    Stop = 1 << 2,

    /// <summary>
    /// Written into the signon buffer instead of broadcast, so a client that
    /// joins later still hears a loop that started before it arrived. The engine
    /// honours it only while signon writes are still open — during level load —
    /// and degrades to an ordinary broadcast with a DevMsg after that.
    /// </summary>
    Spawning = 1 << 3,
}

/// <summary>What one 5Hz modulation step decided.</summary>
/// <param name="Changed">
/// Whether pitch or volume actually moved. Stock sends nothing when they did
/// not, and that is not an optimisation: an ambient update with the same numbers
/// still restarts a non-looping sound on the client.
/// </param>
/// <param name="Silence">
/// The envelope ran off its bottom end. The sound stops and the entity stops
/// thinking — this is how a spindown or a fadeout ends, rather than by a timer.
/// </param>
/// <param name="KeepThinking">
/// Whether there is anything left to ramp. False means every envelope has
/// settled and the LFO is off, so the next step would do nothing at all.
/// </param>
public readonly record struct AmbientStep(
    bool Changed, SoundUpdate Flags, int Pitch, int Volume, bool Silence, bool KeepThinking);

/// <summary>
/// The pitch and volume envelope behind ambient_generic: presets, spin up and
/// down, fade in and out, and a low-frequency oscillator riding on top of both.
///
/// <para>
/// This is the whole reason ambient_generic is a class rather than a call to
/// play a sound. What reaches the engine is an AMBIENT CHANNEL UPDATE — the
/// engine keys a playing ambient by (entity index, sound name), so sending the
/// same sound again with a new pitch retunes what is already running instead of
/// starting a second copy. Everything below exists to work out, five times a
/// second, what that new pitch and volume should be.
/// </para>
///
/// <para>
/// Kept apart from the entity, and pure, because it is arithmetic with no engine
/// in it: the fixed-point ramps, the preset table and the LFO are exactly the
/// kind of thing that is wrong by a factor of 64 in a way nobody notices in
/// game. Stock's own comment on the equivalent struct is that it is saved as a
/// raw memory block and must not be reordered, which is a fair measure of how
/// much this was meant to be looked at.
/// </para>
///
/// <para>
/// Every quantity here is stock's, in stock's units, including the odd ones. Vol
/// is 0..100 rather than 0..1. Pitch is 0..255 with 100 meaning "as recorded".
/// The ramps are 8.8 fixed point, so <see cref="VolumeFraction"/> is the volume
/// times 256 and the per-step increment is a number like (101 - seconds) * 64.
/// Rewriting any of that in friendlier units would change what a map's keys mean.
/// </para>
/// </summary>
public sealed class AmbientModulation
{
    /// <summary>Stock's update rate for the ramps. Every fixed-point increment is scaled by it.</summary>
    public const int UpdateRate = 5;

    /// <summary>Seconds between steps. The reciprocal of <see cref="UpdateRate"/>.</summary>
    public const float ThinkDelay = 1.0f / UpdateRate;

    /// <summary>The pitch that means "as recorded". Never sent as an update — see <see cref="Init"/>.</summary>
    public const int PitchNormal = 100;

    /// <summary>
    /// The shape of the low-frequency oscillator an ambient_generic modulates
    /// with. A closed set of four, not a bit field: a sound has one waveform.
    ///
    /// <para>
    /// Stock's numbering, kept, including its oddity. A map may write 4, which
    /// is not a waveform stock names; its keyvalue guard folds anything ABOVE 4
    /// to a triangle and lets 4 itself through, and the switch then treats it as
    /// a triangle by default anyway. Naming that value <see cref="Unnamed"/>
    /// rather than leaving it as a bare number is the point of the type here --
    /// the two paths that arrive at a triangle are different, and only one of
    /// them is deliberate.
    /// </para>
    /// </summary>
    public enum LfoWaveform
    {
        /// <summary>No modulation at all, and the value that turns the LFO off.</summary>
        None = 0,

        Square = 1,
        Triangle = 2,
        Random = 3,

        /// <summary>
        /// Stock names no waveform 4, but its guard admits one. Behaves as a
        /// triangle through the switch's default arm.
        /// </summary>
        Unnamed = 4,
    }

    // The map's keys, after the fixups KeyValue applies.
    public int Preset;
    public int PitchRun;
    public int PitchStart;
    public int SpinUp;
    public int SpinDown;
    public int VolumeRun;
    public int VolumeStart;
    public int FadeIn;
    public int FadeOut;
    public LfoWaveform LfoType;
    public int LfoRate;
    public int LfoModPitch;
    public int LfoModVolume;

    /// <summary>How many triggers it takes to reach full pitch. Zero for the ordinary on/off ambient.</summary>
    public int CountedSpinUp;

    // Running state. The "Sav" pair are the configured rates, kept because
    // SpinUp and FadeIn are consumed as the ramp completes and have to be
    // restored every time the sound is started again.
    public int SpinCount;
    public int Pitch;
    public int SpinUpSaved;
    public int SpinDownSaved;
    public int PitchFraction;
    public int Volume;
    public int FadeInSaved;
    public int FadeOutSaved;
    public int VolumeFraction;
    public int LfoFraction;
    public int LfoMultiplier;

    /// <summary>
    /// The 27 presets a mapper picks by number, in the FGD's order.
    ///
    /// Each row is the thirteen configured values plus the preset number itself,
    /// exactly as stock's rgdpvpreset table has them and in that order. They are
    /// stored RAW — before the (101 - x) * 64 fixups — because loading a preset
    /// applies the same fixups the keyvalues get, and doing it once here would
    /// mean the two paths could drift.
    /// </summary>
    private static readonly ImmutableArray<ImmutableArray<int>> Presets =
    [
        //     pitch pstart spinup spindn volrun volstart fadein fadeout lfotype lforate modptch modvol cspinup
        [1,    255,   75,    95,    95,    10,     1,      50,     95,     0,      0,      0,      0,      0],
        [2,    255,   85,    70,    88,    10,     1,      20,     88,     0,      0,      0,      0,      0],
        [3,    255,  100,    50,    75,    10,     1,      10,     75,     0,      0,      0,      0,      0],
        [4,    100,  100,     0,     0,    10,     1,      90,     90,     0,      0,      0,      0,      0],
        [5,    100,  100,     0,     0,    10,     1,      80,     80,     0,      0,      0,      0,      0],
        [6,    100,  100,     0,     0,    10,     1,      50,     70,     0,      0,      0,      0,      0],
        [7,    100,  100,     0,     0,     5,     1,      40,     50,     1,     50,      0,     10,      0],
        [8,    100,  100,     0,     0,     5,     1,      40,     50,     1,    150,      0,     10,      0],
        [9,    100,  100,     0,     0,     5,     1,      40,     50,     1,    750,      0,     10,      0],
        [10,   128,  100,    50,    75,    10,     1,      30,     40,     2,      8,     20,      0,      0],
        [11,   128,  100,    50,    75,    10,     1,      30,     40,     2,     25,     20,      0,      0],
        [12,   128,  100,    50,    75,    10,     1,      30,     40,     2,     70,     20,      0,      0],
        [13,    50,   50,     0,     0,    10,     1,      20,     50,     0,      0,      0,      0,      0],
        [14,    70,   70,     0,     0,    10,     1,      20,     50,     0,      0,      0,      0,      0],
        [15,    90,   90,     0,     0,    10,     1,      20,     50,     0,      0,      0,      0,      0],
        [16,   120,  120,     0,     0,    10,     1,      20,     50,     0,      0,      0,      0,      0],
        [17,   180,  180,     0,     0,    10,     1,      20,     50,     0,      0,      0,      0,      0],
        [18,   255,  255,     0,     0,    10,     1,      20,     50,     0,      0,      0,      0,      0],
        [19,   200,   75,    90,    90,    10,     1,      50,     90,     2,    100,     20,      0,      0],
        [20,   255,   75,    97,    90,    10,     1,      50,     90,     1,     40,     50,      0,      0],
        [21,   100,  100,     0,     0,    10,     1,      30,     50,     3,     15,     20,      0,      0],
        [22,   160,  160,     0,     0,    10,     1,      50,     50,     3,    500,     25,      0,      0],
        [23,   255,   75,    88,     0,    10,     1,      40,      0,     0,      0,      0,      0,      5],
        [24,   200,   20,    95,    70,    10,     1,      70,     70,     3,     20,     50,      0,      0],
        [25,   180,  100,    50,    60,    10,     1,      40,     60,     2,     90,    100,    100,      0],
        [26,    60,   60,     0,     0,    10,     1,      40,     70,     3,     80,     20,     50,      0],
        [27,   128,   90,    10,    10,    10,     1,      20,     40,     1,      5,     10,     20,      0],
    ];

    /// <summary>
    /// One of the entity's keys, with stock's fixups applied as it lands.
    ///
    /// The fixups are done HERE and not at spawn on purpose: that is where stock
    /// does them, and where a preset does the same arithmetic to its own row. A
    /// value like spinup arrives as 0..100 seconds-ish and is stored as the
    /// per-step increment (101 - x) * 64, so anything reading the field later
    /// sees the rate rather than the key.
    /// </summary>
    /// <returns>True when the key belonged to the modulation.</returns>
    public bool ApplyKey(string key, string value)
    {
        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer,
                          System.Globalization.CultureInfo.InvariantCulture, out int n))
        {
            // Not a number: only claim it if it is one of ours, so a malformed
            // key is dropped rather than falling through and being taken for
            // something else.
            return IsModulationKey(key);
        }

        switch (key.ToLowerInvariant())
        {
            case "preset":
                Preset = n;
                return true;

            case "pitch":
                PitchRun = Math.Clamp(n, 0, 255);
                return true;

            case "pitchstart":
                PitchStart = Math.Clamp(n, 0, 255);
                return true;

            case "spinup":
                SpinUp = SpinUpSaved = RampFromPercent(n);
                return true;

            case "spindown":
                SpinDown = SpinDownSaved = RampFromPercent(n);
                return true;

            case "volstart":
                // 0..10 in the map, 0..100 everywhere below.
                VolumeStart = Math.Clamp(n, 0, 10) * 10;
                return true;

            case "fadein":
                FadeIn = FadeInSaved = RampFromPercent(n);
                return true;

            case "fadeout":
                FadeOut = FadeOutSaved = RampFromPercent(n);
                return true;

            case "fadeinsecs":
                FadeIn = FadeInSaved = RampFromSeconds(n);
                return true;

            case "fadeoutsecs":
                FadeOut = FadeOutSaved = RampFromSeconds(n);
                return true;

            case "lfotype":
                // Stock's guard, and its oddity kept: anything over 4 becomes a
                // triangle, but 4 itself falls through as an unnamed type that
                // the switch below treats as a triangle anyway.
                LfoType = n > 4 ? LfoWaveform.Triangle : (LfoWaveform)n;
                return true;

            case "lforate":
                LfoRate = Math.Clamp(n, 0, 1000) * 256;
                return true;

            case "lfomodpitch":
                LfoModPitch = Math.Clamp(n, 0, 100);
                return true;

            case "lfomodvol":
                LfoModVolume = Math.Clamp(n, 0, 100);
                return true;

            case "cspinup":
                CountedSpinUp = Math.Clamp(n, 0, 100);
                return true;
        }

        return false;
    }

    private static bool IsModulationKey(string key) => key.ToLowerInvariant() is
        "preset" or "pitch" or "pitchstart" or "spinup" or "spindown" or "volstart"
        or "fadein" or "fadeout" or "fadeinsecs" or "fadeoutsecs" or "lfotype"
        or "lforate" or "lfomodpitch" or "lfomodvol" or "cspinup";

    /// <summary>
    /// The legacy spelling: 0..100 where BIGGER is faster, and zero is off.
    ///
    /// (101 - x) * 64 is not a time in any unit — it is the 8.8 fixed-point step
    /// taken five times a second, so 100 ramps in about a fifth of a second and
    /// 1 takes roughly twenty. Kept exactly because every stock map is tuned
    /// against it.
    /// </summary>
    private static int RampFromPercent(int n)
    {
        n = Math.Clamp(n, 0, 100);
        return n > 0 ? (101 - n) * 64 : 0;
    }

    /// <summary>The later spelling: an actual number of seconds.</summary>
    private static int RampFromSeconds(int n)
    {
        n = Math.Clamp(n, 0, 100);
        return n > 0 ? (100 << 8) / (n * UpdateRate) : 0;
    }

    /// <summary>
    /// Prepares every ramp to play the sound from the start. Runs at spawn and
    /// again every time the sound is turned on.
    /// </summary>
    /// <param name="health">
    /// The map's "health" key, which is where a mapper types the volume. Not a
    /// typo on anyone's part: in 1998 an entity had a handful of general fields
    /// and a map reused whichever was free, and Hammer still labels this one
    /// Health. A PRESET overrides it, because loading a preset replaces the
    /// whole set including the volume.
    /// </param>
    public void Init(int health)
    {
        VolumeRun = Math.Clamp(health * 10, 0, 100);

        if (Preset != 0 && Preset <= Presets.Length)
        {
            ImmutableArray<int> row = Presets[Preset - 1];

            // A preset replaces EVERYTHING, running state included — which is
            // why the saved rates are rebuilt below rather than kept.
            Preset = row[0];
            PitchRun = row[1];
            PitchStart = row[2];
            SpinUp = row[3];
            SpinDown = row[4];
            VolumeRun = row[5];
            VolumeStart = row[6];
            FadeIn = row[7];
            FadeOut = row[8];
            LfoType = (LfoWaveform)row[9];
            LfoRate = row[10];
            LfoModPitch = row[11];
            LfoModVolume = row[12];
            CountedSpinUp = row[13];

            SpinCount = 0;
            Pitch = 0;
            PitchFraction = 0;
            Volume = 0;
            VolumeFraction = 0;
            LfoFraction = 0;
            LfoMultiplier = 0;

            // The same fixups KeyValue applies, because the table is raw.
            if (SpinDown > 0) SpinDown = (101 - SpinDown) * 64;
            if (SpinUp > 0) SpinUp = (101 - SpinUp) * 64;

            VolumeStart *= 10;
            VolumeRun *= 10;

            if (FadeIn > 0) FadeIn = (101 - FadeIn) * 64;
            if (FadeOut > 0) FadeOut = (101 - FadeOut) * 64;

            LfoRate *= 256;

            FadeInSaved = FadeIn;
            FadeOutSaved = FadeOut;
            SpinUpSaved = SpinUp;
            SpinDownSaved = SpinDown;
        }

        FadeIn = FadeInSaved;
        FadeOut = 0;

        Volume = FadeIn != 0 ? VolumeStart : VolumeRun;

        SpinUp = SpinUpSaved;
        SpinDown = 0;

        Pitch = SpinUp != 0 ? PitchStart : PitchRun;

        if (Pitch == 0)
            Pitch = PitchNormal;

        PitchFraction = Pitch << 8;
        VolumeFraction = Volume << 8;

        LfoFraction = 0;
        LfoRate = Math.Abs(LfoRate);

        SpinCount = 1;

        if (CountedSpinUp != 0)
        {
            int increment = (255 - PitchStart) / CountedSpinUp;
            PitchRun = Math.Min(PitchStart + increment, 255);
        }

        // Never open on the pitch that means "no pitch". The engine reads
        // PITCH_NORM as "leave it alone", so a sound started at 100 cannot be
        // retuned later — the first update would be the one that established
        // there was a pitch at all, and it never arrives.
        if ((SpinUpSaved != 0 || SpinDownSaved != 0 || (LfoType != LfoWaveform.None && LfoModPitch != 0))
            && Pitch == PitchNormal)
        {
            Pitch = PitchNormal + 1;
        }
    }

    /// <summary>
    /// Whether anything here would move if stepped. False for the plain ambient
    /// that plays one sound at one volume, which is most of them.
    /// </summary>
    public bool HasRamps
        => SpinUp != 0 || SpinDown != 0 || FadeIn != 0 || FadeOut != 0 || LfoType != LfoWaveform.None;

    /// <summary>
    /// One 5Hz step of every envelope, in stock's order: pitch, then amplitude,
    /// then the LFO riding on both.
    /// </summary>
    /// <param name="randomByte">
    /// Where a random LFO gets its next value. Injected so the step can be
    /// tested without a game and without the engine's generator.
    /// </param>
    public AmbientStep Step(Func<int> randomByte)
    {
        int pitch = Pitch;
        int volume = Volume;
        var flags = SoundUpdate.None;
        bool changed = false;

        // Nothing to ramp and no oscillator: stock returns without rescheduling,
        // so the entity simply stops thinking until something starts it again.
        if (!HasRamps)
            return new AmbientStep(false, SoundUpdate.None, pitch, volume, false, false);

        if (SpinUp != 0 || SpinDown != 0)
        {
            int previous = PitchFraction >> 8;

            if (SpinUp > 0)
                PitchFraction += SpinUp;
            else if (SpinDown > 0)
                PitchFraction -= SpinDown;

            pitch = PitchFraction >> 8;

            if (pitch > PitchRun)
            {
                pitch = PitchRun;
                SpinUp = 0;
            }

            if (pitch < PitchStart)
            {
                // Spun all the way down: the sound ends here rather than
                // continuing at the floor. Pitch is deliberately NOT written
                // back — stock returns before the assignment, so the entity is
                // left holding the pitch it last SENT, which is what a restart
                // then ramps away from.
                SpinDown = 0;
                return new AmbientStep(true, SoundUpdate.Stop, PitchStart, volume, true, false);
            }

            pitch = Math.Clamp(pitch, 1, 255);

            Pitch = pitch;

            changed |= previous != pitch;
            flags |= SoundUpdate.ChangePitch;
        }

        if (FadeIn != 0 || FadeOut != 0)
        {
            int previous = VolumeFraction >> 8;

            if (FadeIn > 0)
                VolumeFraction += FadeIn;
            else if (FadeOut > 0)
                VolumeFraction -= FadeOut;

            volume = VolumeFraction >> 8;

            if (volume > VolumeRun)
            {
                volume = VolumeRun;
                VolumeFraction = volume << 8;
                FadeIn = 0;
            }

            if (volume < VolumeStart)
            {
                Volume = VolumeStart;
                VolumeFraction = VolumeStart << 8;
                FadeOut = 0;

                return new AmbientStep(true, SoundUpdate.Stop, pitch, VolumeStart, true, false);
            }

            if (volume > 100)
            {
                volume = 100;
                VolumeFraction = volume << 8;
            }

            if (volume < 1)
            {
                volume = 1;
                VolumeFraction = volume << 8;
            }

            Volume = volume;

            changed |= previous != volume;
            flags |= SoundUpdate.ChangeVolume;
        }

        if (LfoType != LfoWaveform.None)
        {
            // The counter is a triangle in its own right: it runs up to 255,
            // reverses, and runs back down, and every waveform below is read off
            // its position rather than kept separately.
            if (LfoFraction > 0x6fffffff)
                LfoFraction = 0;

            LfoFraction += LfoRate;
            int position = LfoFraction >> 8;

            if (LfoFraction < 0)
            {
                LfoFraction = 0;
                LfoRate = Math.Abs(LfoRate);
                position = 0;
            }
            else if (position > 255)
            {
                position = 255;
                LfoFraction = 255 << 8;
                LfoRate = -Math.Abs(LfoRate);
            }

            switch (LfoType)
            {
                case LfoWaveform.Square:
                    LfoMultiplier = position < 128 ? 255 : 0;
                    break;

                case LfoWaveform.Random:
                    // Only at the turn, so the value holds for a whole sweep
                    // rather than jittering every step.
                    if (position == 255)
                        LfoMultiplier = randomByte();
                    break;

                case LfoWaveform.Triangle:
                default:
                    LfoMultiplier = position;
                    break;
            }

            if (LfoModPitch != 0)
            {
                int previous = pitch;

                pitch += ((LfoMultiplier - 128) * LfoModPitch) / 100;
                pitch = Math.Clamp(pitch, 1, 255);

                changed |= previous != pitch;
                flags |= SoundUpdate.ChangePitch;
            }

            if (LfoModVolume != 0)
            {
                int previous = volume;

                volume += ((LfoMultiplier - 128) * LfoModVolume) / 100;
                volume = Math.Clamp(volume, 0, 100);

                changed |= previous != volume;
                flags |= SoundUpdate.ChangeVolume;
            }
        }

        // Sent only when something actually moved. Not an optimisation: an
        // ambient update carrying the same numbers still restarts the sound on
        // the client, so a pointless update is audible.
        return new AmbientStep(changed && flags != SoundUpdate.None, flags,
                               pitch, volume, false, true);
    }

    /// <summary>
    /// One press of a counted spin-up: each trigger takes the pitch a step
    /// closer to the top instead of switching the sound off.
    /// </summary>
    /// <returns>False once every step has been used, and the sound may stop.</returns>
    public bool AdvanceCountedSpinUp()
    {
        if (CountedSpinUp == 0 || SpinCount > CountedSpinUp)
            return false;

        SpinCount++;

        SpinUp = SpinUpSaved;
        SpinDown = 0;

        int increment = (255 - PitchStart) / CountedSpinUp;
        PitchRun = Math.Min(PitchStart + (increment * SpinCount), 255);

        return true;
    }

    /// <summary>
    /// Starts the ramps that take the sound down, if it has any. False means
    /// there is nothing to ramp and the sound should just stop.
    /// </summary>
    public bool BeginRampDown()
    {
        if (SpinDownSaved == 0 && FadeOutSaved == 0)
            return false;

        SpinDown = SpinDownSaved;
        SpinUp = 0;

        FadeOut = FadeOutSaved;
        FadeIn = 0;

        return true;
    }

    /// <summary>
    /// A pitch that can be sent as an update. See <see cref="Init"/> for why 100
    /// can never go on the wire.
    /// </summary>
    public static int Sendable(int pitch) => pitch == PitchNormal ? PitchNormal + 1 : pitch;
}
