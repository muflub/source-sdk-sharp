/*
 * The HUD's ammo feed under the standalone player.
 *
 * The defect: the ammo readout read C_BaseCombatWeapon through the native
 * LocalWeapon* asks, and that class is out of both builds — so the HUD drew
 * no weapon and no ammo, silently. The feed is now the carried weapon's own
 * networked state, through ClientArsenal (ILocalArsenal). What can regress is
 * whose weapon is read, what an empty hand feeds, and whether the HUD's
 * readout actually consumes the feed rather than the dead native path.
 */

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class HudAmmoFeedTests
{
    private const int LocalPlayer = 1;
    private const int SomebodyElse = 2;

    /// <summary>The pistol's row, because the test names it by classname.</summary>
    private static readonly int Pistol = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol);

    private sealed class Standalones : IClientStandalones
    {
        public readonly List<ClientStandaloneEntity> Entities = new();
        public IReadOnlyList<ClientStandaloneEntity> Live() => Entities;

        // Row C4's index lookup. These doubles are about animation and the HUD,
        // not about what a gib hit, so they answer "not one of ours" for every
        // index -- which is the honest answer for a table nothing was put in.
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private static ClientSsWeapon Weapon(RecordingEngine engine, int owner, int typeId,
                                         int clip, int reserve)
    {
        var weapon = new ClientSsWeapon(engine)
        {
            Handle = new EntityHandle(0x0100_0041u),
        };

        weapon.State.BindLocal();
        weapon.State.OwnerIndex = owner;
        weapon.State.WeaponTypeId = typeId;
        weapon.State.Clip = clip;
        weapon.State.Reserve = reserve;

        return weapon;
    }

    private static ClientArsenal Arsenal(RecordingEngine engine, Standalones standalones)
        => new(new RecordingLog(), standalones, engine, engine, engine, NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()));

    //-------------------------------------------------------------------------
    // The feed itself.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheCarriedWeaponFeedsTheHud()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var standalones = new Standalones();
        standalones.Entities.Add(Weapon(engine, LocalPlayer, Pistol, clip: 12, reserve: 30));

        var arsenal = Arsenal(engine, standalones);

        Assert.True(arsenal.TryGetActive(out string name, out int clip, out int reserve),
            "an armed local player has a weapon to print");
        Assert.True(name == EntityNames.WeaponPistol, "named by its classname, through the shared table");
        Assert.True(clip == 12, "the clip is the networked clip");
        Assert.True(reserve == 30, "and the reserve is the networked pool behind it");
    }

    [Fact]
    public void AWeaponThatSpendsNoAmmunitionHasNoReserve()
    {
        // The crowbar networks a reserve of 0, which the HUD drew as an empty
        // pool (plan_client_ui U2's first live frame); it spends nothing, so
        // there is no pool to show.
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var standalones = new Standalones();
        standalones.Entities.Add(Weapon(engine, LocalPlayer, WeaponCatalog.Stock().IndexOf(EntityNames.WeaponCrowbar), clip: -1, reserve: 0));

        Assert.True(Arsenal(engine, standalones).TryGetActive(out _, out int clip, out int reserve));
        Assert.Equal((-1, -1), (clip, reserve));
    }

    [Fact]
    public void SomebodyElsesWeaponIsNotMine()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var standalones = new Standalones();
        standalones.Entities.Add(Weapon(engine, SomebodyElse, Pistol, clip: 12, reserve: 30));

        Assert.True(!Arsenal(engine, standalones).TryGetActive(out _, out _, out _),
            "another player's carried weapon feeds their HUD, not this one");
    }

    [Fact]
    public void EmptyHandsFeedNothing()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var standalones = new Standalones();
        standalones.Entities.Add(Weapon(engine, LocalPlayer, typeId: -1, clip: -1, reserve: -1));

        Assert.True(!Arsenal(engine, standalones).TryGetActive(out _, out _, out _),
            "WeaponTypeId -1 is empty-handed — nothing to print rather than a zeroed readout");
    }

    [Fact]
    public void NoLocalPlayerFeedsNothing()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", 0);

        var standalones = new Standalones();
        standalones.Entities.Add(Weapon(engine, LocalPlayer, Pistol, clip: 12, reserve: 30));

        Assert.True(!Arsenal(engine, standalones).TryGetActive(out _, out _, out _),
            "in the menu or between levels there is no viewer to feed");
    }

    [Fact]
    public void ASwitchMovesTheIdentityNotTheEntity()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var standalones = new Standalones();
        var weapon = Weapon(engine, LocalPlayer, Pistol, clip: 12, reserve: 30);
        standalones.Entities.Add(weapon);

        var arsenal = Arsenal(engine, standalones);
        arsenal.TryGetActive(out string before, out _, out _);

        // The switch, as the server performs it: same entity, new identity.
        weapon.State.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponCrowbar);
        weapon.State.Clip = -1;

        Assert.True(arsenal.TryGetActive(out string after, out int clip, out _),
            "the same entity still feeds after a switch");
        Assert.True(before == EntityNames.WeaponPistol && after == EntityNames.WeaponCrowbar,
            "and the name followed the identity");
        Assert.True(clip == -1, "a crowbar has no clip, and says so rather than saying zero");
    }

    //-------------------------------------------------------------------------
    // The readout consumes the feed. The engine double is STRICT: if the HUD
    // fell back to the dead native LocalWeapon* asks while the arsenal had a
    // weapon in hand, the unstubbed ask would fail this test by itself.
    //-------------------------------------------------------------------------

    private sealed class FedArsenal : ILocalArsenal
    {
        public bool TryGetActive(out string name, out int clip, out int reserve)
        {
            name = EntityNames.WeaponPistol;
            clip = 12;
            reserve = 30;
            return true;
        }
    }

    private sealed class EmptyArsenal : ILocalArsenal
    {
        public bool TryGetActive(out string name, out int clip, out int reserve)
        {
            name = "";
            clip = -1;
            reserve = -1;
            return false;
        }
    }

    /// <summary>The UI's view of the local player (plan_client_ui U2), fed by <paramref name="arsenal"/>.</summary>
    private static SourceSharp.Client.Vgui.LocalPlayerUi Player(RecordingEngine engine, ILocalArsenal arsenal)
    {
        engine.Returns("Hud.LocalPlayerHealth", 100);
        var hud = new Hud(new RecordingLog(), engine, engine, new DamageMessage(), new HudMsgMessage(),
                          new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
        var player = new SourceSharp.Client.Vgui.LocalPlayerUi(engine, arsenal, hud);
        player.Refresh();
        return player;
    }

    [Fact]
    public void TheHudReadsTheFedAmmo()
    {
        var player = Player(Fake(), new FedArsenal());

        Assert.True((player.Clip, player.Reserve) == (12, 30), "the ammo panel reads the fed clip and reserve");
        Assert.True(player.HasWeapon && player.WeaponName == EntityNames.WeaponPistol,
            "and the carried weapon's name");
    }

    /// <summary>
    /// With nothing fed, the ammo panel has no weapon (it draws nothing) and no
    /// ammunition; and nothing native is asked for one. The positive control is
    /// the fact above, which reads a fed readout through the same view.
    /// </summary>
    [Fact]
    public void AnEmptyFeedHasNoWeaponAndAsksNothingNative()
    {
        var engine = Fake();
        var player = Player(engine, new EmptyArsenal());

        Assert.True(!player.HasWeapon && (player.Clip, player.Reserve) == (-1, -1),
            "with nothing fed, there is no weapon and no ammunition");

        foreach (var member in new[]
                 {
                     "Hud.LocalWeaponCount", "Hud.LocalActiveWeapon", "Hud.LocalWeaponName",
                     "Hud.LocalWeaponClip", "Hud.LocalWeaponReserve",
                 })
        {
            Assert.True(engine.CountOf(member) == 0,
                member + " is asked again. Its ABI slot is retired and bound to NULL, so "
                       + "the answer is a default the HUD cannot tell from a real reading.");
        }
    }
}
