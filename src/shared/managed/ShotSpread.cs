//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// Where each pellet of a shot goes, the same on the server and on the
/// predicting client.
///
/// <para>
/// Stock's <c>CBaseEntity::FireBullets</c> (baseentity_shared.cpp) takes
/// <c>iSeed = GetPredictionRandomSeed() &amp; 255</c> for a player, seeds the
/// stream with it before each pellet (<c>RandomSeed( iSeed )</c>) and bumps it
/// after (<c>iSeed++</c>); the pellet's direction is
/// <c>CShotManipulator::ApplySpread</c> (shot_manipulator.h) at the default
/// bias (<c>ai_shot_bias_min</c> -1, <c>ai_shot_bias_max</c> 1, so a shot
/// bias of 1 and a flatness of 0.5). The prediction seed is the command's
/// <c>random_seed</c>, which both sides derive from the command number, so a
/// client that replays the command fires the pellets the server did and its
/// impacts land where the server's damage does.
/// </para>
/// </summary>
public static class ShotSpread
{
    /// <summary>
    /// The first pellet's seed: <c>GetPredictionRandomSeed() &amp; 255</c>
    /// for the command's random seed. Pellet <c>n</c> uses this plus n.
    /// </summary>
    public static int FirstSeed(int commandRandomSeed) => commandRandomSeed & 255;

    /// <summary>
    /// <c>ApplySpread( vecSpread )</c> after <c>RandomSeed( seed )</c>: the aim
    /// pushed along right and up by <paramref name="spread"/> (the cone's
    /// sine) times a bias-shaped sample of the unit disc. Not normalised, as
    /// stock's result is not.
    /// </summary>
    public static Vector Apply(Angles.Basis aim, float spread, int seed, SeededUniformStream stream)
    {
        const float shotBias = 1f;
        const float flatness = shotBias * 0.5f;

        stream.SetSeed(seed);

        float x, y, z;
        do
        {
            x = stream.RandomFloat(-1f, 1f) * flatness + stream.RandomFloat(-1f, 1f) * (1f - flatness);
            y = stream.RandomFloat(-1f, 1f) * flatness + stream.RandomFloat(-1f, 1f) * (1f - flatness);
            z = x * x + y * y;
        }
        while (z > 1f);

        return aim.Forward + aim.Right * (x * spread) + aim.Up * (y * spread);
    }

    /// <summary>The cone's half-angle in degrees as the spread vector's component, its sine.</summary>
    public static float SpreadOf(float spreadDegrees) => MathF.Sin(spreadDegrees * MathF.PI / 180f);
}
