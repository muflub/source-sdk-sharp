using SourceSharp.Abi;
using SourceSharp.Ai.Server.Experience;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// Lane G6a: a person's jump onto a place the level has no way to becomes a <c>link</c> row of a human's (its count and its
/// exemplar), through the feed (<see cref="ShownJumpFeed"/>), the service and the worker; a bot's never does but for the
/// stand-in; the store off, learning off or a round not being played reads nothing and writes nothing; a jump one of the
/// level's links makes is thrown away, and a stand-in's is judged against the links only when asked.
/// </summary>
public sealed partial class ExperienceServiceTests
{
    private const int Jumper = 4;
    private readonly Dictionary<EntityHandle, ShownJumpBody> _bodies = new();
    private int _bodyReads;

    /// <summary>The bodies as a fact sets them, with the reads counted; no blast.</summary>
    private sealed class Bodies(ExperienceServiceTests owner) : IExperienceJumps
    {
        public bool Read(EntityHandle player, bool askGround, out ShownJumpBody body)
        {
            owner._bodyReads++;
            return owner._bodies.TryGetValue(player, out body);
        }

        public JumpBlast BlastOf(EntityHandle player) => JumpBlast.None;
    }

    /// <summary>The stand-in's name: a bot called by its slot.</summary>
    private sealed class SlotNames : IShownJumpStandIns
    {
        public bool Named(int slot, string name) => name == $"bot{slot}";
    }

    /// <summary>Two islands of ground 64 u apart: nothing walks from one to the other.</summary>
    private static NavGraph Islands() => SourceSharp.Ai.Tests.Nav.Grids.Parse("....  ....").Graph;

    private static readonly AiVector Here = new(48f, 16f, 0f), There = new(240f, 16f, 0f);

    private (ExperienceService Service, ShownJumpFeed Feed) JumpLevel(string db = Db, string learn = "1", string standIn = "", string standInLinks = "0", bool graph = true, bool withJumps = true)
    {
        _nav = graph ? new GraphNavigation(Islands()) : new NoNavigation();
        ExperienceService service = Playing(db, learn);
        Frame(service);
        var feed = new ShownJumpFeed(service, Bots(), new SlotNames(),
                                     DeclaredConVars.FreshAt<AiExperienceJumpStandInConVar>(standIn),
                                     DeclaredConVars.FreshAt<AiExperienceJumpStandInLinksConVar>(standInLinks), withJumps ? new Bodies(this) : null);
        return (service, feed);
    }

    private IBotPlayers Bots() => FakeEngine.Of<IBotPlayers>((m, a) => m.Name == "IsBot" ? _bots.Contains((EntityHandle)a[0]!) : throw FakeEngine.Unasked(m));

    /// <summary>A player's handle as the engine packs it: its entry under a serial (<see cref="EntityHandle.EntryIndex"/>).</summary>
    private static EntityHandle Player(int slot) => new((uint)slot | (7u << 16));

    private void Command(ShownJumpFeed feed, EntityHandle who, ShownJumpBody body, int buttons = 0)
    {
        _bodies[who] = body;
        var cmd = new UserCmd { Buttons = buttons, ForwardMove = 400f, Pitch = 10f, Yaw = 0f };
        feed.OnCommand(who, in cmd);
    }

    /// <summary>A run, a jump, 40 commands in the air (0.6 s) from here to there, and the landing.</summary>
    private void Jump(ShownJumpFeed feed, EntityHandle who, AiVector from, AiVector to)
    {
        for (int i = 0; i < 40; i++)
            Command(feed, who, new ShownJumpBody(from, default, true, false, false, true), i == 39 ? ShownJumpFeed.InJump : 0);
        for (int i = 1; i <= 40; i++)
            Command(feed, who, new ShownJumpBody(from + (to - from) * (i / 41f), (to - from) * (1f / 0.6f), false, false, false, true));
        Command(feed, who, new ShownJumpBody(to, default, true, false, false, true));
    }

    [Fact]
    public void APersonsJumpToAPlaceWithNoWayIsACountRowAndAnExemplar()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel();
        Jump(feed, Player(Jumper), Here, There);
        Assert.True(service.Worker.Settle());
        service.OnLevelShutdownPreEntity();
        LinkKey[] keys = [.. Merged().Links.Keys.OrderBy(k => k.Recipe.Bytes.Length)];
        Assert.Equal(2, keys.Length);
        Assert.All(keys, k => Assert.Equal((LinkSource.Human, 3, WorldBin.Of(Here), WorldBin.Of(There), (int)ShownJumpKind.Plain), (k.Source, k.Class, k.From, k.To, k.Kind)));
        Assert.Equal(new LinkStat(1d, 1d, 0.6d, 0d), Merged().Links[keys[0]] with { Seconds = Math.Round(Merged().Links[keys[0]].Seconds, 6) });
        Assert.True(ShownJumpRecipe.TryRead(keys[1].Recipe, out _));
    }

    [Fact]
    public void ABotsJumpIsNoRow()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel();
        _bots.Add(Player(Jumper));
        Jump(feed, Player(Jumper), Here, There);
        service.OnLevelShutdownPreEntity();
        Assert.Empty(Merged().Links);
        Assert.Equal(0, _bodyReads);
    }

    [Fact]
    public void TheStandInsJumpIsAPersons()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel(standIn: $"bot{Jumper}");
        _bots.Add(Player(Jumper));
        Jump(feed, Player(Jumper), Here, There);
        service.OnLevelShutdownPreEntity();
        Assert.Equal(2, Merged().Links.Count);
    }

    [Fact]
    public void AnotherBotThanTheStandInIsNotWatched()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel(standIn: "bot9");
        _bots.Add(Player(Jumper));
        Jump(feed, Player(Jumper), Here, There);
        service.OnLevelShutdownPreEntity();
        Assert.Empty(Merged().Links);
    }

    [Fact]
    public void WithTheStoreOffNothingIsRead()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel(db: "");
        Jump(feed, Player(Jumper), Here, There);
        Assert.Equal(0, _bodyReads);
        Assert.False(service.Worker.Running);
    }

    [Fact]
    public void WithLearningOffNothingIsReadOrWritten()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel(learn: "0");
        Jump(feed, Player(Jumper), Here, There);
        service.OnLevelShutdownPreEntity();
        Assert.Equal(0, _bodyReads);
        Assert.Empty(Merged().Links);
    }

    [Fact]
    public void OutsideARoundBeingPlayedNothingIsRead()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel();
        _game.RoundOver = true;
        Frame(service);
        Jump(feed, Player(Jumper), Here, There);
        service.OnLevelShutdownPreEntity();
        Assert.Equal(0, _bodyReads);
        Assert.Empty(Merged().Links);
    }

    [Fact]
    public void AJumpAlongAWalkIsNoRow()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel();
        Jump(feed, Player(Jumper), Here, Here + new AiVector(64f, 0f, 0f));
        Assert.True(service.Worker.Settle());
        Assert.Equal(1L, service.Worker.ShownJumps(ShownJumpVerdict.Walkable));
        service.OnLevelShutdownPreEntity();
        Assert.Empty(Merged().Links);
    }

    /// <summary>The level's one link, from here to there, for the soldier (class 3).</summary>
    private void HereToThereIsALink()
    {
        var (graph, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse("....  ....");
        _nav = new GraphNavigation(graph);
        NavLinks set = NavLinks.Build(graph, [new NavLink(ids[1, 0], ids[7, 0], NavLinkKind.Blast, 1f, 30f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        _links.Keys = new ExperienceLinkKeys(set, [new LinkKey(3, WorldBin.Of(Here), WorldBin.Of(There), 0, LinkSource.Solver, new LinkRecipe([1, 2]))]);
    }

    [Fact]
    public void APersonsJumpOneOfTheLevelsLinksMakesIsNoRow()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel();
        HereToThereIsALink();
        Jump(feed, Player(Jumper), Here, There);
        Assert.True(service.Worker.Settle());
        Assert.Equal(1L, service.Worker.ShownJumps(ShownJumpVerdict.Known));
        service.OnLevelShutdownPreEntity();
        Assert.Empty(Merged().Links);
    }

    [Fact]
    public void TheStandInsJumpIsJudgedWithoutTheLevelsLinksUnlessAsked()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel(standIn: "*");
        HereToThereIsALink();
        _bots.Add(Player(Jumper));
        Jump(feed, Player(Jumper), Here, There);
        Assert.True(service.Worker.Settle());
        Assert.Equal(1L, service.Worker.ShownJumps(ShownJumpVerdict.NoWay));
    }

    [Fact]
    public void TheStandInsJumpIsJudgedWithTheLevelsLinksWhenAsked()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel(standIn: "*", standInLinks: "1");
        HereToThereIsALink();
        _bots.Add(Player(Jumper));
        Jump(feed, Player(Jumper), Here, There);
        Assert.True(service.Worker.Settle());
        Assert.Equal(1L, service.Worker.ShownJumps(ShownJumpVerdict.Known));
    }

    [Fact]
    public void ASecondSightingAddsToTheCountRowAndWritesNoSecondExemplar()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel();
        Jump(feed, Player(Jumper), Here, There);
        Jump(feed, Player(Jumper), Here + new AiVector(4f, 0f, 0f), There);
        service.OnLevelShutdownPreEntity();
        Assert.Equal(2, Merged().Links.Count);
        Assert.Equal(2d, Merged().Links[new LinkKey(3, WorldBin.Of(Here), WorldBin.Of(There), (int)ShownJumpKind.Plain, LinkSource.Human, ShownJumpRecipe.CountRecipe)].Tries);
    }

    [Fact]
    public void AnotherPlayersHitInTheAirMakesTheJumpNoRow()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel();
        EntityHandle other = Player(Jumper + 1);
        Command(feed, other, new ShownJumpBody(There, default, true, false, false, true));
        for (int i = 0; i < 40; i++)
            Command(feed, Player(Jumper), new ShownJumpBody(Here, default, true, false, false, true), i == 39 ? ShownJumpFeed.InJump : 0);
        for (int i = 1; i <= 40; i++)
        {
            Command(feed, Player(Jumper), new ShownJumpBody(Here + (There - Here) * (i / 41f), (There - Here) * (1f / 0.6f), false, false, false, true));
            if (i == 20)
                feed.OnPlayerHurt(new PlayerHurt(Player(Jumper), other, 10, 90));
        }

        Command(feed, Player(Jumper), new ShownJumpBody(There, default, true, false, false, true));
        service.OnLevelShutdownPreEntity();
        Assert.Empty(Merged().Links);
        Assert.Equal(1L, feed.Watch.Thrown(JumpThrow.Pushed));
    }

    [Fact]
    public void AGameWithNoJumpReaderWatchesNothing()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel(withJumps: false);
        Jump(feed, Player(Jumper), Here, There);
        Assert.Equal(0L, feed.WatchedCommands);
        Assert.Equal(0, service.ShownJumpsNoted);
    }

    [Fact]
    public void ALandingWithNoGraphIsNotJudged()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel(graph: false);
        Jump(feed, Player(Jumper), Here, There);
        Assert.Equal(1, service.ShownJumpsUnjudged);
        Assert.Equal(0, service.ShownJumpsNoted);
    }
    /// <summary>The exemplar row of a jump from here to there, as a store holds it.</summary>
    private static bool IsExemplar(LinkKey k) => ShownJumpLedger.IsExemplar(k);

    [Fact]
    public void AJumpWhoseExemplarTheStoreHoldsAddsOnlyToItsCountRow()
    {
        var seeded = new ExperienceDelta();
        var old = new ShownJumpRecipe(ShownJumpKind.Plain, ShownJumpFlags.JumpPressed, Here, There, default, default, default, default, 40, 0, 0, -1, -1, -1, []);
        seeded.Add(old.ExemplarKey(3), default);
        _stores.Kept.Merge(Key, seeded, 20d);
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel();
        Jump(feed, Player(Jumper), Here, There);
        service.OnLevelShutdownPreEntity();
        Assert.True(ShownJumpLedger.IsCount(Assert.Single(Merged().Links).Key));
    }

    [Fact]
    public void AnotherModesKeyWritesTheJumpsExemplarAgain()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel();
        Jump(feed, Player(Jumper), Here, There);
        _game.Mode = "koth";
        Frame(service);
        Jump(feed, Player(Jumper), Here, There);
        service.OnLevelShutdownPreEntity();
        Assert.Equal(2, _stores.Merged.SelectMany(m => m.Delta.Links.Keys).Count(IsExemplar));
    }

    [Fact]
    public void AShotTheGameToldIsTheExemplarsShot()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel();
        for (int i = 0; i < 40; i++)
        {
            Command(feed, Player(Jumper), new ShownJumpBody(Here, default, true, false, false, true), i == 39 ? ShownJumpFeed.InJump : 0);
            if (i == 37)
                feed.OnWeaponEvent(Player(Jumper), 3, PlayerWeaponEvent.Fired, 1);
        }

        for (int i = 1; i <= 40; i++)
            Command(feed, Player(Jumper), new ShownJumpBody(Here + (There - Here) * (i / 41f), (There - Here) * (1f / 0.6f), false, false, false, true));
        Command(feed, Player(Jumper), new ShownJumpBody(There, default, true, false, false, true));
        service.OnLevelShutdownPreEntity();
        LinkKey exemplar = Merged().Links.Keys.Single(IsExemplar);
        Assert.True(ShownJumpRecipe.TryRead(exemplar.Recipe, out ShownJumpRecipe? jump));
        Assert.Equal(-2, jump!.ShotDelay);
    }
    [Fact]
    public void APlayerOnNoSideIsNoRow()
    {
        (ExperienceService service, ShownJumpFeed feed) = JumpLevel();
        _game.Players[Player(Jumper)] = (0, 3);
        Jump(feed, Player(Jumper), Here, There);
        service.OnLevelShutdownPreEntity();
        Assert.Empty(Merged().Links);
        Assert.Equal(0, service.ShownJumpsNoted);
    }
}
