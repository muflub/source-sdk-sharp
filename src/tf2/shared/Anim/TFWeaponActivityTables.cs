//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Shared.Anim;

namespace TF2.Shared.Anim;

/// <summary>
/// The <c>s_acttable*</c> lists of <c>tf_weaponbase.cpp:3660-4181</c> — the
/// twelve role tables that <c>CTFWeaponBase::ActivityList</c> hands out
/// (:4208-4280) by the role <c>GetActivityWeaponRole</c> reads
/// (<see cref="TFWeaponRole"/>), and that
/// <c>CTFPlayerAnimState::TranslateActivity</c> applies to the claimed
/// activity through the weapon's <c>ActivityOverride</c>
/// (<c>tf_playeranimstate.cpp:130-140</c>): a first-match search of the
/// <c>baseAct → weaponAct</c> rows, the claim standing when no row names it.
///
/// <para>
/// One row per stock line, in the stock order, each table its own switch
/// (an expression's duplicate arm would not compile, so stock's own
/// dead duplicate — the second AIRWALK row of <c>s_acttableItem2</c> at
/// :3982, never reachable behind its first-match twin at :3975 — is
/// carried once). The roles with no table of their own
/// (<c>GRENADE</c>, <c>HEAD</c>, <c>MISC</c>, <c>PASSTIME_BALL</c>) take the
/// switch's default arm: the Primary table (:4228-4231).
/// </para>
///
/// <para>
/// <b>Only these twelve tables exist in this build.</b> The per-weapon
/// act-tables are this build's weapon classes' <c>ActivityList</c>
/// overrides, not <c>CTFWeaponBase</c>'s: the passtime gun
/// (<c>tf_weapon_passtime_gun.cpp:251,304</c>) and the grappling hook
/// (<c>tf_weapon_grapplinghook.cpp:150</c>) are deferred with their
/// weapons, as is the econ activity override the layer's claim carries
/// and the cheat convar <c>mp_forceactivityset</c> (:4199-4202). The
/// animation-slot arm of the role itself has landed
/// (<c>TFWeaponTypes.ActivityWeaponRole</c>, :4189-4197). The disguised
/// spy's table swap (the <c>ActivityList</c> client arm, :4212-4222) stands
/// as the layer's disguise arm: the table asked for here is the disguise
/// weapon's when stock's gate says so.
/// </para>
/// </summary>
public static class TFWeaponActivityTables
{
    /// <summary>
    /// The table's answer for the claim: the first row naming it, or the
    /// claim itself. Stock's search (<c>ActivityOverride</c>, the loop
    /// <c>tf_playeranimstate.cpp:255-266</c> runs over this table) takes the
    /// first match, and the tables carry no duplicate live keys, so a
    /// switch's first arm is the same order.
    /// </summary>
    public static string Translate(TFWeaponRole role, string act) => role switch
    {
        TFWeaponRole.Primary or TFWeaponRole.Grenade or TFWeaponRole.Head
            or TFWeaponRole.Misc or TFWeaponRole.PasstimeBall => Primary(act),
        TFWeaponRole.Secondary => Secondary(act),
        TFWeaponRole.Primary2 => Primary2(act),
        TFWeaponRole.Secondary2 => Secondary2(act),
        TFWeaponRole.Melee => Melee(act),
        TFWeaponRole.MeleeAllclass => MeleeAllclass(act),
        TFWeaponRole.Building => Building(act),
        TFWeaponRole.Pda => Pda(act),
        TFWeaponRole.Item1 => Item1(act),
        TFWeaponRole.Item2 => Item2(act),
        TFWeaponRole.Item3 => Item3(act),
        TFWeaponRole.Item4 => Item4(act),
        _ => act,
    };

    /// <summary><c>s_acttablePrimary</c> (:3660-3721).</summary>
    private static string Primary(string act) => act switch
    {
        MpActivities.StandIdle => TFAnimActivities.StandPrimary,           // :3662
        MpActivities.CrouchIdle => "ACT_MP_CROUCH_PRIMARY",                // :3663
        TFAnimActivities.Deployed => "ACT_MP_DEPLOYED_PRIMARY",            // :3664
        TFAnimActivities.CrouchDeployed => "ACT_MP_CROUCHWALK_DEPLOYED",   // :3665 — stock's own quirk: the walk-deployed name
        TFAnimActivities.CrouchDeployedIdle => "ACT_MP_CROUCH_DEPLOYED_IDLE", // :3666
        MpActivities.Run => "ACT_MP_RUN_PRIMARY",                          // :3667
        "ACT_MP_WALK" => "ACT_MP_WALK_PRIMARY",                            // :3668
        TFAnimActivities.Airwalk => "ACT_MP_AIRWALK_PRIMARY",              // :3669
        MpActivities.CrouchWalk => "ACT_MP_CROUCHWALK_PRIMARY",            // :3670
        MpActivities.Jump => "ACT_MP_JUMP_PRIMARY",                        // :3671
        TFAnimActivities.JumpStart => "ACT_MP_JUMP_START_PRIMARY",         // :3672
        TFAnimActivities.JumpFloat => "ACT_MP_JUMP_FLOAT_PRIMARY",         // :3673
        TFAnimActivities.JumpLand => "ACT_MP_JUMP_LAND_PRIMARY",           // :3674
        MpActivities.Swim => "ACT_MP_SWIM_PRIMARY",                        // :3675
        TFAnimActivities.SwimDeployed => "ACT_MP_SWIM_DEPLOYED_PRIMARY",   // :3676
        TFAnimActivities.DoubleJumpCrouch => "ACT_MP_DOUBLEJUMP_CROUCH_PRIMARY", // :3677

        MpActivities.AttackStandPrimaryFire => "ACT_MP_ATTACK_STAND_PRIMARY",                                 // :3679
        "ACT_MP_ATTACK_STAND_PRIMARYFIRE_DEPLOYED" => "ACT_MP_ATTACK_STAND_PRIMARY_DEPLOYED",               // :3680
        "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE" => "ACT_MP_ATTACK_CROUCH_PRIMARY",                               // :3681
        "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE_DEPLOYED" => "ACT_MP_ATTACK_CROUCH_PRIMARY_DEPLOYED",              // :3682
        MpActivities.AttackSwimPrimaryFire => "ACT_MP_ATTACK_SWIM_PRIMARY",                                 // :3683
        "ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE" => "ACT_MP_ATTACK_AIRWALK_PRIMARY",                             // :3684

        MpActivities.ReloadStand => "ACT_MP_RELOAD_STAND_PRIMARY",                                          // :3686
        MpActivities.ReloadStandLoop => "ACT_MP_RELOAD_STAND_PRIMARY_LOOP",                               // :3687
        MpActivities.ReloadStandEnd => "ACT_MP_RELOAD_STAND_PRIMARY_END",                                 // :3688
        MpActivities.ReloadCrouch => "ACT_MP_RELOAD_CROUCH_PRIMARY",                                       // :3689
        MpActivities.ReloadCrouchLoop => "ACT_MP_RELOAD_CROUCH_PRIMARY_LOOP",                             // :3690
        MpActivities.ReloadCrouchEnd => "ACT_MP_RELOAD_CROUCH_PRIMARY_END",                               // :3691
        MpActivities.ReloadSwim => "ACT_MP_RELOAD_SWIM_PRIMARY",                                           // :3692
        MpActivities.ReloadSwimLoop => "ACT_MP_RELOAD_SWIM_PRIMARY_LOOP",                                  // :3693
        MpActivities.ReloadSwimEnd => "ACT_MP_RELOAD_SWIM_PRIMARY_END",                                    // :3694
        "ACT_MP_RELOAD_AIRWALK" => "ACT_MP_RELOAD_AIRWALK_PRIMARY",                                       // :3695
        "ACT_MP_RELOAD_AIRWALK_LOOP" => "ACT_MP_RELOAD_AIRWALK_PRIMARY_LOOP",                             // :3696
        "ACT_MP_RELOAD_AIRWALK_END" => "ACT_MP_RELOAD_AIRWALK_PRIMARY_END",                               // :3697

        "ACT_MP_GESTURE_FLINCH" => "ACT_MP_GESTURE_FLINCH_PRIMARY",                                       // :3699

        "ACT_MP_GRENADE1_DRAW" => "ACT_MP_PRIMARY_GRENADE1_DRAW",                                          // :3701
        "ACT_MP_GRENADE1_IDLE" => "ACT_MP_PRIMARY_GRENADE1_IDLE",                                          // :3702
        "ACT_MP_GRENADE1_ATTACK" => "ACT_MP_PRIMARY_GRENADE1_ATTACK",                                      // :3703
        "ACT_MP_GRENADE2_DRAW" => "ACT_MP_PRIMARY_GRENADE2_DRAW",                                          // :3704
        "ACT_MP_GRENADE2_IDLE" => "ACT_MP_PRIMARY_GRENADE2_IDLE",                                          // :3705
        "ACT_MP_GRENADE2_ATTACK" => "ACT_MP_PRIMARY_GRENADE2_ATTACK",                                      // :3706

        MpActivities.AttackStandGrenade => "ACT_MP_ATTACK_STAND_GRENADE",                                  // :3708
        "ACT_MP_ATTACK_CROUCH_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE",                                   // :3709
        "ACT_MP_ATTACK_SWIM_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE",                                     // :3710
        "ACT_MP_ATTACK_AIRWALK_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE",                                  // :3711

        "ACT_MP_GESTURE_VC_HANDMOUTH" => "ACT_MP_GESTURE_VC_HANDMOUTH_PRIMARY",                            // :3713
        "ACT_MP_GESTURE_VC_FINGERPOINT" => "ACT_MP_GESTURE_VC_FINGERPOINT_PRIMARY",                        // :3714
        "ACT_MP_GESTURE_VC_FISTPUMP" => "ACT_MP_GESTURE_VC_FISTPUMP_PRIMARY",                              // :3715
        "ACT_MP_GESTURE_VC_THUMBSUP" => "ACT_MP_GESTURE_VC_THUMBSUP_PRIMARY",                              // :3716
        "ACT_MP_GESTURE_VC_NODYES" => "ACT_MP_GESTURE_VC_NODYES_PRIMARY",                                  // :3717
        "ACT_MP_GESTURE_VC_NODNO" => "ACT_MP_GESTURE_VC_NODNO_PRIMARY",                                    // :3718

        "ACT_MP_FALLING_STOMP" => "ACT_MP_FALLING_STOMP_PRIMARY",                                          // :3720
        _ => act,
    };

    /// <summary><c>s_acttableSecondary</c> (:3723-3776).</summary>
    private static string Secondary(string act) => act switch
    {
        MpActivities.StandIdle => TFAnimActivities.StandSecondary,         // :3725
        MpActivities.CrouchIdle => "ACT_MP_CROUCH_SECONDARY",              // :3726
        MpActivities.Run => "ACT_MP_RUN_SECONDARY",                        // :3727
        "ACT_MP_WALK" => "ACT_MP_WALK_SECONDARY",                          // :3728
        TFAnimActivities.Airwalk => "ACT_MP_AIRWALK_SECONDARY",            // :3729
        MpActivities.CrouchWalk => "ACT_MP_CROUCHWALK_SECONDARY",          // :3730
        MpActivities.Jump => "ACT_MP_JUMP_SECONDARY",                      // :3731
        TFAnimActivities.JumpStart => "ACT_MP_JUMP_START_SECONDARY",       // :3732
        TFAnimActivities.JumpFloat => "ACT_MP_JUMP_FLOAT_SECONDARY",       // :3733
        TFAnimActivities.JumpLand => "ACT_MP_JUMP_LAND_SECONDARY",         // :3734
        MpActivities.Swim => "ACT_MP_SWIM_SECONDARY",                      // :3735
        TFAnimActivities.DoubleJumpCrouch => "ACT_MP_DOUBLEJUMP_CROUCH_SECONDARY", // :3736

        MpActivities.AttackStandPrimaryFire => "ACT_MP_ATTACK_STAND_SECONDARY",                           // :3738
        "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE" => "ACT_MP_ATTACK_CROUCH_SECONDARY",                           // :3739
        MpActivities.AttackSwimPrimaryFire => "ACT_MP_ATTACK_SWIM_SECONDARY",                             // :3740
        "ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE" => "ACT_MP_ATTACK_AIRWALK_SECONDARY",                         // :3741

        MpActivities.ReloadStand => "ACT_MP_RELOAD_STAND_SECONDARY",                                      // :3743
        MpActivities.ReloadStandLoop => "ACT_MP_RELOAD_STAND_SECONDARY_LOOP",                             // :3744
        MpActivities.ReloadStandEnd => "ACT_MP_RELOAD_STAND_SECONDARY_END",                               // :3745
        MpActivities.ReloadCrouch => "ACT_MP_RELOAD_CROUCH_SECONDARY",                                    // :3746
        MpActivities.ReloadCrouchLoop => "ACT_MP_RELOAD_CROUCH_SECONDARY_LOOP",                           // :3747
        MpActivities.ReloadCrouchEnd => "ACT_MP_RELOAD_CROUCH_SECONDARY_END",                             // :3748
        MpActivities.ReloadSwim => "ACT_MP_RELOAD_SWIM_SECONDARY",                                        // :3749
        MpActivities.ReloadSwimLoop => "ACT_MP_RELOAD_SWIM_SECONDARY_LOOP",                               // :3750
        MpActivities.ReloadSwimEnd => "ACT_MP_RELOAD_SWIM_SECONDARY_END",                                 // :3751
        "ACT_MP_RELOAD_AIRWALK" => "ACT_MP_RELOAD_AIRWALK_SECONDARY",                                     // :3752
        "ACT_MP_RELOAD_AIRWALK_LOOP" => "ACT_MP_RELOAD_AIRWALK_SECONDARY_LOOP",                           // :3753
        "ACT_MP_RELOAD_AIRWALK_END" => "ACT_MP_RELOAD_AIRWALK_SECONDARY_END",                             // :3754

        "ACT_MP_GESTURE_FLINCH" => "ACT_MP_GESTURE_FLINCH_SECONDARY",                                     // :3756

        "ACT_MP_GRENADE1_DRAW" => "ACT_MP_SECONDARY_GRENADE1_DRAW",                                        // :3758
        "ACT_MP_GRENADE1_IDLE" => "ACT_MP_SECONDARY_GRENADE1_IDLE",                                        // :3759
        "ACT_MP_GRENADE1_ATTACK" => "ACT_MP_SECONDARY_GRENADE1_ATTACK",                                    // :3760
        "ACT_MP_GRENADE2_DRAW" => "ACT_MP_SECONDARY_GRENADE2_DRAW",                                        // :3761
        "ACT_MP_GRENADE2_IDLE" => "ACT_MP_SECONDARY_GRENADE2_IDLE",                                        // :3762
        "ACT_MP_GRENADE2_ATTACK" => "ACT_MP_SECONDARY_GRENADE2_ATTACK",                                    // :3763

        MpActivities.AttackStandGrenade => "ACT_MP_ATTACK_STAND_GRENADE",                                  // :3765
        "ACT_MP_ATTACK_CROUCH_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE",                                   // :3766
        "ACT_MP_ATTACK_SWIM_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE",                                     // :3767
        "ACT_MP_ATTACK_AIRWALK_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE",                                  // :3768

        "ACT_MP_GESTURE_VC_HANDMOUTH" => "ACT_MP_GESTURE_VC_HANDMOUTH_SECONDARY",                          // :3770
        "ACT_MP_GESTURE_VC_FINGERPOINT" => "ACT_MP_GESTURE_VC_FINGERPOINT_SECONDARY",                      // :3771
        "ACT_MP_GESTURE_VC_FISTPUMP" => "ACT_MP_GESTURE_VC_FISTPUMP_SECONDARY",                            // :3772
        "ACT_MP_GESTURE_VC_THUMBSUP" => "ACT_MP_GESTURE_VC_THUMBSUP_SECONDARY",                            // :3773
        "ACT_MP_GESTURE_VC_NODYES" => "ACT_MP_GESTURE_VC_NODYES_SECONDARY",                                // :3774
        "ACT_MP_GESTURE_VC_NODNO" => "ACT_MP_GESTURE_VC_NODNO_SECONDARY",                                  // :3775
        _ => act,
    };

    /// <summary>
    /// <c>s_acttablePrimary2</c> (:3778-3829). Stock's own quirks kept as
    /// written: the <c>SWIM_PRIMARY_SUPER</c> row (:3799, unlike its stand
    /// and crouch twins, swaps to the airwalk-super name), the primary
    /// <c>AIRWALK_PRIMARYFIRE</c> row answering plain
    /// <c>AIRWALK_PRIMARY</c> (:3803), the <c>RELOAD_SWIM</c> loop and end
    /// rows answering the non-alt names (:3812-3813), and the
    /// <c>AIRWALK_PRIMARY_ALT</c> reload trio mixing its loop from the
    /// plain table (:3814-3816).
    /// </summary>
    private static string Primary2(string act) => act switch
    {
        MpActivities.StandIdle => TFAnimActivities.StandPrimary,           // :3780
        MpActivities.CrouchIdle => "ACT_MP_CROUCH_PRIMARY",                // :3781
        TFAnimActivities.Deployed => "ACT_MP_DEPLOYED_PRIMARY",            // :3782
        TFAnimActivities.CrouchDeployed => "ACT_MP_CROUCHWALK_DEPLOYED",   // :3783
        TFAnimActivities.CrouchDeployedIdle => "ACT_MP_CROUCH_DEPLOYED_IDLE", // :3784
        MpActivities.Run => "ACT_MP_RUN_PRIMARY",                          // :3785
        "ACT_MP_WALK" => "ACT_MP_WALK_PRIMARY",                            // :3786
        TFAnimActivities.Airwalk => "ACT_MP_AIRWALK_PRIMARY",              // :3787
        MpActivities.CrouchWalk => "ACT_MP_CROUCHWALK_PRIMARY",            // :3788
        MpActivities.Jump => "ACT_MP_JUMP_PRIMARY",                        // :3789
        TFAnimActivities.JumpStart => "ACT_MP_JUMP_START_PRIMARY",         // :3790
        TFAnimActivities.JumpFloat => "ACT_MP_JUMP_FLOAT_PRIMARY",         // :3791
        TFAnimActivities.JumpLand => "ACT_MP_JUMP_LAND_PRIMARY",           // :3792
        MpActivities.Swim => "ACT_MP_SWIM_PRIMARY",                        // :3793
        TFAnimActivities.SwimDeployed => "ACT_MP_SWIM_DEPLOYED_PRIMARY",   // :3794
        TFAnimActivities.DoubleJumpCrouch => "ACT_MP_DOUBLEJUMP_CROUCH_PRIMARY", // :3795

        "ACT_MP_ATTACK_STAND_PRIMARY_SUPER" => "ACT_MP_ATTACK_STAND_PRIMARY_SUPER",                       // :3797
        "ACT_MP_ATTACK_CROUCH_PRIMARY_SUPER" => "ACT_MP_ATTACK_CROUCH_PRIMARY_SUPER",                     // :3798
        "ACT_MP_ATTACK_SWIM_PRIMARY_SUPER" => "ACT_MP_ATTACK_SWIM_PRIMARY_SUPER",                         // :3799
        MpActivities.AttackStandPrimaryFire => "ACT_MP_ATTACK_STAND_PRIMARY_ALT",                         // :3800
        "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE" => "ACT_MP_ATTACK_CROUCH_PRIMARY_ALT",                         // :3801
        MpActivities.AttackSwimPrimaryFire => "ACT_MP_ATTACK_SWIM_PRIMARY_ALT",                           // :3802
        "ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE" => "ACT_MP_ATTACK_AIRWALK_PRIMARY",                           // :3803

        MpActivities.ReloadStand => "ACT_MP_RELOAD_STAND_PRIMARY_ALT",                                    // :3805
        MpActivities.ReloadStandLoop => "ACT_MP_RELOAD_STAND_PRIMARY_LOOP_ALT",                           // :3806
        MpActivities.ReloadStandEnd => "ACT_MP_RELOAD_STAND_PRIMARY_END_ALT",                             // :3807
        MpActivities.ReloadCrouch => "ACT_MP_RELOAD_CROUCH_PRIMARY_ALT",                                  // :3808
        MpActivities.ReloadCrouchLoop => "ACT_MP_RELOAD_CROUCH_PRIMARY_LOOP_ALT",                         // :3809
        MpActivities.ReloadCrouchEnd => "ACT_MP_RELOAD_CROUCH_PRIMARY_END_ALT",                           // :3810
        MpActivities.ReloadSwim => "ACT_MP_RELOAD_SWIM_PRIMARY_ALT",                                      // :3811
        MpActivities.ReloadSwimLoop => "ACT_MP_RELOAD_SWIM_PRIMARY_LOOP",                                 // :3812
        MpActivities.ReloadSwimEnd => "ACT_MP_RELOAD_SWIM_PRIMARY_END",                                   // :3813
        "ACT_MP_RELOAD_AIRWALK" => "ACT_MP_RELOAD_AIRWALK_PRIMARY_ALT",                                   // :3814
        "ACT_MP_RELOAD_AIRWALK_LOOP" => "ACT_MP_RELOAD_AIRWALK_PRIMARY_LOOP_ALT",                         // :3815
        "ACT_MP_RELOAD_AIRWALK_END" => "ACT_MP_RELOAD_AIRWALK_PRIMARY_END_ALT",                           // :3816

        MpActivities.AttackStandGrenade => "ACT_MP_ATTACK_STAND_GRENADE",                                  // :3818
        "ACT_MP_ATTACK_CROUCH_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE",                                   // :3819
        "ACT_MP_ATTACK_SWIM_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE",                                     // :3820
        "ACT_MP_ATTACK_AIRWALK_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE",                                  // :3821

        "ACT_MP_GESTURE_VC_HANDMOUTH" => "ACT_MP_GESTURE_VC_HANDMOUTH_PRIMARY",                            // :3823
        "ACT_MP_GESTURE_VC_FINGERPOINT" => "ACT_MP_GESTURE_VC_FINGERPOINT_PRIMARY",                        // :3824
        "ACT_MP_GESTURE_VC_FISTPUMP" => "ACT_MP_GESTURE_VC_FISTPUMP_PRIMARY",                              // :3825
        "ACT_MP_GESTURE_VC_THUMBSUP" => "ACT_MP_GESTURE_VC_THUMBSUP_PRIMARY",                              // :3826
        "ACT_MP_GESTURE_VC_NODYES" => "ACT_MP_GESTURE_VC_NODYES_PRIMARY",                                  // :3827
        "ACT_MP_GESTURE_VC_NODNO" => "ACT_MP_GESTURE_VC_NODNO_PRIMARY",                                    // :3828
        _ => act,
    };

    /// <summary>
    /// <c>s_acttableSecondary2</c> (:3831-3874). Stock's quirk: the voice
    /// gesture rows at :3868-3873 answer the plain
    /// <c>VC_*_SECONDARY</c> names, not the secondary2 family.
    /// </summary>
    private static string Secondary2(string act) => act switch
    {
        MpActivities.StandIdle => "ACT_MP_STAND_SECONDARY2",               // :3833
        MpActivities.CrouchIdle => "ACT_MP_CROUCH_SECONDARY2",             // :3834
        MpActivities.Run => "ACT_MP_RUN_SECONDARY2",                       // :3835
        "ACT_MP_WALK" => "ACT_MP_WALK_SECONDARY2",                         // :3836
        TFAnimActivities.Airwalk => "ACT_MP_AIRWALK_SECONDARY2",           // :3837
        MpActivities.CrouchWalk => "ACT_MP_CROUCHWALK_SECONDARY2",         // :3838
        MpActivities.Jump => "ACT_MP_JUMP_SECONDARY2",                     // :3839
        TFAnimActivities.JumpStart => "ACT_MP_JUMP_START_SECONDARY2",      // :3840
        TFAnimActivities.JumpFloat => "ACT_MP_JUMP_FLOAT_SECONDARY2",      // :3841
        TFAnimActivities.JumpLand => "ACT_MP_JUMP_LAND_SECONDARY2",        // :3842
        MpActivities.Swim => "ACT_MP_SWIM_SECONDARY2",                     // :3843

        MpActivities.AttackStandPrimaryFire => "ACT_MP_ATTACK_STAND_SECONDARY2",                          // :3845
        "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE" => "ACT_MP_ATTACK_CROUCH_SECONDARY2",                          // :3846
        MpActivities.AttackSwimPrimaryFire => "ACT_MP_ATTACK_SWIM_SECONDARY2",                            // :3847
        "ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE" => "ACT_MP_ATTACK_AIRWALK_SECONDARY2",                        // :3848

        MpActivities.ReloadStand => "ACT_MP_RELOAD_STAND_SECONDARY2",                                     // :3850
        MpActivities.ReloadStandLoop => "ACT_MP_RELOAD_STAND_SECONDARY2_LOOP",                            // :3851
        MpActivities.ReloadStandEnd => "ACT_MP_RELOAD_STAND_SECONDARY2_END",                              // :3852
        MpActivities.ReloadCrouch => "ACT_MP_RELOAD_CROUCH_SECONDARY2",                                   // :3853
        MpActivities.ReloadCrouchLoop => "ACT_MP_RELOAD_CROUCH_SECONDARY2_LOOP",                          // :3854
        MpActivities.ReloadCrouchEnd => "ACT_MP_RELOAD_CROUCH_SECONDARY2_END",                            // :3855
        MpActivities.ReloadSwim => "ACT_MP_RELOAD_SWIM_SECONDARY2",                                       // :3856
        MpActivities.ReloadSwimLoop => "ACT_MP_RELOAD_SWIM_SECONDARY2_LOOP",                              // :3857
        MpActivities.ReloadSwimEnd => "ACT_MP_RELOAD_SWIM_SECONDARY2_END",                                // :3858
        "ACT_MP_RELOAD_AIRWALK" => "ACT_MP_RELOAD_AIRWALK_SECONDARY2",                                    // :3859
        "ACT_MP_RELOAD_AIRWALK_LOOP" => "ACT_MP_RELOAD_AIRWALK_SECONDARY2_LOOP",                          // :3860
        "ACT_MP_RELOAD_AIRWALK_END" => "ACT_MP_RELOAD_AIRWALK_SECONDARY2_END",                            // :3861

        MpActivities.AttackStandGrenade => "ACT_MP_ATTACK_STAND_GRENADE",                                  // :3863
        "ACT_MP_ATTACK_CROUCH_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE",                                   // :3864
        "ACT_MP_ATTACK_SWIM_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE",                                     // :3865
        "ACT_MP_ATTACK_AIRWALK_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE",                                  // :3866

        "ACT_MP_GESTURE_VC_HANDMOUTH" => "ACT_MP_GESTURE_VC_HANDMOUTH_SECONDARY",                          // :3868
        "ACT_MP_GESTURE_VC_FINGERPOINT" => "ACT_MP_GESTURE_VC_FINGERPOINT_SECONDARY",                      // :3869
        "ACT_MP_GESTURE_VC_FISTPUMP" => "ACT_MP_GESTURE_VC_FISTPUMP_SECONDARY",                            // :3870
        "ACT_MP_GESTURE_VC_THUMBSUP" => "ACT_MP_GESTURE_VC_THUMBSUP_SECONDARY",                            // :3871
        "ACT_MP_GESTURE_VC_NODYES" => "ACT_MP_GESTURE_VC_NODYES_SECONDARY",                                // :3872
        "ACT_MP_GESTURE_VC_NODNO" => "ACT_MP_GESTURE_VC_NODNO_SECONDARY",                                  // :3873
        _ => act,
    };

    /// <summary>
    /// <c>s_acttableMelee</c> (:3876-3918): no deployed rows at all (the
    /// deployed claims pass through untranslated), and the secondary fire
    /// pair answers the <c>MELEE_SECONDARY</c> names while its swim and
    /// airwalk twins answer the plain melee names.
    /// </summary>
    private static string Melee(string act) => act switch
    {
        MpActivities.StandIdle => TFAnimActivities.StandMelee,             // :3878
        MpActivities.CrouchIdle => "ACT_MP_CROUCH_MELEE",                  // :3879
        MpActivities.Run => "ACT_MP_RUN_MELEE",                            // :3880
        "ACT_MP_WALK" => "ACT_MP_WALK_MELEE",                              // :3881
        TFAnimActivities.Airwalk => "ACT_MP_AIRWALK_MELEE",                // :3882
        MpActivities.CrouchWalk => "ACT_MP_CROUCHWALK_MELEE",              // :3883
        MpActivities.Jump => "ACT_MP_JUMP_MELEE",                          // :3884
        TFAnimActivities.JumpStart => "ACT_MP_JUMP_START_MELEE",           // :3885
        TFAnimActivities.JumpFloat => "ACT_MP_JUMP_FLOAT_MELEE",           // :3886
        TFAnimActivities.JumpLand => "ACT_MP_JUMP_LAND_MELEE",             // :3887
        MpActivities.Swim => "ACT_MP_SWIM_MELEE",                          // :3888
        TFAnimActivities.DoubleJumpCrouch => "ACT_MP_DOUBLEJUMP_CROUCH_MELEE", // :3889

        MpActivities.AttackStandPrimaryFire => "ACT_MP_ATTACK_STAND_MELEE",                               // :3891
        "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE" => "ACT_MP_ATTACK_CROUCH_MELEE",                               // :3892
        MpActivities.AttackSwimPrimaryFire => "ACT_MP_ATTACK_SWIM_MELEE",                                 // :3893
        "ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE" => "ACT_MP_ATTACK_AIRWALK_MELEE",                             // :3894

        MpActivities.AttackStandSecondaryFire => "ACT_MP_ATTACK_STAND_MELEE_SECONDARY",                   // :3896
        "ACT_MP_ATTACK_CROUCH_SECONDARYFIRE" => "ACT_MP_ATTACK_CROUCH_MELEE_SECONDARY",                   // :3897
        "ACT_MP_ATTACK_SWIM_SECONDARYFIRE" => "ACT_MP_ATTACK_SWIM_MELEE",                                 // :3898
        "ACT_MP_ATTACK_AIRWALK_SECONDARYFIRE" => "ACT_MP_ATTACK_AIRWALK_MELEE",                           // :3899

        "ACT_MP_GESTURE_FLINCH" => "ACT_MP_GESTURE_FLINCH_MELEE",                                         // :3901

        "ACT_MP_GRENADE1_DRAW" => "ACT_MP_MELEE_GRENADE1_DRAW",                                            // :3903
        "ACT_MP_GRENADE1_IDLE" => "ACT_MP_MELEE_GRENADE1_IDLE",                                            // :3904
        "ACT_MP_GRENADE1_ATTACK" => "ACT_MP_MELEE_GRENADE1_ATTACK",                                        // :3905
        "ACT_MP_GRENADE2_DRAW" => "ACT_MP_MELEE_GRENADE2_DRAW",                                            // :3906
        "ACT_MP_GRENADE2_IDLE" => "ACT_MP_MELEE_GRENADE2_IDLE",                                            // :3907
        "ACT_MP_GRENADE2_ATTACK" => "ACT_MP_MELEE_GRENADE2_ATTACK",                                        // :3908

        "ACT_MP_GESTURE_VC_HANDMOUTH" => "ACT_MP_GESTURE_VC_HANDMOUTH_MELEE",                              // :3910
        "ACT_MP_GESTURE_VC_FINGERPOINT" => "ACT_MP_GESTURE_VC_FINGERPOINT_MELEE",                          // :3911
        "ACT_MP_GESTURE_VC_FISTPUMP" => "ACT_MP_GESTURE_VC_FISTPUMP_MELEE",                                // :3912
        "ACT_MP_GESTURE_VC_THUMBSUP" => "ACT_MP_GESTURE_VC_THUMBSUP_MELEE",                                // :3913
        "ACT_MP_GESTURE_VC_NODYES" => "ACT_MP_GESTURE_VC_NODYES_MELEE",                                    // :3914
        "ACT_MP_GESTURE_VC_NODNO" => "ACT_MP_GESTURE_VC_NODNO_MELEE",                                     // :3915

        "ACT_MP_FALLING_STOMP" => "ACT_MP_FALLING_STOMP_MELEE",                                           // :3917
        _ => act,
    };

    /// <summary><c>s_acttableItem1</c> (:3920-3967).</summary>
    private static string Item1(string act) => act switch
    {
        MpActivities.StandIdle => "ACT_MP_STAND_ITEM1",                    // :3922
        MpActivities.CrouchIdle => "ACT_MP_CROUCH_ITEM1",                  // :3923
        MpActivities.Run => "ACT_MP_RUN_ITEM1",                            // :3924
        "ACT_MP_WALK" => "ACT_MP_WALK_ITEM1",                              // :3925
        TFAnimActivities.Airwalk => "ACT_MP_AIRWALK_ITEM1",                // :3926
        MpActivities.CrouchWalk => "ACT_MP_CROUCHWALK_ITEM1",              // :3927
        MpActivities.Jump => "ACT_MP_JUMP_ITEM1",                          // :3928
        TFAnimActivities.JumpStart => "ACT_MP_JUMP_START_ITEM1",           // :3929
        TFAnimActivities.JumpFloat => "ACT_MP_JUMP_FLOAT_ITEM1",           // :3930
        TFAnimActivities.JumpLand => "ACT_MP_JUMP_LAND_ITEM1",             // :3931
        MpActivities.Swim => "ACT_MP_SWIM_ITEM1",                          // :3932
        TFAnimActivities.DoubleJumpCrouch => "ACT_MP_DOUBLEJUMP_CROUCH_ITEM1", // :3933

        MpActivities.AttackStandPrimaryFire => "ACT_MP_ATTACK_STAND_ITEM1",                               // :3935
        "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE" => "ACT_MP_ATTACK_CROUCH_ITEM1",                               // :3936
        MpActivities.AttackSwimPrimaryFire => "ACT_MP_ATTACK_SWIM_ITEM1",                                 // :3937
        "ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE" => "ACT_MP_ATTACK_AIRWALK_ITEM1",                             // :3938

        MpActivities.AttackStandSecondaryFire => "ACT_MP_ATTACK_STAND_ITEM1_SECONDARY",                   // :3940
        "ACT_MP_ATTACK_CROUCH_SECONDARYFIRE" => "ACT_MP_ATTACK_CROUCH_ITEM1_SECONDARY",                   // :3941
        "ACT_MP_ATTACK_SWIM_SECONDARYFIRE" => "ACT_MP_ATTACK_SWIM_ITEM1",                                 // :3942
        "ACT_MP_ATTACK_AIRWALK_SECONDARYFIRE" => "ACT_MP_ATTACK_AIRWALK_ITEM1",                           // :3943

        TFAnimActivities.Deployed => "ACT_MP_DEPLOYED_ITEM1",                                             // :3945
        TFAnimActivities.DeployedIdle => "ACT_MP_DEPLOYED_IDLE_ITEM1",                                    // :3946
        TFAnimActivities.CrouchDeployed => "ACT_MP_CROUCHWALK_DEPLOYED_ITEM1",                            // :3947
        TFAnimActivities.CrouchDeployedIdle => "ACT_MP_CROUCH_DEPLOYED_IDLE_ITEM1",                       // :3948

        "ACT_MP_GESTURE_FLINCH" => "ACT_MP_GESTURE_FLINCH_ITEM1",                                         // :3952

        "ACT_MP_GRENADE1_DRAW" => "ACT_MP_ITEM1_GRENADE1_DRAW",                                            // :3954
        "ACT_MP_GRENADE1_IDLE" => "ACT_MP_ITEM1_GRENADE1_IDLE",                                            // :3955
        "ACT_MP_GRENADE1_ATTACK" => "ACT_MP_ITEM1_GRENADE1_ATTACK",                                        // :3956
        "ACT_MP_GRENADE2_DRAW" => "ACT_MP_ITEM1_GRENADE2_DRAW",                                            // :3957
        "ACT_MP_GRENADE2_IDLE" => "ACT_MP_ITEM1_GRENADE2_IDLE",                                            // :3958
        "ACT_MP_GRENADE2_ATTACK" => "ACT_MP_ITEM1_GRENADE2_ATTACK",                                        // :3959

        "ACT_MP_GESTURE_VC_HANDMOUTH" => "ACT_MP_GESTURE_VC_HANDMOUTH_ITEM1",                              // :3961
        "ACT_MP_GESTURE_VC_FINGERPOINT" => "ACT_MP_GESTURE_VC_FINGERPOINT_ITEM1",                          // :3962
        "ACT_MP_GESTURE_VC_FISTPUMP" => "ACT_MP_GESTURE_VC_FISTPUMP_ITEM1",                                // :3963
        "ACT_MP_GESTURE_VC_THUMBSUP" => "ACT_MP_GESTURE_VC_THUMBSUP_ITEM1",                                // :3964
        "ACT_MP_GESTURE_VC_NODYES" => "ACT_MP_GESTURE_VC_NODYES_ITEM1",                                    // :3965
        "ACT_MP_GESTURE_VC_NODNO" => "ACT_MP_GESTURE_VC_NODNO_ITEM1",                                      // :3966
        _ => act,
    };

    /// <summary>
    /// <c>s_acttableItem2</c> (:3969-4022) — the engineer's-wrench family.
    /// Stock's second AIRWALK row (:3982, a duplicate of :3975 and so never
    /// reached by the first-match search) is carried once; the reload rows
    /// have no loop or end twins (:3985-3988).
    /// </summary>
    private static string Item2(string act) => act switch
    {
        MpActivities.StandIdle => "ACT_MP_STAND_ITEM2",                    // :3971
        MpActivities.CrouchIdle => "ACT_MP_CROUCH_ITEM2",                  // :3972
        MpActivities.Run => "ACT_MP_RUN_ITEM2",                            // :3973
        "ACT_MP_WALK" => "ACT_MP_WALK_ITEM2",                              // :3974
        TFAnimActivities.Airwalk => "ACT_MP_AIRWALK_ITEM2",                // :3975, :3982 (stock's dead duplicate)
        MpActivities.CrouchWalk => "ACT_MP_CROUCHWALK_ITEM2",              // :3976
        MpActivities.Jump => "ACT_MP_JUMP_ITEM2",                          // :3977
        TFAnimActivities.JumpStart => "ACT_MP_JUMP_START_ITEM2",           // :3978
        TFAnimActivities.JumpFloat => "ACT_MP_JUMP_FLOAT_ITEM2",           // :3979
        TFAnimActivities.JumpLand => "ACT_MP_JUMP_LAND_ITEM2",             // :3980
        MpActivities.Swim => "ACT_MP_SWIM_ITEM2",                          // :3981
        TFAnimActivities.DoubleJumpCrouch => "ACT_MP_DOUBLEJUMP_CROUCH_ITEM2", // :3983

        MpActivities.ReloadStand => "ACT_MP_RELOAD_STAND_ITEM2",                                          // :3985
        MpActivities.ReloadCrouch => "ACT_MP_RELOAD_CROUCH_ITEM2",                                        // :3986
        MpActivities.ReloadSwim => "ACT_MP_RELOAD_SWIM_ITEM2",                                            // :3987
        "ACT_MP_RELOAD_AIRWALK" => "ACT_MP_RELOAD_AIRWALK_ITEM2",                                         // :3988

        MpActivities.AttackStandPrimaryFire => "ACT_MP_ATTACK_STAND_ITEM2",                               // :3990
        "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE" => "ACT_MP_ATTACK_CROUCH_ITEM2",                               // :3991
        MpActivities.AttackSwimPrimaryFire => "ACT_MP_ATTACK_SWIM_ITEM2",                                 // :3992
        "ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE" => "ACT_MP_ATTACK_AIRWALK_ITEM2",                             // :3993

        TFAnimActivities.Deployed => "ACT_MP_DEPLOYED_ITEM2",                                             // :3995
        TFAnimActivities.DeployedIdle => "ACT_MP_DEPLOYED_IDLE_ITEM2",                                    // :3996
        TFAnimActivities.CrouchDeployed => "ACT_MP_CROUCHWALK_DEPLOYED_ITEM2",                            // :3997
        TFAnimActivities.CrouchDeployedIdle => "ACT_MP_CROUCH_DEPLOYED_IDLE_ITEM2",                       // :3998

        MpActivities.AttackStandSecondaryFire => "ACT_MP_ATTACK_STAND_ITEM2_SECONDARY",                   // :4002
        "ACT_MP_ATTACK_CROUCH_SECONDARYFIRE" => "ACT_MP_ATTACK_CROUCH_ITEM2_SECONDARY",                   // :4003
        "ACT_MP_ATTACK_SWIM_SECONDARYFIRE" => "ACT_MP_ATTACK_SWIM_ITEM2",                                 // :4004
        "ACT_MP_ATTACK_AIRWALK_SECONDARYFIRE" => "ACT_MP_ATTACK_AIRWALK_ITEM2",                           // :4005

        "ACT_MP_GESTURE_FLINCH" => "ACT_MP_GESTURE_FLINCH_ITEM2",                                         // :4007

        "ACT_MP_GRENADE1_DRAW" => "ACT_MP_ITEM2_GRENADE1_DRAW",                                            // :4009
        "ACT_MP_GRENADE1_IDLE" => "ACT_MP_ITEM2_GRENADE1_IDLE",                                            // :4010
        "ACT_MP_GRENADE1_ATTACK" => "ACT_MP_ITEM2_GRENADE1_ATTACK",                                        // :4011
        "ACT_MP_GRENADE2_DRAW" => "ACT_MP_ITEM2_GRENADE2_DRAW",                                            // :4012
        "ACT_MP_GRENADE2_IDLE" => "ACT_MP_ITEM2_GRENADE2_IDLE",                                            // :4013
        "ACT_MP_GRENADE2_ATTACK" => "ACT_MP_ITEM2_GRENADE2_ATTACK",                                        // :4014

        "ACT_MP_GESTURE_VC_HANDMOUTH" => "ACT_MP_GESTURE_VC_HANDMOUTH_ITEM2",                              // :4016
        "ACT_MP_GESTURE_VC_FINGERPOINT" => "ACT_MP_GESTURE_VC_FINGERPOINT_ITEM2",                          // :4017
        "ACT_MP_GESTURE_VC_FISTPUMP" => "ACT_MP_GESTURE_VC_FISTPUMP_ITEM2",                                // :4018
        "ACT_MP_GESTURE_VC_THUMBSUP" => "ACT_MP_GESTURE_VC_THUMBSUP_ITEM2",                                // :4019
        "ACT_MP_GESTURE_VC_NODYES" => "ACT_MP_GESTURE_VC_NODYES_ITEM2",                                    // :4020
        "ACT_MP_GESTURE_VC_NODNO" => "ACT_MP_GESTURE_VC_NODNO_ITEM2",                                      // :4021
        _ => act,
    };

    /// <summary><c>s_acttableItem3</c> (:4024-4052).</summary>
    private static string Item3(string act) => act switch
    {
        MpActivities.StandIdle => "ACT_MP_STAND_ITEM3",                    // :4026
        MpActivities.CrouchIdle => "ACT_MP_CROUCH_ITEM3",                  // :4027
        MpActivities.Run => "ACT_MP_RUN_ITEM3",                            // :4028
        "ACT_MP_WALK" => "ACT_MP_WALK_ITEM3",                              // :4029
        TFAnimActivities.Airwalk => "ACT_MP_AIRWALK_ITEM3",                // :4030
        MpActivities.CrouchWalk => "ACT_MP_CROUCHWALK_ITEM3",              // :4031
        MpActivities.Jump => "ACT_MP_JUMP_ITEM3",                          // :4032
        TFAnimActivities.JumpStart => "ACT_MP_JUMP_START_ITEM3",           // :4033
        TFAnimActivities.JumpFloat => "ACT_MP_JUMP_FLOAT_ITEM3",           // :4034
        TFAnimActivities.JumpLand => "ACT_MP_JUMP_LAND_ITEM3",             // :4035
        MpActivities.Swim => "ACT_MP_SWIM_ITEM3",                          // :4036

        MpActivities.AttackStandPrimaryFire => "ACT_MP_ATTACK_STAND_ITEM3",                               // :4038
        "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE" => "ACT_MP_ATTACK_CROUCH_ITEM3",                               // :4039
        MpActivities.AttackSwimPrimaryFire => "ACT_MP_ATTACK_SWIM_ITEM3",                                 // :4040
        "ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE" => "ACT_MP_ATTACK_AIRWALK_ITEM3",                             // :4041

        MpActivities.AttackStandSecondaryFire => "ACT_MP_ATTACK_STAND_ITEM3_SECONDARY",                   // :4043
        "ACT_MP_ATTACK_CROUCH_SECONDARYFIRE" => "ACT_MP_ATTACK_CROUCH_ITEM3_SECONDARY",                   // :4044
        "ACT_MP_ATTACK_SWIM_SECONDARYFIRE" => "ACT_MP_ATTACK_SWIM_ITEM3",                                 // :4045
        "ACT_MP_ATTACK_AIRWALK_SECONDARYFIRE" => "ACT_MP_ATTACK_AIRWALK_ITEM3",                           // :4046

        TFAnimActivities.Deployed => "ACT_MP_DEPLOYED_ITEM3",                                             // :4048
        TFAnimActivities.DeployedIdle => "ACT_MP_DEPLOYED_IDLE_ITEM3",                                    // :4049
        TFAnimActivities.CrouchDeployed => "ACT_MP_CROUCHWALK_DEPLOYED_ITEM3",                            // :4050
        TFAnimActivities.CrouchDeployedIdle => "ACT_MP_CROUCH_DEPLOYED_IDLE_ITEM3",                       // :4051
        _ => act,
    };

    /// <summary><c>s_acttableItem4</c> (:4054-4082).</summary>
    private static string Item4(string act) => act switch
    {
        MpActivities.StandIdle => "ACT_MP_STAND_ITEM4",                    // :4056
        MpActivities.CrouchIdle => "ACT_MP_CROUCH_ITEM4",                  // :4057
        MpActivities.Run => "ACT_MP_RUN_ITEM4",                            // :4058
        "ACT_MP_WALK" => "ACT_MP_WALK_ITEM4",                              // :4059
        TFAnimActivities.Airwalk => "ACT_MP_AIRWALK_ITEM4",                // :4060
        MpActivities.CrouchWalk => "ACT_MP_CROUCHWALK_ITEM4",              // :4061
        MpActivities.Jump => "ACT_MP_JUMP_ITEM4",                          // :4062
        TFAnimActivities.JumpStart => "ACT_MP_JUMP_START_ITEM4",           // :4063
        TFAnimActivities.JumpFloat => "ACT_MP_JUMP_FLOAT_ITEM4",           // :4064
        TFAnimActivities.JumpLand => "ACT_MP_JUMP_LAND_ITEM4",             // :4065
        MpActivities.Swim => "ACT_MP_SWIM_ITEM4",                          // :4066

        MpActivities.AttackStandPrimaryFire => "ACT_MP_ATTACK_STAND_ITEM4",                               // :4068
        "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE" => "ACT_MP_ATTACK_CROUCH_ITEM4",                               // :4069
        MpActivities.AttackSwimPrimaryFire => "ACT_MP_ATTACK_SWIM_ITEM4",                                 // :4070
        "ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE" => "ACT_MP_ATTACK_AIRWALK_ITEM4",                             // :4071

        MpActivities.AttackStandSecondaryFire => "ACT_MP_ATTACK_STAND_ITEM4_SECONDARY",                   // :4073
        "ACT_MP_ATTACK_CROUCH_SECONDARYFIRE" => "ACT_MP_ATTACK_CROUCH_ITEM4_SECONDARY",                   // :4074
        "ACT_MP_ATTACK_SWIM_SECONDARYFIRE" => "ACT_MP_ATTACK_SWIM_ITEM4",                                 // :4075
        "ACT_MP_ATTACK_AIRWALK_SECONDARYFIRE" => "ACT_MP_ATTACK_AIRWALK_ITEM4",                           // :4076

        TFAnimActivities.Deployed => "ACT_MP_DEPLOYED_ITEM4",                                             // :4078
        TFAnimActivities.DeployedIdle => "ACT_MP_DEPLOYED_IDLE_ITEM4",                                    // :4079
        TFAnimActivities.CrouchDeployed => "ACT_MP_CROUCHWALK_DEPLOYED_ITEM4",                            // :4080
        TFAnimActivities.CrouchDeployedIdle => "ACT_MP_CROUCH_DEPLOYED_IDLE_ITEM4",                       // :4081
        _ => act,
    };

    /// <summary><c>s_acttableBuilding</c> (:4084-4114).</summary>
    private static string Building(string act) => act switch
    {
        MpActivities.StandIdle => "ACT_MP_STAND_BUILDING",                 // :4086
        MpActivities.CrouchIdle => "ACT_MP_CROUCH_BUILDING",               // :4087
        MpActivities.Run => "ACT_MP_RUN_BUILDING",                         // :4088
        "ACT_MP_WALK" => "ACT_MP_WALK_BUILDING",                           // :4089
        TFAnimActivities.Airwalk => "ACT_MP_AIRWALK_BUILDING",             // :4090
        MpActivities.CrouchWalk => "ACT_MP_CROUCHWALK_BUILDING",           // :4091
        MpActivities.Jump => "ACT_MP_JUMP_BUILDING",                       // :4092
        TFAnimActivities.JumpStart => "ACT_MP_JUMP_START_BUILDING",        // :4093
        TFAnimActivities.JumpFloat => "ACT_MP_JUMP_FLOAT_BUILDING",        // :4094
        TFAnimActivities.JumpLand => "ACT_MP_JUMP_LAND_BUILDING",          // :4095
        MpActivities.Swim => "ACT_MP_SWIM_BUILDING",                       // :4096

        MpActivities.AttackStandPrimaryFire => "ACT_MP_ATTACK_STAND_BUILDING",                            // :4098
        "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE" => "ACT_MP_ATTACK_CROUCH_BUILDING",                            // :4099
        MpActivities.AttackSwimPrimaryFire => "ACT_MP_ATTACK_SWIM_BUILDING",                              // :4100
        "ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE" => "ACT_MP_ATTACK_AIRWALK_BUILDING",                          // :4101

        MpActivities.AttackStandGrenade => "ACT_MP_ATTACK_STAND_GRENADE_BUILDING",                        // :4103
        "ACT_MP_ATTACK_CROUCH_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE_BUILDING",                          // :4104
        "ACT_MP_ATTACK_SWIM_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE_BUILDING",                            // :4105
        "ACT_MP_ATTACK_AIRWALK_GRENADE" => "ACT_MP_ATTACK_STAND_GRENADE_BUILDING",                         // :4106

        "ACT_MP_GESTURE_VC_HANDMOUTH" => "ACT_MP_GESTURE_VC_HANDMOUTH_BUILDING",                           // :4108
        "ACT_MP_GESTURE_VC_FINGERPOINT" => "ACT_MP_GESTURE_VC_FINGERPOINT_BUILDING",                       // :4109
        "ACT_MP_GESTURE_VC_FISTPUMP" => "ACT_MP_GESTURE_VC_FISTPUMP_BUILDING",                             // :4110
        "ACT_MP_GESTURE_VC_THUMBSUP" => "ACT_MP_GESTURE_VC_THUMBSUP_BUILDING",                             // :4111
        "ACT_MP_GESTURE_VC_NODYES" => "ACT_MP_GESTURE_VC_NODYES_BUILDING",                                 // :4112
        "ACT_MP_GESTURE_VC_NODNO" => "ACT_MP_GESTURE_VC_NODNO_BUILDING",                                   // :4113
        _ => act,
    };

    /// <summary><c>s_acttablePDA</c> (:4116-4139): only two attack rows.</summary>
    private static string Pda(string act) => act switch
    {
        MpActivities.StandIdle => "ACT_MP_STAND_PDA",                      // :4118
        MpActivities.CrouchIdle => "ACT_MP_CROUCH_PDA",                    // :4119
        MpActivities.Run => "ACT_MP_RUN_PDA",                              // :4120
        "ACT_MP_WALK" => "ACT_MP_WALK_PDA",                                // :4121
        TFAnimActivities.Airwalk => "ACT_MP_AIRWALK_PDA",                  // :4122
        MpActivities.CrouchWalk => "ACT_MP_CROUCHWALK_PDA",                // :4123
        MpActivities.Jump => "ACT_MP_JUMP_PDA",                            // :4124
        TFAnimActivities.JumpStart => "ACT_MP_JUMP_START_PDA",             // :4125
        TFAnimActivities.JumpFloat => "ACT_MP_JUMP_FLOAT_PDA",             // :4126
        TFAnimActivities.JumpLand => "ACT_MP_JUMP_LAND_PDA",               // :4127
        MpActivities.Swim => "ACT_MP_SWIM_PDA",                            // :4128

        MpActivities.AttackStandPrimaryFire => "ACT_MP_ATTACK_STAND_PDA",                                 // :4130
        MpActivities.AttackSwimPrimaryFire => "ACT_MP_ATTACK_SWIM_PDA",                                   // :4131

        "ACT_MP_GESTURE_VC_HANDMOUTH" => "ACT_MP_GESTURE_VC_HANDMOUTH_PDA",                                // :4133
        "ACT_MP_GESTURE_VC_FINGERPOINT" => "ACT_MP_GESTURE_VC_FINGERPOINT_PDA",                            // :4134
        "ACT_MP_GESTURE_VC_FISTPUMP" => "ACT_MP_GESTURE_VC_FISTPUMP_PDA",                                  // :4135
        "ACT_MP_GESTURE_VC_THUMBSUP" => "ACT_MP_GESTURE_VC_THUMBSUP_PDA",                                  // :4136
        "ACT_MP_GESTURE_VC_NODYES" => "ACT_MP_GESTURE_VC_NODYES_PDA",                                      // :4137
        "ACT_MP_GESTURE_VC_NODNO" => "ACT_MP_GESTURE_VC_NODNO_PDA",                                        // :4138
        _ => act,
    };

    /// <summary>
    /// <c>s_acttableMeleeAllclass</c> (:4141-4181). Stock's quirks: the
    /// double-jump row (:4154) answers the plain <c>_MELEE</c> name, not
    /// the allclass family, and so do the flinch, grenade and voice rows.
    /// </summary>
    private static string MeleeAllclass(string act) => act switch
    {
        MpActivities.StandIdle => "ACT_MP_STAND_MELEE_ALLCLASS",           // :4143
        MpActivities.CrouchIdle => "ACT_MP_CROUCH_MELEE_ALLCLASS",         // :4144
        MpActivities.Run => "ACT_MP_RUN_MELEE_ALLCLASS",                   // :4145
        "ACT_MP_WALK" => "ACT_MP_WALK_MELEE_ALLCLASS",                     // :4146
        TFAnimActivities.Airwalk => "ACT_MP_AIRWALK_MELEE_ALLCLASS",       // :4147
        MpActivities.CrouchWalk => "ACT_MP_CROUCHWALK_MELEE_ALLCLASS",     // :4148
        MpActivities.Jump => "ACT_MP_JUMP_MELEE_ALLCLASS",                 // :4149
        TFAnimActivities.JumpStart => "ACT_MP_JUMP_START_MELEE_ALLCLASS",  // :4150
        TFAnimActivities.JumpFloat => "ACT_MP_JUMP_FLOAT_MELEE_ALLCLASS",  // :4151
        TFAnimActivities.JumpLand => "ACT_MP_JUMP_LAND_MELEE_ALLCLASS",    // :4152
        MpActivities.Swim => "ACT_MP_SWIM_MELEE_ALLCLASS",                 // :4153
        TFAnimActivities.DoubleJumpCrouch => "ACT_MP_DOUBLEJUMP_CROUCH_MELEE", // :4154

        MpActivities.AttackStandPrimaryFire => "ACT_MP_ATTACK_STAND_MELEE_ALLCLASS",                      // :4156
        "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE" => "ACT_MP_ATTACK_CROUCH_MELEE_ALLCLASS",                      // :4157
        MpActivities.AttackSwimPrimaryFire => "ACT_MP_ATTACK_SWIM_MELEE_ALLCLASS",                        // :4158
        "ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE" => "ACT_MP_ATTACK_AIRWALK_MELEE_ALLCLASS",                    // :4159

        MpActivities.AttackStandSecondaryFire => "ACT_MP_ATTACK_STAND_MELEE_SECONDARY",                   // :4161
        "ACT_MP_ATTACK_CROUCH_SECONDARYFIRE" => "ACT_MP_ATTACK_CROUCH_MELEE_SECONDARY",                   // :4162
        "ACT_MP_ATTACK_SWIM_SECONDARYFIRE" => "ACT_MP_ATTACK_SWIM_MELEE_ALLCLASS",                        // :4163
        "ACT_MP_ATTACK_AIRWALK_SECONDARYFIRE" => "ACT_MP_ATTACK_AIRWALK_MELEE_ALLCLASS",                  // :4164

        "ACT_MP_GESTURE_FLINCH" => "ACT_MP_GESTURE_FLINCH_MELEE",                                         // :4166

        "ACT_MP_GRENADE1_DRAW" => "ACT_MP_MELEE_GRENADE1_DRAW",                                            // :4168
        "ACT_MP_GRENADE1_IDLE" => "ACT_MP_MELEE_GRENADE1_IDLE",                                            // :4169
        "ACT_MP_GRENADE1_ATTACK" => "ACT_MP_MELEE_GRENADE1_ATTACK",                                        // :4170
        "ACT_MP_GRENADE2_DRAW" => "ACT_MP_MELEE_GRENADE2_DRAW",                                            // :4171
        "ACT_MP_GRENADE2_IDLE" => "ACT_MP_MELEE_GRENADE2_IDLE",                                            // :4172
        "ACT_MP_GRENADE2_ATTACK" => "ACT_MP_MELEE_GRENADE2_ATTACK",                                        // :4173

        "ACT_MP_GESTURE_VC_HANDMOUTH" => "ACT_MP_GESTURE_VC_HANDMOUTH_MELEE",                              // :4175
        "ACT_MP_GESTURE_VC_FINGERPOINT" => "ACT_MP_GESTURE_VC_FINGERPOINT_MELEE",                          // :4176
        "ACT_MP_GESTURE_VC_FISTPUMP" => "ACT_MP_GESTURE_VC_FISTPUMP_MELEE",                                // :4177
        "ACT_MP_GESTURE_VC_THUMBSUP" => "ACT_MP_GESTURE_VC_THUMBSUP_MELEE",                                // :4178
        "ACT_MP_GESTURE_VC_NODYES" => "ACT_MP_GESTURE_VC_NODYES_MELEE",                                    // :4179
        "ACT_MP_GESTURE_VC_NODNO" => "ACT_MP_GESTURE_VC_NODNO_MELEE",                                      // :4180
        _ => act,
    };
}
