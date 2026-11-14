using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Entities.Ctf;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// <see cref="TFGlowTransmit"/>: the teammate glow's force-transmit on the
/// core's <see cref="ITransmitRule"/> seam, asked through the real routing
/// (<see cref="ClientsBinding.ServerShouldTransmit"/>) — so the facts show the
/// seam is asked for player entities at all, the handle it sees is the
/// directory's, and the answers reach the engine's 0/1/2. The stock walk
/// (tf_player.cpp:3508-3534 over :22887-22891) forces Always for the spectator
/// and the same-team recipient that is dead or holds the glow; everyone else
/// keeps the PVS check.
/// </summary>
public sealed class TFGlowTransmitTests
{
    /// <summary>The player entity the walk FULLCHECKs (entry 11).</summary>
    private static readonly EntityHandle Player = new(0x0000_000Bu);

    private static readonly EntityHandle DeadMate = new(0x0000_0009u);
    private static readonly EntityHandle Spectator = new(0x0000_000Cu);
    private static readonly EntityHandle Plain = new(0x0000_000Du);
    private static readonly EntityHandle Enemy = new(0x0000_000Eu);
    private static readonly EntityHandle Glower = new(0x0000_000Fu);

    /// <summary>Every player the rule may ask about, on the team the fact wants.</summary>
    private sealed class GlowPlayers : ICtfPlayers
    {
        /// <summary>The entity handles the rule asked about, in order.</summary>
        public readonly List<EntityHandle> Seen = [];

        public readonly Dictionary<EntityHandle, TFTeam> Teams = [];
        public readonly HashSet<EntityHandle> Alive = [];
        public readonly Dictionary<EntityHandle, TFCondMask> Conds = [];

        public bool IsPlayer(EntityHandle entity)
        {
            Seen.Add(entity);
            return Teams.ContainsKey(entity);
        }

        public bool IsAlive(EntityHandle player) => Alive.Contains(player);

        public TFTeam Team(EntityHandle player) => Teams.TryGetValue(player, out TFTeam team) ? team : TFTeam.Unassigned;

        public int Index(EntityHandle player) => player.EntryIndex;

        public int UserId(EntityHandle player) => player.EntryIndex;

        public Vector WorldSpaceCenter(EntityHandle player) => Vector.Zero;

        public bool IsTFPlayer(EntityHandle player) => true;

        public TFClass Class(EntityHandle player) => TFClass.Soldier;

        public TFCondMask Conditions(EntityHandle player) => Conds.TryGetValue(player, out TFCondMask mask) ? mask : default;

        public bool AllowedToPickUpFlag(EntityHandle player) => true;

        public float PercentInvisible(EntityHandle player) => 0f;

        public int FlagAttachment(EntityHandle player) => 0;

        public Vector Velocity(EntityHandle player) => Vector.Zero;

        public float MaxSpeed(EntityHandle player) => 1f;
    }

    /// <summary>The players the facts share: everyone Red but the spectator,
    /// the enemy on Blue; everyone alive but the dead mate; only the glow-holder
    /// carries the glow.</summary>
    private static GlowPlayers Rig()
    {
        var players = new GlowPlayers();
        players.Teams[Player] = TFTeam.Red;
        players.Teams[DeadMate] = TFTeam.Red;
        players.Teams[Spectator] = TFTeam.Spectator;
        players.Teams[Plain] = TFTeam.Red;
        players.Teams[Enemy] = TFTeam.Blue;
        players.Teams[Glower] = TFTeam.Red;

        foreach (EntityHandle h in new[] { Player, Spectator, Plain, Enemy, Glower })
            players.Alive.Add(h);

        players.Conds[Glower] = TFCondMask.Of(TFCond.TeamGlows);
        return players;
    }

    /// <summary>The binding of one fact: the rule over the routing, the slot's
    /// client named whole (one recipient per fact).</summary>
    private static ClientsBinding Binding(GlowPlayers players, EntityHandle recipientForSlot)
    {
        var engine = TestServices.Mock().Returns("Players.GetHandle", recipientForSlot);
        var module = DispatchProxy.Create<IServerModule, Module>();
        return new ClientsBinding(new Crossings(), new QuietLog(), module,
                                  i => s_Directory.HandleOf(i),
                                  new HooksOf(new TFGlowTransmit(players, engine)));
    }

    private sealed class HooksOf(params ITransmitRule[] rules) : IHooks<ITransmitRule>
    {
        public IReadOnlyList<ITransmitRule> All => rules;
        public void Each(string what, Action<ITransmitRule> call) => throw new NotSupportedException();
    }

    private static readonly EntityDirectory s_Directory = new(new QuietLog());

    private class Module : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => null;
    }

    private sealed class QuietLog : ILog
    {
        public void Msg(string message) { }

        public void Warning(string message) { }
    }

    static TFGlowTransmitTests()
    {
        Assert.True(s_Directory.Bind(Player, new Thing()));
    }

    private sealed class Thing : ScriptedEntity
    {
    }

    /// <summary>The slot the engine hands the walk carries the recipient's
    /// entry index; the stub names that client whole.</summary>
    private static int Slot(EntityHandle recipient) => recipient.EntryIndex;

    [Fact]
    public void ADeadTeammateIsSentThroughThePvsAndTheSeesThePlayerEntity()
    {
        // :22890's dead arm, and the routing: the entity the rule is asked
        // about is the directory handle for the FULLCHECKed edict.
        var players = Rig();
        var binding = Binding(players, DeadMate);

        Assert.Equal(1, binding.ServerShouldTransmit(Slot(DeadMate), Player.EntryIndex));
        Assert.Contains(Player, players.Seen);
    }

    [Fact]
    public void ASpectatorIsSentAlways()
    {
        // :22889's first arm.
        var players = Rig();

        Assert.Equal(1, Binding(players, Spectator).ServerShouldTransmit(Slot(Spectator), Player.EntryIndex));
    }

    [Fact]
    public void AGlowHolderSeesTheirLivingTeammateThroughThePvs()
    {
        // :22890's glow arm on a living recipient.
        var players = Rig();

        Assert.Equal(1, Binding(players, Glower).ServerShouldTransmit(Slot(Glower), Player.EntryIndex));
    }

    [Fact]
    public void ALivingTeammateWithoutGlowsAndAnEnemyKeepThePvsCheck()
    {
        // :3533's walk for everyone the team arms do not force: no opinion.
        var players = Rig();

        Assert.Equal(2, Binding(players, Plain).ServerShouldTransmit(Slot(Plain), Player.EntryIndex));
        Assert.Equal(2, Binding(players, Enemy).ServerShouldTransmit(Slot(Enemy), Player.EntryIndex));
    }
}
