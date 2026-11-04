/*
 * The carried pair's third kind: a row's attached models (stock's
 * attachedmodel_t — the flamethrower's pilot light), which the deploy dresses
 * beside the weapon and the viewmodel. The row is the whole of the input, so
 * these facts hand the Arsenal a catalog of one; WeaponSwitchTests's world,
 * shared for the same reason its death half shares it.
 */

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

public class AttachedModelTests
{
    private const string Flare = "weapon_flare_test";

    private static WeaponCatalog FlareRow() => new(() => [new RowSource(new WeaponType(Flare, 0.5f)
    {
        ViewModel = "models/weapons/v_flare.mdl",
        WorldModel = "models/weapons/w_flare.mdl",
        AttachedModels =
        [
            new WeaponAttachedModel("models/weapons/w_flare_pilot.mdl", AttachedModelDisplayFlags.MaskAll),
        ],
    })]);

    private sealed class RowSource(params WeaponType[] rows) : IWeaponTypeSource
    {
        public IEnumerable<WeaponType> Rows() => rows;
    }

    private static (Arsenal, RecordingEngine, WeaponSwitchTests.SpawningEntities) FlareWorld(out ScriptedEntityRegistry registry)
    {
        var (arsenal, engine, world) = WeaponSwitchTests.World(out registry, viewmodelModel: null, weapons: FlareRow());

        // Real, distinct indices: the state's entity references are the point.
        world.IndexOf = h => (int)(h.Value & 0xFFFF);

        return (arsenal, engine, world);
    }

    private static bool Shown(RecordingEngine engine, EntityHandle handle, bool shown)
        => engine.Calls.Any(c => c.Member == "EntityRender.SetVisible" &&
                                 c.ArgAs<EntityHandle>(0) == handle &&
                                 c.ArgAs<bool>(1) == shown);

    private static string? Model(RecordingEngine engine, EntityHandle handle)
        => engine.Calls.Where(c => c.Member == "EntityRender.SetModel" && c.ArgAs<EntityHandle>(0) == handle)
                       .Select(c => c.ArgAs<string>(1))
                       .LastOrDefault();

    [Fact]
    public void ADeployDressesTheRowsAttachedModelOnTheBearer()
    {
        var (arsenal, engine, world) = FlareWorld(out var registry);

        Assert.True(arsenal.GiveWeapon(WeaponSwitchTests.Player, Flare), "the flare is taken and deploys");

        var handle = Assert.Single(world.AttachedModelHandles);

        var state = Assert.IsType<SsAttachedModel>(registry.Get(handle)).State;

        Assert.Equal("models/weapons/w_flare_pilot.mdl", Model(engine, handle));
        Assert.Equal((int)(WeaponSwitchTests.Player.Value & 0xFFFF), state.OwnerIndex);
        Assert.Equal((int)AttachedModelDisplayFlags.MaskAll, state.Flags);
        Assert.True(Shown(engine, handle, shown: true), "drawn, on the bearer's bones");
        Assert.True(engine.Calls.Any(c => c.Member == "EntityRender.SetTransmitAlways" &&
                                         c.ArgAs<EntityHandle>(0) == handle),
            "and transmitted like the pair beside it: the bones are someone else's otherwise");
    }

    [Fact]
    public void ARowWithoutAttachedModelsCreatesNone()
    {
        var (arsenal, _, world) = WeaponSwitchTests.World(out _);

        Assert.True(arsenal.GiveWeapon(WeaponSwitchTests.Player, "weapon_crowbar"), "the crowbar is taken");

        Assert.Empty(world.AttachedModelHandles);
        Assert.DoesNotContain(EntityNames.SsAttachedModel, world.CreatedClasses);
    }

    [Fact]
    public void ASwitchAwayHidesTheModelTheNewRowDoesNotName()
    {
        var (arsenal, engine, world) = FlareWorld(out _);

        Assert.True(arsenal.GiveWeapon(WeaponSwitchTests.Player, Flare));
        Assert.True(arsenal.GiveWeapon(WeaponSwitchTests.Player, "weapon_crowbar"),
            "a second weapon is taken and waits");
        Assert.True(arsenal.Switch(WeaponSwitchTests.Player, "weapon_crowbar") == SwitchOutcome.Switched);

        var handle = Assert.Single(world.AttachedModelHandles);

        Assert.True(Shown(engine, handle, shown: false),
            "the crowbar names no attached model: the flare's pilot light goes dark, not away");
    }

    [Fact]
    public void ADeathTakesTheModelWithTheHands()
    {
        var (arsenal, engine, world) = FlareWorld(out _);

        Assert.True(arsenal.GiveWeapon(WeaponSwitchTests.Player, Flare));

        var handle = Assert.Single(world.AttachedModelHandles);
        Assert.True(Shown(engine, handle, shown: true), "the rig: shown while alive");

        arsenal.Strip(WeaponSwitchTests.Player);

        Assert.True(Shown(engine, handle, shown: false), "dead hands carry no flame");
    }
}
