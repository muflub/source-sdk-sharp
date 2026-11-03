using System.Globalization;
using SourceSharp.Abi;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>
/// The skills an <c>ai_bot_add</c> names (plan_hl2mp_ai §3.1.4, §3.5): a preset
/// (<c>easy|normal|hard|expert</c>), or <c>skills=a,b,…,j</c>, which the
/// server judges against the pool whatever the client did. Nothing names
/// "normal" when nothing is given.
/// </summary>
public static class BotSkillSpec
{
    /// <summary>The profile <paramref name="spec"/> names for a bot called <paramref name="name"/>, or the refusal.</summary>
    public static SkillVerdict Parse(string name, string? spec, out SkillProfile? profile)
    {
        profile = null;
        string s = (spec ?? "").Trim();
        if (s.Length == 0)
            s = "normal";

        if (s.StartsWith("skills=", StringComparison.Ordinal))
            return StandardSkills.StandardPool().TryParse(name, s["skills=".Length..], out profile);

        if (SkillPresets.TryGet(s, out SkillProfile? preset))
        {
            profile = preset;
            return SkillVerdict.Ok;
        }

        return SkillVerdict.Refuse($"'{s}' is not a preset ({string.Join("|", SkillPresets.Names)}) or skills=a,b,...");
    }
}

/// <summary>
/// <c>ai_bot_add [name] [easy|normal|hard|expert | skills=a,b,…,j]</c>: with no
/// name, the bot is named from the pools by its most distinctive trait
/// (§3.5.1); a name is used as given.
/// </summary>
[ConCommand("ai_bot_add", Help = "ai_bot_add [name] [easy|normal|hard|expert | skills=a,b,c,d,e,f,g,h,i,j]: add a bot (no name: one is drawn)")]
public sealed class AiBotAddCommand(Hl2mpBots bots, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 2)
        {
            log.Warning("[SourceSharp/Ai] usage: ai_bot_add [name] [easy|normal|hard|expert | skills=a,b,c,d,e,f,g,h,i,j]\n");
            return;
        }

        // One word is a skill spec when it reads as one, else a name.
        bool specOnly = parts.Length == 1 && (parts[0].StartsWith("skills=", StringComparison.Ordinal) || SkillPresets.Names.Contains(parts[0]));
        string? name = parts.Length == 0 || specOnly ? null : parts[0];
        string? spec = specOnly ? parts[0] : parts.Length > 1 ? parts[1] : null;
        SkillVerdict verdict = BotSkillSpec.Parse(name ?? "custom", spec, out SkillProfile? skills);
        string who = name ?? "(unnamed)";
        if (!verdict.Accepted)
        {
            log.Warning($"[SourceSharp/Ai] ai_bot_add {who} refused: {verdict.Refusal}\n");
            return;
        }

        if (bots.Add(name, skills!) is { } refusal)
            log.Warning($"[SourceSharp/Ai] ai_bot_add {who} refused: {refusal}\n");
    }
}

/// <summary><c>ai_bot_kick &lt;name|all&gt;</c>.</summary>
[ConCommand("ai_bot_kick", Help = "ai_bot_kick <name|all>: kick a bot, or every bot")]
public sealed class AiBotKickCommand(Hl2mpBots bots, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        string name = args.Trim();
        if (name.Length == 0)
        {
            log.Warning("[SourceSharp/Ai] usage: ai_bot_kick <name|all>\n");
            return;
        }

        int kicked = bots.Kick(name);
        log.Msg($"[SourceSharp/Ai] ai_bot_kick {name}: {kicked} kicked\n");
    }
}

/// <summary><c>ai_bot_count</c>: how many bots, and each one's name, client and skills.</summary>
[ConCommand("ai_bot_count", Help = "ai_bot_count: the bots in the game")]
public sealed class AiBotCountCommand(Hl2mpBots bots, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        log.Msg($"[SourceSharp/Ai] bots: {bots.Bots.Count}\n");
        foreach (Hl2mpBot bot in bots.Bots)
            log.Msg($"[SourceSharp/Ai]   {bot.Name}: client {bot.Id.Slot}, {bot.Skills.Name} ({bot.Skills.Spent} points)\n");
    }
}

/// <summary>
/// <c>ai_bot_drive &lt;name&gt; &lt;yaw&gt; &lt;seconds&gt;</c>: a scripted intent, no
/// nav (the Phase 3 live check): the bot faces the yaw and runs along it.
/// </summary>
[ConCommand("ai_bot_drive", Help = "ai_bot_drive <name> <yaw> <seconds>: face a yaw and run along it", Flags = ConVarFlags.Cheat)]
public sealed class AiBotDriveCommand(Hl2mpBots bots, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds)
            || seconds <= 0f)
        {
            log.Warning("[SourceSharp/Ai] usage: ai_bot_drive <name> <yaw> <seconds>\n");
            return;
        }

        if (bots.DriveFor(parts[0], yaw, seconds) is { } refusal)
            log.Warning($"[SourceSharp/Ai] ai_bot_drive refused: {refusal}\n");
    }
}

/// <summary>
/// <c>ai_bot_goto &lt;name&gt; &lt;classname | x y z&gt;</c> (plan_hl2mp_ai Phase 3
/// step 5): the bot holds Follow along a flow field to the point, or to the
/// map's nearest placed entity of the class, and logs its arrival and what it
/// picks up.
/// </summary>
[ConCommand("ai_bot_goto", Help = "ai_bot_goto <name> <classname | x y z>: walk a bot along the nav to a map entity or a point", Flags = ConVarFlags.Cheat)]
public sealed class AiBotGotoCommand(Hl2mpBots bots, Nav.IHl2mpNavigation nav, IEngineEntities entities, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        string[] p = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        AiVector target;
        string? pickup = null;
        if (p.Length == 4
            && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            && float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            target = new AiVector(x, y, z);
        }
        else if (p.Length == 2 && nav.Current is { } n)
        {
            Hl2mpBot[] named = [.. bots.Bots.Where(b => string.Equals(b.Name, p[0], StringComparison.OrdinalIgnoreCase))];
            Hl2mpBot? bot = named.Length > 0 ? named[0] : null;
            Nav.MapEntity[] placed = [.. n.Entities.All.Where(e => e.ClassName == p[1])];
            if (bot is null || placed.Length == 0)
            {
                log.Warning($"[SourceSharp/Ai] ai_bot_goto refused: {(bot is null ? $"no bot called '{p[0]}'" : $"the map places no {p[1]}")}\n");
                return;
            }

            Vector at = entities.GetOrigin(bots.Applier.PlayerOf(bot.Value.Id));
            var from = new AiVector(at.X, at.Y, at.Z);
            target = placed.OrderBy(e => e.Origin.DistanceTo(from)).First().Origin;
            pickup = p[1];
        }
        else
        {
            log.Warning("[SourceSharp/Ai] usage: ai_bot_goto <name> <classname | x y z>\n");
            return;
        }

        if (bots.GoTo(p[0], target, pickup) is { } refusal)
            log.Warning($"[SourceSharp/Ai] ai_bot_goto refused: {refusal}\n");
    }
}
