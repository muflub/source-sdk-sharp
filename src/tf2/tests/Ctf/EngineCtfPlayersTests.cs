using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Entities.Ctf;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using TF2.Tests.Entities;
using TF2.Tests.Medigun;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Ctf;

/// <summary>
/// The engine's two row-93 answers for <see cref="ICtfPlayers"/>:
/// <c>CTFPlayer::IsAllowedToPickUpFlag</c>'s <c>cannot_pick_up_intelligence</c>
/// hook over the player's own item manager
/// (<c>shared/tf/tf_player_shared.cpp:11260-11270</c>, the stock hook macro)
/// and <c>CTFPlayerShared::GetPercentInvisible</c> recomputed from the carrier
/// (:8047-8051).
/// </summary>
public sealed class EngineCtfPlayersTests
{
    private static readonly EntityHandle Blue = new(0x0001_0003u);

    private int _next = 2000;

    private AttributeDefinition Def(string hook) =>
        new(_next++, "attr " + _next, hook, AttributeDescriptionFormat.Additive,
            AttributeEffectType.Neutral, false, false, AttributeValueType.Default, MergedKeys.Block("x"));

    private AttributeContainer ManagerWith(params AttributeDefinition[] hooks)
    {
        var manager = new AttributeContainer();
        foreach (var def in hooks)
            manager.Set(AttributeValue.OfFloat(def, 1f));
        return manager;
    }

    private static (EngineCtfPlayers Players, TFHealingPlayerData Data, FakeClock Clock, FakeOwners Owners) Compose(IMovementRules? movement = null)
    {
        var engine = TestServices.Fake();
        engine.ReturnsEachCall("Entities.GetIndex",
                               () => engine.Calls[^1].Args[0] is EntityHandle h ? h.EntryIndex : 0);
        var data = new TFHealingPlayerData();
        var clock = new FakeClock { Now = 100f };
        var owners = new FakeOwners();
        return (new EngineCtfPlayers(engine, engine, engine, data, new AttributeHooks(), clock, movement, owners: owners),
                data, clock, owners);
    }

    /// <summary>A movement table that answers its index over a base, so the ask is visible.</summary>
    private sealed class FakeMovement : IMovementRules
    {
        public MovementParams? Constants => null;

        public float JumpScale(in JumpRequest request) => 1f;

        public float FrictionScale(int playerIndex, float friction, float speed, bool ducking, int groundMaterial)
            => friction;

        public float MaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial)
            => 300f + playerIndex;
    }

    // ---- AllowedToPickUpFlag (tf_player_shared.cpp:11260-11270) ----

    [Fact]
    public void The_cannot_pick_up_intelligence_attribute_on_the_manager_refuses_the_flag()
    {
        var (players, _, _, owners) = Compose();
        owners.Managers[Blue.EntryIndex] = ManagerWith(Def("cannot_pick_up_intelligence"));

        Assert.False(players.AllowedToPickUpFlag(Blue));
    }

    [Fact]
    public void A_manager_without_the_attribute_and_a_player_without_a_manager_may_take_the_flag()
    {
        // The hook's walk answers the starting 0 when the class is absent
        // (attribute_manager.cpp) — the refusal arm is the only one.
        var (players, _, _, owners) = Compose();
        owners.Managers[Blue.EntryIndex] = ManagerWith(Def("crit_pct"));
        Assert.True(players.AllowedToPickUpFlag(Blue));

        owners.Managers.Clear();
        Assert.True(players.AllowedToPickUpFlag(Blue));
    }

    [Fact]
    public void No_owners_service_at_all_still_allows_the_flag()
    {
        // The optional arm: a build without the weapon-owner slice never refuses.
        var engine = TestServices.Fake();
        engine.ReturnsEachCall("Entities.GetIndex",
                               () => engine.Calls[^1].Args[0] is EntityHandle h ? h.EntryIndex : 0);
        var players = new EngineCtfPlayers(engine, engine, engine, new TFHealingPlayerData(),
                                           new AttributeHooks(), new FakeClock { Now = 100f });

        Assert.True(players.AllowedToPickUpFlag(Blue));
    }

    // ---- PercentInvisible (tf_player_shared.cpp:8047-8051) ----

    [Fact]
    public void The_level_comes_from_the_carrier_and_the_clock()
    {
        var (players, data, _, _) = Compose();
        var state = new TFPlayerNetState();
        state.BindLocal();
        state.PlayerClass = (int)TFClass.Spy;
        state.InvisChangeCompleteTime = 101f;   // decloaked at 100.5 over the 2.0 s
        data.States[Blue.EntryIndex] = state;

        // The fade-out arm's half rate: (101 - 100) * 0.5.
        Assert.Equal(0.5f, players.PercentInvisible(Blue), 4);
    }

    [Fact]
    public void A_player_with_no_carrier_is_never_invisible()
    {
        var (players, _, _, _) = Compose();

        Assert.Equal(0f, players.PercentInvisible(Blue), 4);
    }

    // ---- Velocity / MaxSpeed (the flag's trail gate) ----

    [Fact]
    public void The_velocity_is_the_engine_answer_for_the_player()
    {
        var engine = TestServices.Fake();
        engine.Returns("Entities.GetVelocity", new Vector(3f, 0f, 4f));
        var players = new EngineCtfPlayers(engine, engine, engine, new TFHealingPlayerData(),
                                           new AttributeHooks(), new FakeClock { Now = 100f });

        Assert.Equal(new Vector(3f, 0f, 4f), players.Velocity(Blue));
    }

    [Fact]
    public void The_max_speed_comes_from_the_movement_rules_by_index()
    {
        var (players, _, _, _) = Compose(new FakeMovement());

        // Blue's entry index is 3 (handle 0x0001_0003) — the fake answers 300 + index.
        Assert.Equal(303f, players.MaxSpeed(Blue), 4);
    }

    [Fact]
    public void No_movement_rules_answers_zero()
    {
        var (players, _, _, _) = Compose();

        Assert.Equal(0f, players.MaxSpeed(Blue), 4);
    }
}
