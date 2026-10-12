//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>CEnvWindShared</c>'s simulation (env_wind_shared.cpp, carried in
/// shell_effects_sim.cpp until lane c2-sims): two seeded streams, the gust
/// state machine that switches between a steady wind and a gust at random
/// times, the speed ramping towards the average at 150 up and 15 down a
/// second, a variation redrawn every tenth of a second, and the tree sway
/// that lerps the last two two-second samples. Client-server neutral by
/// stock's design: the streams pick the TIMES the wind changes, so the result
/// does not depend on how often it is asked. Replayed bit for bit against
/// <c>Fixtures/wind-stock.tsv</c>.
/// </summary>
public sealed class WindSim
{
    private const float VariationUpdateTime = 0.1f;
    private const float TreeSwayUpdateTime = 2.0f;
    private const float WindAcceleration = 150.0f;
    private const float WindDeceleration = 15.0f;

    // The map's numbers (stock's wire state).
    public int MinWind, MaxWind, MinGust, MaxGust, GustDirChange;
    public float MinGustDelay, MaxGustDelay, GustDuration;

    private readonly UniformStream _stream = new();
    private readonly UniformStream _variationStream = new();

    public float StartTime { get; private set; }
    public float SimTime { get; private set; }
    public float SwitchTime { get; private set; }
    public float VariationTime { get; private set; }
    public float SwayTime { get; private set; }
    public float WindSpeed { get; private set; }
    public float AveWindSpeed { get; private set; }
    public int WindDir { get; private set; }
    public bool Gusting { get; private set; }
    public float AngleVariation { get; private set; } = 1.0f;
    public float SpeedVariation { get; private set; } = 1.0f;

    /// <summary>What <c>GetWindspeedAtTime</c> answers: the wind's last vector.</summary>
    public Vector CurrentWind { get; private set; }

    public Vector PrevSway { get; private set; }

    public Vector CurrentSway { get; private set; }

    /// <summary>The last <c>VECTOR_RENDERPARM_WIND_DIRECTION</c> the sway handed the shaders.</summary>
    public Vector SwayParameter { get; private set; }

    /// <summary>
    /// <c>Init</c>: the five clocks at the start time (the sway's too, which
    /// stock left unset and our shell set; see shell_effects_sim.cpp's note),
    /// both streams seeded, the direction and speed, and gusting so the first
    /// switch ends a gust.
    /// </summary>
    public void Init(int seed, float time, int initialYaw, float initialSpeed)
    {
        AngleVariation = SpeedVariation = 1.0f;
        StartTime = SimTime = SwitchTime = VariationTime = time;
        SwayTime = time;
        CurrentSway = default;
        PrevSway = default;
        _stream.SetSeed(seed);
        _variationStream.SetSeed(seed);
        WindDir = initialYaw;
        AveWindSpeed = WindSpeed = initialSpeed;
        Gusting = true;
    }

    /// <summary><c>anglemod</c> (mathlib.h): the angle wrapped through 16 bits.</summary>
    public static float AngleMod(float a) => (360.0f / 65536) * ((int)(a * (65536.0f / 360.0f)) & 65535);

    private void ComputeWindVariation(float time)
    {
        while (time >= VariationTime)
        {
            AngleVariation = _variationStream.RandomFloat(-10, 10);
            SpeedVariation = (float)(1.0 + _variationStream.RandomFloat(-0.2f, 0.2f));
            VariationTime += VariationUpdateTime;
        }
    }

    private void UpdateTreeSway(float time)
    {
        while (time >= SwayTime)
        {
            PrevSway = CurrentSway;
            CurrentSway = CurrentWind;
            SwayTime += TreeSwayUpdateTime;
        }

        float percentage = 1 - ((SwayTime - time) / TreeSwayUpdateTime);
        Vector lerp = PrevSway + (CurrentSway - PrevSway) * percentage;
        SwayParameter = lerp * (1.0f / 2);
    }

    /// <summary>Stock's <c>WindThink</c>: simulate up to <paramref name="time"/>; the next think time.</summary>
    public float Think(float time)
    {
        ComputeWindVariation(time);
        UpdateTreeSway(time);

        while (true)
        {
            float timeToSwitch = SwitchTime - SimTime;
            float maxDeltaTime = time - SimTime;
            bool gotToSwitchTime = maxDeltaTime > timeToSwitch;
            float simDeltaTime = gotToSwitchTime ? timeToSwitch : maxDeltaTime;

            if (AveWindSpeed > WindSpeed)
            {
                WindSpeed += WindAcceleration * simDeltaTime;
                if (WindSpeed > AveWindSpeed)
                    WindSpeed = AveWindSpeed;
            }
            else if (AveWindSpeed < WindSpeed)
            {
                WindSpeed -= WindDeceleration * simDeltaTime;
                if (WindSpeed < AveWindSpeed)
                    WindSpeed = AveWindSpeed;
            }

            if (!gotToSwitchTime)
            {
                SimTime = time;
                Vector forward = MoveVec.AngleForward(new Vector(0, WindDir + AngleVariation, 0));
                float totalWindSpeed = WindSpeed * SpeedVariation;
                CurrentWind = forward * totalWindSpeed;
                return time + 0.01f;
            }

            SimTime = SwitchTime;
            if (Gusting)
            {
                AveWindSpeed = _stream.RandomInt(MinWind, MaxWind);
                Gusting = false;
                SwitchTime += MinGustDelay + _stream.RandomFloat(0, MaxGustDelay);
            }
            else
            {
                AveWindSpeed = _stream.RandomInt(MinGust, MaxGust);
                WindDir = (int)AngleMod(WindDir + _stream.RandomInt(-GustDirChange, GustDirChange));
                Gusting = true;
                SwitchTime += GustDuration;
            }
        }
    }

    /// <summary><c>CWindThinkSchedule::CanAdvance</c>: a wind with no delay and no duration would never leave its first switch.</summary>
    public bool CanAdvance => MinGustDelay > 0.0f || MaxGustDelay > 0.0f || GustDuration > 0.0f;

    public void Save(BinaryWriter w)
    {
        w.Write(MinWind); w.Write(MaxWind); w.Write(MinGust); w.Write(MaxGust); w.Write(GustDirChange);
        w.Write(MinGustDelay); w.Write(MaxGustDelay); w.Write(GustDuration);
        _stream.Save(w);
        _variationStream.Save(w);
        w.Write(StartTime); w.Write(SimTime); w.Write(SwitchTime); w.Write(VariationTime); w.Write(SwayTime);
        w.Write(WindSpeed); w.Write(AveWindSpeed); w.Write(WindDir); w.Write(Gusting);
        w.Write(AngleVariation); w.Write(SpeedVariation);
        foreach (var v in new[] { CurrentWind, PrevSway, CurrentSway, SwayParameter })
        {
            w.Write(v.X); w.Write(v.Y); w.Write(v.Z);
        }
    }

    public void Restore(BinaryReader r)
    {
        MinWind = r.ReadInt32(); MaxWind = r.ReadInt32(); MinGust = r.ReadInt32(); MaxGust = r.ReadInt32(); GustDirChange = r.ReadInt32();
        MinGustDelay = r.ReadSingle(); MaxGustDelay = r.ReadSingle(); GustDuration = r.ReadSingle();
        _stream.Restore(r);
        _variationStream.Restore(r);
        StartTime = r.ReadSingle(); SimTime = r.ReadSingle(); SwitchTime = r.ReadSingle(); VariationTime = r.ReadSingle(); SwayTime = r.ReadSingle();
        WindSpeed = r.ReadSingle(); AveWindSpeed = r.ReadSingle(); WindDir = r.ReadInt32(); Gusting = r.ReadBoolean();
        AngleVariation = r.ReadSingle(); SpeedVariation = r.ReadSingle();
        Vector V() => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        CurrentWind = V(); PrevSway = V(); CurrentSway = V(); SwayParameter = V();
    }
}
