//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// The client's <c>IEffects</c>, decided in C#: <c>CEffectsClient</c>
/// (<c>game/client/EffectsClient.cpp</c>). Each method is stock's suppression
/// test and then the one <c>FX_*</c> routine stock called, with stock's
/// arguments. The routines are C# (lane c5-particles): fx.cpp's muzzle flashes,
/// smoke and ricochet sound (<see cref="IFxRoutines"/>) and the three spark
/// routines (<see cref="IClientSparks"/>); only <c>FX_Dust</c> is still native
/// (<see cref="IClientDustPuff"/>).
/// <c>Fixtures/ieffects-stock.tsv</c> is the recorded acceptance
/// (ClientEffectsTests).
///
/// <para>
/// <c>Beam</c> is not here: stock's is an <c>Assert(0)</c> and draws nothing.
/// <c>Time</c> and <c>IsServer</c> are not either: they answer
/// <c>gpGlobals-&gt;curtime</c> and <c>false</c>, which decide nothing, so the
/// native thunk answers them without a crossing.
/// </para>
/// </summary>
public sealed class ClientEffects : IClientEffects
{
    private readonly IClientDustPuff _puffs;
    private readonly IFxRoutines _routines;
    private readonly IClientSparks _sparks;
    private readonly IFxSuppression _suppression;
    private readonly IEngineHost _host;
    private readonly IEngineConsole _console;

    // m_bSuppressSound: set only through SuppressEffectsSounds, false at start.
    private bool _suppressSound;

    public ClientEffects(IClientDustPuff puffs, IFxRoutines routines, IClientSparks sparks, IFxSuppression suppression, IEngineHost host,
                         IEngineConsole console)
    {
        _puffs = puffs;
        _routines = routines;
        _sparks = sparks;
        _suppression = suppression;
        _host = host;
        _console = console;
    }

    /// <summary>Calls that drew, for the effects route and its gate.</summary>
    public int Drawn { get; private set; }

    /// <summary>Calls <c>SuppressTE</c> threw away.</summary>
    public int Suppressed { get; private set; }

    /// <summary>Whether ricochets are silent (<c>m_bSuppressSound</c>).</summary>
    public bool EffectsSoundsSuppressed => _suppressSound;

    /// <summary>
    /// Stock's <c>SuppressTE</c> over the PVS filter at <paramref name="origin"/>:
    /// true (throw the effect away) when the system may not predict now, or
    /// when the filter reaches nobody.
    /// </summary>
    private bool SuppressTe(Vector origin)
    {
        if (!_suppression.CanPredict() || _suppression.RecipientCount(origin) == 0)
        {
            Suppressed++;
            return true;
        }

        Drawn++;
        return false;
    }

    /// <summary><c>IEffects::Smoke</c>: a grey puff, its shade drawn from the engine's stream.</summary>
    public void Smoke(Vector origin, int modelIndex, float scale, float framerate)
    {
        if (SuppressTe(origin))
            return;

        int shade = _host.RandomInt(20, 35);
        var color = new Color32(shade, shade, shade, shade);
        Vector up = Angles.FromVector(new Vector(0f, 0f, 1f));
        _routines.Smoke(origin, new QAngle(up.X, up.Y, up.Z), scale * 0.1f, 4, color, 255);
    }

    /// <summary><c>IEffects::Sparks</c>.</summary>
    public void Sparks(Vector position, int magnitude, int trailLength, Vector? direction)
    {
        if (SuppressTe(position))
            return;

        _sparks.ElectricSpark(position, magnitude, trailLength, direction);
    }

    /// <summary><c>IEffects::Dust</c>.</summary>
    public void Dust(Vector origin, Vector direction, float size, float speed)
    {
        if (SuppressTe(origin))
            return;

        _puffs.Puff(origin, direction, size, speed);
    }

    /// <summary>
    /// <c>IEffects::MuzzleFlash</c>: the type picks the routine
    /// (<c>MUZZLEFLASH_TYPE_DEFAULT</c> 1, <c>_GUNSHIP</c> 2, <c>_STRIDER</c> 4);
    /// any other type is stock's console line and nothing drawn.
    /// </summary>
    public void MuzzleFlash(Vector origin, QAngle angles, float scale, int type)
    {
        if (SuppressTe(origin))
            return;

        switch (type)
        {
            case 1:
                _routines.MuzzleEffect(origin, angles, scale);
                break;
            case 2:
                _routines.GunshipMuzzleEffect(origin, angles, scale);
                break;
            case 4:
                _routines.StriderMuzzleEffect(origin, angles, scale);
                break;
            default:
                _console.Msg($"No case for Muzzleflash type: {type}\n");
                break;
        }
    }

    /// <summary><c>IEffects::MetalSparks</c>: the direction is the surface normal too.</summary>
    public void MetalSparks(Vector position, Vector direction)
    {
        if (SuppressTe(position))
            return;

        _sparks.MetalSpark(position, direction, direction);
    }

    /// <summary><c>IEffects::EnergySplash</c>.</summary>
    public void EnergySplash(Vector position, Vector direction, bool explosive)
    {
        if (SuppressTe(position))
            return;

        _sparks.EnergySplash(position, direction, explosive);
    }

    /// <summary><c>IEffects::Ricochet</c>: metal sparks, and the sound unless it is suppressed.</summary>
    public void Ricochet(Vector position, Vector direction)
    {
        if (SuppressTe(position))
            return;

        _sparks.MetalSpark(position, direction, direction);

        if (!_suppressSound)
            _routines.RicochetSound(position);
    }

    /// <summary><c>IEffects::SuppressEffectsSounds</c>.</summary>
    public void SuppressEffectsSounds(bool suppress) => _suppressSound = suppress;
}
