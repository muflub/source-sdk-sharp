//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared;

/// <summary>What a scrape throws off.</summary>
[Flags]
public enum FrictionEffect
{
    None = 0,
    Dust = 1,
    MetalSparks = 2,
}

/// <summary>
/// Stock's <c>PhysFrictionEffect</c> (game/shared/physics_shared.cpp): dust
/// off dirt above 15 u/s at mass 10 and off concrete above 28, and metal or
/// grate scraping concrete or metal above 50 throws sparks. Energy is
/// <c>MASS10_SPEED2ENERGY</c>, speed squared times ten.
/// </summary>
public static class PhysicsFrictionEffect
{
    public const int Dirt = 'D';
    public const int Concrete = 'C';
    public const int Metal = 'M';
    public const int Grate = 'G';

    /// <summary><c>MASS10_SPEED2ENERGY( speed )</c>.</summary>
    public static float Mass10Energy(float speed) => speed * speed * 10f;

    public static FrictionEffect Choose(int selfMaterial, int hitMaterial, float energy)
    {
        var effect = FrictionEffect.None;

        if ((hitMaterial == Dirt && energy >= Mass10Energy(15))
            || (hitMaterial == Concrete && energy >= Mass10Energy(28)))
            effect |= FrictionEffect.Dust;

        if (energy > Mass10Energy(50)
            && (selfMaterial == Metal || selfMaterial == Grate)
            && (hitMaterial == Concrete || hitMaterial == Metal))
            effect |= FrictionEffect.MetalSparks;

        return effect;
    }
}
