extern alias client;

using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

using TFServerSetup = TF2.Server.Rules.TFServerRoundSetup;
using TFClientSetup = client::TF2.Client.Player.TFClientRoundSetup;

namespace TF2.Tests.Rules;

/// <summary>
/// The two halves of the medigun's setup gate (<see cref="ITFRoundSetup"/>)
/// answer alike: stock's flag is one networked word
/// (<c>m_bInSetup</c>, teamplayroundbased_gamerules.cpp:91/:111) both sides
/// read, so a misprediction in the setup period, the running round after it
/// or the preround freeze names the side that moved.
/// </summary>
public sealed class RoundSetupSidesTests
{
    [Fact]
    public void Both_sides_call_the_running_rounds_setup_period_setup()
    {
        var server = new TFServerSetup(new FixedRound(RoundState.RoundRunning, inSetup: true), WithActiveTimer());
        var client = ClientSetup(RoundState.RoundRunning, inSetup: true);

        Assert.True(server.InSetupWithRoundTimer);
        Assert.Equal(server.InSetupWithRoundTimer, client.InSetupWithRoundTimer);
    }

    [Fact]
    public void Both_sides_call_the_round_after_setup_not_setup()
    {
        var server = new TFServerSetup(new FixedRound(RoundState.RoundRunning), WithActiveTimer());
        var client = ClientSetup(RoundState.RoundRunning, inSetup: false);

        Assert.False(server.InSetupWithRoundTimer);
        Assert.Equal(server.InSetupWithRoundTimer, client.InSetupWithRoundTimer);
    }

    [Fact]
    public void Both_sides_call_the_preround_freeze_not_setup()
    {
        var server = new TFServerSetup(new FixedRound(RoundState.Preround), WithActiveTimer());
        var client = ClientSetup(RoundState.Preround, inSetup: false);

        Assert.False(server.InSetupWithRoundTimer);
        Assert.Equal(server.InSetupWithRoundTimer, client.InSetupWithRoundTimer);
    }

    /// <summary>The client's half over a decoded resource row and a live timer.</summary>
    private static TFClientSetup ClientSetup(RoundState state, bool inSetup)
    {
        var rig = new ResourceRig();
        rig.Arrive(s =>
        {
            s.RoundState = (byte)state;
            s.InSetup = inSetup ? (byte)1 : (byte)0;
        });

        return new TFClientSetup(rig.Resources, new FixedClientTimers(new StubTimer()));
    }

    private static FixedTimers WithActiveTimer() => new() { Active = new FixedTimer() };

    /// <summary>The decoded resource row, arrived the way a packet does.</summary>
    private sealed class ResourceRig
    {
        public readonly client::TF2.Client.Player.TFClientPlayerResources Resources = new();

        public readonly client::TF2.Client.Player.ClientTFPlayerResource Entity;

        public ResourceRig()
        {
            Entity = new client::TF2.Client.Player.ClientTFPlayerResource(
                client::SourceSharp.Abi.RecordingEngine.Default(), Resources);
            Entity.State.BindLocal();
        }

        public void Arrive(Action<client::TF2.Shared.Player.TFPlayerResourceNetState>? write = null)
        {
            var state = new client::TF2.Shared.Player.TFPlayerResourceNetState();
            state.BindLocal();
            write?.Invoke(state);

            byte[] blob = state.Snapshot();
            Entity.State.Decode(blob);
            Entity.OnDataChanged(blob, DataUpdateType.DataTableChanged);
        }
    }

    /// <summary>A stand-in round machine for the server's half.</summary>
    private sealed class FixedRound(RoundState state, bool inSetup = false) : TF2.Server.Rules.ITeamRoundState
    {
        public RoundState State => state;

        public bool InWaitingForPlayers => false;

        public bool InSetup => inSetup;

        public float MapResetTime => 0f;

        public float StalemateStartTime => -1f;

        public TFTeam WinningTeam => TFTeam.Unassigned;
    }

    /// <summary>A stand-in timer set whose HUD timer is elected.</summary>
    private sealed class FixedTimers : TF2.Server.Rules.IRoundTimers
    {
        public TF2.Server.Rules.ITeamRoundTimer? Active { get; set; }

        public void SetActive(TF2.Server.Rules.ITeamRoundTimer? newlyActive) => Active = newlyActive;

        public void HideActive() { }

        public void RestoreActive() { }

        public void ResumeHiddenIfRunning() { }
    }

    private sealed class FixedTimer : TF2.Server.Rules.ITeamRoundTimer
    {
        public EntityHandle Handle { get; } = new(0x0002_0001u);

        public float TimeRemaining => 30f;

        public bool ShowInHud => true;

        public bool IsDisabled => false;

        public bool StartPaused => false;

        public void ResumeTimer() { }

        public void SetShowInHud(bool show) { }

        public void InputShowInHud(bool show) { }
    }

    /// <summary>A stand-in client timer set with its HUD timer elected.</summary>
    private sealed class FixedClientTimers(client::TF2.Client.Entities.ITFRoundTimer? active)
        : client::TF2.Client.Entities.ITFRoundTimers
    {
        public client::TF2.Client.Entities.ITFRoundTimer? Active { get; } = active;

        public System.Collections.Generic.IReadOnlyList<client::TF2.Client.Entities.ITFRoundTimer> All => Array.Empty<client::TF2.Client.Entities.ITFRoundTimer>();

        public int Count => 0;
    }

    /// <summary>A stand-in decoded timer, every field the mirror reads.</summary>
    private sealed class StubTimer : client::TF2.Client.Entities.ITFRoundTimer
    {
        public EntityHandle Handle => default;

        public int Index => 0;

        public float TimeRemaining(float now) => 30f;

        public bool IsPaused => false;

        public bool InSetup => false;

        public bool ShowInHud => true;

        public bool IsDisabled => false;

        public int TimerMaxLength => 0;

        public bool ShowTimeRemaining => false;

        public bool IsStopWatch => false;

        public bool InCaptureWatchState => false;

        public float StopWatchTotalTime => 0f;

        public bool AutoCountdown => false;

        public int InitialLength => 0;

        public int SetupLength => 0;
    }
}
