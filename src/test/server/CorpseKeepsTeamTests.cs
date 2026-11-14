using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A body keeps the team it died on (plan_bots_teams_tf2 A.8, ruled
/// 2026-11-02), server half: the corpse takes its model and its team off
/// the dying player at the death and never looks at its owner again, so a
/// re-dress afterwards (team deathmatch moves the player, kills them, then
/// PlayerModels re-dresses them: TeamDeathmatchJoinTests'
/// TheSwitchKillLandsBeforeAWatcherRedressesThePlayer) leaves the body as it was.
/// </summary>
public class CorpseKeepsTeamTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);
    private static readonly EntityHandle Body = new(0x0005_0020u);
    private const string Rebel = "models/humans/group03/male_01.mdl";
    private const string Combine = "models/combine_soldier.mdl";

    [Fact]
    public void ABodyMadeBeforeASwapKeepsItsModelAndTeamAfterIt()
    {
        var services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("EntityRender.GetModelName", Rebel)
              .Returns("Players.GetTeam", TeamDeathmatchMode.TeamRebels)
              .Returns("Entities.GetClassName", EntityNames.Player)
              .Returns("Entities.Create", Body)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.GetIndex", 1);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsRagdoll).Assembly);
        registry.Services = services;
        registry.KeyValue(Player, EntityNames.Player, "targetname", "victim");
        registry.Spawn(Body, EntityNames.Hl2mpRagdoll);
        var corpses = new Corpses(new RecordingLog(), engine, engine, registry, players: engine);
        var body = Assert.IsType<SsRagdoll>(registry.Get(Body));

        Assert.Equal(Body, corpses.Make(Player));

        // The swap: the owner is on the other team and wears its model.
        engine.Returns("Players.GetTeam", TeamDeathmatchMode.TeamCombine)
              .Returns("EntityRender.GetModelName", Combine);

        Assert.Equal(TeamDeathmatchMode.TeamRebels, body.State.Team);
        var dressed = engine.AllOf("EntityRender.SetModel").Where(c => c.ArgAs<EntityHandle>(0) == Body).ToList();
        Assert.Equal([Rebel], dressed.Select(c => c.ArgAs<string>(1)));
    }
}
