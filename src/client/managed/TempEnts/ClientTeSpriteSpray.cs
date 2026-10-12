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
/// <c>CTESpriteSpray</c>, played. A cone of sprites thrown along a direction,
/// fading as they fall.
///
/// <para>
/// The whole of <c>c_te_spritespray.cpp</c>'s
/// <c>C_TESpriteSpray::PostDataUpdate</c>:
/// </para>
/// <code>
/// tempents-&gt;Sprite_Spray( m_vecOrigin, m_vecDirection, m_nModelIndex,
///                        m_nCount, m_nSpeed * 0.2, m_fNoise * 100.0 );
/// </code>
///
/// <para>
/// <b>THE TWO SCALINGS ARE THE INTERESTING PART AND THEY GO OPPOSITE WAYS.</b>
/// The speed is DIVIDED by five on its way in (× 0.2) and the noise is
/// MULTIPLIED by a hundred — and the helper then divides the noise by a hundred
/// again as its first line. That round trip is not pointless: <c>m_fNoise</c>
/// is a float on the wire and <c>iRand</c> is an int in the helper, so the
/// hundred is what preserves two decimal places across an integer parameter.
/// Dropping either scaling looks harmless and is not: without the speed one
/// every spray flies five times too far, and without the noise one every spray
/// is a straight line.
/// </para>
///
/// <para>
/// THE ARGUMENT ORDER IS NOT THE DECLARATION ORDER, and that is a trap worth
/// naming: the send table lists noise before count before speed, and the helper
/// takes count, speed, then noise. Reading the two side by side is how a
/// transposition would be missed, so this class names every argument.
/// </para>
///
/// <para>
/// NO LEDGER ROW.
/// </para>
/// </summary>
[ClientTempEnt("CTESpriteSpray")]
public sealed class ClientTeSpriteSpray : ClientTempEnt<TeSpriteSpray>
{
    private readonly TempEntSpawns _spawns;

    public ClientTeSpriteSpray(TempEntSpawns spawns)
    {
        _spawns = spawns;
    }

    /// <summary>Stock's <c>m_nSpeed * 0.2</c>.</summary>
    private const float SpeedFromWire = 0.2f;

    /// <summary>
    /// Stock's <c>m_fNoise * 100.0</c> — a float turned into hundredths so it
    /// survives an int parameter, which the helper then undoes.
    /// </summary>
    private const float NoiseToHundredths = 100f;

    public override void Play(TeSpriteSpray te)
    {
        _spawns.SpriteSpray(
            pos: te.Origin,
            dir: te.Direction,
            modelIndex: te.ModelIndex,
            count: te.Count,

            // Truncated to an int by C's conversion, as stock's argument is:
            // Sprite_Spray takes `int speed` and is handed a float.
            speed: (int)(te.Speed * SpeedFromWire),

            scatter: (int)(te.Noise * NoiseToHundredths));
    }
}
