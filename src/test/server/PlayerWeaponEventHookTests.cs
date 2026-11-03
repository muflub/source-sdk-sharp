using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="IPlayerWeaponEventHook"/> (plan_hl2mp_ai §4.5): the arsenal
/// tells the hooks what a client watching a player would be shown of its
/// weapon, once per new command: a shot, a reload's start and end.
/// </summary>
public sealed class PlayerWeaponEventHookTests
{
    private static EntityHandle Player => new(0x0001_0001);

    private sealed class ListHooks<T>(params T[] all) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => all;

        public void Each(string what, Action<T> call)
        {
            foreach (T h in all)
                call(h);
        }
    }

    private sealed class Seen : IPlayerWeaponEventHook
    {
        public readonly List<(PlayerWeaponEvent Event, int Count)> All = new();

        public void OnWeaponEvent(EntityHandle player, int weaponType, PlayerWeaponEvent e, int count) => All.Add((e, count));
    }

    private static (Arsenal Arsenal, RecordingEngine Engine, ScriptedEntityRegistry Registry, Seen Seen) Armed()
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", Vector.Zero)
            .Returns("Entities.GetEyeAngles", Vector.Zero)
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("Clock.Now", 10f);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out var entityEngine);
        entityEngine.Strict = false;
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");
        var seen = new Seen();
        var arsenal = new Arsenal(new RecordingLog(), new WeaponSwitchTests.SpawningEntities(engine, registry), engine, engine, engine,
                                  engine, engine, engine, engine, registry, engine, new DamageHitGroups(),
                                  weaponEvents: new ListHooks<IPlayerWeaponEventHook>(seen));
        Assert.True(arsenal.GiveWeapon(Player, EntityNames.WeaponSmg1));
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;
        Assert.Contains(arsenal.Switch(Player, EntityNames.WeaponSmg1), (SwitchOutcome[])[SwitchOutcome.Switched, SwitchOutcome.AlreadyHeld]);
        return (arsenal, engine, registry, seen);
    }

    private static void Command(Arsenal a, RecordingEngine e, float now, InputButtons buttons, int number, bool firstTime = true)
    {
        e.Returns("Clock.Now", now);
        var cmd = new UserCmd { CommandNumber = number, Buttons = (int)buttons };
        a.OnPlayerCommand(Player, in cmd, firstTime);
    }

    [Fact]
    public void AShotIsToldOnceAsTheRoundItSpent()
    {
        var (arsenal, engine, _, seen) = Armed();
        Command(arsenal, engine, 30f, InputButtons.Attack, 1);
        Command(arsenal, engine, 30f, InputButtons.Attack, 1, firstTime: false);
        Assert.Equal([(PlayerWeaponEvent.Fired, 1)], seen.All);
    }

    [Fact]
    public void AReloadIsToldWhenItStartsAndWhenItEnds()
    {
        var (arsenal, engine, registry, seen) = Armed();
        WeaponNetState w = ((SsWeapon)registry.Get(arsenal.CarriedPair(Player).Weapon)!).State;
        w.Clip = 10;
        w.Reserve = 100;
        arsenal.CarryFor(Player).Ammo["SMG1"] = 100;
        Command(arsenal, engine, 30f, InputButtons.Reload, 1);
        Command(arsenal, engine, 40f, (InputButtons)0, 2);
        Assert.Equal([(PlayerWeaponEvent.ReloadStarted, 0), (PlayerWeaponEvent.ReloadFinished, 45)], seen.All);
    }
}
