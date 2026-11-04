using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>
/// <c>ai_bot_kit &lt;name&gt; &lt;weapon&gt;</c> (plan_hl2mp_ai §12.3, the aim range's
/// harness, <c>tools/ai-aim-range</c>): the bot holds that one weapon and a
/// full reserve, so a measurement is of the weapon it names; its second trigger only with ammo asked for.
/// </summary>
[ConCommand("ai_bot_kit", Help = "ai_bot_kit <name> <weapon> [alt ammo]: the bot holds only that weapon, with a full reserve and that many for its second trigger (none unless said)", Flags = ConVarFlags.Cheat)]
public sealed class AiBotKitCommand(Hl2mpBots bots, Arsenal arsenal, WeaponCatalog weapons, ILog log)
{
    private IEnumerable<string> BotNames() => bots.Bots.Select(b => b.Name);

    private IEnumerable<string> Weapons() => weapons.All.Select(w => w.Name);

    public void Execute([Complete(From = nameof(BotNames))] string name, [Complete(From = nameof(Weapons))] string weapon, int altAmmo = 0)
    {
        if (weapons.For(weapon) is not { } type)
        {
            log.Warning("[SourceSharp/Ai] usage: ai_bot_kit <name> <weapon class>\n");
            return;
        }

        foreach (Hl2mpBot bot in bots.Bots)
        {
            if (!string.Equals(bot.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            EntityHandle player = bots.Applier.PlayerOf(bot.Id);
            arsenal.Strip(player);
            if (!arsenal.GiveWeapon(player, weapon))
                log.Warning($"[SourceSharp/Ai] ai_bot_kit: {bot.Name} refused {weapon}\n");
            arsenal.Switch(player, weapon);
            if (type.AmmoName.Length > 0)
                arsenal.GiveAmmo(player, 9999, type.AmmoName);
            if (altAmmo > 0 && type.SecondaryAmmoName.Length > 0)
                arsenal.GiveAmmo(player, altAmmo, type.SecondaryAmmoName);
            return;
        }

        log.Warning($"[SourceSharp/Ai] ai_bot_kit: no bot called '{name}'\n");
    }
}

/// <summary>
/// <c>ai_bot_streak &lt;name&gt; hot|cold</c> (§5.5): the bot starts that streak now, for the aim
/// range to measure a streak by itself; told to its brain as an event, as anything is.
/// </summary>
[ConCommand("ai_bot_streak", Help = "ai_bot_streak <name> hot|cold: the bot starts that streak now", Flags = ConVarFlags.Cheat)]
public sealed class AiBotStreakCommand(Hl2mpBots bots, Senses.Hl2mpAiSession ai, IEngineFrame frame, ILog log)
{
    private IEnumerable<string> BotNames() => bots.Bots.Select(b => b.Name);

    public void Execute([Complete(From = nameof(BotNames))] string name, Streak streak)
    {
        foreach (Hl2mpBot bot in bots.Bots)
        {
            if (!string.Equals(bot.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            ai.Session.Push(AiEvent.StreaksSet(bot.Id, new Tick(frame.TickCount()), streak == Streak.Hot ? AiEvent.StreakHot : AiEvent.StreakCold));
            return;
        }

        log.Warning($"[SourceSharp/Ai] ai_bot_streak: no bot called '{name}'\n");
    }

    public enum Streak
    {
        Hot,
        Cold,
    }
}

/// <summary>
/// <c>ai_aim_tough &lt;slot&gt; &lt;health&gt;</c> (§12.3): the player in that slot is
/// held at that health every tick, so a target outlives its measurement and
/// its hits are still told; 0 stops.
/// </summary>
[ConCommand("ai_aim_tough", Help = "ai_aim_tough <slot> <health>: hold a player at that health each tick (0 stops): the aim range's target", Flags = ConVarFlags.Cheat)]
public sealed class AiAimToughCommand(Senses.AimMeter meter)
{
    public void Execute(int slot, int health)
    {
        meter.Tough(slot, health);
    }
}

/// <summary>
/// <c>ai_curve &lt;parameter&gt; &lt;level&gt; &lt;value&gt;</c> (§12.3): bots added from now
/// on read that value at that level of that curve, for the aim range's
/// probes; the file on disk is the numbers that ship.
/// </summary>
[ConCommand("ai_curve", Help = "ai_curve <parameter> <level> <value>: later bots read that curve value (the aim range's probes)", Flags = ConVarFlags.Cheat)]
public sealed class AiCurveCommand(Skills.ISkillCurveSource curves, Senses.Hl2mpAiSession ai, ILog log)
{
    private IEnumerable<string> Parameters() => curves.Curves is { } now ? now.All.Select(c => c.Parameter) : [];

    public void Execute([Complete(From = nameof(Parameters))] string parameter, int level, float value)
    {
        if (curves.Curves is not { } now || !now.All.Any(c => c.Parameter == parameter) || level is < 0 or > StandardSkills.Cap)
        {
            log.Warning($"[SourceSharp/Ai] ai_curve: no curve '{parameter}' at level {level}\n");
            return;
        }

        ai.Session.Curves = curves.Probe(parameter, level, value);
        log.Msg(string.Create(CultureInfo.InvariantCulture, $"[SourceSharp/Ai] ai_curve: {parameter} at {level} is {value}\n"));
    }
}
