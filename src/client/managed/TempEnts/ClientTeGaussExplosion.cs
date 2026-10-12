//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>CTEGaussExplosion</c>, played — the gauss/physcannon discharge:
/// <c>FX_GaussExplosion</c>, whose spark colour and dust cloud come from the
/// type.
///
/// <para>
/// THIS TABLE HAD NO SENDER AT ALL BEFORE THIS PORT. <c>CTEGaussExplosion</c>'s
/// server file was excluded from the build and <c>ITempEntsSystem::GaussExplosion</c>
/// was additionally <c>#ifdef HL2_DLL</c>, which is defined nowhere here — so
/// <c>DT_TEGaussExplosion</c> was a registered RecvTable waiting for a sender
/// that did not exist. That is behaviour ledger row S3, restored unconditionally
/// by lane F, and this class is the other end of the first pairing it has ever
/// had.
/// </para>
///
/// <para>
/// Its client half hides outside the naming convention like Dust's:
/// <c>C_TEGaussExplosion</c> is 40 lines near the bottom of
/// <c>c_impact_effects.cpp</c>, which was 1,400 lines of unrelated impact work
/// until step 7a moved them to
/// <c>sourcesharp/game/client/shell_impact_effects.cpp</c>.
/// The class is guarded rather than the file evicted, because
/// <c>FX_GaussExplosion</c> above it is what this effect DRAWS with and
/// <c>weapon_physcannon.cpp</c> names it directly as well.
/// </para>
///
/// <para>
/// <c>FX_GaussExplosion</c> itself is C# since lane c5-particles
/// (<see cref="Effects.SparkEffects.GaussExplosion(Vector, Vector, int)"/>): the
/// play ends there rather than in a native slot.
/// </para>
/// </summary>
[ClientTempEnt("CTEGaussExplosion")]
public sealed class ClientTeGaussExplosion : ClientTempEnt<TeGaussExplosion>
{
    private readonly Effects.IClientSparks _sparks;

    public ClientTeGaussExplosion(Effects.IClientSparks sparks)
    {
        _sparks = sparks;
    }

    public override void Play(TeGaussExplosion te)
    {
        // Two bits. FX_GaussExplosion switches on it and has a default arm, so
        // a value it has no case for is stock's own behaviour and is passed
        // through rather than clamped here.

        _sparks.GaussExplosion(te.Origin, te.Direction, te.Type);
    }
}
