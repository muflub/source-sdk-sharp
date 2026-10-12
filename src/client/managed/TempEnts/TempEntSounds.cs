//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// c_te_legacytempents.cpp's <c>CTempEnts::PlaySound</c>: the bounce a
/// tempent makes when it lands. Which sound script its hit sound names, the
/// odds it is heard at all (shell casings at speed always, otherwise one in
/// four; everything else one in six), the volume its vertical speed scales and
/// the pitch it sometimes varies. The engine answers the script's parameters
/// and plays it. Replayed against the recorded bounces (TempEntSoundTests).
/// </summary>
/// <summary>
/// The bounce sound a colliding tempent makes: what ClientTempEntFrame asks
/// at stock's <c>tempents->PlaySound( this, damp )</c>.
/// </summary>
public interface ITempEntBounceSounds
{
    void PlaySound(int hitSound, float damp, in Vector velocity, in Vector origin);
}

public sealed class TempEntSounds(IEngineHost random, IFxGameSounds sounds) : ITempEntBounceSounds
{
    // BOUNCE_* (public/const.h).
    public const int BounceGlass = 0x01, BounceMetal = 0x02, BounceFlesh = 0x04, BounceWood = 0x08,
        BounceShrap = 0x10, BounceShell = 0x20, BounceConcrete = 0x40, BounceShotShell = 0x80;

    /// <summary>Bounces heard since the load (cs_cl_fx_report).</summary>
    public long Played { get; private set; }

    /// <summary>The script a hit sound names; null for none (stock's "null sound").</summary>
    public static string? ScriptFor(int hitSound, out bool shellCasing)
    {
        shellCasing = false;
        switch (hitSound)
        {
            case BounceGlass: return "Bounce.Glass";
            case BounceMetal: return "Bounce.Metal";
            case BounceFlesh: return "Bounce.Flesh";
            case BounceWood: return "Bounce.Wood";
            case BounceShrap: return "Bounce.Shrapnel";
            case BounceShotShell: shellCasing = true; return "Bounce.ShotgunShell";
            case BounceShell: shellCasing = true; return "Bounce.Shell";
            case BounceConcrete: return "Bounce.Concrete";
            default: return null;
        }
    }

    /// <summary>One bounce: <paramref name="damp"/> is the collision's, the velocity and origin the tempent's.</summary>
    public void PlaySound(int hitSound, float damp, in Vector velocity, in Vector origin)
    {
        string? script = ScriptFor(hitSound, out bool shellCasing);
        if (script is null)
            return;

        int zvel = (int)MathF.Abs(velocity.Z);

        if (shellCasing)
        {
            if (zvel < 200 && random.RandomInt(0, 3) != 0)
                return;
        }
        else
        {
            if (random.RandomInt(0, 5) != 0)
                return;
        }

        if (!sounds.Parameters(script, out GameSoundParams p))
            return;

        float volume = p.Volume;

        if (damp > 0.0)
        {
            if (shellCasing)
                volume = (float)(volume * Math.Min(1.0, zvel / 350.0));
            else
                volume = (float)(volume * Math.Min(1.0, zvel / 450.0));

            int pitch;
            if (random.RandomInt(0, 3) == 0 && !shellCasing)
                pitch = random.RandomInt(p.PitchLow, p.PitchHigh);
            else
                pitch = p.Pitch;

            sounds.EmitAtPosition(p, volume, pitch, origin);
            Played++;
        }
    }
}
