using System.Globalization;
using System.Text;

using SourceSharp.Abi;

using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Senses;

namespace TF2.Descent.Server.Horde.Session;

/// <summary>
/// <c>tf2_descent_horde_sight &lt;robot&gt;</c>: why a robot does or does not see each hero now,
/// one line a hero: the verdict, then every stage of the pair (range and the hero's scale with
/// where it came from, the cone, the PVS, the eye and chest lines) and the hero's respawn grace
/// and veil (plan_descent_logic 4.7). The lines are traced again for the print.
/// </summary>
[ConCommand("tf2_descent_horde_sight", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_sight <robot>: why the robot does or does not see each hero now: range (and the lamp and dark's scale), cone, PVS, line, grace")]
public sealed class HordeSightCommand(HordeSession session, Lamp.IHeroVisibility visibility, Hero.RespawnGraces graces, IHordeClock clock, ILog log)
{
    public void Execute(int robot)
    {
        if (session.Brains is not { } b || session.Horde?.Roster is not { } roster || (uint)robot >= (uint)roster.Count)
        {
            log.Msg($"[TF2Descent/Horde] sight: no brain for robot {robot}\n");
            return;
        }

        int body = roster.Body(robot);
        if (body == 0 || !b.BrainAt(body).IsValid)
        {
            log.Msg($"[TF2Descent/Horde] sight: robot {robot} ({roster.State(robot)}) has no body: it sees nobody\n");
            return;
        }

        long now = clock.Tick;
        int lines = 0;
        for (int h = 0; h < HordeSlots.Heroes; h++)
        {
            int seat = b.HeroSlots.SeatAt(h);
            if (seat <= 0)
                continue;
            lines++;
            if (!session.ProbeSight(body, h, out SightProbe p, out float scale))
            {
                log.Msg($"[TF2Descent/Horde] sight: robot {robot} body {body} -> player {seat}: not there (dead or away); grace {Grace(seat, now)}\n");
                continue;
            }

            log.Msg(Line(robot, body, seat, p, scale, visibility.Describe(session.HeroPlayer(h)), Grace(seat, now)));
        }

        if (lines == 0)
            log.Msg($"[TF2Descent/Horde] sight: robot {robot} body {body}: no hero has a slot\n");
    }

    /// <summary>One pair's line (a fact reads it).</summary>
    public static string Line(int robot, int body, int seat, in SightProbe p, float scale, string scaleWhy, string grace)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"[TF2Descent/Horde] sight: robot {robot} body {body} -> player {seat}: {Verdict(p)}; ");
        sb.Append(CultureInfo.InvariantCulture, $"distance {p.Distance:0} of range {p.Range:0} ({(p.InRange ? "in" : "OUT")}; x{scale:0.00}, {scaleWhy}); ");
        sb.Append(CultureInfo.InvariantCulture, $"cone {p.OffConeDeg:0} of {p.HalfConeDeg:0} deg ({(p.InCone ? "in" : "OUT")}); pvs {(p.InPvs ? "yes" : "NO")}; ");
        sb.Append(CultureInfo.InvariantCulture, $"line eye {(p.EyeClear ? "clear" : "blocked")} chest {(p.ChestClear ? "clear" : "blocked")}; ");
        sb.Append(CultureInfo.InvariantCulture, $"grace {grace}, veil {p.Veil}{(p.Watching ? " (watching him since his grace)" : "")}\n");
        return sb.ToString();
    }

    /// <summary>The first reason the pair does not see, or that it does.</summary>
    public static string Verdict(in SightProbe p) =>
        p.Held ? "SEES him"
        : !p.InRange ? "does not see him: out of range"
        : !p.InCone ? "does not see him: outside its cone"
        : !p.InPvs ? "does not see him: not in his PVS"
        : !p.EyeClear && !p.ChestClear ? "does not see him: no clear line"
        : p.Veil == HeroVeil.Hidden ? "does not see him: he is in his respawn's grace"
        : p.Veil == HeroVeil.Watched && p.Watching ? "does not see him: it watched him through his grace and has not lost him from view since"
        : "has him in view: recognising";

    private string Grace(int seat, long now)
    {
        if (!graces.TryGet(seat, out var g))
            return "none";
        if (g.Acted)
            return "ended by his act";
        return now < g.Until
            ? string.Create(CultureInfo.InvariantCulture, $"{(g.Until - now) / clock.TicksPerSecond:0.0}s left (untouchable)")
            : string.Create(CultureInfo.InvariantCulture, $"lapsed {(now - g.Until) / clock.TicksPerSecond:0.0}s ago");
    }
}
