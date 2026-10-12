using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed screen shake and fade (plan_side_isolation §2.2a): the reach
/// rules view_effect_reach_test.cpp pinned until its shake half went (lane HC),
/// the two payloads in the order the
/// stock client reads them (view_effects.cpp MsgFunc_Shake / MsgFunc_Fade),
/// and which players each reaches.
/// </summary>
public class ScreenEffectsTests
{
    // ---- the reach rules ---------------------------------------------------

    [Fact] public void No_radius_is_full_amplitude_for_everyone() => Assert.Equal(4f, ScreenEffects.ShakeAmplitude(1000f, 4f, 0f));
    [Fact] public void Amplitude_falls_linearly_with_distance() => Assert.Equal(3f, ScreenEffects.ShakeAmplitude(25f, 4f, 100f));
    [Fact] public void The_edge_itself_is_in_range_at_zero() => Assert.Equal(0f, ScreenEffects.ShakeAmplitude(100f, 4f, 100f));
    [Fact] public void Past_the_edge_is_out_of_range() => Assert.True(ScreenEffects.ShakeAmplitude(100.5f, 4f, 100f) < 0f);

    [Fact] public void A_grounded_player_is_always_reached() => Assert.True(ScreenEffects.ShakeReachesPlayer(true, false, true));
    [Fact] public void An_airborne_player_gets_a_start_only_with_the_air_flag()
    {
        Assert.False(ScreenEffects.ShakeReachesPlayer(false, false, true));
        Assert.True(ScreenEffects.ShakeReachesPlayer(false, true, true));
    }
    [Fact] public void Anything_but_a_start_reaches_an_airborne_player() => Assert.True(ScreenEffects.ShakeReachesPlayer(false, false, false));

    [Fact]
    public void A_positive_amplitude_is_sent_and_a_spent_start_is_not()
    {
        Assert.True(ScreenEffects.ShakeTransmits(2f, false, out float sent));
        Assert.Equal(2f, sent);
        Assert.False(ScreenEffects.ShakeTransmits(0f, false, out _));
    }

    [Fact]
    public void A_stop_is_always_sent_carrying_zero()
    {
        Assert.True(ScreenEffects.ShakeTransmits(0f, true, out float sent));
        Assert.Equal(0f, sent);

        // And zero however strong the falloff said it was: a stop carrying the
        // computed amplitude reads on the client as a new shake at that
        // strength (view_effect_reach_test's ShakeTransmit arm, carried over).
        Assert.True(ScreenEffects.ShakeTransmits(12.5f, true, out sent));
        Assert.Equal(0f, sent);
    }

    [Fact]
    public void The_falloff_is_linear_across_the_whole_radius()
    {
        Assert.Equal(10f, ScreenEffects.ShakeAmplitude(0f, 10f, 100f));
        Assert.Equal(7.5f, ScreenEffects.ShakeAmplitude(25f, 10f, 100f));
        Assert.Equal(5f, ScreenEffects.ShakeAmplitude(50f, 10f, 100f));
        Assert.Equal(2.5f, ScreenEffects.ShakeAmplitude(75f, 10f, 100f));
    }

    [Theory]
    [InlineData(1.0f, 512)]
    [InlineData(0.9999f, 511)]   // truncated, not rounded
    [InlineData(-1.0f, 0)]       // clamped below
    [InlineData(1000.0f, 65535)] // clamped above
    public void Fade_times_are_truncated_unsigned_7_9_fixed_point(float seconds, int expected)
    {
        Assert.Equal(expected, ScreenEffects.FixedUnsigned16(seconds, 1 << ScreenEffects.FadeFractionBits));
    }

    // ---- the payloads ------------------------------------------------------

    [Fact]
    public void The_shake_payload_is_a_command_byte_then_three_floats()
    {
        Span<byte> buf = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        int n = ScreenEffects.ShakeOf(ShakeCommand.Amplitude, 1.5f, 2.5f, 3.5f).Write(buf);

        Assert.Equal(13, n);
        Assert.Equal((byte)ShakeCommand.Amplitude, buf[0]);
        Assert.Equal(1.5f, BitConverter.ToSingle(buf[1..5]));
        Assert.Equal(2.5f, BitConverter.ToSingle(buf[5..9]));
        Assert.Equal(3.5f, BitConverter.ToSingle(buf[9..13]));
    }

    [Fact]
    public void The_fade_payload_is_three_shorts_then_four_colour_bytes()
    {
        Span<byte> buf = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        int n = ScreenEffects.FadeOf(1.0f, 120.0f, FadeFlags.Out | FadeFlags.StayOut, 300, 2, 3, 4).Write(buf);

        Assert.Equal(10, n);
        Assert.Equal(512, BitConverter.ToUInt16(buf[0..2]));
        Assert.Equal(61440, BitConverter.ToUInt16(buf[2..4]));  // 120 * 512, above a signed short's range
        Assert.Equal((short)(FadeFlags.Out | FadeFlags.StayOut), BitConverter.ToInt16(buf[4..6]));
        Assert.Equal([(byte)44, 2, 3, 4], buf[6..10].ToArray()); // 300 truncates to 44, as (byte) does
    }

    // ---- who is reached ----------------------------------------------------

    internal sealed record Player(int Slot, Vector Center, bool OnGround);

    /// <summary>A server whose given slots hold players; every other slot is empty.</summary>
    internal static (ScreenEffects Effects, RecordingEngine Engine) World(params Player[] present)
    {
        var bySlot = present.ToDictionary(p => p.Slot);
        var byHandle = present.ToDictionary(p => new EntityHandle((uint)(1000 + p.Slot)));
        var engine = TestServices.Mock();

        object? Arg0() => engine.Calls[^1].Args[0];

        engine.ReturnsEachCall("Players.GetHandle",
            () => bySlot.ContainsKey((int)Arg0()!) ? new EntityHandle((uint)(1000 + (int)Arg0()!)) : EntityHandle.None);
        engine.ReturnsEachCall("Entities.GetWorldSpaceCenter", () => byHandle[(EntityHandle)Arg0()!].Center);
        engine.ReturnsEachCall("Networking.EntityGetState",
            () => byHandle[(EntityHandle)Arg0()!].OnGround ? EntityState.OnGround : default(EntityState));
        engine.ReturnsEachCall("Entities.IsValid", () => byHandle.ContainsKey((EntityHandle)Arg0()!));
        engine.ReturnsEachCall("Entities.IsPlayer", () => byHandle.ContainsKey((EntityHandle)Arg0()!));
        engine.ReturnsEachCall("Entities.GetIndex", () => byHandle[(EntityHandle)Arg0()!].Slot);
        engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);

        return (new ScreenEffects(engine, engine, engine, engine), engine);
    }

    private static List<(string Name, int Slot, byte[] Payload)> Sent(RecordingEngine engine) =>
        engine.Calls.Where(c => c.Member == "UserMessages.Send")
                    .Select(c => ((string)c.Arg(0)!, (int)c.Arg(2)!, (byte[])c.Arg(4)!))
                    .ToList();

    [Fact]
    public void A_shake_reaches_each_grounded_player_in_range_with_its_own_amplitude()
    {
        var (fx, engine) = World(new Player(1, new Vector(0, 0, 0), true),
                                 new Player(3, new Vector(50, 0, 0), true),
                                 new Player(4, new Vector(500, 0, 0), true),   // out of range
                                 new Player(6, new Vector(10, 0, 0), false));  // airborne

        fx.Shake(new Vector(0, 0, 0), 20f, 1f, 2f, 100f);   // 20 clamps to 16

        var sent = Sent(engine);
        Assert.Equal([1, 3], sent.Select(s => s.Slot));
        Assert.All(sent, s => Assert.Equal("Shake", s.Name));
        Assert.Equal(16f, BitConverter.ToSingle(sent[0].Payload, 1));
        Assert.Equal(8f, BitConverter.ToSingle(sent[1].Payload, 1));
    }

    [Fact]
    public void A_stop_reaches_airborne_players_in_range_and_carries_zero()
    {
        var (fx, engine) = World(new Player(2, new Vector(10, 0, 0), false),
                                 new Player(5, new Vector(900, 0, 0), true));  // out of range: ignored even for a stop

        fx.Shake(new Vector(0, 0, 0), 4f, 1f, 2f, 100f, ShakeCommand.Stop);

        var only = Assert.Single(Sent(engine));
        Assert.Equal(2, only.Slot);
        Assert.Equal(0f, BitConverter.ToSingle(only.Payload, 1));
    }

    [Fact]
    public void A_fade_aimed_at_a_player_reaches_only_that_player()
    {
        var (fx, engine) = World(new Player(2, default, true), new Player(7, default, true));

        fx.Fade(new EntityHandle(1007), 1, 2, 3, 4, 1f, 1f, FadeFlags.In);

        var only = Assert.Single(Sent(engine));
        Assert.Equal(("Fade", 7), (only.Name, only.Slot));
        Assert.Equal((short)FadeFlags.In, BitConverter.ToInt16(only.Payload, 4));
    }

    [Fact]
    public void A_fade_aimed_at_no_player_reaches_everyone_and_purges()
    {
        var (fx, engine) = World(new Player(2, default, true), new Player(7, default, false));

        fx.Fade(EntityHandle.None, 1, 2, 3, 4, 1f, 1f, FadeFlags.In);

        var sent = Sent(engine);
        Assert.Equal([2, 7], sent.Select(s => s.Slot));
        Assert.All(sent, s => Assert.Equal((short)(FadeFlags.In | FadeFlags.Purge), BitConverter.ToInt16(s.Payload, 4)));
    }
}
