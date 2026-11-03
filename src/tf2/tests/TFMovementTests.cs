extern alias client;

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Movement;
using TF2.Shared.Player;

using Xunit;

using ClientData = client::TF2.Shared.Player.ITFPlayerData;
using ClientEngineFiles = client::SourceSharp.Abi.IEngineFiles;
using ClientState = client::TF2.Shared.Player.TFPlayerNetState;

namespace TF2.Tests;

/// <summary>The class table both sides read, from the sealed class script fixtures.</summary>
internal static class TFMovementFixtures
{
    public static TFClassTable Server() => new(new RecordingLog(), TFClassTableTests.Files.Shipped());

    public static client::TF2.Shared.TFClassTable Client()
        => new(new client::SourceSharp.Abi.RecordingLog(), new ClientFiles(TFClassTableTests.Files.Shipped()));

    private sealed class ClientFiles(TFClassTableTests.Files files) : ClientEngineFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
    }

    /// <summary>One player's carrier state, by index, for the rules to read and write.</summary>
    public sealed class Players : ITFPlayerData
    {
        public Dictionary<int, TFPlayerNetState> States { get; } = [];

        public TFPlayerNetState Add(int index, TFClass playerClass)
        {
            var state = new TFPlayerNetState();
            state.BindLocal();
            state.OwnerIndex = index;
            state.SetClass(playerClass);
            States[index] = state;
            return state;
        }

        public TFPlayerNetState? Predicted(int playerIndex) => States.GetValueOrDefault(playerIndex);
        public TFPlayerNetState? Networked(int playerIndex) => States.GetValueOrDefault(playerIndex);
    }

    public sealed class ClientPlayers : ClientData
    {
        public Dictionary<int, ClientState> States { get; } = [];

        public ClientState Add(int index, TFClass playerClass)
        {
            var state = new ClientState();
            state.BindLocal();
            state.OwnerIndex = index;
            state.PlayerClass = (int)playerClass;
            States[index] = state;
            return state;
        }

        public ClientState? Predicted(int playerIndex) => States.GetValueOrDefault(playerIndex);
        public ClientState? Networked(int playerIndex) => States.GetValueOrDefault(playerIndex);
    }
}

/// <summary>
/// <see cref="TFMaxSpeed"/>: <c>CTFPlayer::TeamFortress_CalculateMaxSpeed</c>'s
/// milestone arms over the real class scripts.
/// </summary>
public sealed class TFMaxSpeedTests
{
    private static readonly TFClassTable Classes = TFMovementFixtures.Server();

    private static float Speed(TFClass c, TFCondMask conditions = default, TFClass disguise = TFClass.Undefined,
                               TFWeaponId weapon = TFWeaponId.None, float speedMod = 1f, float cloakedCap = 999f,
                               AttributeHooks? attribs = null, IAttributeProvider? manager = null,
                               TFStunFlags stunFlags = TFStunFlags.None, bool carrying = false,
                               int healTarget = -1, Func<int, TFPeerInput?>? peers = null, int self = -1)
        => TFMaxSpeed.Calculate(Classes, new TFMoveInputs(c, conditions, disguise, weapon, speedMod, stunFlags,
                                                          carrying, healTarget),
                                cloakedCap, attribs, manager, peers, self);

    /// <summary>A peer table over bare classes, the patient's side of the match.</summary>
    private static Func<int, TFPeerInput?> Peers(params (int Index, TFClass Class)[] edges)
        => index => edges.Where(e => e.Index == index)
                         .Select(e => new TFPeerInput(e.Index, new TFMoveInputs(e.Class, default)))
                         .FirstOrDefault();

    /// <summary>A percentage attribute of the hook class, on a fresh manager as the player's own.</summary>
    private static AttributeContainer ManagerWith(string hookClass, float value)
    {
        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5001, "attr 5001", hookClass,
            AttributeDescriptionFormat.Percentage, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), value));
        return manager;
    }

    [Theory]
    [InlineData(TFClass.Scout, 400f)]
    [InlineData(TFClass.Soldier, 240f)]
    [InlineData(TFClass.HeavyWeapons, 230f)]
    [InlineData(TFClass.Spy, 320f)]
    [InlineData(TFClass.Medic, 320f)]
    public void A_class_runs_at_its_scripts_speed(TFClass c, float expected)
        => Assert.Equal(expected, Speed(c));

    [Fact]
    public void Every_class_without_a_condition_runs_at_its_table_value_unscaled()
    {
        for (var c = TFClass.Scout; c < TFClass.CountAll; c++)
            Assert.Equal(Classes[c].MaxSpeed, Speed(c));
    }

    [Fact]
    public void An_aiming_heavy_is_held_to_a_flat_110()
        => Assert.Equal(TFMaxSpeed.HeavyAiming, Speed(TFClass.HeavyWeapons, TFCondMask.Of(TFCond.Aiming)));

    [Fact]
    public void An_aiming_sniper_is_held_to_80()
        => Assert.Equal(80f, Speed(TFClass.Sniper, TFCondMask.Of(TFCond.Aiming)));

    [Fact]
    public void An_aiming_bow_is_held_to_160()
        => Assert.Equal(160f, Speed(TFClass.Sniper, TFCondMask.Of(TFCond.Aiming), weapon: TFWeaponId.CompoundBow));

    [Fact]
    public void Aiming_never_speeds_a_class_up()
    {
        // 110 is the heavy's cap, 80 everyone else's: a class already slower keeps its own.
        Assert.Equal(80f, Speed(TFClass.Soldier, TFCondMask.Of(TFCond.Aiming)));
        Assert.True(Speed(TFClass.Soldier) > 80f, "the stimulus is a real cap");
    }

    [Fact]
    public void A_disguised_spy_is_no_faster_than_the_disguise()
    {
        var disguised = TFCondMask.Of(TFCond.Disguised);

        Assert.Equal(230f, Speed(TFClass.Spy, disguised, TFClass.HeavyWeapons));
        Assert.Equal(320f, Speed(TFClass.Spy, disguised, TFClass.Scout));   // a min, never a raise
    }

    [Fact]
    public void A_stealthed_disguise_does_not_slow_the_spy()
    {
        var both = TFCondMask.Of([TFCond.Disguised, TFCond.Stealthed]);

        Assert.Equal(320f, Speed(TFClass.Spy, both, TFClass.HeavyWeapons));
        Assert.Equal(320f, Speed(TFClass.Spy, TFCondMask.Of([TFCond.Disguised, TFCond.StealthedUserBuffFading]), TFClass.HeavyWeapons));
    }

    [Fact]
    public void Disguised_as_a_dispenser_stands_still()
        => Assert.Equal(0f, Speed(TFClass.Spy, TFCondMask.Of(TFCond.DisguisedAsDispenser)));

    [Fact]
    public void Stealth_caps_at_the_cloaked_speed_convar()
    {
        Assert.Equal(320f, Speed(TFClass.Spy, TFCondMask.Of(TFCond.Stealthed)));
        Assert.Equal(100f, Speed(TFClass.Spy, TFCondMask.Of(TFCond.Stealthed), cloakedCap: 100f));
    }

    [Fact]
    public void The_active_weapons_speed_mod_scales_the_answer()
    {
        Assert.Equal(200f, Speed(TFClass.Scout, weapon: TFWeaponId.Scattergun, speedMod: 0.5f));
        Assert.Equal(400f, Speed(TFClass.Scout, speedMod: 0.5f));   // no weapon, no mod
    }

    [Fact]
    public void No_class_answers_stocks_one()
        => Assert.Equal(1f, Speed(TFClass.Undefined));

    // ---- the attribute hooks (tf_player_shared.cpp:10913, 11002-11003) -----

    private static readonly AttributeHooks Hooks = new();

    [Fact]
    public void A_player_move_speed_attribute_scales_the_final_speed()
    {
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(400f, Speed(TFClass.Scout, attribs: Hooks));   // no manager, the walk unchanged
        Assert.Equal(200f, Speed(TFClass.Scout, attribs: Hooks, manager: manager));
    }

    [Fact]
    public void The_aiming_attribute_scales_the_cap_while_aiming()
    {
        var aiming = TFCondMask.Of(TFCond.Aiming);
        var manager = ManagerWith(TFMaxSpeed.AimSpeedAttrib, 0.5f);

        Assert.Equal(55f, Speed(TFClass.HeavyWeapons, aiming, attribs: Hooks, manager: manager));
        Assert.Equal(110f, Speed(TFClass.HeavyWeapons, aiming, attribs: Hooks));   // the cap without the attribute
    }

    [Fact]
    public void The_aiming_attribute_waits_for_the_aiming()
    {
        // The hook lives inside the aiming branch (stock :10913), so a soldier
        // at full run is not slowed by an aiming attribute.
        var manager = ManagerWith(TFMaxSpeed.AimSpeedAttrib, 0.5f);

        Assert.Equal(240f, Speed(TFClass.Soldier, attribs: Hooks, manager: manager));
    }

    [Fact]
    public void The_speed_hook_runs_after_the_weapons_own_mod()
    {
        // Stock's order: the active weapon's speed mod (:10959), then
        // mult_player_movespeed (:11003). Both percentage, the walk composes
        // them in that order on the one value.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(100f, Speed(TFClass.Scout, weapon: TFWeaponId.Scattergun, speedMod: 0.5f,
                                 attribs: Hooks, manager: manager));
    }

    // ---- the loser state's slow (tf_player_shared.cpp:10989-11000) ----------

    [Fact]
    public void A_loser_state_stun_halves_the_speed()
    {
        var stunned = TFCondMask.Of(TFCond.Stunned);

        Assert.Equal(200f, Speed(TFClass.Scout, stunned, stunFlags: TFStunFlags.LoserState));
    }

    [Fact]
    public void A_stun_from_a_trigger_shames_less_than_the_walk()
    {
        var stunned = TFCondMask.Of(TFCond.Stunned);
        var byTrigger = TFStunFlags.LoserState | TFStunFlags.ByTrigger;

        Assert.Equal(300f, Speed(TFClass.Scout, stunned, stunFlags: byTrigger));
    }

    [Fact]
    public void A_stun_without_the_loser_state_bit_does_not_slow()
    {
        var stunned = TFCondMask.Of(TFCond.Stunned);

        // The movement and controls stuns slow through their own arms.
        Assert.Equal(400f, Speed(TFClass.Scout, stunned, stunFlags: TFStunFlags.Movement));
        Assert.Equal(400f, Speed(TFClass.Scout, stunned, stunFlags: TFStunFlags.Movement | TFStunFlags.Controls));
    }

    [Fact]
    public void The_stun_slow_is_gated_on_the_stunned_condition()
        // The word outliving the condition (the tick that clears it has not
        // run) is stock's InCond arm: no condition, no slow.
        => Assert.Equal(400f, Speed(TFClass.Scout, stunFlags: TFStunFlags.LoserState));

    [Fact]
    public void A_halloween_bomb_head_spares_the_stun_slow()
    {
        var both = TFCondMask.Of([TFCond.Stunned, TFCond.HalloweenBombHead]);

        Assert.Equal(400f, Speed(TFClass.Scout, both, stunFlags: TFStunFlags.LoserState));
    }

    [Fact]
    public void The_stun_slow_waits_for_the_speed_hook()
    {
        // Stock's order: the stun (:10989-11000) before mult_player_movespeed
        // (:11003) — the hook walks the stunned value.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(100f, Speed(TFClass.Scout, TFCondMask.Of(TFCond.Stunned),
                                 stunFlags: TFStunFlags.LoserState, attribs: Hooks, manager: manager));
    }

    // ---- the carried building's toll (tf_player_shared.cpp:10976-10987) -----

    [Fact]
    public void A_carried_building_takes_a_tenth()
        => Assert.Equal(360f, Speed(TFClass.Scout, carrying: true));

    [Fact]
    public void A_halloween_bomb_head_spares_the_carry_toll()
    {
        var bombHead = TFCondMask.Of(TFCond.HalloweenBombHead);

        Assert.Equal(400f, Speed(TFClass.Scout, bombHead, carrying: true));
    }

    [Fact]
    public void The_carry_toll_stacks_with_the_stun_before_the_hook()
    {
        // Stock's order inside bAllowSlowing: the carry (:10983-10987), then
        // the stun (:10989-11000), then mult_player_movespeed (:11003) —
        // 400 x 0.9 x 0.5 x 0.5.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(90f, Speed(TFClass.Scout, TFCondMask.Of(TFCond.Stunned), stunFlags: TFStunFlags.LoserState,
                                 attribs: Hooks, manager: manager, carrying: true));
    }

    // ---- the medic's match (tf_player_shared.cpp:11029-11045) ---------------

    [Fact]
    public void A_medic_matches_a_faster_patients_speed()
    {
        var peers = Peers((4, TFClass.Scout));

        Assert.Equal(400f, Speed(TFClass.Medic, healTarget: 4, peers: peers, self: 3));
    }

    [Fact]
    public void The_match_is_a_max_never_a_demotion()
    {
        // A slower patient leaves the medic's own walk standing; the arms the
        // patient runs are the patient's, here the stun's.
        var stunned = TFCondMask.Of(TFCond.Stunned);

        Assert.Equal(320f, Speed(TFClass.Medic, healTarget: 5, peers: Peers((5, TFClass.Soldier)), self: 3));
        Assert.Equal(320f, Speed(TFClass.Medic, stunned, healTarget: 4, self: 3,
                                 peers: i => i == 4
                                     ? new TFPeerInput(4, new TFMoveInputs(TFClass.Scout, stunned,
                                                                           StunFlags: TFStunFlags.LoserState))
                                     : null));
    }

    [Fact]
    public void A_medic_without_a_patient_or_a_roster_runs_alone()
    {
        // No target on the medigun, no lookup, a target the side never saw:
        // stock's null patient arm, each way.
        Assert.Equal(320f, Speed(TFClass.Medic));
        Assert.Equal(320f, Speed(TFClass.Medic, healTarget: 4));
        Assert.Equal(320f, Speed(TFClass.Medic, healTarget: 4, peers: _ => null, self: 3));
    }

    [Fact]
    public void Two_medics_matching_each_other_end_at_the_default()
    {
        // Stock's re-entry guard (:10879-10888): the second walk of a player
        // already in flight answers the class table, so the cycle closes at
        // B's own 320, not A's hooked 160, and A ends at 320 not 160.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(320f, Speed(TFClass.Medic, healTarget: 4, attribs: Hooks, manager: manager,
                                 peers: Peers((3, TFClass.Medic), (4, TFClass.Medic)), self: 3));
    }

    [Fact]
    public void A_medic_healing_himself_runs_at_the_table()
    {
        // The self-heal is the re-entry it exists for: the hooked 160 against
        // his own in-flight default, the table's 320 out.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(320f, Speed(TFClass.Medic, healTarget: 3, attribs: Hooks, manager: manager,
                                 peers: Peers((3, TFClass.Medic)), self: 3));
    }

    [Fact]
    public void The_medic_match_waits_for_the_speed_hook()
    {
        // Stock's order: mult_player_movespeed (:11003) BEFORE the match
        // (:11029) — the hooked value is what the patient's raise lifts.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(400f, Speed(TFClass.Medic, healTarget: 4, attribs: Hooks, manager: manager,
                                 peers: Peers((4, TFClass.Scout)), self: 3));
    }
}

/// <summary><see cref="TFJump"/>: <c>CTFPlayer::CanAirDash</c> and <c>CTFGameMovement::AirDash</c>'s speed.</summary>
public sealed class TFJumpTests
{
    [Fact]
    public void A_scout_dashes_once_and_the_next_press_is_refused()
    {
        Assert.Equal(268.3281573f, TFJump.AirDash(TFClass.Scout, default, airDash: 0, dashCount: 1));
        Assert.Equal(0f, TFJump.AirDash(TFClass.Scout, default, airDash: 1, dashCount: 1));
    }

    [Fact]
    public void A_soldier_never_dashes()
        => Assert.Equal(0f, TFJump.AirDash(TFClass.Soldier, default, airDash: 0, dashCount: 1));

    [Fact]
    public void The_dash_count_convar_allows_more()
    {
        Assert.True(TFJump.CanAirDash(TFClass.Scout, default, airDash: 1, dashCount: 2));
        Assert.False(TFJump.CanAirDash(TFClass.Scout, default, airDash: 2, dashCount: 2));
    }

    [Fact]
    public void The_soda_poppers_hype_allows_five()
    {
        var hype = TFCondMask.Of(TFCond.SodapopperHype);

        Assert.True(TFJump.CanAirDash(TFClass.Scout, hype, airDash: 4, dashCount: 1));
        Assert.False(TFJump.CanAirDash(TFClass.Scout, hype, airDash: 5, dashCount: 1));
    }

    [Fact]
    public void The_halloween_conditions_override_the_class()
    {
        Assert.True(TFJump.CanAirDash(TFClass.Soldier, TFCondMask.Of(TFCond.HalloweenSpeedBoost), 9, 1));
        Assert.False(TFJump.CanAirDash(TFClass.Scout, TFCondMask.Of(TFCond.HalloweenKart), 0, 1));
    }

    // ---- the no-dash attribute (tf_player_shared.cpp:12875-12878) ----------

    private static readonly AttributeHooks JumpHooks = new();

    private static AttributeContainer JumpManager(int value)
    {
        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5003, "attr 5003", TFJump.DoubleJumpDisabledAttrib,
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), value));
        return manager;
    }

    [Fact]
    public void A_double_jump_disabled_player_is_refused_the_first_dash_too()
    {
        var manager = JumpManager(1);

        Assert.True(TFJump.CanAirDash(TFClass.Scout, default, 0, 1, attribs: JumpHooks));   // no manager, the hook unset
        Assert.False(TFJump.CanAirDash(TFClass.Scout, default, 0, 1, attribs: JumpHooks, playerAttributes: manager));

        // The dash answers 0: no velocity, the jump refused.
        Assert.Equal(0f, TFJump.AirDash(TFClass.Scout, default, 0, 1, attribs: JumpHooks, playerAttributes: manager));
    }

    [Fact]
    public void Only_a_one_disables_the_dash()
    {
        // Stock's `if ( 1 == iNoAirDash )`: any other value leaves the dash.
        Assert.True(TFJump.CanAirDash(TFClass.Scout, default, 0, 1, attribs: JumpHooks,
                                      playerAttributes: JumpManager(2)));
    }

    // ---- the active weapon's dash count (tf_player_shared.cpp:12860-12864) ----

    private static AttributeContainer WeaponAttrib(string name, float value)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(5005, "attr 5005", name,
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), value));
        return item;
    }

    [Fact]
    public void The_active_weapons_dash_count_buys_the_second_dash()
    {
        var weapon = WeaponAttrib(TFJump.AirDashCountAttrib, 1);

        // The convar alone spent: the weapon's count is still to come.
        Assert.False(TFJump.CanAirDash(TFClass.Scout, default, 1, 1));
        Assert.True(TFJump.CanAirDash(TFClass.Scout, default, 1, 1, weapon, JumpHooks));
        Assert.Equal(TFJump.DashSpeed, TFJump.AirDash(TFClass.Scout, default, 1, 1, weapon, JumpHooks));

        // And the raised count is spent in turn.
        Assert.False(TFJump.CanAirDash(TFClass.Scout, default, 2, 1, weapon, JumpHooks));
    }

    [Fact]
    public void Without_an_active_weapon_the_count_is_the_convars()
        => Assert.False(TFJump.CanAirDash(TFClass.Scout, default, 1, 1, attribs: JumpHooks));

    // ---- the jump-height attributes (tf_gamemovement.cpp:997-1005, 1023) ----

    [Fact]
    public void The_players_jump_height_scales_the_dash()
    {
        var manager = WeaponAttrib(TFJump.JumpHeightAttrib, 0.5f);

        Assert.Equal(TFJump.DashSpeed * 1.5f,
                     TFJump.AirDash(TFClass.Scout, default, 0, 1, attribs: JumpHooks, playerAttributes: manager));
    }

    [Fact]
    public void The_active_weapons_jump_height_scales_on_top()
    {
        var weapon = WeaponAttrib(TFJump.JumpHeightFromWeaponAttrib, 0.5f);

        // Stock's order: the passive walk, then the weapon's over its result.
        Assert.Equal(TFJump.DashSpeed * 1.5f,
                     TFJump.AirDash(TFClass.Scout, default, 0, 1, weapon, JumpHooks));
        Assert.Equal(TFJump.DashSpeed * 2f,
                     TFJump.AirDash(TFClass.Scout, default, 0, 1, weapon, JumpHooks,
                                    WeaponAttrib(TFJump.JumpHeightAttrib, 0.5f)));
    }

    [Fact]
    public void A_refused_dash_has_no_height_to_scale()
        => Assert.Equal(0f, TFJump.AirDash(TFClass.Soldier, default, 0, 1,
                                           WeaponAttrib(TFJump.JumpHeightAttrib, 1f), JumpHooks));
}

/// <summary>
/// <see cref="TFMovementRules"/>: the one <c>IMovementRules</c> owner, deciding
/// from the carrier's state only, the same on both sides.
/// </summary>
public sealed class TFMovementRulesTests
{
    private const int Index = 2;

    private static (TFMovementRules Rules, TFMovementFixtures.Players Players) Make()
    {
        var players = new TFMovementFixtures.Players();
        return (new TFMovementRules(TFMovementFixtures.Server(), players), players);
    }

    [Fact]
    public void The_constants_are_the_tf_view_vectors()
    {
        MovementParams p = Make().Rules.Constants!.Value;

        Assert.Equal(new Vector(0f, 0f, 72f), p.View);
        Assert.Equal(new Vector(-24f, -24f, 0f), p.HullMin);
        Assert.Equal(new Vector(24f, 24f, 82f), p.HullMax);
        Assert.Equal(new Vector(24f, 24f, 62f), p.DuckHullMax);
        Assert.Equal(new Vector(0f, 0f, 45f), p.DuckView);
        Assert.Equal(new Vector(0f, 0f, 14f), p.DeadViewHeight);
        Assert.Equal(800f, p.Gravity);
        Assert.NotEqual(Movement.Current.HullMax, p.HullMax);   // the HL2 hull is replaced
    }

    [Fact]
    public void The_ground_jump_leaves_at_stocks_289()
    {
        MovementParams p = Make().Rules.Constants!.Value;

        // The core's JumpVelocity: sqrt(2 g h) at the constants' gravity.
        Assert.Equal(289f, MathF.Sqrt(2.0f * p.Gravity * p.JumpHeight));
    }

    /// <summary>
    /// That sqrt is the non-HL2 arm's: left in the core's HL2 arm, this height
    /// leaves at 252 (live, before this fact: 234 after the command's gravity,
    /// where stock TF2 reads 271).
    /// </summary>
    [Fact]
    public void The_loop_runs_the_arms_stock_tf2_was_built_with()
        => Assert.True(((IMovementRules)Make().Rules).NoHl2Arms);

    /// <summary>
    /// The categorizer record is the override's shape, unconditional: the
    /// observer refusal first, any ladder off the ground, the 250 rise without
    /// the base's lift relief, the step probe, and no player-surface block
    /// (tf_gamemovement.cpp:2334-2459 against gamemovement.cpp:3794-3931).
    /// </summary>
    [Fact]
    public void The_categorize_rules_are_the_override_and_nobody_else()
    {
        CategorizePositionRules r = Make().Rules.CategorizePosition(Index)!.Value;

        Assert.True(r.ObserverFirst);
        Assert.True(r.GroundlessLadder);
        Assert.Equal(250f, r.RiseVelocity);            // not NON_JUMP_VELOCITY's 140
        Assert.False(r.GroundVelocityRelief);          // the base's :3836-3850 dropped
        Assert.True(r.StepSizeProbe);
        Assert.False(r.PlayerSurfaceMaterial);
    }

    [Fact]
    public void Footing_without_a_carrier_answers_nothing()
        => Assert.Null(Make().Rules.Footing(Index, default, new Vector(0f, 0f, 1f)));

    [Fact]
    public void Footing_without_either_condition_answers_nothing()
    {
        var (rules, players) = Make();
        _ = players.Add(Index, TFClass.Soldier);

        Assert.Null(rules.Footing(Index, new Vector(0f, 100f, 0f), new Vector(0f, 0f, 1f)));
    }

    /// <summary>The air current slows the air, not the ground (tf_gamemovement.cpp:2392-2395).</summary>
    [Fact]
    public void Air_current_scales_the_air_friction_alone()
    {
        var (rules, players) = Make();
        players.Add(Index, TFClass.Soldier).SetConditions(TFCondMask.Of(TFCond.AirCurrent));

        FootingAnswer a = rules.Footing(Index, new Vector(0f, 100f, 0f), new Vector(0f, 0f, 1f))!.Value;

        Assert.False(a.InAir);
        Assert.Equal(1f, a.GroundFrictionScale);
        Assert.Equal(0.75f, a.AirFrictionScale);
    }

    /// <summary>Lost footing's takeoff: any away-from-ground velocity is air (:2400-2404).</summary>
    [Fact]
    public void Lost_footings_rise_is_air()
    {
        var (rules, players) = Make();
        players.Add(Index, TFClass.Soldier).SetConditions(TFCondMask.Of(TFCond.LostFooting));

        FootingAnswer a = rules.Footing(Index, new Vector(30f, 0f, 40f), new Vector(0f, 0f, 1f))!.Value;

        Assert.True(a.InAir);
        Assert.Equal(1f, a.GroundFrictionScale);
    }

    /// <summary>Sliding on lost footing scrapes at the friction convar (:2407-2411).</summary>
    [Fact]
    public void Lost_footings_slide_scrapes_the_ground_friction()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Soldier);
        state.SetConditions(TFCondMask.Of(TFCond.LostFooting));

        // 100 across the floor plane, nothing along it: a slide over the 50 restick.
        FootingAnswer a = rules.Footing(Index, new Vector(100f, 0f, 0f), new Vector(0f, 0f, 1f))!.Value;

        Assert.False(a.InAir);
        Assert.Equal(0.1f, a.GroundFrictionScale);
        Assert.True(state.InCond(TFCond.LostFooting), "a slide keeps the condition");
    }

    /// <summary>Too slow to slide, the footing comes back and the condition goes (:2412-2417).</summary>
    [Fact]
    public void Lost_footings_restick_clears_the_condition()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Soldier);
        state.SetConditions(TFCondMask.Of(TFCond.LostFooting));

        FootingAnswer a = rules.Footing(Index, new Vector(10f, 0f, 0f), new Vector(0f, 0f, 1f))!.Value;

        Assert.False(a.InAir);
        Assert.Equal(1f, a.GroundFrictionScale);
        Assert.False(state.InCond(TFCond.LostFooting), "the footing came back");
    }

    [Fact]
    public void Max_speed_is_the_carriers_class_speed_whatever_the_engine_arrived_at()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Scout);

        Assert.Equal(400f, rules.MaxSpeedScale(Index, 320f, false, 0));

        state.SetClass(TFClass.HeavyWeapons);
        Assert.Equal(230f, rules.MaxSpeedScale(Index, 400f, false, 0));

        state.SetConditions(TFCondMask.Of(TFCond.Aiming));
        Assert.Equal(110f, rules.MaxSpeedScale(Index, 230f, false, 0));
    }

    [Fact]
    public void Without_a_carrier_or_a_class_the_core_speed_stands()
    {
        var (rules, players) = Make();
        Assert.Equal(320f, rules.MaxSpeedScale(Index, 320f, false, 0));

        players.Add(Index, TFClass.Undefined);
        Assert.Equal(320f, rules.MaxSpeedScale(Index, 320f, false, 0));
    }

    /// <summary>The round state, frozen for the fact (<see cref="TF2.Shared.Rules.ITFRoundStateView"/>).</summary>
    private sealed class RoundAt(TF2.Shared.Rules.RoundState state) : TF2.Shared.Rules.ITFRoundStateView
    {
        public TF2.Shared.Rules.RoundState State { get; set; } = state;
    }

    /// <summary>
    /// The restart freeze (<c>tf_player_shared.cpp:10819</c>, :10847): while
    /// the round restarts — stock's <c>InRoundRestart</c>, the PREROUND state —
    /// and <c>tf_player_movement_restart_freeze</c> stands, the speed walk
    /// answers 1 (:10872-10874).
    /// </summary>
    [Fact]
    public void A_round_restart_freezes_the_speed_to_stocks_one()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Scout);
        var rules = new TFMovementRules(TFMovementFixtures.Server(), players,
                                        rounds: new RoundAt(TF2.Shared.Rules.RoundState.Preround));

        Assert.Equal(1f, rules.MaxSpeedScale(Index, 400f, false, 0));
    }

    [Fact]
    public void The_running_round_leaves_the_class_speed()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Scout);
        var rules = new TFMovementRules(TFMovementFixtures.Server(), players,
                                        rounds: new RoundAt(TF2.Shared.Rules.RoundState.RoundRunning));

        Assert.Equal(400f, rules.MaxSpeedScale(Index, 320f, false, 0));
    }

    /// <summary>The freeze is the convar's to lift: registered, its declared default freezes and a 0 does not.</summary>
    [Fact]
    public void The_freeze_follows_the_convar()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Scout);
        var convars = new ConVarRegistry(new ClampBackend());
        convars.RegisterTypes([typeof(TFPlayerMovementRestartFreezeConVar)]);
        var rules = new TFMovementRules(TFMovementFixtures.Server(), players,
                                        rounds: new RoundAt(TF2.Shared.Rules.RoundState.Preround),
                                        restartFreeze: convars.Get<TFPlayerMovementRestartFreezeConVar>());

        Assert.Equal(1f, rules.MaxSpeedScale(Index, 400f, false, 0));

        convars.Get<TFPlayerMovementRestartFreezeConVar>()!.SetValue(false);

        Assert.Equal(400f, rules.MaxSpeedScale(Index, 400f, false, 0));
    }

    /// <summary>
    /// <c>CTFGameMovement::PreventBunnyJumping</c> (tf_gamemovement.cpp:1086-1112):
    /// the cap is <c>BUNNYJUMP_MAX_SPEED_FACTOR</c> (1.2) times the max speed
    /// the loop arrived with, whatever the class — stock reads
    /// <c>m_flMaxspeed</c> and asks nothing else.
    /// </summary>
    [Fact]
    public void The_bunny_jump_cap_is_stocks_1_2_times_the_max_speed()
    {
        var (rules, players) = Make();
        players.Add(Index, TFClass.Scout);

        Assert.Equal(480f, rules.BunnyJumpCap(Index, 400f), 2);
        Assert.Equal(276f, rules.BunnyJumpCap(Index, 230f), 2);
    }

    /// <summary>
    /// Stock's backpedal clamp (<c>tf_clamp_back_speed</c> 0.9 /
    /// <c>tf_clamp_back_speed_min</c> 100, tf_gamemovement.cpp:47-48): the
    /// rules hand the loop that pair whatever the player's class — stock's
    /// WalkMove clamp asks nothing of the player. Unbound convars leave the
    /// rule on stock's values; registered ones move it.
    /// </summary>
    [Fact]
    public void The_backpedal_limit_is_stocks_two_convars()
    {
        var (rules, players) = Make();
        players.Add(Index, TFClass.Scout);

        Assert.Equal(new BackpedalLimit(0.9f, 100f), rules.BackpedalLimit(Index, 400f));
    }

    /// <summary>
    /// The pair is live: a registered convar answers through the rule
    /// (TFWinPanelTests' backend, so a <c>SetValue</c> round-trips), and the
    /// next command's clamp is the operator's values, not the declared ones.
    /// </summary>
    [Fact]
    public void The_backpedal_limit_follows_the_two_convars()
    {
        var players = new TFMovementFixtures.Players();
        players.Add(Index, TFClass.Scout);
        var convars = new ConVarRegistry(new ClampBackend());
        convars.RegisterTypes([typeof(TFClampBackSpeedConVar), typeof(TFClampBackSpeedMinConVar)]);
        var rules = new TFMovementRules(TFMovementFixtures.Server(), players,
                                        backSpeed: convars.Get<TFClampBackSpeedConVar>(),
                                        backSpeedMin: convars.Get<TFClampBackSpeedMinConVar>());

        // The engine's read-back of the declared defaults, not a constant.
        Assert.Equal(new BackpedalLimit(0.9f, 100f), rules.BackpedalLimit(Index, 400f));

        convars.Get<TFClampBackSpeedConVar>()!.SetValue(0.5f);
        convars.Get<TFClampBackSpeedMinConVar>()!.SetValue(150f);

        Assert.Equal(new BackpedalLimit(0.5f, 150f), rules.BackpedalLimit(Index, 400f));
    }

    //=========================================================================
    // The movement stun (CTFGameMovement::StunMove, tf_gamemovement.cpp:537-627):
    // the seized controls and the slowed walk. The rule decides from the
    // carrier's stun mirror — the four fields stock's <c>UpdateLegacyStunSystem</c>
    // keeps networked (tf_player_shared.cpp:7441-7465, gates at :9938-9975) —
    // so the server's run and every client's replay decide alike. The lerp
    // trio (m_flStunLerpTarget, m_flLastMovementStunChange,
    // m_bStunNeedsFadeOut, tf_player_shared.h:1228-1230) is entity-local in
    // stock: sent by none of the tables (:444-482 lists only m_iStunIndex),
    // read by nothing but StunMove — here it is the rules' own per-player
    // history. The heavy-minigun button exception (:548-552) and the
    // CYOA-PDA/match-countdown no-move block (:614-624) are
    // docs/plans/tf2/deferred.tsv's rows.
    //=========================================================================

    /// <summary>
    /// The carrier's stun mirror as <c>UpdateLegacyStunSystem</c> would write
    /// it (:7446-7453): the active index, the entry's flags, its 0..255 amount
    /// and its expiry — with the permanent stun condition stock raises beside
    /// the stack (<c>AddCond( TF_COND_STUNNED, -1.f, ... )</c>, :9932).
    /// </summary>
    private static TFPlayerNetState Stunned(TFPlayerNetState state, int index, TFStunFlags flags,
                                            float amount, float expireTime)
    {
        state.StunIndex = index;
        state.StunFlags = (int)flags;
        state.MovementStunAmount = amount;
        state.StunExpireTime = expireTime;
        state.SetConditions(TFCondMask.Of(TFCond.Stunned));
        return state;
    }

    /// <summary>
    /// Nobody is stunned: without a carrier, and with a carrier whose mirror
    /// never armed, the command stands — stock's StunMove rewrites nothing
    /// outside its two arms (:540, :567, :585 all fail closed).
    /// </summary>
    [Fact]
    public void AnUnstunnedPlayerIsStunnedByNothing()
    {
        var (rules, players) = Make();

        Assert.Null(rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));

        players.Add(Index, TFClass.Scout);
        Assert.Null(rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The slow: half a stun (127.5 of 255) halves the wish moves —
    /// <c>mv-&gt;m_flForwardMove *= 1.f - flStunAmount</c> (:576-577) — and
    /// touches neither the buttons nor the up move.
    /// </summary>
    [Fact]
    public void AMovementStunScalesTheMovesByItsAmount()
    {
        var (rules, players) = Make();
        Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.Movement, 127.5f, 100f);

        Assert.Equal(new MovementStun(12345, 150f, 50f, 50f),
                     rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The amount gate is clamped both ways as stock's <c>GetAmountStunned</c>
    /// clamps it (:9943): over 255 stops the player outright, and a negative
    /// (clamped to 0) is no movement stun at all.
    /// </summary>
    [Fact]
    public void TheStunAmountIsClampedToItsTwoHundredFiftyFives()
    {
        var (rules, players) = Make();
        var state = Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.Movement, -5f, 100f);

        // Clamped to zero, it is no movement stun at all (:9943): no arm of
        // StunMove runs, and there is no lerp history behind it.
        Assert.Null(rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));

        state.MovementStunAmount = 300f;   // clamped to 255: the whole wish move goes
        Assert.Equal(new MovementStun(12345, 0f, 0f, 50f),
                     rules.MovementStun(Index, 10.1f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// Past its expiry the movement stun is no stun — stock's amount gate
    /// asks <c>flExpireTime &gt; gpGlobals-&gt;curtime</c> (:9942) — and with
    /// no lerp history behind it the command stands.
    /// </summary>
    [Fact]
    public void AnExpiredMovementStunStunsNothing()
    {
        var (rules, players) = Make();
        Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.Movement, 127.5f, 9.5f);

        Assert.Null(rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The forward-only flavour also kills the forward move — stock asks
    /// <c>GetStunFlags()</c> (the active entry's flags, tf_player_shared.h:570)
    /// beside the scale, twice over (:578-581, :600-603).
    /// </summary>
    [Fact]
    public void AForwardOnlyStunAlsoZeroesTheForwardMove()
    {
        var (rules, players) = Make();
        Stunned(players.Add(Index, TFClass.Scout), 0,
                TFStunFlags.Movement | TFStunFlags.MovementForwardOnly, 127.5f, 100f);

        Assert.Equal(new MovementStun(12345, 0f, 50f, 50f),
                     rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The controls stun seizes everything: the buttons (no firing, no use,
    /// no jump — :546) and all three moves (:554-559). The heavy-minigun
    /// exception that lets a spun gun through (:548-552) is the deferred row.
    /// </summary>
    [Fact]
    public void AControlStunSeizesTheButtonsAndMoves()
    {
        var (rules, players) = Make();
        Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.Controls, 0f, 100f);

        Assert.Equal(new MovementStun(0, 0f, 0f, 0f),
                     rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The loser-state stun seizes the buttons alone (:540-546): stock's
    /// move-zeroing arm is the controls stun's only (:554), and this stun's
    /// flag is neither the movement nor the controls one.
    /// </summary>
    [Fact]
    public void ALoserStateStunSeizesTheButtonsAlone()
    {
        var (rules, players) = Make();
        Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.LoserState, 0f, 100f);

        Assert.Equal(new MovementStun(0, 300f, 100f, 50f),
                     rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// Stock's <c>IsControlStunned</c> asks the flags and the condition but
    /// never the expiry (:9952-9961) — and neither does the mirror's reading
    /// of it: a controls stun holds past its expire time.
    /// </summary>
    [Fact]
    public void AControlStunHoldsPastItsExpiry()
    {
        var (rules, players) = Make();
        Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.Controls, 0f, 0f);

        Assert.Equal(new MovementStun(0, 0f, 0f, 0f),
                     rules.MovementStun(Index, 100f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The condition is load-bearing: every stun gate stands on
    /// <c>InCond( TF_COND_STUNNED )</c> beside the flags (:9942, :9956, :9970),
    /// so a mirror behind no condition — flags, amount and future expiry
    /// included — is inert.
    /// </summary>
    [Fact]
    public void WithoutTheStunConditionTheMirrorIsInert()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Scout);
        state.StunIndex = 0;
        state.StunFlags = (int)(TFStunFlags.Controls | TFStunFlags.Movement);
        state.MovementStunAmount = 255f;
        state.StunExpireTime = 100f;

        Assert.Null(rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The fade-out (stock's lerp, :569-609): while the stun holds, half the
    /// moves; when it ends, the 0.2 s out — the clock restarts at the tick the
    /// loss is first noticed (:590, that tick still scales by the full
    /// target), the scale halves across the band (:594's reversed
    /// <c>RemapValClamped</c>), and the tick the band runs out zeroes the lerp
    /// trio and returns the command unscaled one last time (:605-611 — stock
    /// returns TRUE there too; only the call after the clear answers false).
    /// The times are binary fractions so the band's arithmetic is exact.
    /// </summary>
    [Fact]
    public void TheMovementStunFadesOutOverTheStockBand()
    {
        var (rules, players) = Make();
        var state = Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.Movement, 127.5f, 100f);

        Assert.Equal(new MovementStun(1, 150f, 50f, 0f),
                     rules.MovementStun(Index, 10f, 1, 300f, 100f, 0f));

        state.StunExpireTime = 10f;   // the stun is gone; the fade-out owns the next 0.2 s
        // The first tick to notice re-arms the change clock (:590): still the full target.
        Assert.Equal(new MovementStun(1, 150f, 50f, 0f),
                     rules.MovementStun(Index, 10.125f, 1, 300f, 100f, 0f));
        // Halfway (Δ=0.125 → the reversed band says 0.375 of the target): scale keeps 1 − 0.5 × 0.375.
        Assert.Equal(new MovementStun(1, 243.75f, 81.25f, 0f),
                     rules.MovementStun(Index, 10.25f, 1, 300f, 100f, 0f));
        // The band has run out (Δ=0.125 past): the trio zeroes, the moves stand (:605-611).
        Assert.Equal(new MovementStun(1, 300f, 100f, 0f),
                     rules.MovementStun(Index, 10.375f, 1, 300f, 100f, 0f));
        Assert.Null(rules.MovementStun(Index, 10.5f, 1, 300f, 100f, 0f));
    }

    /// <summary>
    /// The speed arm reads the carrier's flags behind stock's gate, the same
    /// one <see cref="MovementStun"/> stands on: <c>IsLoserStateStunned</c>
    /// (tf_player_shared.cpp:9966-9975) asks for the active entry AND the
    /// condition before the word means anything, because the legacy copy goes
    /// stale behind a cleared entry (the mirror, :7446-7453). A stunned scout
    /// walks at half (:10994); the same carrier with the entry gone — the
    /// index cleared, the word still saying LoserState, the condition not yet
    /// re-arrived — walks at full speed, and so does the one with the
    /// condition gone.
    /// </summary>
    [Fact]
    public void The_stun_speed_scale_reads_the_flags_behind_the_gate()
    {
        var (rules, players) = Make();
        var state = Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.LoserState, 0f, 100f);

        Assert.Equal(200f, rules.MaxSpeedScale(Index, 320f, false, 0));   // :10994's half

        state.StunIndex = -1;   // the entry gone; the value word stale, as stock's mirror leaves it
        Assert.Equal(400f, rules.MaxSpeedScale(Index, 320f, false, 0));   // the gate: index+cond first

        state.SetConditions(default);   // and the condition-gone shape
        Assert.Equal(400f, rules.MaxSpeedScale(Index, 320f, false, 0));
    }

    /// <summary>A convar backend that stores what it is told, so a SetValue round-trips (the TFWinPanelTests rig).</summary>
    private sealed class ClampBackend : IConVarBackend
    {
        private readonly List<string> _names = [];
        private readonly List<string> _values = [];

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            _names.Add(name);
            _values.Add(defaultValue);
            return _names.Count - 1;
        }

        public void Set(int id, string value) => _values[id] = value;

        public string Get(int id) => _values[id];

        public string? ValueOf(string name) => _names.Contains(name) ? _values[_names.IndexOf(name)] : null;
    }

    /// <summary>The speed hooks walk the player's own attribute manager, the server's grant.</summary>
    [Fact]
    public void The_speed_hooks_walk_the_players_attribute_manager()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Scout);
        var items = new TFPlayerItems();
        var rules = new TFMovementRules(TFMovementFixtures.Server(), players,
                                        owners: new OwnersOf(items), hooks: new AttributeHooks());

        Assert.Equal(400f, rules.MaxSpeedScale(Index, 400f, false, 0));

        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5002, "attr 5002", TFMaxSpeed.MoveSpeedAttrib,
            AttributeDescriptionFormat.Percentage, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 0.5f));
        items.SetManager(Index, manager);

        Assert.Equal(200f, rules.MaxSpeedScale(Index, 400f, false, 0));

        // The player leaves: the grant and the scale go with them.
        items.Forget(Index);
        Assert.Equal(400f, rules.MaxSpeedScale(Index, 400f, false, 0));
    }

    /// <summary>The no-dash attribute reaches the dash through the same grant.</summary>
    [Fact]
    public void The_dash_refusal_walks_the_players_attribute_manager()
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(Index, TFClass.Scout);
        var items = new TFPlayerItems();
        var rules = new TFMovementRules(TFMovementFixtures.Server(), players,
                                        owners: new OwnersOf(items), hooks: new AttributeHooks());
        var request = new JumpRequest { PlayerIndex = Index, VerticalSpeed = 100f };

        Assert.Equal(TFJump.DashSpeed, rules.AirJump(in request));

        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5004, "attr 5004", TFJump.DoubleJumpDisabledAttrib,
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 1));
        items.SetManager(Index, manager);
        state.AirDash = 0;

        Assert.Equal(0f, rules.AirJump(in request));

        // The grant goes with the player: the dash comes back.
        items.Forget(Index);
        Assert.Equal(TFJump.DashSpeed, rules.AirJump(in request));
    }

    /// <summary>The dash count and the jump height walk the ACTIVE weapon's item, both sides alike.</summary>
    [Fact]
    public void The_active_weapons_item_reaches_the_dash()
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(Index, TFClass.Scout);
        var owners = new OwnersOf(new TFPlayerItems());
        var rules = new TFMovementRules(TFMovementFixtures.Server(), players, owners: owners,
                                        hooks: new AttributeHooks());
        var request = new JumpRequest { PlayerIndex = Index, VerticalSpeed = 100f };
        var weapon = new AttributeContainer();
        weapon.Set(AttributeValue.OfFloat(new AttributeDefinition(5006, "attr 5006", TFJump.AirDashCountAttrib,
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 1));

        state.AirDash = 1;
        Assert.Equal(0f, rules.AirJump(in request));   // the convar's one is spent

        owners.Active = weapon;
        Assert.Equal(TFJump.DashSpeed, rules.AirJump(in request));
        Assert.Equal(2, state.AirDash);
    }

    /// <summary>The owners seam as the side's registry holds it: the manager grant, nothing else answerable here.</summary>
    private sealed class OwnersOf(TFPlayerItems items) : ITFWeaponOwners
    {
        /// <summary>The active weapon's item, set by the fact that needs it.</summary>
        public IAttributeProvider? Active;

        public TFClass ClassOf(int playerIndex) => TFClass.Undefined;
        public TFConditions? ConditionsOf(int playerIndex) => null;
        public float CritMult(int playerIndex) => 1f;
        public bool WinsCrits(int playerIndex) => false;
        public IAttributeProvider? ItemOf(int playerIndex, string weaponName) => null;
        public IAttributeProvider? ActiveItemOf(int playerIndex) => Active;
        public IAttributeProvider? ManagerOf(int playerIndex) => items.ManagerOf(playerIndex);
        public bool OffGround(int playerIndex) => false;
        public bool EyesUnderwater(int playerIndex) => false;
        public bool DuckingOf(int playerIndex) => false;
    }

    [Fact]
    public void A_scout_dashes_once_the_third_press_is_refused_and_ground_gives_it_back()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Scout);
        var request = new JumpRequest { PlayerIndex = Index, VerticalSpeed = 100f };

        // The first press left the ground (the core's jump); the second is in the air.
        Assert.Equal(TFJump.DashSpeed, rules.AirJump(in request));
        Assert.Equal(1, state.AirDash);
        Assert.Equal(0f, rules.AirJump(in request));
        Assert.Equal(1, state.AirDash);

        rules.Grounded(Index);
        Assert.Equal(0, state.AirDash);
        Assert.Equal(TFJump.DashSpeed, rules.AirJump(in request));
    }

    [Fact]
    public void A_soldier_is_refused_and_nothing_is_counted()    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Soldier);

        Assert.Equal(0f, rules.AirJump(new JumpRequest { PlayerIndex = Index }));
        Assert.Equal(0, state.AirDash);
    }

    [Fact]
    public void No_carrier_refuses_the_dash()
        => Assert.Equal(0f, Make().Rules.AirJump(new JumpRequest { PlayerIndex = Index }));

    [Theory]
    [InlineData(TFClass.Scout, 65f)]
    [InlineData(TFClass.Soldier, 68f)]
    [InlineData(TFClass.HeavyWeapons, 75f)]
    [InlineData(TFClass.Sniper, 75f)]
    [InlineData(TFClass.Undefined, 72f)]
    public void The_standing_eye_is_the_classs(TFClass c, float eye)
    {
        var (rules, players) = Make();
        players.Add(Index, c);

        Assert.Equal(eye, rules.EyeHeight(Index, 72f));
    }

    /// <summary>One command stream, run twice from fresh rules and state: the answers are byte-identical.</summary>
    [Fact]
    public void The_same_input_stream_gives_the_same_answers()
    {
        static byte[] Run()
        {
            var (rules, players) = Make();
            var state = players.Add(Index, TFClass.Scout);
            var answers = new List<float>();
            var random = new Random(1234);
            for (int i = 0; i < 200; i++)
                Step(rules, state, random, answers);
            return answers.SelectMany(BitConverter.GetBytes).ToArray();
        }

        byte[] first = Run(), second = Run();
        Assert.Equal(first, second);
        Assert.Contains(TFJump.DashSpeed, Floats(first));   // the stream reached a dash
    }

    private static IEnumerable<float> Floats(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i += 4)
            yield return BitConverter.ToSingle(bytes, i);
    }

    private static readonly TFClass[] Normal =
    [
        TFClass.Scout, TFClass.Sniper, TFClass.Soldier, TFClass.Demoman, TFClass.Medic,
        TFClass.HeavyWeapons, TFClass.Pyro, TFClass.Spy, TFClass.Engineer,
    ];

    private static readonly TFCond[] Milestone =
    [
        TFCond.Aiming, TFCond.Disguised, TFCond.Stealthed, TFCond.DisguisedAsDispenser,
        TFCond.SodapopperHype, TFCond.HalloweenKart, TFCond.HalloweenSpeedBoost,
    ];

    /// <summary>One random command: sometimes a class or condition change, then the three asks, recorded.</summary>
    private static void Step(TFMovementRules rules, TFPlayerNetState state, Random random, List<float> answers)
    {
        int roll = random.Next(10);
        if (roll == 0)
            state.SetClass(Normal[random.Next(Normal.Length)]);
        else if (roll == 1)
            state.SetConditions(random.Next(3) == 0 ? default : TFCondMask.Of(Milestone[random.Next(Milestone.Length)]));
        else if (roll == 2)
            rules.Grounded(Index);
        state.DisguiseClass = (int)Normal[random.Next(Normal.Length)];

        answers.Add(rules.MaxSpeedScale(Index, 320f, false, 0));
        answers.Add(rules.AirJump(new JumpRequest { PlayerIndex = Index }));
        answers.Add(rules.EyeHeight(Index, 72f));
        answers.Add(state.AirDash);
    }

    /// <summary>
    /// The rules built in a server container and in a client container (each
    /// side's own copy of the shared source) over the same carrier states give
    /// identical answers, and leave identical state, for 100 random inputs.
    /// </summary>
    [Fact]
    public void Server_and_client_rules_agree_over_100_random_inputs()
    {
        var serverPlayers = new TFMovementFixtures.Players();
        var server = new ServiceContainer()
            .AddInstance(TFMovementFixtures.Server())
            .AddInstance<ITFPlayerData>(serverPlayers)
            .Create<TFMovementRules>();

        var clientPlayers = new TFMovementFixtures.ClientPlayers();
        var client = new client::SourceSharp.Abi.ServiceContainer()
            .AddInstance(TFMovementFixtures.Client())
            .AddInstance<ClientData>(clientPlayers)
            .Create<client::TF2.Shared.Movement.TFMovementRules>();

        var s = serverPlayers.Add(Index, TFClass.Scout);
        var c = clientPlayers.Add(Index, TFClass.Scout);
        var random = new Random(77);
        int dashes = 0, distinctSpeeds = 0;
        var speeds = new HashSet<float>();

        for (int i = 0; i < 100; i++)
        {
            int playerClass = (int)Normal[random.Next(Normal.Length)];
            var cond = random.Next(3) == 0 ? default : TFCondMask.Of(Milestone[random.Next(Milestone.Length)]);
            int disguise = (int)Normal[random.Next(Normal.Length)];
            int airDash = random.Next(3);
            bool ground = random.Next(4) == 0;

            s.PlayerClass = c.PlayerClass = playerClass;
            s.DisguiseClass = c.DisguiseClass = disguise;
            s.AirDash = c.AirDash = airDash;
            s.SetConditions(cond);
            (c.PlayerCond, c.PlayerCondEx, c.PlayerCondEx2, c.PlayerCondEx3, c.PlayerCondEx4)
                = (cond.Word0, cond.Word1, cond.Word2, cond.Word3, cond.Word4);
            if (ground)
            {
                server.Grounded(Index);
                client.Grounded(Index);
            }

            var request = new JumpRequest { PlayerIndex = Index, VerticalSpeed = 50f };
            float speed = server.MaxSpeedScale(Index, 320f, false, 0);
            Assert.Equal(speed, client.MaxSpeedScale(Index, 320f, false, 0));
            float dash = server.AirJump(in request);
            Assert.Equal(dash, client.AirJump(in request));
            Assert.Equal(server.EyeHeight(Index, 72f), client.EyeHeight(Index, 72f));
            Assert.Equal(s.AirDash, c.AirDash);

            if (dash > 0f)
                dashes++;
            if (speeds.Add(speed))
                distinctSpeeds++;
        }

        Assert.True(dashes > 0, "some inputs dashed");
        Assert.True(distinctSpeeds > 3, $"the inputs reached several arms ({distinctSpeeds} speeds)");
    }

    /// <summary>
    /// Stock's fall-through (<c>TauntMove</c> :710-715): a plain taunt without
    /// the sliding gate's yes leaves the command exactly as sent — stock's
    /// <c>tf_allow_sliding_taunt</c> default and the unset item flag.
    /// </summary>
    [Fact]
    public void TauntingWithoutSlidingLeavesTheCommandToTheWalk()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Scout);
        state.SetConditions(TFCondMask.Of(TFCond.Taunting));

        Assert.Null(rules.MovementTaunt(Index, 1f / 60f, 300f));
    }

    /// <summary>
    /// The sliding arm at the item defaults (:644-645; tf_player.cpp:1041-1044
    /// leave speed and ramp at zero): the smoothed move lands on zero and the
    /// caps ride with it (:698-706).
    /// </summary>
    [Fact]
    public void AThrillingTauntSlidesAtTheItemDefaults()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Scout);
        state.SetConditions(TFCondMask.Of([TFCond.Taunting, TFCond.HalloweenThriller]));

        Assert.Equal(new MovementTaunt(0f, 0f, 0f), rules.MovementTaunt(Index, 1f / 60f, 300f));
    }

    /// <summary>A side whose carrier has not arrived answers like stock's no-taunt fall-through.</summary>
    [Fact]
    public void WithoutACarrierTheTauntLeavesTheWalkAlone()
    {
        var (rules, _) = Make();

        Assert.Null(rules.MovementTaunt(Index, 1f / 60f, 300f));
    }
}
