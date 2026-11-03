extern alias client;

using Xunit;

using ClientBob = client::TF2.Client.Weapons.TFViewmodelBob;
using ClientState = client::TF2.Client.Weapons.TFViewmodelBob.State;
using ClientCycle = client::TF2.Client.ClBobCycleConVar;
using ClientUp = client::TF2.Client.ClBobUpConVar;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientStandalones = client::SourceSharp.Client.IClientStandalones;
using ClientPlayer = client::SourceSharp.Client.Standalone.ClientSsPlayer;
using ClientVector = SourceSharp.Abi.Vector;
using ClientEntityHandle = SourceSharp.Abi.EntityHandle;

namespace TF2.Tests.Weapons;

/// <summary>
/// The viewmodel bob's state machine, against the numbers stock's
/// <c>CalcViewModelBobHelper</c> computes (tf_weaponbase.cpp:4713-4796): the
/// guards, the speed clamps, the phase and its two periods, and the wiring
/// that reads the local player.
/// </summary>
public class TFViewmodelBobFacts
{
    private const float Cycle = 0.8f;
    private const float Up = 0.5f;

    [Fact]
    public void A_full_speed_second_lands_on_stocks_vertical_and_lateral()
    {
        // dt 1 s at 320 u/s: bobTime 1, the vertical a quarter cycle in (sin 1),
        // the lateral wrapped by stock's truncating twos-complement of the
        // doubled period to -0.125 (sin -sqrt(1/2)).
        var state = ClientBob.Advance(default, 1f, 0.016f, havePlayer: true, 320f, Up, Cycle);

        Assert.Equal(1f, state.BobTime);
        Assert.Equal(1.6f, state.Vertical, 4);
        Assert.Equal(-0.311959f, state.Lateral, 4);
    }

    [Fact]
    public void The_phase_carries_into_the_next_second()
    {
        var first = ClientBob.Advance(default, 1f, 0.016f, true, 320f, Up, Cycle);
        var second = ClientBob.Advance(first, 2f, 0.016f, true, 320f, Up, Cycle);

        // bobTime 2: half a cycle later the vertical sine crosses zero, and the
        // doubled period wraps the lateral to a whole negative revolution.
        Assert.Equal(0.48f, second.Vertical, 4);
        Assert.Equal(1.6f, second.Lateral, 4);
    }

    [Fact]
    public void A_zero_bobup_still_divides()
    {
        var state = ClientBob.Advance(new ClientState(0f, 1f, 0f, 0f, 0f), 2.005f, 0.016f, true, 320f, 0f, 1f);

        // The 0.01 substitute puts the quarter-cycle hinge inside the 0.005
        // fraction of the period, so the vertical sine is at its peak.
        Assert.Equal(1.6f, state.Vertical, 4);
    }

    [Fact]
    public void A_zero_bobcycle_still_divides()
    {
        var state = ClientBob.Advance(default, 1f, 0.016f, true, 320f, Up, 0f);

        // One whole 0.01 period elapsed: the sine is back at zero, and the
        // magnitude is the bias alone.
        Assert.Equal(0.48f, state.Vertical, 4);
    }

    [Fact]
    public void The_speed_change_is_clamped_to_the_seconds_budget()
    {
        // A hundredth of a second after standing still allows 3.2 u/s of
        // change, whatever the 100 u/s the player now has.
        var state = ClientBob.Advance(new ClientState(0f, 1f, 0f, 0f, 0f), 1.01f, 0.016f, true, 100f, Up, Cycle);

        Assert.Equal(3.2f, state.LastSpeed, 3);
    }

    [Fact]
    public void The_speed_is_clamped_to_the_plus_minus_320_floor()
    {
        var state = ClientBob.Advance(new ClientState(0f, 0f, 1000f, 0f, 0f), 1f, 0.016f, true, 1000f, Up, Cycle);

        Assert.Equal(320f, state.LastSpeed, 4);
        Assert.Equal(1.6f, state.Vertical, 4);
    }

    [Fact]
    public void A_zero_frame_keeps_the_last_pair_and_the_clock()
    {
        var state = new ClientState(5f, 5f, 40f, 3f, -1f);

        Assert.Equal(state, ClientBob.Advance(state, 9f, 0f, true, 100f, Up, Cycle));
    }

    [Fact]
    public void No_player_keeps_the_last_pair_and_the_clock()
    {
        var state = new ClientState(5f, 5f, 40f, 3f, -1f);

        Assert.Equal(state, ClientBob.Advance(state, 9f, 0.016f, havePlayer: false, 100f, Up, Cycle));
    }

    // ---------------------------------------------------------------------
    // The wiring: the local player's horizontal speed, the convars, the pair.
    // ---------------------------------------------------------------------

    private sealed class Standalones : ClientStandalones
    {
        public readonly Dictionary<int, ClientPlayer> ByIndex = new();
        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => ByIndex.Values.ToList();
        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => ByIndex.TryGetValue(entityIndex, out var entity) ? entity : null;
    }

    private static (ClientBob Bob, ClientPlayer Player) Rig(float x, float y, float z, int localIndex = 1)
    {
        var engine = new ClientEngine();
        engine.Returns("Hud.LocalPlayerIndex", localIndex);
        var player = new ClientPlayer(engine) { Handle = new ClientEntityHandle(0x0001_0001u) };
        player.State.BindLocal();
        player.State.Velocity = new ClientVector(x, y, z);

        var cycle = new ClientCycle();
        cycle.Bind(-1, "cl_bobcycle", "0.8");
        var up = new ClientUp();
        up.Bind(-1, "cl_bobup", "0.5");

        var standalones = new Standalones { ByIndex = { [1] = player } };
        return (new ClientBob(cycle, up, standalones, engine), player);
    }

    [Fact]
    public void Tick_bobs_by_the_horizontal_speed_alone()
    {
        var (bob, _) = Rig(240f, 0f, 30f);

        Span<float> pair = stackalloc float[2];
        bob.Tick(1f, 0.016f, pair);

        // 240 u/s is a three-quarter offset: the vertical past the hinge, the
        // lateral still inside its first half, and the 30 u/s of fall ignored.
        Assert.Equal(0.038546f, pair[0], 4);
        Assert.Equal(0.523876f, pair[1], 4);
    }

    [Fact]
    public void Tick_without_a_local_player_reports_the_last_pair()
    {
        var (bob, _) = Rig(240f, 0f, 0f, localIndex: 0);

        Span<float> pair = stackalloc float[2];
        bob.Tick(1f, 0.016f, pair);
        Assert.Equal(0f, pair[0], 4);
        Assert.Equal(0f, pair[1], 4);

        bob.Tick(2f, 0.016f, pair);
        Assert.Equal(0f, pair[0], 4);
    }

    [Fact]
    public void Tick_carries_the_state_between_frames()
    {
        var (bob, _) = Rig(320f, 0f, 0f);

        Span<float> first = stackalloc float[2];
        Span<float> second = stackalloc float[2];
        bob.Tick(1f, 0.016f, first);
        bob.Tick(2f, 0.016f, second);

        Assert.Equal(1.6f, first[0], 4);
        Assert.Equal(0.48f, second[0], 4);
    }
}
