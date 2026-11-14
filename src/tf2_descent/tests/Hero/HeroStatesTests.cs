extern alias client;

using TF2.Descent.Server.Hero;

using Xunit;

using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientHeroEntity = client::TF2.Descent.Client.ClientHeroEntity;
using ClientStandaloneEntity = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using HeroStates = client::TF2.Descent.Client.HeroStates;
using IClientStandalones = client::SourceSharp.Client.IClientStandalones;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// The client's heroes (plan_descent_logic HE2): what the server's
/// <c>tf2_descent_hero</c> state says, read on the client through
/// <see cref="HeroStates"/>, from the bytes the server's state holds.
/// </summary>
public sealed class HeroStatesTests
{
    private sealed class Standalones(List<ClientStandaloneEntity> all) : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => all;
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    /// <summary>The client's entity for a server hero, holding what the server's state holds.</summary>
    private static ClientHeroEntity Received(HeroComponent hero, ClientEngine engine)
    {
        var entity = new ClientHeroEntity(engine);
        entity.State.BindLocal();
        entity.State.Decode(hero.State!.Snapshot());
        return entity;
    }

    private static (HeroRig Rig, HeroComponent Hero, ClientEngine Engine, List<ClientStandaloneEntity> All, HeroStates States) World()
    {
        var rig = new HeroRig();
        var hero = rig.Join()!;
        var engine = new ClientEngine();
        engine.Returns("Hud.LocalPlayerIndex", HeroRig.Index);
        var all = new List<ClientStandaloneEntity>();
        return (rig, hero, engine, all, new HeroStates(new Standalones(all), engine));
    }

    [Fact]
    public void The_client_reads_a_heros_level_xp_points_and_mana_as_the_server_wrote_them()
    {
        var (rig, hero, engine, all, states) = World();
        Assert.True(hero.Apply(s => rig.AtLevel(s, 4)).Ok);
        hero.SetMana(9.75);
        all.Add(Received(hero, engine));

        var seen = states.Of(HeroRig.Index);

        Assert.NotNull(seen);
        Assert.Equal(HeroRig.Index, seen.PlayerIndex);
        Assert.Equal(3, seen.ClassId);    // TFClass.Soldier
        Assert.Equal(4, seen.Level);
        Assert.Equal(hero.Sheet!.Xp, seen.Xp);
        Assert.Equal(rig.Rules.Heroes.XpAtLevel(4), seen.LevelXp);
        Assert.Equal(rig.Rules.Heroes.XpAtLevel(5), seen.NextLevelXp);
        Assert.Equal(15, seen.AttributePoints);
        Assert.Equal(3, seen.SkillPoints);
        Assert.Equal(9, seen.Mana);
        Assert.Equal(hero.State!.ManaMax, seen.ManaMax);
        Assert.Equal(hero.State.LifeMax, seen.LifeMax);
        Assert.Equal(0, seen.LevelProgress);
    }

    [Fact]
    public void The_local_hero_is_the_local_players()
    {
        var (rig, hero, engine, all, states) = World();
        rig.Players.Steam[5] = HeroRig.SteamId + 5;
        var other = rig.Join(5)!;
        all.Add(Received(other, engine));
        all.Add(Received(hero, engine));

        Assert.Equal(HeroRig.Index, states.Local()!.PlayerIndex);
        Assert.Equal(5, states.Of(5)!.PlayerIndex);
        Assert.Equal([HeroRig.Index, 5], states.All().Select(h => h.PlayerIndex));
    }

    [Fact]
    public void A_player_with_no_hero_entity_has_no_hero_state()
    {
        var (_, _, _, _, states) = World();

        Assert.Null(states.Of(HeroRig.Index));
        Assert.Null(states.Local());
        Assert.Empty(states.All());
    }

    [Fact]
    public void The_xp_bar_fills_by_the_share_of_the_level_earned()
    {
        var (rig, hero, engine, all, states) = World();
        long half = (rig.Rules.Heroes.XpAtLevel(3) - rig.Rules.Heroes.XpAtLevel(2)) / 2;
        Assert.True(hero.Apply(s => rig.Rules.Heroes.GainXp(rig.AtLevel(s, 2), half).Sheet).Ok);
        all.Add(Received(hero, engine));

        Assert.Equal(0.5, states.Local()!.LevelProgress, 2);
    }
}
