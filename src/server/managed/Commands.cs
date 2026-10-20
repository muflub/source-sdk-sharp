using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Server console commands. Each class is discovered by attribute at init and
/// bound to a fresh instance — no id bookkeeping in the module.
/// </summary>
[ConCommand("cs_hello", Help = "Say hello from C# (server)")]
public sealed class HelloCommand(ILog log) : IConCommand
{
    public void Execute(string args)
        => log.Msg($"[SourceSharp/Server] hello from C#! args='{args}'\n");
}

[ConCommand("cs_status", Help = "Print SourceSharp server status")]
public sealed class StatusCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IServerTicks _ticks;

    /// <summary>The module's frame counter.</summary>
    public StatusCommand(ILog log, IServerTicks ticks)
    {
        _log = log;
        _ticks = ticks;
    }

    public void Execute(string args)
    {
        _log.Msg($"[SourceSharp/Server] {SharedInfo.Version}\n");
        _log.Msg($"[SourceSharp/Server] ticks since level init: {_ticks.Count}\n");
    }
}

/// <summary>
/// Toggles first-chance exception logging. Off by default because it reports
/// every throw, including handled ones — but invaluable when an exception is
/// being swallowed somewhere downstream.
/// </summary>
[ConCommand("cs_firstchance", Help = "Toggle first-chance exception logging (1/0)")]
public sealed class FirstChanceCommand(ILog log) : IConCommand
{
    private bool _enabled;

    // This command's first-chance handler, over its composition's log
    // (statics to zero: it was a static handler on ManagedDiagnostics).
    private IDisposable? _logging;

    public void Execute(string args)
    {
        _enabled = args.Trim() is "1" or "on" or "true" || (args.Trim().Length == 0 && !_enabled);
        _logging?.Dispose();
        _logging = ManagedDiagnostics.SetFirstChanceLogging(log, _enabled);
    }
}

/// <summary>
/// The inventory as C# sees it: every occupied slot as an untyped handle, plus
/// the view model's. The proof that a weapon is addressable without a
/// CBaseCombatWeapon in the question — drive it from the dev API and the
/// handles it prints are the ones every weapon ABI call takes.
/// </summary>
[ConCommand("cs_weapons", Help = "List a player's weapon slots as entity handles. cs_weapons [player]")]
public sealed class WeaponsCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IEnginePlayers _players;
    private readonly IEngineEntities _entities;

    public WeaponsCommand(ILog log, IEnginePlayers players, IEngineEntities entities)
    {

        _log = log;
        _players = players;
        _entities = entities;
    }

    public void Execute(string args)
    {
        int index = int.TryParse(args.Trim(), out int parsed) ? parsed : 1;

        var player = _players.GetHandle(index);
        if (!_entities.IsValid(player))
        {
            _log.Warning($"[SourceSharp/Weapons] no player {index}\n");
            return;
        }

        var slots = WeaponInventory.Read(_players, _entities, player);
        var viewModel = _players.GetViewModelEntity(player);

        _log.Msg($"[SourceSharp/Weapons] player {index}: {slots.Count} weapon(s)\n");

        foreach (string line in WeaponInventory.Report(slots, viewModel.Value))
            _log.Msg(line + "\n");
    }
}

/// <summary>Deliberately throws, to prove the crash handlers actually report.</summary>
/// <summary>
/// The Arsenal's own report (Phase 10): what a player OWNS, with clips and
/// pools, from the managed inventory that is the authority now. cs_weapons
/// above reads the native slot array, which is the CBasePlayer path's and
/// empty for a hierarchy player.
/// </summary>
[ConCommand("cs_arsenal", Help = "List a player's managed inventory. cs_arsenal [player]")]
public sealed class ArsenalCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly Arsenal _arsenal;
    private readonly IEnginePlayers _players;

    private readonly IEngineEntities _entities;

    public ArsenalCommand(ILog log, Arsenal arsenal, IEnginePlayers players, IEngineEntities entities)
    {

        _log = log;
        _arsenal = arsenal;
        _players = players;
        _entities = entities;
    }

    public void Execute(string args)
    {
        int index = int.TryParse(args.Trim(), out int parsed) ? parsed : 1;

        var player = _players.GetHandle(index);
        if (!_entities.IsValid(player))
        {
            _log.Warning($"[SourceSharp/Arsenal] no player {index}\n");
            return;
        }

        var holdings = _arsenal.Holdings(player);
        _log.Msg($"[SourceSharp/Arsenal] player {index}: {holdings.Count} weapon(s)\n");

        foreach (var (name, clip, active) in holdings)
            _log.Msg($"  {name}  clip {clip}{(active ? "  (active)" : "")}\n");

        foreach (var (ammo, count) in _arsenal.Pools(player))
            _log.Msg($"  pool {ammo}: {count}\n");
    }
}

/// <summary>
/// Arms a player from the console — the give path live verification stands
/// on, because only the loadout and the map's pickups can arm one otherwise
/// and the loadout is three weapons of nine.
/// </summary>
[ConCommand("cs_give", Help = "Give a weapon and full ammo for it. cs_give [player] <weapon classname>")]
public sealed class GiveWeaponCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly Arsenal _arsenal;
    private readonly IEnginePlayers _players;

    private readonly IEngineEntities _entities;

    // This side's tables (injected through the container): cs_give addresses
    // what the game HAS, and the mods' guns are part of what it has now.
    private readonly WeaponCatalog _weapons;
    private readonly AmmoCatalog _ammo;

    public GiveWeaponCommand(ILog log, Arsenal arsenal, IEnginePlayers players, IEngineEntities entities,
                             WeaponCatalog? weapons = null, AmmoCatalog? ammo = null)
    {

        _log = log;
        _arsenal = arsenal;
        _players = players;
        _entities = entities;
        _weapons = weapons ?? WeaponCatalog.Stock();
        _ammo = ammo ?? AmmoCatalog.Stock();
    }

    public void Execute(string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int index = parts.Length >= 2 && int.TryParse(parts[0], out int parsed) ? parsed : 1;
        string weapon = parts.Length >= 2 ? parts[1] : (parts.Length == 1 ? parts[0] : "");

        if (weapon.Length == 0 || _weapons.For(weapon) is not WeaponType type)
        {
            _log.Warning("[SourceSharp/Arsenal] cs_give [player] <weapon classname>\n");
            return;
        }

        var player = _players.GetHandle(index);
        if (!_entities.IsValid(player))
        {
            _log.Warning($"[SourceSharp/Arsenal] no player {index}\n");
            return;
        }

        _arsenal.GiveWeapon(player, type.Name);

        // And the ammunition to use it, filled to the carry limits — a dev
        // command that hands over a weapon you cannot fire is not worth having.
        if (type.AmmoName.Length > 0 && _ammo.For(type.AmmoName) is AmmoType ammo)
            _arsenal.GiveAmmo(player, ammo.MaxCarry, ammo.Name);

        if (type.SecondaryAmmoName.Length > 0 && _ammo.For(type.SecondaryAmmoName) is AmmoType pool)
            _arsenal.GiveAmmo(player, pool.MaxCarry, pool.Name);

        _arsenal.Switch(player, type.Name);

        _log.Msg($"[SourceSharp/Arsenal] gave {type.Name} to player {index}\n");
    }
}

[ConCommand("cs_throw", Help = "Throw a test exception (verifies crash reporting)")]
public sealed class ThrowCommand : IConCommand
{
    public void Execute(string args)
        => throw new InvalidOperationException("cs_throw: deliberate test exception");
}

/// <summary>
/// Round-trips a ConVar through the engine: sets a new value, then reports what
/// the cached accessor reads back. Proves SetValue and the change callback both
/// work, rather than assuming.
/// </summary>
[ConCommand("cs_convar_test", Help = "Round-trip a C# ConVar through the engine")]
public sealed class ConVarTestCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly GreetingConVar? _greeting;

    /// <summary>
    /// The greeting convar, from the container, or null when it is not
    /// registered — which the command reports rather than refusing to exist.
    /// </summary>
    public ConVarTestCommand(ILog log, GreetingConVar? greeting = null)
    {
        _log = log;
        _greeting = greeting;
    }

    public void Execute(string args)
    {
        var greeting = _greeting;
        if (greeting is null)
        {
            _log.Warning("[SourceSharp/Server] greeting convar not registered\n");
            return;
        }

        string before = greeting.Value;
        string target = args.Trim().Length > 0 ? args.Trim() : "roundtrip";

        greeting.SetValue(target);

        _log.Msg($"[SourceSharp/Server] convar round-trip: '{before}' -> set '{target}' -> reads '{greeting.Value}'\n");
        _log.Msg(greeting.Value == target
            ? "[SourceSharp/Server] convar round-trip OK\n"
            : "[SourceSharp/Server] convar round-trip FAILED\n");
    }
}

/// <summary>
/// Exercises the handle safety property directly.
///
/// plan.md Part 6, group C calls the slot-reuse case the single most important
/// test here: an entity index alone is unsafe because Source reuses slots, so a
/// stale index silently addresses whichever entity now occupies the slot. A
/// handle carries a serial number, so a stale one must fail closed.
///
/// This checks what can be checked from inside a running game: a live handle
/// resolves and reports coherent state, a deliberately corrupted serial does
/// not resolve, and EntityHandle.None never resolves.
/// </summary>
[ConCommand("cs_handle_test", Help = "Verify entity handle validation")]
public sealed class HandleTestCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IEnginePlayers _players;

    private readonly IEngineEntities _entities;

    public HandleTestCommand(ILog log, IEnginePlayers players, IEngineEntities entities)
    {

        _log = log;
        _players = players;
        _entities = entities;
    }

    public void Execute(string args)
    {
        var player = _players.GetHandle(1);

        if (!_entities.IsValid(player))
        {
            _log.Warning("[SourceSharp/Server] handle test: no player 1 to test with\n");
            return;
        }

        _log.Msg($"[SourceSharp/Server] handle test: live handle -> {player}, " +
                   $"health {_entities.GetHealth(player)}, origin {_entities.GetOrigin(player)}\n");

        // Same slot, wrong serial: must NOT resolve. This is the case a bare
        // entity index cannot distinguish.
        uint raw = player.Value;
        var wrongSerial = new EntityHandle(raw ^ 0x8000_0000u);
        _log.Msg(_entities.IsValid(wrongSerial)
            ? "[SourceSharp/Server] handle test FAILED: stale serial still resolved\n"
            : "[SourceSharp/Server] handle test OK: stale serial rejected\n");

        _log.Msg(_entities.IsValid(EntityHandle.None)
            ? "[SourceSharp/Server] handle test FAILED: None resolved\n"
            : "[SourceSharp/Server] handle test OK: None rejected\n");
    }
}


/// <summary>
/// Acquire every engine interface this side serves and call one method on each.
///
/// <para>
/// WHAT THIS IS AND, MORE IMPORTANTLY, WHAT IT IS NOT. It is not a differential
/// and it proves no index: the owner's ruling is that the engine's factory
/// interfaces are stable and the version string is the contract, so there is
/// nothing to re-derive against. It exists because after the verification
/// apparatus was deleted NOTHING in the shipped DLL called
/// <c>SourceSharp_AcquireInterface</c> at all — and an unexercised acquire is
/// an acquire nobody has seen work.
/// </para>
///
/// <para>
/// So this runs the one check in a real process: native's precompiled table
/// length against <c>sizeof(TVtbl) / sizeof(void*)</c>, then one call through
/// the struct to show the binding dispatches. <c>VEngineClient014</c> is
/// expected to be REFUSED here and that is the designed answer, not a failure —
/// <c>IVEngineClient</c> exists only on the client.
/// </para>
/// </summary>
[ConCommand("cs_vtable", Help = "Acquire the engine interfaces this side serves through "
                              + "their vtables and report what each answered")]
public sealed class VtableAcquireCommand(IServerEngineInterfaces engine, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        // Each line is the seam's: the acquire, and what the bound interface
        // answered or the engine's reason for refusing it, composed in the
        // interop where the acquire is (ServerEngineInterfaces).
        Print(engine.RandomStreamProbe());
        Print(engine.ModelInfoProbe());
        Print(engine.BaseFileSystemProbe());
        Print(engine.EngineServerProbe());

        // Expected to refuse on this side. Reported rather than skipped: a
        // refusal that is never printed is indistinguishable from a call nobody
        // made.
        Print(engine.EngineClientProbe());

        // THE SHIM LAYER, through the SAME acquire and the same dispatch --
        // plan_native_api.md Phase 2. The only difference from the five above
        // is which factory answers, which is the whole claim of the route.
        //
        // The sentence is composed inside SourceSharp.Abi (EngineFrame.Probe)
        // because the layout structs are internal there and a raw interface
        // pointer must not reach game code. What it says is second-sourced like
        // every line here: the tick through the shim's own vtable, and the
        // engine's CGlobalVars read through the independent data pointer beside
        // it.
        Print(engine.FrameProbe());
        Print(engine.LevelProbe());
    }

    private void Print(string line) => log.Msg($"[SourceSharp/Vtable] {line}\n");
}
