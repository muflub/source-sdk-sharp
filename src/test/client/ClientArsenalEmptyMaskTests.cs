using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The emptied held weapon's selectability bit rides the player's working copy
/// out of the replay, so the command that spends the last round carries the
/// same EmptyWeapons word the server's frame writes for it
/// (Arsenal.PublishSelection). The server recomputes both masks every frame
/// and its ack for the spend carries that word; the client's ring slot is
/// judged against that arrival. Without the client's own write in the replayed
/// press, the slot stands one command behind and every emptying press
/// mispredicts the word.
/// </summary>
public class ClientArsenalEmptyMaskTests
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

    /// <summary>
    /// The replay over a held pistol with one round and an empty pool: the
    /// basis, the press, and the working-copy player the command's ring save
    /// carries.
    /// </summary>
    private static (ClientArsenal Arsenal, PlayerNetState Working, int PistolIndex)
        PressOnce(WeaponScriptDatabase? scripts = null)
    {
        const float commandClock = 10f;

        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
        engine.Returns("EntityRender.LookupSequenceByActivity", 0);   // the idle anim resolves

        int pistolIndex = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol);

        var weapon = new ClientSsWeapon(engine) { Handle = new EntityHandle(0x0100_0041u) };
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = LocalPlayer;
        weapon.State.WeaponTypeId = pistolIndex;
        weapon.State.Clip = 1;                    // one round: the press spends it
        weapon.State.Reserve = 0;                 // no pool to reload from
        weapon.State.NextPrimaryAttack = commandClock;   // due NOW, held

        var viewModel = new ClientSsViewModel(engine, engine) { Handle = new EntityHandle(0x0100_0042u) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = LocalPlayer;

        var pair = new Pair();
        pair.Directory.Bind(weapon.Handle, -1, weapon);
        pair.Directory.Bind(viewModel.Handle, -1, viewModel);

        var arsenal = new ClientArsenal(new RecordingLog(), pair, engine, engine, engine,
                                        NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()),
                                        scripts: scripts);
        var player = new EntityHandle(0x0001_0000u | LocalPlayer);
        var working = new PlayerNetState();
        working.BindLocal();          // the working copy is the client's own buffer
        arsenal.WorkingPlayer = working;

        // Phase 2: the replay's basis reset to the decoded word.
        var basis = new UserCmd { CommandNumber = 10 };
        arsenal.OnPlayerCommand(player, in basis, 2, false);

        // Phase 1: the press that fires the held pistol's last round.
        var cmd = new UserCmd { CommandNumber = 11, Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(player, in cmd, 1, true, commandClock);

        return (arsenal, working, pistolIndex);
    }

    /// <summary>
    /// An emptied held weapon is unselectable (Arsenal.CanBeSelected): the
    /// press that spends the last round of a clip with nothing behind it must
    /// raise the held weapon's bit in the working copy's EmptyMask, as the
    /// server's frame writes it for the acked command. Bits are by WeaponTypes
    /// index, so the expected word is one shift over the row's index.
    /// </summary>
    [Fact]
    public void ThePressThatEmptiesTheHeldWeaponSetsItsEmptyBitInTheReplay()
    {
        var (arsenal, working, pistolIndex) = PressOnce();

        Assert.Equal(0, arsenal.WorkingWeapon.Clip);     // the press spent it

        Assert.Equal((UInt128)(1u << pistolIndex), working.EmptyMask);
    }

    /// <summary>
    /// The same press on a pistol whose pool still feeds the reload keeps the
    /// bit down: held-and-reloadable is selectable, so the replay answers what
    /// the server's PublishSelection writes for the same command.
    /// </summary>
    [Fact]
    public void ThePressWithAPoolLeftKeepsTheBitDownInTheReplay()
    {
        const float commandClock = 10f;

        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
        engine.Returns("EntityRender.LookupSequenceByActivity", 0);

        int pistolIndex = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol);

        var weapon = new ClientSsWeapon(engine) { Handle = new EntityHandle(0x0100_0041u) };
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = LocalPlayer;
        weapon.State.WeaponTypeId = pistolIndex;
        weapon.State.Clip = 1;
        weapon.State.Reserve = 4;         // the reload still has something to take
        weapon.State.NextPrimaryAttack = commandClock;

        var viewModel = new ClientSsViewModel(engine, engine) { Handle = new EntityHandle(0x0100_0042u) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = LocalPlayer;

        var pair = new Pair();
        pair.Directory.Bind(weapon.Handle, -1, weapon);
        pair.Directory.Bind(viewModel.Handle, -1, viewModel);

        var arsenal = new ClientArsenal(new RecordingLog(), pair, engine, engine, engine,
                                        NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()));
        var player = new EntityHandle(0x0001_0000u | LocalPlayer);
        var working = new PlayerNetState();
        working.BindLocal();          // the working copy is the client's own buffer
        arsenal.WorkingPlayer = working;

        var basis = new UserCmd { CommandNumber = 10 };
        arsenal.OnPlayerCommand(player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11, Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(player, in cmd, 1, true, commandClock);

        Assert.Equal(UInt128.Zero, working.EmptyMask);
    }

    /// <summary>
    /// The script's SelectOnEmpty flag outranks the ammo arms (stock's
    /// CBaseCombatWeapon::CanBeSelected): a row named for the flag answers
    /// selectable even empty, and the replay must answer it from the SAME
    /// script reads the server's PublishSelection makes. The gate is the row's
    /// ITEM_FLAG_SELECTONEMPTY, parsed by WeaponScriptParser.
    /// </summary>
    [Fact]
    public void TheScriptsSelectOnEmptyFlagKeepsTheBitDownWhenEvenThePoolIsGone()
    {
        const float commandClock = 10f;

        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
        engine.Returns("EntityRender.LookupSequenceByActivity", 0);

        int pistolIndex = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol);

        var scripts = new WeaponScriptDatabase(new RecordingLog());
        scripts.AddFromText("weapon_pistol",
                             "WeaponData\n{\n\"ITEM_FLAG_SELECTONEMPTY\" \"1\"\n}\n");

        var weapon = new ClientSsWeapon(engine) { Handle = new EntityHandle(0x0100_0041u) };
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = LocalPlayer;
        weapon.State.WeaponTypeId = pistolIndex;
        weapon.State.Clip = 1;            // clip and pool both empty after the press
        weapon.State.Reserve = 0;
        weapon.State.NextPrimaryAttack = commandClock;

        var viewModel = new ClientSsViewModel(engine, engine) { Handle = new EntityHandle(0x0100_0042u) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = LocalPlayer;

        var pair = new Pair();
        pair.Directory.Bind(weapon.Handle, -1, weapon);
        pair.Directory.Bind(viewModel.Handle, -1, viewModel);

        var arsenal = new ClientArsenal(new RecordingLog(), pair, engine, engine, engine,
                                        NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()),
                                        scripts: scripts);
        var player = new EntityHandle(0x0001_0000u | LocalPlayer);
        var working = new PlayerNetState();
        working.BindLocal();          // the working copy is the client's own buffer
        arsenal.WorkingPlayer = working;

        var basis = new UserCmd { CommandNumber = 10 };
        arsenal.OnPlayerCommand(player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11, Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(player, in cmd, 1, true, commandClock);

        Assert.Equal(0, arsenal.WorkingWeapon.Clip);     // spent, and nothing behind it

        Assert.Equal(UInt128.Zero, working.EmptyMask);
    }
}
