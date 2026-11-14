using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Items;
using TF2.Descent.Server.Mode;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// <c>tf2_descent_corpse</c>: a hero's corpse (plan_descent_logic WO5, death
/// plan M4), one a hero, made where he died and kept for the level's life,
/// across his respawn. It is drawn as a TF2 gravestone (no new art; the
/// hero's ragdoll fades as stock's does), not solid, takes no damage, and is
/// sent to every client (death plan Q3: everyone sees bodies) wherever it
/// stands, so the owner's client knows where his body lies for the automap.
/// Section 2's budget: one edict a hero, under "hero bodies".
/// </summary>
[ScriptedEntity(CorpseNetState.ClassName, ScriptedEntityKind.Item)]
[Precache(PrecacheKind.Model, Model)]
public sealed class CorpseEntity(IEngineEntityRender render) : ScriptedEntity
{
    /// <summary>What a corpse looks like: a TF2 prop, from the mounted TF2 content.</summary>
    public const string Model = "models/props_manor/gravestone_05.mdl";

    [NetState] public CorpseNetState State = new();

    public override bool SpawnsPhysics => false;

    public override bool TakesDamage => false;

    public override void OnSpawn()
    {
        SetModel(Model);
        SetSolidity(SolidType.None, SolidFlags.None);
        render.SetTransmitAlways(Handle, true);
    }
}

/// <summary>The engine work a corpse needs, behind one interface so the corpse rules have facts over a fake world.</summary>
public interface ICorpseWorld
{
    /// <summary>A new corpse entity at <paramref name="at"/>; null when none could be made.</summary>
    (EntityHandle Entity, CorpseNetState State)? Make(Vector at);

    void Move(EntityHandle corpse, Vector at);

    void Remove(EntityHandle corpse);
}

/// <summary>The engine's corpses: <see cref="CorpseEntity"/> made, moved and removed.</summary>
[SideService(Side.Server, As = [typeof(ICorpseWorld)])]
public sealed class EngineCorpseWorld(IEngineEntities entities, IEntityDirectory directory) : ICorpseWorld
{
    public (EntityHandle Entity, CorpseNetState State)? Make(Vector at)
    {
        var handle = entities.Create(CorpseNetState.ClassName);
        if (handle == EntityHandle.None)
            return null;
        entities.SetOrigin(handle, at);
        entities.Spawn(handle);
        return directory.Get(handle) is CorpseEntity corpse ? (handle, corpse.State) : null;
    }

    public void Move(EntityHandle corpse, Vector at) => entities.SetOrigin(corpse, at);

    public void Remove(EntityHandle corpse)
    {
        if (corpse != EntityHandle.None && entities.IsValid(corpse))
            entities.Remove(corpse);
    }
}

/// <summary>A hero's corpse on this level: whose (the ledger's name and the owner's slot), its entity and state, where, and whether hardcore.</summary>
public sealed class HeroCorpse(string hero, int ownerIndex, EntityHandle entity, CorpseNetState state, Vector at, bool hardcore)
{
    public string Hero { get; } = hero;
    public int OwnerIndex { get; set; } = ownerIndex;
    public EntityHandle Entity { get; } = entity;
    public CorpseNetState State { get; } = state;
    public Vector At { get; set; } = at;
    public bool Hardcore { get; set; } = hardcore;
}

/// <summary>
/// LOOT YOUR OWN BODY (plan_descent_logic WO5; death plan M4; plan_tf2_rpg
/// Phase 10): every hero's corpse on the level, and the use that takes it back.
///
/// <list type="bullet">
/// <item>A death (<see cref="HeroDeathCosts"/>) leaves what the rules drop on
/// the hero's corpse, through the ledger, and stands its entity where he
/// fell. One corpse a hero: a second death merges onto the first (the ledger
/// keeps one corpse a hero; the rules are silent), and the corpse moves to
/// where he fell last.</item>
/// <item>A press of +use within <see cref="LootRange"/> units of his own
/// corpse, standing and up, takes all that fits through the ledger
/// (<see cref="IItemLedger.LootCorpse"/>); what does not fit stays, and an
/// emptied corpse is removed. Another hero's corpse is refused (D6, Q3), as
/// is a hardcore hero's, by its owner too (D7).</item>
/// <item>The corpses are the level's: a new level starts with none.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroCorpses(IDescentModeState mode, Heroes heroes, HeroKeys keys, IItemLedger ledger, ICorpseWorld world,
                                IDownedWorld players, ClientMessages messages, ILog log) : IGameLifecycle
{
    /// <summary>The plan's reach for the use (death plan M4).</summary>
    public const float LootRange = 96f;

    /// <summary>In TF2 Descent's downed block.</summary>
    public const int Order = ModStages.First + 953;

    public int LifecycleOrder => Order;

    private readonly Dictionary<string, HeroCorpse> _byHero = new(StringComparer.Ordinal);
    private readonly HashSet<int> _useHeld = [];

    /// <summary>Every corpse on the level.</summary>
    public IReadOnlyCollection<HeroCorpse> All => _byHero.Values;

    /// <summary>The corpse of the hero the ledger names <paramref name="hero"/>, or null.</summary>
    public HeroCorpse? Of(string hero) => _byHero.GetValueOrDefault(hero);

    /// <summary>
    /// The hero in <paramref name="ownerIndex"/> died at <paramref name="at"/> and
    /// the ledger has his corpse: its entity stands there (made, or moved from
    /// where he fell before), counting what the ledger holds on it. Nothing
    /// stands for a corpse with nothing on it.
    /// </summary>
    public HeroCorpse? Place(int ownerIndex, string hero, Vector at, bool hardcore)
    {
        var held = ledger.Corpse(hero);
        bool unknown = ledger.Kind == LedgerKind.Hosted;
        if (!unknown && (held is null || held.Empty))
        {
            log.Msg($"[TF2Descent/Corpse] player {ownerIndex} left nothing to a corpse\n");
            return null;
        }

        if (_byHero.TryGetValue(hero, out var corpse))
        {
            world.Move(corpse.Entity, at);
            corpse.At = at;
            corpse.OwnerIndex = ownerIndex;
            corpse.Hardcore |= hardcore;
        }
        else
        {
            if (world.Make(at) is not { } made)
            {
                log.Warning($"[TF2Descent/Corpse] no {CorpseNetState.ClassName} could be made for player {ownerIndex}\n");
                return null;
            }
            corpse = new HeroCorpse(hero, ownerIndex, made.Entity, made.State, at, hardcore);
            _byHero[hero] = corpse;
        }

        Count(corpse);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Corpse] player {ownerIndex}'s corpse stands at ({at.X:0} {at.Y:0} {at.Z:0}): {Describe(held)}{(corpse.Hardcore ? ", hardcore: lootable by nobody" : "")}\n"));
        return corpse;
    }

    /// <summary>Each frame: a hero who presses +use near a corpse loots his own, or is told whose it is.</summary>
    public void Think()
    {
        if (!mode.Active)
            return;
        foreach (var hero in heroes.All)
        {
            int index = hero.PlayerIndex;
            bool held = players.IsAlive(hero.Player) && players.HoldingUse(hero.Player);
            bool pressed = held && _useHeld.Add(index);
            if (!held)
                _useHeld.Remove(index);
            if (pressed && !hero.Downed)
                _ = Use(index);
        }
    }

    /// <summary>
    /// A use by the hero in <paramref name="playerIndex"/>: his own corpse in
    /// reach is looted; another's in reach is refused by name. Answers what
    /// happened, for the dev command and the facts.
    /// </summary>
    public async Task<string> Use(int playerIndex)
    {
        if (heroes.Of(playerIndex) is not { } hero || keys.Of(playerIndex) is not { } key)
            return "no hero";
        Vector here = players.Origin(hero.Player);
        var near = _byHero.Values.Where(c => (c.At - here).Length <= LootRange).OrderBy(c => (c.At - here).Length).ToList();
        if (near.FirstOrDefault(c => c.Hero == key) is not { } mine)
        {
            if (near.FirstOrDefault() is { } other)
            {
                messages.Notice(playerIndex, $"That is player {other.OwnerIndex}'s body: only its owner can loot it");
                return $"refused: player {other.OwnerIndex}'s body";
            }
            return "no corpse in reach";
        }
        if (mine.Hardcore)
        {
            messages.Notice(playerIndex, "A hardcore hero's body cannot be looted");
            return "refused: hardcore";
        }

        var answer = await ledger.LootCorpse(key);
        if (!answer.Ok)
        {
            log.Warning($"[TF2Descent/Corpse] player {playerIndex}'s loot was refused: {answer.Reason}\n");
            messages.Notice(playerIndex, "Your body could not be looted: " + answer.Reason);
            return "refused: " + answer.Reason;
        }

        var left = ledger.Corpse(key);
        messages.Notice(playerIndex, $"Recovered {answer.Recovered} item(s)");
        // Hosted, the ledger cannot list what stayed: a loot the host took ends the corpse.
        if (left is null)
        {
            world.Remove(mine.Entity);
            _byHero.Remove(key);
            log.Msg($"[TF2Descent/Corpse] player {playerIndex} recovered {answer.Recovered} item(s): the corpse is empty and gone\n");
            return $"recovered {answer.Recovered}, corpse gone";
        }
        Count(mine);
        log.Msg($"[TF2Descent/Corpse] player {playerIndex} recovered {answer.Recovered} item(s); {Describe(left)} stay on the corpse\n");
        return $"recovered {answer.Recovered}, {left.Items.Count} stay";
    }

    /// <summary>Every corpse, one line each, for <c>tf2_descent_corpses</c>.</summary>
    public IEnumerable<string> Lines() =>
        _byHero.Values.Select(c => string.Create(CultureInfo.InvariantCulture,
            $"{c.Hero} (player {c.OwnerIndex}) at ({c.At.X:0} {c.At.Y:0} {c.At.Z:0}) {c.Entity}: {Describe(ledger.Corpse(c.Hero))}{(c.Hardcore ? ", hardcore" : "")}"));

    private void Count(HeroCorpse corpse)
    {
        corpse.State.OwnerIndex = (byte)Math.Clamp(corpse.OwnerIndex, 0, 127);
        corpse.State.Hardcore = corpse.Hardcore;
        corpse.State.Items = ledger.Corpse(corpse.Hero) is { } held ? (short)Math.Min(held.Items.Count, short.MaxValue) : (short)-1;
    }

    private static string Describe(LedgerCorpse? held) =>
        held is null ? "contents unknown (the ledger lists no corpse)"
        : $"{held.Items.Count} item(s) [{string.Join(' ', held.Items)}] and {held.Australium} Australium";

    /// <summary>A new level: the last level's corpses went with it (the ledger keeps what was on them).</summary>
    public void OnLevelInit(string mapName)
    {
        _byHero.Clear();
        _useHeld.Clear();
    }
}
