using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.World;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Level;
using TF2.Descent.Server.Mode;

namespace TF2.Descent.Server.Lamp;

/// <summary>What the lamp asks of the world: whether a player lives, where he looks, and how deep the level is.</summary>
public interface ILampWorld
{
    /// <summary>Whether <paramref name="player"/> is alive.</summary>
    bool IsAlive(EntityHandle player);

    /// <summary>The player's look's pitch in degrees (negative is up).</summary>
    double Pitch(EntityHandle player);

    /// <summary>The level's depth: 0 for the town or any map that is no Descent level.</summary>
    int Depth { get; }
}

/// <summary>The engine's players and eye angles, and the level the server read.</summary>
[SideService(Side.Server, As = [typeof(ILampWorld)])]
public sealed class EngineLampWorld(IEnginePlayers players, IEngineEntities entities, ILevelData level) : ILampWorld
{
    public bool IsAlive(EntityHandle player) => players.IsAlive(player);

    public double Pitch(EntityHandle player) => entities.GetEyeAngles(player).X;

    public int Depth => level.Current?.Depth ?? 0;
}

/// <summary>What a lamp switch came to.</summary>
public enum LampAnswer
{
    /// <summary>The lamp is lit.</summary>
    On,

    /// <summary>The lamp is out.</summary>
    Off,

    /// <summary>Refused: the player is no hero.</summary>
    NoHero,

    /// <summary>Refused: the hero is dead.</summary>
    Dead,

    /// <summary>Refused: the hero is down (WO6): he can do nothing but wait.</summary>
    Downed,
}

/// <summary>
/// THE MINER'S LAMP on the server (plan_descent_logic HE10; mine plan M9, D7, E5): one switch a
/// hero, its state on the hero's own networked entity (<see cref="Shared.HeroNetState.LampOn"/>),
/// which every client holds, so every client lights every lamp it can see and the lamp costs no
/// edict. A hero arriving at a level finds it lit from lamp.json's depth on (every level, not the
/// town); the switch is his to flip (the lamp key's <c>tf2_descent_lamp_toggle</c>) while he is
/// alive and up. The wire shows the lamp lit only while the switch is on and the hero lives, and
/// carries his look (<see cref="Shared.HeroNetState.LampPitch"/>) while it is lit. A level's end
/// forgets every switch: the next level's depth decides again.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroLamps(IDescentModeState mode, Heroes heroes, TF2DescentRules rules, ILampWorld world, ILog log)
    : IGameLifecycle, IFrameLifecycle
{
    /// <summary>TF2 Descent's lamp and hazard block.</summary>
    public const int Order = ModStages.First + 1500;

    public int LifecycleOrder => Order;

    public int FrameOrder => Order;

    private readonly Dictionary<int, bool> _switch = new();

    /// <summary>Switches flipped since the module started (the key and the dev command), and refused.</summary>
    public int Flips { get; private set; }

    public int Refusals { get; private set; }

    private LampSettings Lamp => rules.Content.Lamp;

    /// <summary>Whether the hero in <paramref name="playerIndex"/> has his switch on: his own choice this level, else the depth's.</summary>
    public bool SwitchOf(int playerIndex) =>
        _switch.TryGetValue(playerIndex, out bool on) ? on : LampRules.OnByDefault(Lamp, world.Depth);

    /// <summary>Whether the hero's lamp is lit: his switch is on and he lives.</summary>
    public bool Lit(HeroComponent hero) => SwitchOf(hero.PlayerIndex) && world.IsAlive(hero.Player);

    /// <summary>
    /// Flips (<paramref name="on"/> null) or sets the switch of the hero in
    /// <paramref name="playerIndex"/>, for <paramref name="by"/> (the log's word): refused for a
    /// player who is no hero, a dead hero, or a downed one. A change goes on the wire at once.
    /// </summary>
    public LampAnswer Set(int playerIndex, bool? on, string by)
    {
        LampAnswer answer = heroes.Of(playerIndex) is not { } hero ? LampAnswer.NoHero
            : !world.IsAlive(hero.Player) ? LampAnswer.Dead
            : hero.Downed ? LampAnswer.Downed
            : (on ?? !SwitchOf(playerIndex)) ? LampAnswer.On : LampAnswer.Off;

        if (answer is LampAnswer.On or LampAnswer.Off)
        {
            _switch[playerIndex] = answer == LampAnswer.On;
            Flips++;
            Publish(heroes.Of(playerIndex)!);
        }
        else
        {
            Refusals++;
        }

        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Lamp] player {playerIndex} {by}: {(answer is LampAnswer.On or LampAnswer.Off ? "lamp " : "refused, ")}{answer.ToString().ToLowerInvariant()}\n"));
        return answer;
    }

    public void OnLevelInit(string mapName) => _switch.Clear();

    public void OnLevelShutdown() => _switch.Clear();

    public void OnThink(float frameTime)
    {
        if (!mode.Active)
            return;
        foreach (var hero in heroes.All)
            Publish(hero);
    }

    /// <summary>
    /// The hero's lamp onto his networked state: lit or not, his look while lit, and its range and
    /// brightness from his stats (lamp.json's lamp before his stats are known), which every client
    /// draws his lamp with (L11: what he has equipped is seen by all).
    /// </summary>
    public void Publish(HeroComponent hero)
    {
        if (hero.State is not { } state)
            return;
        bool lit = Lit(hero);
        state.LampOn = lit;
        if (lit)
            state.LampPitch = LampRules.PitchSteps(Lamp, world.Pitch(hero.Player));
        state.LampRadius = LampRules.WireRadius(RadiusOf(hero));
        state.LampBrightness = LampRules.WireBrightness(BrightnessOf(hero));
    }

    /// <summary>How far the hero's lamp reaches: his stats' (<see cref="Rules.Hero.HeroStats.LampRadius"/>), else lamp.json's.</summary>
    public double RadiusOf(HeroComponent hero) => hero.Stats?.LampRadius ?? Lamp.Light.Radius;

    /// <summary>How bright the hero's lamp is, as a multiplier on lamp.json's light: his stats', else 1.</summary>
    public double BrightnessOf(HeroComponent hero) => hero.Stats?.LampBrightness ?? 1;

    /// <summary>
    /// The hero's lamp stats as one phrase (the dev prints'): his radius and brightness, each with
    /// its bonus, what the bonus comes from (each counting contribution by its source) and the
    /// ceiling that holds it.
    /// </summary>
    public static string Describe(HeroStats stats, LampSettings lamp) =>
        string.Create(CultureInfo.InvariantCulture,
            $"lamp radius {stats.LampRadius:0} ({stats.LampRangeBonus:+0.##;-0.##;+0} %: {Sources(stats, lamp.Range.Stat)}; {lamp.Range.Min:0.##}..{lamp.Range.Max:+0.##}) " +
            $"brightness x{stats.LampBrightness:0.##} ({stats.LampBrightnessBonus:+0.##;-0.##;+0} %: {Sources(stats, lamp.Brightness.Stat)}; {lamp.Brightness.Min:0.##}..{lamp.Brightness.Max:+0.##})");

    private static string Sources(HeroStats stats, string stat)
    {
        var from = stats.From(stat).Select(c => string.Create(CultureInfo.InvariantCulture,
            $"{c.Source.Kind.ToString().ToLowerInvariant()} {c.Source.Id} {c.Amount:+0.##;-0.##;+0}")).ToList();
        return from.Count == 0 ? "nothing" : string.Join(", ", from);
    }

    /// <summary>Every hero's lamp, as lines: switch, lit, the wire's state and look.</summary>
    public string Report()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"[TF2Descent/Lamp] depth {world.Depth}: lit from depth {Lamp.OnFromDepth}, radius {Lamp.Light.Radius:0}, cone {Lamp.Light.InnerAngle:0}/{Lamp.Light.OuterAngle:0}; {heroes.All.Count} hero(es), {Flips} flip(s), {Refusals} refused\n");
        foreach (var hero in heroes.All.OrderBy(h => h.PlayerIndex))
        {
            var s = hero.State;
            sb.Append(CultureInfo.InvariantCulture,
                $"[TF2Descent/Lamp]   player {hero.PlayerIndex}: switch {(SwitchOf(hero.PlayerIndex) ? "on" : "off")}{(_switch.ContainsKey(hero.PlayerIndex) ? " (his)" : " (depth's)")}, " +
                $"{(world.IsAlive(hero.Player) ? "alive" : "dead")}{(hero.Downed ? ", downed" : "")}, wire {(s is null ? "none" : s.LampOn ? "lit" : "dark")}" +
                $"{(s is { LampOn: true } ? $" pitch {LampRules.PitchOf(Lamp, s.LampPitch):0.#}" : "")}" +
                $"{(s is null ? "" : $" radius {s.LampRadius} brightness {s.LampBrightness} %")}" +
                $"{(hero.Stats is { } st ? $"; {Describe(st, Lamp)}" : "")}\n");
        }

        return sb.ToString();
    }
}

/// <summary>
/// The lamp key's verb (<c>tf2_descent_lamp_toggle [on|off]</c>, the client's L key), claimed for a
/// hero only: it flips his switch, or sets it.
/// </summary>
[SideService(Side.Server)]
public sealed class LampClientCommands(HeroLamps lamps, Heroes heroes, IEngineEntities entities) : IClientCommandHook, IOrderedHook
{
    public const string Toggle = "tf2_descent_lamp_toggle";

    public const int Order = HeroLamps.Order + 1;

    public int HookOrder => Order;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (command != Toggle)
            return false;
        int index = entities.GetIndex(player);
        if (heroes.Of(index) is null)
            return false;
        bool? on = args.Length >= 1 ? args[0] switch { "on" or "1" => true, "off" or "0" => false, _ => null } : null;
        lamps.Set(index, on, "key");
        return true;
    }
}

/// <summary><c>tf2_descent_lamp &lt;player&gt; [on|off]</c>: flips or sets the hero's lamp as his key would (sv_cheats).</summary>
[ConCommand("tf2_descent_lamp", Help = "tf2_descent_lamp <player slot> [on|off]: flip or set the hero's miner's lamp, as his key would (sv_cheats).")]
public sealed class LampCommand(SvCheatsConVar cheats, HeroLamps lamps, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, [Complete("on", "off")] string state = "")
    {
        if (!cheats.Value)
        {
            log.Warning("[TF2Descent/Lamp] tf2_descent_lamp runs only under sv_cheats\n");
            return;
        }
        bool? on = state switch { "on" => true, "off" => false, "" => null, _ => null };
        if (state.Length > 0 && on is null)
        {
            log.Warning($"[TF2Descent/Lamp] tf2_descent_lamp: '{state}' is not on or off\n");
            return;
        }
        lamps.Set(slot, on, "tf2_descent_lamp");
    }
}

/// <summary><c>tf2_descent_lamps</c>: every hero's lamp (sv_cheats).</summary>
[ConCommand("tf2_descent_lamps", Help = "tf2_descent_lamps: every hero's miner's lamp: switch, lit, the wire's look (sv_cheats).")]
public sealed class LampsCommand(SvCheatsConVar cheats, HeroLamps lamps, ILog log)
{
    public void Execute()
    {
        if (!cheats.Value)
        {
            log.Warning("[TF2Descent/Lamp] tf2_descent_lamps runs only under sv_cheats\n");
            return;
        }
        log.Msg(lamps.Report());
    }
}
