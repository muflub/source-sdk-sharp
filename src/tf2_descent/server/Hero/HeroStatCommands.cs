using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.World;
using TF2.Descent.Server.Lamp;
using TF2.Server.Player;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// <c>tf2_descent_contrib &lt;slot&gt; &lt;stat&gt; &lt;amount&gt; [active|inactive|broken]</c>:
/// a test contribution to the hero's Now (a stats.json row, an attribute
/// included), the stats derived again at once.
/// </summary>
[ConCommand("tf2_descent_contrib",
            Help = "tf2_descent_contrib <player slot> <stat> <amount> [active|inactive|broken]: add a test contribution to the hero's stats (sv_cheats).")]
public sealed class HeroContribCommand(SvCheatsConVar cheats, Heroes heroes, DevContributions dev, TF2DescentRules rules, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, string stat, float amount,
                        [Complete("active", "inactive", "broken")] string state = "active")
    {
        var gate = new HeroCommandGate(cheats, heroes, log);
        if (gate.Hero("tf2_descent_contrib", slot) is not { } hero)
            return;

        if (!rules.Content.Stats.Rows.Any(r => r.Id == stat))
        {
            log.Warning($"[TF2Descent/Hero] tf2_descent_contrib: '{stat}' is no stat (stats.json)\n");
            return;
        }

        if (!Enum.TryParse<SourceState>(state, ignoreCase: true, out var sourceState))
        {
            log.Warning($"[TF2Descent/Hero] tf2_descent_contrib: '{state}' is not active, inactive or broken\n");
            return;
        }

        var added = dev.Add(slot, stat, amount, sourceState);
        hero.Recompute();
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Hero] {slot} +{amount} {stat} ({added.Source.Id}, {sourceState.ToString().ToLowerInvariant()})\n"));
    }
}

/// <summary><c>tf2_descent_contrib_clear &lt;slot&gt;</c>: every test contribution of the hero gone, the stats derived again.</summary>
[ConCommand("tf2_descent_contrib_clear", Help = "tf2_descent_contrib_clear <player slot>: remove the hero's test contributions (sv_cheats).")]
public sealed class HeroContribClearCommand(SvCheatsConVar cheats, Heroes heroes, DevContributions dev, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot)
    {
        var gate = new HeroCommandGate(cheats, heroes, log);
        if (gate.Hero("tf2_descent_contrib_clear", slot) is not { } hero)
            return;

        int removed = dev.Clear(slot);
        hero.Recompute();
        log.Msg($"[TF2Descent/Hero] {slot} cleared {removed} test contribution(s)\n");
    }
}

/// <summary>
/// <c>tf2_descent_stats &lt;slot&gt;</c>: the hero's <see cref="HeroStats"/>
/// (pools, armour, crit, the damage multipliers by kind, the resistances,
/// sustain) and what is in force in TF2: the attributes the hero holds, TF's
/// maximum health and speed, and the health now.
/// </summary>
[ConCommand("tf2_descent_stats", Help = "tf2_descent_stats <player slot>: print the hero's stats and the TF attributes in force (sv_cheats).")]
public sealed class HeroStatsCommand(SvCheatsConVar cheats, Heroes heroes, HeroAttributes attributes, ITFPlayerResupply resupply,
                                     IMovementRules movement, IEngineEntities entities, IAttackerLevel levels, TF2DescentRules rules, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot)
    {
        var gate = new HeroCommandGate(cheats, heroes, log);
        if (gate.Hero("tf2_descent_stats", slot) is not { } hero)
            return;

        log.Msg(Describe(slot, hero, hero.Stats!, attributes.Of(slot),
                         resupply.MaxHealth(hero.Player), movement.MaxSpeedScale(slot, 0f, ducking: false, groundMaterial: 0),
                         entities.GetHealth(hero.Player), rules.Heroes.ArmourReduction(hero.Stats!.Armour, levels.LevelOf(EntityHandle.None)),
                         rules.Content.Lamp));
    }

    /// <summary>The console line: every number the rules derived, then TF's.</summary>
    public static string Describe(int slot, HeroComponent hero, HeroStats st, HeroTFAttributes? tf, int tfMaxHealth, float tfMaxSpeed,
                                  int health, double armourReduction, LampSettings lamp)
    {
        var c = CultureInfo.InvariantCulture;
        var line = new StringBuilder();
        line.Append(c, $"[TF2Descent/Stats] {slot} level {hero.Sheet!.Level}: life max {st.MaxLife:0.##} mana {hero.Mana:0.##}/{st.MaxMana:0.##} ");
        line.Append(c, $"speed {st.MoveSpeed:0.##} armour {st.Armour:0.##} (-{armourReduction:0.####} at level 1) crit {st.CritChance:0.####} | damage x");
        line.Append(c, $" melee {st.DamageMultiplier("melee"):0.####} blast {st.DamageMultiplier("blast"):0.####}");
        line.Append(c, $" bullet {st.DamageMultiplier("bullet"):0.####} projectile {st.DamageMultiplier("projectile"):0.####}");
        line.Append(c, $" rocket {st.DamageMultiplier("blast", "projectile"):0.####} action {st.DamageMultiplier("action"):0.####} | resist");
        line.Append(c, $" fire {st.Resist("fire"):0.####} blast {st.Resist("blast"):0.####} bullet {st.Resist("bullet"):0.####} shock {st.Resist("shock"):0.####}");
        line.Append(c, $" | regen {st.LifeRegenPerSecond:0.###}/s leech {st.LifeLeech:0.####} mana regen {st.ManaRegenPerSecond:0.###}/s mana leech {st.ManaLeech:0.####}");
        line.Append(c, $" | {HeroLamps.Describe(st, lamp)}");
        line.Append(c, $" | TF add_maxhealth {(tf is { } a ? a.AddMaxHealth.ToString("0.##", c) : "none")}");
        line.Append(c, $" movespeed x{(tf is { } b ? b.MoveSpeedFactor.ToString("0.####", c) : "none")}");
        line.Append(c, $" max health {tfMaxHealth} max speed {tfMaxSpeed:0.##} health {health}\n");
        return line.ToString();
    }
}

/// <summary>
/// <c>tf2_descent_dummy &lt;slot&gt; [health] [mortal]</c>: a target dummy
/// (<see cref="HeroDummy"/>) where the player in the slot is aiming, a
/// player's width short of the surface the aim meets.
/// </summary>
[ConCommand("tf2_descent_dummy", Help = "tf2_descent_dummy <player slot> [health] [mortal 0|1]: spawn a target dummy at the player's crosshair (sv_cheats).")]
public sealed class HeroDummyCommand(SvCheatsConVar cheats, IEnginePlayers players, IEngineEntities entities, IEngineWorldQuery world,
                                     IEntityDirectory directory, ILog log)
{
    /// <summary>How far the aim looks for the place.</summary>
    public const float Range = 4096f;

    /// <summary>How far short of the aimed surface the dummy stands.</summary>
    public const float Back = 48f;

    public void Execute([ArgRange(1, 64)] int slot, int health = HeroDummy.DefaultHealth, int mortal = 0)
    {
        if (!cheats.Value)
        {
            log.Warning("[TF2Descent/Dummy] tf2_descent_dummy runs only under sv_cheats\n");
            return;
        }

        var player = players.GetHandle(slot);
        if (player == EntityHandle.None || !entities.IsValid(player))
        {
            log.Warning($"[TF2Descent/Dummy] tf2_descent_dummy: no player in slot {slot}\n");
            return;
        }

        world.TracePlayerAim(player, Range, out Vector hit);
        Vector eye = players.GetShootOrigin(player);
        Vector flat = new(hit.X - eye.X, hit.Y - eye.Y, 0f);
        Vector at = flat.Length > Back ? hit - flat.Normalized * Back : hit;

        var dummy = entities.Create(HeroDummy.EntityClass);
        if (dummy == EntityHandle.None)
        {
            log.Warning($"[TF2Descent/Dummy] no {HeroDummy.EntityClass} could be made\n");
            return;
        }

        entities.SetOrigin(dummy, at);
        entities.Spawn(dummy);
        if (directory.Get(dummy) is HeroDummy made)
        {
            made.Mortal = mortal != 0;
            made.SetPool(health);
        }

        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Dummy] #{entities.GetIndex(dummy)} at ({at.X:0.#}, {at.Y:0.#}, {at.Z:0.#}), {Math.Max(1, health)} health{(mortal != 0 ? ", mortal" : "")}\n"));
    }
}
