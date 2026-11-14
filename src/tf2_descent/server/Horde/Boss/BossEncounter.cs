using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Bosses;
using TF2.Descent.Server.Horde.Motion;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Horde.Boss;

/// <summary>
/// THE BOSS FIGHT AS THE SERVER KEEPS IT (plan_descent_logic HO13; D13): a member of the horde's
/// tickers, it follows the level's boss (the roster's boss row: the level's, or a dev boss) ten
/// times a second.
///
/// <list type="bullet">
/// <item><b>Intro.</b> The first time a living hero stands in its arena (the room holding its
/// post), each hero there is told its name (<c>#Descent_Boss_Intro</c>), its entrance sounds, and
/// its companion (the Tank, for Major Crits) is called into the arena (<see cref="IBossAdds"/>).</item>
/// <item><b>Phase.</b> The phase its life is in (<see cref="BossRules.PhaseSeen"/>, held once
/// entered): a change is logged with its life and tick, told to the heroes in the arena
/// (<c>#Descent_Boss_Phase</c>) with MvM's siren.</item>
/// <item><b>Reset</b> (the design is silent; HO13's choice): once every hero has been out of its
/// arena (gone, or dead) for bosses.json's <c>resetSeconds</c>, the boss is whole again at its
/// post, in its first phase, and the next hero in starts the fight again. Its brain sees itself
/// whole with nobody in sight and forgets the fight (<c>BossDecider</c>). Adds and the Tank stay.</item>
/// <item><b>The bar</b> (<see cref="BossBar"/>): to each hero in the arena, its name, life, most,
/// phase and phases, when they change; absent once to a hero who leaves, and when it dies.</item>
/// </list>
/// A new level starts it over (the controller is a new one).
/// </summary>
[SideService(Side.Server)]
public sealed class BossEncounter(IHordeRules rules, IHordeHeroes heroes, IBossWorld world, IBossAdds adds, RobotMotion motion, ILog log)
    : IHordeTicker, IOrderedHook
{
    /// <summary>In TF2 Descent's boss band (First + 1300 to 1399), after the champions' tickers.</summary>
    public const int Order = ModStages.First + 1310;

    public int HookOrder => Order;

    private const int MaxHeroes = 33;

    private readonly HeroPlace[] _places = new HeroPlace[MaxHeroes];
    private readonly HeroEye[] _eyes = new HeroEye[MaxHeroes];
    private readonly bool[] _shown = new bool[MaxHeroes + 1];
    private readonly bool[] _inArena = new bool[MaxHeroes + 1];
    private readonly int[] _sentLife = new int[MaxHeroes + 1], _sentPhase = new int[MaxHeroes + 1];
    private HordeController? _level;
    private int _rosterCount = -1;
    private long _next, _emptySince;
    private BossScript? _script;

    /// <summary>The boss's roster row on this level; -1 for none.</summary>
    public int Row { get; private set; } = -1;

    /// <summary>The room its fight is in: the room holding its post.</summary>
    public int Arena { get; private set; } = -1;

    public BossScript? Script => _script;

    /// <summary>The phase it is in (0-based), held once entered.</summary>
    public int Phase { get; private set; }

    /// <summary>Whether a hero has stood in its arena since the level began or its last reset.</summary>
    public bool Engaged { get; private set; }

    /// <summary>The tick of its last intro; 0 for none.</summary>
    public long IntroTick { get; private set; }

    /// <summary>Times it was reset whole.</summary>
    public int Resets { get; private set; }

    /// <summary>Heroes in its arena at the last look.</summary>
    public int HeroesInArena { get; private set; }

    /// <summary>The companion row its intro called (the Tank); -1 for none.</summary>
    public int Companion { get; private set; } = -1;

    /// <summary>Bars sent to heroes, and absent bars.</summary>
    public long BarsSent { get; private set; }

    public long BarsWithdrawn { get; private set; }

    /// <summary>The phase changes of this level: phase, the life it was entered at, the tick.</summary>
    public IReadOnlyList<(int Phase, double Life, long Tick)> Phases => _phases;

    private readonly List<(int Phase, double Life, long Tick)> _phases = [];

    public void Tick(HordeController horde, long now)
    {
        ArgumentNullException.ThrowIfNull(horde);
        if (!ReferenceEquals(horde, _level))
            Begin(horde);
        if (horde.Roster is not { } roster || horde.Level is not { } level)
            return;
        if (roster.Count != _rosterCount)
            Find(roster);
        if (Row < 0 || now < _next)
            return;
        _next = now + Math.Max(1, horde.DecideInterval);

        int n = heroes.Gather(level, _places, _eyes);
        int inArena = 0;
        Array.Clear(_inArena);
        for (int h = 0; h < n; h++)
        {
            if (_places[h].Room != Arena || (uint)_places[h].Seat > MaxHeroes)
                continue;
            _inArena[_places[h].Seat] = true;
            inArena++;
        }

        HeroesInArena = inArena;
        bool alive = roster.State(Row) is not (RobotState.Dead or RobotState.Empty);
        if (alive)
        {
            var script = _script!;
            double life = roster.Life(Row), most = roster.Stats(Row).Life;
            int phase = Math.Max(Engaged ? Phase : 0, BossRules.PhaseSeen(script, life, most));
            if (inArena > 0)
            {
                _emptySince = 0;
                if (!Engaged)
                    Intro(horde, roster, now, inArena);
            }
            else if (Engaged)
            {
                if (_emptySince == 0)
                    _emptySince = now;
                else if (now - _emptySince >= BossRules.Ticks(rules.Rules.Bosses.Settings.ResetSeconds, TicksPerSecond(horde)))
                {
                    Reset(horde, roster, now);
                    phase = 0;
                }
            }

            if (phase > Phase && Engaged)
                PhaseChanged(horde, roster, phase, life, now);
            else if (!Engaged)
                Phase = 0;
        }

        Bars(horde, roster, alive);
    }

    //-------------------------------------------------------------------------

    private void Begin(HordeController horde)
    {
        _level = horde;
        _rosterCount = -1;
        Row = -1;
        Arena = -1;
        _script = null;
        Phase = 0;
        Engaged = false;
        IntroTick = 0;
        Resets = 0;
        Companion = -1;
        _next = 0;
        _emptySince = 0;
        _phases.Clear();
        Array.Clear(_shown);
        Array.Clear(_sentLife);
        Array.Clear(_sentPhase);
    }

    /// <summary>The roster's living boss row (the newest, when a dev boss joined a level whose own is dead); its script and arena.</summary>
    private void Find(TF2.Descent.Ai.Roster.RobotRoster roster)
    {
        _rosterCount = roster.Count;
        int found = -1;
        for (int i = 0; i < roster.Count; i++)
            if (roster.IsBoss(i) && (found < 0 || roster.State(i) is not RobotState.Dead))
                found = i;
        if (found == Row || found < 0)
            return;
        if (rules.Rules.Bosses.Of(roster.Template(found)) is not { } script)
            return;
        Row = found;
        _script = script;
        Arena = roster.HomeRoom(found);
        Phase = 0;
        Engaged = false;
        _phases.Clear();
    }

    private void Intro(HordeController horde, TF2.Descent.Ai.Roster.RobotRoster roster, long now, int inArena)
    {
        Engaged = true;
        IntroTick = now;
        var t = rules.Rules.Robots.Template(roster.Template(Row));
        var sounds = rules.Rules.Bosses.Settings.Sounds;
        for (int p = 1; p <= MaxHeroes; p++)
        {
            if (!_inArena[p])
                continue;
            world.Notice(p, IntroToken, t.Name);
            world.Sound(world.Player(p), sounds.Intro);
        }

        if (_script!.Companion.Length > 0 && (Companion < 0 || roster.State(Companion) is RobotState.Dead or RobotState.Empty))
        {
            int before = roster.Count;
            int put = adds.Summon(horde, Row, _script.Companion, 1, roster.Seed(Row) ^ 0xC0FFEEUL ^ (ulong)Resets, now);
            Companion = put > 0 ? before : -1;
        }

        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Boss] intro: {t.Name} ({t.Id}) row {Row} in arena room {Arena}, life {roster.Life(Row):0} of {roster.Stats(Row).Life:0}, phase {Phase} of {_script.Phases.Count}, {inArena} hero(es) in the arena, tick {now}{(Companion >= 0 ? $", companion {roster.Template(Companion)} row {Companion}" : "")}\n"));
    }

    private void PhaseChanged(HordeController horde, TF2.Descent.Ai.Roster.RobotRoster roster, int phase, double life, long now)
    {
        Phase = phase;
        _phases.Add((phase, life, now));
        var t = rules.Rules.Robots.Template(roster.Template(Row));
        string sound = rules.Rules.Bosses.Settings.Sounds.Phase;
        for (int p = 1; p <= MaxHeroes; p++)
        {
            if (!_inArena[p])
                continue;
            world.Notice(p, PhaseToken, t.Name);
            world.Sound(world.Player(p), sound);
        }

        var at = _script!.Phases[phase];
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Boss] phase {phase} of {_script.Phases.Count}: {t.Id} at life {life:0.#} of {roster.Stats(Row).Life:0} ({100 * life / roster.Stats(Row).Life:0.#} %, threshold {at.At} %), tick {now}; special '{at.Special}', adds {at.Adds.Sum(a => a.Count)}\n"));
    }

    /// <summary>Whole again at its post, in its first phase (see the class remarks).</summary>
    private void Reset(HordeController horde, TF2.Descent.Ai.Roster.RobotRoster roster, long now)
    {
        double most = roster.Stats(Row).Life;
        roster.Heal(Row, most - roster.Life(Row));
        AiVector post = roster.Post(Row);
        float yaw = roster.PostYaw(Row);
        if (roster.Body(Row) is int b and > 0 && horde.Bodies is { } bodies)
        {
            var body = bodies.Body(b);
            body.Healed(most, most);
            body.MoveTo(post, yaw);
            motion.Restart(b);
        }
        else
        {
            roster.SetPose(Row, Arena, post, yaw);
        }

        Engaged = false;
        Phase = 0;
        Resets++;
        _emptySince = 0;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Boss] reset: {roster.Template(Row)} row {Row} is whole ({most:0}) at its post ({post.X:0},{post.Y:0},{post.Z:0}): no hero in its arena for {rules.Rules.Bosses.Settings.ResetSeconds} s, tick {now}\n"));
    }

    /// <summary>The bar to each hero in the arena when it changed; absent to one who left, and to all when it is dead.</summary>
    private void Bars(HordeController horde, TF2.Descent.Ai.Roster.RobotRoster roster, bool alive)
    {
        int life = alive ? (int)Math.Ceiling(roster.Life(Row)) : 0;
        for (int p = 1; p <= MaxHeroes; p++)
        {
            bool show = alive && _inArena[p];
            if (show)
            {
                if (_shown[p] && _sentLife[p] == life && _sentPhase[p] == Phase)
                    continue;
                _shown[p] = true;
                _sentLife[p] = life;
                _sentPhase[p] = Phase;
                world.Bar(p, Bar(roster, life));
                BarsSent++;
            }
            else if (_shown[p])
            {
                _shown[p] = false;
                world.Bar(p, BossBar.Of(BossBarView.Absent));
                BarsWithdrawn++;
            }
        }
    }

    private BossBar Bar(TF2.Descent.Ai.Roster.RobotRoster roster, int life)
    {
        var t = rules.Rules.Robots.Template(roster.Template(Row));
        return new BossBar
        {
            Present = 1,
            Template = TemplateNumber(t.Id),
            Name = t.Name.Length <= BossBar.MaxName ? t.Name : t.Name[..BossBar.MaxName],
            Life = life,
            LifeMax = (int)Math.Ceiling(roster.Stats(Row).Life),
            Phase = (byte)Phase,
            Phases = (byte)_script!.Phases.Count,
        };
    }

    /// <summary>A template's row in robots.json plus one, as the bodies' net state numbers it.</summary>
    private byte TemplateNumber(string id)
    {
        var rows = rules.Rules.Content.Robots.Rows;
        for (int k = 0; k < rows.Count; k++)
            if (rows[k].Id == id)
                return (byte)Math.Min(255, k + 1);
        return 0;
    }

    private static double TicksPerSecond(HordeController horde) => horde.Roster?.TicksPerSecond ?? 66;

    /// <summary>The notices a boss fight shows (tf2_descent_english.txt).</summary>
    public const string IntroToken = "#Descent_Boss_Intro", PhaseToken = "#Descent_Boss_Phase", ResetToken = "#Descent_Boss_Reset";

    /// <summary>The fight as lines for <c>tf2_descent_boss</c>.</summary>
    public IEnumerable<string> Lines(HordeController horde)
    {
        if (!ReferenceEquals(horde, _level) || Row < 0 || horde.Roster is not { } roster)
        {
            yield return "no boss on this level";
            yield break;
        }

        var t = rules.Rules.Robots.Template(roster.Template(Row));
        yield return string.Create(CultureInfo.InvariantCulture,
            $"boss {t.Name} ({t.Id}) row {Row}: {roster.State(Row)}, body {roster.Body(Row)}, life {roster.Life(Row):0.#} of {roster.Stats(Row).Life:0}, scale {roster.Scale(Row):0.##}, level {roster.MonsterLevel(Row)}, arena room {Arena}, post ({roster.Post(Row).X:0},{roster.Post(Row).Y:0},{roster.Post(Row).Z:0}), at ({roster.Position(Row).X:0},{roster.Position(Row).Y:0},{roster.Position(Row).Z:0})");
        yield return string.Create(CultureInfo.InvariantCulture,
            $"fight: phase {Phase} of {_script!.Phases.Count} (thresholds {string.Join(' ', _script.Phases.Select(p => p.At))}), engaged {Engaged} (intro tick {IntroTick}), resets {Resets}, heroes in the arena {HeroesInArena}, bars sent {BarsSent}, withdrawn {BarsWithdrawn}");
        foreach (var (phase, life, tick) in _phases)
            yield return string.Create(CultureInfo.InvariantCulture, $"  phase {phase} entered at life {life:0.#}, tick {tick}");
        if (Companion >= 0 && Companion < roster.Count)
            yield return string.Create(CultureInfo.InvariantCulture,
                $"companion {roster.Template(Companion)} row {Companion}: {roster.State(Companion)}, body {roster.Body(Companion)}, life {roster.Life(Companion):0} of {roster.Stats(Companion).Life:0}, at ({roster.Position(Companion).X:0},{roster.Position(Companion).Y:0},{roster.Position(Companion).Z:0})");
    }
}
