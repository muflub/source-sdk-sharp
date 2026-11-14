using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Ai.Doors;
using TF2.Descent.Ai.Roster;

namespace TF2.Descent.Server.Horde.Doors;

/// <summary>
/// <c>tf2_descent_shove</c> (plan_descent_logic HO11): each door's shove state: its rooms,
/// middle, normal and width, the robots held up there by a hero on the last tick one was, the
/// heroes standing inside it now, the last shove's tick, hero, body and impulse, and the shoves
/// given there. Doors with nothing to say are summed in one line.
/// </summary>
[ConCommand("tf2_descent_shove", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_shove: each door's shove state: robots held up, heroes inside, the last shove and its impulse")]
public sealed class ShoveCommand(HordeService horde, DoorShove shove, IHordeHeroes heroes, IHordeRules rules, IHordeClock clock, ILog log)
{
    public const string Tag = "[TF2Descent/Shove]";

    public void Execute()
    {
        if (horde.Current is not { Begun: true, Level: { } level, Roster: { } roster })
        {
            log.Msg($"{Tag} no horde this level\n");
            return;
        }

        var doorRules = shove.Rules ?? new DoorShoveRules(rules.Rules.Content.Horde, roster.TicksPerSecond);
        var places = new HeroPlace[HordeController.MaxHeroes];
        var eyes = new HeroEye[HordeController.MaxHeroes];
        int n = heroes.Gather(level, places, eyes);
        var states = shove.Doors;
        long now = clock.Tick;
        int quiet = 0;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"{Tag} tick {now}: {level.Doors.Length} doors, {n} heroes, {shove.Shoves} shoves since the server started; dwell {doorRules.Settings.DwellSeconds:0.##} s (at once at discipline {doorRules.Settings.LowDiscipline} or less), cooldown {doorRules.CooldownTicks} ticks\n"));
        for (int d = 0; d < level.Doors.Length; d++)
        {
            var door = level.Doors[d];
            var s = d < states.Length ? states[d] : default;
            var inside = new List<int>();
            for (int h = 0; h < n; h++)
                if (doorRules.InDoorway(door, places[h].Position))
                    inside.Add(places[h].Seat);
            if (s.Shoves == 0 && s.BlockedTick == 0 && inside.Count == 0)
            {
                quiet++;
                continue;
            }
            string last = s.Shoves == 0 ? "never shoved" : string.Create(CultureInfo.InvariantCulture,
                $"last shove tick {s.LastShove} ({now - s.LastShove} ago) hero {s.LastHero} by body {s.LastBody} impulse ({s.LastImpulse.X:0.#}, {s.LastImpulse.Y:0.#}, {s.LastImpulse.Z:0.#})");
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"{Tag}   door {d} rooms {door.RoomA}-{door.RoomB} at ({door.Position.X:0}, {door.Position.Y:0}, {door.Position.Z:0}) normal ({door.Normal.X:0.##}, {door.Normal.Y:0.##}) width {door.Width:0}: robots held up {s.RobotsBlocked} (tick {s.BlockedTick}), heroes inside [{string.Join(' ', inside)}], {last}, shoves {s.Shoves}\n"));
        }
        log.Msg($"{Tag}   {quiet} doors with no robot held up, no hero inside and no shove\n");
    }
}
