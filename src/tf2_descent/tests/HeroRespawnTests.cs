using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Mode;
using TF2.Server.Rules;
using TF2.Shared;

using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// The Descent mode's respawn (HE1b): TF2's death cam makes a dead hero an
/// observer, whom the core's button never releases, so the mode brings a dead
/// hero back itself once the core's wait is over and no gate holds them.
/// </summary>
public sealed class HeroRespawnTests
{
    private static readonly EntityHandle Hero = new(0x0001_0001u);

    private sealed class ModeState(bool active) : IDescentModeState
    {
        public bool Active => active;
    }

    private sealed class Roster : ITeamRoundRoster
    {
        public Dictionary<EntityHandle, TFTeam> Teams { get; } = [];
        public Dictionary<EntityHandle, float> Died { get; } = [];
        public List<EntityHandle> Respawned { get; } = [];

        public IReadOnlyList<EntityHandle> Players(TFTeam team) => [.. Teams.Where(t => t.Value == team).Select(t => t.Key)];
        public TFTeam TeamOf(EntityHandle player) => Teams.GetValueOrDefault(player);
        public bool IsAlive(EntityHandle player) => !Died.ContainsKey(player);
        public float DeathTime(EntityHandle player) => Died.TryGetValue(player, out float at) ? at : -1f;
        public bool IsReadyToPlay(EntityHandle player) => true;

        public void ForceRespawn(EntityHandle player)
        {
            Respawned.Add(player);
            Died.Remove(player);
        }

        public bool ChangeTeam(EntityHandle player, TFTeam team) => false;
        public string Name(EntityHandle player) => "hero";
        public int EntIndex(EntityHandle player) => (int)player.EntryIndex;
    }

    private sealed class Records(float delay) : IPlayerDirectory
    {
        public PlayerRecord this[EntityHandle player] => throw new NotSupportedException();
        public bool Knows(EntityHandle player) => false;
        public IEnumerable<(EntityHandle Player, PlayerRecord Record)> All => [];
        public float WaitRemaining(EntityHandle player, float now) => 0f;
        public float RespawnDelay { get; set; } = delay;
        public ObserverMode DeathView { get; set; }
        public bool DropWeaponOnDeath { get; set; }
        public float DropSpeed { get; set; }
        public void OnSpawned(EntityHandle player, float now) { }
        public void OnKilled(EntityHandle player, EntityHandle attacker, float now) { }
        public void Forget(EntityHandle player) { }
    }

    private sealed class Gate(bool allows) : IRespawnGate
    {
        public bool MayRespawn(EntityHandle player, int entIndex) => allows;
    }

    private sealed class Gates(params IRespawnGate[] gates) : IHooks<IRespawnGate>
    {
        public IReadOnlyList<IRespawnGate> All => gates;
        public void Each(string what, Action<IRespawnGate> call) { foreach (var g in gates) call(g); }
    }

    private sealed class NoClock : IGameClock
    {
        public float Now => 0f;
    }

    private static (HeroRespawn Respawn, Roster Roster) Make(bool active = true, params IRespawnGate[] gates)
    {
        var roster = new Roster();
        roster.Teams[Hero] = DescentMode.HeroTeam;
        roster.Died[Hero] = 10f;
        return (new HeroRespawn(new ModeState(active), roster, new Records(3f), new Gates(gates), new NoClock(),
                                new RecordingLog()), roster);
    }

    [Fact]
    public void A_dead_hero_comes_back_once_the_cores_wait_is_over()
    {
        var (respawn, roster) = Make();

        respawn.Think(13f);

        Assert.Equal([Hero], roster.Respawned);
    }

    [Fact]
    public void A_dead_hero_waits_out_the_cores_delay()
    {
        var (respawn, roster) = Make();

        respawn.Think(12.9f);

        Assert.Empty(roster.Respawned);
    }

    [Fact]
    public void A_gate_that_holds_the_hero_keeps_them_dead()
    {
        var (respawn, roster) = Make(true, new Gate(true), new Gate(false));

        respawn.Think(60f);

        Assert.Empty(roster.Respawned);
    }

    [Fact]
    public void A_hero_comes_back_once_and_only_once()
    {
        var (respawn, roster) = Make();

        respawn.Think(13f);
        respawn.Think(14f);

        Assert.Single(roster.Respawned);
    }

    [Fact]
    public void Nobody_is_brought_back_under_another_mode()
    {
        var (respawn, roster) = Make(active: false);

        respawn.Think(60f);

        Assert.Empty(roster.Respawned);
    }
}
