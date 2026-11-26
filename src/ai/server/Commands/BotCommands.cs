using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Server;

/// <summary>
/// The skills an <c>ai_bot_add</c> names (plan_hl2mp_ai §3.1.4, §3.5): a preset
/// (<c>easy|medium|hard|expert</c>), or <c>skills=a,b,…,j</c>, which the
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
        {
            // Eleven levels carry Teamwork (plan_bots_teams_tf2 B.7); ten are the deathmatch ten.
            string list = s["skills=".Length..];
            return StandardSkills.PoolFor(list.Split(',').Length).TryParse(name, list, out profile);
        }

        if (SkillPresets.TryGet(s, out SkillProfile? preset))
        {
            profile = preset;
            return SkillVerdict.Ok;
        }

        return SkillVerdict.Refuse($"'{s}' is not a preset ({string.Join("|", SkillPresets.Names)}) or skills=a,b,...");
    }
}

/// <summary>
/// <c>ai_bot_add [name] [easy|medium|hard|expert | skills=a,b,…,j]</c>: with no
/// name, the bot is named from the pools by its most distinctive trait
/// (§3.5.1); a name is used as given. The bot joins the roster (§3.5); a
/// preset bot draws its levels from its seed.
/// </summary>
[ConCommand("ai_bot_add", Help = "ai_bot_add [name] [easy|medium|hard|expert | skills=a,b,c,d,e,f,g,h,i,j] [team=auto|2|3|4|5] [key=value ...]: add a bot (no name: one is drawn; team: under a mode with teams; a game's own keys, TF2's class= and archetype=, pin it)")]
public sealed class AiBotAddCommand(AiRoster roster, ILog log, IPlayerNotices notices, IBotOptions? gameOptions = null)
{
    private IEnumerable<string> Presets() => SkillPresets.Names;

    public void Execute([Complete(From = nameof(Presets))] string? nameOrSkills = null, [Complete(From = nameof(Presets))] string? skills = null, string? team = null,
                        [Rest] string? more = null)
    {
        // team= may stand in any place (plan_bots_teams_tf2 B.6), and so may a game's own key=value (P7: TF2's class=, archetype=).
        var positional = new List<string>();
        string? teamField = null;
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> words = new[] { nameOrSkills, skills, team }.Where(w => w is not null).Select(w => w!)
            .Concat((more ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (string word in words)
        {
            int eq = word.IndexOf('=', StringComparison.Ordinal);
            string key = eq > 0 ? word[..eq] : "";
            if (key == "team" && teamField is null)
                teamField = word;
            else if (eq > 0 && key != "skills" && key != "profile" && gameOptions?.Keys.Contains(key, StringComparer.OrdinalIgnoreCase) == true)
                options[key] = word[(eq + 1)..];
            else if (eq > 0 && key != "skills" && key != "profile")
            {
                Refuse($"ai_bot_add refused: '{word}' is no option ({string.Join(", ", new[] { "team=" }.Concat(gameOptions?.Keys.Select(k => k + "=") ?? []))})");
                return;
            }
            else
                positional.Add(word);
        }

        // A third plain word is the team, as before; past that, a word too many.
        if (positional.Count >= 3)
        {
            if (teamField is not null || positional.Count > 3)
            {
                Refuse($"ai_bot_add refused: '{positional[teamField is not null ? 2 : 3]}' is one argument too many");
                return;
            }

            teamField = positional[2];
            positional.RemoveAt(2);
        }

        int wanted = RosterTeam.Auto;
        if (teamField is not null)
        {
            string value = teamField.StartsWith("team=", StringComparison.Ordinal) ? teamField["team=".Length..] : teamField;
            if (RosterTeam.Parse(value, out string? bad) is not { } parsed)
            {
                Refuse($"ai_bot_add refused: {bad}");
                return;
            }

            wanted = parsed;
        }

        if (options.Count > 0 && gameOptions?.Check(options) is { } wrong)
        {
            Refuse($"ai_bot_add refused: {wrong}");
            return;
        }

        string? first = positional.Count > 0 ? positional[0] : null, second = positional.Count > 1 ? positional[1] : null;
        // One word is a skill spec when it reads as one, else a name.
        bool specOnly = second is null && first is not null
                        && (first.StartsWith("skills=", StringComparison.Ordinal) || SkillPresets.TryGet(first, out _));
        string? name = specOnly ? null : first;
        string? spec = specOnly ? first : second;
        string who = name ?? "(unnamed)";
        if (roster.Add(name, spec, wanted, options) is { } refusal)
            Refuse($"ai_bot_add {who} refused: {refusal}");
    }

    // The server's console keeps its line; the player who typed it (a listen server's host too) hears it as well.
    private void Refuse(string line)
    {
        log.Warning($"[SourceSharp/Ai] {line}\n");
        notices.Refused(notices.CommandIssuer(), line);
    }
}

/// <summary><c>ai_bot_kick &lt;name|all&gt;</c>: out of the game and off the roster.</summary>
[ConCommand("ai_bot_kick", Help = "ai_bot_kick <name|all>: remove a bot, or every bot, from the game and the roster")]
public sealed class AiBotKickCommand(AiRoster roster, ILog log)
{
    private IEnumerable<string> BotNames() => ["all", .. roster.Names];

    public void Execute([Complete(From = nameof(BotNames)), Rest] string name)
    {
        // The whole of the line: a second "Alyx" is "Alyx 2", quoted or not.
        name = name.Trim('"');
        int kicked = roster.Remove(name);
        log.Msg($"[SourceSharp/Ai] ai_bot_kick {name}: {kicked} kicked\n");
    }
}

/// <summary>
/// <c>ai_bot_say &lt;name&gt; team|all &lt;text&gt;</c>: the bot says a chat line
/// through the say path a player's <c>say</c> and <c>say_team</c> take (the
/// live gate's <c>tdm-chat</c>, plan_bots_teams_tf2 A.5): a scripted line, so a
/// step knows what was said, by whom and to whom.
/// </summary>
[ConCommand("ai_bot_say", Help = "ai_bot_say <name> team|all <text>: the bot says a chat line, to its team or to everyone", Flags = ConVarFlags.Cheat)]
public sealed class AiBotSayCommand(AiBots bots, HostSay say, ILog log)
{
    private IEnumerable<string> BotNames() => bots.Bots.Select(b => b.Name);

    public void Execute([Complete(From = nameof(BotNames))] string name, Audience to, [Rest] string text = "")
    {
        EntityHandle player = bots.PlayerNamed(name);
        if (player == EntityHandle.None)
        {
            log.Warning($"[SourceSharp/Ai] ai_bot_say refused: no bot called '{name}'\n");
            return;
        }

        say.Say(player, text, teamOnly: to == Audience.Team);
    }

    /// <summary>Who hears the line: the speaker's team (<c>say_team</c>) or everyone (<c>say</c>).</summary>
    public enum Audience
    {
        Team,
        All,
    }
}

/// <summary>
/// <c>ai_bot_cmd &lt;name&gt; &lt;client command&gt;</c>: the bot sends a client command as its own player's, through the
/// mods' command chain a player's typed verb takes (<see cref="IBotClientCommand"/>): a scripted order a live proof needs
/// that the bot's play does not give, such as an Engineer's <c>build 1 0</c>.
/// </summary>
[ConCommand("ai_bot_cmd", Help = "ai_bot_cmd <name> <client command>: the bot sends a client command as its own (build 1 0, destroy 2 0)", Flags = ConVarFlags.Cheat)]
public sealed class AiBotCmdCommand(AiBots bots, IBotClientCommand commands, ILog log)
{
    private IEnumerable<string> BotNames() => bots.Bots.Select(b => b.Name);

    public void Execute([Complete(From = nameof(BotNames))] string name, [Rest] string command = "")
    {
        EntityHandle player = bots.PlayerNamed(name);
        if (player == EntityHandle.None || command.Length == 0)
        {
            log.Warning($"[SourceSharp/Ai] ai_bot_cmd refused: {(command.Length == 0 ? "usage: ai_bot_cmd <name> <client command>" : $"no bot called '{name}'")}\n");
            return;
        }

        commands.Run(player, command);
        log.Msg($"[SourceSharp/Ai] ai_bot_cmd {name}: {command}\n");
    }
}

/// <summary><c>ai_bot_count</c>: the roster: each bot's name, skills and drawn levels, active or benched and why.</summary>
[ConCommand("ai_bot_count", Help = "ai_bot_count: the bots wanted, in the game or benched and why")]
public sealed class AiBotCountCommand(AiRoster roster, ILog log)
{
    public void Execute()
    {
        foreach (string line in roster.Status())
            log.Msg(line);
    }
}

/// <summary>
/// <c>ai_bot_drive &lt;name&gt; &lt;yaw&gt; &lt;seconds&gt;</c>: a scripted intent, no
/// nav (the Phase 3 live check): the bot faces the yaw and runs along it.
/// </summary>
[ConCommand("ai_bot_drive", Help = "ai_bot_drive <name> <yaw> <seconds>: face a yaw and run along it", Flags = ConVarFlags.Cheat)]
public sealed class AiBotDriveCommand(AiBots bots, ILog log)
{
    private IEnumerable<string> BotNames() => bots.Bots.Select(b => b.Name);

    public void Execute([Complete(From = nameof(BotNames))] string name, float yaw, float seconds)
    {
        if (seconds <= 0f)
        {
            log.Warning("[SourceSharp/Ai] usage: ai_bot_drive <name> <yaw> <seconds>\n");
            return;
        }

        if (bots.DriveFor(name, yaw, seconds) is { } refusal)
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
public sealed class AiBotGotoCommand(AiBots bots, SourceSharp.Ai.Server.Nav.IAiNavigation nav, IEngineEntities entities, ILog log)
{
    private IEnumerable<string> BotNames() => bots.Bots.Select(b => b.Name);

    /// <summary>What the map places, which is what a class name can mean here.</summary>
    private IEnumerable<string> MapClasses()
        => nav.Current is { } n ? n.Entities.All.Select(e => e.ClassName).Distinct() : [];

    public void Execute([Complete(From = nameof(BotNames))] string name, [Complete(From = nameof(MapClasses))] string classNameOrX, float? y = null, float? z = null)
    {
        AiVector target;
        string? pickup = null;
        if (y is { } atY && z is { } atZ && float.TryParse(classNameOrX, NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
        {
            target = new AiVector(x, atY, atZ);
        }
        else if (y is null && z is null && nav.Current is { } n)
        {
            AiBot[] named = [.. bots.Bots.Where(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))];
            AiBot? bot = named.Length > 0 ? named[0] : null;
            SourceSharp.Ai.Server.Nav.MapEntity[] placed = [.. n.Entities.All.Where(e => e.ClassName == classNameOrX)];
            if (bot is null || placed.Length == 0)
            {
                log.Warning($"[SourceSharp/Ai] ai_bot_goto refused: {(bot is null ? $"no bot called '{name}'" : $"the map places no {classNameOrX}")}\n");
                return;
            }

            Vector at = entities.GetOrigin(bots.Applier.PlayerOf(bot.Value.Id));
            var from = new AiVector(at.X, at.Y, at.Z);
            target = placed.OrderBy(e => e.Origin.DistanceTo(from)).First().Origin;
            pickup = classNameOrX;
        }
        else
        {
            log.Warning("[SourceSharp/Ai] usage: ai_bot_goto <name> <classname | x y z>\n");
            return;
        }

        if (bots.GoTo(name, target, pickup) is { } refusal)
            log.Warning($"[SourceSharp/Ai] ai_bot_goto refused: {refusal}\n");
    }
}

/// <summary>
/// <c>ai_bot_knows &lt;name&gt; x y z</c>: the cell at a point as the bot routes by it (free, costly
/// or blocked) and as the entities truly take it, for a live proof that a bot does not know
/// what it has not been sent.
/// </summary>
[ConCommand("ai_bot_knows", Help = "ai_bot_knows <name> x y z: the cell at a point as the bot knows it, and truly", Flags = ConVarFlags.Cheat)]
public sealed class AiBotKnowsCommand(AiBots bots, SourceSharp.Ai.Server.Nav.AiNav nav, ILog log)
{
    private IEnumerable<string> BotNames() => bots.Bots.Select(b => b.Name);

    public void Execute([Complete(From = nameof(BotNames))] string name, Vector at)
    {
        (float x, float y, float z) = (at.X, at.Y, at.Z);
        if (nav.Current is not { } n || nav.Field is not { } field)
        {
            log.Warning("[SourceSharp/Ai] usage: ai_bot_knows <name> x y z (with navigation loaded)\n");
            return;
        }

        CellId cell = n.Nav.Graph.FindCell(new AiVector(x, y, z));
        foreach (AiBot bot in bots.Bots)
        {
            if (!string.Equals(bot.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            byte known = cell.IsValid ? field.StateFor(bots.Applier.PlayerOf(bot.Id)).Cells?.State(cell.Index) ?? NavCellBlocks.Free : NavCellBlocks.Free;
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[SourceSharp/Ai] ai_bot_knows {bot.Name} ({x:F0} {y:F0} {z:F0}): cell {cell.Index}, knows {known}, truly {(cell.IsValid ? field.TruthAt(cell.Index) : 0)} (0 free, 1 costly, 2 blocked)\n"));
            return;
        }

        log.Warning($"[SourceSharp/Ai] ai_bot_knows: no bot called '{name}'\n");
    }
}

/// <summary><c>ai_think 0|1</c>: whether the bots' brains drive them (on by default; off, they stand still unless scripted).</summary>
[ConCommand("ai_think", Help = "ai_think 0|1: whether the bots' brains drive them", Flags = ConVarFlags.Cheat)]
public sealed class AiThinkCommand(AiBots bots, ILog log)
{
    public void Execute(bool? on = null)
    {
        if (on is { } wanted)
            bots.Thinking = wanted;
        log.Msg($"[SourceSharp/Ai] ai_think {(bots.Thinking ? 1 : 0)}\n");
    }
}

/// <summary><c>ai_think_log 0|1</c>: write each change of a bot's branch, leaf or verdict to the console.</summary>
[ConCommand("ai_think_log", Help = "ai_think_log 0|1: print each bot's decision changes", Flags = ConVarFlags.Cheat)]
public sealed class AiThinkLogCommand(AiBots bots, ILog log)
{
    public void Execute(bool on = true)
    {
        bots.LogThink = on;
        log.Msg($"[SourceSharp/Ai] ai_think_log {(bots.LogThink ? 1 : 0)}\n");
    }
}

/// <summary>
/// The bots' place in the admin menu. While the level cannot take a bot (no
/// navigation), the add items say why in their place rather than doing nothing
/// when chosen; an add refused when it is taken answers why, and the admin who
/// chose it is told (plan_bots_teams_tf2 D.4's refusal line).
/// </summary>
[SideService(Side.Server, As = [typeof(IAdminMenuProvider)])]
public sealed class AiAdminMenu(AiRoster roster, SourceSharp.Ai.Server.Nav.IAiNavigation nav, SourceSharp.Ai.Server.Teams.IRosterTeams teams,
                                SourceSharp.Ai.Server.Teams.AiBotOrdersConVar orders) : IAdminMenuProvider
{
    /// <summary>The add folder's difficulties: the label and the preset each adds.</summary>
    private static (string Label, string Skill)[] Presets
        => [("#SS_UI_Admin_BotEasy", "easy"), ("#SS_UI_Admin_BotNormal", "normal"), ("#SS_UI_Admin_BotHard", "hard"), ("#SS_UI_Admin_BotExpert", "expert")];

    /// <summary>
    /// The Bots folder (plan_bots_teams_tf2 D.4): add a bot of a difficulty, under a
    /// mode with teams to a chosen team too, re-roll them, whether they follow orders
    /// (with teams), and remove them all.
    /// </summary>
    public IEnumerable<AdminNode> AdminItems(int slot)
    {
        // While the level's navigation is built (plan_bots_teams_tf2 E.3) an add is taken: the bot waits and joins when it lands.
        string? unavailable = nav.Building ? null : Unavailable(nav.Refusal, nav.MissingFile);
        var items = new List<AdminNode>
        {
            AdminNode.Folder("#SS_UI_Admin_BotAdd", [.. Presets.Select(p => Add(p.Label, p.Skill, RosterTeam.Auto, unavailable))]),
        };
        if (teams.Teamplay)
        {
            items.Add(AdminNode.Folder("#SS_UI_Admin_BotTeam",
                [.. Enumerable.Range(TeamRule.FirstGameTeam, teams.TeamCount)
                              .Select(t => AdminNode.Folder($"#SS_UI_Team_{t}", [.. Presets.Select(p => Add(p.Label, p.Skill, t, unavailable))]))]));
            items.Add(orders.Value
                ? new AdminNode("#SS_UI_Admin_BotOrdersOff", "bots:orders:0", () => orders.SetValue(false))
                : new AdminNode("#SS_UI_Admin_BotOrdersOn", "bots:orders:1", () => orders.SetValue(true)));
        }
        items.Add(AdminNode.Refusable("#SS_UI_Admin_BotReroll", "bots:reroll", roster.RerollNow));
        items.Add(new AdminNode("#SS_UI_Admin_BotRemoveAll", "bots:remove", () => roster.Remove("all"), Stay: true));
        yield return AdminNode.Folder("#SS_UI_Admin_Bots", items);
    }

    /// <summary>
    /// Why no bot can be added on this level, for the menu, from the navigation's
    /// <paramref name="refusal"/> and the file it found <paramref name="missing"/>; null when one can.
    /// </summary>
    public static string? Unavailable(string? refusal, string? missing)
    {
        if (refusal is null)
            return null;
        if (missing is null)
            return $"Bots need navigation: {refusal}";
        return $"Bots need navigation: {missing} is missing (make map MAP={Path.GetFileNameWithoutExtension(missing)} builds it)";
    }

    // The menu stays up: bots are added a few at a time.
    private AdminNode Add(string label, string skill, int team, string? unavailable)
        => AdminNode.Refusable(label, team == RosterTeam.Auto ? "bots:add:" + skill : $"bots:add:{skill}:team{team}",
                               () => roster.Add(null, skill, team), stay: true, unavailable);
}
