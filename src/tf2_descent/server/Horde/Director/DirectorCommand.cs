using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Ai.Director;
using TF2.Descent.Server.Horde.Motion;

namespace TF2.Descent.Server.Horde.Director;

/// <summary>
/// <c>tf2_descent_director [markers]</c> (plan_descent_logic HO9, "live, a level's roster
/// matches its replay"): the roster's digest beside the spawn director's answer for the same
/// inputs, replayed now by a fresh director, and every row where they differ; the census and
/// the plan's own counts; how many posts a hull would start in solid at (the motion settles
/// those at the lend). With <c>markers</c>, each pack marker's measured clearance.
/// </summary>
[ConCommand("tf2_descent_director", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_director [markers]: the roster's digest against the spawn director's replay and every difference; with markers, each pack marker's clearance")]
public sealed class DirectorCommand(HordeService horde, IHordeRules rules, IMotionWorld world, ILog log)
{
    public const string Tag = "[TF2Descent/Director]";

    public void Execute(string what = "")
    {
        if (horde.Current is not { Begun: true, Level: { } level, Roster: { } roster } c)
        {
            log.Msg($"{Tag} no horde this level\n");
            return;
        }

        var replay = new SpawnDirector(rules.Rules).Plan(level, c.PartySize);
        var held = roster.Rows().ToList();
        var again = replay.Rows().ToList();
        var differences = RosterRow.Differences(held, again);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"{Tag} roster {RosterRow.Digest(held)} replay {RosterRow.Digest(again)}: {(differences.Count == 0 ? "the same" : $"{differences.Count} differences")}; census: {roster.Count} robots in {roster.PackCount} packs; replay: {replay.Summary}\n"));
        foreach (string line in differences)
            log.Msg($"{Tag}   {line}\n");

        int solid = 0;
        foreach (var row in held)
        {
            var (mins, maxs) = RobotMotion.Hull((float)row.Scale);
            var at = HordeVectors.ToEngine(row.Post + new AiVector(0, 0, RobotMotion.SettleLift));
            if (world.Sweep(at, at, mins, maxs, EntityHandle.None).StartSolid) solid++;
        }
        log.Msg($"{Tag} posts a hull would start in solid at: {solid} of {held.Count} (a lent body counts; the motion settles these at the lend)\n");

        if (what != "markers")
            return;
        foreach (var room in level.Rooms)
            for (int m = 0; m < room.Markers.Length; m++)
            {
                var marker = room.Markers[m];
                string clearance = marker.Clearance is { } k
                    ? string.Create(CultureInfo.InvariantCulture, $"west {k.West:0} east {k.East:0} south {k.South:0} north {k.North:0} height {k.Height:0}")
                    : "unmeasured";
                log.Msg(string.Create(CultureInfo.InvariantCulture,
                    $"{Tag}   room {room.Id} marker {m} at ({marker.Position.X:0}, {marker.Position.Y:0}, {marker.Position.Z:0}): {clearance}\n"));
            }
    }
}
