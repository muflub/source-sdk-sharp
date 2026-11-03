//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Entities.Items;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Buildings;

/// <summary>
/// The <c>build &lt;type&gt; [mode]</c> client command (the <c>build</c>
/// ClientCommand branch, tf_player.cpp:7594): the engineer's order —
/// refused while he carries a building (:7601-7603) — lands as
/// <c>StartBuildingObjectOfType</c> (tf_player_shared.cpp:13234-13258):
/// the <c>CanBuild</c> gate, then the builder weapon into the hands. The
/// blueprint is born at the deploy (<c>tf_weapon_builder.cpp:165-172</c>)
/// and stands where the crosshair walks when the trigger places it.
///
/// <para>
/// The bare <c>build</c> is stock's usage line alone (:7636): the command
/// never opens the build menu — that is the client's own show arm, the
/// build PDA the hands hold (<c>tf_weapon_pda.cpp:157-180</c>) — and a
/// choice in the menu comes back as the two-number order below.
/// <c>build 3</c> is stock's backward-compat spelling of the teleporter
/// exit (:7613-7622).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFBuildCommand(ITFMapPlayers players, ITFBuildingPickup pickup,
                                   ITFBuilderPlacement placement, ILog log,
                                   ITeamRoundState? round = null, ITFStalemateSettings? stalemate = null)
    : IClientCommandHook, IOrderedHook
{
    private readonly ITeamRoundState? _round = round;
    private readonly ITFStalemateSettings? _stalemate = stalemate;

    public const string Verb = "build";

    /// <summary>A stage of its own in TF2's thousand.</summary>
    public const int Order = 1_000_000 - 1_000 + 930;

    /// <summary>Stock's backward-compat spelling: <c>build 3</c> alone is the teleporter exit.</summary>
    public const int LegacyTeleporterExit = 3;

    public int HookOrder => Order;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (!string.Equals(command, Verb, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!players.IsAlive(player))
        {
            log.Msg($"[TF2/Build] {player}: dead, cannot build\n");
            return true;
        }

        // In a melee-only sudden death the order is refused outright
        // (tf_player.cpp:7598-7599).
        if (_round?.State == RoundState.Stalemate && _stalemate?.MeleeOnly == true)
        {
            log.Msg($"[TF2/Build] {player}: the sudden death is melee-only\n");
            return true;
        }

        // Can't issue a build command while carrying an object
        // (tf_player.cpp:7601-7603); the builder in the arms places it.
        if (pickup.Carried(player) is { } carried)
        {
            log.Msg($"[TF2/Build] {player}: carrying a {TFObjectClassNames.Raw(carried.Type)}; "
                  + "build refused until it is placed\n");
            return true;
        }

        // IsTaunting (tf_player.cpp:7605-7606): a taunt holds the command.
        if (players.InCond(player, TFCond.Taunting))
        {
            log.Msg($"[TF2/Build] {player}: taunting; the build order waits\n");
            return true;
        }

        // The bare build is stock's usage line alone (tf_player.cpp:7636):
        // the command never opens the build menu — that is the client's
        // own show arm, the build PDA the hands hold
        // (tf_weapon_pda.cpp:157-180).
        if (args.Length < 1)
        {
            log.Warning("[TF2/Build] Usage: build <building> <mode>\n");
            return true;
        }

        if (!int.TryParse(args[0], System.Globalization.CultureInfo.InvariantCulture, out int number))
        {
            log.Warning($"[TF2/Build] build {args[0]}: the type is a number, 0 dispenser, 1 teleporter, 2 sentrygun\n");
            return true;
        }

        var type = (TFObjectType)number;
        int mode = 0;
        if (number == LegacyTeleporterExit && args.Length == 1)
        {
            type = TFObjectType.Teleporter;
            mode = 1;
        }
        else if (args.Length >= 2)
        {
            int.TryParse(args[1], System.Globalization.CultureInfo.InvariantCulture, out mode);
        }

        // StartBuildingObjectOfType (tf_player_shared.cpp:13234-13258): the
        // gate, then the builder — the blueprint at the deploy.
        if (!placement.BeginOrder(player, type, mode))
            log.Msg($"[TF2/Build] {player}: build {number} refused\n");
        else
            placement.ArmBuilder(player.EntryIndex);

        return true;
    }
}

/// <summary>
/// The <c>destroy &lt;type&gt; &lt;mode&gt;</c> client command
/// (tf_player.cpp:7665-7704, the pair to <c>build</c>): the engineer —
/// spies cannot destroy, they sap — takes down their own one of the kind
/// and mode; a bare <c>destroy 3</c> is the old bind for the teleport
/// exit, now a mode (tf_player.cpp:7676-7685). Each object tells its
/// builder as it goes (<see cref="ITFObjectAnnouncer"/>).
/// </summary>
[SideService(Side.Server)]
public sealed class TFDestroyCommand(ITFMapPlayers players, ITFObjectBuilder builder, ILog log)
    : IClientCommandHook, IOrderedHook
{
    public const string Verb = "destroy";

    /// <summary>A stage of its own in TF2's thousand.</summary>
    public const int Order = 1_000_000 - 1_000 + 931;

    public int HookOrder => Order;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (!string.Equals(command, Verb, StringComparison.OrdinalIgnoreCase))
            return false;

        // Spies can't destroy buildings (sappers) — the class gate
        // (tf_player.cpp:7669).
        if (args.Length < 1 || players.ClassOf(player) != TFClass.Engineer)
            return true;

        if (!players.IsAlive(player))
        {
            log.Msg($"[TF2/Build] {player}: dead, cannot destroy\n");
            return true;
        }

        int type, mode;
        if (args.Length == 1)
        {
            // Fixup old binds: the teleport exit is now a mode
            // (tf_player.cpp:7676-7685).
            if (!TryNumber(args[0], out type))
                return true;
            if (type == 3)
            {
                type = 1;
                mode = 1;
            }
            else
                mode = 0;
        }
        else if (args.Length == 2)
        {
            if (!TryNumber(args[0], out type) || !TryNumber(args[1], out mode))
                return true;
        }
        else
        {
            log.Warning("[TF2/Build] Usage: destroy <building> <mode>\n");
            return true;
        }

        if (!builder.DestroyOne(player, (TFObjectType)type, mode))
            log.Msg($"[TF2/Build] {player}: nothing of type {type} mode {mode} to destroy\n");

        return true;
    }

    private bool TryNumber(string word, out int number)
    {
        if (int.TryParse(word, System.Globalization.CultureInfo.InvariantCulture, out number))
            return true;

        log.Warning($"[TF2/Build] destroy {word}: the type is a number, 0 dispenser, 1 teleporter, 2 sentrygun\n");
        return false;
    }
}
