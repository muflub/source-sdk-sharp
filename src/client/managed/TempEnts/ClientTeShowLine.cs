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
/// <c>CTEShowLine</c>, played — a line of static particles from the origin to
/// <c>m_vecEnd</c>, which is a debug primitive rather than an effect anything
/// in a map fires.
///
/// <para>
/// The whole of <c>c_te_showline.cpp</c>'s <c>PostDataUpdate</c> is the walk
/// itself, so the DECODE is what moved here and the walk is
/// <c>ClientShowLine</c>. Nothing was lost with the file: it had no
/// <c>Precache</c> and no tool recording.
/// </para>
///
/// <para>
/// STOCK ADVANCED <c>m_vecOrigin</c> AS IT WALKED — <c>m_vecOrigin += vec;</c>
/// on the temp entity's own member, inside the loop — which worked only because
/// the singleton is overwritten by the next event of the class. The backend
/// walks a local instead. That is the same drawing and it is worth naming,
/// because it is exactly the kind of difference that would otherwise be found
/// as "the second line of the level starts in the wrong place".
/// </para>
/// </summary>
[ClientTempEnt("CTEShowLine")]
public sealed class ClientTeShowLine : ClientTempEnt<TeShowLine>
{
    private readonly Effects.IClientTeParticles _particles;

    public ClientTeShowLine(Effects.IClientTeParticles particles)
    {
        _particles = particles;
    }

    public override void Play(TeShowLine te)
    {
        // Origin is DT_TEParticleSystem's and End is this table's own; the
        // declaration reader walks the chain, so both arrive from one call.

        // C_TEShowLine's dots, C#'s since lane c5-particles (TeParticles).
        _particles.ShowLine(te.Origin, te.End);
    }
}
