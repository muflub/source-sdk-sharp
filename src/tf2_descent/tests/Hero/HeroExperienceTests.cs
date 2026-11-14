using System.Collections.Immutable;

using SourceSharp.Abi;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Bestiary;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Party;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// HE5's XP (plan_descent_logic HE5, plan_tf2_rpg D10, L3, L6): a robot's
/// death pays each living hero within the rules' party range the rules' kill
/// XP by his own level; the sheet takes it through the rules' level-up, the
/// evidence records it with the kill, the bestiary counts the kill, and a
/// level-up refills life and mana, tells the hero and asks for a checkpoint.
/// </summary>
public sealed class HeroExperienceTests
{
    private const int Other = 4;
    private const int Far = 5;

    private sealed class Checkpoints : IHeroLevelCheckpoint
    {
        public List<int> Asked { get; } = [];
        public bool Checkpoint(int entIndex)
        {
            Asked.Add(entIndex);
            return true;
        }
    }

    private sealed class XpRig
    {
        public readonly HeroRig Rig = new();
        public readonly Checkpoints Checkpoints = new();
        public readonly Dictionary<EntityHandle, Vector> Origins = new();
        public readonly HashSet<EntityHandle> DeadPlayers = [];
        public readonly HeroExperience Xp;
        public readonly InMemoryParties Parties;

        /// <summary>Whether each hero joins the one party the rig keeps (WO9); false leaves him solo.</summary>
        public bool JoinParty { get; set; } = true;

        public XpRig()
        {
            Rig.Engine.ReturnsEachCall("Entities.GetOrigin", () => Origins.GetValueOrDefault(Rig.Engine.Calls[^1].ArgAs<EntityHandle>(0)));
            Rig.Engine.ReturnsEachCall("Players.IsAlive", () => !DeadPlayers.Contains(Rig.Engine.Calls[^1].ArgAs<EntityHandle>(0)));
            Parties = new InMemoryParties(Rig.Rules, new Party.PartyChanges().Hooks);
            var roster = new PartyRoster(Parties, new EnginePartySlots(Rig.Heroes, Rig.Keys()));
            Xp = new HeroExperience(Rig.Heroes, Rig.Sheets, Rig.Engine, Rig.Engine, roster, new ClientMessages(Rig.Engine), Checkpoints, Rig.Log);
        }

        private readonly List<string> _party = [];

        public TF2.Descent.Rules.TF2DescentRules Rules => Rig.Rules;

        /// <summary>A hero in <paramref name="index"/> at <paramref name="level"/>, standing at <paramref name="at"/>.</summary>
        public HeroComponent Hero(int index = HeroRig.Index, int level = 1, Vector at = default)
        {
            Rig.Players.Steam[index] = HeroRig.SteamId + (ulong)index;
            var hero = Rig.Join(index)!;
            if (level > 1)
                Assert.True(hero.Apply(s => Rig.AtLevel(s, level)).Ok);
            Origins[HeroRig.PlayerOf(index)] = at;
            if (JoinParty)
            {
                _party.Add(Rig.Keys().Of(index)!);
                if (_party.Count >= 2)
                    Assert.True(Parties.Make(_party).GetAwaiter().GetResult().Ok);
            }
            return hero;
        }

        /// <summary>A kill of <paramref name="template"/> at <paramref name="level"/>, where it died and by whom.</summary>
        public RobotKill Kill(string template = "soldier", int level = 5, Vector at = default, int killer = HeroRig.Index, uint sequence = 0)
        {
            var stats = Rules.Robots.Stats(Rules.Robots.Template(template), 1.0, level, []);
            return new RobotKill(sequence, new RobotId(0, 1), template, level, 1.0, ImmutableArray<string>.Empty, false, stats, at,
                                 killer, killer > 0 ? HeroRig.PlayerOf(killer) : EntityHandle.None);
        }

        public long KillXp(RobotKill kill, int heroLevel) => Rules.Experience.KillXp(kill.Stats, heroLevel);

        public List<string> Notices() =>
            Rig.Engine.Calls.Where(c => c.Member == "UserMessages.Send" && (string)c.Arg(0)! == "TF2.Descent:notice")
                .Select(c => System.Text.Encoding.UTF8.GetString((byte[])c.Arg(4)!)).ToList();

        public int HealthSets(int index) =>
            Rig.Engine.Calls.Count(c => c.Member == "Entities.SetHealth" && c.ArgAs<EntityHandle>(0) == HeroRig.PlayerOf(index));
    }

    // ---- who a kill pays (WO9: the killer's party in range) ----

    [Fact]
    public void A_stranger_in_range_of_a_heros_kill_is_paid_nothing()
    {
        var x = new XpRig { JoinParty = false };
        x.Hero(level: 5);
        var stranger = x.Hero(Other, level: 5);
        long before = stranger.Sheet!.Xp;

        var paid = x.Xp.Pay(x.Kill(level: 5));

        Assert.Equal([HeroRig.Index], paid.Select(g => g.PlayerIndex));
        Assert.Equal(before, stranger.Sheet!.Xp);
    }

    [Fact]
    public void A_party_member_in_range_of_the_killers_kill_is_paid_in_full_by_his_own_level()
    {
        var x = new XpRig();
        x.Hero(level: 5);
        var member = x.Hero(Other, level: 3);
        var kill = x.Kill(level: 5);

        x.Xp.Pay(kill);

        Assert.Equal(x.Rules.Heroes.XpAtLevel(3) + x.KillXp(kill, 3), member.Sheet!.Xp);
    }

    [Fact]
    public void A_party_member_out_of_range_is_paid_nothing()
    {
        var x = new XpRig();
        x.Hero(level: 5);
        var far = x.Hero(Far, level: 5, at: new Vector((float)x.Rules.Content.Experience.PartyRangeUnits + 1, 0, 0));

        var paid = x.Xp.Pay(x.Kill(level: 5));

        Assert.DoesNotContain(Far, paid.Select(g => g.PlayerIndex));
        Assert.Equal(x.Rules.Heroes.XpAtLevel(5), far.Sheet!.Xp);
    }

    [Fact]
    public void A_kill_by_no_hero_pays_every_hero_in_range_party_or_not()
    {
        var x = new XpRig { JoinParty = false };
        x.Hero(level: 5);
        x.Hero(Other, level: 5);

        var paid = x.Xp.Pay(x.Kill(level: 5, killer: 0));

        Assert.Equal([HeroRig.Index, Other], paid.Select(g => g.PlayerIndex));
    }

    // ---- what a kill pays ----

    [Fact]
    public void A_hero_below_the_robots_level_is_paid_the_rules_xp()
    {
        var x = new XpRig();
        var hero = x.Hero(level: 2);
        var kill = x.Kill(level: 8);

        x.Xp.Pay(kill);

        Assert.Equal(x.Rules.Heroes.XpAtLevel(2) + x.KillXp(kill, 2), hero.Sheet!.Xp);
        Assert.True(x.KillXp(kill, 2) > (long)Math.Round(kill.Stats.Xp), "the stimulus: a weaker hero earns more than the robot's own XP");
    }

    [Fact]
    public void A_hero_at_the_robots_level_is_paid_the_robots_xp()
    {
        var x = new XpRig();
        var hero = x.Hero(level: 5);
        var kill = x.Kill(level: 5);

        x.Xp.Pay(kill);

        Assert.Equal(x.Rules.Heroes.XpAtLevel(5) + (long)Math.Round(kill.Stats.Xp, MidpointRounding.AwayFromZero), hero.Sheet!.Xp);
    }

    [Fact]
    public void A_hero_above_the_robots_level_is_paid_the_rules_reduced_xp()
    {
        var x = new XpRig();
        var hero = x.Hero(level: 12);
        var kill = x.Kill(level: 4);

        x.Xp.Pay(kill);

        Assert.Equal(x.Rules.Heroes.XpAtLevel(12) + x.KillXp(kill, 12), hero.Sheet!.Xp);
        Assert.True(x.KillXp(kill, 12) < (long)Math.Round(kill.Stats.Xp), "the stimulus: a stronger hero earns less");
    }

    [Fact]
    public void Two_heroes_in_range_are_each_paid_in_full_by_their_own_level_and_one_out_of_range_is_not()
    {
        var x = new XpRig();
        double range = x.Rules.Content.Experience.PartyRangeUnits;
        var near = x.Hero(HeroRig.Index, level: 2, at: new Vector(100, 0, 0));
        var other = x.Hero(Other, level: 7, at: new Vector(0, (float)range - 1, 0));
        var far = x.Hero(Far, level: 2, at: new Vector((float)range + 1, 0, 0));
        var kill = x.Kill(level: 6);

        var paid = x.Xp.Pay(kill);

        Assert.Equal(x.Rules.Heroes.XpAtLevel(2) + x.KillXp(kill, 2), near.Sheet!.Xp);
        Assert.Equal(x.Rules.Heroes.XpAtLevel(7) + x.KillXp(kill, 7), other.Sheet!.Xp);
        Assert.Equal(x.Rules.Heroes.XpAtLevel(2), far.Sheet!.Xp);
        Assert.Equal([HeroRig.Index, Other], paid.Select(p => p.PlayerIndex).Order());
    }

    [Fact]
    public void A_kill_by_the_world_pays_the_heroes_in_range()
    {
        var x = new XpRig();
        var hero = x.Hero();

        x.Xp.Pay(x.Kill(killer: 0));

        Assert.True(hero.Sheet!.Xp > 0);
    }

    [Fact]
    public void The_killer_out_of_range_is_not_paid_and_does_not_stop_the_others()
    {
        var x = new XpRig();
        double range = x.Rules.Content.Experience.PartyRangeUnits;
        var killer = x.Hero(HeroRig.Index, at: new Vector((float)range * 2, 0, 0));
        var mate = x.Hero(Other);

        x.Xp.Pay(x.Kill(killer: HeroRig.Index));

        Assert.Equal(0, killer.Sheet!.Xp);
        Assert.True(mate.Sheet!.Xp > 0);
    }

    [Fact]
    public void A_dead_hero_is_not_paid()
    {
        var x = new XpRig();
        var hero = x.Hero();
        x.DeadPlayers.Add(HeroRig.PlayerOf(HeroRig.Index));

        Assert.Empty(x.Xp.Pay(x.Kill()));
        Assert.Equal(0, hero.Sheet!.Xp);
    }

    [Fact]
    public void The_kill_hook_pays()
    {
        var x = new XpRig();
        var hero = x.Hero();

        ((IRobotKillHook)x.Xp).OnRobotKilled(x.Kill());

        Assert.True(hero.Sheet!.Xp > 0);
    }

    // ---- evidence and the bestiary ----

    [Fact]
    public void The_xp_and_the_kill_are_recorded_as_evidence()
    {
        var x = new XpRig();
        var hero = x.Hero(level: 3);
        var kill = x.Kill("heavy", level: 3);

        x.Xp.Pay(kill);

        var pending = x.Rig.Sheets.Pending(HeroRig.Index);
        Assert.Equal(x.KillXp(kill, 3), pending.XpEarned);
        Assert.Equal(1, pending.KillsOf("heavy"));
    }

    [Fact]
    public void The_bestiary_counts_the_kill_for_every_hero_paid()
    {
        var x = new XpRig();
        var hero = x.Hero();
        var mate = x.Hero(Other);

        x.Xp.Pay(x.Kill("heavy"));
        x.Xp.Pay(x.Kill("heavy", sequence: 1));

        Assert.Equal(2, BestiaryRules.Kills(hero.Sheet!, "heavy"));
        Assert.Equal(2, BestiaryRules.Kills(mate.Sheet!, "heavy"));
    }

    // ---- the level-up ----

    [Fact]
    public void A_level_up_refills_life_and_mana_from_one_hp_and_none()
    {
        var x = new XpRig();
        var hero = x.Hero();
        x.Rig.Engine.Returns("Entities.GetHealth", 1);
        hero.SetMana(0);
        long toNext = x.Rules.Heroes.XpAtLevel(2);
        var kill = x.Kill(level: 30);
        Assert.True(x.KillXp(kill, 1) >= toNext, "the stimulus: the kill is worth a level");

        x.Xp.Pay(kill);

        Assert.True(hero.Sheet!.Level >= 2);
        Assert.Equal(1, x.HealthSets(HeroRig.Index));
        var set = x.Rig.Engine.Calls.Last(c => c.Member == "Entities.SetHealth");
        Assert.Equal(x.Rig.Health.Max, set.ArgAs<int>(1));
        Assert.Equal(hero.Stats!.MaxMana, hero.Mana);
    }

    [Fact]
    public void A_level_up_grants_its_points()
    {
        var x = new XpRig();
        var hero = x.Hero();
        var kill = x.Kill(level: 30);

        var gain = Assert.Single(x.Xp.Pay(kill));

        int levels = hero.Sheet!.Level - 1;
        Assert.True(levels >= 1);
        Assert.Equal(levels, gain.Result.LevelsGained);
        Assert.Equal(5 * levels, hero.Sheet.AttributePoints);
        Assert.Equal(levels, hero.Sheet.SkillPoints);
    }

    [Fact]
    public void Several_levels_from_one_kill_grant_all_their_points_and_one_refill_one_notice_one_checkpoint()
    {
        var x = new XpRig();
        var hero = x.Hero();
        x.Rig.Engine.Returns("Entities.GetHealth", 1);
        var kill = x.Kill("heavy", level: 40);

        x.Xp.Pay(kill);

        int levels = hero.Sheet!.Level - 1;
        Assert.True(levels >= 2, $"the stimulus: one kill is worth several levels ({levels})");
        Assert.Equal(5 * levels, hero.Sheet.AttributePoints);
        Assert.Equal(levels, hero.Sheet.SkillPoints);
        Assert.Equal(1, x.HealthSets(HeroRig.Index));
        Assert.Single(x.Notices(), n => n.Contains(HeroExperience.LevelUpToken));
        Assert.Equal([HeroRig.Index], x.Checkpoints.Asked);
    }

    [Fact]
    public void The_level_up_notice_names_the_new_level()
    {
        var x = new XpRig();
        var hero = x.Hero();

        x.Xp.Pay(x.Kill(level: 30));

        string notice = Assert.Single(x.Notices());
        Assert.Contains(HeroExperience.LevelUpToken, notice);
        Assert.Contains(hero.Sheet!.Level.ToString(System.Globalization.CultureInfo.InvariantCulture), notice);
    }

    [Fact]
    public void A_kill_with_no_level_up_asks_no_checkpoint_and_no_refill()
    {
        var x = new XpRig();
        var hero = x.Hero(level: 10);
        var kill = x.Kill(level: 1);

        x.Xp.Pay(kill);

        Assert.Equal(10, hero.Sheet!.Level);
        Assert.True(hero.Sheet.Xp > x.Rules.Heroes.XpAtLevel(10), "the stimulus: the kill paid");
        Assert.Empty(x.Checkpoints.Asked);
        Assert.Equal(0, x.HealthSets(HeroRig.Index));
        Assert.Empty(x.Notices());
    }

    [Fact]
    public void At_the_level_cap_nothing_more_is_granted()
    {
        var x = new XpRig();
        int cap = x.Rules.Heroes.LevelCap;
        var hero = x.Hero(level: cap);
        var before = hero.Sheet!;

        x.Xp.Pay(x.Kill(level: 40));

        Assert.Equal(cap, hero.Sheet!.Level);
        Assert.Equal(before.Xp, hero.Sheet.Xp);
        Assert.Equal(before.AttributePoints, hero.Sheet.AttributePoints);
        Assert.Equal(before.SkillPoints, hero.Sheet.SkillPoints);
        Assert.Empty(x.Checkpoints.Asked);
        Assert.Equal(0, x.Rig.Sheets.Pending(HeroRig.Index).XpEarned);
    }
}
