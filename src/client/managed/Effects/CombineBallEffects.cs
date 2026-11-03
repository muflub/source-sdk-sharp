//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>DECLARE_CLIENT_EFFECT( "cball_bounce", CombineBallImpactCallback )</c>
/// (c_prop_combine_ball.cpp): where the AR2's ball comes off a wall, a quick flash,
/// a lingering burn and sparks.
/// </summary>
public sealed class CombineBallBounceEffect(IFxQuadSink quads, IClientSparks sparks) : IClientEffectHandler
{
    private const FxQuadFlags Bias = FxQuadFlags.BiasScale | FxQuadFlags.BiasAlpha;

    private static readonly Vector White = new(1f, 1f, 1f);

    // Each quad's spin, as stock's random->RandomInt picks it: unseeded.
    private readonly Random _yaw = new();

    public string EffectName => "cball_bounce";

    public void Dispatch(in EffectData data)
    {
        Vector normal = data.Normal;
        quads.AddQuad(data.Origin, normal, data.Radius * 10f, 0f, 0.75f, 1f, 0f, 0.4f, _yaw.Next(0, 361), 0f,
                      White, 0.25f, "effects/combinemuzzle1_nocull", Bias);
        quads.AddQuad(data.Origin, normal, data.Radius * 2f, data.Radius * 4f, 0.75f, 1f, 0f, 0.4f, _yaw.Next(0, 361), 0f,
                      White, 0.5f, "effects/combinemuzzle2_nocull", Bias);
        sparks.ElectricSpark(data.Origin, 2, 1, normal);
    }
}

/// <summary>
/// <c>DECLARE_CLIENT_EFFECT( "cball_explode", CombineBallExplosionCallback )</c>: the
/// sparks at the ball's end, under the server's rings.
/// </summary>
public sealed class CombineBallExplodeEffect(IClientSparks sparks) : IClientEffectHandler
{
    private static readonly Vector Up = new(0f, 0f, 1f);

    public string EffectName => "cball_explode";

    public void Dispatch(in EffectData data) => sparks.ElectricSpark(data.Origin, 4, 1, Up);
}
