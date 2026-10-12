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
/// fx_shelleject.cpp's three callbacks: a spent case from the owner's gun, its
/// orientation the effect's angles and the owner's render angles, its kind the
/// effect's (0 pistol, 1 rifle, 2 shotgun). Nothing when the owner has no
/// renderable. Replayed against stock's recorded calls (ClientShellEjectTests).
/// </summary>
public abstract class ShellEjectEffect(IFxRender render, int type, string name) : IClientEffectHandler
{
    public string EffectName => name;

    public void Dispatch(in EffectData data)
    {
        // Use the gun angles to orient the shell.
        if (render.EntityRenderAngles(data.EntIndex, out QAngle gunAngles))
            render.EjectBrass(data.Origin, new QAngle(data.Angles.X, data.Angles.Y, data.Angles.Z), gunAngles, type);
    }
}

/// <summary><c>ShellEjectCallback</c>.</summary>
public sealed class PistolShellEjectEffect(IFxRender render) : ShellEjectEffect(render, 0, "ShellEject");

/// <summary><c>RifleShellEjectCallback</c>.</summary>
public sealed class RifleShellEjectEffect(IFxRender render) : ShellEjectEffect(render, 1, "RifleShellEject");

/// <summary><c>ShotgunShellEjectCallback</c>.</summary>
public sealed class ShotgunShellEjectEffect(IFxRender render) : ShellEjectEffect(render, 2, "ShotgunShellEject");
