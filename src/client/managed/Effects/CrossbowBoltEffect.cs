//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.TempEnts;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>DECLARE_CLIENT_EFFECT( "BoltImpact", StickyBoltCallback )</c>
/// (c_stickybolt.cpp, in stock hl2mp's client through client_base.vpc, never in
/// this build's): a crossbow bolt left sticking out of what it hit.
///
/// <para>
/// <b>WHAT IS CARRIED.</b> <c>StickRagdollNow</c>'s sky test (a bolt that hit
/// the skybox leaves nothing) and <c>CreateCrossbowBolt</c>: the bolt model, 8
/// units back along the flight, pointing along it, still, for 30 seconds in a
/// multiplayer game and forever in a single-player one. That is the
/// <c>CTempEnts::SpawnTempModel</c> arm, which the legacy tempents' thunk keeps
/// as a no-caller stub because this is its only hl2mp caller.
/// </para>
///
/// <para>
/// <b>WHAT IS NOT.</b> <c>CRagdollBoltEnumerator</c>, which pins a ragdoll the
/// bolt passed through to the world with a ballsocket constraint and plays
/// <c>Weapon_Crossbow.BoltSkewer</c>. It needs the client ragdoll's physics
/// objects, which no managed surface reaches yet; it is said once. The sender
/// itself arrived with the port of <c>BoltTouch</c>'s dispatch into
/// <c>ss_bolt</c>, which now sends <c>BoltImpact</c> from both impact branches.
/// </para>
/// </summary>
public sealed class CrossbowBoltEffect(ITempModelSpawner tempEnts, IEngineWorldQuery world,
                                       IEngineEnvironment environment, ILog log) : IClientEffectHandler
{
    /// <summary>The bolt model stock loads by name.</summary>
    public const string BoltModel = "models/crossbow_bolt.mdl";

    /// <summary><c>SURF_SKY</c>.</summary>
    private const int SurfSky = 0x0004;

    private bool _saidRagdolls;

    public string EffectName => "BoltImpact";

    public void Dispatch(in EffectData data)
    {
        Vector origin = data.Origin;
        Vector direction = data.Normal;

        // StickRagdollNow: UTIL_TraceLine( origin, origin + dir * 16,
        // MASK_SOLID_BRUSHONLY ) and out on the sky -- before the bolt is made.
        ClientTraceResult tr = world.ClientTraceLine(origin, origin + direction * 16f, TraceMask.Brushes);
        if ((tr.SurfaceFlags & SurfSky) != 0)
            return;

        if (!_saidRagdolls)
        {
            _saidRagdolls = true;
            log.Msg("[SourceSharp/Client] BoltImpact: the bolt is drawn; pinning a ragdoll it passed "
                  + "through (CRagdollBoltEnumerator) is not carried\n");
        }

        CreateCrossbowBolt(origin, direction);
    }

    /// <summary><c>CreateCrossbowBolt</c>.</summary>
    private void CreateCrossbowBolt(Vector origin, Vector direction)
    {
        Vector angles = Angles.FromVector(direction);
        Vector at = origin - direction * 8f;

        int maxClients = environment.Globals.IsAvailable ? environment.Globals.MaxClients : 1;

        if (maxClients > 1)
            tempEnts.SpawnTempModel(BoltModel, at, angles, Vector.Zero, 30f, (int)TempEntFlags.None);
        else
            tempEnts.SpawnTempModel(BoltModel, at, angles, Vector.Zero, 1f, (int)TempEntFlags.NeverDie);
    }
}
