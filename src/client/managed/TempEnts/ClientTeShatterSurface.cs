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
/// Where a decoded pane goes: <c>C_TEShatterSurface::PostDataUpdate</c>'s
/// grid of shards (C# since lane c5-particles: ShardEffects).
/// </summary>
public interface IClientShatter
{
    void ShatterSurface(Vector origin, Vector angles, Vector force, Vector forcePos, float width, float height, float shardSize,
                        int surfaceType, Color32 front, Color32 back);
}

/// <summary>
/// <c>CTEShatterSurface</c>, played. A pane of glass or a wall of tile coming
/// apart into per-shard geometry.
///
/// <para>
/// <b>THE FILE IS <c>c_te_glassshatter.cpp</c>, NOT
/// <c>c_te_shattersurface.cpp</c>.</b> The class, the table, the <c>TE_</c>
/// function and both halves are all spelt <c>ShatterSurface</c>; only the
/// filename is not, and two lanes have lost time to that — lane E on the server
/// side and this one on the client's. <see cref="TeShatterSurface"/> says the
/// same thing for the same reason.
/// </para>
///
/// <para>
/// <b>WHAT MOVED AND WHAT DID NOT.</b> The decode moved — fourteen fields,
/// including the two-bit surface type and six colour bytes that are six
/// separate props. The GEOMETRY did not, and could not: stock's
/// <c>PostDataUpdate</c> below the decode is a doubly nested loop laying out
/// <c>(width / shardSize) × (height / shardSize)</c> <c>Particle3D</c> quads
/// along the two axes <c>AngleVectors</c> derives from the pane's angles, each
/// with a random lifetime, a random spin, a random size jitter, and a velocity
/// that falls off as the inverse square of its distance from the impact point —
/// then hands the emitter's <c>CParticleCollision</c> the minimum and maximum
/// of the speeds it just drew. Lane C2 named this as one of the drawing paths
/// that cannot be flattened into a generic slot and it was right.
/// </para>
///
/// <para>
/// <b>SO THIS LANE ADDED ONE ABI SLOT</b>, on lane CP's terms:
/// <c>ClientShatterSurface</c>, named for what it draws, taking the fourteen
/// decoded values and no kind enum. It is the ninth such primitive and the last
/// of plan Part 4's particle row before the legacy pool.
/// </para>
///
/// <para>
/// <b>THE LIGHTING IS NOT SENT AND IS NOT DECODED.</b> Stock asks
/// <c>engine-&gt;ComputeLighting</c> at the pane's origin, lerps the answer 30%
/// toward white under its own <c>// HACK: Blend a little toward white to match
/// the materials</c>, and multiplies both face colours by it. That is a
/// renderer question about a point in the world, so it stays in the backend
/// where the renderer is; this class sends the colours the wire carried.
/// Putting it here would have meant <c>ComputeLighting</c> on the ABI so that
/// C# could multiply six bytes and hand them straight back.
/// </para>
///
/// <para>
/// <b>THE ANGLES ARE READ AS A PLAIN COORD VECTOR</b>, because that is how they
/// were sent: stock declares the member a <c>CNetworkQAngle</c> and the prop a
/// <c>SendPropVector</c>, and <c>c_te_glassshatter.cpp:76</c> receives it with
/// <c>RecvPropVector</c> to match. <see cref="TeShatterSurface"/> has the whole
/// argument. They are handed to <c>AngleVectors</c> as angles at the far end,
/// which is exactly what stock does with them.
/// </para>
///
/// <para>
/// <b>NO LIVE NATIVE SENDER TODAY.</b> <c>func_breakablesurf.cpp</c> is this
/// table's only stock caller and it is excluded from this build; what fires it
/// is <c>TempEnts.ShatterSurface</c> from managed code (and
/// <c>FuncBreakableSurf.ShatterAt</c>, which is the managed replacement).
/// Nothing that fires stops firing; what changes is which table the wire is
/// built from and which side decodes it.
/// </para>
///
/// <para>
/// NO LEDGER ROW. <c>CSimple3DEmitter</c> is ordinary client particle code that
/// was never guarded, and stock's <c>Precache()</c> was empty — but its
/// <c>CLIENTEFFECT_REGISTER</c> block was not, and those four fleck materials
/// moved to <c>shell_te_backends_client.cpp</c> with the drawing rather than
/// leaving with the file. It was the only thing in the build precaching them.
/// </para>
/// </summary>
[ClientTempEnt("CTEShatterSurface")]
public sealed class ClientTeShatterSurface : ClientTempEnt<TeShatterSurface>
{
    private readonly IClientShatter _shatter;

    public ClientTeShatterSurface(IClientShatter shatter)
    {
        _shatter = shatter;
    }

    public override void Play(TeShatterSurface te)
    {
        // Two bits UNSIGNED. ShatterSurface_t has exactly two values and stock's
        // own test is `if ( == SHATTERSURFACE_GLASS ) ... else ...`, so anything
        // that is not glass draws as tile — reproduced rather than validated,
        // because a third value cannot cross a two-bit prop.

        // SIX FIELDS AT SIX CONSECUTIVE OFFSETS, which is what stock's
        // SENDINFO_ARRAYELEM produces and why TeShatterSurface declares them one
        // per prop rather than through the Elems knob. Each is ONE BYTE of blob
        // — RecvPropInt picks its write proxy from the declared storage width —
        // so reading four bytes at m_uchFrontColor[0] would be three bytes of
        // its neighbours.
        var front = new Color32(
            te.FrontColorR,
            te.FrontColorG,
            te.FrontColorB);

        var back = new Color32(
            te.BackColorR,
            te.BackColorG,
            te.BackColorB);

        _shatter.ShatterSurface(te.Origin, te.Angles, te.Force, te.ForcePos,
                                      te.Width, te.Height, te.ShardSize,

                                      // The declaration types this as the enum it is; the ABI
                                      // takes the number. The cast is the boundary, and it is
                                      // better here than a declaration that calls a surface kind
                                      // an int.
                                      (int)te.SurfaceType,
                                      front, back);
    }
}
