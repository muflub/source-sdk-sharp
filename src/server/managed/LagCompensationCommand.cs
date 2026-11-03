using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_lagcomp</c>: the lag compensation as the server holds it. With no
/// arguments, every slot's history and the client words the gates read. With
/// <c>&lt;shooter slot&gt; &lt;ticks back&gt;</c>, a probe: an attacking
/// command sent that many ticks ago is compensated for that shooter, and each
/// other player's place is read before, during and after, with a shot-mask
/// line down through where the rewind put them traced during the rewind and
/// again after the restore. A cheat: the probe moves players.
/// </summary>
[ConCommand("cs_sv_lagcomp", Flags = ConVarFlags.Cheat,
    Help = "Lag compensation: each slot's history and client words; with <shooter slot> <ticks back>, "
         + "a rewind probe that reads every other player's place and a shot trace during and after.")]
public sealed class LagCompensationCommand(LagCompensation lag, ILagCompensationWorld world,
                                           IEngineWorldQuery trace, IEngineConsole console) : IConCommand
{
    public void Execute(string args)
    {
        string[] argv = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var text = new StringBuilder();

        if (argv.Length >= 2 && int.TryParse(argv[0], out int slot) && int.TryParse(argv[1], out int back))
            Probe(text, slot, back);
        else
            Report(text);

        console.Msg(text.ToString());
    }

    private void Report(StringBuilder text)
    {
        text.Append($"[SourceSharp/LagComp] tick {world.TickCount} curtime {world.CurTime:F3} maxclients {world.MaxClients}\n");
        for (int i = 1; i <= world.MaxClients; i++)
        {
            EntityHandle player = world.PlayerByIndex(i);
            if (player == EntityHandle.None)
                continue;

            IReadOnlyList<LagRecord> track = lag.History(i);
            string span = track.Count > 0
                ? $"{track[0].SimulationTime:F3}..{track[^1].SimulationTime:F3}"
                : "-";
            text.Append($"[SourceSharp/LagComp]   slot {i}: records {track.Count} ({span}) "
                      + $"wants {world.WantsLagCompensation(player)} lerp {world.LerpTime(player):F3} "
                      + $"latency {(world.Latency(player) is float l ? l.ToString("F3") : "none")}\n");
        }
    }

    private void Probe(StringBuilder text, int slot, int back)
    {
        EntityHandle shooter = world.PlayerByIndex(slot);
        if (shooter == EntityHandle.None)
        {
            text.Append($"[SourceSharp/LagComp] probe: no player in slot {slot}\n");
            return;
        }

        var before = new Dictionary<int, Vector>();
        for (int i = 1; i <= world.MaxClients; i++)
        {
            if (i != slot && world.PlayerByIndex(i) is var p && p != EntityHandle.None
                && world.TryGetPlacement(p, out EntityPlacement place))
                before[i] = place.Origin;
        }

        // The command looks at the first other player: the target rules refuse
        // a far player outside the cone the command looks along.
        world.TryGetPlacement(shooter, out EntityPlacement from);
        Vector aim = before.Count > 0 ? before.First().Value - from.Origin : Vector.Zero;

        var cmd = new UserCmd
        {
            TickCount = world.TickCount - back,
            CommandNumber = 1,
            Buttons = (int)InputButtons.Attack,
            Pitch = -MathF.Atan2(aim.Z, MathF.Sqrt(aim.X * aim.X + aim.Y * aim.Y)) * 180f / MathF.PI,
            Yaw = MathF.Atan2(aim.Y, aim.X) * 180f / MathF.PI,
        };

        lag.Start(shooter, in cmd);
        float target = lag.TargetTime(shooter, in cmd);
        var during = new Dictionary<int, (Vector Origin, TraceResult Hit)>();
        foreach (int i in before.Keys)
        {
            EntityHandle p = world.PlayerByIndex(i);
            world.TryGetPlacement(p, out EntityPlacement place);
            during[i] = (place.Origin, Down(place.Origin, shooter));
        }

        bool active = lag.IsCurrentlyDoingLagCompensation;
        lag.Finish(shooter);

        text.Append($"[SourceSharp/LagComp] probe: shooter slot {slot}, server tick {world.TickCount}, command tick {cmd.TickCount}, "
                  + $"target time {target:F3}, compensating {active}\n");
        foreach ((int i, Vector was) in before)
        {
            EntityHandle p = world.PlayerByIndex(i);
            world.TryGetPlacement(p, out EntityPlacement after);
            TraceResult afterHit = Down(during[i].Origin, shooter);
            LagRecord? record = RecordAt(i, target);

            text.Append($"[SourceSharp/LagComp]   slot {i}: before {was} during {during[i].Origin} "
                      + $"record {(record is null ? "-" : record.Origin.ToString())} after {after.Origin}\n");
            text.Append($"[SourceSharp/LagComp]   slot {i}: trace at the rewound place during hits "
                      + $"{Named(during[i].Hit, p)}; after the restore hits {Named(afterHit, p)}\n");
        }
    }

    /// <summary>A shot-mask line from above the head down to the knees, at <paramref name="at"/>.</summary>
    private TraceResult Down(Vector at, EntityHandle shooter)
        => trace.TraceLine(at + new Vector(0f, 0f, 120f), at + new Vector(0f, 0f, 20f), TraceMask.Shot, shooter);

    private static string Named(TraceResult hit, EntityHandle player)
        => !hit.Hit ? "nothing" : hit.Entity == player ? "the player" : $"something else ({hit.Entity})";

    private LagRecord? RecordAt(int slot, float time)
    {
        IReadOnlyList<LagRecord> track = lag.History(slot);
        for (int r = track.Count - 1; r >= 0; r--)
        {
            if (track[r].SimulationTime <= time)
                return track[r];
        }

        return null;
    }
}
