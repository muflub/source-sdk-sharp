//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

namespace SourceSharp.Client.Sounds;

/// <summary>
/// <c>C_BasePlayer::Simulate</c>'s "update step sounds for all other
/// players", once a client frame: each player but the local one steps from
/// where it is drawn and its networked movement, over the surface below its
/// feet (<c>GetGroundSurface</c>). Stock estimated the velocity from the
/// interpolated origin's history (<c>EstimateAbsVelocity</c>); this reads the
/// networked velocity the player's animation reads, and traces a ray, not the
/// player's box, for the ground.
/// </summary>
public sealed class OtherPlayerSteps(IClientStandalones standalones, IEngineHud screen, IEngineEntities entities,
                                     IEngineWorldQuery world, IFwdPhysicsSurfaceProps surfaces, ClientStepSounds steps)
{
    /// <summary>GetGroundSurface's reach below the feet.</summary>
    public const float GroundReach = 64f;

    public void Frame(float frameTime)
    {
        int local = screen.LocalPlayerIndex;
        foreach (ClientStandaloneEntity e in standalones.Live())
        {
            if (e is not ClientSsPlayer p || p.Index == local || p.Index <= 0)
                continue;

            Vector origin = entities.GetOrigin(p.Handle);
            steps.UpdateOther(p.Index, origin, p.State.Velocity, frameTime, (int)p.State.Flags, p.State.MoveType, p.State.WaterLevel,
                              GroundMaterial(origin));
        }
    }

    /// <summary>The game material of the surface under <paramref name="origin"/>, or -1 for no ground within reach.</summary>
    private int GroundMaterial(in Vector origin)
    {
        ClientTraceResult down = world.ClientTraceLine(origin + new Vector(0f, 0f, 1f), origin - new Vector(0f, 0f, GroundReach), TraceMask.Brushes);
        if (!down.Hit || down.SurfaceProps < 0)
            return -1;
        return surfaces.Surface(down.SurfaceProps) is { Valid: true } s ? s.Material : -1;
    }
}
