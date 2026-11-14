using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Commands;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Commands;

/// <summary>
/// <see cref="TFCondsCommand"/> (<c>tf2_conds [player]</c>): the server's own
/// one-line readout of a player — cheats-gated like <see cref="TFSetChargeCommand"/> —
/// built from the engine seams and the carrier's networked state. With no index
/// it walks the round roster's four teams once each, as
/// <see cref="TFConditionCommands"/> walks them.
/// </summary>
public sealed class TFCondsCommandTests
{
    private static readonly EntityHandle Player = new(0x0001_0000u | 3);

    private readonly RecordingLog _log = new();

    /// <summary>The engine double answering every read the line is made of.</summary>
    private RecordingEngine Engine()
    {
        var engine = new RecordingEngine();
        engine.Returns("Players.GetHandle", Player);
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Players.GetTeam", 3);
        engine.Returns("Entities.GetHealth", 125);
        engine.Returns("Entities.GetOrigin", new Vector(-1200f, 600f, 400f));
        engine.Returns("Entities.GetVelocity", new Vector(0f, 0f, -150f));
        engine.Returns("Players.GetActiveWeapon", "tf_weapon_pistol");
        engine.Returns("Players.IsAlive", true);
        return engine;
    }

    private sealed class SpyState : ITFPlayerData
    {
        private readonly TFPlayerNetState _state = Spy();

        private static TFPlayerNetState Spy()
        {
            var state = new TFPlayerNetState();
            state.BindLocal();
            state.PlayerClass = (int)TFClass.Spy;
            state.CloakMeter = 87f;
            state.ChargeMeter = 42f;
            state.PlayerCond = 1 << (int)TFCond.Stealthed;
            return state;
        }

        public TFPlayerNetState? Predicted(int playerIndex) => _state;
        public TFPlayerNetState? Networked(int playerIndex) => _state;
    }

    private sealed class TwoTeams : ITeamRoundRoster
    {
        public IReadOnlyList<EntityHandle> Players(TFTeam team) => team switch
        {
            TFTeam.Red => [new EntityHandle(0x0001_0000u | 2)],
            TFTeam.Blue => [new EntityHandle(0x0001_0000u | 3)],
            _ => [],
        };

        public TFTeam TeamOf(EntityHandle player) => TFTeam.Red;
        public bool IsAlive(EntityHandle player) => true;
        public float DeathTime(EntityHandle player) => -1f;
        public bool IsReadyToPlay(EntityHandle player) => true;
        public void ForceRespawn(EntityHandle player) { }
        public bool ChangeTeam(EntityHandle player, TFTeam team) => true;
        public string Name(EntityHandle player) => "player";
        public int EntIndex(EntityHandle player) => player.EntryIndex;
    }

    private TFCondsCommand Command(RecordingEngine engine, ITFPlayerData data, string cheats = "1")
        => new(_log, DeclaredConVars.Holding<SourceSharp.Shared.SvCheatsConVar>(cheats),
               engine, engine, data, new TwoTeams());

    [Fact]
    public void Without_sv_cheats_the_line_is_refused_and_the_state_is_not_read()
    {
        var engine = Engine();

        Command(engine, new SpyState(), cheats: "0").RunLine("3");

        // The refusal says so, once, and reads nothing else. The same rig
        // with cheats on answers the line, so the silence is the gate, not
        // an empty rig.
        Assert.Equal("[TF2/Conds] tf2_conds runs only under sv_cheats\n",
                     _log.Lines.Single(l => l.Warning).Text);
        Assert.DoesNotContain(_log.Lines, l => !l.Warning);

        Command(Engine(), new SpyState()).RunLine("3");
        Assert.Contains(_log.Lines, l => !l.Warning && l.Text.Contains("[TF2/Conds]"));
    }

    [Fact]
    public void The_named_player_gets_one_line_of_their_server_state()
    {
        Command(Engine(), new SpyState()).RunLine("3");

        Assert.Equal(
            "[TF2/Conds] 3 Spy team 3 hp 125 at -1200.0 600.0 400.0 vz -150 " +
            "weapon tf_weapon_pistol conds TF_COND_STEALTHED cloak 87 charge 42\n",
            _log.Lines.Single(l => !l.Warning).Text);
    }

    [Fact]
    public void No_index_prints_the_rostered_players_once_each()
    {
        // The bare line: both teams walk in order, each player answered from
        // the carrier — and with no carrier yet, from the engine reads alone
        // with an Undefined class and the meters at zero.
        var engine = Engine();
        int asked = 0;
        engine.ReturnsEachCall("Entities.GetIndex", () => ++asked == 1 ? 2 : 3);

        Command(engine, new NoCarriers()).RunLine("");

        var lines = _log.Lines.Where(l => !l.Warning).ToList();
        Assert.Equal(2, lines.Count);
        Assert.StartsWith("[TF2/Conds] 2 Undefined", lines[0].Text);
        Assert.Contains("conds - cloak 0 charge 0", lines[^1].Text);
    }

    private sealed class MedicState : ITFPlayerData
    {
        private readonly TFPlayerNetState _state = Build();

        private static TFPlayerNetState Build()
        {
            var state = new TFPlayerNetState();
            state.BindLocal();
            state.PlayerClass = (int)TFClass.Medic;
            state.MedicCharge = 0.5f;
            return state;
        }

        public TFPlayerNetState? Predicted(int playerIndex) => _state;
        public TFPlayerNetState? Networked(int playerIndex) => _state;
    }

    /// <summary>
    /// The medic's own half of the charge column: the ubercharge meter is
    /// published as <see cref="TFPlayerNetState.MedicCharge"/>, not the shield's
    /// <see cref="TFPlayerNetState.ChargeMeter"/> (the Ubersaw's kill adds half
    /// a charge through <c>SetChargeLevel</c>, which publishes there), so the
    /// medic line must answer it with its decimals — a half charge reads
    /// <c>charge 0.5</c>, where the plain meter field's integer format would
    /// flatten it away. Other classes keep the shield meter's whole numbers.
    /// </summary>
    [Fact]
    public void The_medic_line_reads_the_charge_column_from_the_medic_meter()
    {
        Command(Engine(), new MedicState()).RunLine("3");

        Assert.Contains("charge 0.5", _log.Lines.Single(l => !l.Warning).Text);
    }

    private sealed class SoldierState : ITFPlayerData
    {
        private readonly TFPlayerNetState _state = Build();

        private static TFPlayerNetState Build()
        {
            var state = new TFPlayerNetState();
            state.BindLocal();
            state.PlayerClass = (int)TFClass.Soldier;
            state.RageMeter = 87f;          // the buff meter the fills and the drain move
            state.ChargeMeter = 42f;        // the shield's meter, which a soldier never writes
            return state;
        }

        public TFPlayerNetState? Predicted(int playerIndex) => _state;
        public TFPlayerNetState? Networked(int playerIndex) => _state;
    }

    /// <summary>
    /// The soldier's half of the charge column: the Buff Banner's deploy gate
    /// reads <see cref="TFPlayerNetState.RageMeter"/>, and the shield's
    /// <see cref="TFPlayerNetState.ChargeMeter"/> stands only for the demoman's
    /// bash — so the soldier line answers the rage meter where the old shape
    /// printed the never-written shield meter's zero. The 42 under the 87
    /// shows which of the two fields the column takes.
    /// </summary>
    [Fact]
    public void The_soldier_line_reads_the_charge_column_from_the_rage_meter()
    {
        Command(Engine(), new SoldierState()).RunLine("3");

        Assert.Contains("charge 87", _log.Lines.Single(l => !l.Warning).Text);
    }

    private sealed class NoCarriers : ITFPlayerData
    {
        public TFPlayerNetState? Predicted(int playerIndex) => null;
        public TFPlayerNetState? Networked(int playerIndex) => null;
    }
}
