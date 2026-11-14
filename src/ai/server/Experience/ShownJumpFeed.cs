using System.Globalization;
using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Server.Experience;

/// <summary>
/// The jumps players make, into the experience (plan_bots_teams_tf2_part_g G.6, lane G6a): every command of a watched
/// player, with its body and the game's word on blasts, goes to a <see cref="JumpWatch"/>, as do its shots, its hurts and
/// its death; a jump the watch hands over goes to <see cref="ExperienceService.NoteShownJump"/>, which judges it off the
/// game's thread against the level's graph and links and writes the learned ones.
///
/// <para><b>Who is watched.</b> A person: never a bot, whose tries of the level's links are rows of their own (lane
/// G5e). The one exception is the stand-in (<see cref="AiExperienceJumpStandInConVar"/>, a cheat, off by default), a
/// named bot (or every bot) taken as a person, for a proof or a measurement with nobody at the keyboard.</para>
///
/// <para><b>What it reads.</b> Through the game's <see cref="IExperienceJumps"/>: with none (HL2MP) nothing is watched.</para>
///
/// <para><b>What it costs.</b> With the store off, or not gathering (learning off, no key, a round not being played), one
/// check a command and nothing read. A bot's command: a slot's cached answer. A watched player's command: the body's
/// fields off its managed state, the game's blast state, a copy of the command, and the watch's arithmetic, timed in
/// thread CPU (<see cref="WatchedMicros"/>). The graph search a landing needs runs on the experience worker.</para>
/// </summary>
[SideService(Side.Server)]
public sealed class ShownJumpFeed(ExperienceService experience, IBotPlayers botPlayers, IShownJumpStandIns names,
                                  AiExperienceJumpStandInConVar standIn, AiExperienceJumpStandInLinksConVar standInLinks, IExperienceJumps? jumps = null)
    : IPlayerCommandObserver, IPlayerWeaponEventHook, IPlayerHurtHook, IPlayerDeathHook, IOrderedHook, IDebugRouteProvider
{
    /// <summary>in_buttons.h's bits the trace keeps.</summary>
    public const int InAttack = 1 << 0, InJump = 1 << 1, InDuck = 1 << 2, InAttack2 = 1 << 11;

    private enum Who : byte
    {
        Unknown,
        Person,
        StandIn,
        Unwatched,
    }

    private readonly JumpWatch _watch = new(ServerAiSession.TickSeconds);
    private readonly EntityHandle[] _handles = new EntityHandle[VisionPass.Slots];
    private readonly Who[] _who = new Who[VisionPass.Slots];
    private readonly List<ShownJumpRecipe> _landed = new();
    private readonly Func<double>? _clock = ThreadCpuClock.Micros();
    private string? _standInRead;
    private bool _watching;

    /// <summary>After the experience feed (First + 910) and before the jumps (First + 920), in every chain it is in.</summary>
    public int HookOrder => ModStages.First + 915;

    /// <summary>The watch, for the counters and the facts.</summary>
    public JumpWatch Watch => _watch;

    /// <summary>Commands of watched players read since the server started (<c>ai.experience.jumps.commands</c>).</summary>
    public long WatchedCommands { get; private set; }

    /// <summary>The thread CPU those commands took here, microseconds (<c>ai.experience.jumps.us</c>).</summary>
    public double WatchedMicros { get; private set; }

    /// <summary>Jumps handed to the experience since the server started (<c>ai.experience.jumps.handed</c>).</summary>
    public long Handed { get; private set; }

    public void OnCommand(EntityHandle player, in UserCmd cmd)
    {
        if (jumps is null || !experience.Gathering)
        {
            Stop();
            return;
        }

        _watching = true;
        int slot = player.EntryIndex;
        if (slot is < 1 or >= VisionPass.Slots)
            return;
        Who who = WhoIs(slot, player);
        if (who is Who.Unwatched)
            return;

        double start = _clock?.Invoke() ?? 0d;
        if (jumps.Read(player, askGround: _watch.Flying(slot), out ShownJumpBody body))
        {
            _watch.Command(slot, in body, Compact(in cmd), jumps.BlastOf(player), _landed);
            if (_landed.Count > 0)
                Hand(player, who);
        }

        WatchedCommands++;
        if (_clock is not null)
            WatchedMicros += _clock() - start;
    }

    public void OnWeaponEvent(EntityHandle player, int weaponType, PlayerWeaponEvent e, int count)
    {
        if (_watching && e is PlayerWeaponEvent.Fired or PlayerWeaponEvent.AltFired && Watched(player.EntryIndex, player))
            _watch.Fired(player.EntryIndex);
    }

    public void OnPlayerHurt(in PlayerHurt hurt)
    {
        if (!_watching || !Watched(hurt.Player.EntryIndex, hurt.Player))
            return;
        bool self = hurt.Attacker == hurt.Player;
        bool byOther = !self && hurt.Attacker != EntityHandle.None && hurt.Attacker.EntryIndex is >= 1 and < VisionPass.Slots
                       && _handles[hurt.Attacker.EntryIndex] == hurt.Attacker;
        _watch.Hurt(hurt.Player.EntryIndex, hurt.Taken, byOther, ownBlast: self && (hurt.Kind & DamageType.Blast) != 0);
    }

    public void OnPlayerDied(PlayerDeath death)
    {
        if (_watching && Watched(death.Player.EntryIndex, death.Player))
            _watch.Died(death.Player.EntryIndex);
    }

    /// <summary>The landed jumps to the experience, with whether a stand-in's is judged against the level's links.</summary>
    private void Hand(EntityHandle player, Who who)
    {
        bool withLinks = who is Who.Person || standInLinks.Value;
        foreach (ShownJumpRecipe jump in _landed)
        {
            if (experience.NoteShownJump(player, jump, withLinks))
                Handed++;
        }

        _landed.Clear();
    }

    /// <summary>Nothing is gathered now: every flight is dropped, and the slots are asked again when it starts.</summary>
    private void Stop()
    {
        if (!_watching)
            return;
        _watching = false;
        _watch.Clear();
        Array.Clear(_handles);
        Array.Clear(_who);
    }

    private bool Watched(int slot, EntityHandle player)
        => slot is >= 1 and < VisionPass.Slots && _handles[slot] == player && _who[slot] is Who.Person or Who.StandIn;

    /// <summary>Who plays <paramref name="slot"/>: asked again when another player takes it or the stand-in is renamed.</summary>
    private Who WhoIs(int slot, EntityHandle player)
    {
        string named = standIn.Value;
        if (!ReferenceEquals(named, _standInRead))
        {
            _standInRead = named;
            Array.Clear(_who);
        }

        if (_handles[slot] != player)
        {
            _watch.Leave(slot);
            _handles[slot] = player;
            _who[slot] = Who.Unknown;
        }

        if (_who[slot] is Who.Unknown)
            _who[slot] = !botPlayers.IsBot(player) ? Who.Person : IsStandIn(slot, named) ? Who.StandIn : Who.Unwatched;
        return _who[slot];
    }

    private bool IsStandIn(int slot, string named)
    {
        string name = named.Trim();
        return name.Length > 0 && (name == "*" || names.Named(slot, name));
    }

    /// <summary>A command as the trace keeps it.</summary>
    public static ShownJumpCommand Compact(in UserCmd cmd)
    {
        int b = cmd.Buttons;
        byte buttons = (byte)(((b & InJump) != 0 ? ShownJumpCommand.Jump : 0) | ((b & InDuck) != 0 ? ShownJumpCommand.Duck : 0)
                              | ((b & InAttack) != 0 ? ShownJumpCommand.Attack : 0) | ((b & InAttack2) != 0 ? ShownJumpCommand.Attack2 : 0)
                              | (cmd.ForwardMove > 0f ? ShownJumpCommand.MoveForward : 0) | (cmd.ForwardMove < 0f ? ShownJumpCommand.MoveBack : 0)
                              | (cmd.SideMove > 0f ? ShownJumpCommand.MoveRight : 0) | (cmd.SideMove < 0f ? ShownJumpCommand.MoveLeft : 0));
        return new ShownJumpCommand(buttons, cmd.Pitch, cmd.Yaw, cmd.ForwardMove, cmd.SideMove);
    }

    /// <summary>The route that says what the watch saw.</summary>
    public const string Route = "/ai/experience/jumps";

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", Route, "the jumps players showed (lane G6a): the watch's commands read and their thread CPU, flights landed and thrown by why, jumps handed, judged by verdict and written; the feed's own thread CPU; the last jumps judged; and the snapshot's learned jumps with their counts and exemplars"),
    ];

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query) => new(200, Json());

    /// <summary>The route's answer.</summary>
    public string Json()
    {
        IReadOnlyList<ExperienceShownJump> learned = experience.Slot.Current.For(ExperienceContext.All).ShownJumps();
        return AiJson.Write(new
        {
            gathering = experience.Gathering,
            standin = standIn.Value,
            standin_links = standInLinks.Value,
            commands = WatchedCommands,
            us = Math.Round(WatchedMicros, 1),
            feed_us = Math.Round(experience.FeedMicros, 1),
            landed = _watch.Landed,
            thrown = Enum.GetValues<JumpThrow>().ToDictionary(t => t.ToString().ToLowerInvariant(), t => _watch.Thrown(t)),
            handed = Handed,
            noted = experience.ShownJumpsNoted,
            unjudged = experience.ShownJumpsUnjudged,
            verdicts = Enum.GetValues<ShownJumpVerdict>().ToDictionary(v => v.ToString().ToLowerInvariant(), v => experience.ShownJumpVerdicts(v)),
            recent = experience.RecentShownJumps().Select(j => new
            {
                kind = j.Kind.ToString().ToLowerInvariant(), cls = j.Class, from = Bin(j.From), to = Bin(j.To),
                seconds = Math.Round(j.Seconds, 3), health = j.Health, verdict = j.Verdict.ToString().ToLowerInvariant(),
            }).ToArray(),
            snapshot = learned.Select(j => new
            {
                kind = j.Kind.ToString().ToLowerInvariant(), cls = j.Key.Class, from = Bin(j.Key.From), to = Bin(j.Key.To),
                tries = j.Stat.Tries, successes = j.Stat.Successes, mean_seconds = Math.Round(j.Stat.MeanSeconds, 3), mean_health = Math.Round(j.Stat.MeanHealthCost, 1),
                exemplar = j.Exemplar is { } e ? new
                {
                    takeoff = Point(e.Takeoff), landing = Point(e.Landing), air = e.AirCommands, health = e.HealthCost, flags = e.Flags.ToString(),
                    takeoff_index = e.TakeoffIndex, jump_index = e.JumpIndex, shot_index = e.ShotIndex, blast_index = e.BlastIndex, shot_delay = e.ShotDelay,
                    shot_view = e.ShotCommand is { } c ? new[] { c.Pitch, c.Yaw } : null, trace = e.Trace.Length,
                } : null,
            }).ToArray(),
        });
    }

    private static int[] Bin(WorldBin b) => [b.X, b.Y, b.Z];

    private static float[] Point(AiVector v) => [MathF.Round(v.X, 1), MathF.Round(v.Y, 1), MathF.Round(v.Z, 1)];
}

/// <summary>Whether the bot in a slot is called a name (lane G6a: the stand-in, <see cref="AiExperienceJumpStandInConVar"/>).</summary>
public interface IShownJumpStandIns
{
    /// <summary>Whether the bot in <paramref name="slot"/> is called <paramref name="name"/> (any case).</summary>
    bool Named(int slot, string name);
}

/// <summary><see cref="IShownJumpStandIns"/> over the AI's bots.</summary>
[SideService(Side.Server, As = [typeof(IShownJumpStandIns)])]
public sealed class AiBotStandIns(AiBots bots) : IShownJumpStandIns
{
    public bool Named(int slot, string name)
    {
        foreach (AiBot b in bots.Bots)
        {
            if (b.Id.Slot == slot)
                return string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
