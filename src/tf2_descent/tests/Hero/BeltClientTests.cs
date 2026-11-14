extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientBeltMessage = client::TF2.Descent.Shared.BeltMessage;
using ClientBeltState = client::TF2.Descent.Client.Belt.BeltState;
using ClientGearWearLevel = client::TF2.Descent.Shared.GearWearLevel;
using ClientGearWearMessage = client::TF2.Descent.Shared.GearWearMessage;
using ClientGearWearState = client::TF2.Descent.Client.Belt.GearWearState;
using ClientKeyActions = client::SourceSharp.Client.Input.KeyActions;
using IClientBeltHook = client::TF2.Descent.Shared.IBeltHook;
using IClientGearWearHook = client::TF2.Descent.Shared.IGearWearHook;
using IClientLevelHook = client::SourceSharp.Client.IClientLevelHook;
using IClientUserMessage = client::SourceSharp.Abi.IUserMessage;
using ClientReader = client::SourceSharp.Abi.UserMessageReader;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// The belt's and the gear wear's client half (HE8, HE9): the server's bytes read into the
/// client's state services, which a level's end clears; the cooldown's fraction; the worst
/// piece of gear warned of; the belt keys, bound to keys TF2 and the skill keys leave free.
/// </summary>
public sealed class BeltClientTests
{
    private sealed class Hooks<T>(params T[] hooks) : client::SourceSharp.Abi.IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    /// <summary>A payload as the SERVER writes it.</summary>
    private static byte[] Written<T>(T payload) where T : IUserMessagePayload<T>
    {
        var buffer = new byte[255];
        int n = payload.Write(buffer);
        Assert.True(n >= 0, "the payload fits a user message");
        return buffer[..n];
    }

    private static void Dispatch(IClientUserMessage handler, byte[] bytes) => handler.OnReceived(new ClientReader(bytes));

    private static TF2.Descent.Shared.Belt Full() => new()
    {
        Bases = "health_medium,health_medium,mana_medium,mana_medium",
        Names = "Half Sandvich|Half Sandvich|Bonk! Can|Bonk! Can",
        Counts = [4, 4, 4, 4],
        ReadyTick = 1200,
        CooldownTicks = 200,
    };

    [Fact]
    public void The_servers_belt_bytes_are_read_into_the_clients_belt_state()
    {
        var state = new ClientBeltState();

        Dispatch(new ClientBeltMessage(new Hooks<IClientBeltHook>(state)), Written(Full()));

        Assert.True(state.Received);
        Assert.Equal("mana_medium", state.BaseOf(2));
        Assert.Equal("Bonk! Can", state.NameOf(3));
        Assert.Equal(4, state.CountOf(0));
        Assert.Equal((1200, 200), (state.ReadyTick, state.CooldownTicks));
    }

    [Fact]
    public void A_full_belt_of_the_longest_names_fits_one_message()
    {
        var belt = Full() with { Names = string.Join('|', Enumerable.Repeat(new string('n', TF2.Descent.Shared.Belt.MaxName), 4)) };

        Assert.InRange(Written(belt).Length, 1, 255);
    }

    [Fact]
    public void The_cooldowns_fraction_runs_from_one_to_zero()
    {
        var state = new ClientBeltState();
        Dispatch(new ClientBeltMessage(new Hooks<IClientBeltHook>(state)), Written(Full()));

        Assert.Equal(1f, state.CooldownFraction(1000));
        Assert.Equal(0.5f, state.CooldownFraction(1100));
        Assert.Equal(0f, state.CooldownFraction(1200));
    }

    [Fact]
    public void A_levels_end_clears_the_clients_belt()
    {
        var state = new ClientBeltState();
        Dispatch(new ClientBeltMessage(new Hooks<IClientBeltHook>(state)), Written(Full()));

        ((IClientLevelHook)state).OnLevelShutdown();

        Assert.False(state.Received);
        Assert.Equal(0, state.CountOf(0));
    }

    [Fact]
    public void The_gear_warning_is_the_worst_piece_and_an_emptied_slot_leaves_it()
    {
        var state = new ClientGearWearState();
        var message = new ClientGearWearMessage(new Hooks<IClientGearWearHook>(state));

        Dispatch(message, Written(new TF2.Descent.Shared.GearWear { Slot = "hat", Name = "Field Cap", Level = (byte)TF2.Descent.Shared.GearWearLevel.Low, Durability = 9, MaxDurability = 40 }));
        Dispatch(message, Written(new TF2.Descent.Shared.GearWear { Slot = "primary", Name = "Rocket Launcher", Level = (byte)TF2.Descent.Shared.GearWearLevel.Broken, Durability = 0, MaxDurability = 60 }));
        var worst = state.Warning;
        Dispatch(message, Written(new TF2.Descent.Shared.GearWear { Slot = "primary", Level = (byte)TF2.Descent.Shared.GearWearLevel.Empty }));

        Assert.Equal(("primary", ClientGearWearLevel.Broken), (worst!.Slot, worst.Level));
        Assert.Equal("hat", state.Warning!.Slot);
        Assert.Single(state.Slots);
    }

    [Fact]
    public void A_levels_end_clears_the_gear_wear()
    {
        var state = new ClientGearWearState();
        Dispatch(new ClientGearWearMessage(new Hooks<IClientGearWearHook>(state)),
                 Written(new TF2.Descent.Shared.GearWear { Slot = "hat", Name = "Field Cap", Level = (byte)TF2.Descent.Shared.GearWearLevel.Low, Durability = 9, MaxDurability = 40 }));

        ((IClientLevelHook)state).OnLevelShutdown();

        Assert.Null(state.Warning);
        Assert.Empty(state.Slots);
    }

    [Fact]
    public void The_belt_keys_are_the_keypads_one_to_four_and_no_other_action_binds_them()
    {
        var mod = typeof(ClientBeltState).Assembly;
        var tf2 = typeof(client::TF2.Client.MoveForwardKey).Assembly;
        var actions = ClientKeyActions.Of(tf2.GetTypes().Concat(mod.GetTypes()), typeof(ClientKeyActions).Assembly);

        var belt = actions.DefaultBinds.Where(b => b.Command.StartsWith("tf2_descent_belt ", StringComparison.Ordinal)).ToArray();
        Assert.Equal([("KP_END", "tf2_descent_belt 1"), ("KP_DOWNARROW", "tf2_descent_belt 2"),
                      ("KP_PGDN", "tf2_descent_belt 3"), ("KP_LEFTARROW", "tf2_descent_belt 4")], belt);
        Assert.Equal(client::TF2.Descent.Client.Belt.BeltKeys.SlotKeys, belt.Select(b => b.Key));

        // No clash: nothing else (TF2's list, the skill keys) binds a belt key, and the skill keys stay 4 to 9.
        Assert.All(belt, b => Assert.Single(actions.DefaultBinds, d => d.Key == b.Key));
        Assert.Equal(["4", "5", "6", "7", "8", "9"],
                     actions.DefaultBinds.Where(b => b.Command.StartsWith("+tf2_descent_skill", StringComparison.Ordinal)).Select(b => b.Key));
    }

    [Fact]
    public void A_belt_keys_line_is_the_verb_the_server_answers()
    {
        Assert.Equal("tf2_descent_belt 3", client::TF2.Descent.Client.Belt.BeltKeys.DrinkLine(3));
        Assert.Equal(TF2.Descent.Server.Hero.BeltClientCommands.Drink, client::TF2.Descent.Client.Belt.BeltKeys.DrinkLine(1).Split(' ')[0]);
    }
}
