using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Mode;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// The horde's life with the level (plan_descent_logic HO2). At level ready,
/// when the Descent mode plays and the map is a level
/// (<see cref="DescentLevelName"/>: a generated <c>tf2_descent-&lt;depth&gt;-s&lt;seed&gt;</c>,
/// never the town), it asks the level source for the level, measures each pack
/// marker's clearance (HO9), asks the party present (D11), makes the one
/// controller, has it roll the roster through the spawn director and make the
/// pool, logs the census, and starts the level's navigation
/// (<see cref="Nav.HordeNav"/>, off the game thread).
/// Every server tick it runs the controller's tick (timed while the timer is
/// on). At level shutdown, while the entities still exist, the controller
/// drops what it owns and the parking table is cleared; the engine removes the
/// entities with the level.
/// </summary>
[SideService(Side.Server)]
public sealed class HordeService(IDescentModeState mode, IHordeLevelSource levels, IHordeSpawner spawner, IHordeClock clock,
                                 HordeParking parking, HordeCensus census, HordeTickTimer timer, ILog log,
                                 Director.IMarkerClearances clearances, Director.IHordeParty party, Nav.HordeNav nav)
    : IGameLifecycle, IFrameLifecycle
{
    public const int Order = ModStages.First + 500;

    public int LifecycleOrder => Order;

    public int FrameOrder => Order;

    /// <summary>This level's controller; null when the level has no horde.</summary>
    public HordeController? Current { get; private set; }

    /// <summary>Controllers made since the server started: one a level with a horde.</summary>
    public int ControllersMade { get; private set; }

    public void OnLevelReady(string mapName)
    {
        if (Current is not null)
            Drop();

        if (!mode.Active)
            return;

        if (!DescentLevelName.TryParse(mapName, out int depth, out ulong seed))
        {
            log.Msg($"[TF2Descent/Horde] {mapName} is not a level: no horde\n");
            return;
        }

        var level = levels.LevelFor(mapName, depth, seed);
        if (level is null)
        {
            log.Warning($"[TF2Descent/Horde] {mapName}: the level source has no level: no horde\n");
            return;
        }

        // The spawn director plans from each marker's clearance, which no level ships: measure it now (HO9).
        level = clearances.Measure(level);
        int partySize = party.PartySize();
        var (markers, inSolid) = clearances.Last;
        log.Msg($"[TF2Descent/Horde] {mapName}: measured {markers} pack markers ({inSolid} in solid); the roster is rolled for a party of {partySize}\n");

        int map = spawner.Edicts();
        var controller = spawner.Controller();
        if (controller is null)
        {
            log.Warning($"[TF2Descent/Horde] {mapName}: the engine refused the horde controller\n");
            return;
        }

        ControllersMade++;
        Current = controller;
        if (!controller.Begin(level, clock.TicksPerSecond, partySize, clock.Tick))
        {
            Drop();
            return;
        }

        census.LevelReady(controller, map);
        nav.Begin(mapName, level);
    }

    public void OnThink(float frameTime)
    {
        if (Current is not { Begun: true } controller)
            return;

        timer.Enter();
        nav.Tick();
        controller.Tick(clock.Tick);
        timer.Leave();
        census.Watch(controller);
    }

    public void OnLevelShutdownPreEntity() => Drop();

    private void Drop()
    {
        Current?.End();
        nav.End();
        Current = null;
        parking.Clear();
        census.Forget();
    }
}

/// <summary>
/// The horde's edicts against the engine's (plan_descent_logic section 2): the
/// pool's bodies and weapons, lent and parked, and the engine's count beside
/// the map's. One line at level ready, and a loud warning, once a level, when
/// the count passes the map's plus the run's 512.
/// </summary>
[SideService(Side.Server)]
public sealed class HordeCensus(IHordeSpawner spawner, HordeParking parking, ILog log)
{
    /// <summary>The edicts a level's run may add to its map's (the mine plan's budget).</summary>
    public const int RunBudget = 512;

    private int _checkEvery;

    /// <summary>The engine's edicts at level ready, before the horde made anything: the map's count.</summary>
    public int MapEdicts { get; private set; }

    /// <summary>The most the engine held since level ready.</summary>
    public int MaxEdicts { get; private set; }

    public bool OverBudget { get; private set; }

    /// <summary>The pool's own count, from the controller.</summary>
    public static (int Bodies, int Weapons, int Lent, int Parked) Pool(HordeController c) =>
        c.Bodies is { } b ? (c.BodiesMade, c.WeaponsMade, b.Lent, b.LastBody - b.Lent) : (0, 0, 0, 0);

    public void LevelReady(HordeController controller, int mapEdicts)
    {
        MapEdicts = mapEdicts;
        OverBudget = false;
        int now = spawner.Edicts();
        MaxEdicts = now;
        var roll = controller.Roll!;
        log.Msg($"[TF2Descent/Horde] level ready: {roll.Robots} robots in {roll.Packs} packs, {controller.BodiesMade} bodies, {controller.WeaponsMade} weapons, {now} entities (map {mapEdicts})\n");
        if (!roll.Dropped.IsEmpty)
            log.Warning($"[TF2Descent/Horde] {roll.Summary}\n");
        Check(now);
    }

    /// <summary>Reads the count once a second or so and remembers the most.</summary>
    public void Watch(HordeController controller)
    {
        if (++_checkEvery < 64)
            return;
        _checkEvery = 0;
        Check(spawner.Edicts());
    }

    public void Forget()
    {
        MapEdicts = 0;
        MaxEdicts = 0;
        OverBudget = false;
    }

    /// <summary>The census as one line.</summary>
    public string Line(HordeController controller)
    {
        int now = spawner.Edicts();
        Check(now);
        var (bodies, weapons, lent, parked) = Pool(controller);
        return $"census: entities {now} (map {MapEdicts}, most {MaxEdicts}, budget {MapEdicts + RunBudget}), bodies {bodies} (lent {lent}, parked {parked}), weapons {weapons}, parked entities {parking.Count}";
    }

    private void Check(int now)
    {
        if (now > MaxEdicts)
            MaxEdicts = now;
        if (OverBudget || MapEdicts == 0 || now <= MapEdicts + RunBudget)
            return;
        OverBudget = true;
        log.Warning($"[TF2Descent/Horde] OVER THE EDICT BUDGET: {now} entities, the map's {MapEdicts} plus {RunBudget} is {MapEdicts + RunBudget}\n");
    }
}
