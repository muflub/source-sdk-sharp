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
/// <c>CTEDust</c>, played — the dust PUFF at a point.
///
/// <para>
/// NOT THE <c>func_dust</c> VOLUME, which is a different effect that happens to
/// live in the same file: that one has been C#'s since long before this port
/// (<c>shell_dust_render.cpp</c> owns its motes). <c>c_func_dust.cpp</c> holds
/// three unrelated things and only the middle one is this.
/// </para>
///
/// <para>
/// This is one of the two temp entities in the build whose BOTH halves hide
/// outside the naming convention — the server's was moved verbatim into
/// <c>shell_entity_orphans.cpp</c> and closed as ledger row S6 by lane F, and
/// the client's is here in <c>c_func_dust.cpp</c>. A lane scanning by filename
/// ports 43 of 44 and the count still looks right.
/// </para>
///
/// <para>
/// <c>C_TEDust::PostDataUpdate</c> is one line — <c>FX_Dust</c> — so the whole
/// of the port is this decode. <c>FX_Dust</c> itself was NOT ported and must not
/// be: <c>g_pEffects-&gt;Dust</c> calls it too. It has MOVED, though — step 7a
/// took it out of <c>c_func_dust.cpp</c>, which was the last compiled body in
/// that file, and put it in <c>sourcesharp/game/client/shell_dust_render.cpp</c>
/// beside the motes; the stock TU is out of <c>client_csharp.vpc</c>.
/// <c>src/sourcesharp/test/dust_verdict_test.cpp</c> asserts it is compiled
/// there and defined nowhere else.
/// </para>
/// </summary>
[ClientTempEnt("CTEDust")]
public sealed class ClientTeDust : ClientTempEnt<TeDust>
{
    private readonly Effects.IClientDustPuff _puff;

    public ClientTeDust(Effects.IClientDustPuff puff)
    {
        _puff = puff;
    }

    public override void Play(TeDust te)
    {
        // Size and speed are COORDS on the wire — full precision, not the
        // narrow ranges the direction beside them uses.

        // Four bits over -1..1. That is a very coarse direction and it is
        // stock's: TE_Dust's callers pass a surface normal, and a puff is not
        // aimed so much as tipped.

        // FX_Dust, C#'s since lane c5-particles (DustPuff).
        _puff.Puff(te.Origin, te.Direction, te.Size, te.Speed);
    }
}
