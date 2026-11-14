using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Bestiary;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Server.Hero;

/// <summary>What one hero was paid: the experience the sheet grew by and the rules' level-up.</summary>
public sealed record HeroGain(int PlayerIndex, long Xp, LevelUpResult Result);

/// <summary>Asks for a checkpoint of a hero's sheet: what follows a level-up.</summary>
public interface IHeroLevelCheckpoint
{
    /// <summary>Starts a checkpoint of the hero in <paramref name="entIndex"/>; false when there is no host to send it to (unhosted).</summary>
    bool Checkpoint(int entIndex);
}

/// <summary>
/// The level-up's checkpoint through <see cref="PlayerService.Checkpoint"/>:
/// the sheet as it is now with the evidence that explains it. Unhosted no
/// player is joined to a host, and nothing is sent.
/// </summary>
[SideService(Side.Server, As = [typeof(IHeroLevelCheckpoint)])]
public sealed class PlayerLevelCheckpoint(PlayerService players) : IHeroLevelCheckpoint
{
    public bool Checkpoint(int entIndex)
    {
        if (players.ByEntity(entIndex) is not { } entry)
            return false;

        _ = players.Checkpoint(entry.SteamId);
        return true;
    }
}

/// <summary>
/// Kills pay experience (plan_descent_logic HE5; plan_tf2_rpg D10; rulings L3
/// and L6). On every robot death (<see cref="IRobotKillHook"/>):
///
/// <list type="bullet">
/// <item>Every living hero with a sheet within experience.json's party range
/// of where the robot died, and in the killer's party (WO9; a solo killer is
/// his own party, so a stranger in range is paid nothing), is paid
/// <see cref="TF2.Descent.Rules.Loot.ExperienceRules.KillXp(TF2.Descent.Rules.Robots.RobotStats, int)"/>
/// by his own level, in full: a party does not split it (D10). The killer's
/// place counts for nothing: a hero out of range is not paid though he killed
/// it, and a kill by the world (no hero) pays every hero in range.</item>
/// <item>The sheet takes it through <see cref="HeroComponent.Apply"/>: the
/// rules' <see cref="HeroRules.GainXp"/> (points for each level, the cap's
/// excess discarded) and one more kill of the template in the bestiary
/// (<see cref="BestiaryRules.RecordKill"/>, for every hero paid).</item>
/// <item>The evidence records the experience the sheet grew by and the kill
/// (<see cref="HeroSheets.RecordEarned"/>), so the next checkpoint explains
/// both (RU13).</item>
/// <item>A level gained, however many: life and mana refilled once (L3), the
/// stats derived again, the hero told (<see cref="LevelUpToken"/> with the
/// level), the log told, and a checkpoint asked for
/// (<see cref="IHeroLevelCheckpoint"/>), which carries that evidence.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroExperience(Heroes heroes, HeroSheets sheets, IEngineEntities entities, IEnginePlayers players, Party.IPartyRoster roster,
                                   ClientMessages messages, IHeroLevelCheckpoint checkpoints, ILog log)
    : IRobotKillHook, IOrderedHook
{
    /// <summary>The hero's level-up line; its <c>%s1</c> is the new level.</summary>
    public const string LevelUpToken = "#Descent_Hero_LevelUp";

    /// <summary>In TF2 Descent's hero block of the kill chain.</summary>
    public const int Order = ModStages.First + 300;

    public int HookOrder => Order;

    public void OnRobotKilled(in RobotKill kill) => Pay(kill);

    /// <summary>Pays every hero in range of <paramref name="kill"/>; answers who was paid, by player index.</summary>
    public IReadOnlyList<HeroGain> Pay(in RobotKill kill)
    {
        var rules = sheets.Rules;
        double range = rules.Content.Experience.PartyRangeUnits;
        var paid = new List<HeroGain>();
        bool byHero = kill.ByPlayer && heroes.Of(kill.Killer) is not null;
        foreach (var hero in heroes.All.OrderBy(h => h.PlayerIndex).ToList())
        {
            if (hero.Sheet is not { } sheet || hero.Player == EntityHandle.None || !players.IsAlive(hero.Player))
                continue;
            if (byHero && !roster.SameParty(kill.Killer, hero.PlayerIndex))
                continue;
            if (Distance(entities.GetOrigin(hero.Player), kill.Position) > range)
                continue;

            long xp = rules.Experience.KillXp(kill.Stats, sheet.Level);
            var kills = new Dictionary<string, int>(StringComparer.Ordinal) { [kill.Template] = 1 };
            string why = string.Create(CultureInfo.InvariantCulture, $"kill {kill.Sequence} ({kill.Template} level {kill.Level})");
            if (Grant(hero, xp, kills, countKill: kill.Template, why) is { } gain)
                paid.Add(gain);
        }

        return paid;
    }

    /// <summary>
    /// Gives <paramref name="hero"/> <paramref name="xp"/> experience earned by
    /// <paramref name="kills"/> (the evidence that explains it), the bestiary
    /// counting one kill of <paramref name="countKill"/> when one is named, and
    /// runs the level-up when a level is gained. Null when the sheet refused it.
    /// </summary>
    public HeroGain? Grant(HeroComponent hero, long xp, IReadOnlyDictionary<string, int> kills, string? countKill, string why)
    {
        var rules = sheets.Rules;
        LevelUpResult? gain = null;
        long before = hero.Sheet?.Xp ?? 0;
        var audit = hero.Apply(sheet =>
        {
            gain = rules.Heroes.GainXp(sheet, xp);
            return countKill is null ? gain.Sheet : BestiaryRules.RecordKill(gain.Sheet, countKill);
        });
        if (!audit.Ok || gain is null)
        {
            log.Warning($"[TF2Descent/Hero] player {hero.PlayerIndex} was not paid for {why}: {audit.Rule}\n");
            return null;
        }

        long grew = gain.Sheet.Xp - before;
        sheets.RecordEarned(hero.PlayerIndex, grew, kills);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Hero] player {hero.PlayerIndex} +{grew} xp for {why}: xp {gain.Sheet.Xp}, level {gain.Sheet.Level}\n"));

        if (gain.LevelsGained > 0)
            LevelUp(hero, gain);
        return new HeroGain(hero.PlayerIndex, grew, gain);
    }

    private void LevelUp(HeroComponent hero, LevelUpResult gain)
    {
        hero.Recompute();
        if (gain.RefillsLifeAndMana)
            hero.RefillLifeAndMana();

        int level = gain.Sheet.Level;
        messages.Notice(hero.PlayerIndex, LevelUpToken, level.ToString(CultureInfo.InvariantCulture));
        bool sent = checkpoints.Checkpoint(hero.PlayerIndex);
        log.Msg($"[TF2Descent/Hero] player {hero.PlayerIndex} reached level {level} (+{gain.LevelsGained}): " +
                $"+{gain.AttributePointsGranted} attribute points, +{gain.SkillPointsGranted} skill points, " +
                $"{(gain.RefillsLifeAndMana ? "life and mana refilled" : "no refill")}, " +
                $"{(sent ? "checkpoint sent" : "no checkpoint (no host)")}\n");
    }

    private static double Distance(Vector a, Vector b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
