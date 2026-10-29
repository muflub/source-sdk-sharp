//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ragdolls;
using SourceSharp.Client.TempEnts;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>DECLARE_CLIENT_EFFECT( "BoltImpact", StickyBoltCallback )</c>
/// (c_stickybolt.cpp, in stock hl2mp's client through client_base.vpc, never in
/// this build's): a crossbow bolt left sticking out of what it hit.
///
/// <para>
/// <b>WHAT IS CARRIED.</b> All of <c>StickRagdollNow</c>: the sky test (a bolt
/// that hit the skybox leaves nothing), <c>CRagdollBoltEnumerator</c>
/// (<see cref="IRagdollBoltPin"/>: a body the bolt passed through is pinned to
/// the world with a ballsocket constraint, and <c>Weapon_Crossbow.BoltSkewer</c>
/// plays), then <c>CreateCrossbowBolt</c>: the bolt model, 8 units back along
/// the flight, pointing along it, still, for 30 seconds in a multiplayer game
/// and forever in a single-player one. That is the
/// <c>CTempEnts::SpawnTempModel</c> arm, which the legacy tempents' thunk keeps
/// as a no-caller stub because this is its only hl2mp caller. The sender is
/// <c>crossbow_bolt</c>'s port of <c>BoltTouch</c>, which sends <c>BoltImpact</c>
/// from both impact branches.
/// </para>
/// </summary>
public sealed class CrossbowBoltEffect(ITempModelSpawner tempEnts, IEngineWorldQuery world,
                                       IEngineEnvironment environment, IRagdollBoltPin ragdolls) : IClientEffectHandler
{
    /// <summary>The bolt model stock loads by name.</summary>
    public const string BoltModel = "models/crossbow_bolt.mdl";

    /// <summary><c>SURF_SKY</c>.</summary>
    private const int SurfSky = 0x0004;

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

        // The enumerator over the line back along the flight, then the bolt.
        ragdolls.Stick(origin, direction);

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
