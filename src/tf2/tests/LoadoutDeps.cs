using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;
using TF2.Shared.Econ;

namespace TF2.Tests;

/// <summary>
/// The loadout provider's dependencies, real and answering an empty game: no
/// round machine, no stalemate, no bots, nothing granted. The provider takes
/// every dependency required, so a fact that means to drive one hands its own
/// holder; the rest come from here, the same for every suite.
/// </summary>
internal static class LoadoutDeps
{
    /// <summary>The provider over <paramref name="inventory"/>, every dependency a real holder.</summary>
    public static TFLoadoutProvider Loadout(LocalInventory inventory,
                                            ItemSchema? schema = null,
                                            ITFPlayerClasses? classes = null,
                                            TFPlayerItems? items = null,
                                            TFClassTable? table = null,
                                            ITeamRoundState? round = null,
                                            ITFStalemateSettings? stalemate = null,
                                            WeaponCatalog? catalog = null,
                                            TFPlayerCarriers? carriers = null,
                                            BotInventory? botInventory = null,
                                            IBotPlayers? bots = null,
                                            AttributeHooks? hooks = null,
                                            IEnginePlayers? players = null,
                                            IEnginePrecache? precache = null,
                                            ILog? log = null,
                                            ITFPlayerAttributeSource[]? attributeSources = null,
                                            params ITFItemSource[] sources)
    {
        log ??= new RecordingLog();
        schema ??= EconTestFiles.TrimmedSchema();
        var side = new TFPlayerCarriers();
        var engine = new RecordingEngine();
        // The active-weapon read the grant walk makes: no weapon, no name.
        // The kit's model precache: an index nobody asserts on here.
        engine.Returns("Players.GetHandle", EntityHandle.None);
        engine.Returns("Players.GetActiveWeapon", string.Empty);
        engine.Returns("Precache.PrecacheModel", 0);
        var shipped = Weapons.WeaponFixtures.WithExtras(Weapons.WeaponFixtures.ShippedWithClasses());
        var theTable = table ?? new TFClassTable(log, shipped);
        var theCatalog = catalog ?? new WeaponCatalog(
            () => [new TFWeaponTypes(new TFWeaponScripts(log, shipped), theTable, schema)], log);
        // TF2's own source over the inventories, then the fact's own after
        // it — the order a mod's source takes in the chain.
        var own = new TFInventoryItemSource(inventory,
                                            botInventory ?? new BotInventory(schema, engine, NoBots(), log),
                                            bots ?? NoBots(), schema, players ?? engine);
        return new TFLoadoutProvider(
            new Rules.ListHooks<ITFItemSource>([own, .. sources]),
            new Rules.ListHooks<ITFPlayerAttributeSource>(attributeSources ?? []),
            schema,
            classes ?? side,
            items ?? new TFPlayerItems(),
            theTable,
            round ?? new IdleRound(),
            stalemate ?? new NoStalemate(),
            theCatalog,
            carriers ?? side,
            hooks ?? new AttributeHooks(),
            players ?? engine,
            precache ?? engine,
            log);
    }

    /// <summary>The bots answer: a fresh record with none in it.</summary>
    public static IBotPlayers NoBots() => new NoBotPlayers();

    /// <summary>No round machine: the gated arms see none running.</summary>
    public sealed class IdleRound : ITeamRoundState
    {
        public RoundState State => RoundState.Init;

        public bool InWaitingForPlayers => false;

        public bool InSetup => false;

        public float MapResetTime => 0f;

        public float StalemateStartTime => -1f;

        public TFTeam WinningTeam => TFTeam.Unassigned;
    }

    /// <summary><c>mp_stalemate_meleeonly 0</c>: the stalemate arm never gates.</summary>
    public sealed class NoStalemate : ITFStalemateSettings
    {
        public bool MeleeOnly => false;

        public float ChangeClassTime => 0f;
    }

    /// <summary>A server with no bots: <see cref="IBotPlayers.IsBot"/> false for everyone.</summary>
    private sealed class NoBotPlayers : IBotPlayers
    {
        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal, int team = 0)
        {
            refusal = "no bots";
            return EntityHandle.None;
        }

        public bool IsBot(EntityHandle player) => false;

        public bool Kick(EntityHandle bot, string reason) => false;

        public IReadOnlyList<BotPlayer> Bots => [];
    }

    private static IBotPlayers BotPlayers() => NoBots();
}
