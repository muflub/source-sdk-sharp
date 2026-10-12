using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The four entities that manage something on a map's behalf — how many pieces
/// may fly, how many guns may lie about, who is at the controls, and which events
/// reach the map.
///
/// What is worth testing is what is invisible when it is wrong. A gib budget that
/// caps the world instead of a burst looks like a map with no debris; a sweep that
/// counts a hand-placed shotgun as surplus deletes part of the level and reads as
/// a missing entity; a button edge reported the wrong way round runs a crane
/// backwards; and an event listener that ignores its team filter fires for the
/// other side, which a map author sees as their door opening for the enemy.
/// </summary>
public class GameManagerTests
{
    private static readonly EntityHandle Ent = new(0x0041_0041u);
    private static readonly EntityHandle Player = new(0x0042_0042u);

    private static T Spawn<T>(ScriptedEntityRegistry registry, string className) where T : ScriptedEntity
    {
        registry.Spawn(Ent, className);
        return (T)registry.Get(Ent)!;
    }

    /// <summary>A feed of this test's own, over events of its own.</summary>
    private static GameEventFeed NewFeed()
        => new(new PlayerConnectEvent(), new PlayerDeathEvent(), new PlayerActivateEvent(),
               new PlayerTeamEvent(), new PlayerDisconnectEvent());

    private static ScriptedEntityRegistry Registry(IGameEventFeed? feed = null)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(GameGibManager).Assembly);

        // The systems these entities ask for, faked. game_ui reads the buttons
        // a player is holding, and this is the whole reason it takes a service
        // rather than the static delegate it used to: a test can hand it one.
        // The registry refuses to build an entity whose services are missing —
        // which is the point, and which is why this line is not optional.
        // logic_eventlistener's feed: the module's in the game, a fresh one
        // (or the caller's) here, so no two tests share a listener list.
        registry.Services = new ServiceContainer()
            .AddInstance<IPlayerButtons>(new NoButtons())
            .AddInstance<IGameEventFeed>(feed ?? NewFeed());

        return registry;
    }

    /// <summary>Nobody is holding anything. These tests drive the edges by hand.</summary>
    private sealed class NoButtons : IPlayerButtons
    {
        public int Held(EntityHandle player) => 0;
    }

    [Fact]
    public void GibBurstBudget()
    {
        // The condition that makes this a burst budget rather than a world one:
        // the count is only consulted in a frame that has already spawned a
        // piece. A crate broken on its own is never refused.
        Assert.True(GameGibManager.Allows(allowNewGibs: false, maxPieces: 4, livePieces: 40, spawnedThisFrame: false), "the first piece of a frame is always allowed");

        Assert.True(!GameGibManager.Allows(allowNewGibs: false, maxPieces: 4, livePieces: 4, spawnedThisFrame: true), "a burst is cut off at the budget");

        Assert.True(GameGibManager.Allows(allowNewGibs: false, maxPieces: 4, livePieces: 3, spawnedThisFrame: true), "and allowed below it");

        // Zero is a budget and not an absence, the same as the ragdoll one.
        Assert.True(!GameGibManager.Allows(allowNewGibs: false, maxPieces: 0, livePieces: 0, spawnedThisFrame: false), "a budget of zero forbids pieces outright");

        Assert.True(GameGibManager.Allows(allowNewGibs: false, maxPieces: -1, livePieces: 400, spawnedThisFrame: true), "no budget means no limit of this entity's own");

        // The waiver beats every one of them, which is what a map turns on for
        // the one room that is allowed to be expensive.
        Assert.True(GameGibManager.Allows(allowNewGibs: true, maxPieces: 0, livePieces: 400, spawnedThisFrame: true), "allownewgibs waives the budget");
    }

    [Fact]
    public void GibKeysAndInputs()
    {
        var registry = Registry();

        registry.KeyValue(Ent, EntityNames.GameGibManager, "maxpieces", "12");
        registry.KeyValue(Ent, EntityNames.GameGibManager, "maxpiecesdx8", "4");
        registry.KeyValue(Ent, EntityNames.GameGibManager, "allownewgibs", "0");

        var manager = Spawn<GameGibManager>(registry, EntityNames.GameGibManager);

        // No engine to answer mat_dxlevel, which is the dedicated-server case and
        // the capable path — so the plain budget stands, not the DX8 one.
        Assert.True(manager.CurrentMaxPieces == 12, "the map's piece budget is in force after spawn");
        Assert.True(manager.CurrentMaxPieces != 4, "an unanswered dx level does not apply the DX8 budget");
        Assert.True(!manager.AllowNewGibs, "allownewgibs is read from the map");

        registry.Input(Ent, "SetMaxPieces", "6", 6f, EntityHandle.None);
        Assert.True(manager.CurrentMaxPieces == 6, "an input replaces the budget");

        // Setting the DX8 budget changes nothing on a capable machine: they are
        // two numbers and only one of them applies.
        registry.Input(Ent, "SetMaxPiecesDX8", "1", 1f, EntityHandle.None);
        Assert.True(manager.CurrentMaxPieces == 6, "the DX8 input does not disturb the plain budget");

        Assert.True(manager.AllowsAnother(livePieces: 6, spawnedThisFrame: true) == false, "the entity answers to the budget it was told to hold");
    }

    [Fact]
    public void WeaponSweep()
    {
        var registry = Registry();

        registry.KeyValue(Ent, EntityNames.GameWeaponManager, "weaponname", "weapon_smg1");
        registry.KeyValue(Ent, EntityNames.GameWeaponManager, "maxpieces", "2");

        var manager = Spawn<GameWeaponManager>(registry, EntityNames.GameWeaponManager);

        var placed = new[] { new EntityHandle(0x1001u), new EntityHandle(0x1002u) };

        // The survey. Whatever is already there when the manager first looks is
        // the map's, and the first sweep must therefore remove nothing at all —
        // including when there are already more of them than the budget allows.
        Assert.True(manager.Surplus(placed).Count == 0, "the first sweep removes nothing");

        var dropped = new[] { new EntityHandle(0x2001u), new EntityHandle(0x2002u) };

        // Two dropped, two allowed: nothing is surplus yet, and the two the map
        // placed are not counted against the budget.
        var floor = new List<EntityHandle> { placed[0], placed[1], dropped[0], dropped[1] };
        Assert.True(manager.Surplus(floor).Count == 0, "hand-placed weapons are not counted against the budget");

        // A third arrives, and the oldest of the dropped ones goes.
        floor.Add(new EntityHandle(0x2003u));
        var surplus = manager.Surplus(floor);

        Assert.True(surplus.Count == 1, "one over the budget removes one");
        Assert.True(surplus[0].Value == dropped[0].Value, "and it is the one that has been on the floor longest");

        // The map's own weapons are never candidates however many pile up.
        var manyPlaced = new List<EntityHandle>(placed);
        for (uint i = 0; i < 8; i++)
            manyPlaced.Add(new EntityHandle(0x2100u + i));

        Assert.True(manager.Surplus(manyPlaced).Count == 6, "a pile of dropped weapons is cut back to the budget");
    }

    [Fact]
    public void WeaponKeysAndInputs()
    {
        var registry = Registry();

        registry.KeyValue(Ent, EntityNames.GameWeaponManager, "weaponname", "weapon_shotgun");
        var manager = Spawn<GameWeaponManager>(registry, EntityNames.GameWeaponManager);

        // Stock's default is zero, because the keyfield starts zeroed — so a
        // manager with no maxpieces sweeps every dropped one. Worth pinning: the
        // tempting "unset means unlimited" reading would make the entity inert.
        manager.Surplus(Array.Empty<EntityHandle>());
        Assert.True(manager.Surplus(new[] { new EntityHandle(0x3001u) }).Count == 1, "no budget key means no dropped weapons are tolerated");

        registry.Input(Ent, "SetMaxPieces", "1", 1f, EntityHandle.None);
        Assert.True(manager.Surplus(new[] { new EntityHandle(0x3001u) }).Count == 0, "an input raises the budget");

        // A name this mod has no weapon for. Stock creates one to find out and
        // removes itself when it cannot; this asks the weapon table.
        var other = new ScriptedEntityRegistry();
        other.RegisterAll(typeof(GameWeaponManager).Assembly);
        other.Services = Services();
        other.KeyValue(Ent, EntityNames.GameWeaponManager, "weaponname", "weapon_physcannon");
        other.Spawn(Ent, EntityNames.GameWeaponManager);

        Assert.True(!((GameWeaponManager)other.Get(Ent)!).Managing, "a manager for a weapon this mod does not have removes itself");

        Assert.True(manager.Managing, "and one for a weapon it does have stays");
    }

    [Fact]
    public void UiButtonEdges()
    {
        var registry = Registry();
        var ui = Spawn<GameUi>(registry, EntityNames.GameUi);

        ui.Report((int)InputButtons.Forward);
        Assert.True(ui.PressedForward.FireCount == 1, "holding forward reports a press");
        Assert.True(ui.UnpressedForward.FireCount == 0, "and not a release");

        // Held, not changed. The map has already been told.
        ui.Report((int)InputButtons.Forward);
        Assert.True(ui.PressedForward.FireCount == 1, "still holding forward reports nothing new");

        ui.Report(0);
        Assert.True(ui.UnpressedForward.FireCount == 1, "letting go reports a release");
        Assert.True(ui.PressedForward.FireCount == 1, "and does not report another press");

        // Two at once, and the pair that is easiest to transpose.
        ui.Report((int)(InputButtons.MoveLeft | InputButtons.Attack));
        Assert.True(ui.PressedMoveLeft.FireCount == 1, "left is reported as left");
        Assert.True(ui.PressedMoveRight.FireCount == 0, "and not as right");
        Assert.True(ui.PressedAttack.FireCount == 1, "attack is reported alongside it");

        ui.Report((int)InputButtons.Attack2);
        Assert.True(ui.UnpressedMoveLeft.FireCount == 1 && ui.PressedAttack2.FireCount == 1, "swapping keys in one tick reports both edges");
        Assert.True(ui.UnpressedAttack.FireCount == 1, "and the released attack too");
    }

    [Fact]
    public void UiAxes()
    {
        Assert.True(GameUi.AxisX((int)InputButtons.MoveRight) == 1f, "right is positive on X");
        Assert.True(GameUi.AxisX((int)InputButtons.MoveLeft) == -1f, "left is negative on X");
        Assert.True(GameUi.AxisY((int)InputButtons.Forward) == 1f, "forward is positive on Y");
        Assert.True(GameUi.AxisY((int)InputButtons.Back) == -1f, "back is negative on Y");

        // Both at once is stock's order rather than a considered one, and pinned
        // here because it is the order a crane was built against.
        Assert.True(GameUi.AxisX((int)(InputButtons.MoveLeft | InputButtons.MoveRight)) == 1f, "right wins when both strafe keys are held");
        Assert.True(GameUi.AxisX(0) == 0f && GameUi.AxisY(0) == 0f, "nothing held is zero");

        var registry = Registry();
        var ui = Spawn<GameUi>(registry, EntityNames.GameUi);

        ui.Report((int)InputButtons.MoveRight);
        Assert.True(ui.XAxis.FireCount == 1, "the axis is published when it moves");
        Assert.True(ui.XAxis.LastValue == "1", "with the value the keys ask for");

        ui.Report((int)InputButtons.MoveRight);
        Assert.True(ui.XAxis.FireCount == 1, "and not republished while it holds still");

        ui.Report(0);
        Assert.True(ui.XAxis.FireCount == 2 && ui.XAxis.LastValue == "0", "returning to centre is published");

        // A key on another axis must not disturb this one.
        ui.Report((int)InputButtons.Forward);
        Assert.True(ui.XAxis.FireCount == 2, "an unrelated key leaves the X axis alone");
        Assert.True(ui.YAxis.LastValue == "1", "and moves the Y axis");
    }

    [Fact]
    public void UiExits()
    {
        // Named from the entity's own enum. These were two more copies of the
        // numbers, sitting in a test file with nothing tying them to the
        // constants the entity actually reads.
        const GameUi.SpawnFlag UseDeactivates = GameUi.SpawnFlag.UseDeactivates;
        const GameUi.SpawnFlag JumpDeactivates = GameUi.SpawnFlag.JumpDeactivates;

        int use = (int)InputButtons.Use;
        int jump = (int)InputButtons.Jump;

        Assert.True(GameUi.Deactivates(UseDeactivates, use), "+use releases the controls when the flag is set");
        Assert.True(!GameUi.Deactivates(JumpDeactivates, use), "and does not when it is not");
        Assert.True(GameUi.Deactivates(JumpDeactivates, jump), "jump releases them under its own flag");
        Assert.True(!GameUi.Deactivates(GameUi.SpawnFlag.None, use | jump), "no flags means no key gets out");

        // Only the leading edge is passed in, which is what stops a player who
        // arrived holding +use — which is how most of them arrive — from being
        // ejected on the tick they sat down.
        Assert.True(!GameUi.Deactivates(UseDeactivates | JumpDeactivates, 0), "nothing newly pressed releases nothing");

        // The field of view tolerance is a dot product: 1 is straight at it.
        var atIt = new Vector(1f, 0f, 0f);
        var away = new Vector(-1f, 0f, 0f);
        var toControls = new Vector(64f, 0f, 0f);

        Assert.True(GameUi.Watching(atIt, toControls, 0.7f), "a player looking at the controls keeps them");
        Assert.True(!GameUi.Watching(away, toControls, 0.7f), "and loses them on looking away");

        // Distance must not matter: a console across the room lets go at the same
        // angle as one under the player's nose.
        Assert.True(GameUi.Watching(atIt, toControls * 40f, 0.99f), "the tolerance is an angle, not a distance");
    }

    [Fact]
    public void EventListening()
    {
        var feed = NewFeed();

        var registry = Registry(feed);
        registry.KeyValue(Ent, EntityNames.LogicEventlistener, "EventName", "player_death");
        var listener = Spawn<LogicEventListener>(registry, EntityNames.LogicEventlistener);

        Assert.True(feed.ListenerCount == 1, "a listener is live once it spawns");

        // Stock's default, and the surprising one: off until told otherwise.
        Assert.True(!listener.IsEnabled, "a listener with no IsEnabled key starts disabled");

        feed.Deliver("player_death", 7);
        Assert.True(listener.OnEventFired.FireCount == 0, "a disabled listener reports nothing");

        registry.Input(Ent, "Enable", "", 0f, EntityHandle.None);
        Assert.True(listener.IsEnabled, "Enable turns it on");

        feed.Deliver("player_death", 7);
        Assert.True(listener.OnEventFired.FireCount == 1, "an enabled listener reports its event");

        feed.Deliver("player_connect", 7);
        Assert.True(listener.OnEventFired.FireCount == 1, "and ignores an event it was not listening for");

        registry.Input(Ent, "Disable", "", 0f, EntityHandle.None);
        feed.Deliver("player_death", 7);
        Assert.True(listener.OnEventFired.FireCount == 1, "Disable turns it off again");

        // Removal is how the feed is emptied between maps, so it is worth
        // knowing it happens at all.
        registry.Removed(Ent);
        Assert.True(feed.ListenerCount == 0, "a removed listener leaves the feed");
    }

    [Fact]
    public void TheFeedHearsTheEventsItWasGiven()
    {
        // The feed attaches to ITS events: a death raised on the event object
        // it was built over reaches a listener spawned into it.
        var deaths = new PlayerDeathEvent();
        var feed = new GameEventFeed(new PlayerConnectEvent(), deaths, new PlayerActivateEvent(),
                                     new PlayerTeamEvent(), new PlayerDisconnectEvent());

        var registry = Registry(feed);
        registry.KeyValue(Ent, EntityNames.LogicEventlistener, "EventName", "player_death");
        registry.KeyValue(Ent, EntityNames.LogicEventlistener, "IsEnabled", "1");
        var listener = Spawn<LogicEventListener>(registry, EntityNames.LogicEventlistener);

        deaths.Raise(new PlayerDeathArgs(7, 0, "world", false));

        Assert.True(listener.OnEventFired.FireCount == 1, "the injected event reaches the listener");
    }

    [Fact]
    public void TwoFeedsDoNotShareListeners()
    {
        // The property the static feed lacked: a listener spawned into one
        // module's feed is not reported by another's, and is not counted there.
        var mine = NewFeed();
        var theirs = NewFeed();

        var registry = Registry(mine);
        registry.KeyValue(Ent, EntityNames.LogicEventlistener, "EventName", "player_death");
        registry.KeyValue(Ent, EntityNames.LogicEventlistener, "IsEnabled", "1");
        var listener = Spawn<LogicEventListener>(registry, EntityNames.LogicEventlistener);

        Assert.True(mine.ListenerCount == 1, "the listener joined the feed it was given");
        Assert.True(theirs.ListenerCount == 0, "and no other");

        theirs.Deliver("player_death", 7);
        Assert.True(listener.OnEventFired.FireCount == 0, "another feed's event does not reach it");

        mine.Deliver("player_death", 7);
        Assert.True(listener.OnEventFired.FireCount == 1, "its own feed's does");
    }

    [Fact]
    public void TwoFeedsDoNotShareTeams()
    {
        var mine = NewFeed();
        var theirs = NewFeed();

        mine.Remember(userId: 7, team: 2);

        Assert.True(mine.TeamOf(7) == 2, "a feed remembers the team it was told");
        Assert.True(theirs.TeamOf(7) == GameEventFeed.NoTeam, "another feed was told nothing");
    }

    [Fact]
    public void EventTeamFilter()
    {
        var feed = NewFeed();

        var registry = Registry(feed);
        registry.KeyValue(Ent, EntityNames.LogicEventlistener, "EventName", "player_death");
        registry.KeyValue(Ent, EntityNames.LogicEventlistener, "IsEnabled", "1");
        registry.KeyValue(Ent, EntityNames.LogicEventlistener, "TeamNum", "2");
        var listener = Spawn<LogicEventListener>(registry, EntityNames.LogicEventlistener);

        // Nobody's side has been learned yet. Failing the filter is the right way
        // to be wrong here: firing would report the wrong team.
        feed.Deliver("player_death", 7);
        Assert.True(listener.OnEventFired.FireCount == 0, "a filter on a team nobody is known to be on does not fire");

        feed.Remember(userId: 7, team: 3);
        feed.Deliver("player_death", 7);
        Assert.True(listener.OnEventFired.FireCount == 0, "and does not fire for the other team");

        feed.Remember(userId: 7, team: 2);
        feed.Deliver("player_death", 7);
        Assert.True(listener.OnEventFired.FireCount == 1, "it fires for its own team");

        // No team key at all means anybody, which is what most maps want.
        var any = new ScriptedEntityRegistry();
        any.RegisterAll(typeof(LogicEventListener).Assembly);
        var anyServices = Services();
        anyServices.AddInstance<IGameEventFeed>(feed);
        any.Services = anyServices;
        var other = new EntityHandle(0x0043_0043u);
        any.KeyValue(other, EntityNames.LogicEventlistener, "EventName", "player_death");
        any.KeyValue(other, EntityNames.LogicEventlistener, "IsEnabled", "1");
        any.Spawn(other, EntityNames.LogicEventlistener);

        feed.Deliver("player_death", 99);
        Assert.True(((LogicEventListener)any.Get(other)!).OnEventFired.FireCount == 1, "a listener with no team filter fires for anybody");
    }

    [Fact]
    public void ItemEquipNeverFires()
    {
        var feed = NewFeed();

        var registry = Registry(feed);
        registry.KeyValue(Ent, EntityNames.LogicEventlistenerItemequip, "IsEnabled", "1");
        registry.KeyValue(Ent, EntityNames.LogicEventlistenerItemequip, "WeaponClassname", "weapon_smg1");
        var listener = Spawn<LogicEventListenerItemEquip>(registry, EntityNames.LogicEventlistenerItemequip);

        Assert.True(listener.IsEnabled, "the item-equip listener shares the base's enable");

        // It listens for one fixed event whatever the map wrote, which is the
        // only thing the subclass decides.
        feed.Deliver("player_death", 7);
        Assert.True(listener.OnEventFired.FireCount == 0, "it does not answer to another event");

        // And item_equip is not among the events this mod declares, so the
        // entity resolves and never reports. See the class comment.
        feed.Deliver("item_equip", 7);
        Assert.True(listener.OnEventFired.FireCount == 1, "it would answer to its own event if one were ever fired");
    }
}
