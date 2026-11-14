using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Level;

namespace TF2.Descent.Server.Automap;

/// <summary>
/// <c>tf2_descent_automap &lt;player&gt; [reveal]</c>: the rooms a hero has revealed, the room he
/// stands in, and the level's count (UI5's live proof). With <c>reveal</c> (sv_cheats), the shrine of
/// sight's effect: every room revealed to him (<see cref="IAutomapReveal.RevealAll"/>).
/// </summary>
[ConCommand("tf2_descent_automap", Help = "tf2_descent_automap <player slot> [reveal]: the rooms a hero has revealed; reveal (sv_cheats) reveals them all")]
public sealed class AutomapCommand(AutomapService automap, ILevelData levels, Heroes heroes, IEngineEntities entities,
                                   SvCheatsConVar cheats, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, string verb = "")
    {
        if (levels.Current is not { } level || automap.Graph is null)
        {
            log.Msg("[TF2Descent/Automap] no level with rooms: no automap\n");
            return;
        }
        if (heroes.Of(slot) is not { } hero)
        {
            log.Msg($"[TF2Descent/Automap] player {slot} is no hero\n");
            return;
        }
        if (verb == "reveal")
        {
            if (!cheats.Value)
            {
                log.Warning("[TF2Descent/Automap] reveal runs only under sv_cheats\n");
                return;
            }
            automap.RevealAll(slot);
        }

        int here = hero.Player == EntityHandle.None ? -1 : level.RoomAt(entities.GetOrigin(hero.Player));
        var map = automap.MapOf(slot);
        var seen = map.RoomsSeen.Select(r => int.Parse(r, CultureInfo.InvariantCulture)).Order()
            .Select(r => level.Rooms[r].Label.Length > 0 ? $"{r} ({level.Rooms[r].Label})" : r.ToString(CultureInfo.InvariantCulture));
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Automap] player {slot} in room {here}: {map.RoomsSeen.Count} of {level.Rooms.Length} rooms revealed: {string.Join(", ", seen)}; {map.DoorsSeen.Count} door stubs; {automap.RoomsSent} rooms sent\n"));
    }
}
