using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// <see cref="TFCapturePlayerRules"/>: CTFGameRules' three capture answers
/// (tf_gamerules.cpp:16835, :16953, :18121) over the player's carrier.
/// </summary>
public sealed class TFCapturePlayerRulesTests
{
    private const int Index = 4;
    private static readonly EntityHandle Player = new(0x0001_0000u | Index);

    private sealed class Teams : ITeams
    {
        public int Of { get; set; } = (int)TFTeam.Red;
        public int Team(EntityHandle player) => Of;
        public IReadOnlyList<EntityHandle> Players(int team) => [];
        public int Count(int team) => 0;
        public JoinTeamResult Join(EntityHandle player, int requested) => throw new NotSupportedException();
        public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;
    }

    private static (TFCapturePlayerRules Rules, TFPlayerNetState State) Make(TFClass playerClass, params TFCond[] conds)
        => Make(playerClass, null, conds);

    private static (TFCapturePlayerRules Rules, TFPlayerNetState State) Make(TFClass playerClass, MpCapStyleConVar? capStyle, params TFCond[] conds)
    {
        var (rules, state, _) = Build(playerClass, conds, capStyle);
        return (rules, state);
    }

    /// <summary>The rules over a player whose attribute manager is <paramref name="manager"/> (null for none).</summary>
    private static (TFCapturePlayerRules Rules, TFPlayerNetState State) Owned(TFClass playerClass, IAttributeProvider? manager,
                                                                             MpCapStyleConVar? capStyle = null)
    {
        var owners = new FakeOwners();
        if (manager is not null)
            owners.Managers[Index] = manager;
        var (rules, state, _) = Build(playerClass, [], capStyle, owners);
        return (rules, state);
    }

    private static (TFCapturePlayerRules Rules, TFPlayerNetState State, FakeOwners Owners) Build(
        TFClass playerClass, TFCond[] conds, MpCapStyleConVar? capStyle = null, FakeOwners? owners = null)
    {
        var engine = new RecordingEngine();
        engine.Returns("Entities.Create", new EntityHandle(0x0002_0050u));
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Entities.GetIndex", Index);
        engine.Returns("Entities.GetOrigin", default(Vector));
        var entity = new TFPlayerDataEntity(engine);
        entity.State.BindLocal();
        var carriers = new TFPlayerCarriers();
        var carrier = new TFPlayerCarrier(engine, new OneEntity(new EntityHandle(0x0002_0050u), entity), carriers, new RecordingLog());
        carrier.Attach(Player);
        entity.State.SetClass(playerClass);
        entity.State.SetConditions(TFCondMask.Of(conds));
        owners ??= new FakeOwners();
        return (new TFCapturePlayerRules(carriers, new Teams(), new AttributeHooks(), owners, capStyle), entity.State, owners);
    }

    /// <summary>A bound <c>mp_capstyle</c>, so a fact chooses the style instead of relying on the null default.</summary>
    private static MpCapStyleConVar CapStyle(string value)
    {
        MpCapStyleConVar convar = new();
        convar.Bind(-1, "mp_capstyle", value);
        return convar;
    }

    private sealed class OneEntity(EntityHandle handle, ScriptedEntity entity) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => h == handle ? entity : null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => true;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    [Fact]
    public void An_ordinary_player_may_cap_counts_once_and_does_not_only_block()
    {
        var (rules, _) = Make(TFClass.Soldier);

        Assert.True(rules.PlayerMayCapturePoint(Player, 0));
        Assert.False(rules.PlayerMayBlockPoint(Player, 0));
        Assert.Equal(1, rules.CaptureValueForPlayer(Player));
    }

    [Theory]
    [InlineData(TFCond.Stealthed)]
    [InlineData(TFCond.StealthedUserBuffFading)]
    [InlineData(TFCond.Invulnerable)]
    [InlineData(TFCond.InvulnerableUserBuff)]
    [InlineData(TFCond.Megaheal)]
    [InlineData(TFCond.Phase)]
    public void A_cloaked_ubered_megahealed_or_phased_player_may_not_cap(TFCond cond)
        => Assert.False(Make(TFClass.Spy, cond).Rules.PlayerMayCapturePoint(Player, 0));

    [Fact]
    public void A_spy_disguised_as_the_enemy_may_not_cap_but_as_his_own_team_may()
    {
        var (rules, state) = Make(TFClass.Spy, TFCond.Disguised);
        state.DisguiseTeam = (int)TFTeam.Blue;
        Assert.False(rules.PlayerMayCapturePoint(Player, 0));

        state.DisguiseTeam = (int)TFTeam.Red;
        Assert.True(rules.PlayerMayCapturePoint(Player, 0));
    }

    [Fact]
    public void An_invulnerable_player_blocks()
        => Assert.True(Make(TFClass.Medic, TFCond.Invulnerable).Rules.PlayerMayBlockPoint(Player, 0));

    [Fact]
    public void A_scout_counts_twice_under_capstyle_one()
        => Assert.Equal(2, Make(TFClass.Scout, CapStyle("1")).Rules.CaptureValueForPlayer(Player));

    [Fact]
    public void A_scout_counts_ten_when_capstyle_is_not_one()
        => Assert.Equal(10, Make(TFClass.Scout, CapStyle("0")).Rules.CaptureValueForPlayer(Player));

    [Fact]
    public void A_control_stunned_player_may_not_cap_and_does_not_only_block()
    {
        // IsControlStunned (tf_player_shared.cpp:9952): the active entry, the
        // stunned condition and the controls bit, on the carrier's words.
        var (rules, state) = Make(TFClass.Scout, TFCond.Stunned);
        state.StunIndex = 0;
        state.StunFlags = (int)TFStunFlags.Controls;

        Assert.False(rules.PlayerMayCapturePoint(Player, 0));
        Assert.False(rules.PlayerMayBlockPoint(Player, 0));
    }

    [Fact]
    public void A_movement_stunned_player_may_cap()
    {
        // TF_STUN_MOVEMENT alone is not IsControlStunned: the rocket
        // specialist's stun on impact leaves the cap hand working.
        var (rules, state) = Make(TFClass.Soldier, TFCond.Stunned);
        state.StunIndex = 0;
        state.StunFlags = (int)TFStunFlags.Movement;

        Assert.True(rules.PlayerMayCapturePoint(Player, 0));
    }

    [Fact]
    public void A_stun_word_without_the_condition_does_not_stop_the_cap()
    {
        // IsControlStunned wants TF_COND_STUNNED behind the index: the value
        // words a cleared entry leaves answer nothing.
        var (rules, state) = Make(TFClass.Scout);
        state.StunIndex = 0;
        state.StunFlags = (int)TFStunFlags.Controls;

        Assert.True(rules.PlayerMayCapturePoint(Player, 0));
    }

    [Fact]
    public void A_player_with_no_carrier_is_not_a_tf_player()
    {
        var rules = new TFCapturePlayerRules(new TFPlayerCarriers(), new Teams(), new AttributeHooks(), new FakeOwners());

        Assert.False(rules.PlayerMayCapturePoint(Player, 0));
        Assert.False(rules.PlayerMayBlockPoint(Player, 0));
        Assert.Equal(1, rules.CaptureValueForPlayer(Player));
    }

    // --- the capture value's attribute (CALL_ATTRIB_HOOK_INT_ON_OTHER, :18156) --

    /// <summary>The player's attribute manager holding one additive attribute, as <c>m_AttributeManager</c> would.</summary>
    private static IAttributeProvider Arm(string name, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(5004, "attr 5004", name, AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    [Fact]
    public void TheCaptureValueAttributeAdds()
    {
        var (rules, _) = Owned(TFClass.Soldier, Arm(TFCapturePlayerRules.CaptureValueAttrib, 2f));

        Assert.Equal(3, rules.CaptureValueForPlayer(Player));
    }

    [Fact]
    public void WithoutTheAttributeTheCaptureValueIsTheClass()
    {
        var (rules, _) = Owned(TFClass.Soldier, manager: null);

        Assert.Equal(1, rules.CaptureValueForPlayer(Player));
    }

    [Fact]
    public void TheCaptureValueAttributeRidesTheScoutArm()
    {
        // stock's order: the hook runs after the scout arm (:18142-18156).
        var (rules, _) = Owned(TFClass.Scout, Arm(TFCapturePlayerRules.CaptureValueAttrib, 1f), CapStyle("1"));

        Assert.Equal(3, rules.CaptureValueForPlayer(Player));
    }
}
