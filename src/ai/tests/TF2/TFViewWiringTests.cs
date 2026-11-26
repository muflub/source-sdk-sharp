using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Rules;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The TF2 view over the mod's reads (P6 step 3): a bot's own HUD carries its class, its
/// conditions and its metal; a sighted player is drawn by the viewer's own team, from the mod's
/// facts; a player the mod has no facts of is drawn as the engine read him.
/// </summary>
public sealed class TFViewWiringTests
{
    private sealed class Facts : ITFActorFacts
    {
        public readonly Dictionary<int, TFActorFacts> By = new();

        public bool TryRead(int playerIndex, out TFActorFacts facts) => By.TryGetValue(playerIndex, out facts);
    }

    private sealed class Players : ITFPlayerData
    {
        public readonly Dictionary<int, TFPlayerNetState> By = new();

        public TFPlayerNetState? Predicted(int playerIndex) => null;

        public TFPlayerNetState? Networked(int playerIndex) => By.GetValueOrDefault(playerIndex);
    }

    public class Unasked : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException($"asked {method?.Name}");
    }

    /// <summary>The players' bodies a fact sets (round 2's S4); anyone else dry and on the ground.</summary>
    internal sealed class Bodies : ITFBodies
    {
        public readonly Dictionary<EntityHandle, TFBody> By = new();

        public TFBody BodyOf(EntityHandle player) => By.GetValueOrDefault(player, TFBody.Unread);
    }

    private static readonly EntityHandle Bot = new(0x0001_0002u), Them = new(0x0001_0005u);

    private sealed class Rounds : ITFRoundStateView
    {
        public RoundState State { get; set; } = RoundState.RoundRunning;

        public TFTeam Winner => TFTeam.Unassigned;
    }

    private readonly Facts _facts = new();
    private readonly Players _players = new();
    private readonly Rounds _rounds = new();
    private readonly Bodies _bodies = new();
    private readonly TFBotGameView _view;

    public TFViewWiringTests() => _view = View(freeze: "1");

    private TFBotGameView View(string freeze)
        => new(_facts, _players, DispatchProxy.Create<IEngineEntities, Unasked>(), DispatchProxy.Create<IEntityDirectory, Unasked>(),
               new TFDisguiseWeapons(null!, null!, null!, null!, null!, null!), WeaponCatalog.Stock(), null!, _rounds,
               new ConVarRegistry(new NamedConVars(new Dictionary<string, string> { ["tf_player_movement_restart_freeze"] = freeze }))
                   .Declare<TFPlayerMovementRestartFreezeConVar>(), null!, new TFPlayerLives(), DeclaredConVars.Fresh<TFAvoidTeammatesConVar>(), _bodies);

    /// <summary>
    /// The pre-round freeze reaches the bot's HUD as its client knows it: the round restarting, with the
    /// replicated freeze on. Running, or with the freeze off, the bot is free (P6 step 1: a held bot hopped).
    /// </summary>
    [Fact]
    public void TheBotsHudShowsItFrozenOnlyInThePreroundWithTheFreezeOn()
    {
        _facts.By[2] = Of(TFClass.Soldier, TFTeam.Red);
        var read = new SelfState(default, default, default, 200, 0, 1, 4, 20, SelfFlags.Alive | SelfFlags.Grounded);
        Assert.Equal(SelfFlags.None, _view.Hud(Bot, read).Flags & SelfFlags.Frozen);
        Assert.False(_view.Frozen(Bot));

        _rounds.State = RoundState.Preround;
        Assert.True(_view.Frozen(Bot));
        Assert.Equal(SelfFlags.Alive | SelfFlags.Grounded | SelfFlags.Frozen, _view.Hud(Bot, read).Flags);
        Assert.False(View(freeze: "0").Frozen(Bot));
    }

    private static TFActorFacts Of(TFClass c, TFTeam team, bool disguised = false, bool burning = false)
        => new(c, team, disguised ? TFClass.Medic : TFClass.Civilian, disguised ? TFTeam.Red : TFTeam.Unassigned, disguised ? 2 : 0, 0f, 0,
               burning, false, false, disguised, false, false);

    [Fact]
    public void TheBotsOwnHudCarriesItsClassConditionsAndMetal()
    {
        _facts.By[2] = Of(TFClass.Engineer, TFTeam.Red, burning: true);
        var state = new TFPlayerNetState();
        state.BindLocal();
        state.Ammo[(int)TFAmmo.Metal] = 150;
        _players.By[2] = state;
        SelfState hud = _view.Hud(Bot, new SelfState(default, default, default, 125, 0, 1, 6, 32, SelfFlags.Alive));
        Assert.Equal(((int)TFClass.Engineer, ActorConditions.Burning, 150, 125), (hud.Class, hud.Conditions, hud.Metal, hud.Health));
    }

    /// <summary>Dominations (plan_bots_teams_tf2_part_g G.2.5): the bot's own row for a player, as its scoreboard's nemesis mark reads it, and only its own.</summary>
    [Fact]
    public void TheBotsOwnHudSaysWhoDominatesIt()
    {
        var own = new TFPlayerNetState();
        own.BindLocal();
        own.PlayerDominatingMe[5] = true;
        _players.By[2] = own;
        var theirs = new TFPlayerNetState();
        theirs.BindLocal();
        theirs.PlayerDominatingMe[9] = true;
        _players.By[5] = theirs;
        // Slot 5 dominates the bot; slot 9 dominates slot 5, which is no business of the bot's HUD.
        Assert.Equal((true, false, false), (_view.DominatedBy(Bot, 5), _view.DominatedBy(Bot, 9), _view.DominatedBy(Bot, 2)));
    }

    [Fact]
    public void ABotWithNoStateOfItsOwnIsDominatedByNobody() => Assert.False(_view.DominatedBy(Bot, 5));

    [Fact]
    public void ASightedPlayerIsDrawnForTheViewersOwnTeam()
    {
        _facts.By[2] = Of(TFClass.Soldier, TFTeam.Red);
        _facts.By[5] = Of(TFClass.Spy, TFTeam.Blue, disguised: true);
        var drawn = new ActorFacts(new ActorId(5, 1), default, default, 3, ActorActivity.None, 1f, (int)TFTeam.Blue);
        Assert.True(_view.Render(Bot, Them, ref drawn));
        Assert.Equal(((int)TFTeam.Red, (int)TFClass.Medic, 2), (drawn.Team, drawn.Class, drawn.NameSlot));

        // The control: to his own team he is a Spy.
        _facts.By[2] = Of(TFClass.Soldier, TFTeam.Blue);
        var own = new ActorFacts(new ActorId(5, 1), default, default, 3, ActorActivity.None, 1f, (int)TFTeam.Blue);
        Assert.True(_view.Render(Bot, Them, ref own));
        Assert.Equal(((int)TFTeam.Blue, (int)TFClass.Spy), (own.Team, own.Class));
    }

    [Fact]
    public void APlayerTheModHasNoFactsOfIsDrawnAsTheEngineReadHim()
    {
        var read = new ActorFacts(new ActorId(5, 1), new AiVector(1, 2, 3), default, 3, ActorActivity.None, 1f, (int)TFTeam.Blue);
        ActorFacts drawn = read;
        Assert.True(_view.Render(Bot, Them, ref drawn));
        Assert.Equal(read, drawn);
    }

    // ---- round 2's S4: the body read reaches what the view draws (review r2s4 item 4)

    private static ActorFacts EngineRead() => new(new ActorId(5, 1), default, default, 3, ActorActivity.None, 1f, (int)TFTeam.Blue);

    /// <summary>An enemy standing in water is drawn wet: the body's water level, read for him, reaches the view.</summary>
    [Fact]
    public void AnEnemyInWaterIsDrawnWet()
    {
        _facts.By[2] = Of(TFClass.Soldier, TFTeam.Red);
        _facts.By[5] = Of(TFClass.Soldier, TFTeam.Blue);
        _bodies.By[Them] = new TFBody(WaterLevel: 2, OnGround: true);
        ActorFacts drawn = EngineRead();
        Assert.True(_view.Render(Bot, Them, ref drawn));
        Assert.Equal(ActorConditions.Wet, drawn.Conditions);
    }

    /// <summary>An enemy a blast threw, off the ground, is drawn airborne: the body's ground, read for him, reaches the view.</summary>
    [Fact]
    public void AnEnemyABlastThrewIsDrawnAirborne()
    {
        _facts.By[2] = Of(TFClass.Soldier, TFTeam.Red);
        _facts.By[5] = Of(TFClass.Soldier, TFTeam.Blue) with { BlastJumping = true };
        _bodies.By[Them] = new TFBody(WaterLevel: 0, OnGround: false);
        ActorFacts drawn = EngineRead();
        Assert.True(_view.Render(Bot, Them, ref drawn));
        Assert.Equal(ActorConditions.BlastAirborne, drawn.Conditions);
    }

    /// <summary>The bot's own HUD shows its mark for death while it is disguised, as stock's icon by the health does.</summary>
    [Fact]
    public void TheBotsOwnHudShowsItsMarkWhileDisguised()
    {
        _facts.By[2] = Of(TFClass.Spy, TFTeam.Red, disguised: true) with { MarkedForDeath = true };
        SelfState hud = _view.Hud(Bot, new SelfState(default, default, default, 125, 0, 1, 6, 32, SelfFlags.Alive));
        Assert.Equal(ActorConditions.MarkedForDeath, hud.Conditions & ActorConditions.MarkedForDeath);
    }
}
