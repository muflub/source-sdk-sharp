extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientEffects = client::SourceSharp.Abi.RecordingEngine;
using Burn = client::TF2.Client.Player.TFBurningEffect;
using Hook = client::TF2.Client.Player.ITFClientConditionHook;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using Cond = client::TF2.Shared.TFCond;
using TFTeam = client::TF2.Shared.TFTeam;

namespace TF2.Tests.Client;

/// <summary>
/// The burning player's particle, stock's <c>OnAddBurning</c> client half
/// (tf_player_shared.cpp:7296-7307) through the condition driver: the team's
/// name, the guard, the pyro-vision skip, and the removal's stop.
/// </summary>
public sealed class TFBurningEffectTests
{
    private static readonly EntityHandle Player = new(0x0002_0004u);

    [Fact]
    public void A_red_players_burn_is_burningplayer_red()
    {
        var rig = Red();

        rig.Burn.OnAdded(Player, Cond.Burning);

        var start = Assert.Single(rig.Engine.Calls, c => c.Member == "Effects.ParticleSystemStart");
        Assert.Equal(Player, start.ArgAs<EntityHandle>(0));
        Assert.Equal("burningplayer_red", start.ArgAs<string>(1));
    }

    [Fact]
    public void Every_other_team_wears_burningplayer_blue()
    {
        var rig = Team(TFTeam.Blue);

        rig.Burn.OnAdded(Player, Cond.Burning);

        Assert.Equal("burningplayer_blue", rig.LastStartName());
    }

    [Fact]
    public void A_player_with_no_resource_yet_wears_blue()
    {
        var rig = NoResource();

        rig.Burn.OnAdded(Player, Cond.Burning);

        Assert.Equal("burningplayer_blue", rig.LastStartName());
    }

    [Fact]
    public void Only_conditions_named_burning_reach_the_arm()
    {
        var rig = Red();

        rig.Burn.OnAdded(Player, Cond.Stealthed);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Effects.ParticleSystemStart");
    }

    [Fact]
    public void The_removal_stops_a_burn_that_started()
    {
        var rig = Red();

        rig.Burn.OnAdded(Player, Cond.Burning);
        rig.Burn.OnRemoved(Player, Cond.Burning);

        var stop = Assert.Single(rig.Engine.Calls, c => c.Member == "Effects.ParticleSystemStop");
        Assert.Equal(Player, stop.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void A_second_add_for_a_burning_player_starts_nothing()
    {
        var rig = Red();

        rig.Burn.OnAdded(Player, Cond.Burning);
        var afterFirst = rig.Engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStart");
        Assert.Equal(1, afterFirst);

        rig.Burn.OnAdded(Player, Cond.Burning);

        Assert.Equal(afterFirst, rig.Engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStart"));
    }

    [Fact]
    public void The_local_pyers_own_burn_wears_nothing()
    {
        var rig = Red(cvars: new() { ["localplayer_visionflags"] = "1" }, localPlayer: 4);

        rig.Burn.OnAdded(Player, Cond.Burning);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member.StartsWith("Effects.ParticleSystem"));
    }

    [Fact]
    public void Another_players_burn_survives_the_locals_pyro_vision()
    {
        var rig = Red(cvars: new() { ["localplayer_visionflags"] = "1" }, localPlayer: 7);

        rig.Burn.OnAdded(Player, Cond.Burning);

        Assert.Equal("burningplayer_red", rig.LastStartName());
    }

    [Fact]
    public void A_visionless_locals_burn_shows()
    {
        var rig = Red(cvars: [], localPlayer: 4);

        rig.Burn.OnAdded(Player, Cond.Burning);

        Assert.Equal("burningplayer_red", rig.LastStartName());
    }

    /// <summary>The arm over a red team resource, the start always succeeding.</summary>
    private static Rig Red(Dictionary<string, string>? cvars = null, int localPlayer = 4) =>
        Team(TFTeam.Red, cvars, localPlayer);

    private static Rig Team(TFTeam team, Dictionary<string, string>? cvars = null, int localPlayer = 4)
    {
        var engine = new ClientEffects();
        engine.Returns("Effects.ParticleSystemStart", true);
        engine.Returns("Hud.LocalPlayerIndex", localPlayer);

        var resources = new PlayerResources();
        var resource = new client::TF2.Client.Player.ClientTFPlayerResource(engine, resources);
        resource.State.BindLocal();
        resource.OnSpawn();
        resource.State.Team[4] = (byte)team;

        return new Rig(engine, Arm(engine, new ConsoleFake(cvars ?? []), resources));
    }

    private static Rig NoResource()
    {
        var engine = new ClientEffects();
        engine.Returns("Effects.ParticleSystemStart", true);
        engine.Returns("Hud.LocalPlayerIndex", 4);

        return new Rig(engine, Arm(engine, new ConsoleFake([]), new PlayerResources()));
    }

    private static Burn Arm(ClientEffects engine, client::SourceSharp.Abi.IEngineConsole console,
                            PlayerResources resources) =>
        new(engine, engine, console, resources);

    private sealed record Rig(ClientEffects Engine, Burn Burn)
    {
        public string? LastStartName()
        {
            var starts = Engine.Calls.Where(c => c.Member == "Effects.ParticleSystemStart").ToList();

            return starts.Count == 0 ? null : starts[^1].ArgAs<string>(1);
        }
    }

    private sealed class ConsoleFake(Dictionary<string, string> values) : client::SourceSharp.Abi.IEngineConsole
    {
        public void ExecuteServerCommand(string command) { }
        public string? FindConVarValue(string name) => values.GetValueOrDefault(name);
        public string GetConVarValue(int id) => throw new NotSupportedException();
        public void Msg(string message) { }
        public int RegisterConCommand(string name, string help, client::SourceSharp.Abi.ConVarFlags flags) => 0;
        public int RegisterConVar(string name, string defaultValue, string help, int flags, float? min = null, float? max = null) => 0;
        public void SendClientCommand(EntityHandle player, string command) { }
        public void SetConVarValue(int id, string value) { }
        public void SetConVarValueByName(string name, string value) { }
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out client::SourceSharp.Abi.NativeConsoleEntry entry) { entry = default; return false; }
        public void Warning(string message) { }
    }
}
