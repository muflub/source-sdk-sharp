//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Entities.Ctf;

/// <summary>
/// The game's <see cref="IFlagCarriers"/>: the walk the rules' own
/// <c>CarriedBy</c> makes — the stolen flags of stock's
/// <c>ICaptureFlagAutoList</c> (entity_capture_flag.cpp), asked for their
/// carrier — over the side's entity directory. A leaf on purpose: the medigun
/// healing's charge refusal holds this seam, and the rules behind
/// <see cref="ICtfRules"/> would close the medigun's composition into a cycle
/// the container refuses.
/// </summary>
[SideService(Side.Server, As = [typeof(IFlagCarriers)])]
public sealed class FlagCarriers(IEntityDirectory directory) : IFlagCarriers
{
    public ItemTeamFlag? CarriedBy(EntityHandle player)
        => player == EntityHandle.None ? null
         : directory.OfClass(ItemTeamFlag.MapClass).OfType<ItemTeamFlag>()
             .FirstOrDefault(f => f.IsStolen && f.Carrier == player);
}
