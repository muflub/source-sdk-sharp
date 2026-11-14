using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Loot;

namespace TF2.Descent.Server.Horde.Boss;

/// <summary>
/// A boss level's way down (mine plan D6; plan_descent_logic HO13, <see cref="IBossExit"/>):
/// closed while the level's roster holds a living boss, so a second load of the level (a new
/// roster, its boss alive) starts closed. Asked by the level's <c>logic_level_transition</c>
/// before it sends a hero down; a refusal tells him why (<c>#Descent_Boss_ExitClosed</c>, the
/// boss's name). The level files ship no brush for the exit (their arena and exit rooms carry
/// markers only), so the server holds it.
/// </summary>
[SideService(Side.Server, As = [typeof(IBossExit)])]
public sealed class BossExit(IHordeCurrent horde, IHordeRules rules, IBossWorld world, ILog log) : IBossExit
{
    public const string ClosedToken = "#Descent_Boss_ExitClosed";

    /// <summary>Ways down refused, all levels.</summary>
    public long Refusals { get; private set; }

    public bool Closed => LivingBoss() >= 0;

    public bool MayLeave(int player)
    {
        int row = LivingBoss();
        if (row < 0)
            return true;

        Refusals++;
        var roster = horde.Current!.Roster!;
        string name = rules.Rules.Robots.Template(roster.Template(row)).Name;
        world.Notice(player, ClosedToken, name);
        log.Msg($"[TF2Descent/Boss] the way down is closed: {roster.Template(row)} row {row} stands ({roster.Life(row):0} life); player {player} refused\n");
        return false;
    }

    /// <summary>The roster's living boss row; -1 for none (no horde, no boss, or the boss dead).</summary>
    private int LivingBoss()
    {
        if (horde.Current is not { Roster: { } roster })
            return -1;
        for (int i = 0; i < roster.Count; i++)
            if (roster.IsBoss(i) && roster.State(i) is not (RobotState.Dead or RobotState.Empty))
                return i;
        return -1;
    }
}

/// <summary>
/// A BOSS'S DEATH (plan_descent_logic HO13; mine plan D4, D6), a member of the kill chain after
/// the experience (+300) and the loot (+310), so the kill's XP and its table are paid first:
///
/// <list type="bullet">
/// <item><b>The way down opens</b> for every hero: MvM's bomb on each, and
/// <c>#Descent_Boss_ExitOpen</c> with the boss's name (<see cref="BossExit"/> reads the dead row).</item>
/// <item><b>The camp record</b> (D4): each hero the kill pays (<see cref="ILootParty.Near"/>, the
/// loot's and the XP's range) has the boss added to his sheet's boss kills through the sheet's
/// audited change (<see cref="HeroSheets.Apply"/>), once and only once
/// (<see cref="Rules.Bosses.BossRules.RecordKill"/>); a hero who already holds it is told nothing new. A
/// kill that opens a camp tells him so (<c>#Descent_Boss_Camp</c>, the camp's depth). Hosted, the
/// next checkpoint carries the sheet; the town reads it back (<see cref="SheetBossKills"/>).</item>
/// <item><b>Its death</b>: the boss's MvM explosion; the Tank's wreck particle and blast.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class BossFallen(IHordeRules rules, IBossWorld world, ILootParty party, HeroSheets sheets, IHordeHeroes heroes, IHordeCurrent horde,
                               ILog log)
    : IRobotKillHook, IOrderedHook
{
    /// <summary>In the boss band: after the experience (+300) and the loot (+310).</summary>
    public const int Order = ModStages.First + 1300;

    public const string OpenToken = "#Descent_Boss_ExitOpen", CampToken = "#Descent_Boss_Camp";

    private const int MaxHeroes = 33;

    private readonly TF2.Descent.Ai.Roster.HeroPlace[] _places = new TF2.Descent.Ai.Roster.HeroPlace[MaxHeroes];
    private readonly HeroEye[] _eyes = new HeroEye[MaxHeroes];

    public int HookOrder => Order;

    /// <summary>Bosses killed, camp records written, records a hero already held, exits opened, Tanks wrecked: all levels.</summary>
    public long Kills { get; private set; }

    public long Records { get; private set; }

    public long AlreadyHeld { get; private set; }

    public long Opened { get; private set; }

    public long Tanks { get; private set; }

    public void OnRobotKilled(in RobotKill kill)
    {
        var bosses = rules.Rules.Bosses;
        var sounds = bosses.Settings.Sounds;
        if (bosses.IsTank(kill.Template))
        {
            Tanks++;
            world.Particle(sounds.TankDeathParticle, kill.Position);
            if (kill.Attacker != EntityHandle.None)
                world.Sound(kill.Attacker, sounds.TankDeath);
            log.Msg($"[TF2Descent/Boss] the Tank is wrecked (kill {kill.Sequence}, player {kill.Killer})\n");
            return;
        }

        if (!kill.Boss || bosses.Of(kill.Template) is not { } script)
            return;

        Kills++;
        var t = rules.Rules.Robots.Template(kill.Template);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Boss] {t.Name} ({t.Id}) is dead: kill {kill.Sequence} by player {kill.Killer}, level {kill.Level}, its {kill.Stats.Xp:0} XP and its own table paid by the chain before this\n"));

        // The way down: every living hero on the level hears it and is told.
        Opened++;
        if (horde.Current is { Level: { } level })
        {
            int n = heroes.Gather(level, _places, _eyes);
            for (int h = 0; h < n; h++)
            {
                world.Sound(_eyes[h].Player, sounds.ExitOpens);
                world.Notice(_places[h].Seat, OpenToken, t.Name);
            }

            log.Msg($"[TF2Descent/Boss] the way down is open: {n} hero(es) told\n");
        }

        world.Sound(world.Player(Math.Max(1, kill.Killer)), sounds.Death);

        // The camps (D4).
        int camp = script.Depth switch
        {
            Rules.TownDescents.FirstBoss => Rules.TownDescents.FirstCamp,
            Rules.TownDescents.SecondBoss => Rules.TownDescents.SecondCamp,
            _ => 0,
        };
        string template = kill.Template;
        foreach (var member in party.Near(kill.Position, kill.Killer))
        {
            bool added = false;
            var audit = sheets.Apply(member.Player, s => bosses.RecordKill(s, template, out added));
            if (!audit.Ok)
            {
                log.Warning($"[TF2Descent/Boss] camp record for {member.Hero} refused: {audit.Rule}\n");
                continue;
            }

            if (!added)
            {
                AlreadyHeld++;
                log.Msg($"[TF2Descent/Boss] camp: {member.Hero} (player {member.Player}) already holds {kill.Template}: nothing new\n");
                continue;
            }

            Records++;
            var depths = bosses.DepthsKilled(sheets.Of(member.Player)?.BossKills ?? []);
            log.Msg($"[TF2Descent/Boss] camp: {member.Hero} (player {member.Player}) has killed {kill.Template} (depth {script.Depth}); boss depths killed [{string.Join(',', depths)}], the town offers [{string.Join(',', Rules.TownDescents.Destinations(0, depths))}] from depth 0\n");
            if (camp > 0)
                world.Notice(member.Player, CampToken, camp.ToString(CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>
/// The camps' read of the boss kills (mine plan D4; <see cref="IBossKills"/>, which the town's
/// cage and the travel service ask): the boss depths of the bosses on the hero's sheet
/// (<see cref="Rules.Hero.HeroSheet.BossKills"/>, written by <see cref="BossFallen"/>). Hosted, a
/// character is found by the join's character id; unhosted, by the hero's slot.
/// </summary>
[SideService(Side.Server, As = [typeof(IBossKills)])]
public sealed class SheetBossKills(PlayerService players, HeroSheets sheets, IHordeRules rules) : IBossKills
{
    public IReadOnlyCollection<int> Depths(string characterId)
    {
        foreach (var e in players.Players)
            if (e.CharacterId == characterId)
                return DepthsOf(e.EntIndex);
        return [];
    }

    public IReadOnlyCollection<int> DepthsOf(int player) =>
        sheets.Of(player) is { } sheet ? rules.Rules.Bosses.DepthsKilled(sheet.BossKills) : [];
}
