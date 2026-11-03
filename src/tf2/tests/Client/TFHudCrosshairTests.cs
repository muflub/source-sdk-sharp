extern alias client;

using System.Text;
using System.Text.RegularExpressions;

using TF2.Tests;
using TF2.Tests.Weapons;

using Xunit;

using Adapter = client::TF2.Client.Ui.TFHudCrosshair;
using Arsenal = client::SourceSharp.Client.ILocalArsenal;
using BlueConVar = client::TF2.Client.ClCrosshairBlueConVar;
using ClientClass = client::TF2.Shared.TFClass;
using ClientCond = client::TF2.Shared.Conditions.TFConditions;
using ClientFiles = client::SourceSharp.Abi.IEngineFiles;
using ClientLog = client::SourceSharp.Abi.RecordingLog;
using ClientScripts = client::TF2.Shared.TFWeaponScripts;
using ClientSsWeapon = client::SourceSharp.Client.Standalone.ClientSsWeapon;
using Clock = client::SourceSharp.Abi.IGameClock;
using Econ = client::TF2.Shared.Econ;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using FileConVar = client::TF2.Client.ClCrosshairFileConVar;
using GreenConVar = client::TF2.Client.ClCrosshairGreenConVar;
using OwnersContract = client::TF2.Shared.ITFWeaponOwners;
using RedConVar = client::TF2.Client.ClCrosshairRedConVar;
using ScaleConVar = client::TF2.Client.ClCrosshairScaleConVar;
using StandaloneEntity = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using Standalones = client::SourceSharp.Client.IClientStandalones;
using StatesContract = client::SourceSharp.Client.WeaponEffects.IWeaponDrawStates;
using WState = client::SourceSharp.Shared.WeaponNetState;

// The contract (ITFCrosshair) is unaliased: it is global, as the damage
// history's is.

namespace TF2.Tests.Client;

/// <summary>
/// The crosshair's per-weapon and per-player reads. Whether a crosshair draws
/// is stock's <c>ShouldDrawCrosshair</c> (tf_weaponbase.cpp:3638-3648): an
/// empty <c>cl_crosshair_file</c> follows the script's <c>DrawCrosshair</c>
/// word (read at tf_weapon_parse.cpp:78) and empty hands draw nothing (stock's
/// base <c>CHudCrosshair</c> path with no weapon); a custom file draws however
/// the script answers (the "return true" arm, :3646-3647). The scale is
/// <c>GetWeaponCrosshairScale</c> times the player's
/// <c>cl_crosshair_scale</c>/32 (Paint's <c>flWeaponScale × flPlayerScale</c>,
/// tf_hud_crosshair.cpp:189-216): the base 1.0
/// (basecombatweapon_shared.h:531) for every weapon but the revolver, whose
/// headshot mode (<c>set_weapon_mode</c> 1 on its item, CanHeadshot,
/// tf_weapon_revolver.h:49) blooms the crosshair off the age of the accuracy
/// clock <c>PrimaryAttack</c> stamps (tf_weapon_revolver.cpp:157) —
/// <c>RemapValClamped</c> from (1.0, 0.5) to (0.75, 2.5), :207-223 — the clock
/// read off the carried weapon's state, the replay's working copy while it is
/// fresh, at the clock's <c>Now</c>. The material is Paint's
/// <c>vgui/crosshairs/&lt;file&gt;</c> path (:153-154); the colour is the
/// three <c>cl_crosshair_*</c> ints (:200). The convars are held at stock's
/// declared defaults by the rig, whose strings arrive the way the engine's
/// change callback delivers them.
/// </summary>
public sealed class TFHudCrosshairTests
{
    private sealed class RigArsenal : Arsenal
    {
        public string? Weapon;

        public bool TryGetActive(out string name, out int clip, out int reserve)
        {
            name = Weapon ?? "";
            clip = -1;
            reserve = 0;
            return Weapon is not null;
        }
    }

    /// <summary>The shipped pack, served as the client's files (the prediction rig's Served).</summary>
    private sealed class Served(TFClassTableTests.Files files) : ClientFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
    }

    /// <summary>The local player's carried weapon — the core holds one per
    /// armed player — with the accuracy clock stock's shot stamps.</summary>
    private sealed class GunStands : Standalones
    {
        private readonly Dictionary<int, ClientSsWeapon> _guns = [];

        public ClientSsWeapon Gun(int ownerIndex, float accuracyClock)
        {
            var gun = new ClientSsWeapon(new Engine());
            gun.State.BindLocal();
            gun.State.OwnerIndex = ownerIndex;
            gun.State.BehaviourKeptTime = accuracyClock; // the behaviour's kept clock (stock's m_flLastAccuracyCheck)
            _guns[ownerIndex] = gun;
            return gun;
        }

        public IReadOnlyList<StandaloneEntity> Live()
            => _guns.Values.Cast<StandaloneEntity>().ToList();

        public StandaloneEntity? GetByIndex(int entityIndex) => null;
        public IEnumerable<StandaloneEntity> OfClass(string? className) => [];
        public IEnumerable<StandaloneEntity> OfName(string? name) => [];
    }

    /// <summary>The replay's working copy of the carried weapon while it is
    /// fresh: the clock the ring last wrote, not the decoded state's.</summary>
    private sealed class StatesDouble : StatesContract
    {
        public float? WorkingClock;

        public WState? PredictedStateOf(ClientSsWeapon weapon)
        {
            if (WorkingClock is not { } clock)
                return null;
            var copy = new WState();
            copy.BindLocal();
            copy.BehaviourKeptTime = clock;
            return copy;
        }
    }

    private sealed class HudClock : Clock
    {
        public float Now { get; set; }
    }

    /// <summary>The local player's weapon item as a hand-built container (the
    /// damage rig's <c>AttackerItem</c> shape over the client's own Econ
    /// types): the client's real answer walks the schema's statics by the
    /// carrier's defindex, proven by TFWeaponOwnersTests.</summary>
    private sealed class RigOwners : OwnersContract
    {
        private int _nextDef = 9000;
        private Econ.IAttributeProvider? _item;

        public void Item(string hook, float value)
        {
            var container = new Econ.AttributeContainer(null, Econ.AttributeProviderType.Generic);
            container.Set(Econ.AttributeValue.OfFloat(
                new Econ.AttributeDefinition(_nextDef++, "attr " + _nextDef, hook,
                    Econ.AttributeDescriptionFormat.Additive, Econ.AttributeEffectType.Neutral,
                    false, false, Econ.AttributeValueType.Default, Econ.MergedKeys.Block("x")), value));
            _item = container;
        }

        public ClientClass ClassOf(int playerIndex) => ClientClass.Undefined;
        public ClientCond? ConditionsOf(int playerIndex) => null;
        public float CritMult(int playerIndex) => 1f;
        public bool WinsCrits(int playerIndex) => false;
        public Econ.IAttributeProvider? ItemOf(int playerIndex, string weaponName) => _item;
        public Econ.IAttributeProvider? ManagerOf(int playerIndex) => null;
        public bool OffGround(int playerIndex) => false;
        public bool EyesUnderwater(int playerIndex) => false;
        public bool DuckingOf(int playerIndex) => false;
    }

    private static ClientScripts Shipped() =>
        new(new ClientLog(), new Served(WeaponFixtures.ShippedWithClasses()));

    private sealed class Rig
    {
        // 0 = nobody: the slot guard's own refusal. SpyHands moves it to the
        // local player 2; the strict engine answers nothing it was not told.
        public Engine Engine = new Engine().Returns("Hud.LocalPlayerIndex", 0);
        public RigArsenal Arsenal = new();
        public CrosshairVars Vars = new();
        public GunStands Stands = new();
        public StatesDouble States = new();
        public HudClock Clock = new();
        public RigOwners Owners = new();
        public Econ.AttributeHooks Hooks = new();

        public Adapter Source() => new(Arsenal, Shipped(), Engine, Stands, States, Clock,
                                       Owners, Hooks, Vars.File, Vars.Scale, Vars.Red, Vars.Green, Vars.Blue);
    }

    /// <summary>The local spy's hands on the revolver: index 2, the carried
    /// gun stamping its clock at 40, the clock's hand at <paramref name="now"/>.</summary>
    private static Rig SpyHands(Rig rig, float now = 41f, float gunClock = 40f)
    {
        rig.Engine.Returns("Hud.LocalPlayerIndex", 2);
        rig.Arsenal.Weapon = "tf_weapon_revolver";
        rig.Stands.Gun(2, gunClock);
        rig.Clock.Now = now;
        return rig;
    }

    private static Adapter OverShipped(RigArsenal arsenal, CrosshairVars? vars = null)
    {
        var rig = new Rig { Arsenal = arsenal };
        if (vars is not null)
            rig.Vars = vars;
        return rig.Source();
    }

    private sealed class CrosshairVars
    {
        public readonly FileConVar File = new();
        public readonly ScaleConVar Scale = new();
        public readonly RedConVar Red = new();
        public readonly GreenConVar Green = new();
        public readonly BlueConVar Blue = new();

        public CrosshairVars(string file = "", string scale = "32.0",
                             string red = "200", string green = "200", string blue = "200")
        {
            File.NotifyChanged(file, "");
            Scale.NotifyChanged(scale, "32.0");
            Red.NotifyChanged(red, "200");
            Green.NotifyChanged(green, "200");
            Blue.NotifyChanged(blue, "200");
        }
    }

    [Fact]
    public void A_weapon_whose_script_draws_shows_the_crosshair_at_the_base_scale()
    {
        var arsenal = new RigArsenal { Weapon = "tf_weapon_revolver" };

        var source = OverShipped(arsenal);

        Assert.True(source.Draw, "the revolver's script carries no DrawCrosshair 0");
        Assert.Equal(1f, source.Scale);
        Assert.Equal("", source.Material); // the empty cl_crosshair_file is the default crosshair
    }

    [Fact]
    public void A_script_saying_DrawCrosshair_0_hides_it()
    {
        var arsenal = new RigArsenal { Weapon = "tf_weapon_sniperrifle" };

        var source = SeededRifleSource(arsenal, new CrosshairVars());

        Assert.False(source.Draw, "the scoped rifle's script says DrawCrosshair 0");
    }

    [Fact]
    public void A_custom_crosshair_file_draws_where_the_script_refuses()
    {
        // ShouldDrawCrosshair's custom arm (tf_weaponbase.cpp:3646-3647): with
        // a cl_crosshair_file the script's DrawCrosshair 0 does not refuse.
        var arsenal = new RigArsenal { Weapon = "tf_weapon_sniperrifle" };
        var vars = new CrosshairVars();
        var source = SeededRifleSource(arsenal, vars);
        Assert.False(source.Draw, "the same weapon refuses while the file is empty");

        vars.File.NotifyChanged("crosshair01", "");

        Assert.True(source.Draw, "the custom file's arm is stock's return true");
        Assert.Equal("vgui/crosshairs/crosshair01", source.Material);
    }

    [Fact]
    public void The_scale_convar_is_the_player_scale_over_the_32_pixel_texture()
    {
        var arsenal = new RigArsenal { Weapon = "tf_weapon_revolver" };
        var vars = new CrosshairVars();

        var source = OverShipped(arsenal, vars);
        Assert.Equal(1f, source.Scale); // the stock default 32 is the 32-pixel texture itself

        vars.Scale.NotifyChanged("64", "32.0");
        Assert.Equal(2f, source.Scale); // Paint's flPlayerScale is cl_crosshair_scale / 32
    }

    [Fact]
    public void The_colour_is_the_three_cl_crosshair_ints()
    {
        var arsenal = new RigArsenal { Weapon = "tf_weapon_revolver" };
        var vars = new CrosshairVars();

        var source = OverShipped(arsenal, vars);
        Assert.Equal((200, 200, 200), (source.Red, source.Green, source.Blue)); // stock's declared values

        vars.Red.NotifyChanged("12", "200");
        vars.Green.NotifyChanged("34", "200");
        vars.Blue.NotifyChanged("56", "200");
        Assert.Equal((12, 34, 56), (source.Red, source.Green, source.Blue)); // the paint modulates by these
    }

    [Fact]
    public void Empty_hands_draw_nothing()
    {
        var source = OverShipped(new RigArsenal { Weapon = null });

        Assert.False(source.Draw, "stock's base path draws nothing without a weapon");
    }

    [Fact]
    public void Empty_hands_draw_nothing_even_behind_a_custom_file()
    {
        // ShouldDrawCrosshair is the WEAPON's question: no weapon asks it,
        // however the player's file is set.
        var vars = new CrosshairVars(file: "crosshair01");

        var source = OverShipped(new RigArsenal { Weapon = null }, vars);

        Assert.False(source.Draw, "no weapon, no ShouldDrawCrosshair to answer true");
    }

    [Fact]
    public void A_weapon_with_no_script_at_all_shows_nothing()
    {
        var arsenal = new RigArsenal { Weapon = "tf_weapon_nowhere" };

        var source = OverShipped(arsenal);

        Assert.False(source.Draw, "a missing script is stock's gNullWeaponInfo, no crosshair from it");
    }

    // ---- the script crosshair entry (weapons_resource.cpp:123-127) -----------

    /// <summary>The shipped scripts with the revolver's script given a
    /// TextureData crosshair block (the shipped files are otherwise untouched).</summary>
    private static bool SeedCrosshairEntry(TFClassTableTests.Files files)
    {
        var text = WeaponFixtures.Text("tf_weapon_revolver");
        var seeded = new Regex("\n\\{").Replace(text,
            "\n{\n\t\"TextureData\"\t{\n\t\t\"crosshair\"\t{\n\t\t\t\"file\"\t\"crosshairs/hvy\"\n\t\t\t\"x\"\t\"6\"\n\t\t\t\"y\"\t\"2\"\n\t\t\t\"width\"\t\"24\"\n\t\t\t\"height\"\t\"16\"\n\t\t}\n\t}", 1);
        if (seeded == text)
            return false;
        files.Bytes[ClientScripts.ScriptName("tf_weapon_revolver") + ".ctx"] =
            WeaponFixtures.Seal(Encoding.UTF8.GetBytes(seeded));
        return true;
    }

    [Fact]
    public void The_script_s_crosshair_entry_reaches_the_reads()
    {
        // Stock's iconCrosshair (weapons_resource.cpp:123-127): the script's
        // TextureData crosshair, file and rect, handed to the hud element by
        // ItemPreFrame's SetCrosshair (c_basecombatweapon.cpp:280-284).
        var files = WeaponFixtures.ShippedWithClasses();
        Assert.True(SeedCrosshairEntry(files), "the seed found no brace to hang the TextureData block on");
        var arsenal = new RigArsenal { Weapon = "tf_weapon_revolver" };

        var rig = new Rig { Arsenal = arsenal };
        var source = new Adapter(arsenal, new ClientScripts(new ClientLog(), new Served(files)),
                                 rig.Engine, rig.Stands, rig.States, rig.Clock, rig.Owners, rig.Hooks,
                                 rig.Vars.File, rig.Vars.Scale, rig.Vars.Red, rig.Vars.Green, rig.Vars.Blue);

        Assert.Equal("crosshairs/hvy", source.ScriptFile); // the script's own path, unprefixed like stock's bind (hud.cpp:723)
        Assert.Equal((6, 2, 24, 16), (source.ScriptX, source.ScriptY, source.ScriptWide, source.ScriptTall)); // the rect Paint reads (:253-254)
    }

    [Fact]
    public void A_script_without_the_entry_reads_an_empty_crosshair()
    {
        // The trimmed shipped fixture keeps no TextureData block (README): the
        // missing entry is stock's null iconCrosshair, whose arm of the base
        // Paint draws nothing (hud_crosshair.cpp:235-236).
        var arsenal = new RigArsenal { Weapon = "tf_weapon_revolver" };

        var source = OverShipped(arsenal);

        Assert.Equal("", source.ScriptFile);
        Assert.Equal(0, source.ScriptWide);
    }

    // ---- GetWeaponCrosshairScale's bloom arm (tf_weapon_revolver.cpp:207-223) --

    [Fact]
    public void The_headshot_revolver_blooms_the_crosshair_off_the_clocks_age()
    {
        // The Ambassador's arm (set_weapon_mode 1, tf_weapon_revolver.h:49):
        // RemapValClamped from (1.0, 0.5) to (0.75, 2.5) (tf_weapon_revolver.cpp:207-223)
        // — wide just after the shot, settled a second on. The last shot
        // stamped the clock at 40.
        var rig = SpyHands(new Rig(), now: 41f);
        rig.Owners.Item("set_weapon_mode", 1f);
        var source = rig.Source();

        Assert.Equal(0.75f, source.Scale, 3); // a second since the shot: the remap's floor

        rig.Clock.Now = 40.25f; // just after the shot: the remap clamps at its full bloom
        Assert.Equal(2.5f, source.Scale, 3);

        rig.Clock.Now = 40.75f; // the remap's midpoint
        Assert.Equal(1.625f, source.Scale, 3);

        // Paint multiplies the two scales (tf_hud_crosshair.cpp:189-216): the
        // player's cl_crosshair_scale / 32 rides the bloom.
        rig.Clock.Now = 40.25f;
        rig.Vars.Scale.NotifyChanged("64", "32.0");
        Assert.Equal(5f, source.Scale, 3); // 2.5 × 2
    }

    [Fact]
    public void The_plain_revolver_never_blooms()
    {
        // The stock spy revolver carries no set_weapon_mode (the item answers
        // no mode 1), so the base's 1.0 stands however fresh the shot is
        // (tf_weapon_revolver.cpp:221).
        var rig = SpyHands(new Rig(), now: 40.25f); // 0.25 since the shot: the bloom's fullest

        Assert.Equal(1f, rig.Source().Scale, 5); // no item, no mode: the base arm
    }

    [Fact]
    public void A_mode_other_than_the_headshot_never_blooms()
    {
        // CanHeadshot is the mode EQUAL 1 (:49); the shotgun's mode 2 and the
        // revolver's own mode 0 stay at the base 1.0.
        var rig = SpyHands(new Rig(), now: 40.25f);
        rig.Owners.Item("set_weapon_mode", 2f);

        Assert.Equal(1f, rig.Source().Scale, 5);
    }

    [Fact]
    public void The_bloom_reads_the_replays_working_copy_when_it_is_fresh()
    {
        // The ring's working copy leads the decoded state (the same read the
        // scope's charge meter makes): the decoded clock is the old 10, the
        // replay's last shot was 40.
        var rig = SpyHands(new Rig(), now: 40.25f, gunClock: 10f);
        rig.Owners.Item("set_weapon_mode", 1f);
        rig.States.WorkingClock = 40f;

        Assert.Equal(2.5f, rig.Source().Scale, 3); // the working copy's age 0.25, not the decoded 30.25
    }

    [Fact]
    public void Another_players_gun_blooms_the_local_crosshair_not()
    {
        // The carried entity must be the local player's (stock asks the
        // player's own active weapon): index 2 is local and the only gun
        // carried belongs to 5, however fresh its clock and however its item.
        var rig = new Rig();
        rig.Engine.Returns("Hud.LocalPlayerIndex", 2);
        rig.Arsenal.Weapon = "tf_weapon_revolver";
        rig.Clock.Now = 40.25f;
        rig.Owners.Item("set_weapon_mode", 1f);
        rig.Stands.Gun(5, 40f);

        Assert.Equal(1f, rig.Source().Scale, 5);
    }

    [Fact]
    public void A_non_revolver_never_blooms_behind_a_headshot_item()
    {
        // The bloom arm belongs to CTFRevolver alone: hands holding a pistol
        // with a mode-1 item and a fresh clock still draw the base 1.0
        // (the base's GetWeaponCrosshairScale, basecombatweapon_shared.h:531).
        var rig = SpyHands(new Rig(), now: 40.25f);
        rig.Owners.Item("set_weapon_mode", 1f);
        rig.Arsenal.Weapon = "tf_weapon_pistol";

        Assert.Equal(1f, rig.Source().Scale, 5);
    }

    // ---- the DrawCrosshair 0 script, shipped pack plus one seeded file ------

    /// <summary>The shipped scripts with the scoped rifle's script given a
    /// DrawCrosshair 0 (the shipped files are otherwise untouched).</summary>
    private static bool SeedDrawCrosshair0(TFClassTableTests.Files files)
    {
        var text = WeaponFixtures.Text("tf_weapon_sniperrifle");
        var seeded = new Regex("\n\\{").Replace(text, "\n{\n\t\"DrawCrosshair\"\t\"0\"", 1);
        if (seeded == text)
            return false;
        files.Bytes[ClientScripts.ScriptName("tf_weapon_sniperrifle") + ".ctx"] =
            WeaponFixtures.Seal(Encoding.UTF8.GetBytes(seeded));
        return true;
    }

    private static ClientScripts SeededRifleScripts()
    {
        var files = WeaponFixtures.ShippedWithClasses();
        Assert.True(SeedDrawCrosshair0(files), "the seed found no brace to hang DrawCrosshair 0 on");
        return new ClientScripts(new ClientLog(), new Served(files));
    }

    private static Adapter SeededRifleSource(RigArsenal arsenal, CrosshairVars vars)
    {
        var rig = new Rig();
        rig.Arsenal = arsenal;
        return new Adapter(arsenal, SeededRifleScripts(), rig.Engine, rig.Stands, rig.States,
                           rig.Clock, rig.Owners, rig.Hooks,
                           vars.File, vars.Scale, vars.Red, vars.Green, vars.Blue);
    }
}
