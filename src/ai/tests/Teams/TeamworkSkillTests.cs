using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Tests.Skills;
using Xunit;
using SourceSharp.Ai.Server;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// Teamwork, the eleventh row (plan_bots_teams_tf2 B.7, Q4): a profile that carries it
/// spends on it like any skill; a preset (ten rows) reads it at its own mean and spends
/// nothing on it. It drives the leash, the callout chance, how long a bot follows, cover
/// of a teammate and the Trade and Crossfire utilities, and is never a spread's favourite.
/// </summary>
public sealed class TeamworkSkillTests
{
    private static SkillProfile WithTeamwork(int teamwork, params int[] ten)
        => new("tw", StandardSkills.Eleven, [.. ten, teamwork]);

    private static readonly int[] Flat = [3, 3, 3, 3, 3, 3, 3, 3, 3, 3];

    [Fact]
    public void TeamworkIsTheEleventhRow()
        => Assert.Equal(("teamwork", 11), (StandardSkills.Eleven[^1].Key, StandardSkills.TeamPool().Skills.Length));

    [Fact]
    public void AProfilesOwnTeamworkIsRead() => Assert.Equal(9f, StandardSkills.TeamworkOf(WithTeamwork(9, Flat)));

    /// <summary>A preset spends nothing on it: Medium (35 over ten) reads 3, its mean.</summary>
    [Fact]
    public void APresetReadsTeamworkAtItsMean()
    {
        SkillPresets.TryGet("normal", out SkillProfile? normal);
        Assert.Equal((10, 35, 3f), (normal!.Levels.Length, normal.Spent, StandardSkills.TeamworkOf(normal)));
    }

    [Fact]
    public void ElevenLevelsParseOverTheTeamPool()
    {
        Assert.True(BotSkillSpec.Parse("x", "skills=3,3,3,3,3,3,3,3,3,3,8", out SkillProfile? p).Accepted);
        Assert.Equal(8, p!["teamwork"]);
    }

    [Fact]
    public void TenLevelsStillParseOverTheTen()
    {
        Assert.True(BotSkillSpec.Parse("x", "skills=3,3,3,3,3,3,3,3,3,3", out SkillProfile? p).Accepted);
        Assert.Equal(-1, p!.IndexOf("teamwork"));
    }

    /// <summary>The pool binds the eleventh row too: 60 points, cap 10.</summary>
    [Fact]
    public void TheTeamPoolStillHoldsSixtyPoints()
        => Assert.False(BotSkillSpec.Parse("x", "skills=6,6,6,6,6,6,6,6,6,6,1", out _).Accepted);

    /// <summary>The leash shortens and the rest rise as Teamwork rises: 2, 6, 10.</summary>
    [Fact]
    public void TheTeamParametersMoveWithTeamwork()
    {
        SkillCurves curves = Curves.Game();
        DecisionParams[] p = [.. new[] { 2, 6, 10 }.Select(t => curves.For(WithTeamwork(t, Flat)))];
        Assert.True(p[0].LeashUnits > p[1].LeashUnits && p[1].LeashUnits > p[2].LeashUnits, "leash");
        Assert.True(p[0].CalloutChance < p[1].CalloutChance && p[1].CalloutChance < p[2].CalloutChance, "callout chance");
        Assert.True(p[0].FollowSeconds < p[2].FollowSeconds && p[0].TeamCoverWeight < p[2].TeamCoverWeight, "follow and cover");
        Assert.True(p[0].TradeWeight < p[2].TradeWeight && p[0].CrossfireWeight < p[2].CrossfireWeight, "trade and crossfire");
        Assert.Equal((1500f, 300f), (curves.For(WithTeamwork(0, Flat)).LeashUnits, curves.For(WithTeamwork(10, Flat)).LeashUnits));
    }

    /// <summary>Teamwork changes nothing a deathmatch bot reads: the ten's parameters are the same at Teamwork 0 and 10.</summary>
    [Fact]
    public void TeamworkMovesNoDeathmatchParameter()
    {
        SkillCurves curves = Curves.Game();
        DecisionParams lo = curves.For(WithTeamwork(0, Flat)), hi = curves.For(WithTeamwork(10, Flat));
        Assert.Equal((lo.AimErrorDeg, lo.ReactionMs, lo.ChaseSeconds, lo.CoverWeight, lo.RankBonus),
                     (hi.AimErrorDeg, hi.ReactionMs, hi.ChaseSeconds, hi.CoverWeight, hi.RankBonus));
    }

    [Fact]
    public void TeamworkIsNeverASpreadsFavourite()
    {
        SkillProfile mean = WithTeamwork(3, Flat);
        for (ulong seed = 1; seed <= 500; seed++)
        {
            (int first, int second) = SkillSpread.Favourites(mean, new AiRandom(seed));
            Assert.True(first != 10 && second != 10, $"seed {seed}");
        }
    }

    /// <summary>The control: over the ten, the favourites reach the last row.</summary>
    [Fact]
    public void OverTheTenTheLastRowCanBeAFavourite()
    {
        SkillProfile mean = new("ten", StandardSkills.Ten, [.. Flat]);
        Assert.Contains(Enumerable.Range(1, 200), s => SkillSpread.Favourites(mean, new AiRandom((ulong)s)).First == 9);
    }

    /// <summary>A bot whose Teamwork stands furthest out is named for it.</summary>
    [Fact]
    public void ABotGluedToItsTeamIsNamedForIt()
    {
        BotNamePools pools = BotNamePools.Parse(File.ReadAllText(SourceSharp.Tests.RepoLayout.Repo("game", "mod_test", "scripts", "ai", "hl2mp_bot_names.json")));
        string name = BotNamer.Name(pools, WithTeamwork(10, [2, 2, 2, 2, 2, 2, 2, 2, 2, 2]), 7, new HashSet<string>());
        Assert.Contains(pools.Traits["teamwork"].High, d => name.EndsWith(d, StringComparison.Ordinal));
    }
}
