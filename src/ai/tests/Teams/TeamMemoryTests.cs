using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// Team as a bot perceives it (plan_bots_teams_tf2 B.2, B.3): friend or foe is the
/// core's one rule over the team on the current row; the view, the <c>player_team</c>
/// event and the HUD's scores are where a bot learns it.
/// </summary>
public sealed class TeamMemoryTests
{
    internal static readonly ActorId Bot = new(3, 1);
    internal static readonly ActorId Mate = new(4, 2);
    internal static readonly ActorId Foe = new(2, 0x4000_0001);
    internal static readonly WeaponFacts Smg1 = new(3, "weapon_smg1", 45, 0.075f, 1.5f, true, false, 0f);

    internal static BotMemory Memory()
        => new(Bot, 8, new MemoryParams(4f, 120f),
               new MemoryWorld(MapKnowledge.Empty(new WeaponKnowledge([Smg1])), null, 0.015f, id => id.ToString()), new HabitStore());

    /// <summary>A view of the bot on <paramref name="team"/> seeing each of <paramref name="seen"/> at 500 u along x, on its team.</summary>
    internal static PerceivedView View(int tick, int team, params (ActorId Id, int Team, float X)[] seen)
    {
        var v = new PerceivedView(8);
        v.Reset(Bot, new Tick(tick), new SelfState(default, default, default, 100, 0, 3, 45, 90, SelfFlags.Alive, Team: team));
        foreach ((ActorId id, int t, float x) in seen)
            v.AddSighted(new ActorFacts(id, new AiVector(x, 0, 0), default, 3, ActorActivity.None, 1f, t));
        return v;
    }

    [Fact]
    public void TwoOnOneGameTeamAreFriends() => Assert.True(TeamRule.Friends(2, 2));

    [Fact]
    public void TwoOnDifferentTeamsAreNot() => Assert.False(TeamRule.Friends(2, 3));

    /// <summary>Unassigned (a deathmatch) and spectator make no friends, even with each other.</summary>
    [Fact]
    public void NoTeamAndSpectatorMakeNoFriends() => Assert.False(TeamRule.Friends(0, 0) || TeamRule.Friends(1, 1));

    [Fact]
    public void ASightedActorsTeamIsRememberedFromTheView()
    {
        BotMemory m = Memory();
        m.Step(new Tick(6), View(6, 2, (Mate, 2, 300f), (Foe, 3, 500f)), []);
        Assert.Equal(2, m.Team);
        Assert.True(m.IsFriend(m.Board.Of(Mate, make: false)!));
        Assert.False(m.IsFriend(m.Board.Of(Foe, make: false)!));
    }

    /// <summary>The <c>player_team</c> event re-classifies someone out of sight at once (B.9).</summary>
    [Fact]
    public void ATeamEventReclassifiesSomeoneUnseen()
    {
        BotMemory m = Memory();
        m.Step(new Tick(6), View(6, 2, (Foe, 3, 500f)), []);
        m.Step(new Tick(12), View(12, 2), [AiEvent.ActorTeam(Bot, new Tick(10), Foe, 2, 3)]);
        Assert.True(m.IsFriend(m.Board.Of(Foe, make: false)!));
    }

    [Fact]
    public void TheBotsOwnTeamEventSetsItsTeam()
    {
        BotMemory m = Memory();
        m.Apply(AiEvent.ActorTeam(Bot, new Tick(3), Bot, 3, 2));
        Assert.Equal(3, m.Team);
        Assert.False(m.Board.Of(Bot, make: false) is not null, "the bot is not one of the actors it remembers");
    }

    [Fact]
    public void TheHudsTeamScoresAreKept()
    {
        BotMemory m = Memory();
        m.Apply(AiEvent.MatchState(Bot, new Tick(3), 2, 7));
        m.Apply(AiEvent.MatchState(Bot, new Tick(3), 5, 4));
        Assert.Equal((7, 0, 0, 4), (m.Board.TeamScore(2), m.Board.TeamScore(3), m.Board.TeamScore(4), m.Board.TeamScore(5)));
    }

    /// <summary>With four teams, behind means some team is ahead (user 2026-11-08: 2/3/4 teams): second of four is behind; the leader is not.</summary>
    [Fact]
    public void BehindIsAnyTeamAheadOfFour()
    {
        BotMemory m = Memory();
        foreach ((int team, int score) in new[] { (2, 3), (3, 1), (4, 9), (5, 0) })
            m.Apply(AiEvent.MatchState(Bot, new Tick(3), team, score));
        Assert.True(m.Board.Behind(2));
        Assert.False(m.Board.Behind(4));
    }

    /// <summary>Format 6 writes each view's team and each sighted actor's; reading gives them back.</summary>
    [Fact]
    public void ARecordedSnapshotKeepsTheTeams()
    {
        var ring = new SnapshotRing(2, 8, 8);
        AiSnapshot s = ring.BeginWrite(new Tick(6));
        PerceivedView v = s.Add(Bot, new SelfState(default, default, default, 100, 0, 3, 45, 90, SelfFlags.Alive, Team: 3));
        v.AddSighted(new ActorFacts(Mate, new AiVector(1, 2, 3), default, 3, ActorActivity.None, 1f, 3));
        v.AddSighted(new ActorFacts(Foe, new AiVector(4, 5, 6), default, 3, ActorActivity.None, 1f, 2));
        var bytes = new MemoryStream();
        using (var w = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
            AiRecordFormat.Write(w, s);
        bytes.Position = 0;
        AiSnapshot read = AiRecordFormat.ReadSnapshot(new BinaryReader(bytes), new SnapshotRing(2, 8, 8));
        PerceivedView back = read.ViewFor(Bot.Slot)!;
        Assert.Equal(3, back.Self.Team);
        Assert.Equal([3, 2], back.Sighted.ToArray().Select(f => f.Team));
        Assert.Equal(bytes.Length, bytes.Position);
    }

    /// <summary>A format 5 snapshot, with no team tail, reads every team as 0: an old recording has no friends.</summary>
    [Fact]
    public void AnOlderSnapshotReadsNoTeams()
    {
        var ring = new SnapshotRing(2, 8, 8);
        AiSnapshot s = ring.BeginWrite(new Tick(6));
        PerceivedView v = s.Add(Bot, new SelfState(default, default, default, 100, 0, 3, 45, 90, SelfFlags.Alive, Team: 3));
        v.AddSighted(new ActorFacts(Mate, new AiVector(1, 2, 3), default, 3, ActorActivity.None, 1f, 3));
        var bytes = new MemoryStream();
        using (var w = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
            AiRecordFormat.Write(w, s);
        // The team tail is the self's byte and one per sighted actor: cut it.
        byte[] old = bytes.ToArray()[..^2];
        AiSnapshot read = AiRecordFormat.ReadSnapshot(new BinaryReader(new MemoryStream(old)), new SnapshotRing(2, 8, 8));
        PerceivedView back = read.ViewFor(Bot.Slot)!;
        Assert.Equal((0, 0), (back.Self.Team, back.Sighted[0].Team));
        Assert.Equal(45, back.Self.Clip);
    }
}
