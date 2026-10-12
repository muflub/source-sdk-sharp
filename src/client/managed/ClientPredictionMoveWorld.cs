//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// The movement loop's world during the client's prediction, with stock's
/// client move helper (game/client/movehelper_client.cpp's CMoveHelperClient)
/// as its effects (plan_client_novalve C6, cluster 9; lane c6-input): the
/// questions -- traces, contents, surfaces -- go to the engine as before, and
/// the helper's answers are taken here instead of crossing to native's inert
/// stand-in.
/// </summary>
public sealed class ClientPredictionMoveWorld(IMoveWorld engine, Func<EntityHandle> host, Func<bool> firstRun,
                                              IEngineSound sound) : IMoveWorld
{
    public MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs, uint mask, int collisionGroup)
        => engine.Trace(start, end, mins, maxs, mask, collisionGroup);

    public int PointContents(in Vector point, int slot) => engine.PointContents(point, slot);

    public MoveSurface Surface(int surfaceProps) => engine.Surface(surfaceProps);

    // THE TOUCH LIST HAS NO READER ON THIS CLIENT. Stock's helper keeps it for
    // ProcessImpacts, which hands each touched entity C_BaseEntity::
    // PhysicsImpact -- its touch function -- and no client entity in this
    // build has one (the shells and the managed client entities define no
    // touch), so the list and cl_movehelper_process* had nothing to act on;
    // native's stand-in ignored both calls already.
    public void ResetTouchList()
    {
    }

    public void AddToTouched(in MoveTrace trace, in Vector velocity)
    {
    }

    /// <summary>
    /// CMoveHelperClient::StartSound( origin, soundname ): from the predicting
    /// player at the origin, to the local player, under the prediction rules --
    /// which drop the local player from the filter on every run but the first,
    /// so a re-predicted command is silent.
    /// </summary>
    public void StartSound(in Vector origin, string soundName)
    {
        if (!firstRun())
            return;

        sound.ClientEmitSound(host(), soundName, origin);
    }

    /// <summary>"Do nothing on the client. Animations are set on the server."</summary>
    public void PlayerSetAnimation(int activity)
    {
    }

    /// <summary>"Do nothing; falling damage is applied in MoveHelper_Server::PlayerFallingDamage."</summary>
    public bool PlayerFallingDamage(float damage) => true;

    public void PlayStepSound(in Vector origin, int surfaceProps, float volume, bool force)
        => engine.PlayStepSound(origin, surfaceProps, volume, force);

    public void UpdateStepSound(in Vector origin, in Vector velocity, int surfaceProps)
        => engine.UpdateStepSound(origin, velocity, surfaceProps);

    public void Splash() => engine.Splash();

    public void RumbleEffect(byte index, byte data, byte flags) => engine.RumbleEffect(index, data, flags);

    public float EngineTime() => engine.EngineTime();

    public void ResetDuckLatched() => engine.ResetDuckLatched();

    public void SetPlayerSurface(int material) => engine.SetPlayerSurface(material);
}
