using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Nav;
using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// The teams' own spawns as the bots know them before play (plan_bots_teams_tf2 B.5): carried
/// through a recording, and where a rally is called when the caller knows of no teammate's
/// place, the spawn-side spot out of the enemy's last-seen sight.
/// </summary>
public sealed class TeamSpawnTests
{
    private static readonly ActorId Mate = Duel.Other, Foe = Duel.Opponent;

    private static SkillProfile Teamwork(int level)
    {
        SkillPresets.TryGet("normal", out SkillProfile? p);
        return new SkillProfile("tw", StandardSkills.Eleven, [.. p!.Levels, level]);
    }

    private static SelfState Self(Duel d) => d.Self() with { Origin = new AiVector(1024, 1024, 0), Team = 2 };

    /// <summary>The cells of a 64 x 64 grid: an enemy sees a cell within 400 u of it along x, nothing farther.</summary>
    private sealed class NearSight(NavGraph g) : ICellSight
    {
        public float Range => 4096f;

        public bool Visible(int a, int b) => MathF.Abs(g.Cell(a).Centre.X - g.Cell(b).Centre.X) < 400f;

        public float Exposure(int cell) => 0f;
    }

    /// <summary>
    /// A Teamwork 8 bot on team 2, losing on the team scores with no team kill for 90 s, that has
    /// seen no teammate: the rally it calls. The enemy, if <paramref name="enemyAt"/>, was seen there.
    /// </summary>
    private static IntentCommand Rally(TeamSpawn[] spawns, AiVector? enemyAt)
    {
        var (g, _) = Grids.Parse([.. Enumerable.Repeat(new string('.', 64), 64)]);
        var d = new Duel(Teamwork(8), Duel.Bot, 7, g, new NearSight(g), null, spawns, []);
        var events = new List<AiEvent>
        {
            AiEvent.ActorTeam(Duel.Bot, new Tick(d.Tick + 6), Mate, 2, 0),
            AiEvent.MatchState(Duel.Bot, new Tick(d.Tick + 6), 2, 1),
            AiEvent.MatchState(Duel.Bot, new Tick(d.Tick + 6), 3, 5),
        };
        if (enemyAt is { } e)
        {
            events.Add(AiEvent.ActorTeam(Duel.Bot, new Tick(d.Tick + 6), Foe, 3, 0));
            events.AddRange(d.SeeAt(Foe, e));
        }

        d.StepTeams(Self(d), enemyAt is { } at ? [(Foe, at, 3)] : [], [.. events]);
        if (enemyAt is { } lost)
            d.StepTeams(Self(d), [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Foe, lost, default));
        d.Skip((int)(Hl2mpDecider.NoProgressTeamSeconds / 0.015f));
        return d.StepTeams(Self(d), []);
    }

    private static readonly TeamSpawn Near = new(2, new AiVector(1400, 1024, 0)), Far = new(2, new AiVector(200, 1024, 0)),
                                      Theirs = new(3, new AiVector(1900, 1024, 0));

    [Fact]
    public void KnowingNoTeammateARallyIsCalledAtItsTeamsNearestSpawn()
    {
        IntentCommand i = Rally([Far, Near, Theirs], null);
        Assert.Equal(CalloutKind.Rally, i.Say.Kind);
        Assert.True(i.Say.Place.DistanceTo(Near.Position) < 256f, $"at its own team's nearest spawn: {i.Say.Place}");
    }

    /// <summary>The enemy last seen beside the nearer spawn looks at it: the rally goes to the one it cannot see.</summary>
    [Fact]
    public void ASpawnTheEnemyLooksAtIsPassedOver()
    {
        IntentCommand i = Rally([Far, Near, Theirs], new AiVector(1500, 1500, 0));
        Assert.Equal(CalloutKind.Rally, i.Say.Kind);
        Assert.True(i.Say.Place.DistanceTo(Far.Position) < 256f, $"at the spawn out of the enemy's sight: {i.Say.Place}");
    }

    /// <summary>The control: a map with no spawns of the bot's team rallies where the bot stands, as before.</summary>
    [Fact]
    public void WithNoSpawnOfItsTeamTheRallyIsWhereTheBotStands()
    {
        IntentCommand i = Rally([Theirs], null);
        Assert.Equal(CalloutKind.Rally, i.Say.Kind);
        Assert.True(i.Say.Place.DistanceTo(new AiVector(1024, 1024, 0)) < 256f, $"where it stands: {i.Say.Place}");
    }

    /// <summary>A recording carries the team spawns in its world (format 7), so a replay knows them as the live bots did.</summary>
    [Fact]
    public void ARecordedWorldCarriesTheTeamSpawns()
    {
        var map = new MapKnowledge("m", [], [new AiVector(1, 2, 3)], Duel.Weapons, [Near, Theirs]);
        var bytes = new MemoryStream();
        new AiRecorder(bytes, new AiRecordingHeader(1, "m", 0, 0, 4, 4)).World(new AiRecordedWorld(map, 0.015f, "", 0));
        bytes.Position = 0;
        AiRecordFormat.ReadFileHeader(bytes);
        AiRecordedWorld? read = null;
        while (AiRecordFormat.TryReadRecord(bytes, out AiRecordType type, out byte[] payload))
        {
            if (type == AiRecordType.World)
                read = AiRecordFormat.ReadWorld(new BinaryReader(new MemoryStream(payload)));
        }

        Assert.Equal((TeamSpawn[])[Near, Theirs], read!.Map.TeamSpawns.ToArray());
    }
}
