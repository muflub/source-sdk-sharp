extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientBell = client::TF2.Client.Weapons.TFSniperRifleBell;
using ClientSwitch = client::TF2.Client.TFSniperFullChargeBellConVar;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientWeapon = client::SourceSharp.Client.Standalone.ClientSsWeapon;
using ClientPlayer = client::SourceSharp.Client.Standalone.ClientSsPlayer;
using ClientStandalones = client::SourceSharp.Client.IClientStandalones;
using ClientEffects = client::SourceSharp.Client.WeaponEffects;

namespace TF2.Tests.Weapons;

/// <summary>
/// The full charge's bell (the client charge arms, tf_weapon_sniperrifle.cpp:386-396,
/// :1931-1941): one <c>TFPlayer.ReCharged</c> at the local player's ears the frame
/// the charge tops <c>TF_WEAPON_SNIPERRIFLE_DAMAGE_MAX</c>, once per charge, and
/// only when <c>tf_sniper_fullcharge_bell</c> asks for it.
/// </summary>
public class TFSniperRifleBellFacts
{
    private const int Local = 7;

    [Fact]
    public void A_full_charge_rings_once()
    {
        var rig = Rig("1");
        rig.Charged(10f);
        rig.Frame(10f);

        var ring = rig.Engine.Last("Sound.EntityEmitSound");
        Assert.Equal(rig.Player.Handle, ring!.Value.ArgAs<EntityHandle>(0));
        Assert.Equal("TFPlayer.ReCharged", ring!.Value.ArgAs<string>(1));

        rig.Frame(10.1f);
        Assert.Equal(1, rig.Engine.CountOf("Sound.EntityEmitSound"));
    }

    [Fact]
    public void A_new_charge_rings_again()
    {
        var rig = Rig("1");
        rig.Charged(10f);
        rig.Frame(10f);

        // The shot zeroes the charge, a frame sees it, and the next tops again.
        rig.Weapon.State.BehaviourMeter = 0f;
        rig.Frame(11f);
        rig.Charged(14f);
        rig.Frame(14f);

        Assert.Equal(2, rig.Engine.CountOf("Sound.EntityEmitSound"));
    }

    [Fact]
    public void An_unfinished_charge_is_silent()
    {
        var rig = Rig("1");
        rig.Charged(10f, held: 2f);
        rig.Frame(10f);

        Assert.Equal(0, rig.Engine.CountOf("Sound.EntityEmitSound"));
    }

    [Fact]
    public void The_switch_muted_is_silent()
    {
        var rig = Rig("0");
        rig.Charged(10f);

        rig.Frame(10f);
        rig.Frame(10.1f);

        Assert.Equal(0, rig.Engine.CountOf("Sound.EntityEmitSound"));
    }

    [Fact]
    public void Somebody_elses_rifle_is_never_rung()
    {
        var rig = Rig("1", local: 1);
        rig.Weapon.State.OwnerIndex = 2;
        rig.Charged(10f);
        rig.Frame(10f);

        Assert.Equal(0, rig.Engine.CountOf("Sound.EntityEmitSound"));
    }

    // ---------------------------------------------------------------------

    private sealed class Standalones : ClientStandalones
    {
        public readonly Dictionary<int, ClientPlayer> ByIndex = [];
        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => ByIndex.Values.ToList();
        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => ByIndex.TryGetValue(entityIndex, out var entity) ? entity : null;
    }

    private static BellRig Rig(string switchDefault, int local = Local)
    {
        var engine = new ClientEngine();
        engine.Returns("Hud.LocalPlayerIndex", local);

        var player = new ClientPlayer(engine) { Handle = new EntityHandle(0x0001_0007u) };
        var weapon = new ClientWeapon(engine) { Handle = new EntityHandle(0x0002_0008u) };
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = Local;

        var switchConVar = new ClientSwitch();
        switchConVar.Bind(-1, "tf_sniper_fullcharge_bell", switchDefault);

        var standalones = new Standalones { ByIndex = { [Local] = player } };
        return new BellRig(engine, weapon, player,
                       new ClientBell(switchConVar, engine, standalones, engine));
    }

    private sealed class BellRig(ClientEngine engine, ClientWeapon weapon, ClientPlayer player, ClientBell bell)
    {
        public readonly ClientEngine Engine = engine;
        public readonly ClientWeapon Weapon = weapon;
        public readonly ClientPlayer Player = player;
        private readonly ClientBell _bell = bell;

        /// <summary>The charge begun <paramref name="held"/> before <paramref name="now"/>; the default is a full one.</summary>
        public void Charged(float now, float held = 3.5f) => Weapon.State.BehaviourMeter = now - held;

        public void Frame(float now) =>
            _bell.Think(new ClientEffects.WeaponDrawContext(Weapon, null, FirstPerson: false, Now: now));
    }
}
