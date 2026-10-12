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
/// <c>CTEBloodStream</c>, played — a stream of gravity voxels down an arcing
/// direction, with a few rogue chunky ones behind it.
///
/// <para>
/// <c>c_te_bloodstream.cpp</c>'s <c>PostDataUpdate</c> builds a
/// <c>CBroadcastRecipientFilter</c> and calls the file's own
/// <c>TE_BloodStream</c>; the filter is dropped here for the reason lane C1's
/// forward records — a client-side <c>TE_</c> function is already on the only
/// machine that will see the effect, and stock's never looked at either the
/// filter or the delay.
/// </para>
///
/// <para>
/// THE AMOUNT IS ALSO THE SPEED. Stock seeds the particle velocity from the
/// same number that decides how many rogue voxels there are
/// (<c>amount / 5</c>), so an amount of 100 is 100 + 20 + 40 = 160 particles
/// moving at 100 units. Both readings are stock's and neither is separated out
/// here — a second parameter would be one the wire does not carry.
/// </para>
///
/// <para>
/// THE COLOUR IS FOUR SEPARATE PROPS, and they are named <c>r</c>, <c>g</c>,
/// <c>b</c>, <c>a</c> on the wire — one letter each, no <c>m_</c> prefix,
/// because that is what <c>RECVINFO(r)</c> spells. Reading them by any tidier
/// name would be a warning and a default rather than a colour.
/// </para>
///
/// <para>
/// WHAT WAS LOST WITH THE FILE: <c>RecordBloodStream</c>, the tool-recording
/// KeyValues message, which is <c>ToolsEnabled()</c>-gated and unreachable in
/// this build. Recorded in docs/divergences.md rather than left silent.
/// </para>
/// </summary>
[ClientTempEnt("CTEBloodStream")]
public sealed class ClientTeBloodStream : ClientTempEnt<TeBloodStream>
{
    private readonly Effects.IClientTeParticles _particles;

    public ClientTeBloodStream(Effects.IClientTeParticles particles)
    {
        _particles = particles;
    }

    public override void Play(TeBloodStream te)
    {
        // Eleven bits over -10..10, which is a normalised direction with room
        // to spare rather than a coord — the backend normalises it again before
        // use, as stock does.

        // TE_BloodStream, C#'s since lane c5-particles (TeParticles).
        _particles.BloodStream(te.Origin, te.Direction, te.Color.R, te.Color.G, te.Color.B, te.Color.A, te.Amount);
    }
}
