using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Shared.Anim;

using TF2.Shared;
using TF2.Shared.Anim;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// <see cref="TFAnimLayer"/>: the TF2 answer to the machine's main sequence.
///
/// <para>
/// Stock's <c>CTFPlayerAnimState</c> overrode every gait handler of
/// <c>CMultiPlayerAnimState::CalcMainActivity</c> (jump, duck, swim, move —
/// <c>tf_playeranimstate.cpp:1265-1538</c>) and translated the chosen
/// activity through the active weapon's act-table
/// (<c>CTFWeaponBase::GetActivityWeaponRole</c> and the <c>s_acttable*</c>
/// lists, tf_weaponbase.cpp:3660-4181). Here that override is a layer
/// ordered under the HL2 one, and these facts are the stock table read back:
/// every expected name is a literal quoting its row in
/// <c>tf_weaponbase.cpp</c> or its line in <c>tf_playeranimstate.cpp</c>.
/// </para>
/// </summary>
public sealed class TFAnimLayerTests
{
    //-------------------------------------------------------------------------
    // The rig: a fake target (the machine's whole question surface), fakes
    // for the two mod seams, and the machine driven through the TF2 layer
    // above the HL2 one — the composition ServerModule/ClientModule build.
    //-------------------------------------------------------------------------

    private sealed class FakeTarget : IAnimTarget
    {
        public Vector VelocityField;
        public float MaxSpeedValue = 320f;
        public bool OnGroundValue = true;
        public bool DuckedValue { get; set; }
        public int WaterLevelValue { get; set; }
        public int EntityIndexValue = 1;

        public readonly List<(int Sequence, float Rate, bool Restart)> Played = new();
        public readonly List<(int Slot, int Seq)> LayerPushes = new();
        public readonly Dictionary<string, int> Sequences = new(StringComparer.Ordinal);

        public int EntityIndex => EntityIndexValue;
        public Vector Velocity => VelocityField;
        public float BodyYaw => 0f;
        public bool RootFollowsFeet => true;
        public float MaxSpeed => MaxSpeedValue;
        public bool OnGround => OnGroundValue;
        public bool Ducked => DuckedValue;
        public int WaterLevel => WaterLevelValue;
        public bool Alive => true;
        public bool ShouldUpdateAnimState => true;
        public LegAnimType LegAnimType => LegAnimType.EightWay;
        public bool HasModel => true;
        public PlayerPosture? Posture { get; set; }

        public float SequenceCycle(int sequence) => float.NaN;
        public string SequenceActivity(int sequence) => string.Empty;
        public int LookupPoseParameter(string name) => -1;
        public float SetPoseParameter(int index, float value) => value;
        public float GetPoseParameter(int index) => 0f;
        public int SelectWeightedSequence(string activity) => Sequences.TryGetValue(activity, out int s) ? s : -1;
        public float SequenceGroundSpeed(int sequence) => float.NaN;
        public float SequenceDuration(int sequence) => 1f;
        public bool SequenceLooping(int sequence) => true;
        public int CurrentSequence => -1;

        public void PlaySequence(int sequence, float playbackRate, bool restart) => Played.Add((sequence, playbackRate, restart));

        public void SetGestureLayer(int slot, int sequence, float weight, float playbackRate, float cycle, int order, bool restart)
            => LayerPushes.Add((slot, sequence));

        public void ClearGestureLayer(int slot) => LayerPushes.Add((slot, -1));
    }

    private sealed class FakePlayerData : ITFPlayerData
    {
        public Dictionary<int, TFPlayerNetState> Players { get; } = new();

        public TFPlayerNetState? Predicted(int playerIndex) => Players.GetValueOrDefault(playerIndex);
        public TFPlayerNetState? Networked(int playerIndex) => Players.GetValueOrDefault(playerIndex);
    }

    private sealed class FakeWeapons : ITFAnimWeapons
    {
        public Dictionary<int, TFAnimWeapon?> Active { get; } = new();

        /// <summary>The disguise weapon's answer: absent means the swap's seam-side gates
        /// (enemy view, a real disguise weapon, not the carried one) refused — stock's
        /// no-swap arms (tf_weaponbase.cpp:4214-4218) and the server side alike.</summary>
        public Dictionary<int, TFAnimWeapon?> Disguise { get; } = new();

        public TFAnimWeapon? ActiveWeapon(int playerIndex) => Active.GetValueOrDefault(playerIndex);

        public TFAnimWeapon? DisguiseWeapon(int playerIndex) => Disguise.GetValueOrDefault(playerIndex);
    }

    private sealed class Rig
    {
        public FakeTarget Target { get; } = new();
        public FakePlayerData Data { get; } = new();
        public FakeWeapons Weapons { get; } = new();
        public TFAnimLayer Layer { get; }
        public MultiplayerAnimState State { get; }

        public Rig(TFClassTable classes, bool hl2Only = false, TFAnimHookChain? hooks = null)
        {
            Layer = new TFAnimLayer(classes, Data, Weapons, hooks);
            State = new MultiplayerAnimState(Target, AnimStateParams.Hl2Defaults(),
                new AnimStateLayerChain(hl2Only
                    ? [new Hl2PostureLayer(Target)]
                    : [new Hl2PostureLayer(Target), Layer]));
        }
    }

    /// <summary>A hook that answers the main activity (or the gesture) with a fixed name when its slot/claim matches.</summary>
    private sealed class FakeAnimHook(string? mainAnswer = null, string? gestureAnswer = null,
                                      int? gestureForSlot = null, string? gestureForClaim = null,
                                      bool throws = false, int order = 999_310) : ITFAnimHook
    {
        public int HookOrder => order;
        public TFAnimHookContext? Asked { get; private set; }

        public string? OverrideMainActivity(in TFAnimHookContext context)
        {
            if (throws) throw new InvalidOperationException("main");
            Asked = context;
            return mainAnswer;
        }

        public string? OverrideGesture(in TFAnimHookContext context, int gestureSlot, string gestureActivity)
        {
            if (throws) throw new InvalidOperationException("gesture");
            if (gestureAnswer is null) return null;
            if (gestureForSlot is not null && gestureSlot != gestureForSlot) return null;
            if (gestureForClaim is not null && gestureActivity != gestureForClaim) return null;
            Asked = context;
            return gestureAnswer;
        }
    }

    private static Rig Running(TFClass playerClass, TFWeaponRole? role = null, TFWeaponId id = default,
                               TFCond[]? conds = null, TFClassTable? classes = null, bool hl2Only = false,
                               TFAnimHookChain? hooks = null)
    {
        var rig = new Rig(classes ?? TFMovementFixtures.Server(), hl2Only: hl2Only, hooks: hooks);
        var state = new TFPlayerNetState();
        state.BindLocal();   // a client-style standalone state: a buffer of its own (ClientObjectiveRig.cs:40)
        state.SetClass(playerClass);
        if (conds is not null)
        {
            var mask = new TFCondMask();
            foreach (var c in conds)
                mask.Set(c);
            state.SetConditions(mask);
        }
        rig.Data.Players[1] = state;
        if (role is not null)
            rig.Weapons.Active[1] = new TFAnimWeapon(role.Value, id);
        return rig;
    }

    /// <summary>One drive of the machine; the main activity it settled on.</summary>
    private static string Answered(Rig rig, Action<FakeTarget> setup, float now = 1f)
    {
        setup(rig.Target);
        rig.State.Update(0f, 0f, now, 1 / 64f);
        return rig.State.CurrentMainActivity;
    }

    /// <summary>The gait names this fake model wears (the facts read the answer's NAME, which never needs a resolved sequence).</summary>
    private static void Modelled(FakeTarget t, params string[] activities)
    {
        int seq = 1;
        foreach (var a in activities)
            t.Sequences.TryAdd(a, seq++);
    }

    /// <summary>The role's translated crouchwalk — HandleDucking's duck-cancel
    /// gate query (tf_playeranimstate.cpp:1344), the weapon table's replacement
    /// for ACT_MP_CROUCHWALK (tf_weaponbase.cpp:3670, 3730, 3883).</summary>
    private static string CrouchwalkRow(TFWeaponRole role) => role switch
    {
        TFWeaponRole.Primary => "ACT_MP_CROUCHWALK_PRIMARY",
        TFWeaponRole.Secondary => "ACT_MP_CROUCHWALK_SECONDARY",
        _ => "ACT_MP_CROUCHWALK_MELEE",
    };

    private static TFClassTable TableWithOverride(string classScriptName, string kvBody)
    {
        var files = TFClassTableTests.Files.Shipped();
        files.Bytes[$"scripts/playerclasses/{classScriptName}.txt"] = System.Text.Encoding.UTF8.GetBytes(kvBody);
        return new TFClassTable(new RecordingLog(), files);
    }

    //-------------------------------------------------------------------------
    // The layer's shape: a both-side IAnimStateLayer ordered under the HL2 one.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheLayerIsABothSideMainSequenceLayerOrderedUnderTheHl2Layer()
    {
        // A mod's anim layer is a PLAIN-marked container service, found by
        // type (AnimStateLayers.cs:20-27): AnimStateLayers reaches it through
        // ResolveAll<IAnimStateLayer>'s concrete-type scan, so the marks
        // carry no As list — the interface it implements is the claim.
        var services = typeof(TFAnimLayer).GetCustomAttributes<SideServiceAttribute>().ToArray();
        Assert.Contains(services, a => a.Side == Side.Server);
        Assert.Contains(services, a => a.Side == Side.Client);
        Assert.True(typeof(IAnimStateLayer).IsAssignableFrom(typeof(TFAnimLayer)));
        Assert.Equal(310, new TFAnimLayer(TFMovementFixtures.Server(), new FakePlayerData(), new FakeWeapons()).LayerOrder);
        Assert.True(310 < Hl2PostureLayer.LayerOrderDefault,
            "it answers before the HL2 layer (Hl2PostureLayer.LayerOrderDefault = " + Hl2PostureLayer.LayerOrderDefault + ")");
    }

    [Fact]
    public void APlayerWithNoCarrierKeepsTheHl2Answer()
    {
        // Stock has no TF2 player without a CTFPlayer; this build's honest
        // answer for a target whose carrier has not arrived is "not mine" —
        // the HL2 layer's answer stands untouched.
        var rig = new Rig(TFMovementFixtures.Server());   // no carrier registered
        string tf2Machine = Answered(rig, t => t.VelocityField = new Vector(300f, 0f, 0f));

        var hl2 = Running(TFClass.Soldier, hl2Only: true);
        string reference = Answered(hl2, t => t.VelocityField = new Vector(300f, 0f, 0f));

        Assert.Equal(reference, tf2Machine);
    }

    [Fact]
    public void ATargetWithoutAnIndexKeepsTheHl2Answer()
    {
        var rig = Running(TFClass.Soldier, TFWeaponRole.Primary);
        rig.Target.EntityIndexValue = -1;
        string tf2Machine = Answered(rig, t => t.VelocityField = new Vector(300f, 0f, 0f));

        var hl2 = Running(TFClass.Soldier, hl2Only: true);
        string reference = Answered(hl2, t => t.VelocityField = new Vector(300f, 0f, 0f));

        Assert.Equal(reference, tf2Machine);
    }

    //-------------------------------------------------------------------------
    // The weapon table: the gait is the active weapon's role's answer, and
    // the class does not enter the translation at all (s_acttable* are keyed
    // by role alone — GetActivityWeaponRole tf_weaponbase.cpp:4185 and the
    // ActivityList switch :4208; its default arm, like GRENADE/HEAD/MISC/
    // PASSTIME, is the Primary table).
    //-------------------------------------------------------------------------

    public static TheoryData<TFClass, TFWeaponRole, string, string, string> GaitRows => new()
    {
        // class,      role,      state,        expected,                      stock row (tf_weaponbase.cpp)
        { TFClass.Scout,   TFWeaponRole.Primary,   "stand",      "ACT_MP_STAND_PRIMARY",        "3662" },
        { TFClass.Scout,   TFWeaponRole.Primary,   "run",        "ACT_MP_RUN_PRIMARY",          "3667" },
        { TFClass.Scout,   TFWeaponRole.Primary,   "crouch",     "ACT_MP_CROUCH_PRIMARY",       "3663" },
        { TFClass.Scout,   TFWeaponRole.Primary,   "crouchwalk", "ACT_MP_CROUCHWALK_PRIMARY",   "3670" },
        { TFClass.Scout,   TFWeaponRole.Secondary, "stand",      "ACT_MP_STAND_SECONDARY",      "3725" },
        { TFClass.Scout,   TFWeaponRole.Secondary, "run",        "ACT_MP_RUN_SECONDARY",        "3727" },
        { TFClass.Scout,   TFWeaponRole.Secondary, "crouch",     "ACT_MP_CROUCH_SECONDARY",     "3726" },
        { TFClass.Scout,   TFWeaponRole.Secondary, "crouchwalk", "ACT_MP_CROUCHWALK_SECONDARY", "3730" },
        { TFClass.Scout,   TFWeaponRole.Melee,     "stand",      "ACT_MP_STAND_MELEE",          "3878" },
        { TFClass.Scout,   TFWeaponRole.Melee,     "run",        "ACT_MP_RUN_MELEE",            "3880" },
        { TFClass.Scout,   TFWeaponRole.Melee,     "crouch",     "ACT_MP_CROUCH_MELEE",         "3879" },
        { TFClass.Scout,   TFWeaponRole.Melee,     "crouchwalk", "ACT_MP_CROUCHWALK_MELEE",     "3883" },
        { TFClass.Soldier, TFWeaponRole.Primary,   "stand",      "ACT_MP_STAND_PRIMARY",        "3662" },
        { TFClass.Soldier, TFWeaponRole.Primary,   "run",        "ACT_MP_RUN_PRIMARY",          "3667" },
        { TFClass.Soldier, TFWeaponRole.Primary,   "crouch",     "ACT_MP_CROUCH_PRIMARY",       "3663" },
        { TFClass.Soldier, TFWeaponRole.Primary,   "crouchwalk", "ACT_MP_CROUCHWALK_PRIMARY",   "3670" },
        { TFClass.Soldier, TFWeaponRole.Secondary, "stand",      "ACT_MP_STAND_SECONDARY",      "3725" },
        { TFClass.Soldier, TFWeaponRole.Secondary, "run",        "ACT_MP_RUN_SECONDARY",        "3727" },
        { TFClass.Soldier, TFWeaponRole.Secondary, "crouch",     "ACT_MP_CROUCH_SECONDARY",     "3726" },
        { TFClass.Soldier, TFWeaponRole.Secondary, "crouchwalk", "ACT_MP_CROUCHWALK_SECONDARY", "3730" },
        { TFClass.Soldier, TFWeaponRole.Melee,     "stand",      "ACT_MP_STAND_MELEE",          "3878" },
        { TFClass.Soldier, TFWeaponRole.Melee,     "run",        "ACT_MP_RUN_MELEE",            "3880" },
        { TFClass.Soldier, TFWeaponRole.Melee,     "crouch",     "ACT_MP_CROUCH_MELEE",         "3879" },
        { TFClass.Soldier, TFWeaponRole.Melee,     "crouchwalk", "ACT_MP_CROUCHWALK_MELEE",     "3883" },
        { TFClass.HeavyWeapons, TFWeaponRole.Primary,   "stand",      "ACT_MP_STAND_PRIMARY",        "3662" },
        { TFClass.HeavyWeapons, TFWeaponRole.Primary,   "run",        "ACT_MP_RUN_PRIMARY",          "3667" },
        { TFClass.HeavyWeapons, TFWeaponRole.Primary,   "crouch",     "ACT_MP_CROUCH_PRIMARY",       "3663" },
        { TFClass.HeavyWeapons, TFWeaponRole.Primary,   "crouchwalk", "ACT_MP_CROUCHWALK_PRIMARY",   "3670" },
        { TFClass.HeavyWeapons, TFWeaponRole.Secondary, "stand",      "ACT_MP_STAND_SECONDARY",      "3725" },
        { TFClass.HeavyWeapons, TFWeaponRole.Secondary, "run",        "ACT_MP_RUN_SECONDARY",        "3727" },
        { TFClass.HeavyWeapons, TFWeaponRole.Secondary, "crouch",     "ACT_MP_CROUCH_SECONDARY",     "3726" },
        { TFClass.HeavyWeapons, TFWeaponRole.Secondary, "crouchwalk", "ACT_MP_CROUCHWALK_SECONDARY", "3730" },
        { TFClass.HeavyWeapons, TFWeaponRole.Melee,     "stand",      "ACT_MP_STAND_MELEE",          "3878" },
        { TFClass.HeavyWeapons, TFWeaponRole.Melee,     "run",        "ACT_MP_RUN_MELEE",            "3880" },
        { TFClass.HeavyWeapons, TFWeaponRole.Melee,     "crouch",     "ACT_MP_CROUCH_MELEE",         "3879" },
        { TFClass.HeavyWeapons, TFWeaponRole.Melee,     "crouchwalk", "ACT_MP_CROUCHWALK_MELEE",     "3883" },
    };

    [Theory]
    [MemberData(nameof(GaitRows))]
    public void TheGaitIsTheActiveWeaponsTableAnswer(TFClass playerClass, TFWeaponRole role, string state, string expected, string stockLine)
    {
        _ = stockLine;   // the cited row of tf_weaponbase.cpp the expected name was read from
        var rig = Running(playerClass, role);
        // The translated crouchwalk rides along: HandleDucking's duck-cancel
        // gate (tf_playeranimstate.cpp:1344) asks the model for it before it
        // honours the duck — with it unmodelled the crouch rows would answer
        // the gait handler instead (TheDuckCancelsWhenTheModelHasNoCrouchwalk
        // proves the gate from the other side).
        Modelled(rig.Target, expected, CrouchwalkRow(role));

        string answer = state switch
        {
            "stand" => Answered(rig, _ => { }),
            "run" => Answered(rig, t => t.VelocityField = new Vector(300f, 0f, 0f)),
            "crouch" => Answered(rig, t => t.DuckedValue = true),
            "crouchwalk" => Answered(rig, t => { t.DuckedValue = true; t.VelocityField = new Vector(300f, 0f, 0f); }),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

        Assert.Equal(expected, answer);
    }

    [Fact]
    public void AWeaponWithNoRoleTranslatesToNothing()
    {
        // The seam answers null for an empty active-weapon read (the weapon
        // entities are not in the core yet): then no table applies and the
        // base name stands — stock's no-weapon arm (no table consulted).
        var rig = Running(TFClass.Soldier);   // no weapon behind the seam
        Assert.Equal("ACT_MP_STAND_IDLE", Answered(rig, _ => { }));
        Assert.Equal("ACT_MP_RUN", Answered(rig, t => t.VelocityField = new Vector(300f, 0f, 0f)));
    }

    //-------------------------------------------------------------------------
    // HandleSwimming (tf_playeranimstate.cpp:1265-1290): the base claims at
    // waist-deep; IsAiming with a minigun — or a ZOOMED sniper rifle —
    // becomes ACT_MP_SWIM_DEPLOYED (the *_PRIMARY row, tf_weaponbase.cpp:3676;
    // no other table has a SWIM_DEPLOYED row).
    //-------------------------------------------------------------------------

    [Fact]
    public void WaistDeepIsTheSwimAnswer()
    {
        var rig = Running(TFClass.Scout, TFWeaponRole.Primary);
        Modelled(rig.Target, "ACT_MP_SWIM_PRIMARY");

        Assert.Equal("ACT_MP_SWIM_PRIMARY", Answered(rig, t => t.WaterLevelValue = MultiplayerAnimState.WaterLevelWaist));
    }

    [Fact]
    public void TheDeployedMinigunSwimsDeployed()
    {
        var rig = Running(TFClass.HeavyWeapons, TFWeaponRole.Primary, TFWeaponId.Minigun, [TFCond.Aiming]);
        Modelled(rig.Target, "ACT_MP_SWIM_PRIMARY", "ACT_MP_SWIM_DEPLOYED_PRIMARY");

        Assert.Equal("ACT_MP_SWIM_DEPLOYED_PRIMARY",
            Answered(rig, t => t.WaterLevelValue = MultiplayerAnimState.WaterLevelWaist));   // :1274-1277
    }

    [Fact]
    public void TheSniperDeploysInWaterOnlyZoomed()
    {
        // :1279-1285 — WeaponID_IsSniperRifle and TF_COND_ZOOMED both required.
        var aiming = Running(TFClass.Sniper, TFWeaponRole.Primary, TFWeaponId.Sniperrifle, [TFCond.Aiming]);
        Modelled(aiming.Target, "ACT_MP_SWIM_PRIMARY", "ACT_MP_SWIM_DEPLOYED_PRIMARY");
        Assert.Equal("ACT_MP_SWIM_PRIMARY", Answered(aiming, t => t.WaterLevelValue = MultiplayerAnimState.WaterLevelWaist));

        var zoomed = Running(TFClass.Sniper, TFWeaponRole.Primary, TFWeaponId.Sniperrifle, [TFCond.Aiming, TFCond.Zoomed]);
        Modelled(zoomed.Target, "ACT_MP_SWIM_PRIMARY", "ACT_MP_SWIM_DEPLOYED_PRIMARY");
        Assert.Equal("ACT_MP_SWIM_DEPLOYED_PRIMARY", Answered(zoomed, t => t.WaterLevelValue = MultiplayerAnimState.WaterLevelWaist));
    }

    //-------------------------------------------------------------------------
    // HandleJumping (tf_playeranimstate.cpp:1427-1538).
    //-------------------------------------------------------------------------

    [Fact]
    public void TheNewJumpTimesStartAndFloatFromTheStamp()
    {
        // :1482 bNewJump = !m_bDontDoNewJump; :1515-1524 — ACT_MP_JUMP_START
        // until 0.5 s after m_flJumpStartTime, then ACT_MP_JUMP_FLOAT. (The
        // stamp is the core's DoAnimationEvent(Jump) arm, from stock
        // multiplayer_animstate.cpp:293 — JumpStartTime is what the TF
        // handler reads, tf_playeranimstate.cpp:1498,1517.)
        var rig = Running(TFClass.Scout, TFWeaponRole.Primary);
        Modelled(rig.Target, "ACT_MP_JUMP_START_PRIMARY", "ACT_MP_JUMP_FLOAT_PRIMARY", "ACT_MP_JUMP_PRIMARY");
        rig.Target.OnGroundValue = false;
        rig.State.Update(0f, 0f, 1f, 1 / 64f);
        rig.State.DoAnimationEvent(PlayerAnimEvent.Jump);
        rig.State.Update(0f, 0f, 1.3f, 1 / 64f);
        Assert.Equal("ACT_MP_JUMP_START_PRIMARY", rig.State.CurrentMainActivity);

        rig.State.Update(0f, 0f, 1.6f, 1 / 64f);
        Assert.Equal("ACT_MP_JUMP_FLOAT_PRIMARY", rig.State.CurrentMainActivity);
    }

    [Fact]
    public void TheOldJumpIsPlainEvenPastTheFloatTime()
    {
        // :1526-1529 — m_bDontDoNewJump classes take plain ACT_MP_JUMP
        // (row 3671) whenever another class would be floating.
        var classes = TableWithOverride("soldier", """PlayerClass { "speed_max" "240" "health_max" "200" "DontDoNewJump" "1" }""");
        var rig = Running(TFClass.Soldier, TFWeaponRole.Primary, classes: classes);
        Modelled(rig.Target, "ACT_MP_JUMP_PRIMARY");
        rig.Target.OnGroundValue = false;
        rig.State.Update(0f, 0f, 1f, 1 / 64f);
        rig.State.DoAnimationEvent(PlayerAnimEvent.Jump);
        rig.State.Update(0f, 0f, 1.6f, 1 / 64f);

        Assert.Equal("ACT_MP_JUMP_PRIMARY", rig.State.CurrentMainActivity);
    }

    [Fact]
    public void AHeavyThatIsAimingIsNotGivenTheJump()
    {
        // :1439-1440 — heavy + TF_COND_AIMING: HandleJumping refuses; the
        // gait handler then answers, and its deployed-idle stands (:1320 —
        // no DEPLOYED_IDLE row in the Primary table, so the base name
        // itself; contrast Item1's row 3946).
        var rig = Running(TFClass.HeavyWeapons, TFWeaponRole.Primary, conds: [TFCond.Aiming]);
        Modelled(rig.Target, "ACT_MP_DEPLOYED_IDLE");
        rig.Target.OnGroundValue = false;
        rig.State.Update(0f, 0f, 1f, 1 / 64f);
        rig.State.DoAnimationEvent(PlayerAnimEvent.Jump);
        rig.State.Update(0f, 0f, 1.1f, 1 / 64f);

        Assert.Equal("ACT_MP_DEPLOYED_IDLE", rig.State.CurrentMainActivity);
    }

    [Fact]
    public void TheAirwalkNeedsTheClassFlagAndEndsOnTheGround()
    {
        // :1444-1473: airborne with velocity z > 300 the scout airwalks
        // (ACT_MP_AIRWALK, row 3669); back on the ground with the flag set
        // it clears, the main sequence and the JUMP-slot land gesture are
        // restarted (:1449-1454), and the gait handler answers. The land
        // gesture goes through TF's RestartGesture override (:958-963),
        // which translates the activity first — so the slot plays
        // ACT_MP_JUMP_LAND_PRIMARY (row 3674), not the base name.
        var rig = Running(TFClass.Scout, TFWeaponRole.Primary);
        Modelled(rig.Target, "ACT_MP_AIRWALK_PRIMARY", "ACT_MP_JUMP_LAND_PRIMARY", "ACT_MP_STAND_PRIMARY");
        rig.Target.OnGroundValue = false;
        rig.Target.VelocityField = new Vector(0f, 0f, 400f);

        Assert.Equal("ACT_MP_AIRWALK_PRIMARY", Answered(rig, _ => { }, now: 1f));

        rig.Target.OnGroundValue = true;
        Assert.Equal("ACT_MP_STAND_PRIMARY", Answered(rig, _ => { }, now: 1.1f));
        rig.State.Update(0f, 0f, 1.1f, 1 / 64f);   // flush a gesture-restart the machine may defer to the drive's tail

        Assert.Contains(rig.Target.LayerPushes, p => p.Slot == GestureSlot.Jump
            && p.Seq == rig.Target.Sequences["ACT_MP_JUMP_LAND_PRIMARY"]);   // :1453 + the :958-963 translate
    }

    [Fact]
    public void ADontDoAirwalkClassNeverAirwalks()
    {
        // :1444 — the whole airwalk branch is gated on m_bDontDoAirwalk
        // being false; with the flag this class simply takes the gait
        // handler's answer instead.
        var classes = TableWithOverride("scout", """PlayerClass { "speed_max" "400" "health_max" "125" "DontDoAirwalk" "1" }""");
        var rig = Running(TFClass.Scout, TFWeaponRole.Primary, classes: classes);
        Modelled(rig.Target, "ACT_MP_AIRWALK_PRIMARY", "ACT_MP_STAND_PRIMARY");
        rig.Target.OnGroundValue = false;
        rig.Target.VelocityField = new Vector(0f, 0f, 400f);

        Assert.Equal("ACT_MP_STAND_PRIMARY", Answered(rig, _ => { }));
    }

    //-------------------------------------------------------------------------
    // HandleDucking (tf_playeranimstate.cpp:1341-1392). The IsLoser
    // exceptions are deferred (no IsLoser in the net state).
    //-------------------------------------------------------------------------

    [Fact]
    public void TheDuckCancelsWhenTheModelHasNoCrouchwalk()
    {
        // :1343-1347 — FL_DUCKING but ACT_MP_CROUCHWALK (translated, so
        // CROUCHWALK_PRIMARY) not on the model: the duck is cancelled and
        // the gait handler answers RUN (row 3667) — where the HL2 machine,
        // same target, says CROUCHWALK. The two answers DIFFERING is the
        // proof the cancellation ran.
        var rig = Running(TFClass.Soldier, TFWeaponRole.Primary);   // nothing modelled: the translated crouchwalk is absent
        string withLayer = Answered(rig, t => { t.DuckedValue = true; t.VelocityField = new Vector(300f, 0f, 0f); });
        Assert.Equal("ACT_MP_RUN_PRIMARY", withLayer);

        var hl2 = Running(TFClass.Soldier, hl2Only: true);
        string reference = Answered(hl2, t => { t.DuckedValue = true; t.VelocityField = new Vector(300f, 0f, 0f); });
        Assert.Equal("ACT_MP_CROUCHWALK", reference);
        Assert.NotEqual(reference, withLayer);
    }

    [Fact]
    public void DuckingAndMovingCrouchwalksThroughTheTable()
    {
        var rig = Running(TFClass.Scout, TFWeaponRole.Secondary);
        Modelled(rig.Target, "ACT_MP_CROUCHWALK_SECONDARY");

        Assert.Equal("ACT_MP_CROUCHWALK_SECONDARY",
            Answered(rig, t => { t.DuckedValue = true; t.VelocityField = new Vector(300f, 0f, 0f); }));   // :1367
    }

    [Fact]
    public void DuckingAndMovingWithAnAirDashDoubleJumps()
    {
        // :1361-1363 — GetAirDash() > 0: ACT_MP_DOUBLEJUMP_CROUCH (row 3677).
        var rig = Running(TFClass.Scout, TFWeaponRole.Primary);
        rig.Data.Players[1].AirDash = 1;
        Modelled(rig.Target, "ACT_MP_DOUBLEJUMP_CROUCH_PRIMARY", "ACT_MP_CROUCHWALK_PRIMARY");

        Assert.Equal("ACT_MP_DOUBLEJUMP_CROUCH_PRIMARY",
            Answered(rig, t => { t.DuckedValue = true; t.VelocityField = new Vector(300f, 0f, 0f); }));
    }

    [Fact]
    public void DuckingAndMovingWhileAimingWearsTheCrouchwalkDeployedQuirk()
    {
        // :1370-1384 — ACT_MP_CROUCH_DEPLOYED through the Primary table lands
        // on ACT_MP_CROUCHWALK_DEPLOYED (tf_weaponbase.cpp:3665 — stock's own
        // quirk: the table's replacement, not the constant).
        var rig = Running(TFClass.Scout, TFWeaponRole.Primary, conds: [TFCond.Aiming]);
        Modelled(rig.Target, "ACT_MP_CROUCHWALK_DEPLOYED", "ACT_MP_CROUCHWALK_PRIMARY");   // the second: the :1344 gate row

        Assert.Equal("ACT_MP_CROUCHWALK_DEPLOYED",
            Answered(rig, t => { t.DuckedValue = true; t.VelocityField = new Vector(300f, 0f, 0f); }));
    }

    [Fact]
    public void TheMinigunNeverDeployCrouchwalks()
    {
        // :1372-1381 — the active weapon being the minigun is the one
        // exception: no CROUCH_DEPLOYED, the crouchwalk stands.
        var rig = Running(TFClass.HeavyWeapons, TFWeaponRole.Primary, TFWeaponId.Minigun, [TFCond.Aiming]);
        Modelled(rig.Target, "ACT_MP_CROUCHWALK_DEPLOYED", "ACT_MP_CROUCHWALK_PRIMARY");

        Assert.Equal("ACT_MP_CROUCHWALK_PRIMARY",
            Answered(rig, t => { t.DuckedValue = true; t.VelocityField = new Vector(300f, 0f, 0f); }));
    }

    [Fact]
    public void DuckingStillWhileAimingHoldsTheDeployedIdle()
    {
        // :1353-1357 — still (or loser): ACT_MP_CROUCH_IDLE, and
        // CROUCH_DEPLOYED_IDLE when aiming; the Primary table maps it to
        // itself (row 3666 — the identity row is what pins the name).
        var rig = Running(TFClass.Scout, TFWeaponRole.Primary, conds: [TFCond.Aiming]);
        Modelled(rig.Target, "ACT_MP_CROUCH_DEPLOYED_IDLE", "ACT_MP_CROUCHWALK_PRIMARY");   // the second: the :1344 gate row

        Assert.Equal("ACT_MP_CROUCH_DEPLOYED_IDLE", Answered(rig, t => t.DuckedValue = true));
    }

    //-------------------------------------------------------------------------
    // HandleMoving (tf_playeranimstate.cpp:1297-1334) and IsAiming
    // (tf_player_shared.cpp:11448-11460).
    //-------------------------------------------------------------------------

    [Fact]
    public void MovingWhileAimingDeploysAndStandingWhileAimingIsDeployedIdle()
    {
        var run = Running(TFClass.Scout, TFWeaponRole.Primary, conds: [TFCond.Aiming]);
        Modelled(run.Target, "ACT_MP_DEPLOYED_PRIMARY");
        Assert.Equal("ACT_MP_DEPLOYED_PRIMARY", Answered(run, t => t.VelocityField = new Vector(300f, 0f, 0f)));   // :1316, row 3664

        var still = Running(TFClass.Scout, TFWeaponRole.Primary, conds: [TFCond.Aiming]);
        Modelled(still.Target, "ACT_MP_DEPLOYED_IDLE");
        Assert.Equal("ACT_MP_DEPLOYED_IDLE", Answered(still, _ => { }));   // :1320; the Primary table has no DEPLOYED_IDLE row
    }

    [Fact]
    public void TheSoldierIsNeverAiming()
    {
        // IsAiming, tf_player_shared.cpp:11448-11450 — InCond(AIMING) &&
        // !IsPlayerClass(SOLDIER): the soldier's cond is not aiming; were it,
        // the deployed arm would answer ACT_MP_DEPLOYED_PRIMARY (row 3664).
        var rig = Running(TFClass.Soldier, TFWeaponRole.Primary, conds: [TFCond.Aiming]);
        Modelled(rig.Target, "ACT_MP_RUN_PRIMARY", "ACT_MP_DEPLOYED_PRIMARY");

        Assert.Equal("ACT_MP_RUN_PRIMARY", Answered(rig, t => t.VelocityField = new Vector(300f, 0f, 0f)));
    }

    [Fact]
    public void TheSniperClassicGatesAimingOnTheZoom()
    {
        // tf_player_shared.cpp:11451-11459: Sniper with the CLASSIC rifle is
        // aiming only while TF_COND_ZOOMED; any other rifle keeps the cond.
        var classic = Running(TFClass.Sniper, TFWeaponRole.Primary, TFWeaponId.SniperrifleClassic, [TFCond.Aiming]);
        Modelled(classic.Target, "ACT_MP_RUN_PRIMARY", "ACT_MP_DEPLOYED_PRIMARY");
        Assert.Equal("ACT_MP_RUN_PRIMARY", Answered(classic, t => t.VelocityField = new Vector(300f, 0f, 0f)));

        var zoomed = Running(TFClass.Sniper, TFWeaponRole.Primary, TFWeaponId.SniperrifleClassic, [TFCond.Aiming, TFCond.Zoomed]);
        Modelled(zoomed.Target, "ACT_MP_RUN_PRIMARY", "ACT_MP_DEPLOYED_PRIMARY");
        Assert.Equal("ACT_MP_DEPLOYED_PRIMARY", Answered(zoomed, t => t.VelocityField = new Vector(300f, 0f, 0f)));

        var decap = Running(TFClass.Sniper, TFWeaponRole.Primary, TFWeaponId.SniperrifleDecap, [TFCond.Aiming]);
        Modelled(decap.Target, "ACT_MP_RUN_PRIMARY", "ACT_MP_DEPLOYED_PRIMARY");
        Assert.Equal("ACT_MP_DEPLOYED_PRIMARY", Answered(decap, t => t.VelocityField = new Vector(300f, 0f, 0f)));
    }

    //-------------------------------------------------------------------------
    // The condition tables of TranslateActivity (tf_playeranimstate.cpp:124-154,
    // ActivityOverride :223-269). The IsLoser and IsCarryingObject tables are
    // deferred (grep proof in the commit); the kart and competitive tables
    // have their conds on the net state.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheKartConditionReplacesTheGaitFirst()
    {
        // :157-168 + :231-235 — the kart table is tried first of all the
        // condition tables; its RUN row (:160) stands (no weapon table has
        // an ACT_KART_* row, so nothing re-translates it afterwards).
        var rig = Running(TFClass.Scout, TFWeaponRole.Primary, conds: [TFCond.HalloweenKart]);
        Modelled(rig.Target, "ACT_KART_IDLE");

        Assert.Equal("ACT_KART_IDLE", Answered(rig, t => t.VelocityField = new Vector(300f, 0f, 0f)));
    }

    [Fact]
    public void TheCompetitiveLoserStandBecomesTheLoserState()
    {
        // :186-189 (table) + :238-242 (gated by TF_COND_COMPETITIVE_LOSER):
        // only the STAND_IDLE row replaces; RUN is untouched by the table
        // and takes its Primary translation.
        var rig = Running(TFClass.Scout, TFWeaponRole.Primary, conds: [TFCond.CompetitiveLoser]);
        Modelled(rig.Target, "ACT_MP_COMPETITIVE_LOSERSTATE", "ACT_MP_RUN_PRIMARY");

        Assert.Equal("ACT_MP_COMPETITIVE_LOSERSTATE", Answered(rig, _ => { }));
        Assert.Equal("ACT_MP_RUN_PRIMARY", Answered(rig, t => t.VelocityField = new Vector(300f, 0f, 0f)));
    }

    [Fact]
    public void TheCompetitiveWinnerStandsOnTheWinnerState()
    {
        // :143-151 — AFTER the weapon table: a translated STAND_PRIMARY (any
        // class), a SPY's STAND_MELEE, or a DEMOMAN's STAND_SECONDARY
        // becomes ACT_MP_COMPETITIVE_WINNERSTATE; other stands do not.
        var scout = Running(TFClass.Scout, TFWeaponRole.Primary, conds: [TFCond.CompetitiveWinner]);
        Modelled(scout.Target, "ACT_MP_COMPETITIVE_WINNERSTATE");
        Assert.Equal("ACT_MP_COMPETITIVE_WINNERSTATE", Answered(scout, _ => { }));

        var spyMelee = Running(TFClass.Spy, TFWeaponRole.Melee, conds: [TFCond.CompetitiveWinner]);
        Modelled(spyMelee.Target, "ACT_MP_COMPETITIVE_WINNERSTATE");
        Assert.Equal("ACT_MP_COMPETITIVE_WINNERSTATE", Answered(spyMelee, _ => { }));

        var demoSecondary = Running(TFClass.Demoman, TFWeaponRole.Secondary, conds: [TFCond.CompetitiveWinner]);
        Modelled(demoSecondary.Target, "ACT_MP_COMPETITIVE_WINNERSTATE");
        Assert.Equal("ACT_MP_COMPETITIVE_WINNERSTATE", Answered(demoSecondary, _ => { }));

        var scoutSecondary = Running(TFClass.Scout, TFWeaponRole.Secondary, conds: [TFCond.CompetitiveWinner]);
        Modelled(scoutSecondary.Target, "ACT_MP_STAND_SECONDARY");
        Assert.Equal("ACT_MP_STAND_SECONDARY", Answered(scoutSecondary, _ => { }));
    }

    //-------------------------------------------------------------------------
    // The TF DoAnimationEvent switch (tf_playeranimstate.cpp:969-1259): the
    // gesture decisions. Each expected name is the weapon table's answer for
    // the rig's role (tf_weaponbase.cpp, the row cited), or the arm's own
    // name where no table keys it — the s_acttable* blocks carry no PREFIRE,
    // POSTFIRE, DEPLOYED_IDLE, DOUBLEJUMP, STUN, PASSTIME_THROW or
    // CYOA_PDA rows, so those reach the model as stock wrote them.
    //-------------------------------------------------------------------------

    /// <summary>The newest push into this slot, by NAME (the fake records (slot, sequence)).</summary>
    private static void AssertGesture(Rig rig, int slot, string activity)
    {
        Assert.True(rig.Target.Sequences.ContainsKey(activity), $"{activity} not modelled");
        // Seq > 0: the state's ctor empties all seven slots, and the fake
        // records those (slot, -1) clears alongside the real pushes.
        Assert.Equal(rig.Target.Sequences[activity],
            rig.Target.LayerPushes.Where(p => p.Slot == slot && p.Seq > 0).Last().Seq);
    }

    [Fact]
    public void TheFireReplacesTheReloadGestureOnTheAttackSlot()
    {
        // The reload trio (:1138-1176) and the generic fire arm (:1030-1036)
        // share GESTURE_SLOT_ATTACK_AND_RELOAD: the fire interrupts — the
        // slot ends on the fire's translated name (:3679); the reload's
        // (:3686) no longer stands.
        var rig = Running(TFClass.Soldier, TFWeaponRole.Primary);
        Modelled(rig.Target, "ACT_MP_RELOAD_STAND_PRIMARY", "ACT_MP_ATTACK_STAND_PRIMARY");

        rig.State.DoAnimationEvent(PlayerAnimEvent.Reload);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_RELOAD_STAND_PRIMARY");   // :3686

        rig.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_STAND_PRIMARY");   // :3679
    }

    [Fact]
    public void TheFireGateNeedsTheTranslatedCrouchwalk()
    {
        // The switch's gate (:971-975): the duck is honoured only when the
        // model wears the translated crouchwalk — without it the fire is the
        // stand one (:3679), with it the crouch one (:3681).
        var ungated = Running(TFClass.Soldier, TFWeaponRole.Primary);
        ungated.Target.DuckedValue = true;
        Modelled(ungated.Target, "ACT_MP_ATTACK_STAND_PRIMARY");
        ungated.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        AssertGesture(ungated, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_STAND_PRIMARY");

        var gated = Running(TFClass.Soldier, TFWeaponRole.Primary);
        gated.Target.DuckedValue = true;
        Modelled(gated.Target, "ACT_MP_ATTACK_CROUCH_PRIMARY", "ACT_MP_CROUCHWALK_PRIMARY");
        gated.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        AssertGesture(gated, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_CROUCH_PRIMARY");
    }

    [Fact]
    public void TheZoomedSniperFireIsDeployedAndHoldsThePose()
    {
        // :1013-1028 — sniper rifle (or bow) and ZOOMED: the deployed fire
        // (:3680), and the deployed pose is held (:1028) through the moving
        // handler's hold arm (:1323) until moving clears it (:1304).
        var rig = Running(TFClass.Sniper, TFWeaponRole.Primary, id: TFWeaponId.SniperrifleDecap,
                          conds: [TFCond.Zoomed]);
        Modelled(rig.Target, "ACT_MP_ATTACK_STAND_PRIMARY_DEPLOYED", "ACT_MP_DEPLOYED_IDLE", "ACT_MP_RUN_PRIMARY");

        rig.State.Update(0f, 0f, 1f, 1 / 64f);
        rig.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_STAND_PRIMARY_DEPLOYED");   // :1022 via :3680

        // Standing still inside the two-second window: the held deployed idle (:1323).
        Assert.Equal("ACT_MP_DEPLOYED_IDLE", Answered(rig, _ => { }, now: 1.5f));

        // Moving clears the hold early (:1304): the run is back.
        Assert.Equal("ACT_MP_RUN_PRIMARY", Answered(rig, t => t.VelocityField = new Vector(300f, 0f, 0f), now: 1.5f));
    }

    [Fact]
    public void TheZoomedBowAndTheZoomedCrouchFireAreDeployedToo()
    {
        // The bow joins the rifles (:989); the ducked zoomed fire is the
        // crouch deployed (:1018 via :3682); without ZOOMED the sniper is
        // the generic arm (:1030-1036).
        var bow = Running(TFClass.Sniper, TFWeaponRole.Primary, id: TFWeaponId.CompoundBow, conds: [TFCond.Zoomed]);
        Modelled(bow.Target, "ACT_MP_ATTACK_STAND_PRIMARY_DEPLOYED");
        bow.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        AssertGesture(bow, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_STAND_PRIMARY_DEPLOYED");

        var ducked = Running(TFClass.Sniper, TFWeaponRole.Primary, id: TFWeaponId.SniperrifleClassic, conds: [TFCond.Zoomed]);
        ducked.Target.DuckedValue = true;
        Modelled(ducked.Target, "ACT_MP_ATTACK_CROUCH_PRIMARY_DEPLOYED", "ACT_MP_CROUCHWALK_PRIMARY");
        ducked.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        AssertGesture(ducked, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_CROUCH_PRIMARY_DEPLOYED");   // :3682

        var unzoomed = Running(TFClass.Sniper, TFWeaponRole.Primary, id: TFWeaponId.SniperrifleDecap);
        Modelled(unzoomed.Target, "ACT_MP_ATTACK_STAND_PRIMARY");
        unzoomed.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        AssertGesture(unzoomed, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_STAND_PRIMARY");   // :3679
    }

    [Fact]
    public void TheMinigunFirePlaysOnlyWhileItsGestureIsNotAlreadyPlaying()
    {
        // Stock's guard (:1008-1011): the minigun's fire gesture restarts
        // only when the slot is not already playing it — a second fire is
        // silent. The generic arm (:1034) has no guard: its second fire
        // re-pushes through the core's restart path.
        var minigun = Running(TFClass.HeavyWeapons, TFWeaponRole.Primary, id: TFWeaponId.Minigun);
        Modelled(minigun.Target, "ACT_MP_ATTACK_STAND_PRIMARY");
        minigun.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        minigun.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        Assert.Single(minigun.Target.LayerPushes, p => p.Slot == (int)GestureSlot.AttackAndReload && p.Seq > 0);   // :1008-1011

        var generic = Running(TFClass.Soldier, TFWeaponRole.Primary);
        Modelled(generic.Target, "ACT_MP_ATTACK_STAND_PRIMARY");
        generic.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        generic.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        Assert.Equal(2, generic.Target.LayerPushes.Count(p => p.Slot == (int)GestureSlot.AttackAndReload && p.Seq > 0));   // :1034 unguarded
    }

    [Fact]
    public void TheReloadTrioWearsTheWeaponsTableRows()
    {
        // :1138-1176 — the base picks (duck first, then swim, then stand
        // :1147) and the weapon table translates. The PRIMARY-suffixed names
        // exist nowhere in the core's switch: without the TF translate the
        // push would carry the base names instead.
        var rig = Running(TFClass.Soldier, TFWeaponRole.Primary);
        Modelled(rig.Target, "ACT_MP_RELOAD_STAND_PRIMARY", "ACT_MP_RELOAD_STAND_PRIMARY_LOOP",
                            "ACT_MP_RELOAD_STAND_PRIMARY_END");
        rig.State.DoAnimationEvent(PlayerAnimEvent.Reload);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_RELOAD_STAND_PRIMARY");       // :3686
        rig.State.DoAnimationEvent(PlayerAnimEvent.ReloadLoop);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_RELOAD_STAND_PRIMARY_LOOP");  // :3687
        rig.State.DoAnimationEvent(PlayerAnimEvent.ReloadEnd);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_RELOAD_STAND_PRIMARY_END");   // :3688

        var ducked = Running(TFClass.Soldier, TFWeaponRole.Primary);
        ducked.Target.DuckedValue = true;   // the base arm reads the raw duck flag, no gate
        Modelled(ducked.Target, "ACT_MP_RELOAD_CROUCH_PRIMARY");
        ducked.State.DoAnimationEvent(PlayerAnimEvent.Reload);
        AssertGesture(ducked, (int)GestureSlot.AttackAndReload, "ACT_MP_RELOAD_CROUCH_PRIMARY");   // :3689
    }

    [Fact]
    public void TheAirwalkReloadTrioTakesTheAirwalkRows()
    {
        // :1138-1176 — m_bInAirWalk (raised here by the airwalk claim,
        // HandleJumping :1461-1473) swaps the trio for its airwalk names
        // (:3695-3697).
        var rig = Running(TFClass.Scout, TFWeaponRole.Primary);
        Modelled(rig.Target, "ACT_MP_AIRWALK_PRIMARY",
                            "ACT_MP_RELOAD_AIRWALK_PRIMARY",
                            "ACT_MP_RELOAD_AIRWALK_PRIMARY_LOOP",
                            "ACT_MP_RELOAD_AIRWALK_PRIMARY_END");
        rig.Target.OnGroundValue = false;
        rig.Target.VelocityField = new Vector(0f, 0f, 400f);
        rig.State.Update(0f, 0f, 1f, 1 / 64f);   // the airwalk frame sets m_bInAirWalk (:1470-1473)

        rig.State.DoAnimationEvent(PlayerAnimEvent.Reload);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_RELOAD_AIRWALK_PRIMARY");       // :3695
        rig.State.DoAnimationEvent(PlayerAnimEvent.ReloadLoop);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_RELOAD_AIRWALK_PRIMARY_LOOP");  // :3696
        rig.State.DoAnimationEvent(PlayerAnimEvent.ReloadEnd);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_RELOAD_AIRWALK_PRIMARY_END");   // :3697
    }

    [Fact]
    public void TheSuperFireWearsThePrimary2SelfRows()
    {
        // :1041-1051 (duck over stand, swim wins over both :1046); the
        // Primary2 table keys the super trio and answers it with itself
        // (:3797-3799) — self-rows, so the pushed name reads as the raw one
        // and the table's own lines are the subject.
        var rig = Running(TFClass.Pyro, TFWeaponRole.Primary2);
        Modelled(rig.Target, "ACT_MP_ATTACK_STAND_PRIMARY_SUPER");
        rig.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimarySuper);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_STAND_PRIMARY_SUPER");   // :1043 via :3797

        var swim = Running(TFClass.Pyro, TFWeaponRole.Primary2);
        Modelled(swim.Target, "ACT_MP_ATTACK_SWIM_PRIMARY_SUPER");
        swim.Target.WaterLevelValue = MultiplayerAnimState.WaterLevelWaist;
        swim.State.Update(0f, 0f, 1f, 1 / 64f);   // the swim frame raises m_bInSwim
        swim.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimarySuper);
        AssertGesture(swim, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_SWIM_PRIMARY_SUPER");   // :1046 via :3799

        var ducked = Running(TFClass.Pyro, TFWeaponRole.Primary2);
        ducked.Target.DuckedValue = true;
        Modelled(ducked.Target, "ACT_MP_ATTACK_CROUCH_PRIMARY_SUPER", "ACT_MP_CROUCHWALK_PRIMARY");
        ducked.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimarySuper);
        AssertGesture(ducked, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_CROUCH_PRIMARY_SUPER");   // :1043 via :3798
    }

    [Fact]
    public void TheSecondaryFirePassesThroughAndDucksIntoTheMeleeRow()
    {
        // :1061-1071. The Secondary table keys no SECONDARYFIRE name at all
        // — its attack rows (:3738-3741) key the PRIMARYFIRE names — so the
        // plain and swim secondary fires reach the model raw; the Melee
        // table does key the crouch one (:3897), so under a melee weapon the
        // ducked secondary fire is ACT_MP_ATTACK_CROUCH_MELEE_SECONDARY.
        var rig = Running(TFClass.Soldier, TFWeaponRole.Secondary);
        Modelled(rig.Target, "ACT_MP_ATTACK_STAND_SECONDARYFIRE");
        rig.State.DoAnimationEvent(PlayerAnimEvent.AttackSecondary);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_STAND_SECONDARYFIRE");   // :1063, no row

        var melee = Running(TFClass.Soldier, TFWeaponRole.Melee);
        melee.Target.DuckedValue = true;
        Modelled(melee.Target, "ACT_MP_ATTACK_CROUCH_MELEE_SECONDARY", "ACT_MP_CROUCHWALK_MELEE");
        melee.State.DoAnimationEvent(PlayerAnimEvent.AttackSecondary);
        AssertGesture(melee, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_CROUCH_MELEE_SECONDARY");   // :1063 via :3897

        var swimming = Running(TFClass.Soldier, TFWeaponRole.Secondary);
        swimming.Target.WaterLevelValue = MultiplayerAnimState.WaterLevelWaist;   // the raw water level, not the flag (:1064)
        Modelled(swimming.Target, "ACT_MP_ATTACK_SWIM_SECONDARYFIRE");
        swimming.State.DoAnimationEvent(PlayerAnimEvent.AttackSecondary);
        AssertGesture(swimming, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_SWIM_SECONDARYFIRE");   // :1066, no row
    }

    [Fact]
    public void ThePreAndPostFireWindupsTakeTheWaterArmsForTheMinigun()
    {
        // :1073-1136 — the stand windup and winddown, and the minigun's
        // swim variants. No table keys PREFIRE or POSTFIRE anywhere, so the
        // names reach the model as stock wrote them. (Stock's autoKill
        // distinction — the minigun's prefire is auto-killed :1082-1086 —
        // is set through the core's RestartGesture parameter.)
        var rig = Running(TFClass.HeavyWeapons, TFWeaponRole.Primary, id: TFWeaponId.Minigun);
        Modelled(rig.Target, "ACT_MP_ATTACK_STAND_PREFIRE", "ACT_MP_ATTACK_STAND_POSTFIRE",
                            "ACT_MP_ATTACK_SWIM_PREFIRE", "ACT_MP_ATTACK_SWIM_POSTFIRE");

        rig.State.DoAnimationEvent(PlayerAnimEvent.AttackPre);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_STAND_PREFIRE");     // :1101
        rig.State.DoAnimationEvent(PlayerAnimEvent.AttackPost);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_STAND_POSTFIRE");    // :1130

        rig.Target.WaterLevelValue = MultiplayerAnimState.WaterLevelWaist;
        rig.State.Update(0f, 0f, 2f, 1 / 64f);   // the swim frame raises m_bInSwim
        rig.State.DoAnimationEvent(PlayerAnimEvent.AttackPre);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_SWIM_PREFIRE");      // :1091
        rig.State.DoAnimationEvent(PlayerAnimEvent.AttackPost);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_SWIM_POSTFIRE");     // :1120
    }

    [Fact]
    public void TheFlinchStandsAsideForTheCoresArm()
    {
        // Stock's TF switch has no flinch arm (:1241-1245: default falls to
        // the base), and the tables key the GENERIC flinch only (:3699), so
        // the directional chest name reaches the model unchanged under this
        // layer — the core's arm answers as stock's base did.
        var rig = Running(TFClass.Soldier, TFWeaponRole.Primary);
        Modelled(rig.Target, "ACT_MP_GESTURE_FLINCH_CHEST");
        rig.State.DoAnimationEvent(PlayerAnimEvent.FlinchChest);
        AssertGesture(rig, (int)GestureSlot.Flinch, "ACT_MP_GESTURE_FLINCH_CHEST");
    }

    [Fact]
    public void TheDoubleJumpStampsItsOwnJumpClockAndDashes()
    {
        // :1177-1204 — the dash starts the jump on the book's stamp and
        // plays the JUMP slot's dash gesture every time (:1195-1203); a
        // second dash does NOT restamp (:1184-1190 guard): the pose clock
        // keeps running from the first stamp. The dash gesture is raw — the
        // tables key DOUBLEJUMP_CROUCH only (:3677).
        var rig = Running(TFClass.Scout, TFWeaponRole.Primary);
        Modelled(rig.Target, "ACT_MP_DOUBLEJUMP", "ACT_MP_JUMP_START_PRIMARY", "ACT_MP_JUMP_FLOAT_PRIMARY");
        rig.Target.OnGroundValue = false;
        rig.State.Update(0f, 0f, 1f, 1 / 64f);

        rig.State.DoAnimationEvent(PlayerAnimEvent.DoubleJump);
        AssertGesture(rig, (int)GestureSlot.Jump, "ACT_MP_DOUBLEJUMP");   // :1202

        rig.State.Update(0f, 0f, 1.3f, 1 / 64f);
        Assert.Equal("ACT_MP_JUMP_START_PRIMARY", rig.State.CurrentMainActivity);   // :1521-1523 via :3672, 0.3 s on the stamp

        rig.State.Update(0f, 0f, 1.6f, 1 / 64f);
        Assert.Equal("ACT_MP_JUMP_FLOAT_PRIMARY", rig.State.CurrentMainActivity);   // :1517-1519 via :3673

        // The dash gesture plays EACH dash (:1195-1203), counted across the
        // per-frame pushes UpdateGestureLayer mirrors: the second dash adds
        // exactly one new push (:1184-1190 guard — the stamp does not move).
        int beforeDash2 = rig.Target.LayerPushes.Count(p => p.Slot == (int)GestureSlot.Jump && p.Seq > 0);
        rig.State.DoAnimationEvent(PlayerAnimEvent.DoubleJump);   // already jumping: the stamp stands
        Assert.Equal(beforeDash2 + 1,
            rig.Target.LayerPushes.Count(p => p.Slot == (int)GestureSlot.Jump && p.Seq > 0));   // the dash gesture plays each time

        rig.State.Update(0f, 0f, 1.9f, 1 / 64f);
        Assert.Equal("ACT_MP_JUMP_FLOAT_PRIMARY", rig.State.CurrentMainActivity);   // still 0.9 s from the FIRST stamp — a restamp would read START
    }

    [Fact]
    public void TheStunPasstimeAndCyoaTriosFillTheCustomSlot()
    {
        // :1214-1240 — all three trios restart GESTURE_SLOT_CUSTOM (the
        // begins and middles not auto-killed, the ends auto-killed — the
        // core's RestartGesture autoKill parameter). No table keys any of
        // these names, so they reach the model raw.
        var rig = Running(TFClass.Scout, TFWeaponRole.Primary);
        Modelled(rig.Target, "ACT_MP_STUN_BEGIN", "ACT_MP_STUN_MIDDLE", "ACT_MP_STUN_END",
                            "ACT_MP_PASSTIME_THROW_BEGIN", "ACT_MP_PASSTIME_THROW_MIDDLE", "ACT_MP_PASSTIME_THROW_END",
                            "ACT_MP_CYOA_PDA_INTRO", "ACT_MP_CYOA_PDA_IDLE", "ACT_MP_CYOA_PDA_OUTRO");

        rig.State.DoAnimationEvent(PlayerAnimEvent.StunBegin);
        AssertGesture(rig, (int)GestureSlot.Custom, "ACT_MP_STUN_BEGIN");            // :1215
        rig.State.DoAnimationEvent(PlayerAnimEvent.StunMiddle);
        AssertGesture(rig, (int)GestureSlot.Custom, "ACT_MP_STUN_MIDDLE");            // :1218
        rig.State.DoAnimationEvent(PlayerAnimEvent.StunEnd);
        AssertGesture(rig, (int)GestureSlot.Custom, "ACT_MP_STUN_END");               // :1221
        rig.State.DoAnimationEvent(PlayerAnimEvent.PasstimeThrowBegin);
        AssertGesture(rig, (int)GestureSlot.Custom, "ACT_MP_PASSTIME_THROW_BEGIN");   // :1224
        rig.State.DoAnimationEvent(PlayerAnimEvent.PasstimeThrowMiddle);
        AssertGesture(rig, (int)GestureSlot.Custom, "ACT_MP_PASSTIME_THROW_MIDDLE");  // :1227
        rig.State.DoAnimationEvent(PlayerAnimEvent.PasstimeThrowEnd);
        AssertGesture(rig, (int)GestureSlot.Custom, "ACT_MP_PASSTIME_THROW_END");     // :1230
        rig.State.DoAnimationEvent(PlayerAnimEvent.CyoaPdaBegin);
        AssertGesture(rig, (int)GestureSlot.Custom, "ACT_MP_CYOA_PDA_INTRO");         // :1233
        rig.State.DoAnimationEvent(PlayerAnimEvent.CyoaPdaMiddle);
        AssertGesture(rig, (int)GestureSlot.Custom, "ACT_MP_CYOA_PDA_IDLE");          // :1236
        rig.State.DoAnimationEvent(PlayerAnimEvent.CyoaPdaEnd);
        AssertGesture(rig, (int)GestureSlot.Custom, "ACT_MP_CYOA_PDA_OUTRO");         // :1239
    }

    [Fact]
    public void TheCrouchDoubleJumpIsConsumedAndDoesNothing()
    {
        // :1206-1213 — the body is commented out upstream (and the base has
        // no such arm): the event is consumed and the machine moves on — no
        // JUMP-slot gesture, no jump stamp: airborne still answers the gait.
        var rig = Running(TFClass.Scout, TFWeaponRole.Primary);
        Modelled(rig.Target, "ACT_MP_JUMP_START_PRIMARY");
        rig.Target.OnGroundValue = false;
        rig.State.Update(0f, 0f, 1f, 1 / 64f);

        rig.State.DoAnimationEvent(PlayerAnimEvent.DoubleJumpCrouch);

        rig.State.Update(0f, 0f, 1.3f, 1 / 64f);
        Assert.DoesNotContain(rig.Target.LayerPushes, p => p.Slot == (int)GestureSlot.Jump && p.Seq > 0);
        Assert.Equal("ACT_MP_STAND_PRIMARY", rig.State.CurrentMainActivity);   // the gait still answers (airborne, still: the stand)
    }

    //-------------------------------------------------------------------------
    // The disguised spy's table swap (tf_weaponbase.cpp:4212-4222): the
    // ActivityList CLIENT arm — for the enemy's eye, the DISGUISE weapon's
    // act-table answers for the carried one's. The cond is the layer's check
    // (:4214's first term); the enemy view, the disguise weapon's existence
    // and its not being the carried weapon (:4214/:4217) are the seam side's
    // reads, and stock's arm is #ifdef CLIENT_DLL — the server never swaps.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheDisguisedSpyAnimatesAsTheDisguise()
    {
        // The carried weapon is the revolver (the Item2 table's answer for the
        // gait: STAND_ITEM2, :3971) and the disguise weapon the minigun
        // (the Primary table's: STAND_PRIMARY, :3662): the enemy's eye sees
        // :4219's table — the disguise's.
        var rig = Running(TFClass.Spy, TFWeaponRole.Item2, TFWeaponId.Revolver, conds: [TFCond.Disguised]);
        rig.Weapons.Disguise[1] = new TFAnimWeapon(TFWeaponRole.Primary, TFWeaponId.Minigun);
        Modelled(rig.Target, "ACT_MP_STAND_ITEM2", "ACT_MP_STAND_PRIMARY",
                                    "ACT_MP_RUN_ITEM2", "ACT_MP_RUN_PRIMARY");

        Assert.Equal("ACT_MP_STAND_PRIMARY", Answered(rig, _ => { }));
        Assert.Equal("ACT_MP_RUN_PRIMARY", Answered(rig, t => t.VelocityField = new Vector(300f, 0f, 0f)));
    }

    [Fact]
    public void TheDisguiseSwapNeedsTheDisguisedCondition()
    {
        // The cond is the gate: with it absent (:4214's first term) the
        // carried weapon's table stands — the same rig, undisguised, answers
        // the revolver's table.
        var rig = Running(TFClass.Spy, TFWeaponRole.Item2, TFWeaponId.Revolver);
        rig.Weapons.Disguise[1] = new TFAnimWeapon(TFWeaponRole.Primary, TFWeaponId.Minigun);
        Modelled(rig.Target, "ACT_MP_STAND_ITEM2", "ACT_MP_STAND_PRIMARY");
        Assert.Equal("ACT_MP_STAND_ITEM2", Answered(rig, _ => { }));
    }

    [Fact]
    public void TheDisguiseSwapNeedsTheSeamToAnswer()
    {
        // The seam-side gates all answer as stock's no-swap arms (:4214-4218):
        // no enemy viewer (IsEnemyPlayer), no disguise weapon
        // (GetDisguiseWeapon null), or the server side (CLIENT_DLL) — the
        // cond is on, the seam refuses, the carried table stands.
        var rig = Running(TFClass.Spy, TFWeaponRole.Item2, TFWeaponId.Revolver, conds: [TFCond.Disguised]);
        Modelled(rig.Target, "ACT_MP_STAND_ITEM2", "ACT_MP_STAND_PRIMARY");
        Assert.Equal("ACT_MP_STAND_ITEM2", Answered(rig, _ => { }));
    }

    [Fact]
    public void TheDisguiseSwapMovesOnlyTheTable()
    {
        // The weapon arms read the carried weapon (GetActiveTFWeapon at
        // :993/:1013) — the swap belongs to the table alone. The revolver
        // spy disguised as a heavy's minigun: the arm takes the generic
        // shape (:1030-1036, the unguarded else — not the minigun's guarded
        // arm), so a second fire pushes again; and the name reaches the
        // model through the DISGUISE's table (:3679 — the Item2 table would
        // have answered ATTACK_STAND_ITEM2, :3990).
        var rig = Running(TFClass.Spy, TFWeaponRole.Item2, TFWeaponId.Revolver, conds: [TFCond.Disguised]);
        rig.Weapons.Disguise[1] = new TFAnimWeapon(TFWeaponRole.Primary, TFWeaponId.Minigun);
        Modelled(rig.Target, "ACT_MP_ATTACK_STAND_PRIMARY", "ACT_MP_ATTACK_STAND_ITEM2");

        rig.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_STAND_PRIMARY");   // :3679, reached via :4219

        int before = rig.Target.LayerPushes.Count(p => p.Slot == (int)GestureSlot.AttackAndReload && p.Seq > 0);
        rig.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        // The unguarded arm: the minigun guard would have stood the second
        // fire aside — the arm read the carried revolver, not the ghost.
        Assert.Equal(before + 1, rig.Target.LayerPushes.Count(p => p.Slot == (int)GestureSlot.AttackAndReload && p.Seq > 0));
    }

    //-------------------------------------------------------------------------
    // The D10 hook chain (ITFAnimHook): links consulted INSIDE the layer,
    // before its decision, at the two points a mod's subclass of stock's
    // CTFPlayerAnimState would have overridden — the main activity and a
    // gesture's landing. First answer wins; unclaimed, the layer answers as
    // stock wrote it.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheModHookAnswersTheMainSequenceBeforeTheLayerDecides()
    {
        // A hook naming the main activity stands in for the gait answer —
        // the layer's handlers never run, and the answer is not translated
        // (the mod's CalcMainActivity override's early return, stock's own
        // subclass shape). The context carries the layer's two reads.
        var hook = new FakeAnimHook(mainAnswer: "ACT_MP_STUN_BEGIN");
        var rig = Running(TFClass.Soldier, TFWeaponRole.Primary, TFWeaponId.Minigun, hooks: TFAnimHookChain.Of(hook));
        Modelled(rig.Target, "ACT_MP_STAND_PRIMARY", "ACT_MP_STUN_BEGIN");

        Assert.Equal("ACT_MP_STUN_BEGIN", Answered(rig, _ => { }));
        Assert.Equal(new TFAnimHookContext(rig.Target.EntityIndex, TFClass.Soldier,
            new TFAnimWeapon(TFWeaponRole.Primary, TFWeaponId.Minigun), 1f), hook.Asked);
    }

    [Fact]
    public void TheModHookAnswersTheGestureBeforeTheLayerPlaysIt()
    {
        // A hook naming the gesture for the slot lands its name there
        // instead of the event's translated claim (the mod's own
        // RestartGesture override, never calling the base): the fire's
        // ATTACK_STAND_PRIMARY is replaced before the table ever applies.
        var rig = Running(TFClass.Soldier, TFWeaponRole.Primary,
            hooks: TFAnimHookChain.Of(new FakeAnimHook(
                gestureAnswer: "ACT_MP_STUN_MIDDLE",
                gestureForSlot: (int)GestureSlot.AttackAndReload,
                gestureForClaim: MpActivities.AttackStandPrimaryFire)));
        Modelled(rig.Target, "ACT_MP_STUN_MIDDLE");

        rig.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_STUN_MIDDLE");
    }

    [Fact]
    public void TheLayerDecidesWhenNoHookAnswers()
    {
        // The brief's other half: a chain over a hook that answers nothing
        // leaves every decision the layer's own — the gait stands on the
        // weapon's table, the fire's gesture its translated claim.
        var rig = Running(TFClass.Soldier, TFWeaponRole.Primary, hooks: TFAnimHookChain.Of(new FakeAnimHook()));
        Modelled(rig.Target, "ACT_MP_STAND_PRIMARY", "ACT_MP_ATTACK_STAND_PRIMARY");

        Assert.Equal("ACT_MP_STAND_PRIMARY", Answered(rig, _ => { }));
        rig.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_STAND_PRIMARY");   // :3679
    }

    [Fact]
    public void TheFirstHookInTheOrderAnswers()
    {
        // The chain sorts by HookOrder before asking, whatever the
        // registration order: the 999_310 hook answers and wins.
        var late = new FakeAnimHook(mainAnswer: "ACT_MP_STUN_BEGIN", order: 999_311);
        var early = new FakeAnimHook(mainAnswer: "ACT_MP_STUN_MIDDLE", order: 999_310);
        var rig = Running(TFClass.Soldier, TFWeaponRole.Primary, hooks: TFAnimHookChain.Of(late, early));
        Modelled(rig.Target, "ACT_MP_STAND_PRIMARY", "ACT_MP_STUN_BEGIN", "ACT_MP_STUN_MIDDLE");

        Assert.Equal("ACT_MP_STUN_MIDDLE", Answered(rig, _ => { }));
    }

    [Fact]
    public void AThrowingHookStandsAside()
    {
        // The chain's guard (the weapon chain's rule): a hook that throws is
        // reported and the answers before/without it stand — the layer's
        // decision survives a broken mod link.
        var rig = Running(TFClass.Soldier, TFWeaponRole.Primary, hooks: TFAnimHookChain.Of(new FakeAnimHook(throws: true)));
        Modelled(rig.Target, "ACT_MP_STAND_PRIMARY", "ACT_MP_ATTACK_STAND_PRIMARY");

        Assert.Equal("ACT_MP_STAND_PRIMARY", Answered(rig, _ => { }));
        rig.State.DoAnimationEvent(PlayerAnimEvent.AttackPrimary);
        AssertGesture(rig, (int)GestureSlot.AttackAndReload, "ACT_MP_ATTACK_STAND_PRIMARY");
    }
}
