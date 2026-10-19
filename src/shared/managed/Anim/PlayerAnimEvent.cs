//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared.Anim;

/// <summary>
/// The animation events a player's body can be told to do: stock's
/// <c>PlayerAnimEvent_t</c> (multiplayer_animstate.h:23-76), verbatim in
/// order and value — the ordinals go on the wire the day anim events are
/// networked, and a reordered enum is a silent mismatch.
///
/// <para>
/// <b>The TF-only values are kept, the TF-only CODE is not.</b> Phase 0.10
/// ports the arms the core has (<see
/// cref="MultiplayerAnimState.DoAnimationEvent"/>); the seven
/// <c>TF_DLL</c> code sites of the stock file stay out for Stage 3.5, and
/// they live in the reload-rate attribute hooks, not in these enum members —
/// keeping the members costs nothing and keeps the numbering honest.
/// </para>
/// </summary>
public enum PlayerAnimEvent
{
    AttackPrimary,
    AttackSecondary,
    AttackGrenade,
    Reload,
    ReloadLoop,
    ReloadEnd,
    Jump,
    Swim,
    Die,
    FlinchChest,
    FlinchHead,
    FlinchLeftArm,
    FlinchRightArm,
    FlinchLeftLeg,
    FlinchRightLeg,
    DoubleJump,

    /// <summary>Cancel the current gesture.</summary>
    Cancel,

    Spawn,

    /// <summary>Snap to the current yaw exactly.</summary>
    SnapYaw,

    /// <summary>Play a specific activity (the stock int carried the activity index).</summary>
    Custom,

    CustomGesture,

    /// <summary>Play a specific sequence.</summary>
    CustomSequence,

    CustomGestureSequence,

    // TF specific. Here until there's a derived game solution to this.
    AttackPre,
    AttackPost,
    Grenade1Draw,
    Grenade2Draw,
    Grenade1Throw,
    Grenade2Throw,
    VoiceCommandGesture,
    DoubleJumpCrouch,
    StunBegin,
    StunMiddle,
    StunEnd,
    PasstimeThrowBegin,
    PasstimeThrowMiddle,
    PasstimeThrowEnd,
    CyoaPdaBegin,
    CyoaPdaMiddle,
    CyoaPdaEnd,

    AttackPrimarySuper,

    Count,
}

/// <summary>
/// Stock's <c>IsCustomPlayerAnimEvent</c> (multiplayer_animstate.h:103):
/// the four events whose payload is the caller's, not the event's.
/// </summary>
public static class PlayerAnimEvents
{
    public static bool IsCustom(PlayerAnimEvent animEvent) =>
        animEvent is PlayerAnimEvent.Custom or PlayerAnimEvent.CustomGesture
                       or PlayerAnimEvent.CustomSequence or PlayerAnimEvent.CustomGestureSequence;
}

/// <summary>
/// The gesture slots: stock's anonymous enum (multiplayer_animstate.h:79-92)
/// and <c>GESTURE_SLOT_INVALID</c>. The slot's NAME is its meaning — one
/// gesture at a time per named slot, which is what makes firing twice in a
/// row an interruption of the SAME gesture rather than a pile of them.
/// </summary>
public static class GestureSlot
{
    public const int AttackAndReload = 0;
    public const int Grenade = 1;
    public const int Jump = 2;
    public const int Swim = 3;
    public const int Flinch = 4;
    public const int Vcd = 5;
    public const int Custom = 6;

    public const int Count = 7;

    /// <summary>Stock's <c>GESTURE_SLOT_INVALID</c>: a slot nothing owns.</summary>
    public const int Invalid = -1;
}
