using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>
/// <c>ai_bot_kit &lt;name&gt; &lt;weapon&gt;</c> (plan_hl2mp_ai §12.3, the aim range's
/// harness, <c>tools/ai-aim-range</c>): the bot holds that one weapon and a
/// full reserve, so a measurement is of the weapon it names.
/// </summary>
[ConCommand("ai_bot_kit", Help = "ai_bot_kit <name> <weapon>: the bot holds only that weapon, with a full reserve", Flags = ConVarFlags.Cheat)]
public sealed class AiBotKitCommand(Hl2mpBots bots, Arsenal arsenal, WeaponCatalog weapons, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        string[] p = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2 || weapons.For(p[1]) is not { } type)
        {
            log.Warning("[SourceSharp/Ai] usage: ai_bot_kit <name> <weapon class>\n");
            return;
        }

        foreach (Hl2mpBot bot in bots.Bots)
        {
            if (!string.Equals(bot.Name, p[0], StringComparison.OrdinalIgnoreCase))
                continue;
            EntityHandle player = bots.Applier.PlayerOf(bot.Id);
            arsenal.Strip(player);
            if (!arsenal.GiveWeapon(player, p[1]))
                log.Warning($"[SourceSharp/Ai] ai_bot_kit: {bot.Name} refused {p[1]}\n");
            arsenal.Switch(player, p[1]);
            if (type.AmmoName.Length > 0)
                arsenal.GiveAmmo(player, 9999, type.AmmoName);
            return;
        }

        log.Warning($"[SourceSharp/Ai] ai_bot_kit: no bot called '{p[0]}'\n");
    }
}

/// <summary>
/// <c>ai_bot_streak &lt;name&gt; hot|cold</c> (§5.5): the bot starts that streak now, for the aim
/// range to measure a streak by itself; told to its brain as an event, as anything is.
/// </summary>
[ConCommand("ai_bot_streak", Help = "ai_bot_streak <name> hot|cold: the bot starts that streak now", Flags = ConVarFlags.Cheat)]
public sealed class AiBotStreakCommand(Hl2mpBots bots, Senses.Hl2mpAiSession ai, IEngineFrame frame, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        string[] p = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2 || p[1] is not ("hot" or "cold"))
        {
            log.Warning("[SourceSharp/Ai] usage: ai_bot_streak <name> hot|cold\n");
            return;
        }

        foreach (Hl2mpBot bot in bots.Bots)
        {
            if (!string.Equals(bot.Name, p[0], StringComparison.OrdinalIgnoreCase))
                continue;
            ai.Session.Push(AiEvent.StreaksSet(bot.Id, new Tick(frame.TickCount()), p[1] == "hot" ? AiEvent.StreakHot : AiEvent.StreakCold));
            return;
        }

        log.Warning($"[SourceSharp/Ai] ai_bot_streak: no bot called '{p[0]}'\n");
    }
}

/// <summary>
/// <c>ai_aim_tough &lt;slot&gt; &lt;health&gt;</c> (§12.3): the player in that slot is
/// held at that health every tick, so a target outlives its measurement and
/// its hits are still told; 0 stops.
/// </summary>
[ConCommand("ai_aim_tough", Help = "ai_aim_tough <slot> <health>: hold a player at that health each tick (0 stops): the aim range's target", Flags = ConVarFlags.Cheat)]
public sealed class AiAimToughCommand(Senses.AimMeter meter, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        string[] p = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int health))
        {
            log.Warning("[SourceSharp/Ai] usage: ai_aim_tough <slot> <health>\n");
            return;
        }

        meter.Tough(slot, health);
    }
}

/// <summary>
/// <c>ai_curve &lt;parameter&gt; &lt;level&gt; &lt;value&gt;</c> (§12.3): bots added from now
/// on read that value at that level of that curve, for the aim range's
/// probes; the file on disk is the numbers that ship.
/// </summary>
[ConCommand("ai_curve", Help = "ai_curve <parameter> <level> <value>: later bots read that curve value (the aim range's probes)", Flags = ConVarFlags.Cheat)]
public sealed class AiCurveCommand(Skills.ISkillCurveSource curves, Senses.Hl2mpAiSession ai, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        string[] p = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 3 || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)
            || !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            log.Warning("[SourceSharp/Ai] usage: ai_curve <parameter> <level> <value>\n");
            return;
        }

        if (curves.Curves is not { } now || !now.All.Any(c => c.Parameter == p[0]) || level is < 0 or > StandardSkills.Cap)
        {
            log.Warning($"[SourceSharp/Ai] ai_curve: no curve '{p[0]}' at level {level}\n");
            return;
        }

        ai.Session.Curves = curves.Probe(p[0], level, value);
        log.Msg(string.Create(CultureInfo.InvariantCulture, $"[SourceSharp/Ai] ai_curve: {p[0]} at {level} is {value}\n"));
    }
}
