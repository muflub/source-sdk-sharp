using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// The rules' content hash, logged once at server start (plan_descent_logic
/// X5: the host contract has no member for it, so the game's line is this
/// one): <c>[TF2Descent] rules content &lt;hash&gt;</c>, for the host's log to
/// be compared with.
/// </summary>
[SideService(Side.Server)]
public sealed class RulesContentLine(TF2DescentRules rules, ILog log) : IGameLifecycle
{
    /// <summary>In TF2 Descent's hero block.</summary>
    public int LifecycleOrder => ModStages.First + 200;

    /// <summary>The line, as logged.</summary>
    public string Line => $"[TF2Descent] rules content {rules.ContentHash}\n";

    public void OnPostInit() => log.Msg(Line);
}

/// <summary>What the hero's dev commands share: the sv_cheats gate and the hero of a slot.</summary>
public sealed class HeroCommandGate(SvCheatsConVar cheats, Heroes heroes, ILog log)
{
    public ILog Log => log;

    /// <summary>The hero in <paramref name="slot"/> when cheats are on and there is one; else null, and the console told why.</summary>
    public HeroComponent? Hero(string command, int slot)
    {
        if (!cheats.Value)
        {
            log.Warning($"[TF2Descent/Hero] {command} runs only under sv_cheats\n");
            return null;
        }

        if (heroes.Of(slot) is not { Sheet: not null } hero)
        {
            log.Warning($"[TF2Descent/Hero] {command}: player {slot} is no hero with a sheet\n");
            return null;
        }

        return hero;
    }

    /// <summary>One hero's sheet, stats and mana, as the console shows them.</summary>
    public static string Describe(int slot, HeroComponent hero)
    {
        var s = hero.Sheet!;
        var st = hero.Stats!;
        var c = CultureInfo.InvariantCulture;
        string attributes = string.Join(" ", s.Attributes.Select(a => string.Create(c, $"{a.Key} {a.Value}/{st.Now(a.Key):0.#}")));
        return string.Create(c,
            $"[TF2Descent/Hero] {slot} {s.Name}: {s.ClassId} level {s.Level} xp {s.Xp} points {s.AttributePoints} skill points {s.SkillPoints} " +
            $"hardcore {(s.Hardcore ? 1 : 0)} | {attributes} | life max {st.MaxLife:0.#} mana {hero.Mana:0.##}/{st.MaxMana:0.#} " +
            $"regen {st.ManaRegenPerSecond:0.###}/s speed {st.MoveSpeed:0.#} armour {st.Armour:0.#} crit {st.CritChance:0.###} " +
            $"| net mana {hero.State?.Mana} level {hero.State?.Level} | spells {Spells(s)} | bestiary {Bestiary(s)}\n");
    }

    /// <summary>The sheet's spell ranks read from tomes (<c>fireball 2</c>), or <c>none</c>.</summary>
    public static string Spells(TF2.Descent.Rules.Hero.HeroSheet sheet) =>
        sheet.SpellRanks.IsEmpty
            ? "none"
            : string.Join(" ", sheet.SpellRanks.Select(b => string.Create(CultureInfo.InvariantCulture, $"{b.Key} {b.Value}")));

    /// <summary>The sheet's kills by template (<c>runt_scout 2 soldier 1</c>), or <c>none</c>.</summary>
    public static string Bestiary(TF2.Descent.Rules.Hero.HeroSheet sheet) =>
        sheet.Bestiary.IsEmpty
            ? "none"
            : string.Join(" ", sheet.Bestiary.Select(b => string.Create(CultureInfo.InvariantCulture, $"{b.Key} {b.Value}")));
}

/// <summary><c>tf2_descent_sheet &lt;slot&gt;</c>: the hero's sheet, stats and mana on the console.</summary>
[ConCommand("tf2_descent_sheet", Help = "tf2_descent_sheet <player slot>: print the hero's sheet, stats and mana (sv_cheats).")]
public sealed class HeroSheetCommand(SvCheatsConVar cheats, Heroes heroes, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot)
    {
        var gate = new HeroCommandGate(cheats, heroes, log);
        if (gate.Hero("tf2_descent_sheet", slot) is { } hero)
            log.Msg(HeroCommandGate.Describe(slot, hero));
    }
}

/// <summary>
/// <c>tf2_descent_setlevel &lt;slot&gt; &lt;level&gt;</c>: the hero to a level
/// by the rules (the experience that level takes, through
/// <see cref="HeroRules.GainXp"/>, so the points come with it and the audit
/// holds). A level at or below the hero's is refused: the rules only grow a sheet.
/// </summary>
[ConCommand("tf2_descent_setlevel", Help = "tf2_descent_setlevel <player slot> <level>: raise the hero to a level by the rules (sv_cheats).")]
public sealed class HeroSetLevelCommand(SvCheatsConVar cheats, Heroes heroes, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, [ArgRange(1, 1000)] int level)
    {
        var gate = new HeroCommandGate(cheats, heroes, log);
        if (gate.Hero("tf2_descent_setlevel", slot) is not { } hero)
            return;

        var heroRules = hero.Rules.Heroes;
        int was = hero.Sheet!.Level;
        if (level <= was || level > heroRules.LevelCap)
        {
            log.Warning($"[TF2Descent/Hero] tf2_descent_setlevel: level {level} is not above {was} and at most {heroRules.LevelCap}\n");
            return;
        }

        var result = hero.Apply(sheet => heroRules.GainXp(sheet, heroRules.XpAtLevel(level) - sheet.Xp).Sheet);
        var audit = heroRules.Audit(hero.Sheet!);
        log.Msg($"[TF2Descent/Hero] {slot} set to level {hero.Sheet!.Level}: {(result.Ok ? "applied" : "refused " + result.Rule)}, audit {(audit.Ok ? "ok" : audit.Rule)}\n");
    }
}

/// <summary><c>tf2_descent_mana &lt;slot&gt; &lt;amount&gt;</c>: sets the hero's current mana (held inside 0..max).</summary>
[ConCommand("tf2_descent_mana", Help = "tf2_descent_mana <player slot> <amount>: set the hero's current mana (sv_cheats).")]
public sealed class HeroManaCommand(SvCheatsConVar cheats, Heroes heroes, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, float amount)
    {
        var gate = new HeroCommandGate(cheats, heroes, log);
        if (gate.Hero("tf2_descent_mana", slot) is not { } hero)
            return;

        hero.SetMana(amount);
        log.Msg(string.Create(CultureInfo.InvariantCulture, $"[TF2Descent/Hero] {slot} mana {hero.Mana:0.##}/{hero.Stats!.MaxMana:0.#}\n"));
    }
}

/// <summary><c>tf2_descent_refill &lt;slot&gt;</c>: life and mana to full, as a level-up does (L3).</summary>
[ConCommand("tf2_descent_refill", Help = "tf2_descent_refill <player slot>: life and mana to full, as a level-up does (sv_cheats).")]
public sealed class HeroRefillCommand(SvCheatsConVar cheats, Heroes heroes, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot)
    {
        var gate = new HeroCommandGate(cheats, heroes, log);
        if (gate.Hero("tf2_descent_refill", slot) is not { } hero)
            return;

        hero.RefillLifeAndMana();
        log.Msg(string.Create(CultureInfo.InvariantCulture, $"[TF2Descent/Hero] {slot} refilled: mana {hero.Mana:0.##}/{hero.Stats!.MaxMana:0.#}\n"));
    }
}
