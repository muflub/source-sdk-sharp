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
/// <c>CTEBloodSprite</c>, played. The bullet-wound sprite: one large,
/// high-priority sprite that plays its whole animation in a quarter of a second.
///
/// <para>
/// The whole of <c>c_te_bloodsprite.cpp</c>'s
/// <c>C_TEBloodSprite::PostDataUpdate</c>:
/// </para>
/// <code>
/// Vector offset = m_vecOrigin + ( m_vecDirection * 4.0f );
/// tempents-&gt;BloodSprite( offset, r, g, b, a, m_nSprayModel, m_nDropModel, m_nSize );
/// FX_Blood( offset, m_vecDirection, r, g, b, a );
/// </code>
///
/// <para>
/// <b>TWO EFFECTS.</b> The second line is <c>FX_Blood</c> — a separate
/// particle emitter that draws the spray of droplets and has nothing to do with
/// the temp entity pool. It is C#'s <see cref="Effects.ImpactSprays"/> (lane
/// c5-particles), reached through <see cref="Effects.IImpactBlood"/>, so both
/// halves of every bullet wound happen here, managed, as stock's two calls did.
/// </para>
///
/// <para>
/// THE OFFSET IS FOUR UNITS ALONG THE DIRECTION, and it is applied ONCE, here,
/// to both halves. Stock computes it in a local and hands the same value to
/// both calls; doing it twice, or applying it in the helper, would move the
/// sprite four units and leave the droplets behind.
/// </para>
///
/// <para>
/// THE DROP MODEL IS CARRIED AND NEVER READ.
/// <see cref="TempEntSpawns.BloodSprite"/> says why: stock's helper takes it and
/// its body never mentions it. It is on the wire, the server sends it, and this
/// hands it on so that the call reads as stock's does.
/// </para>
///
/// <para>
/// NO LEDGER ROW. Both effects still fire and neither was replaced by a stub.
/// </para>
/// </summary>
[ClientTempEnt("CTEBloodSprite")]
public sealed class ClientTeBloodSprite : ClientTempEnt<TeBloodSprite>
{
    private readonly TempEntSpawns _spawns;
    private readonly Effects.IImpactBlood _blood;

    public ClientTeBloodSprite(TempEntSpawns spawns, Effects.IImpactBlood blood)
    {
        _spawns = spawns;
        _blood = blood;
    }

    /// <summary>Stock's <c>m_vecOrigin + ( m_vecDirection * 4.0f )</c>.</summary>
    private const float SpriteStandoff = 4f;

    public override void Play(TeBloodSprite te)
    {
        // ONE local, handed to both halves, exactly as stock does it.
        Vector offset = te.Origin + te.Direction * SpriteStandoff;

        _spawns.BloodSprite(offset, te.Color.R, te.Color.G, te.Color.B, te.Color.A, te.SprayModel, te.DropModel, te.Size);

        // The droplets: FX_Blood, C#'s ImpactSprays (lane c5-particles). This is
        // the SECOND effect of two and dropping it would halve every bullet
        // wound in the game.
        _blood.Blood(offset, te.Direction, te.Color.R, te.Color.G, te.Color.B, te.Color.A);
    }
}
