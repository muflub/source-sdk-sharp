using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Server.Horde.Session;

namespace TF2.Descent.Server.Hero;

/// <summary><c>tf2_descent_down &lt;player&gt;</c>: puts the hero down as a lethal hit would (solo: he dies, by the rules) (sv_cheats).</summary>
[ConCommand("tf2_descent_down", Help = "tf2_descent_down <player slot>: put the hero down as a lethal hit would (solo, he dies) (sv_cheats).")]
public sealed class DownedDownCommand(SvCheatsConVar cheats, Heroes heroes, HeroDowned downed, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot)
    {
        if (new HeroCommandGate(cheats, heroes, log).Hero("tf2_descent_down", slot) is null)
            return;
        log.Msg($"[TF2Descent/Downed] tf2_descent_down {slot}: {downed.DevDown(slot)}\n");
    }
}

/// <summary>
/// <c>tf2_descent_revive &lt;player&gt; [by]</c>: a revive of the downed hero,
/// held by <c>by</c> (or the nearest hero up) as a held +use would hold it,
/// through the same machine: it completes after the rules' hold (sv_cheats).
/// </summary>
[ConCommand("tf2_descent_revive", Help = "tf2_descent_revive <player slot> [by slot]: a held revive of the downed hero (sv_cheats).")]
public sealed class DownedReviveCommand(SvCheatsConVar cheats, Heroes heroes, HeroDowned downed, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, [ArgRange(0, 64)] int by = 0)
    {
        if (new HeroCommandGate(cheats, heroes, log).Hero("tf2_descent_revive", slot) is null)
            return;
        log.Msg($"[TF2Descent/Downed] tf2_descent_revive {slot}: {downed.DevRevive(slot, by)}\n");
    }
}

/// <summary><c>tf2_descent_bleedout &lt;player&gt;</c>: the downed hero's window lapses now (sv_cheats).</summary>
[ConCommand("tf2_descent_bleedout", Help = "tf2_descent_bleedout <player slot>: the downed hero bleeds out now (sv_cheats).")]
public sealed class DownedBleedOutCommand(SvCheatsConVar cheats, Heroes heroes, HeroDowned downed, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot)
    {
        if (new HeroCommandGate(cheats, heroes, log).Hero("tf2_descent_bleedout", slot) is null)
            return;
        log.Msg(downed.DevBleedOut(slot)
            ? $"[TF2Descent/Downed] tf2_descent_bleedout {slot}: the window lapsed\n"
            : $"[TF2Descent/Downed] tf2_descent_bleedout {slot}: player {slot} is not down\n");
    }
}

/// <summary>
/// <c>tf2_descent_downed</c>: every hero's downed state (standing, timer,
/// reviver and progress), the brains told, and every corpse on the level (sv_cheats).
/// </summary>
[ConCommand("tf2_descent_downed", Help = "tf2_descent_downed: every hero's downed state, the horde told, the corpses (sv_cheats).")]
public sealed class DownedPrintCommand(SvCheatsConVar cheats, HeroDowned downed, HeroCorpses corpses, HordeSession session,
                                       HeroDeathCosts costs, DownedHordeTap tap, ILog log)
{
    public void Execute()
    {
        if (!cheats.Value)
        {
            log.Warning("[TF2Descent/Downed] tf2_descent_downed runs only under sv_cheats\n");
            return;
        }
        log.Msg($"[TF2Descent/Downed] {downed.Machines.Count} party machine(s), {downed.Machines.Values.Sum(p => p.Members.Length)} member(s); told {downed.DownsTold} down(s) and {downed.UpsTold} up(s); the horde told {session.HeroDowns} down(s), {session.HeroUps} up(s); deaths {downed.DeathsTold}, respawns {downed.RespawnsTold}, brains told to forget {tap.Forgotten}, back {tap.Returned}\n");
        foreach (var line in downed.Lines())
            log.Msg($"[TF2Descent/Downed]   {line}\n");
        foreach (var line in corpses.Lines())
            log.Msg($"[TF2Descent/Corpse]   {line}\n");
        if (costs.LastCosts.Length > 0)
            log.Msg($"[TF2Descent/Death]   last: {costs.LastCosts}\n");
    }
}

/// <summary><c>tf2_descent_loot_corpse &lt;player&gt;</c>: the hero's use on the corpses in reach, as a +use press (sv_cheats).</summary>
[ConCommand("tf2_descent_loot_corpse", Help = "tf2_descent_loot_corpse <player slot>: the hero uses the corpse in reach, as a +use press (sv_cheats).")]
public sealed class CorpseLootCommand(SvCheatsConVar cheats, Heroes heroes, HeroCorpses corpses, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot)
    {
        if (new HeroCommandGate(cheats, heroes, log).Hero("tf2_descent_loot_corpse", slot) is null)
            return;
        var use = corpses.Use(slot);
        log.Msg(use.IsCompleted
            ? $"[TF2Descent/Corpse] tf2_descent_loot_corpse {slot}: {use.Result}\n"
            : $"[TF2Descent/Corpse] tf2_descent_loot_corpse {slot}: asked\n");
    }
}
