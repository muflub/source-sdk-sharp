//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;

namespace SourceSharp.Abi;

/// <summary>
/// THE SHARED ANIMATION-EVENT TABLE: the 50 event names the game registers, in
/// the order that IS the <c>Animevent</c> index, each with the native
/// <c>AE_TYPE_*</c> mask stock registers it under.
///
/// <para>
/// This replaces <c>EventList_RegisterSharedEvents()</c> in
/// <c>game/shared/eventlist.cpp</c>, which has left both <c>_csharp.vpc</c>
/// file lists (plan_shared.md Phase 1). As with <see cref="ActivityCatalog"/>,
/// the registry that answers <c>EventList_IndexForName</c> stayed native, in
/// <c>sourcesharp/game/shared/shell_animlists.cpp</c>, and is filled from here
/// in one crossing per level.
/// </para>
///
/// <para>
/// <b>IT IS IN Abi RATHER THAN Shared, AND SO IS THE SIDE TABLE NOW.</b>
/// <c>SourceSharp.Bootstrap</c> — which is where the ABI slots are filled —
/// references <c>SourceSharp.Abi</c> and nothing else, so a table it has to
/// serialise cannot live in <c>Shared</c>. What that would otherwise have
/// bought is a SECOND copy of the same fifty facts: <c>Shared</c>'s
/// <c>AnimationEventCatalog</c> already knew which side each event is for, and
/// two tables of fifty flags kept in step by a test is exactly the shape this
/// port keeps having to unpick. So this is the one table, and
/// <c>AnimationEventCatalog</c> is now a VIEW of it.
/// </para>
///
/// <para>
/// The bit positions here are native's, not the managed enum's, and the
/// difference is real rather than an accident of transcription:
/// <c>AE_TYPE_CLIENT</c> is <c>1 &lt;&lt; 4</c> in <c>eventlist.h</c> with four
/// unrelated flags below it, while <c>AnimationEventSide</c> is numbered for
/// reading. This file speaks native's dialect because it is the one that
/// crosses; the translation happens once, where the view is built.
/// </para>
///
/// <para>
/// <c>EventCatalogTests</c> reads <c>eventlist.h</c> and <c>eventlist.cpp</c>
/// and asserts the names, the order and every mask against them, in both
/// directions.
/// </para>
/// </summary>
public static class EventCatalog
{
    // eventlist.h. The four flags stock defines and never registers anything
    // under — SCRIPTED, SHARED, FACEPOSER, NEWEVENTSYSTEM — are not here,
    // because a constant no table uses is a constant nothing keeps honest.
    private const int TypeServer = 1 << 0;
    private const int TypeWeapon = 1 << 3;
    private const int TypeClient = 1 << 4;

    /// <summary>
    /// The registered event names, index-ordered: <c>Ordered[i]</c> is the name
    /// of <c>Animevent</c> <c>i</c>. <c>AE_EMPTY</c> is 0, as the enum says.
    /// </summary>
    public static readonly ImmutableArray<string> Ordered =
    [
        "AE_EMPTY",
        "AE_NPC_LEFTFOOT",
        "AE_NPC_RIGHTFOOT",
        "AE_NPC_BODYDROP_LIGHT",
        "AE_NPC_BODYDROP_HEAVY",
        "AE_NPC_SWISHSOUND",
        "AE_NPC_180TURN",
        "AE_NPC_ITEM_PICKUP",
        "AE_NPC_WEAPON_DROP",
        "AE_NPC_WEAPON_SET_SEQUENCE_NAME",
        "AE_NPC_WEAPON_SET_SEQUENCE_NUMBER",
        "AE_NPC_WEAPON_SET_ACTIVITY",
        "AE_NPC_HOLSTER",
        "AE_NPC_DRAW",
        "AE_NPC_WEAPON_FIRE",
        "AE_CL_PLAYSOUND",
        "AE_SV_PLAYSOUND",
        "AE_CL_STOPSOUND",
        "AE_START_SCRIPTED_EFFECT",
        "AE_STOP_SCRIPTED_EFFECT",
        "AE_CLIENT_EFFECT_ATTACH",
        "AE_MUZZLEFLASH",
        "AE_NPC_MUZZLEFLASH",
        "AE_THUMPER_THUMP",
        "AE_AMMOCRATE_PICKUP_AMMO",
        "AE_NPC_RAGDOLL",
        "AE_NPC_ADDGESTURE",
        "AE_NPC_RESTARTGESTURE",
        "AE_NPC_ATTACK_BROADCAST",
        "AE_NPC_HURT_INTERACTION_PARTNER",
        "AE_NPC_SET_INTERACTION_CANTDIE",
        "AE_SV_DUSTTRAIL",
        "AE_CL_CREATE_PARTICLE_EFFECT",
        "AE_RAGDOLL",
        "AE_CL_ENABLE_BODYGROUP",
        "AE_CL_DISABLE_BODYGROUP",
        "AE_CL_BODYGROUP_SET_VALUE",
        "AE_CL_BODYGROUP_SET_VALUE_CMODEL_WPN",
        "AE_WPN_PRIMARYATTACK",
        "AE_WPN_INCREMENTAMMO",
        "AE_WPN_HIDE",
        "AE_WPN_UNHIDE",
        "AE_WPN_PLAYWPNSOUND",
        "AE_RD_ROBOT_POP_PANELS_OFF",
        "AE_TAUNT_ENABLE_MOVE",
        "AE_TAUNT_DISABLE_MOVE",
        "AE_CL_REMOVE_PARTICLE_EFFECT",
        "AE_TAUNT_ADD_ATTRIBUTE",
        "AE_SV_EXCLUDE_PLAYER_SOUND",
        "AE_CL_EXCLUDE_PLAYER_SOUND",
    ];

    /// <summary>
    /// The <c>iType</c> stock registers each event under, in the same order:
    /// <c>NativeTypes[i]</c> belongs to <c>Ordered[i]</c>. This is what
    /// <c>EventList_GetEventType</c> answers.
    /// </summary>
    public static readonly ImmutableArray<int> NativeTypes =
    [
        TypeServer,                                // AE_EMPTY
        TypeServer,                                // AE_NPC_LEFTFOOT
        TypeServer,                                // AE_NPC_RIGHTFOOT
        TypeServer,                                // AE_NPC_BODYDROP_LIGHT
        TypeServer,                                // AE_NPC_BODYDROP_HEAVY
        TypeServer,                                // AE_NPC_SWISHSOUND
        TypeServer,                                // AE_NPC_180TURN
        TypeServer,                                // AE_NPC_ITEM_PICKUP
        TypeServer,                                // AE_NPC_WEAPON_DROP
        TypeServer,                                // AE_NPC_WEAPON_SET_SEQUENCE_NAME
        TypeServer,                                // AE_NPC_WEAPON_SET_SEQUENCE_NUMBER
        TypeServer,                                // AE_NPC_WEAPON_SET_ACTIVITY
        TypeServer,                                // AE_NPC_HOLSTER
        TypeServer,                                // AE_NPC_DRAW
        TypeServer | TypeWeapon,                   // AE_NPC_WEAPON_FIRE
        TypeClient,                                // AE_CL_PLAYSOUND
        TypeServer,                                // AE_SV_PLAYSOUND
        TypeClient,                                // AE_CL_STOPSOUND
        TypeServer,                                // AE_START_SCRIPTED_EFFECT
        TypeServer,                                // AE_STOP_SCRIPTED_EFFECT
        TypeClient,                                // AE_CLIENT_EFFECT_ATTACH
        TypeClient,                                // AE_MUZZLEFLASH
        TypeClient,                                // AE_NPC_MUZZLEFLASH
        TypeServer,                                // AE_THUMPER_THUMP
        TypeServer,                                // AE_AMMOCRATE_PICKUP_AMMO
        TypeServer,                                // AE_NPC_RAGDOLL
        TypeServer,                                // AE_NPC_ADDGESTURE
        TypeServer,                                // AE_NPC_RESTARTGESTURE
        TypeServer,                                // AE_NPC_ATTACK_BROADCAST
        TypeServer,                                // AE_NPC_HURT_INTERACTION_PARTNER
        TypeServer,                                // AE_NPC_SET_INTERACTION_CANTDIE
        TypeServer,                                // AE_SV_DUSTTRAIL
        TypeClient,                                // AE_CL_CREATE_PARTICLE_EFFECT
        TypeServer,                                // AE_RAGDOLL
        TypeClient,                                // AE_CL_ENABLE_BODYGROUP
        TypeClient,                                // AE_CL_DISABLE_BODYGROUP
        TypeClient,                                // AE_CL_BODYGROUP_SET_VALUE
        TypeClient,                                // AE_CL_BODYGROUP_SET_VALUE_CMODEL_WPN
        TypeClient | TypeServer,                   // AE_WPN_PRIMARYATTACK
        TypeClient | TypeServer,                   // AE_WPN_INCREMENTAMMO
        TypeClient | TypeServer,                   // AE_WPN_HIDE
        TypeClient | TypeServer,                   // AE_WPN_UNHIDE
        TypeClient | TypeServer,                   // AE_WPN_PLAYWPNSOUND
        TypeClient | TypeServer,                   // AE_RD_ROBOT_POP_PANELS_OFF
        TypeClient | TypeServer,                   // AE_TAUNT_ENABLE_MOVE
        TypeClient | TypeServer,                   // AE_TAUNT_DISABLE_MOVE
        TypeClient,                                // AE_CL_REMOVE_PARTICLE_EFFECT
        TypeServer,                                // AE_TAUNT_ADD_ATTRIBUTE
        TypeServer,                                // AE_SV_EXCLUDE_PLAYER_SOUND
        TypeClient,                                // AE_CL_EXCLUDE_PLAYER_SOUND
    ];

    /// <summary>How many shared animation events there are.</summary>
    public static int Count => Ordered.Length;

    /// <summary>
    /// The index of a named shared event, or -1 — <c>AE_INVALID</c>, which is
    /// what stock's <c>EventList_IndexForName</c> answers for a name it does
    /// not hold.
    /// </summary>
    public static int IndexForName(string name)
    {
        for (int i = 0; i < Ordered.Length; i++)
        {
            // Linear over fifty rows, and CASE-INSENSITIVE because stock's
            // lookup is: CStringRegistry is a CUtlDict whose default compare
            // type is k_eDictCompareTypeCaseInsensitive (utldict.h:51), so a
            // model naming "ae_muzzleflash" resolves there and must here.
            if (string.Equals(Ordered[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The native <c>AE_TYPE_*</c> mask of a named event, or 0 for a name the
    /// table does not hold.
    ///
    /// <para>
    /// Zero rather than -1, and the distinction is stock's: -1 is what
    /// <c>EventList_GetEventType</c> answers for an INDEX it has never seen,
    /// while a private event registered from a model carries a real mask of
    /// <c>AE_TYPE_SERVER</c>. Nothing registered here is private, so a name
    /// this table does not hold has no type rather than an invalid one.
    /// </para>
    /// </summary>
    public static int NativeTypeMask(string name)
    {
        int i = IndexForName(name);
        return i >= 0 ? NativeTypes[i] : 0;
    }
}
