//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// <c>CTFPlayer::PostThink</c>'s <c>m_angEyeAngles = EyeAngles()</c>
/// (tf_player.cpp:2687), once a frame for every player, into the carrier's
/// wire words (<see cref="TFEyeAngleWire"/>): what another client's in-eye
/// view looks along.
/// </summary>
[SideService(Side.Server)]
public sealed class TFEyeAngles(IEntityDirectory directory, IEngineEntities entities, TFPlayerCarriers carriers)
    : IFrameLifecycle
{
    int IFrameLifecycle.FrameOrder => TFPlayerOrders.EyeAngles;

    void IFrameLifecycle.OnPostThink(float frameTime) => Copy();

    /// <summary>Every live player's eye angles into its carrier.</summary>
    public void Copy()
    {
        foreach (var entity in directory.OfClass(EntityNames.Player))
        {
            if (entity is not SsPlayer player || carriers.Get(player.Handle.EntryIndex) is not { } state)
                continue;

            Vector eyes = entities.GetEyeAngles(player.Handle);
            state.EyePitch = TFEyeAngleWire.EncodePitch(TFEyeAngleWire.SignedPitch(eyes.X));
            state.EyeYaw = TFEyeAngleWire.EncodeYaw(eyes.Y);
        }
    }
}
