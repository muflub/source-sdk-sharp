//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// What the impulse cheats need of the <see cref="Arsenal"/>: giving, which is
/// everything impulse 101 asks for. An interface rather than the class itself
/// so the dispatch can be exercised against a recorder — the Arsenal is sealed
/// and reaches the engine on every give.
/// </summary>
public interface IWeaponGiver
{
    /// <inheritdoc cref="Arsenal.GiveWeapon"/>
    bool GiveWeapon(EntityHandle player, string className);

    /// <inheritdoc cref="Arsenal.GiveAmmo"/>
    int GiveAmmo(EntityHandle player, int count, string ammoName);
}

/// <summary>
/// The impulse cheats — stock's <c>CBasePlayer::CheatImpulseCommands</c>, which
/// left the build with player.cpp at the -ss_player flip and lives here now.
///
/// <para>
/// The impulse rides the user command (<see cref="UserCmd.Impulse"/>), exactly
/// one command per console <c>impulse N</c>, and is dispatched from
/// <see cref="ServerModule.OnStandalonePlayerCommand"/> at phase 1 — after the
/// movement, which is where stock ran ImpulseCommands from ItemPostFrame.
/// </para>
///
/// <para>
/// Every impulse here is a CHEAT and stays behind sv_cheats, as stock's were.
/// The gate is on THIS class only: ordinary commands (weapon switching, the
/// managed client-command crossing) run in front of it and are not gated —
/// that boundary was put wrong once already, see StandalonePlayer_ClientCommand
/// in shell_standalone_player.cpp.
/// </para>
///
/// <para>
/// Deliberately not stock's whole switch. What this mod HAS is what 101 gives:
/// the nine weapon rows of <see cref="WeaponTypes.All"/> and every ammo pool of
/// <see cref="AmmoTypes.All"/>, not HL2's list — and the cases serving classes
/// that left the build (NPC creation and debugging, vehicles, gibs, node
/// viewers, decals) are dropped rather than ported to do nothing.
/// </para>
/// </summary>
public sealed class ImpulseCheats
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    /// <summary>Stock's give-everything impulse.</summary>
    public const int GiveAll = 101;

    /// <summary>Stock's what-am-I-looking-at impulse: classname and targetname.</summary>
    public const int Identify = 106;

    /// <summary>Stock's remove-what-I-am-looking-at impulse.</summary>
    public const int RemoveAimed = 203;

    /// <summary>
    /// How far the crosshair impulses look, in units. Stock's FindEntityForward
    /// traces MAX_COORD_RANGE; anything past this is scenery either way.
    /// </summary>
    public const float AimRange = 8192f;

    private readonly IWeaponGiver _arsenal;
    private readonly IEngineEntities _entities;
    private readonly IEnginePlayers _players;
    private readonly IEngineWorldQuery _world;

    public ImpulseCheats(ILog log, IWeaponGiver arsenal, IEngineEntities entities,
                         IEnginePlayers players, IEngineWorldQuery world, IEngineConsole console)
    {

        _log = log;
        _arsenal = arsenal;
        _entities = entities;
        _players = players;
        _world = world;
        CheatsEnabled = () => CheatsOn(NativeConsole.ValueOf(console, "sv_cheats"));
    }

    /// <summary>
    /// The sv_cheats gate. A seam so a test can hold it open or shut; in the
    /// game it reads the engine's convar live, per impulse — impulses are rare
    /// enough that a lookup per press costs nothing.
    /// </summary>
    public Func<bool> CheatsEnabled { get; set; }

    /// <summary>
    /// Whether a convar value string means cheats are on. Null — no engine, or
    /// no such convar — fails CLOSED, as does garbage: a gate that cannot read
    /// its own switch must stay shut.
    /// </summary>
    public static bool CheatsOn(string? svCheats)
        => int.TryParse(svCheats, out int value) && value != 0;

    /// <summary>
    /// One command's impulse, for one player. True when the impulse was a cheat
    /// this class handled; false for zero, for cheats-off, and for every number
    /// this build has no answer to — silence is stock's behaviour for those too.
    /// </summary>
    public bool Run(EntityHandle player, int impulse)
    {
        if (impulse == 0 || !CheatsEnabled())
            return false;

        // Stock ran these from ItemPostFrame, which a dead player does not get:
        // a corpse cannot re-arm itself out of the death it is in.
        if (!_players.IsAlive(player))
            return false;

        switch (impulse)
        {
            case GiveAll:
                GiveEverything(player);
                return true;

            case Identify:
                IdentifyAimed(player);
                return true;

            case RemoveAimed:
                RemoveAimedEntity(player);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Impulse 101: everything this mod has. Every ammo pool to its carry limit
    /// first — so each weapon arrives onto full reserves — then every weapon row
    /// the mod registers, then stock's top-up: 25 health when below 100. Stock's
    /// EquipSuit is dropped with the suit: this build has no HEV suit to equip.
    /// </summary>
    private void GiveEverything(EntityHandle player)
    {
        foreach (var ammo in AmmoTypes.All)
            _arsenal.GiveAmmo(player, ammo.MaxCarry, ammo.Name);

        int given = 0;

        foreach (var weapon in WeaponTypes.All)
        {
            if (_arsenal.GiveWeapon(player, weapon.Name))
                given++;
        }

        int health = _entities.GetHealth(player);
        int healed = HealedTo(health);

        if (healed != health)
            _entities.SetHealth(player, healed);

        _log.Msg($"[SourceSharp/Impulse] 101: {given} weapon(s) given, " +
                $"{AmmoTypes.All.Length} pool(s) filled, health {health} -> {healed}\n");
    }

    /// <summary>
    /// Stock's TakeHealth(25) when below 100: a quarter back, capped at the
    /// spawn maximum. Dead (zero) stays dead — 101 is a resupply, not a revive
    /// — and a health already at or over the maximum is left alone.
    /// </summary>
    public static int HealedTo(int health)
        => health <= 0 || health >= SsPlayer.SpawnHealth
            ? health
            : Math.Min(SsPlayer.SpawnHealth, health + 25);

    /// <summary>
    /// Impulse 106: what am I looking at. Classname and targetname of the
    /// entity under the crosshair, to the console — stock printed model and
    /// globalname too, which this build's entities do not carry the same way.
    /// </summary>
    private void IdentifyAimed(EntityHandle player)
    {
        var hit = _world.TracePlayerAim(player, AimRange, out _);

        if (hit == EntityHandle.None)
        {
            _log.Msg("[SourceSharp/Impulse] 106: nothing there\n");
            return;
        }

        string name = _entities.GetName(hit);

        _log.Msg($"[SourceSharp/Impulse] 106: Classname: {_entities.GetClassName(hit)}" +
                (name.Length > 0 ? $" - Name: {name}\n" : " - Name: No Targetname\n"));
    }

    /// <summary>
    /// Impulse 203: remove the entity under the crosshair. The world and
    /// players are refused — stock would UTIL_Remove a player too, which was a
    /// crash nobody meant to keep.
    /// </summary>
    private void RemoveAimedEntity(EntityHandle player)
    {
        var hit = _world.TracePlayerAim(player, AimRange, out _);

        if (hit == EntityHandle.None || hit == player)
            return;

        string className = _entities.GetClassName(hit);

        if (className == "worldspawn" || className == EntityNames.SsPlayer)
            return;

        _log.Msg($"[SourceSharp/Impulse] 203: removing {className}\n");
        _entities.Remove(hit);
    }
}
