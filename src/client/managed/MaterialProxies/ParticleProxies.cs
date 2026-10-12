//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>
/// What the particle manager holds for a lit particle material's bind
/// (<c>CParticleMgr::GetDirectionalLightInfo</c> and <c>GetModelView</c>):
/// the directional light the drawing effect handed it and the model-view
/// matrix its binding set up for the draw.
/// </summary>
public readonly record struct ParticleBindLight(Vector Position, Vector Color, float Intensity, VMatrix ModelView);

/// <summary>
/// particle_proxies.cpp's ParticleSphereProxy ("ParticleSphereProxy"): the
/// particle manager's directional light into a ParticleSphere material,
/// its position in the space the particles draw in and its colour scaled by
/// its intensity. particle/SmokeStack.vmt names it (env_smokestack's
/// default material).
/// </summary>
public sealed class ParticleSphereProxy : MaterialProxy
{
    private int _lightPosition = -1;
    private int _lightColor = -1;

    /// <summary>Stock finds both without complaint and never refuses; a var not found still holds the dummy stock keeps.</summary>
    public override bool Init(IProxyInit init)
    {
        _lightPosition = init.FindVar("$light_position", false, out _);
        _lightColor = init.FindVar("$light_color", false, out _);
        return true;
    }

    /// <summary>The bind argument is the particle manager; with none stock does nothing.</summary>
    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg == 0)
            return;
        ParticleBindLight light = bind.ParticleLight();

        // VMatrix * Vector: the 3x4 part, term for term.
        VMatrix m = light.ModelView;
        Vector p = light.Position;
        Span<float> v = stackalloc float[3];
        v[0] = m[0, 0] * p.X + m[0, 1] * p.Y + m[0, 2] * p.Z + m[0, 3];
        v[1] = m[1, 0] * p.X + m[1, 1] * p.Y + m[1, 2] * p.Z + m[1, 3];
        v[2] = m[2, 0] * p.X + m[2, 1] * p.Y + m[2, 2] * p.Z + m[2, 3];
        if (_lightPosition >= 0)
            bind.SetVec(_lightPosition, v, 3);

        if (_lightColor >= 0)
        {
            Vector c = light.Color;
            v[0] = c.X * light.Intensity;
            v[1] = c.Y * light.Intensity;
            v[2] = c.Z * light.Intensity;
            bind.SetVec(_lightColor, v, 3);
        }
    }
}
