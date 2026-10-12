//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Each player's client-data update, once a frame: stock's
/// <c>CBasePlayer::UpdateClientData</c> chain as far as this mod ports it,
/// which is <c>CHL2_Player</c>'s Damage message
/// (<see cref="SsPlayer.UpdateClientData"/>).
///
/// <para>
/// Stock reached it from every player's <c>PreThink</c>; there is no native
/// player to think here, so the walk is the fog's and the skybox's:
/// every <see cref="SsPlayer"/>, at <c>OnPreClientUpdate</c>, after every hit
/// of the frame has landed and before the snapshot goes out.
/// </para>
/// </summary>
public sealed class PlayerClientData : IFrameLifecycle
{
    private readonly IEntityDirectory _entities;
    private readonly IGameClock _clock;

    public PlayerClientData(IEntityDirectory entities, IGameClock clock, Observers? observers = null)
    {
        _entities = entities;
        _clock = clock;
        _observers = observers;
    }

    private readonly Observers? _observers;

    public int FrameOrder => (int)FrameStage.PlayerClientData;

    /// <summary>Updates run since the module started, for the registration test.</summary>
    public int Updates { get; private set; }

    void IFrameLifecycle.OnPreClientUpdate(float frameTime) => Update();

    public void Update()
    {
        Updates++;
        float now = _clock.Now;

        foreach (var entity in _entities.OfClass(EntityNames.SsPlayer))
        {
            if (entity is not SsPlayer player)
                continue;

            player.PreThink(now);

            // PreThink: CheckObserverSettings each frame past the freeze cam.
            if (_observers is not null && player.State.ObserverMode > (int)ObserverMode.FreezeCam)
                _observers.CheckSettings(player, now);
        }
    }
}
