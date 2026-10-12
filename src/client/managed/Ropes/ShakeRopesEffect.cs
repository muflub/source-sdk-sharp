//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Ropes;

/// <summary>
/// The "ShakeRopes" client effect (c_rope.cpp's <c>ShakeRopesCallback</c>):
/// every rope near the effect's origin takes an upward impulse that falls off
/// with distance.
/// </summary>
public sealed class ShakeRopesEffect(IClientRopes ropes) : IClientEffectHandler
{
    public string EffectName => "ShakeRopes";

    public void Dispatch(in EffectData data) => ropes.Shake(data.Origin, data.Radius, data.Magnitude);
}
