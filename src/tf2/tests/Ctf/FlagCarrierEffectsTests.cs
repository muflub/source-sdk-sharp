using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Entities.Ctf;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Conditions;

using TF2.Tests.Combat;

using Xunit;

namespace TF2.Tests.Ctf;

/// <summary>
/// The game's <see cref="FlagCarrierEffects"/>: the capture bonus crits
/// (<c>CTFGameRules::HandleCTFCaptureBonus</c>, tf_gamerules.cpp:20523-20538
/// over <c>tf_ctf_bonus_time</c>, :1114) and the spy's lost disguise on a
/// pick-up (<c>CCaptureFlag::PickUp</c>, entity_capture_flag.cpp:1235-1243).
/// </summary>
public sealed class FlagCarrierEffectsTests
{
    private static readonly EntityHandle Scout = new(0x0001_0004u);
    private static readonly EntityHandle Heavy = new(0x0001_0005u);
    private static readonly EntityHandle Dead = new(0x0001_0006u);
    private static readonly EntityHandle Enemy = new(0x0001_0007u);

    private readonly TFMovementFixtures.Players _states = new();
    private readonly RigConditions _conditions;
    private readonly FakeRoster _roster = new();
    private readonly FakeCtfPlayers _players = new();
    private readonly RecordingSpies _spies = new();
    private readonly ConVarRegistry _conVars;
    private readonly FlagCarrierEffects _effects;

    public FlagCarrierEffectsTests()
    {
        _conditions = new RigConditions(_states);
        foreach (var (index, team) in new[] { (4, TFTeam.Red), (5, TFTeam.Red), (6, TFTeam.Red), (7, TFTeam.Blue) })
        {
            _states.Add(index, TFClass.Scout);
            var handle = _players.Add(index, team);
            _roster.On(handle, team);
        }
        _roster.Alive[Dead] = false;

        _conVars = ConVarRegistry.For(TestServices.UnattachedConsole());
        _conVars.RegisterAll(typeof(TFCTFBonusTimeConVar).Assembly);
        _effects = new FlagCarrierEffects(_roster, _conditions, _players, _spies,
                                          _conVars.Declared<TFCTFBonusTimeConVar>());
    }

    private void BonusTime(string value) => _conVars.Declared<TFCTFBonusTimeConVar>().Bind(-1, "tf_ctf_bonus_time", value);

    [Fact]
    public void The_capture_crits_the_living_of_the_capping_team()
    {
        _effects.CaptureBonus(TFTeam.Red);

        Assert.True(_conditions.InCond(Scout, TFCond.CritboostedCtfCapture));
        Assert.True(_conditions.InCond(Heavy, TFCond.CritboostedCtfCapture));
    }

    [Fact]
    public void The_dead_of_the_capping_team_and_the_other_team_are_not_critted()
    {
        _effects.CaptureBonus(TFTeam.Red);

        Assert.False(_conditions.InCond(Dead, TFCond.CritboostedCtfCapture));
        Assert.False(_conditions.InCond(Enemy, TFCond.CritboostedCtfCapture));
    }

    [Fact]
    public void The_crit_runs_for_the_bonus_time()
    {
        // The convar's stock default is 10 (tf_gamerules.cpp:1114).
        _effects.CaptureBonus(TFTeam.Red);
        Assert.Equal(10f, _conditions.Of(Scout.EntryIndex)!.Duration(TFCond.CritboostedCtfCapture), 4);

        BonusTime("4");
        _effects.CaptureBonus(TFTeam.Blue);
        Assert.Equal(4f, _conditions.Of(Enemy.EntryIndex)!.Duration(TFCond.CritboostedCtfCapture), 4);
    }

    [Fact]
    public void A_bonus_time_of_zero_crits_nobody()
    {
        // The call ends before the walk (:20525-20528).
        BonusTime("0");
        Assert.Equal(0f, _conVars.Declared<TFCTFBonusTimeConVar>().Value);

        _effects.CaptureBonus(TFTeam.Red);

        Assert.False(_conditions.InCond(Scout, TFCond.CritboostedCtfCapture));
        Assert.Equal(0f, _conditions.Of(Scout.EntryIndex)!.Duration(TFCond.CritboostedCtfCapture));
    }

    [Fact]
    public void A_pick_ups_lost_disguise_reaches_the_spy_slice_by_index()
    {
        _effects.RemoveDisguise(Scout);

        Assert.Equal([4], _spies.DisguiseRemovals);
    }

    // ---- the map's SetCTFCaptureBonusTime override (tf_gamerules.h:451-460) ------------

    [Fact]
    public void The_map_s_bonus_time_override_beats_the_convar()
    {
        BonusTime("4");
        _effects.SetCaptureBonusTime(30f);

        _effects.CaptureBonus(TFTeam.Red);

        Assert.Equal(30f, _conditions.Of(Scout.EntryIndex)!.Duration(TFCond.CritboostedCtfCapture), 4);
    }

    [Fact]
    public void A_negative_override_restores_the_convar_and_a_level_init_does_too()
    {
        _effects.SetCaptureBonusTime(30f);
        _effects.SetCaptureBonusTime(-1f);
        _effects.CaptureBonus(TFTeam.Red);

        // GetCTFCaptureBonusTime's else arm: the convar's 10 (tf_gamerules.cpp:1114).
        Assert.Equal(10f, _conditions.Of(Scout.EntryIndex)!.Duration(TFCond.CritboostedCtfCapture), 4);

        _effects.SetCaptureBonusTime(30f);
        ((IGameLifecycle)_effects).OnLevelInit("ctf_2fort");
        _effects.CaptureBonus(TFTeam.Red);

        Assert.Equal(10f, _conditions.Of(Heavy.EntryIndex)!.Duration(TFCond.CritboostedCtfCapture), 4);
    }

    /// <summary>The roster, as teams of handles with an alive flag a fact sets.</summary>
    private sealed class FakeRoster : ITeamRoundRoster
    {
        private readonly Dictionary<TFTeam, List<EntityHandle>> _teams = [];

        public Dictionary<EntityHandle, bool> Alive { get; } = [];

        public void On(EntityHandle player, TFTeam team)
        {
            if (!_teams.TryGetValue(team, out var list))
                _teams[team] = list = [];
            list.Add(player);
        }

        public IReadOnlyList<EntityHandle> Players(TFTeam team) => _teams.TryGetValue(team, out var list) ? list : [];
        public TFTeam TeamOf(EntityHandle player) => _teams.First(kv => kv.Value.Contains(player)).Key;
        public bool IsAlive(EntityHandle player) => Alive.TryGetValue(player, out var alive) ? alive : true;
        public float DeathTime(EntityHandle player) => IsAlive(player) ? -1f : 0f;
        public bool IsReadyToPlay(EntityHandle player) => true;
        public void ForceRespawn(EntityHandle player) { Alive[player] = true; }
        public bool ChangeTeam(EntityHandle player, TFTeam team) => false;
        public string Name(EntityHandle player) => "player" + player.EntryIndex;
        public int EntIndex(EntityHandle player) => player.EntryIndex;
    }

    /// <summary>The spy state, recording the asks the flag's arms make.</summary>
    private sealed class RecordingSpies : ITFSpyState
    {
        public List<int> DisguiseRemovals { get; } = [];

        public bool Swing(int ownerIndex, float range) => false;
        public bool Smack(int ownerIndex) => false;
        public void RemoveDisguise(int playerIndex) => DisguiseRemovals.Add(playerIndex);
        public void RemoveInvisibility(int playerIndex) { }
        public bool ActivateInvisibilityWatch(int playerIndex) => false;
        public bool Disguise(int playerIndex, TFClass disguiseClass, TFTeam disguiseTeam) => false;
        public bool LastDisguise(int playerIndex, TFClass desired = TFClass.Undefined) => false;
    }
}
