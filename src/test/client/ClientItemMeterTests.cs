using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The replayed switch's item meter: the mirror's deploy advances the
/// published pair by the behaviour's seconds exactly as the server's deploy
/// does (Arsenal.RestoreItemMeter, the same question
/// <see cref="IWeaponBehaviour.ItemMeterSeconds"/> answers), so the judged
/// bytes are the switch's and not a misprediction the compare eats. The pair
/// is the side's own word — the stub publisher here stands where the
/// carrier's mirror stands in the game.
/// </summary>
public class ClientItemMeterTests
{
    private const int LocalPlayer = 1;

    private const string BehaviourKey = "metered";

    private const string MeteredName = "weapon_metered";

    private static readonly WeaponType MeteredRow =
        WeaponCatalog.Stock().For(EntityNames.WeaponPistol)!.Value with { Name = MeteredName, Behaviour = BehaviourKey };

    private sealed class RowSource : IWeaponTypeSource
    {
        public IEnumerable<WeaponType> Rows() => [MeteredRow];
    }

    private sealed class BehaviourSource(IWeaponBehaviour behaviour) : IWeaponBehaviourSource
    {
        public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours()
            => [KeyValuePair.Create<string, IWeaponBehaviour>(BehaviourKey, behaviour)];
    }

    private sealed class Metered(float seconds) : IWeaponBehaviour
    {
        public float ItemMeterSeconds(in WeaponType type, int ownerIndex) => seconds;
    }

    private sealed class Published(float meter, float stamp) : ILocalItemMeters
    {
        public bool MeterOf(int typeIndex, out float pairMeter, out float pairStamp)
        {
            pairMeter = meter;
            pairStamp = stamp;
            return true;
        }
    }

    private sealed class Pair : IClientStandalones
    {
        public readonly ClientEntityDirectory Directory = new();

        public IReadOnlyList<ClientStandaloneEntity> Live()
            => throw new InvalidOperationException("the arsenal finds its pair by class");

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => Directory.ByIndex(entityIndex);

        public IEnumerable<ClientStandaloneEntity> OfClass(string? className)
            => Directory.OfClass(className);

        public IEnumerable<ClientStandaloneEntity> OfName(string? name)
            => Directory.OfName(name);
    }

    private static ClientArsenal Rig(ILocalItemMeters? published, float seconds)
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
        engine.Returns("Clock.Now", 5000f);
        engine.Returns("EntityRender.LookupSequenceByActivity", 0);   // the draw resolves

        var weapons = new WeaponCatalog(() => [new RowSource()], new RecordingLog());
        var behaviours = new WeaponBehaviours(() => [new BehaviourSource(new Metered(seconds))], new RecordingLog());

        int pistol = weapons.IndexOf(EntityNames.WeaponPistol);
        int metered = weapons.IndexOf(MeteredName);

        var weapon = new ClientSsWeapon(engine) { Handle = new EntityHandle(0x0100_0041u) };
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = LocalPlayer;
        weapon.State.WeaponTypeId = pistol;
        weapon.State.ItemMeter = 0.25f;

        var viewModel = new ClientSsViewModel(engine, engine) { Handle = new EntityHandle(0x0100_0042u) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = LocalPlayer;

        var pair = new Pair();
        pair.Directory.Bind(weapon.Handle, -1, weapon);
        pair.Directory.Bind(viewModel.Handle, -1, viewModel);

        var arsenal = new ClientArsenal(new RecordingLog(), pair, engine, engine, engine,
                                        NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()),
                                        weapons: weapons, behaviours: behaviours, localItemMeters: published);
        var player = new EntityHandle(0x0001_0000u | LocalPlayer);

        var basis = new UserCmd { CommandNumber = 10 };
        arsenal.OnPlayerCommand(player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11, WeaponSelect = metered + 1 };
        arsenal.OnPlayerCommand(player, in cmd, 1, true, 10f);
        return arsenal;
    }

    [Fact]
    public void TheSwitchRestoresThePublishedPairAtTheServersArithmetic()
    {
        // 0.25 held aside, published two seconds before the command, fifty
        // to the charge: the server's restore writes 0.25 + 6/50, the mirror
        // the same byte.
        var arsenal = Rig(new Published(0.25f, 4f), seconds: 50f);

        Assert.Equal(0.37f, arsenal.WorkingWeapon.ItemMeter, 4);
        Assert.Equal(10f, arsenal.WorkingWeapon.ItemMeterTime, 4);
    }

    [Fact]
    public void NoPublishedPairLeavesTheMeterTheHandsHold()
    {
        var arsenal = Rig(published: null, seconds: 50f);

        // The stub behaviour answers, the side publishes nothing: the row's
        // restore writes nothing, the meter the last deploy left stands.
        Assert.Equal(0.25f, arsenal.WorkingWeapon.ItemMeter, 4);
    }
}
