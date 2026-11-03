using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The spawn choice and its placement (<see cref="SpawnPoints"/>, what
/// <see cref="ServerModule.SelectSpawnPoint"/> hands each spawn to).
///
/// <para>
/// WHAT EACH FACT PINS: a spot an <see cref="ISpawnSelector"/> names (through
/// <see cref="ITeams.SelectSpawn"/>) is placed, once, at its own origin lifted
/// one unit and with its own angles, exactly as the core's walk places its
/// spot; the core's walk is not asked when a selector named one; and no spot
/// at all places nobody. The walk's fact is the positive control: the same
/// placement call over the double, reached by the other branch. The selector
/// fact failed before the fix, when a selector's spot was answered to native
/// (which ignores it) and never placed.
/// </para>
/// </summary>
public class SpawnPointsTests
{
    private static readonly EntityHandle Player = new(0x010001);
    private static readonly EntityHandle TeamSpot = new(0x020042);
    private static readonly EntityHandle WalkSpot = new(0x020043);

    private static readonly Vector TeamSpotAt = new(-1200f, 640f, -96f);
    private static readonly Vector TeamSpotFacing = new(0f, 270f, 0f);
    private static readonly Vector WalkSpotAt = new(300f, -40f, 8f);
    private static readonly Vector WalkSpotFacing = new(0f, 45f, 0f);

    /// <summary>The team service, as far as the spawn ask reads it: a fixed answer.</summary>
    private sealed class SelectingTeams(EntityHandle spot) : ITeams
    {
        public int Asks { get; private set; }
        public int Team(EntityHandle player) => 2;
        public IReadOnlyList<EntityHandle> Players(int team) => [];
        public int Count(int team) => 0;
        public JoinTeamResult Join(EntityHandle player, int requested) => throw new NotSupportedException();

        public EntityHandle SelectSpawn(EntityHandle player)
        {
            Asks++;
            return spot;
        }
    }

    /// <summary>The chain double: the hooks as registered, already in order.</summary>
    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    /// <summary>
    /// A placement hook with an answer: the position it was given and the
    /// player and spot it was asked for are recorded, so the fold and the
    /// throw-away can tell what the chain actually passed it.
    /// </summary>
    private sealed class Placer(Vector moved, bool throws = false) : ISpawnPlacement, IOrderedHook
    {
        public int HookOrder { get; init; }
        public Vector? Given { get; private set; }
        public EntityHandle AskedPlayer { get; private set; }
        public EntityHandle AskedSpot { get; private set; }

        public Vector PlaceAt(EntityHandle player, EntityHandle spot, Vector placement)
        {
            Given = placement;
            AskedPlayer = player;
            AskedSpot = spot;
            if (throws)
                throw new InvalidOperationException("the mod is broken");
            return moved;
        }
    }

    /// <summary>
    /// A double answering each spot's own origin and angles by handle, one
    /// info_player_deathmatch on the map (the walk's), and nobody near it.
    /// </summary>
    private static (SpawnPoints Points, RecordingEngine Engine, RecordingLog Log) Rig(
        IHooks<ISpawnPlacement>? placement = null)
    {
        var engine = new RecordingEngine();
        Vector By(string member, Vector team, Vector walk)
            => (EntityHandle)engine.Last(member)!.Value.Args[0]! == TeamSpot ? team : walk;

        engine.ReturnsEachCall("Entities.GetOrigin", () => By("Entities.GetOrigin", TeamSpotAt, WalkSpotAt))
              .ReturnsEachCall("Entities.GetAngles", () => By("Entities.GetAngles", TeamSpotFacing, WalkSpotFacing))
              .ReturnsEachCall("Entities.FindAllByClassname",
                               () => (string)engine.Last("Entities.FindAllByClassname")!.Value.Args[0]! == EntityNames.InfoPlayerDeathmatch
                                   ? new[] { WalkSpot }
                                   : Array.Empty<EntityHandle>())
              .Returns("WorldQuery.EntitiesInSphere", 0)
              .Ignores("Players.PlaceAtSpawn");

        var log = new RecordingLog();
        return (new SpawnPoints(engine, engine, engine, engine, Convars(), new Random(1), placement, log: log), engine, log);
    }

    /// <summary>A convar backend that keeps what it is told.</summary>
    private sealed class Backend : IConVarBackend
    {
        private readonly Dictionary<int, string> _held = new();
        private int _next;

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            _held[_next] = defaultValue;
            return _next++;
        }

        public void Set(int id, string value) => _held[id] = value;
        public string Get(int id) => _held.TryGetValue(id, out string? v) ? v : "";
        public string? ValueOf(string name) => null;
    }

    /// <summary>The registry with the frag's radius declared, at its default unless told.</summary>
    private static ConVarRegistry Convars(float? radius = null)
    {
        var convars = new ConVarRegistry(new Backend());
        convars.RegisterTypes([typeof(Hl2mpSpawnFragFallbackRadiusConVar)]);
        if (radius is { } r)
            convars.Get<Hl2mpSpawnFragFallbackRadiusConVar>()!.SetValue(r);
        return convars;
    }

    private static readonly EntityHandle Near = new(0x010002);
    private static readonly EntityHandle Far = new(0x010003);
    private static readonly EntityHandle NotAPlayer = new(0x030000);

    /// <summary>A rule of a mod's.</summary>
    private sealed class FragRule(bool frags) : ISpawnFragRule
    {
        public bool Frags(EntityHandle spawning) => frags;
    }

    /// <summary>
    /// Every spot of <paramref name="spawnClass"/> taken: <see cref="Far"/> stands 100 u
    /// from each (the 128 u check sees them), <see cref="Near"/> 30 u from each, and the
    /// sphere asked of the world answers by its radius, the spawning player included.
    /// </summary>
    private static (SpawnPoints Points, RecordingEngine Engine) EveryPointTaken(
        string spawnClass, float? radius = null, IHooks<ISpawnFragRule>? rules = null, bool taken = true)
    {
        var engine = new RecordingEngine();
        engine.ReturnsEachCall("Entities.GetOrigin", () => WalkSpotAt)
              .ReturnsEachCall("Entities.GetAngles", () => WalkSpotFacing)
              .ReturnsEachCall("Entities.FindAllByClassname",
                               () => (string)engine.Last("Entities.FindAllByClassname")!.Value.Args[0]! == spawnClass
                                   ? new[] { WalkSpot, TeamSpot }
                                   : Array.Empty<EntityHandle>())
              .ReturnsEachCall("WorldQuery.EntitiesInSphere.into", () =>
              {
                  float asked = (float)engine.Last("WorldQuery.EntitiesInSphere")!.Value.Args[1]!;
                  if (!taken)
                      return new[] { NotAPlayer, Player };
                  return asked >= 100f ? new[] { NotAPlayer, Player, Near, Far }
                       : asked >= 30f ? new[] { NotAPlayer, Player, Near }
                       : new[] { NotAPlayer, Player };
              })
              .ReturnsEachCall("Entities.IsPlayer", () => (EntityHandle)engine.Last("Entities.IsPlayer")!.Value.Args[0]! != NotAPlayer)
              .Ignores("Players.PlaceAtSpawn")
              .Ignores("Damage.TakeDamage");
        return (new SpawnPoints(engine, engine, engine, engine, Convars(radius), new Random(1), fragRules: rules), engine);
    }

    /// <summary>
    /// CHL2MP_Player::EntSelectSpawnPoint's fallback (hl2mp_player.cpp:1447-1457): every
    /// spot taken, a player within 48 u of the settled one takes 300 generic from the
    /// world, and the spawning player, a thing that is no player and one 100 u away
    /// (which the 128 u check counted) do not.
    /// </summary>
    [Fact]
    public void EveryPointTakenKillsThePlayerWithinTheRadiusOfTheSettledSpot()
    {
        var (points, engine) = EveryPointTaken(EntityNames.InfoPlayerDeathmatch);

        EntityHandle settled = points.SelectAndPlace(Player, teams: null);

        Assert.True(settled == WalkSpot || settled == TeamSpot);
        var hit = Assert.Single(engine.AllOf("Damage.TakeDamage"));
        Assert.Equal(Near, hit.Args[0]);
        Assert.Equal(new EntityHandle(0u), hit.Args[1]);
        Assert.Equal(300f, hit.Args[2]);
        Assert.Equal(DamageType.Generic, hit.Args[3]);
        Assert.Single(engine.AllOf("Players.PlaceAtSpawn"));
    }

    [Fact]
    public void TheKillsRadiusIsTheConvars()
    {
        var (narrow, narrowEngine) = EveryPointTaken(EntityNames.InfoPlayerDeathmatch, radius: 10f);
        narrow.SelectAndPlace(Player, teams: null);
        Assert.False(narrowEngine.Called("Damage.TakeDamage"), narrowEngine.Describe());

        var (wide, wideEngine) = EveryPointTaken(EntityNames.InfoPlayerDeathmatch, radius: 120f);
        wide.SelectAndPlace(Player, teams: null);
        Assert.Equal([Near, Far], wideEngine.AllOf("Damage.TakeDamage").Select(c => c.ArgAs<EntityHandle>(0)));
    }

    /// <summary>
    /// A respawn is a jump, and clients are told, after the move: without it each
    /// drew the body flying from where it died to its spawn, and past it.
    /// </summary>
    [Fact]
    public void A_placement_breaks_the_players_interpolation_after_moving_it()
    {
        var (points, engine) = EveryPointTaken(EntityNames.InfoPlayerDeathmatch, taken: false);

        points.SelectAndPlace(Player, teams: null);

        var calls = engine.Calls.Select(c => c.Member).ToList();
        int placed = calls.IndexOf("Players.PlaceAtSpawn");
        Assert.True(placed >= 0, engine.Describe());
        Assert.True(calls.IndexOf("Players.BreakInterpolation") > placed, engine.Describe());
    }

    [Fact]
    public void AFreePointKillsNobody()
    {
        var (points, engine) = EveryPointTaken(EntityNames.InfoPlayerDeathmatch, taken: false);

        points.SelectAndPlace(Player, teams: null);

        Assert.True(engine.Called("Players.PlaceAtSpawn"));
        Assert.False(engine.Called("Damage.TakeDamage"), engine.Describe());
    }

    /// <summary>Stock places on an info_player_start without asking who is on it (hl2mp_player.cpp:1460-1466).</summary>
    [Fact]
    public void AnInfoPlayerStartIsNotFoughtOver()
    {
        var (points, engine) = EveryPointTaken(EntityNames.InfoPlayerStart);

        Assert.NotEqual(EntityHandle.None, points.SelectAndPlace(Player, teams: null));

        Assert.False(engine.Called("Damage.TakeDamage"), engine.Describe());
    }

    /// <summary>A mod's rule refuses the kill, and the positive arm is that a rule allowing it changes nothing.</summary>
    [Fact]
    public void AModsRuleCanRefuseTheKill()
    {
        var (refused, refusedEngine) = EveryPointTaken(EntityNames.InfoPlayerDeathmatch,
                                                       rules: new ListHooks<ISpawnFragRule>(new FragRule(true), new FragRule(false)));
        refused.SelectAndPlace(Player, teams: null);
        Assert.True(refusedEngine.Called("Players.PlaceAtSpawn"));
        Assert.False(refusedEngine.Called("Damage.TakeDamage"), refusedEngine.Describe());

        var (allowed, allowedEngine) = EveryPointTaken(EntityNames.InfoPlayerDeathmatch,
                                                       rules: new ListHooks<ISpawnFragRule>(new FragRule(true)));
        allowed.SelectAndPlace(Player, teams: null);
        Assert.True(allowedEngine.Called("Damage.TakeDamage"));
    }

    [Fact]
    public void ASelectorsSpotPlacesThePlayerOnItLiftedOneUnitWithItsAngles()
    {
        var (points, engine, _) = Rig();

        EntityHandle placed = points.SelectAndPlace(Player, new SelectingTeams(TeamSpot));

        Assert.Equal(TeamSpot, placed);
        var call = Assert.Single(engine.AllOf("Players.PlaceAtSpawn"));
        Assert.Equal(Player, call.Args[0]);
        Assert.Equal(TeamSpotAt with { Z = TeamSpotAt.Z + 1f }, call.Args[1]);
        Assert.Equal(TeamSpotFacing, call.Args[2]);
    }

    [Fact]
    public void ASelectorsSpotIsNotSecondGuessedByTheCoresWalk()
    {
        var (points, engine, _) = Rig();
        var teams = new SelectingTeams(TeamSpot);

        points.SelectAndPlace(Player, teams);

        Assert.Equal(1, teams.Asks);
        Assert.False(engine.Called("Entities.FindAllByClassname"), engine.Describe());
    }

    [Fact]
    public void TheCoresWalkPlacesItsSpotTheSameWay()
    {
        var (points, engine, _) = Rig();

        EntityHandle placed = points.SelectAndPlace(Player, new SelectingTeams(EntityHandle.None));

        Assert.Equal(WalkSpot, placed);
        var call = Assert.Single(engine.AllOf("Players.PlaceAtSpawn"));
        Assert.Equal(Player, call.Args[0]);
        Assert.Equal(WalkSpotAt with { Z = WalkSpotAt.Z + 1f }, call.Args[1]);
        Assert.Equal(WalkSpotFacing, call.Args[2]);
    }

    /// <summary>
    /// pPlayer->SetAbsVelocity( vec3_origin ) (gamerules.cpp:195): what a player moved
    /// at when it died does not come back with it.
    /// </summary>
    [Fact]
    public void APlacementStopsThePlayer()
    {
        var (points, engine, _) = Rig();
        engine.Ignores("Entities.SetVelocity");

        points.SelectAndPlace(Player, new SelectingTeams(EntityHandle.None));

        var call = Assert.Single(engine.AllOf("Entities.SetVelocity"));
        Assert.Equal(Player, call.Args[0]);
        Assert.Equal(Vector.Zero, call.Args[1]);
    }

    [Fact]
    public void NoSpotAnywherePlacesNobody()
    {
        var (points, engine, _) = Rig();
        engine.Returns("Entities.FindAllByClassname", Array.Empty<EntityHandle>());

        EntityHandle placed = points.SelectAndPlace(Player, teams: null);

        Assert.Equal(EntityHandle.None, placed);
        Assert.True(engine.Called("Entities.FindAllByClassname"));
        Assert.False(engine.Called("Players.PlaceAtSpawn"), engine.Describe());
    }

    [Fact]
    public void APlacementHookMovesThePlacementAndTheOneUnitLiftStillFollows()
    {
        // Stock's numbers, the deferred row's example: the ctf_2fort red spawn
        // at z 276 is dropped ~20 units to the floor (z 256.75) by
        // CTFGameRules::GetPlayerSpawnSpot's DropToGround, and the core's
        // one-unit lift still applies on top of the hook's answer.
        var hook = new Placer(TeamSpotAt with { Z = TeamSpotAt.Z - 19.25f });
        var (points, engine, _) = Rig(new ListHooks<ISpawnPlacement>(hook));

        points.SelectAndPlace(Player, new SelectingTeams(TeamSpot));

        var call = Assert.Single(engine.AllOf("Players.PlaceAtSpawn"));
        Assert.Equal(TeamSpotAt with { Z = TeamSpotAt.Z - 18.25f }, call.Args[1]);
        Assert.Equal(TeamSpotFacing, call.Args[2]);
        Assert.Equal(Player, hook.AskedPlayer);
        Assert.Equal(TeamSpot, hook.AskedSpot);
    }

    [Fact]
    public void TheChainFoldsInHookOrderEachHookGettingThePreviousAnswer()
    {
        var first = new Placer(TeamSpotAt with { X = TeamSpotAt.X + 10f }) { HookOrder = 10 };
        var second = new Placer(TeamSpotAt with { X = TeamSpotAt.X + 10f, Y = TeamSpotAt.Y + 20f }) { HookOrder = 20 };
        var (points, engine, _) = Rig(new ListHooks<ISpawnPlacement>(first, second));

        points.SelectAndPlace(Player, new SelectingTeams(TeamSpot));

        Assert.Equal(TeamSpotAt, first.Given);
        Assert.Equal(TeamSpotAt with { X = TeamSpotAt.X + 10f }, second.Given);
        var call = Assert.Single(engine.AllOf("Players.PlaceAtSpawn"));
        Assert.Equal(TeamSpotAt with { X = TeamSpotAt.X + 10f, Y = TeamSpotAt.Y + 20f,
                                       Z = TeamSpotAt.Z + 1f }, call.Args[1]);
    }

    /// <summary>
    /// A spot with a player on it among many other things is taken: looking at
    /// only the first 16 near it, the spawn check missed them, and two players
    /// stood in each other at it, a person as well as a bot (live, dm_lockdown's
    /// spawn among lights and decals). The control: the same spot empty is taken up.
    /// </summary>
    [Fact]
    public void ASpotWithAPlayerAmongManyThingsIsTaken()
    {
        EntityHandle standing = new(0x010002);
        EntityHandle[] clutter = [.. Enumerable.Range(0, 40).Select(i => new EntityHandle(0x030000u + (uint)i)), standing];
        EntityHandle Chosen(int seed, EntityHandle[] atWalkSpot)
        {
            var engine = new RecordingEngine();
            engine.ReturnsEachCall("Entities.GetOrigin", () => (EntityHandle)engine.Last("Entities.GetOrigin")!.Value.Args[0]! == TeamSpot ? TeamSpotAt : WalkSpotAt)
                  .ReturnsEachCall("Entities.FindAllByClassname", () => (string)engine.Last("Entities.FindAllByClassname")!.Value.Args[0]! == EntityNames.InfoPlayerDeathmatch ? new[] { WalkSpot, TeamSpot } : Array.Empty<EntityHandle>())
                  .ReturnsEachCall("WorldQuery.EntitiesInSphere.into", () => ((Vector)engine.Last("WorldQuery.EntitiesInSphere")!.Value.Args[0]!).X == WalkSpotAt.X ? atWalkSpot : Array.Empty<EntityHandle>())
                  .ReturnsEachCall("Entities.IsPlayer", () => (EntityHandle)engine.Last("Entities.IsPlayer")!.Value.Args[0]! == standing);
            return new SpawnPoints(engine, engine, engine, engine, Convars(), new Random(seed)).Choose(Player, null);
        }

        foreach (int seed in Enumerable.Range(1, 8))
            Assert.Equal(TeamSpot, Chosen(seed, clutter));
        Assert.Contains(Enumerable.Range(1, 8), seed => Chosen(seed, []) == WalkSpot);
    }

    [Fact]
    public void AThrowingHookIsNamedAndThePlacementPassesThroughUnmoved()
    {
        var (points, engine, log) = Rig(new ListHooks<ISpawnPlacement>(new Placer(TeamSpotAt, throws: true)));

        points.SelectAndPlace(Player, new SelectingTeams(TeamSpot));

        var call = Assert.Single(engine.AllOf("Players.PlaceAtSpawn"));
        Assert.Equal(TeamSpotAt with { Z = TeamSpotAt.Z + 1f }, call.Args[1]);
        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains("spawn placement hook threw"));
    }
}
