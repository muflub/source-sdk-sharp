using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;
using SourceSharp.Ai.Server;

namespace SourceSharp.Ai.Tests.Hl2mp;

/// <summary>HL2MP's answers to the server binding (plan_bots_teams_tf2 C.2): what the generic layer asks of the game.</summary>
public class Hl2mpBotGameTests
{
    [Fact]
    public void ABrainRunsTheHl2mpDecider()
    {
        var duel = new Trees.Duel(Trees.Duel.Preset("normal"));
        Assert.IsType<Hl2mpDecider>(TestGames.Hl2mp().Decider(new BrainSetup(new ActorId(1, 1), 7UL, duel.Memory, duel.Params, 0.015f)));
    }

    [Fact]
    public void ABotJoinsInTheModelItsStreamDraws()
    {
        // The same draws as stock's list: the generic bots ask the game, and the stream moves as it did.
        KeyValuePair<string, string>[] asked = TestGames.Hl2mp().UserInfo(new AiRandom(42UL), BotAppearance.Combine, 2);
        Assert.Equal(BotAppearance.For(new AiRandom(42UL), BotAppearance.Combine, 2), asked);
    }

    [Fact]
    public void TheHudCountsTheFragAsGrenades()
        => Assert.Equal(EntityNames.WeaponFrag, TestGames.Hl2mp().GrenadeWeapon);

    [Fact]
    public void OnlyTheFragIsThrown()
    {
        WeaponCatalog weapons = WeaponCatalog.Stock();
        IBotGame game = TestGames.Hl2mp();
        Assert.True(game.Thrown(weapons.For(EntityNames.WeaponFrag)!.Value));
        Assert.False(game.Thrown(weapons.For("weapon_smg1")!.Value));
    }

    [Fact]
    public void Hl2mpPublishesNoAbilities()
        // Its bots act with weapons only (plan_bots_teams_tf2 C.3).
        => Assert.Equal(0, TestGames.Hl2mp().Abilities.Count);

    [Fact]
    public void Hl2mpsMapKnowledgeHasNoAbilitiesAndNoObjectives()
    {
        MapKnowledge map = SourceSharp.Ai.Hl2mp.Knowledge.MapKnowledgeBuilder.Build("dm", new SourceSharp.Ai.Server.Nav.MapEntities([], null), WeaponCatalog.Stock(),
                                                                                    new WeaponKnowledge([]), 20f, 20f);
        Assert.Equal(0, map.Abilities.Count);
        Assert.Empty(map.Objectives.All);
    }
}
