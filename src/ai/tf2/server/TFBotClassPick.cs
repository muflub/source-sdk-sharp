using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server;
using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;

namespace SourceSharp.Ai.TF2;

/// <summary><c>ai_bot_class_cooldown</c> (plan_bots_teams_tf2 C.4): after any bot changes class, no bot may for this long; one clock for both teams.</summary>
[ConVar("ai_bot_class_cooldown", "30", Help = "seconds after any bot changes class before any bot may again (one server-wide clock, both teams)")]
public sealed class AiBotClassCooldownConVar : FloatConVar { }

/// <summary><c>ai_bot_class_stay</c> (plan_bots_teams_tf2 C.4): a bot that changed class does not change again for this long.</summary>
[ConVar("ai_bot_class_stay", "120", Help = "seconds a bot that changed class keeps it before it may change again")]
public sealed class AiBotClassStayConVar : FloatConVar { }

/// <summary>
/// <c>ai_bot_classes_ready</c> (P7): classes the composition fields beside those whose file says they are ready, a comma
/// list (<c>scout,medic</c>), for a class lane's A/B runs before its class is ready; empty by default.
/// </summary>
[ConVar("ai_bot_classes_ready", "", Help = "classes the bots' composition fields beside the ready ones (comma list, e.g. scout,medic); for a class lane's runs", Flags = ConVarFlags.Cheat)]
public sealed class AiBotClassesReadyConVar : StringConVar { }

/// <summary>
/// The shared clock of class changes (plan_bots_teams_tf2 C.4): one server-wide window (<c>ai_bot_class_cooldown</c>)
/// and each bot's own stay (<c>ai_bot_class_stay</c>); of the bots that want a change at once the biggest gain wins,
/// ties to the lowest slot. A change in setup time neither waits for the window nor starts it.
/// </summary>
public sealed class TFClassChanges
{
    private float _lastAt = float.NegativeInfinity;
    private readonly Dictionary<int, float> _botAt = new();

    /// <summary>Changes made, for the facts and the routes.</summary>
    public int Changes { get; private set; }

    /// <summary>Changes a bot wanted and the window or its stay did not let it make.</summary>
    public int Held { get; private set; }

    /// <summary>When the last change that started the window was made.</summary>
    public float LastAt => _lastAt;

    public bool Open(float now, float cooldown, bool setup) => setup || now - _lastAt >= cooldown;

    public bool Stayed(int slot, float now, float stay, bool setup) => setup || !_botAt.TryGetValue(slot, out float at) || now - at >= stay;

    /// <summary>Of <paramref name="wants"/> (slot, class, gain), the one that gets the window: the biggest gain, ties to the lowest slot; null for none.</summary>
    public static (int Slot, TFClass To, float Gain)? Winner(IReadOnlyList<(int Slot, TFClass To, float Gain)> wants)
    {
        (int Slot, TFClass To, float Gain)? best = null;
        foreach (var w in wants)
        {
            if (w.Gain <= 0f)
                continue;
            if (best is not { } b || w.Gain > b.Gain || (w.Gain == b.Gain && w.Slot < b.Slot))
                best = w;
        }

        return best;
    }

    /// <summary>The bot in <paramref name="slot"/> changed class at <paramref name="now"/>.</summary>
    public void Changed(int slot, float now, bool setup)
    {
        Changes++;
        if (setup)
            return;
        _lastAt = now;
        _botAt[slot] = now;
    }

    public void Refused() => Held++;

    /// <summary>A new level: the game clock starts again, so every stamp of the last level's is forgotten (the window open, every stay over).</summary>
    public void Reset()
    {
        _lastAt = float.NegativeInfinity;
        _botAt.Clear();
    }
}

/// <summary>
/// One bot as the class pick reads it each frame (<see cref="TFBotClassPick.Step"/>): its slot, player, team, asked-for class,
/// life and pin, and whether it holds its class (<see cref="TFBotClassPick.HoldsClass"/>: an Engineer with a sentry or a
/// teleporter exit standing).
/// </summary>
public readonly record struct TFClassSeat(int Slot, EntityHandle Player, int Team, TFClass Desired, bool Alive, TFBotPin? Pin, bool HoldsClass = false);

/// <summary>
/// A TF2 bot picks its class as a player does at the class menu (plan_bots_teams_tf2 C.4, P6 step 7, P7 step 4): on a
/// playing team with no class asked for yet, it sends the mod's own <c>joinclass</c> word as that player's
/// (<see cref="IBotClientCommand"/>), and the mod's rules decide (class limits, the instant spawn of a first pick).
/// <list type="bullet">
/// <item>The first pick is the pinned class (<c>ai_bot_add class=</c>, <see cref="TFBotPins"/>), else the ready class its
/// team needs most (<see cref="TFClassComposition"/>, the team's classes as its scoreboard shows them, people counted).
/// It does not start the clock.</item>
/// <item>A dead bot weighs a change once per death, while it waits to respawn; the best gain over its stay bonus gets the
/// shared window (<see cref="TFClassChanges"/>), and <c>joinclass</c> takes effect at the respawn, by the mod's rules. A
/// pinned bot never changes.</item>
/// <item>A class whose file is not ready (<see cref="TFClassSpecs.Ready"/>) is never fielded unless
/// <c>ai_bot_classes_ready</c> lists it.</item>
/// </list>
/// A pick the mod refused is asked again after <see cref="RetrySeconds"/>.
/// </summary>
[SideService(Side.Server)]
public sealed class TFBotClassPick(AiBots bots, IEnginePlayers players, TFPlayerCarriers carriers, IBotClientCommand commands, IGameClock clock,
                                   TFBotPins pins, AiBotClassCooldownConVar cooldown, AiBotClassStayConVar stay, AiBotClassesReadyConVar extraReady,
                                   ITFRoundFacts round, IEngineEnvironment environment, global::TF2.Server.Buildings.ITFBuildings buildings)
    : IFrameLifecycle, IGameLifecycle
{
    /// <summary>A refused pick is asked again after this long.</summary>
    public const float RetrySeconds = 3f;

    /// <summary>After the TF2 AI's level service (First + 650).</summary>
    public int FrameOrder => ModStages.First + 655;

    /// <summary>Beside the frame order: the level hooks of the TF2 AI's band.</summary>
    public int LifecycleOrder => ModStages.First + 655;

    /// <summary>
    /// The level ends: the game clock restarts with the next, so the class clock, each bot's stay, the death latches and
    /// the asks' stamps are forgotten (review-p7f F2: a change at 1500 s on one map shut the window on the next until 1530).
    /// </summary>
    public void OnLevelShutdownPreEntity()
    {
        Clock.Reset();
        _weighed.Clear();
        _askedAt.Clear();
    }

    private readonly List<TFClassSeat> _seats = new();

    private readonly Dictionary<int, float> _askedAt = new();
    private readonly Dictionary<int, bool> _weighed = new();
    private readonly int[] _red = new int[(int)TFClass.CountAll], _blue = new int[(int)TFClass.CountAll];
    private readonly List<(int Slot, TFClass To, float Gain)> _wants = new();
    private readonly Dictionary<int, EntityHandle> _wantsPlayer = new();

    /// <summary>The shared clock of changes.</summary>
    public TFClassChanges Clock { get; } = new();

    /// <summary>Picks sent (first picks and changes), for the facts and the log.</summary>
    public int Picks { get; private set; }

    /// <summary>The stance of a team in the mode: symmetric for CTF and 5CP; attack and defence are read when the mode says (P8).</summary>
    public static TFTeamStance StanceOf(int team) => TFTeamStance.Symmetric;

    /// <summary>Whether the composition may field <paramref name="playerClass"/>: its file says so, or <c>ai_bot_classes_ready</c> lists it.</summary>
    public bool Ready(TFClass playerClass) => ReadyBy(playerClass, extraReady.Value);

    /// <summary>Whether <paramref name="playerClass"/> is ready by its file or by <paramref name="extra"/>'s comma list.</summary>
    public static bool ReadyBy(TFClass playerClass, string? extra)
    {
        if (TFClassSpecs.Ready(playerClass))
            return true;
        foreach (string word in (extra ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TFBotPins.ClassNamed(word) == playerClass)
                return true;
        }

        return false;
    }

    public void OnPostThink(float frameTime)
    {
        _seats.Clear();
        foreach (AiBot bot in bots.Bots)
        {
            EntityHandle player = bots.Applier.PlayerOf(bot.Id);
            if (player == EntityHandle.None)
                continue;
            TFClass desired = carriers.Part<TFPlayerClass>(player.EntryIndex)?.Desired ?? TFClass.Undefined;
            _seats.Add(new TFClassSeat(bot.Id.Slot, player, players.GetTeam(player), desired, players.IsAlive(player), pins.Of(bot.Id),
                                       HoldsClass(buildings, player, desired)));
        }

        CountTeams();
        Step(clock.Now, round.InSetup, _seats, _red, _blue, cooldown.Value, stay.Value);
    }

    /// <summary>
    /// One frame of the pick over <paramref name="seats"/> (what <see cref="OnPostThink"/> reads off the bots and the
    /// engine), with each team's classes as the scoreboard shows them (<paramref name="red"/>, <paramref name="blue"/>, by
    /// class number, the bots counted, each changed as a pick is asked): a first pick for a bot with none, and for the
    /// dead the one change the window lets through this frame.
    /// </summary>
    public void Step(float now, bool setup, IReadOnlyList<TFClassSeat> seats, int[] red, int[] blue, float cooldownSeconds, float staySeconds)
    {
        _wants.Clear();
        _wantsPlayer.Clear();
        foreach (TFClassSeat seat in seats)
        {
            int slot = seat.Slot, team = seat.Team;
            if (team is not ((int)TFTeam.Red or (int)TFTeam.Blue))
                continue;
            TFClass desired = seat.Desired;
            TFBotPin? pin = seat.Pin;
            int[] counts = team == (int)TFTeam.Red ? red : blue;
            if (desired == TFClass.Undefined)
            {
                if (_askedAt.TryGetValue(slot, out float at) && now - at < RetrySeconds && now >= at)
                    continue;
                TFClass first = pin is { Class: not TFClass.Undefined } ? pin.Class : TFClassComposition.Pick(counts, StanceOf(team), Ready);
                _askedAt[slot] = now;
                Ask(seat.Player, first);
                counts[(int)first]++;
                continue;
            }

            if (pin is { Class: not TFClass.Undefined })
                continue;
            if (seat.Alive)
            {
                _weighed[slot] = false;
                continue;
            }

            if (_weighed.GetValueOrDefault(slot) || seat.HoldsClass)
                continue;
            _weighed[slot] = true;
            counts[(int)desired]--;
            (TFClass to, float gain) = TFClassComposition.Change(desired, counts, StanceOf(team), Ready);
            counts[(int)desired]++;
            if (gain <= 0f)
                continue;
            if (!Clock.Open(now, cooldownSeconds, setup) || !Clock.Stayed(slot, now, staySeconds, setup))
            {
                Clock.Refused();
                continue;
            }

            _wants.Add((slot, to, gain));
            _wantsPlayer[slot] = seat.Player;
        }

        if (TFClassChanges.Winner(_wants) is not { } won)
            return;

        foreach (var w in _wants)
        {
            if (w.Slot != won.Slot)
                Clock.Refused();
        }

        Ask(_wantsPlayer[won.Slot], won.To);
        Clock.Changed(won.Slot, now, setup);
    }

    private void Ask(EntityHandle player, TFClass cls)
    {
        Picks++;
        commands.Run(player, Word(cls));
    }

    /// <summary>The team's classes as the scoreboard shows them (a person's too), each player's asked-for class first.</summary>
    private void CountTeams()
    {
        Array.Clear(_red);
        Array.Clear(_blue);
        int max = environment.Globals.IsAvailable ? environment.Globals.MaxClients : 0;
        for (int i = 1; i <= max; i++)
        {
            EntityHandle p = players.GetHandle(i);
            if (p == EntityHandle.None)
                continue;
            int team = players.GetTeam(p);
            if (team is not ((int)TFTeam.Red or (int)TFTeam.Blue))
                continue;
            TFClass c = carriers.Part<TFPlayerClass>(i)?.Desired ?? TFClass.Undefined;
            if (c == TFClass.Undefined)
                c = carriers.ClassOf(p);
            if (c is > TFClass.Undefined and < TFClass.CountAll)
                (team == (int)TFTeam.Red ? _red : _blue)[(int)c]++;
        }
    }

    /// <summary>
    /// Stock's CanChangeClass (tf_bot.cpp:1156-1163), the Engineer's arm: an Engineer with a sentry or a teleporter exit of
    /// his standing does not change class (the respawn would take them down). Read off the game's buildings by builder.
    /// </summary>
    public static bool HoldsClass(global::TF2.Server.Buildings.ITFBuildings buildings, EntityHandle player, TFClass desired)
        => desired == TFClass.Engineer
           && (buildings.OwnedBy(player, TFObjectType.Sentrygun, null).Any() || buildings.OwnedBy(player, TFObjectType.Teleporter, 1).Any());

    /// <summary>The word a bot that wants <paramref name="playerClass"/> sends: <c>joinclass soldier</c>.</summary>
    public static string Word(TFClass playerClass) => $"{TFJoinClassCommand.Verb} {TFClassNames.Raw(playerClass)}";
}
