using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The client's weapon machine runs each replayed command at that command's
/// own clock, as the server ran it. It read the engine's curtime instead --
/// the FRAME's, shared by every command the frame replays -- and every timer
/// it set (TimeWeaponIdle on each idle re-send) mispredicted by a tick.
/// </summary>
public class ClientArsenalClockTests
{
    private const int LocalPlayer = 1;

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

    [Fact]
    public void AReplayedCommandRunsAtItsOwnClockNotTheFrames()
    {
        const float frameClock = 5000f;
        const float commandClock = 10f;

        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
        engine.Returns("Clock.Now", frameClock);
        engine.Returns("EntityRender.LookupSequenceByActivity", 0);   // the idle anim resolves

        var weapon = new ClientSsWeapon(engine) { Handle = new EntityHandle(0x0100_0041u) };
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = LocalPlayer;
        weapon.State.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol);
        weapon.State.Clip = 12;
        weapon.State.TimeWeaponIdle = 0f;   // due: the idle re-send fires

        var viewModel = new ClientSsViewModel(engine, engine) { Handle = new EntityHandle(0x0100_0042u) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = LocalPlayer;

        var pair = new Pair();
        pair.Directory.Bind(weapon.Handle, -1, weapon);
        pair.Directory.Bind(viewModel.Handle, -1, viewModel);

        var arsenal = new ClientArsenal(new RecordingLog(), pair, engine, engine, engine,
                                        new NetStateCatalog(TestServices.ClientGame, new RecordingLog()));
        var player = new EntityHandle(0x0001_0000u | LocalPlayer);

        var basis = new UserCmd { CommandNumber = 10 };
        arsenal.OnPlayerCommand(player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11 };
        arsenal.OnPlayerCommand(player, in cmd, 1, true, commandClock);

        float idle = arsenal.WorkingWeapon.TimeWeaponIdle;

        Assert.True(idle > commandClock && idle < commandClock + 60f,
            $"the next idle is scheduled from the command's clock {commandClock}, " +
            $"not the frame's {frameClock}: {idle}");
    }
}
