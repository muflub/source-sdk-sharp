//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Shared;

namespace TF2.Server.Buildings;

/// <summary>
/// The world's standing buildings, asked the way stock asks its player list
/// (<c>CTFPlayer::GetObjectOfType</c>'s <c>m_aObjects</c> walk,
/// tf_player.cpp:14848; the teleporter's <c>FindMatch</c>): by builder, by
/// type, by mode.
/// </summary>
public interface ITFBuildings
{
    /// <summary>
    /// The live buildings, optionally narrowed by builder, by type, and by
    /// mode. Enumerated from the entity directory, so a removed building is
    /// out of the answer at once.
    /// </summary>
    IEnumerable<TFBaseObject> OwnedBy(EntityHandle? builder = null, TFObjectType? type = null, int? mode = null);
}

/// <summary>The directory's obj_* entities, as <see cref="ITFBuildings"/>.</summary>
[SideService(Side.Server, As = [typeof(ITFBuildings)])]
public sealed class TFBuildings(IEntityDirectory directory) : ITFBuildings
{
    public IEnumerable<TFBaseObject> OwnedBy(EntityHandle? builder = null, TFObjectType? type = null, int? mode = null)
    {
        foreach (string className in TFObjectClassNames.AllClassNames())
        {
            foreach (TFBaseObject building in directory.OfClass(className).OfType<TFBaseObject>())
            {
                if (building.IsDead)
                    continue;

                if (builder is { } who && building.Builder != who)
                    continue;

                if (type is { } what && building.Type != what)
                    continue;

                if (mode is { } which && building.Mode != which)
                    continue;

                yield return building;
            }
        }
    }
}
