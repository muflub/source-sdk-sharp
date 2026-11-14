using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Tests.Player;

/// <summary>
/// The player-component source as the server's composition builds it: the
/// scan over the server assembly — every <c>[SideService]</c> arm, the
/// declared convars published as the game's registry does — standing behind
/// the rig's own answers. A rig holds the real components the game composes,
/// so an arm whose dependency is missing fails here, at the compose, naming
/// the parameter; not at the first tick under a null-guarded half.
/// </summary>
internal static class ComposedComponents
{
    /// <summary>The components source over <paramref name="engine"/>'s scripted answers and the rig's own instances.</summary>
    public static TFPlayerComponents Source(IEngineEntities entities, IEntityDirectory directory,
        TFPlayerCarriers carriers, ILog log, TFPlayerItems items, TFPlayerConditions conditions,
        ITFMapPlayers mapPlayers, IGameClock clock, IEngineNetworking networking, ItemSchema schema,
        IWeaponGiver arsenal, IEnginePlayers players, IEngineGameEvents events, IEngineWorldQuery world,
        IEngineDamage damage, TFWeaponScripts scripts, TFDamageScopes scopes, RecordingEngine engine)
    {
        var services = new ServiceContainer();

        // The engine double under every interface it answers: the arms built
        // below read the same scripted engine the rig's facts do.
        foreach (var iface in engine.GetType().GetInterfaces())
        {
            if (!iface.IsGenericType)
                services.AddInstance(iface, engine);
        }

        var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
        convars.RegisterAll(typeof(TFPlayerComponents).Assembly);
        convars.PublishTo(services);

        SideServices.Register(services, Side.Server, typeof(TFPlayerComponents).Assembly.GetTypes());

        // The rig's own instances, over the scan's lazies: the seams these
        // facts script against.
        services.AddInstance(entities);
        services.AddInstance(directory);
        services.AddInstance(carriers);
        services.AddInstance(log);
        services.AddInstance(items);
        services.AddInstance(conditions);
        services.AddInstance(mapPlayers);
        services.AddInstance(clock);
        services.AddInstance(networking);
        services.AddInstance(schema);
        services.AddInstance(arsenal);
        services.AddInstance(players);
        services.AddInstance(events);
        services.AddInstance(world);
        services.AddInstance(damage);
        services.AddInstance(scripts);
        services.AddInstance(scopes);

        // The composition's leaves of its own: the mod word and the connected
        // clients nobody is, as the composition test's server holds them.
        services.AddInstance<IModInfo>(new ModInfo(_ => null));
        services.AddInstance<IConnectedClients>(new NoClients());
        services.AddInstance<ITeams>(new IdleTeams());
        services.AddInstance<IRandomSeeds>(new RandomSeeds(20260929));
        // The round flow the scan builds asks the level by constructor
        // (required since d5fb481c1's TeamRoundFlow), as the round
        // composition test hands it.
        services.AddInstance<ILevelInfo>(new TF2.Tests.Rules.FakeLevel());
        // TFSpy takes the death notice and the corpses by constructor; both
        // are ServerModule's own instances beside the scan, held here over
        // the same engine fake, as the weapon composition test holds them.
        services.AddInstance(new SourceSharp.Server.DeathNotice(engine, engine, engine,
                                                                new SourceSharp.Server.DamageInflictors()));
        services.AddInstance(new SourceSharp.Server.Corpses(log, engine, engine,
                                                            new TF2.Tests.Combat.NoEntities()));
        services.AddInstance(new WeaponCatalog(
            () => [new TFWeaponTypes(scripts, new TFClassTable(log, TF2.Tests.Weapons.WeaponFixtures.ShippedWithClasses()), schema)], log));
        services.AddInstance<IScreenEffects>(new IdleScreen());
        // The core's lag compensation, which the server module registers before
        // this scan: the medigun weapon's required rewind, idle here.
        services.AddInstance<ILagCompensation>(new IdleLag());
        services.AddInstance<IResponseRules>(new SourceSharp.Server.ResponseRules(log, new IdleFiles(), new FixedRandom()));
        services.AddInstance<SourceSharp.Server.IBotPlayers>(TF2.Tests.LoadoutDeps.NoBots());
        services.AddInstance<IUniformRandom>(new FixedRandom());
        services.AddInstance<IServerSkins>(new RecordingServerSkins());
        services.AddInstance(new SourceSharp.Server.DamageInflictors());
        services.AddInstance<ITempEnts>(new SourceSharp.Server.TempEnts(log, new TempEntRegistry(log), engine));
        services.AddInstance(new SourceSharp.Server.DamageHitGroups());
        services.AddInstance(new Arsenal(log, engine, engine, engine, engine, engine, engine, engine, engine,
            directory, clock, new SourceSharp.Server.DamageHitGroups(),
            weapons: new WeaponCatalog(() => [new TFWeaponTypes(scripts, new TFClassTable(log, TF2.Tests.Weapons.WeaponFixtures.ShippedWithClasses()), schema)], log),
            ammo: new AmmoCatalog(() => [new TF2.Shared.TFAmmoTypes()], log)));
        services.AddInstance<IPlayerDirectory>(new NoRecords());
        // The placement hooks the eureka teleport's required arm asks; no mod
        // places anything here, the empty chain, as the server module's
        // factory answers when no mod registers a placement.
        services.AddInstance<IHooks<ISpawnPlacement>>(new TF2.Tests.Rules.ListHooks<ISpawnPlacement>());
        // The core's new-match chain the round machine raises at a game restart
        // (plan_bots_teams_tf2 A.6); nobody listens here.
        services.AddInstance<IHooks<SourceSharp.Server.IMatchResetHook>>(new TF2.Tests.Rules.ListHooks<SourceSharp.Server.IMatchResetHook>());
        services.AddInstance<IMapCleanup>(new NoCleanup());
        services.AddInstance(new EntityDirectory(log));
        services.AddInstance(new GameModes(log, clock, services,
            DeclaredConVars.Fresh<SvModeSwitchWarmupConVar>(),
            DeclaredConVars.Fresh<MpWaitingForPlayersTimeConVar>()));
        services.AddInstance(new Intermission(log,
            DeclaredConVars.Fresh<MpChatTimeConVar>(), DeclaredConVars.Fresh<NextLevelConVar>(),
            DeclaredConVars.Fresh<MapCycleFileConVar>(), directory, entities, engine, engine,
            new IdleConsole(), new IdleFiles()));
        services.AddInstance(new Observers(log, directory, entities, players, engine, engine, damage, world, engine,
                                           DeclaredConVars.Fresh<ForceCameraConVar>(),
                                           DeclaredConVars.Fresh<AllowSpectatorsConVar>()));
        // The round roster's names: ServerModule's own instance beside the
        // scan (the same Create the module runs, after the clients it reads).
        services.AddInstance(services.Create<PlayerNames>());

        return services.Resolve<TFPlayerComponents>() ?? throw new ServiceResolutionException(
            "the composition resolved no TFPlayerComponents");
    }

    private sealed class NoClients : IConnectedClients
    {
        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => null;
        public int UserId(int entIndex) => entIndex;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    /// <summary>Everyone on red, nobody tracked: the roster and the taunt
    /// arms read a team word that never moves, as the rig's engine says.</summary>
    private sealed class IdleTeams : ITeams
    {
        public int Team(EntityHandle player) => (int)TFTeam.Red;
        public IReadOnlyList<EntityHandle> Players(int team) => [];
        public int Count(int team) => 0;
        public JoinTeamResult Join(EntityHandle player, int requested) => default;
        public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;
    }

    /// <summary>A directory that knows nobody: the record it invents on the
    /// indexer is fresh each read, the waits zero.</summary>
    private sealed class NoRecords : IPlayerDirectory
    {
        public PlayerRecord this[EntityHandle player] => new();
        public bool Knows(EntityHandle player) => false;
        public IEnumerable<(EntityHandle Player, PlayerRecord Record)> All => [];
        public float WaitRemaining(EntityHandle player, float now) => 0f;
        public float RespawnDelay { get; set; }
        public ObserverMode DeathView { get; set; }
        public bool DropWeaponOnDeath { get; set; }
        public float DropSpeed { get; set; }
        public void OnSpawned(EntityHandle player, float now) { }
        public void OnKilled(EntityHandle player, EntityHandle attacker, float now) { }
        public void Forget(EntityHandle player) { }
    }

    /// <summary>A cleanup that remakes nothing: the report of one, empty.</summary>
    private sealed class NoCleanup : IMapCleanup
    {
        public MapCleanupReport CleanUpMap(IMapCleanupFilter filter) => default;
    }

    /// <summary>A console that runs nothing and quits nowhere.</summary>
    private sealed class IdleConsole : IServerConsole
    {
        public string Execute(string command) => string.Empty;
        public void Quit(int exitCode) { }
    }

    /// <summary>No file is on any path: the size −1, the reads null.</summary>
    private sealed class IdleFiles : IEngineFiles
    {
        public int GameFileSize(string path) => -1;
        public string? ReadGameFile(string path) => null;
        public byte[]? ReadGameFileBytes(string path) => null;
        public byte[]? ReadFileOnPath(string path, string pathId) => null;
    }

    /// <summary>The stream's foot: always the range's lower bound, so a pick
    /// under it is the first lawful one every run.</summary>
    private sealed class FixedRandom : IUniformRandom
    {
        public float RandomFloat(float min, float max) => min;
        public int RandomInt(int min, int max) => min;
    }

    /// <summary>A screen that neither shakes nor fades.</summary>
    private sealed class IdleScreen : IScreenEffects
    {
        public void Shake(Vector origin, float amplitude, float frequency, float duration, float radius,
                          ShakeCommand command = ShakeCommand.Start, bool airShake = false) { }
        public void Fade(EntityHandle player, int r, int g, int b, int a, float duration, float holdTime, FadeFlags flags) { }
    }

    /// <summary>A lag session that never records: nobody moved, nothing to put back.</summary>
    public sealed class IdleLag : ILagCompensation
    {
        public void Start(EntityHandle player) { }

        public void Start(EntityHandle player, in UserCmd cmd) { }

        public void Finish(EntityHandle player) { }

        public bool IsCurrentlyDoingLagCompensation => false;

        public void NoteWeaponFired(EntityHandle player) { }
    }
}
