//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using TF2.Shared;

namespace TF2.Server.Buildings;

/// <summary>
/// The buildables' art: the model each type wears per state — the blueprint
/// while placing, the build model over construction, the level model once
/// finished — and the collision hull each stands on. Stock's defines
/// (tf_obj_sentrygun.cpp:38-49, tf_obj_dispenser.h:75-81 and
/// tf_obj_dispenser.cpp:24-28, tf_obj_teleporter.cpp:29-35) and its
/// <c>SetBuildingSize</c> / <c>UTIL_SetSize</c> hulls. The sapper has no row:
/// stock gives it <c>OF_DOESNT_HAVE_A_MODEL</c> and no model at all.
/// </summary>
public static class TFObjectModels
{
    /// <summary>
    /// The blueprint worn at placement (<c>GetPlacementModel</c>): the
    /// teleporter's names its end (MODE_TELEPORTER_ENTRANCE 0, EXIT 1,
    /// tf_shareddefs.h:1389).
    /// </summary>
    public static string Placement(TFObjectType type, int mode) => type switch
    {
        TFObjectType.Sentrygun => "models/buildables/sentry1_blueprint.mdl",
        TFObjectType.Dispenser => "models/buildables/dispenser_blueprint.mdl",
        TFObjectType.Teleporter => mode == 0
            ? "models/buildables/teleporter_blueprint_enter.mdl"
            : "models/buildables/teleporter_blueprint_exit.mdl",
        _ => "",
    };

    /// <summary>
    /// The model construction plays over (<c>SetStartBuildingModel</c>): the
    /// sentry's is its level 1 upgrade body (tf_obj_sentrygun.cpp:360); the
    /// dispenser's builds in the level's own body
    /// (<c>GetBuildingModel</c>, tf_obj_dispenser.cpp:210); the teleporter
    /// wears the one build model at every level (tf_obj_teleporter.cpp:390).
    /// </summary>
    public static string Building(TFObjectType type) => Building(type, 1);

    /// <summary>The level <paramref name="level"/> building body (<c>GetBuildingModel</c>).</summary>
    public static string Building(TFObjectType type, int level) => type switch
    {
        TFObjectType.Sentrygun => "models/buildables/sentry1_heavy.mdl",
        TFObjectType.Dispenser => level switch
        {
            2 => "models/buildables/dispenser_lvl2.mdl",
            >= 3 => "models/buildables/dispenser_lvl3.mdl",
            _ => "models/buildables/dispenser.mdl",
        },
        TFObjectType.Teleporter => "models/buildables/teleporter.mdl",
        _ => "",
    };

    /// <summary>
    /// The model the upgrade plays over (<c>StartUpgrading</c>'s
    /// <c>SetModel</c>): the sentry wears its level's <c>_heavy</c> body
    /// (tf_obj_sentrygun.cpp:543, :549), the dispenser its level's building
    /// body (tf_obj_dispenser.cpp:326), the teleporter none — stock changes
    /// no model there (tf_obj_teleporter.cpp:1376).
    /// </summary>
    public static string? Upgrading(TFObjectType type, int level) => type switch
    {
        TFObjectType.Sentrygun => level switch
        {
            2 => "models/buildables/sentry2_heavy.mdl",
            >= 3 => "models/buildables/sentry3_heavy.mdl",
            _ => "models/buildables/sentry1_heavy.mdl",
        },
        TFObjectType.Dispenser => Building(type, level),
        _ => null,
    };

    /// <summary>The finished level 1 model (<c>OnGoActive</c>'s, or the dispenser's <c>GetFinishedModel</c>).</summary>
    public static string Finished(TFObjectType type) => Finished(type, 1);

    /// <summary>
    /// The finished model of <paramref name="level"/>: the sentry's and the
    /// dispenser's come one per level (<c>FinishUpgrading</c>'s
    /// <c>SetModel</c>, tf_obj_sentrygun.cpp:589-592,
    /// tf_obj_dispenser.cpp:348); the teleporter's is one at every level
    /// (tf_obj_teleporter.cpp:425).
    /// </summary>
    public static string Finished(TFObjectType type, int level) => type switch
    {
        TFObjectType.Sentrygun => level switch
        {
            2 => "models/buildables/sentry2.mdl",
            >= 3 => "models/buildables/sentry3.mdl",
            _ => "models/buildables/sentry1.mdl",
        },
        TFObjectType.Dispenser => level switch
        {
            2 => "models/buildables/dispenser_lvl2_light.mdl",
            >= 3 => "models/buildables/dispenser_lvl3_light.mdl",
            _ => "models/buildables/dispenser_light.mdl",
        },
        TFObjectType.Teleporter => "models/buildables/teleporter_light.mdl",
        _ => "",
    };

    /// <summary><c>SENTRY_ROCKET_MODEL</c> (tf_obj_sentrygun.cpp:46).</summary>
    public const string SentryRocketModel = "models/buildables/sentry3_rockets.mdl";

    /// <summary>
    /// The collision hull, stock's mins/maxs: the sentry's
    /// SENTRYGUN_MINS/MAXS (tf_obj_sentrygun.cpp:48-49), the dispenser's
    /// DISPENSER_MINS/MAXS (tf_obj_dispenser.cpp:24-25), the teleporter's
    /// TELEPORTER_MINS/MAXS (tf_obj_teleporter.cpp:34-35) — all standing on
    /// the origin plane.
    /// </summary>
    public static (Vector Mins, Vector Maxs) Hull(TFObjectType type) => type switch
    {
        TFObjectType.Sentrygun => (new Vector(-20f, -20f, 0f), new Vector(20f, 20f, 66f)),
        TFObjectType.Dispenser => (new Vector(-20f, -20f, 0f), new Vector(20f, 20f, 55f)),
        TFObjectType.Teleporter => (new Vector(-24f, -24f, 0f), new Vector(24f, 24f, 12f)),
        _ => (default, default),
    };
}
