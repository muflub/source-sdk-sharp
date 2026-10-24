using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The mode's half of the death cycle (tf2 stage 0.7 part C): the
/// <see cref="IRespawnPolicy"/> the active mode owns — the wait from the
/// death and the release from the wait — as <c>Players.Think</c> consults
/// it. The core's own answer (3 s, one button press) must survive every
/// arm that does not name a policy, and a mode that does must be able to
/// hold, hasten, and release.
/// </summary>
public class RespawnPolicyTests
{
    private const int PlayerIndex = 3;
    private static readonly EntityHandle Player = new(7u);

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    // The rig's mode pickers: handed to the proxy at construction, as the
    // module hands it the registered chain, and named by Use before the
    // first level asks them.
    private sealed class SelectorList : IHooks<IGameModeSelector>
    {
        public readonly List<IGameModeSelector> Items = new();
        public IReadOnlyList<IGameModeSelector> All => Items;
        public void Each(string what, Action<IGameModeSelector> call) { foreach (var h in Items) call(h); }
    }

    /// <summary>
    /// The policy under test: both numbers settable, both asks counted, and
    /// the handle it was asked for recorded (the wave fact and the
    /// throw-away facts read this state, so it outlives one Think).
    /// </summary>
    private sealed class Policy : IRespawnPolicy
    {
        public float DelaySeconds = 3f;
        public bool Releases = true;
        public int DelayAsks { get; private set; }
        public int WaveAsks { get; private set; }
        public readonly List<string> Asked = [];

        public float Delay(EntityHandle player)
        {
            DelayAsks++;
            Asked.Add($"delay>{player}");
            if (ThrowOn == "Delay") throw new InvalidOperationException("broken mode");
            return DelaySeconds;
        }

        public bool WaveRelease(EntityHandle player, float now)
        {
            WaveAsks++;
            Asked.Add($"wave>{player}>{now.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (ThrowOn == "WaveRelease") throw new InvalidOperationException("broken mode");
            return Releases;
        }

        public string? ThrowOn { get; set; }
    }

    private sealed class Flow : IRoundFlow
    {
        public void Think(float now) { }
        public bool RoundOver => false;
        public void ResetRound() { }
        public void OnPlayerSpawned(EntityHandle player, float now) { }
        public void OnPlayerKilled(PlayerDeathArgs death) { }
    }

    private sealed class Mode(string id, IRespawnPolicy? respawn) : IGameMode
    {
        public string Id { get; } = id;
        public IRoundFlow Rounds { get; } = new Flow();
        public IRespawnPolicy? Respawn { get; } = respawn;
    }

    private sealed class Selector(string id) : IGameModeSelector
    {
        public string? PickMode() => id;
    }

    /// <summary>
    /// A dead-and-asking player over the module's own parts. One engine
    /// double answers host, players and entities, as the death-cycle facts
    /// do; the modes proxy is built the way the module builds it, and given
    /// to the roster through the same Attach the module calls — a fact that
    /// never attaches gets the no-owner arm for free.
    /// </summary>
    private sealed class Rig
    {
        public readonly ConVarRegistry ConVars = ConVarRegistry.For(TestServices.UnattachedConsole());
        public readonly RecordingLog Log = new();
        public readonly RecordingEngine Engine = Fake();
        public readonly ServiceContainer Services = TestServices.Services();
        public readonly GameModes Modes;
        public readonly Players Roster;
        private readonly SelectorList _selectors = new();

        public Rig()
        {
            ConVars.RegisterAll(typeof(GameRules).Assembly);   // the modes' convars
            Engine.Returns("Host.ActiveSide", Side.Server);
            Engine.Returns("Entities.IsValid", true);
            Engine.Returns("Entities.GetIndex", PlayerIndex);
            Engine.Returns("Players.IsAlive", false);
            Modes = new GameModes(Log, Engine, Services, ConVars, _selectors);
            Roster = new Players(Log, Engine, Engine, Engine, NoButtons, new ListHooks<IRespawnGate>())
            {
                DropWeaponOnDeath = false,   // the weapon is not this file's subject
            };
            Roster.AttachModes(Modes);
        }

        /// <summary>Registers the mode and names it, the way the container and a selector do.</summary>
        public void Use(IGameMode mode)
        {
            Services.AddInstance(mode.GetType(), mode);
            _selectors.Items.Clear();
            _selectors.Items.Add(new Selector(mode.Id));
        }

        /// <summary>Kills the player at now=0 and starts the death cam.</summary>
        public void Kill()
        {
            Roster.OnKilled(Player, EntityHandle.None, now: 0f);
            Roster.Think(1f, _ => true);
        }

        /// <summary>Frames from `from` to `to`, asking every step; counts the respawns.</summary>
        public int Think(float from, float to)
        {
            int before = Engine.Calls.Count(c => c.Member == "Players.Respawn");
            for (float t = from; t <= to; t += 0.5f)
                Roster.Think(t, _ => true);
            return Engine.Calls.Count(c => c.Member == "Players.Respawn") - before;
        }
    }

    //-------------------------------------------------------------------------
    // The no-owner arm: the 3-second deathmatch wait, differential form.
    //-------------------------------------------------------------------------

    [Fact]
    public void WithoutAModeTheWaitIsTheCoreSThreeSeconds()
    {
        // Three arms, one answer: never attached (a roster the module built
        // before the proxy existed), attached with no mode registered, and a
        // mode that leaves the concern out — all must wait the core's 3 s,
        // which is what every death-cycle fact before modes existed measured.
        var bare = new Rig();
        bare.Kill();
        Assert.True(bare.Think(2.4f, 2.4f) == 0, "inside the core's 3 s the core holds");
        Assert.True(bare.Think(3.4f, 3.4f) == 1, "and releases when its wait is over");

        var empty = new Rig();
        empty.Kill();
        Assert.True(empty.Think(2.4f, 2.4f) == 0, "an attached proxy with no mode must not answer either");
        Assert.True(empty.Think(3.4f, 3.4f) == 1, "and the wait is still the core's 3 s");

        var noPolicy = new Rig();
        noPolicy.Use(new Mode("arena", respawn: null));   // interface default: no policy
        noPolicy.Kill();
        Assert.True(noPolicy.Think(2.4f, 2.4f) == 0, "a mode that leaves the concern out must not answer");
        Assert.True(noPolicy.Think(3.4f, 3.4f) == 1, "the game the mode left alone is the game that was");
    }

    //-------------------------------------------------------------------------
    // The owner arms: hold, hasten, release — each against the bare arm.
    //-------------------------------------------------------------------------

    [Fact]
    public void ALongerModeDelayHoldsThePlayerDead()
    {
        // BEFORE (the differential): the same frame releases at 3.4 s...
        var bare = new Rig();
        bare.Kill();
        Assert.True(bare.Think(3.4f, 3.4f) == 1, "without the policy the frame releases");

        // AFTER: a 10-second wait holds it, then lets go.
        var rig = new Rig();
        var policy = new Policy { DelaySeconds = 10f };
        rig.Use(new Mode("waves", policy));
        rig.Kill();

        Assert.True(rig.Think(3.4f, 3.4f) == 0, "the mode's wait, not the core's, holds at 3.4 s");
        Assert.True(rig.Think(10.4f, 10.4f) == 1, "and the player returns once it is over");
        Assert.True(policy.DelayAsks > 0, "the policy was never asked: the stimulus did not reach it");
    }

    [Fact]
    public void AShorterModeDelayBringsThemBackSooner()
    {
        var bare = new Rig();
        bare.Kill();
        Assert.True(bare.Think(1.4f, 1.4f) == 0, "the core would still be holding at 1.4 s");

        var rig = new Rig();
        rig.Use(new Mode("arena", new Policy { DelaySeconds = 1f }));
        rig.Kill();

        Assert.True(rig.Think(1.4f, 1.4f) == 1, "the mode's 1 s wait released them");
    }

    [Fact]
    public void AFalseWaveReleaseKeepsThemDeadPastTheDelayUntilTheWaveFires()
    {
        var rig = new Rig();
        var policy = new Policy { DelaySeconds = 0f, Releases = false };
        rig.Use(new Mode("arena", policy));
        rig.Kill();

        // The wait is over immediately (delay 0) — only the wave holds now.
        Assert.True(rig.Think(2f, 4f) == 0, "a held wave keeps the player dead however long the wait was");
        Assert.True(policy.WaveAsks > 0, "the release was never asked: the stimulus did not reach it");

        policy.Releases = true;    // the wave fires
        Assert.True(rig.Think(4.5f, 4.5f) == 1, "the release is what let them out");
    }

    [Fact]
    public void AThrowingPolicyIsNamedAndLeavesTheCoreSWait()
    {
        // A broken mode must not trap a player dead, and the log must name
        // which one. Both halves: the wait falls back to the core's 3 s, and
        // the release falls back to yes.
        var rig = new Rig();
        var policy = new Policy { DelaySeconds = 99f, ThrowOn = "Delay" };
        rig.Use(new Mode("arena", policy));
        rig.Kill();

        Assert.True(rig.Think(2f, 2f) == 0, "the core's wait still holds early frames");
        Assert.True(rig.Think(3.4f, 3.4f) == 1, "and ends at the core's 3 s, not the mode's 99");

        string[] lines = rig.Log.Lines.Select(l => l.Text).ToArray();
        Assert.True(lines.Any(l => l.Contains("RespawnPolicyTests+Policy") && l.Contains("Delay")),
            $"the throwing policy was not named on the log: {string.Join(" | ", lines.Where(l => l.Contains("policy")))}");

        var releaseRig = new Rig();
        var throwsWave = new Policy { DelaySeconds = 0f, ThrowOn = "WaveRelease" };
        releaseRig.Use(new Mode("arena", throwsWave));
        releaseRig.Kill();
        Assert.True(releaseRig.Think(1.5f, 1.5f) == 1, "a throwing release is a yes, not a lock");
    }

    [Fact]
    public void TheHudReadAndTheAsksSeeTheRealPlayer()
    {
        var rig = new Rig();
        var policy = new Policy { DelaySeconds = 10f };
        rig.Use(new Mode("arena", policy));
        rig.Kill();

        // The HUD readout (WaitRemaining) is the mode's wait too — a HUD on
        // the core's 3 s while the mode holds 10 s is a lie by seven seconds.
        Assert.Equal(10f - 2f, rig.Roster.WaitRemaining(Player, 2f), 0.01f);

        Assert.True(policy.Asked.Count > 0 && policy.Asked.All(a => a.Contains("#7")),
            $"the policy was asked about someone else: {string.Join(" | ", policy.Asked)}");
    }
}
