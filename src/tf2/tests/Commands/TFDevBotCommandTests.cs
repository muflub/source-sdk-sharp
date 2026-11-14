using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Commands;
using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Tests.Entities;
using TF2.Tests.Player;

using Xunit;

namespace TF2.Tests.Commands;

/// <summary>
/// <see cref="TFDevBotCommand"/> (<c>tf2_dev_bot &lt;red|blue&gt; &lt;class&gt;
/// [distance]</c>) and <see cref="TFDevBotKickCommand"/>(<c>tf2_dev_bot_kick</c>):
/// the cheats gate, the placement along the caller's flattened view facing back,
/// the default distance, the human join path for the bot's team and class, and
/// the kick of every added bot. The class goes through the real
/// <see cref="TFJoinClassCommand"/>, as the console's <c>joinclass</c> does.
/// </summary>
public sealed class TFDevBotCommandTests
{
    /// <summary>One caller: who typed the word, found through the command client.</summary>
    private static readonly EntityHandle Caller = new(0x0001_0000u | 5);

    private sealed class Waves : ITeamRespawnWaves
    {
        public float NextRespawnWave(TFTeam team) => 105f;
        public float NextRespawnWave(TFTeam team, EntityHandle player) => 105f;
        public float RespawnWaveMaxLength(TFTeam team, bool scaleWithNumPlayers = true) => 10f;
        public void SetTeamRespawnWaveTime(TFTeam team, float seconds) { }
        public void AddTeamRespawnWaveTime(TFTeam team, float seconds) { }
    }

    private sealed class Rooms : TF2.Server.Entities.Rooms.ITFRespawnRooms
    {
        public bool InRespawnRoom(EntityHandle player, bool sameTeamOnly) => false;
        public bool PointInRespawnRoom(Vector point) => false;
        public bool PointInRespawnRoom(EntityHandle target, Vector origin, bool touchingSameTeamOnly) => false;
        public bool PointsCrossRespawnRoomVisualizer(Vector start, Vector end, int teamToIgnore = 0) => false;
        public int TouchCount(EntityHandle player) => 0;
    }

    private sealed class Limits : ITFClassLimitSettings
    {
        public bool Highlander { get; set; }
        public int ClassLimit { get; set; }
    }

    private sealed class StalemateSettings : ITFStalemateSettings
    {
        public bool MeleeOnly => true;
        public float ChangeClassTime => 20f;
    }

    /// <summary>Bots as a scripted seam: what Add is handed, what it answers, and the kick tally.</summary>
    private sealed class BotSeam : IBotPlayers
    {
        public EntityHandle Answer = PlayerRig.Player;
        public string Refusal = string.Empty;
        public List<(string Name, int Team)> Added { get; } = [];
        public List<(EntityHandle Bot, string Reason)> Kicked { get; } = [];
        public List<BotPlayer> Listed { get; } = [];

        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo,
                                out string refusal, int team = 0)
        {
            Added.Add((name, team));
            refusal = Refusal;
            return Answer;
        }

        public bool IsBot(EntityHandle player) => true;

        public bool Kick(EntityHandle bot, string reason)
        {
            Kicked.Add((bot, reason));
            return true;
        }

        public IReadOnlyList<BotPlayer> Bots => Listed;
    }

    /// <summary>The team joins the fake answers, beside the roster the class command counts against.</summary>
    private sealed class Teams : ITeams
    {
        public List<(EntityHandle Player, int Team)> Joined { get; } = [];

        /// <summary>What a join answers; the facts' default is the accepted join.</summary>
        public JoinTeamResult Result { get; set; } = JoinTeamResult.Playing;

        public int Team(EntityHandle player) => (int)TFTeam.Red;
        public IReadOnlyList<EntityHandle> Players(int team) => [];
        public int Count(int team) => 0;
        public JoinTeamResult Join(EntityHandle player, int requested)
        {
            Joined.Add((player, requested));
            return Result;
        }
        public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;
    }

    private sealed class Rig
    {
        public readonly PlayerRig Player = new();
        public readonly TFWeaponOwnersTests.Roster Inner = new() { Red = [PlayerRig.Player] };
        public readonly TFPlayerClass Class;
        public readonly BotSeam Bots = new();
        public readonly Teams Teams = new();
        public readonly TFDevBotCommand Command;

        public Rig(string cheats = "1")
        {
            var engine = Player.Engine;
            engine.Returns("Players.GetObserverMode", ObserverMode.None);
            engine.Returns("Players.GetCommandClient", Caller);
            Class = new TFPlayerClass(Player.Carriers, Player.Items);
            Player.Extra.Add(Class);
            Player.Join();

            // The real joinclass command, as the console's word runs it: the
            // carrier composite carries the player's class component, the
            // roster answers the class-limit counts, the round is running.
            var joinclass = new TFJoinClassCommand(Player.Carriers, engine, Inner, new Rooms(), Player.Classes,
                                                   new FakeRandom(), engine, engine, new FakeClock { Now = 100f },
                                                   Player.Log, new FakeRoundState(), new Waves(), new Limits(),
                                                   new StalemateSettings(),
                                                   new TFClassChangeAccess(new Rules.ListHooks<ITFClassChangeGate>(), Player.Log));
            Command = new TFDevBotCommand(Player.Log, DeclaredConVars.Holding<SourceSharp.Shared.SvCheatsConVar>(cheats),
                                          engine, engine, Bots, Teams, joinclass);
        }
    }

    /// <summary>The caller's view stubs: standing at <paramref name="origin"/>, looking along <paramref name="eye"/>.</summary>
    private static Rig Standing(Vector origin, Vector eye)
    {
        var rig = new Rig();
        rig.Player.Engine.Returns("Entities.GetOrigin", origin);
        rig.Player.Engine.Returns("Entities.GetEyeAngles", eye);
        return rig;
    }

    [Fact]
    public void Without_sv_cheats_the_add_is_refused_and_nothing_else_is_asked()
    {
        var rig = new Rig(cheats: "0");
        rig.Player.Engine.Clear();

        rig.Command.RunLine("red scout");

        Assert.Equal("[TF2/Bots] tf2_dev_bot runs only under sv_cheats\n",
                     rig.Player.Log.Lines.Single(l => l.Warning).Text);
        Assert.DoesNotContain(rig.Player.Log.Lines, l => !l.Warning);
        Assert.Empty(rig.Player.Engine.Calls);

        // The same line under cheats answers, so the silence is the gate.
        var on = Standing(new Vector(0f, 0f, 0f), new Vector(0f, 0f, 0f));
        on.Command.RunLine("red scout");
        Assert.Contains(on.Player.Log.Lines, l => !l.Warning && l.Text.Contains("[TF2/Bots]"));
    }

    [Fact]
    public void The_bot_lands_in_front_of_the_callers_flat_view_facing_back()
    {
        // At (10, 20, 30) looking due east (yaw 90): 90 units along the flat
        // view is (10, 110, 30), turned back west — pitch 0, yaw 270.
        var rig = Standing(new Vector(10f, 20f, 30f), new Vector(0f, 90f, 0f));

        rig.Command.RunLine("red scout 90");

        var teleported = Assert.Single(rig.Player.Engine.AllOf("Entities.Teleport"));
        Assert.Equal(PlayerRig.Player, teleported.ArgAs<EntityHandle>(0));
        var at = teleported.ArgAs<Vector>(1);
        Assert.Equal(10.0, Math.Round((double)at.X, 3));
        Assert.Equal(110.0, Math.Round((double)at.Y, 3));
        Assert.Equal(30.0, Math.Round((double)at.Z, 3));
        var facing = teleported.ArgAs<Vector>(2);
        Assert.Equal(0d, Math.Round((double)facing.X, 3));
        Assert.Equal(270d, Math.Round((double)facing.Y, 3));
        Assert.True(teleported.ArgAs<bool>(3));

        Assert.Contains(rig.Player.Log.Lines,
                        l => !l.Warning
                             && l.Text == "[TF2/Bots] bot 3 red Scout at 10.0 110.0 30.0\n");
    }

    [Fact]
    public void No_distance_puts_the_bot_a_hundred_units_ahead()
    {
        // Looking due north (yaw 0): (10, 20, 30) plus 100 along x is
        // (110, 20, 30); the bot turns back west.
        var rig = Standing(new Vector(10f, 20f, 30f), new Vector(0f, 0f, 0f));

        rig.Command.RunLine("blue heavyweapons");

        var teleported = Assert.Single(rig.Player.Engine.AllOf("Entities.Teleport"));
        Assert.Equal(new Vector(110f, 20f, 30f), teleported.ArgAs<Vector>(1));
        Assert.Contains(rig.Player.Log.Lines,
                        l => !l.Warning
                             && l.Text == "[TF2/Bots] bot 3 blue Heavy at 110.0 20.0 30.0\n");
    }

    [Fact]
    public void The_bots_team_and_class_walk_the_human_join_path()
    {
        var rig = Standing(new Vector(0f, 0f, 0f), new Vector(0f, 0f, 0f));
        rig.Bots.Answer = PlayerRig.Player;

        rig.Command.RunLine("red scout");

        // Added on the named team, joined to it, and classed through joinclass:
        // the first pick of a classless bot respawns at once.
        Assert.Equal(("scout", (int)TFTeam.Red), Assert.Single(rig.Bots.Added));
        Assert.Equal((PlayerRig.Player, (int)TFTeam.Red), Assert.Single(rig.Teams.Joined));
        Assert.Contains(PlayerRig.Player, rig.Inner.Respawned);
        Assert.Equal(TFClass.Scout, rig.Class.Desired);
        Assert.Contains(rig.Player.Log.Lines, l => !l.Warning && l.Text.Contains("joins as Scout now"));
    }

    /// <summary>
    /// The balance refusal is a human-join answer; the dev bot's team is the
    /// caller's choice. When the join itself refuses, the command still puts
    /// the bot on the named team through the one ABI store.
    /// </summary>
    [Fact]
    public void A_refused_join_still_puts_the_dev_bot_on_the_named_team()
    {
        var rig = Standing(new Vector(0f, 0f, 0f), new Vector(0f, 0f, 0f));
        rig.Teams.Result = JoinTeamResult.Refused;

        rig.Command.RunLine("blue scout");

        Assert.Equal((PlayerRig.Player, (int)TFTeam.Blue), Assert.Single(rig.Teams.Joined));
        var store = Assert.Single(rig.Player.Engine.AllOf("Players.SetTeam"));
        Assert.Equal(PlayerRig.Player, store.ArgAs<EntityHandle>(0));
        Assert.Equal((int)TFTeam.Blue, store.ArgAs<int>(1));
    }

    [Fact]
    public void A_refused_add_is_logged_and_places_nothing()
    {
        var rig = Standing(new Vector(0f, 0f, 0f), new Vector(0f, 0f, 0f));
        rig.Bots.Answer = EntityHandle.None;
        rig.Bots.Refusal = "no room for another client";

        rig.Command.RunLine("red scout");

        Assert.Contains(rig.Player.Log.Lines,
                        l => l.Warning && l.Text == "[TF2/Bots] no room for another client\n");
        Assert.DoesNotContain(rig.Player.Engine.AllOf("Entities.Teleport"), _ => true);
    }

    private sealed class KickRig
    {
        public readonly RecordingLog Log = new();
        public readonly BotSeam Bots = new();

        public KickRig(params (EntityHandle Bot, int Index, string Name)[] listed)
        {
            foreach (var (bot, index, name) in listed)
                Bots.Listed.Add(new BotPlayer(bot, index, name));
        }

        public void Kick(string cheats = "1") =>
            new TFDevBotKickCommand(Log, DeclaredConVars.Holding<SourceSharp.Shared.SvCheatsConVar>(cheats), Bots)
                .RunLine(string.Empty);
    }

    [Fact]
    public void Every_added_bot_is_kicked_once_and_the_line_counts_them()
    {
        var mate = new EntityHandle(0x0001_0000u | 4);
        var rig = new KickRig((PlayerRig.Player, 3, "scout"), (mate, 4, "Heavy"));

        rig.Kick();

        Assert.Equal(2, rig.Bots.Kicked.Count);
        Assert.Equal(PlayerRig.Player, rig.Bots.Kicked[0].Bot);
        Assert.Equal(mate, rig.Bots.Kicked[1].Bot);
        Assert.Contains(rig.Log.Lines, l => !l.Warning && l.Text == "[TF2/Bots] kicked 2 bots\n");

        // One bot, the singular spelling.
        var one = new KickRig((PlayerRig.Player, 3, "scout"));
        one.Kick();
        Assert.Contains(one.Log.Lines, l => !l.Warning && l.Text == "[TF2/Bots] kicked 1 bot\n");
    }

    [Fact]
    public void Without_sv_cheats_the_kick_is_refused_too()
    {
        var rig = new KickRig((PlayerRig.Player, 3, "scout"));

        rig.Kick(cheats: "0");

        Assert.Equal("[TF2/Bots] tf2_dev_bot_kick runs only under sv_cheats\n",
                     rig.Log.Lines.Single(l => l.Warning).Text);
        Assert.DoesNotContain(rig.Log.Lines, l => !l.Warning);

        // Same rig, cheats on: the count line answers.
        var on = new KickRig((PlayerRig.Player, 3, "scout"));
        on.Kick();
        Assert.Contains(on.Log.Lines, l => !l.Warning && l.Text.Contains("kicked"));
    }

    /// <summary>"heavy" is taken for joinclass's heavyweapons.</summary>
    [Fact]
    public void Heavy_names_the_heavyweapons_class()
    {
        var rig = Standing(new Vector(0f, 0f, 0f), new Vector(0f, 0f, 0f));

        rig.Command.RunLine("red heavy");

        Assert.Equal(TFClass.HeavyWeapons, rig.Class.Desired);
    }

    /// <summary>A class name joinclass does not know is refused before a bot is added.</summary>
    [Fact]
    public void An_unknown_class_is_refused_and_adds_no_bot()
    {
        var rig = Standing(new Vector(0f, 0f, 0f), new Vector(0f, 0f, 0f));
        rig.Player.Engine.Clear();

        rig.Command.RunLine("red wizard");

        Assert.StartsWith("[TF2/Bots] tf2_dev_bot: no class 'wizard'",
                          rig.Player.Log.Lines.Single(l => l.Warning).Text);
        Assert.Empty(rig.Player.Engine.Calls);
    }

    /// <summary>A word that names neither playing team is refused before anything is asked.</summary>
    [Fact]
    public void No_such_team_names_itself_in_the_refusal()
    {
        var rig = Standing(new Vector(0f, 0f, 0f), new Vector(0f, 0f, 0f));
        rig.Player.Engine.Clear();

        rig.Command.RunLine("green scout");

        Assert.Equal("[TF2/Bots] tf2_dev_bot takes red or blue\n",
                     rig.Player.Log.Lines.Single(l => l.Warning).Text);
        Assert.Empty(rig.Player.Engine.Calls);
    }
}
