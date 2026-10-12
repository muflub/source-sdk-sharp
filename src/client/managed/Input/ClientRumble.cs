//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Input;

/// <summary>
/// The rumble effects mixer, ported from stock's <c>CRumbleEffects</c>
/// (c_rumble.cpp): up to three effect channels, each playing a 30-sample
/// waveform at 10 Hz, mixed with the screen shake into the controller's two
/// motors once a frame.
///
/// <para>
/// THE WAVEFORM TABLE KEEPS STOCK'S OVERRUN. GenerateSquareWaveEffect writes
/// past its 30 samples when 30 is not a multiple of two steps: for
/// RUMBLE_PHYSCANNON_LOW (6 cycles, right channel) the two extra writes land in
/// that waveform's sample count (0.25f's bits, 1048576000) and in
/// RUMBLE_PHYSCANNON_MEDIUM's first left sample. rumble-stock.tsv records
/// both, and RumbleStockOracleTests holds the table to it.
/// </para>
/// </summary>
public sealed class ClientRumble
{
    public const int MaxChannels = 3;
    public const int WaveSamples = 30;

    // rumble_shared.h
    public const int FlagStop = 0x0001;
    public const int FlagLoop = 0x0002;
    public const int FlagRestart = 0x0004;
    public const int FlagUpdateScale = 0x0008;
    public const int FlagOnlyOne = 0x0010;
    public const int FlagRandomAmplitude = 0x0020;
    public const int FlagInitialScale = 0x0040;

    public const int StopAll = 0;
    public const int Pistol = 1, Rumble357 = 2, Smg1 = 3, Ar2 = 4, ShotgunSingle = 5, ShotgunDouble = 6, Ar2AltFire = 7;
    public const int RpgMissile = 8, CrowbarSwing = 9, AirboatGun = 10, JeepEngineLoop = 11;
    public const int FlatLeft = 12, FlatRight = 13, FlatBoth = 14, DmgLow = 15, DmgMed = 16, DmgHigh = 17;
    public const int FallLong = 18, FallShort = 19, PhyscannonOpen = 20, PhyscannonPunt = 21, PhyscannonLow = 22;
    public const int PhyscannonMedium = 23, PhyscannonHigh = 24, PortalgunLeft = 25, PortalgunRight = 26;
    public const int PortalPlacementFailure = 27;
    public const int EffectCount = 28;

    public sealed class Waveform
    {
        public readonly float[] Left = new float[WaveSamples];
        public readonly float[] Right = new float[WaveSamples];
        public int Samples = WaveSamples;
    }

    public struct Channel
    {
        public float StartTime;
        public int Waveform;
        public int Priority;
        public bool InUse;
        public int Flags;
        public float Scale;
    }

    private readonly IInputDevices _devices;
    private readonly InputCvars _cvars;
    private readonly IUniformRandom _random;
    private readonly Action<string> _debug;

    public Waveform[] Waveforms { get; } = new Waveform[EffectCount];
    public Channel[] Channels { get; } = new Channel[MaxChannels];
    public float ScreenShake
    {
        get => _screenShake;
        private set { _screenShake = value; PublishShake(); }
    }

    public bool OutputEnabled
    {
        get => _outputEnabled;
        private set { _outputEnabled = value; PublishShake(); }
    }

    private readonly IInputMailbox? _mailbox;
    private float _screenShake;
    private bool _outputEnabled;

    /// <summary>
    /// When UpdateScreenShakeRumble would change nothing, for native to skip
    /// it (the input mailbox): with the output off, a store writes zero, so
    /// it changes nothing while the shake is +0; with it on, a store writes
    /// the shake, so it changes nothing when the bits are the same. Either
    /// way a dead player's store changes nothing as well, so this answer
    /// holds whatever the local player's state.
    /// </summary>
    public (ShakeSkip Skip, float Value) ShakeNoop()
    {
        if (_outputEnabled)
            return (ShakeSkip.WhenEqual, _screenShake);
        return (BitConverter.SingleToUInt32Bits(_screenShake) == 0 ? ShakeSkip.Always : ShakeSkip.Never, _screenShake);
    }

    private void PublishShake()
    {
        if (_mailbox is null)
            return;
        (ShakeSkip skip, float value) = ShakeNoop();
        _mailbox.PublishShake(skip, value);
    }

    /// <summary>The motor values the last update sent.</summary>
    public (float Left, float Right) LastOutput { get; private set; }

    public ClientRumble(IInputDevices devices, InputCvars cvars, IUniformRandom random, Action<string>? debug = null,
                        IInputMailbox? mailbox = null)
    {
        _mailbox = mailbox;
        _devices = devices;
        _cvars = cvars;
        _random = random;
        _debug = debug ?? (_ => { });
        for (int i = 0; i < EffectCount; i++)
            Waveforms[i] = new Waveform();
        Init();
    }

    private readonly record struct WaveParams(float Cycles, float AmplitudeScale, bool LeftChannel, float MinAmplitude, float MaxAmplitude);

    private static void Terminate(Waveform w, int samples)
    {
        if (samples <= WaveSamples)
            w.Samples = samples;
    }

    private static void EaseIn(Waveform w, int samples, bool left)
    {
        float step = 1.0f / samples;
        float factor = 0.0f;
        for (int i = 0; i < samples; i++)
        {
            if (left)
                w.Left[i] *= factor;
            else
                w.Right[i] *= factor;
            factor += step;
        }
    }

    private static void EaseOut(Waveform w, int samples, bool left)
    {
        float step = 1.0f / samples;
        float factor = 0.0f;
        int i = WaveSamples - 1;
        for (int j = 0; j < samples; j++)
        {
            if (left)
                w.Left[i] *= factor;
            else
                w.Right[i] *= factor;
            factor += step;
            i--;
        }
    }

    private static void Sawtooth(Waveform w, WaveParams p)
    {
        float delta = p.MaxAmplitude - p.MinAmplitude;
        int waveLength = (int)(WaveSamples / p.Cycles);
        float vstep = delta / waveLength;
        float amplitude = p.MinAmplitude;
        for (int i = 0; i < WaveSamples; i++)
        {
            if (p.LeftChannel)
                w.Left[i] = amplitude;
            else
                w.Right[i] = amplitude;
            amplitude += vstep;
            if (amplitude > p.MaxAmplitude)
                amplitude = p.MinAmplitude;
        }
    }

    /// <summary>
    /// GenerateSquareWaveEffect, overrun and all: stock's struct is
    /// {left[30], right[30], numSamples} and the waveforms sit back to back, so
    /// left[30..31] are right[0..1] and right[30] is numSamples, right[31] the
    /// next waveform's left[0].
    /// </summary>
    private void SquareWave(int index, WaveParams p)
    {
        int i = 0;
        int steps = (int)(WaveSamples / (p.Cycles * 2.0f));

        while (i < WaveSamples)
        {
            for (int j = 0; j < steps; j++)
                Store(index, p.LeftChannel, i++, p.MinAmplitude);
            for (int j = 0; j < steps; j++)
                Store(index, p.LeftChannel, i++, p.MaxAmplitude);
        }
    }

    private void Store(int index, bool left, int sample, float value)
    {
        Waveform w = Waveforms[index];
        if (sample < WaveSamples)
        {
            (left ? w.Left : w.Right)[sample] = value;
            return;
        }

        int over = sample - WaveSamples;
        if (left)
        {
            w.Right[over] = value;
        }
        else if (over == 0)
        {
            w.Samples = BitConverter.SingleToInt32Bits(value);
        }
        else if (index + 1 < EffectCount)
        {
            Waveforms[index + 1].Left[over - 1] = value;
        }
    }

    private static void Flat(Waveform w, WaveParams p)
    {
        for (int i = 0; i < WaveSamples; i++)
        {
            if (p.LeftChannel)
                w.Left[i] = p.MaxAmplitude;
            else
                w.Right[i] = p.MaxAmplitude;
        }
    }

    /// <summary>CRumbleEffects::Init: the channels free, the waveform table built.</summary>
    public void Init()
    {
        SetOutputEnabled(true);

        for (int i = 0; i < MaxChannels; i++)
        {
            Channels[i].InUse = false;
            Channels[i].Priority = 0;
        }

        for (int i = 0; i < EffectCount; i++)
            Waveforms[i].Samples = WaveSamples;

        Waveform[] w = Waveforms;

        Flat(w[JeepEngineLoop], new(1, 1.0f, false, 0.0f, 0.15f));

        Flat(w[Pistol], new(1, 1.0f, false, 0.0f, 0.5f));
        Terminate(w[Pistol], 3);

        Flat(w[Smg1], new(1, 1.0f, true, 0.0f, 0.2f));
        Flat(w[Smg1], new(1, 1.0f, false, 0.0f, 0.4f));
        Terminate(w[Smg1], 3);

        Flat(w[Ar2], new(1, 1.0f, true, 0.0f, 0.5f));
        Flat(w[Ar2], new(1, 1.0f, false, 0.0f, 0.3f));
        Terminate(w[Ar2], 3);

        Flat(w[Ar2AltFire], new(1, 1.0f, true, 0.0f, 0.5f));
        EaseIn(w[Ar2AltFire], 5, true);
        Flat(w[Ar2AltFire], new(1, 1.0f, false, 0.0f, 0.7f));
        EaseIn(w[Ar2AltFire], 5, false);
        Terminate(w[Ar2AltFire], 7);

        Flat(w[Rumble357], new(1, 1.0f, true, 0.0f, 0.75f));
        Flat(w[Rumble357], new(1, 1.0f, false, 0.0f, 0.75f));
        Terminate(w[Rumble357], 2);

        Flat(w[ShotgunSingle], new(1, 1.0f, true, 0.0f, 0.8f));
        Flat(w[ShotgunSingle], new(1, 1.0f, false, 0.0f, 0.8f));
        Terminate(w[ShotgunSingle], 3);

        Flat(w[ShotgunDouble], new(1, 1.0f, true, 0.0f, 1.0f));
        Flat(w[ShotgunDouble], new(1, 1.0f, false, 0.0f, 1.0f));
        Terminate(w[ShotgunDouble], 3);

        Flat(w[RpgMissile], new(1, 1.0f, false, 0.0f, 0.3f));
        EaseOut(w[RpgMissile], 30, false);
        Terminate(w[RpgMissile], 6);

        Flat(w[PhyscannonOpen], new(1, 1.0f, false, 0.0f, 0.25f));
        Terminate(w[PhyscannonOpen], 4);

        Flat(w[PhyscannonLow], new(1, 1.0f, true, 0.0f, 0.2f));
        SquareWave(PhyscannonLow, new(6, 1.0f, false, 0.0f, 0.25f));

        Flat(w[CrowbarSwing], new(1, 1.0f, false, 0.0f, 0.35f));
        EaseOut(w[CrowbarSwing], 30, false);
        Terminate(w[CrowbarSwing], 4);

        Flat(w[AirboatGun], new(1, 1.0f, false, 0.0f, 0.4f));
        Sawtooth(w[AirboatGun], new(12, 1.0f, true, 0.0f, 0.5f));

        Flat(w[FlatLeft], new(1, 1.0f, true, 0.0f, 1.0f));
        Flat(w[FlatRight], new(1, 1.0f, false, 0.0f, 1.0f));
        Flat(w[FlatBoth], new(1, 1.0f, true, 0.0f, 1.0f));
        Flat(w[FlatBoth], new(1, 1.0f, false, 0.0f, 1.0f));

        Flat(w[FallLong], new(1, 1.0f, false, 0.0f, 0.5f));
        Flat(w[FallLong], new(1, 1.0f, true, 0.0f, 0.5f));
        Terminate(w[FallLong], 3);

        Flat(w[FallShort], new(1, 1.0f, false, 0.0f, 0.3f));
        Flat(w[FallShort], new(1, 1.0f, true, 0.0f, 0.3f));
        Terminate(w[FallShort], 2);

        Flat(w[PortalgunLeft], new(1, 1.0f, true, 0.0f, 0.3f));
        Terminate(w[PortalgunLeft], 2);

        Flat(w[PortalgunRight], new(1, 1.0f, false, 0.0f, 0.3f));
        Terminate(w[PortalgunRight], 2);

        SquareWave(PortalPlacementFailure, new(12, 1.0f, true, 0.0f, 1.0f));
        SquareWave(PortalPlacementFailure, new(12, 1.0f, false, 0.0f, 1.0f));
        Terminate(w[PortalPlacementFailure], 6);
    }

    private int FindExistingChannel(int index)
    {
        for (int i = 0; i < MaxChannels; i++)
        {
            if (Channels[i].InUse && Channels[i].Waveform == index)
                return i;
        }
        return -1;
    }

    private int FindAvailableChannel(int priority, float curTime)
    {
        for (int i = 0; i < MaxChannels; i++)
        {
            if (!Channels[i].InUse)
                return i;
        }

        int lowestPriority = priority;
        int best = -1;
        float oldest = float.MaxValue;

        // Every channel in use: find one to slam.
        for (int i = 0; i < MaxChannels; i++)
        {
            ref Channel c = ref Channels[i];
            if ((c.Flags & FlagLoop) != 0)
                continue;

            if (c.Priority < lowestPriority)
            {
                // Always slam a lower priority.
                best = i;
                lowestPriority = c.Priority;
            }
            else if (c.Priority == lowestPriority)
            {
                // The same priority: replace the oldest.
                if (best >= 0)
                {
                    float age = curTime - c.StartTime;
                    if (age > oldest)
                    {
                        best = i;
                        oldest = age;
                    }
                }
                else
                {
                    best = i;
                    oldest = curTime - c.StartTime;
                }
            }
        }

        return best;
    }

    /// <summary>CRumbleEffects::SetOutputEnabled: off shuts the motors at once.</summary>
    public void SetOutputEnabled(bool enable)
    {
        OutputEnabled = enable;
        if (!enable)
        {
            ScreenShake = 0.0f;
            _devices.StopRumble();
        }
    }

    /// <summary>CRumbleEffects::StartEffect: the Rumble user message's effect, data and flags.</summary>
    public void StartEffect(int effectIndex, int rumbleData, int rumbleFlags, float curTime)
    {
        if (effectIndex == StopAll)
        {
            StopAllEffects();
            return;
        }

        if ((rumbleFlags & FlagStop) != 0)
        {
            StopEffect(effectIndex);
            return;
        }

        int priority = 1;
        int channel = -1;

        if ((rumbleFlags & FlagRestart) != 0)
            channel = FindExistingChannel(effectIndex);

        if ((rumbleFlags & FlagOnlyOne) != 0)
        {
            channel = FindExistingChannel(effectIndex);
            if (channel >= 0)
                return;     // already playing
        }

        if ((rumbleFlags & FlagUpdateScale) != 0)
        {
            channel = FindExistingChannel(effectIndex);
            if (channel >= 0)
                Channels[channel].Scale = rumbleData / 100.0f;

            // An update of an effect never started does nothing.
            return;
        }

        if (channel < 0)
            channel = FindAvailableChannel(priority, curTime);

        if (channel >= 0)
        {
            ref Channel c = ref Channels[channel];
            c.Waveform = effectIndex;
            c.Priority = 1;
            c.StartTime = curTime;
            c.InUse = true;
            c.Flags = rumbleFlags;
            c.Scale = (rumbleFlags & FlagInitialScale) != 0 ? rumbleData / 100.0f : 1.0f;
        }

        // Stock wrote through the channel pointer here even when none was
        // found; a NULL write it never survived. With no channel there is
        // nothing to scale.
        if ((rumbleFlags & FlagRandomAmplitude) != 0 && channel >= 0)
            Channels[channel].Scale = _random.RandomFloat(0.1f, 1.0f);
    }

    /// <summary>Every playing instance of the effect stops.</summary>
    public void StopEffect(int effectIndex)
    {
        for (int i = 0; i < MaxChannels; i++)
        {
            if (Channels[i].InUse && Channels[i].Waveform == effectIndex)
                Channels[i].InUse = false;
        }
    }

    public void StopAllEffects()
    {
        for (int i = 0; i < MaxChannels; i++)
            Channels[i].InUse = false;
        ScreenShake = 0.0f;
    }

    private void ComputeAmplitudes(ref Channel channel, float curTime, out float leftOut, out float rightOut)
    {
        float elapsed = curTime - channel.StartTime;

        if (elapsed >= (WaveSamples / 10))
        {
            if ((channel.Flags & FlagLoop) != 0)
            {
                channel.StartTime = curTime;
                elapsed = curTime - channel.StartTime;
            }
            else
            {
                leftOut = 0;
                rightOut = 0;
                channel.InUse = false;
                return;
            }
        }

        int seconds = (int)elapsed;
        int sample = (int)(elapsed * 10.0f);
        float fraction = elapsed - seconds;

        float left, right;
        Waveform w = Waveforms[channel.Waveform];

        if (sample == w.Samples)
        {
            // Done: zeroes for this frame and the channel off -- unless it loops.
            if ((channel.Flags & FlagLoop) != 0)
            {
                channel.StartTime = curTime;
                left = w.Left[0];
                right = w.Right[0];
            }
            else
            {
                left = 0.0f;
                right = 0.0f;
                channel.InUse = false;
            }
        }
        else
        {
            left = w.Left[sample];
            right = w.Right[sample];
        }

        left *= channel.Scale;
        right *= channel.Scale;

        if (_cvars.DebugRumble.Bool)
            _debug($"Seconds:{seconds} Fraction:{fraction:F6} Sample:{sample}  L:{left:F6} R:{right:F6}\n");

        if (!OutputEnabled)
        {
            left = 0;
            right = 0;
        }

        leftOut = left;
        rightOut = right;
    }

    /// <summary>CRumbleEffects::UpdateScreenShakeRumble.</summary>
    public void UpdateScreenShakeRumble(float shake)
        => ScreenShake = OutputEnabled ? shake : 0.0f;

    /// <summary>CRumbleEffects::UpdateEffects: the channels and the shake into the two motors.</summary>
    public void UpdateEffects(float curTime, bool paused)
    {
        float leftMotor = 0.0f;
        float rightMotor = 0.0f;

        for (int i = 0; i < MaxChannels; i++)
        {
            if (Channels[i].InUse)
            {
                ComputeAmplitudes(ref Channels[i], curTime, out float left, out float right);
                leftMotor += left;
                rightMotor += right;
            }
        }

        // The screen shake: negative on the left motor, positive on the right.
        float shakeLeft = 0.0f;
        float shakeRight = 0.0f;
        if (ScreenShake != 0.0f)
        {
            if (ScreenShake < 0.0f)
                shakeLeft = MathF.Abs(ScreenShake);
            else
                shakeRight = ScreenShake;
        }

        leftMotor += shakeLeft;
        rightMotor += shakeRight;

        leftMotor *= _cvars.RumbleScale.Float;
        rightMotor *= _cvars.RumbleScale.Float;

        if (paused)
        {
            leftMotor = 0.0f;
            rightMotor = 0.0f;
        }

        LastOutput = (leftMotor, rightMotor);
        _devices.SetRumble(leftMotor, rightMotor);
    }

    /// <summary>
    /// UpdateRumbleEffects, once a frame: a dead (or absent) local player
    /// stops everything. Stock asked C_BasePlayer::GetLocalPlayer, which is
    /// NULL on this client, so its mixer never ran past this check; the
    /// managed player answers now (memory: port dead stock arms).
    /// </summary>
    public void Frame(float curTime, bool paused, bool localPlayerAlive)
    {
        if (!localPlayerAlive)
        {
            StopAllRumble();
            return;
        }

        UpdateEffects(curTime, paused);
    }

    /// <summary>StopAllRumbleEffects: the channels, and the motors at once.</summary>
    public void StopAllRumble()
    {
        StopAllEffects();
        _devices.StopRumble();
    }

    /// <summary>cs_cl_reload: the channels, the shake and the output switch (the waveforms are rebuilt by Init).</summary>
    public void Save(ReloadWriter w)
    {
        w.Write(MaxChannels);
        foreach (Channel c in Channels)
        {
            w.Write(c.StartTime);
            w.Write(c.Waveform);
            w.Write(c.Priority);
            w.Write(c.InUse);
            w.Write(c.Flags);
            w.Write(c.Scale);
        }
        w.Write(ScreenShake);
        w.Write(OutputEnabled);
    }

    public void Restore(ReloadReader r)
    {
        int channels = r.ReadInt();
        if (channels != MaxChannels)
            throw new ReloadFormatException($"{channels} rumble channels carried, this build has {MaxChannels}");
        for (int i = 0; i < MaxChannels; i++)
        {
            Channels[i].StartTime = r.ReadFloat();
            Channels[i].Waveform = r.ReadInt();
            Channels[i].Priority = r.ReadInt();
            Channels[i].InUse = r.ReadBool();
            Channels[i].Flags = r.ReadInt();
            Channels[i].Scale = r.ReadFloat();
        }
        ScreenShake = r.ReadFloat();
        OutputEnabled = r.ReadBool();
    }

    /// <summary>UpdateScreenShakeRumble from the view effects: only for a living local player.</summary>
    public void ScreenShakeFromView(float shake, bool localPlayerAlive)
    {
        if (!localPlayerAlive)
            return;
        UpdateScreenShakeRumble(shake);
    }
}
