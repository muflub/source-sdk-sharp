//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using TF2.Shared;

namespace TF2.Server.Buildings;

/// <summary>
/// <c>g_ObjectInfos[]</c>: one row of <c>scripts/objects.txt</c> — the metal
/// a build costs, how long it takes, how many one builder may have of a
/// kind at once (per <c>(type, mode)</c>, which is how a MaxObjects of 1 still
/// lets an engineer have both teleporter ends:
/// <c>CTFPlayer::GetNumObjects</c> counts that pair, tf_player.h:402), the
/// metal an upgrade level asks (<c>m_UpgradeCost</c>) and how long that
/// upgrade takes (<c>m_flUpgradeDuration</c>).
///
/// <para>
/// The values are transcribed from the shipped <c>objects.txt</c> rather than
/// read from it at level load; the file-driven table is deferred with the rest
/// of the builder's furniture (<c>docs/plans/tf2/deferred.tsv</c>). The sapper
/// has no row on purpose: it is not the engineer's table to build, and its
/// zero build time is the <see cref="TFBaseObject"/> fallback.
/// </para>
/// </summary>
public sealed record TFObjectInfo(TFObjectType Type, int Cost, float BuildTime, int MaxObjects, int Modes,
                                  int UpgradeCost, float UpgradeDuration)
{
    /// <summary>The row for a buildable type, null for the sapper and anything past it.</summary>
    public static TFObjectInfo? Of(TFObjectType type) => type switch
    {
        TFObjectType.Dispenser => new TFObjectInfo(TFObjectType.Dispenser, Cost: 100, BuildTime: 20f, MaxObjects: 1, Modes: 1,
                                                   UpgradeCost: 200, UpgradeDuration: 20f),
        TFObjectType.Teleporter => new TFObjectInfo(TFObjectType.Teleporter, Cost: 50, BuildTime: 20f, MaxObjects: 1, Modes: 2,
                                                    UpgradeCost: 100, UpgradeDuration: 20f),
        TFObjectType.Sentrygun => new TFObjectInfo(TFObjectType.Sentrygun, Cost: 130, BuildTime: 10f, MaxObjects: 1, Modes: 1,
                                                   UpgradeCost: 200, UpgradeDuration: 10f),
        _ => null,
    };
}

/// <summary>
/// The entity class name each buildable spawns under (<c>CFCreatedFuncs</c>'
/// <c>CREATE_CLASSNAME( obj_sentrygun )</c> trio, tf_obj_sentrygun.cpp et al.)
/// and the sapper's attachment (<c>LINK_ENTITY_TO_CLASS(obj_attachment_sapper,
/// CObjectSapper)</c>, tf_obj_sapper.cpp).
/// </summary>
public static class TFObjectClassNames
{
    public const string SentryGun = "obj_sentrygun";
    public const string Dispenser = "obj_dispenser";
    public const string Teleporter = "obj_teleporter";
    public const string Sapper = "obj_attachment_sapper";

    /// <summary>Stock's class name for an object type.</summary>
    public static string Raw(TFObjectType type) => type switch
    {
        TFObjectType.Sentrygun => SentryGun,
        TFObjectType.Dispenser => Dispenser,
        TFObjectType.Teleporter => Teleporter,
        TFObjectType.AttachmentSapper => Sapper,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "no obj_* class answers that type"),
    };

    /// <summary>The three buildables and the sapper, for enumerating the world's objects.</summary>
    public static string[] AllClassNames() => [SentryGun, Dispenser, Teleporter, Sapper];
}
