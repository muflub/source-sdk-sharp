//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Buildings;
using TF2.Server.Entities.Ctf;
using TF2.Server.Voice;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Player;

/// <summary>
/// The Eureka Effect's teleport home: the <c>eureka_teleport</c> command
/// (<c>CTFPlayer::ClientCommand</c>, tf_player.cpp:7705-7739) and the landing
/// when the taunt ends (<c>CTFPlayer::</c>'s
/// <c>m_bIsTeleportingUsingEurekaEffect</c> block, tf_player.cpp:1560-1621).
///
/// <para>
/// The command gates on the engineer and the held weapon's
/// <c>alt_fire_teleport_to_spawn</c> — stock's ON_OTHER read of the active
/// weapon, :7719-7722 — records the target (<c>m_eEurekaTeleportTarget</c>:
/// the argument, else home) and starts the taunt
/// (<c>Taunt(TAUNT_SPECIAL, MP_CONCEPT_TAUNT_EUREKA_EFFECT_TELEPORT)</c>,
/// :7734). The landing, asked from the taunt watch the tick the condition is
/// gone, drops the flag (:1602) and sounds the send (:1605) and moves: to
/// the builder's teleporter exit when that was the target and it stands
/// built (<c>RecieveTeleportingPlayer</c>, :1611-1614 — the arrival at deck
/// height, the exit's angles, no velocity), else the spawn spot
/// (<c>TFGameRules::GetPlayerSpawnSpot</c>, :1618 — the selectors' choice
/// through the placement chain, the ground drop included, the one-unit lift
/// and the facing after it). The flag's rune, the screen fade and the
/// arrival's solid-block and telefrag sweeps wait with the client show.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFEurekaTeleport(TFPlayerCarriers carriers, IEngineEntities entities, IEnginePlayers enginePlayers,
                                     ITeams teams, IEngineSound sound, ITFBuildings buildings,
                                     TFTauntCommand taunt, IHooks<ISpawnPlacement>? placement = null,
                                     ITFWeaponOwners? owners = null, AttributeHooks? hooks = null,
                                     ICtfRules? ctf = null) : IClientCommandHook, IOrderedHook
{
    /// <summary><c>eureka_teleport</c> (tf_player.cpp:7705).</summary>
    public const string Command = "eureka_teleport";

    /// <summary><c>EUREKA_TELEPORT_HOME</c> (tf_shareddefs.h:1847); the exit is 1.</summary>
    public const int Home = 0;

    public int HookOrder => TFPlayerOrders.EurekaTeleport;

    /// <summary>The target each pending player chose, and whether the taunt owes them a landing.</summary>
    private readonly Dictionary<int, int> _pending = [];

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (!string.Equals(command, Command, StringComparison.OrdinalIgnoreCase))
            return false;

        // The gates (:7709-7722): the class, and the active weapon's
        // attribute. The kart and hell arms (:7713-7717) are the halloween
        // lane's, not this build's; a refused taunt consumes the command, as
        // stock's fallthrough returns do.
        if (carriers.ClassOf(player) != TFClass.Engineer)
            return true;

        int teleport = hooks?.Int(0, "alt_fire_teleport_to_spawn",
                                  owners?.ActiveItemOf(player.EntryIndex)) ?? 0;
        if (teleport == 0)
            return true;

        // The target (m_eEurekaTeleportTarget, :7724-7731): the argument's
        // number, home without one.
        int target = args.Length > 0 && int.TryParse(args[0], out int parsed) ? parsed : Home;

        if (!taunt.TauntNow(player, TFConceptSpeech.TauntEurekaEffect))
            return true;

        _pending[player.EntryIndex] = target;
        return true;
    }

    /// <summary>
    /// The taunt went (the cond gone, asked from <c>TFTauntThink</c>'s tick
    /// after its own): the landing, once — the pending target is spent here.
    /// </summary>
    internal void TauntWent(EntityHandle player)
    {
        if (!_pending.Remove(player.EntryIndex, out int target))
            return;

        // The drops and the send (:1602-1605): the carried flag goes back to
        // its base and the departure sounds from the player.
        ctf?.CarriedBy(player)?.DropFlag(player);
        sound.EntityEmitSound(player, TFTeleporter.SendSound);

        // The chosen exit, built (:1608-1614): the builder's own mode 1
        // teleporter that stands past its construction. Anything else — no
        // exit, a building one, the home target — is the spawn (:1618).
        if (target != Home
            && buildings.OwnedBy(player, TFObjectType.Teleporter, mode: 1)
                        .OfType<TFTeleporter>().FirstOrDefault(stands => stands.State != TFTeleporterState.Building)
                        is { } exit)
        {
            entities.Teleport(player, entities.GetOrigin(exit.Handle) + new Vector(0f, 0f, TFTeleporter.ArrivalHeight),
                              entities.GetAngles(exit.Handle), zeroVelocity: true);
            return;
        }

        EntityHandle spot = teams.SelectSpawn(player);
        if (spot == EntityHandle.None)
            return;   // no spot: stock's GetPlayerSpawnSpot leaves the player where they stand

        // The placement chain's answer (the ground drop among them), then the
        // lift and the facing — CGameRules::GetPlayerSpawnSpot's tail, whose
        // core home this is; the interpolation breaks as any spawn's does.
        Vector origin = entities.GetOrigin(spot);
        if (placement is not null)
        {
            foreach (ISpawnPlacement hook in placement.All)
                origin = hook.PlaceAt(player, spot, origin);
        }

        enginePlayers.PlaceAtSpawn(player, origin with { Z = origin.Z + 1f }, entities.GetAngles(spot));
        enginePlayers.BreakInterpolation(player);
    }

    /// <summary>The pending landing dies with the player; the next taunt starts clean.</summary>
    internal void Left(EntityHandle player) => _pending.Remove(player.EntryIndex);
}

/// <summary>
/// The eureka's hook on the taunt's life: the landing is asked the tick the
/// condition stands removed — the stop's water arm and the countdown's
/// expiry both — and the player's leaving clears the pending target.
/// </summary>
public sealed class TFEurekaTauntWatch(TFEurekaTeleport? eureka, TFPlayerConditions conditions) : IPlayerComponent
{
    /// <summary>After the taunt attack (27): the cond is the think's and the attack's before this asks.</summary>
    public int Order => 28;

    private EntityHandle _player = EntityHandle.None;

    public void Attach(EntityHandle player) => _player = player;

    public void Tick(float now)
    {
        // A taunt in flight is never landed early: the ask rides the
        // not-taunting tick alone, the request's own spend its once.
        if (_player == EntityHandle.None || conditions.InCond(_player, TFCond.Taunting))
            return;

        eureka?.TauntWent(_player);
    }

    public void Detach() => eureka?.Left(_player);
}
