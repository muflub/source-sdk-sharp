using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ambient_generic's envelope, which is the whole of what the class does that a
/// call to play a sound could not.
///
/// <para>
/// Everything here is arithmetic against fixed-point ramps in units nobody would
/// choose today: volume 0..100, pitch 0..255, and per-step increments of
/// (101 - key) * 64. It is exactly the kind of code that can be wrong by a
/// factor of 64 and sound merely "a bit off" in game — which is why the
/// modulation was split out of the entity and is checked here rather than by
/// listening to it.
/// </para>
///
/// <para>
/// The numbers below are stock's, taken from sound.cpp rather than derived, so a
/// change to any of them is a change to what a map's keys mean.
/// </para>
/// </summary>
public class AmbientTests
{

    /// <summary>Deterministic stand-in for the random LFO, so a sweep is reproducible.</summary>
    private static int Fixed255() => 255;

    [Fact]
    public void RadiusBecomesDecibels()
    {
        // 40dB at the 36-unit reference distance, falling 20dB per decade.
        Assert.True(AmbientGeneric.ComputeSoundLevel(36f, false) == 40, "radius 36 is 40dB");
        Assert.True(AmbientGeneric.ComputeSoundLevel(360f, false) == 60, "radius 360 is 60dB");

        // dm_lockdown's own guard_crash_sound entities.
        Assert.True(AmbientGeneric.ComputeSoundLevel(3000f, false) == 78, "radius 3000 is 78dB");

        // SNDLVL_NONE, which the engine reads as no falloff at all — the same
        // answer the Everywhere flag asks for.
        Assert.True(AmbientGeneric.ComputeSoundLevel(0f, false) == 0, "radius 0 has no level");
        Assert.True(AmbientGeneric.ComputeSoundLevel(3000f, true) == 0, "playing everywhere has no level");
    }

    [Fact]
    public void KeysGetStockFixups()
    {
        var m = new AmbientModulation();

        // The legacy spelling is not a time: it becomes an 8.8 fixed-point step
        // taken five times a second, and BIGGER means faster.
        m.ApplyKey("spinup", "95");
        Assert.True(m.SpinUp == 6 * 64 && m.SpinUpSaved == 6 * 64, "spinup 95 becomes (101-95)*64");

        m.ApplyKey("spindown", "1");
        Assert.True(m.SpinDown == 100 * 64, "spindown 1 is the slowest ramp");

        m.ApplyKey("fadein", "0");
        Assert.True(m.FadeIn == 0, "a zero fade is off, not instant");

        // The later spelling IS a time.
        m.ApplyKey("fadeinsecs", "4");
        Assert.True(m.FadeIn == (100 << 8) / (4 * 5), "fadeinsecs 4 is (100<<8)/(4*5)");

        m.ApplyKey("volstart", "3");
        Assert.True(m.VolumeStart == 30, "volstart 3 becomes 30");

        m.ApplyKey("lforate", "40");
        Assert.True(m.LfoRate == 40 * 256, "lforate is scaled by 256");

        m.ApplyKey("pitch", "900");
        Assert.True(m.PitchRun == 255, "pitch clamps to 255");

        m.ApplyKey("lfotype", "9");
        Assert.True(m.LfoType == AmbientModulation.LfoWaveform.Triangle, "an unknown lfotype falls back to triangle");

        Assert.True(!m.ApplyKey("message", "foo.wav"), "a key that is not ours is not claimed");
    }

    [Fact]
    public void HealthIsTheVolume()
    {
        var m = new AmbientModulation();
        m.ApplyKey("pitch", "100");
        m.Init(7);

        // dm_lockdown's torture_beam_sounds writes health 7 and means 70%.
        Assert.True(m.VolumeRun == 70 && m.Volume == 70, "health 7 is volume 70");
        Assert.True(m.VolumeFraction == 70 << 8, "volume fraction is the volume shifted up 8");

        var loud = new AmbientModulation();
        loud.Init(50);
        Assert.True(loud.VolumeRun == 100, "health above 10 clamps to full");
    }

    [Fact]
    public void PresetsOverrideKeys()
    {
        var m = new AmbientModulation();

        // k_lab.mini_teleport_warmup in dm_lockdown: preset 1, health 5, pitch 90.
        m.ApplyKey("preset", "1");
        m.ApplyKey("pitch", "90");
        m.Init(5);

        Assert.True(m.PitchRun == 255, "a preset replaces the pitch the map wrote");
        Assert.True(m.VolumeRun == 100, "a preset replaces the volume health set");
        Assert.True(m.SpinUpSaved == (101 - 95) * 64, "a preset's spinup gets the same fixup a key would");
        Assert.True(m.SpinDownSaved == (101 - 95) * 64, "a preset's spindown gets the same fixup");

        // Preset 1 has fadein 50 and volstart 1, so it opens at the START volume
        // and ramps up rather than beginning at full.
        Assert.True(m.Volume == 10, "a preset with a fade in opens at its start volume");

        // Preset 1 has spinup, so it opens at pitchstart rather than pitchrun.
        Assert.True(m.Pitch == 75, "a preset with a spin up opens at its start pitch");
    }

    [Fact]
    public void NeverOpensOnNoPitch()
    {
        // 100 means "as recorded" on the wire, which the engine reads as "leave
        // the pitch alone" — so a sound that starts on it can never be retuned
        // afterwards, because the update that would establish a pitch never
        // arrives. Anything that intends to modulate must open one step off.
        var m = new AmbientModulation();
        m.ApplyKey("pitch", "100");
        m.ApplyKey("lfotype", "2");
        m.ApplyKey("lfomodpitch", "20");
        m.Init(10);

        Assert.True(m.Pitch == 101, "an LFO on pitch never opens on 100");

        var plain = new AmbientModulation();
        plain.ApplyKey("pitch", "100");
        plain.Init(10);

        Assert.True(plain.Pitch == 100, "a plain ambient does open on 100");
        Assert.True(AmbientModulation.Sendable(100) == 101, "but nothing is ever SENT as 100");
        Assert.True(AmbientModulation.Sendable(90) == 90, "and any other pitch goes as it is");
    }

    [Fact]
    public void FadeInRunsUpAndSettles()
    {
        var m = new AmbientModulation();
        m.ApplyKey("fadeinsecs", "2");
        m.ApplyKey("volstart", "1");
        m.Init(10);

        Assert.True(m.Volume == 10, "opens at the start volume");

        int steps = 0;
        AmbientStep step = default;

        while (steps < 200)
        {
            step = m.Step(Fixed255);
            steps++;

            if (m.FadeIn == 0)
                break;
        }

        // Two seconds at 5Hz is ten steps, and the ramp is sized to reach 100
        // from zero in that time — so from 10 it arrives a little early.
        Assert.True(steps is >= 8 and <= 11, $"a two second fade in settles in about ten steps (took {steps})");
        Assert.True(m.Volume == 100, "and settles at the run volume");
        Assert.True(!step.Silence, "with no silence on the way");
    }

    [Fact]
    public void FadeOutEndsInSilence()
    {
        var m = new AmbientModulation();
        m.ApplyKey("volstart", "1");
        m.Init(10);

        Assert.True(!m.HasRamps, "no fade configured, so nothing to ramp");

        // What the FadeOut input does.
        m.FadeIn = 0;
        m.FadeOut = (100 << 8) / (2 * AmbientModulation.UpdateRate);

        AmbientStep step = default;
        int steps = 0;

        while (steps < 200)
        {
            step = m.Step(Fixed255);
            steps++;

            if (step.Silence)
                break;
        }

        Assert.True(step.Silence, $"a two second fade out reaches silence (took {steps})");
        Assert.True(!step.KeepThinking, "and asks to stop thinking");
        Assert.True(step.Flags == SoundUpdate.Stop, "and carries a stop");
    }

    [Fact]
    public void SpinUpReachesRunPitch()
    {
        var m = new AmbientModulation();
        m.ApplyKey("pitch", "200");
        m.ApplyKey("pitchstart", "80");
        m.ApplyKey("spinup", "90");
        m.Init(10);

        Assert.True(m.Pitch == 80, "opens at the start pitch");

        AmbientStep step = default;
        int steps = 0;

        while (steps < 500)
        {
            step = m.Step(Fixed255);
            steps++;

            if (m.SpinUp == 0)
                break;
        }

        Assert.True(m.Pitch == 200, $"the spin up reaches the run pitch (took {steps} steps)");
        Assert.True(!step.Silence, "without ever falling silent");
        Assert.True(step.Flags.HasFlag(SoundUpdate.ChangePitch), "and each step asked for a pitch change");
    }

    [Fact]
    public void SpinDownEndsInSilence()
    {
        var m = new AmbientModulation();
        m.ApplyKey("pitch", "200");
        m.ApplyKey("pitchstart", "80");
        m.ApplyKey("spinup", "90");
        m.ApplyKey("spindown", "90");
        m.Init(10);

        // Where the sound has already reached full pitch.
        m.SpinUp = 0;
        m.Pitch = 200;
        m.PitchFraction = 200 << 8;

        Assert.True(m.BeginRampDown(), "winding down is available");
        Assert.True(m.FadeIn == 0, "and cancels any fade in");

        AmbientStep step = default;
        int steps = 0;

        while (steps < 500)
        {
            step = m.Step(Fixed255);
            steps++;

            if (step.Silence)
                break;
        }

        Assert.True(step.Silence, $"the spin down ends in silence (took {steps} steps)");
        Assert.True(!step.KeepThinking, "and stops thinking");

        // Stock returns before writing the pitch back, so the entity is left
        // holding what it last SENT rather than the floor it stopped at.
        Assert.True(m.Pitch > m.PitchStart, "and leaves the pitch it last sent alone");
    }

    [Fact]
    public void PlainAmbientStopsThinking()
    {
        // The common case: dm_lockdown's guard_crash_sound has no envelope at
        // all, so its first think must decide there is nothing to do and stop —
        // otherwise twenty-one ambients tick five times a second for nothing.
        var m = new AmbientModulation();
        m.ApplyKey("pitch", "100");
        m.ApplyKey("pitchstart", "100");
        m.Init(10);

        Assert.True(!m.HasRamps, "a plain ambient has no ramps");

        var step = m.Step(Fixed255);

        Assert.True(!step.Changed, "so its step changes nothing");
        Assert.True(!step.KeepThinking, "and it stops thinking");
        Assert.True(!step.Silence, "without stopping the sound");
    }

    [Fact]
    public void LfoModulatesWithoutRamping()
    {
        var m = new AmbientModulation();
        m.ApplyKey("pitch", "100");
        m.ApplyKey("lfotype", "2");     // triangle
        m.ApplyKey("lforate", "40");
        m.ApplyKey("lfomodpitch", "50");
        m.Init(10);

        Assert.True(m.HasRamps, "an LFO alone counts as a ramp");

        int low = int.MaxValue;
        int high = int.MinValue;

        for (int i = 0; i < 40; i++)
        {
            var step = m.Step(Fixed255);

            Assert.True(step.KeepThinking, "the LFO never asks to stop thinking early");

            low = Math.Min(low, step.Pitch);
            high = Math.Max(high, step.Pitch);
        }

        // The multiplier sweeps 0..255 and is centred on 128, so at 50% depth
        // the pitch swings roughly +/-64 around where it started.
        Assert.True(low < 101 && high > 101, $"the LFO swings the pitch either side of centre (low {low}, high {high})");

        // And it must not have touched the base pitch: the LFO is per-step
        // modulation on top, not a new resting value.
        Assert.True(m.Pitch == 101, "without moving the resting pitch");
    }

    [Fact]
    public void CountedSpinUpWindsUpThenIgnores()
    {
        var m = new AmbientModulation();
        m.ApplyKey("pitchstart", "75");
        m.ApplyKey("spinup", "88");
        m.ApplyKey("cspinup", "5");
        m.Init(10);

        // Init sets the first rung: one increment above the start pitch.
        int increment = (255 - 75) / 5;
        Assert.True(m.PitchRun == 75 + increment, "a counted spin up starts one rung up");

        for (int rung = 2; rung <= 5; rung++)
        {
            Assert.True(m.AdvanceCountedSpinUp(), $"rung {rung} is available");
            Assert.True(m.PitchRun == Math.Min(75 + (increment * rung), 255), $"rung {rung} raises the target pitch");
        }

        Assert.True(m.AdvanceCountedSpinUp(), "a sixth press is still accepted");
        Assert.True(!m.AdvanceCountedSpinUp(), "and a seventh is not");
    }
}
