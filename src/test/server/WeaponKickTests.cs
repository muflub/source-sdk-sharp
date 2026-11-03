using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>Recoil: each HL2MP weapon's kick on the view, from stock's AddViewKick and ViewPunch calls.</summary>
public class WeaponKickTests
{
    private static PlayerNetState Carrier()
    {
        var state = new PlayerNetState();
        state.BindLocal();
        return state;
    }

    private static Vector Kicked(WeaponKick kick, int seed = 7, float fireDuration = 0f, PlayerNetState? carrier = null)
    {
        carrier ??= Carrier();
        kick.Apply(carrier, new SeededUniformStream(), seed, fireDuration);
        return carrier.PunchAngleVel.AsVector();
    }

    private sealed class Arms(PlayerNetState carrier) : IWeaponArms
    {
        public int PunchesKept;
        public PlayerNetState? Carrier => carrier;
        public void PunchChanged(PlayerNetState kicked) => PunchesKept++;
        public int Jolts;
        public void DisorientView() => Jolts++;
        public void Sound(string soundScript) { }
        public Vector PunchAtShot;
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) => PunchAtShot = carrier.PunchAngle.AsVector();
        public void MeleeSwing(in WeaponType type) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) { }
        public int LookupSequence(WeaponAnim anim) => (int)anim + 1;
        public float AnimDuration(WeaponAnim anim) => 1f / 3f;
    }

    private static (WeaponNetState W, ViewModelNetState Vm) Held(int clip)
    {
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponSmg1);
        w.Clip = clip;
        w.Reserve = 10;
        return (w, vm);
    }

    private static WeaponType Row(string name) => WeaponTypes.All.Single(t => t.Name == name);

    /// <summary>The punch's velocity is twenty times the kick (CBasePlayer::ViewPunch), inside the row's range.</summary>
    [Theory]
    [InlineData(EntityNames.WeaponPistol, 0.25f, 0.5f, -0.6f, 0.6f)]
    [InlineData(EntityNames.Weapon357, -8f, -8f, -2f, 2f)]
    [InlineData(EntityNames.WeaponShotgun, -2f, -1f, -2f, 2f)]
    [InlineData(EntityNames.WeaponCrossbow, -2f, -2f, 0f, 0f)]
    public void A_ranged_kick_is_stocks_range_times_twenty(string weapon, float pitchMin, float pitchMax, float yawMin, float yawMax)
    {
        WeaponKick kick = Row(weapon).Kick!.Value;
        for (int seed = 0; seed < 64; seed++)
        {
            Vector v = Kicked(kick, seed);
            Assert.InRange(v.X, pitchMin * 20f - 0.001f, pitchMax * 20f + 0.001f);
            Assert.InRange(v.Y, yawMin * 20f - 0.001f, yawMax * 20f + 0.001f);
        }
    }

    [Theory]
    [InlineData(EntityNames.WeaponCrowbar)]
    [InlineData(EntityNames.WeaponRpg)]
    [InlineData(EntityNames.WeaponFrag)]
    [InlineData(EntityNames.WeaponStunstick)]
    public void A_weapon_stocks_does_not_kick_has_no_kick(string weapon) => Assert.Null(Row(weapon).Kick);

    [Fact]
    public void The_same_seed_kicks_alike_on_both_sides_and_another_differently()
    {
        WeaponKick kick = Row(EntityNames.WeaponShotgun).Kick!.Value;
        Assert.Equal(Kicked(kick, 3), Kicked(kick, 3));
        Assert.NotEqual(Kicked(kick, 3), Kicked(kick, 4));
    }

    /// <summary>DoMachineGunKick: 0.2 degrees at the first shot, plus the max at the slide limit, halved; upward.</summary>
    [Fact]
    public void A_machine_guns_kick_grows_until_the_slide_limit()
    {
        WeaponKick smg = Row(EntityNames.WeaponSmg1).Kick!.Value;
        Assert.Equal(-(0.2f) * 0.5f * 20f, Kicked(smg, fireDuration: 0f).X, 3);
        Assert.Equal(-(0.2f + 0.5f) * 0.5f * 20f, Kicked(smg, fireDuration: 1f).X, 3);
        Assert.Equal(-(0.2f + 1f) * 0.5f * 20f, Kicked(smg, fireDuration: 2f).X, 3);
        Assert.Equal(-(0.2f + 1f) * 0.5f * 20f, Kicked(smg, fireDuration: 9f).X, 3);
    }

    /// <summary>UTIL_ClipPunchAngleOffset with QAngle( 24, 3, 1 ), and ViewPunchReset( 10 ) leaves a big punch.</summary>
    [Fact]
    public void A_machine_guns_kick_stops_at_the_punch_clip()
    {
        WeaponKick ar2 = Row(EntityNames.WeaponAr2).Kick!.Value;
        var carrier = Carrier();
        carrier.PunchAngle = new QAngle(-24f, 0f, 0f);
        Assert.Equal(0f, Kicked(ar2, fireDuration: 5f, carrier: carrier).X, 3);
    }

    [Fact]
    public void A_machine_gun_clears_a_small_punch_and_keeps_a_large_one()
    {
        WeaponKick smg = Row(EntityNames.WeaponSmg1).Kick!.Value;

        var small = Carrier();
        small.PunchAngleVel = new QAngle(5f, 0f, 0f);
        Assert.Equal(-2f, Kicked(smg, carrier: small).X, 3);

        var large = Carrier();
        large.PunchAngleVel = new QAngle(50f, 0f, 0f);
        Assert.Equal(50f - 2f, Kicked(smg, carrier: large).X, 3);
    }

    [Fact]
    public void The_pistol_resets_the_punch_before_each_shot()
    {
        WeaponKick pistol = Row(EntityNames.WeaponPistol).Kick!.Value;
        var carrier = Carrier();
        carrier.PunchAngle = new QAngle(30f, 0f, 0f);
        carrier.PunchAngleVel = new QAngle(500f, 0f, 0f);
        pistol.BeforeShot(carrier);
        Vector v = Kicked(pistol, carrier: carrier);
        Assert.Equal(0f, carrier.PunchAngle.AsVector().X);
        Assert.InRange(v.X, 5f, 10f);
    }

    /// <summary>Through the weapon machine, on a replay too: the punch is predicted state, not an effect.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Firing_kicks_the_carrier(bool effectsLive)
    {
        var carrier = Carrier();
        var logic = new WeaponLogic(new Arms(carrier));
        var (w, vm) = Held(10);

        logic.RunCommand(w, vm, (int)InputButtons.Attack, 10f, effectsLive);

        Assert.NotEqual(0f, carrier.PunchAngleVel.AsVector().X);
    }

    /// <summary>
    /// The .357's jolt is the first prediction's alone (IsFirstTimePredicted), and the
    /// .357's alone: a replay does not knock the aim again.
    /// </summary>
    [Theory]
    [InlineData(EntityNames.Weapon357, true, 1)]
    [InlineData(EntityNames.Weapon357, false, 0)]
    [InlineData(EntityNames.WeaponSmg1, true, 0)]
    public void Only_the_357s_first_run_jolts_the_view(string weapon, bool effectsLive, int jolts)
    {
        var arms = new Arms(Carrier());
        var logic = new WeaponLogic(arms);
        var (w, vm) = Held(10);
        w.WeaponTypeId = WeaponCatalog.Stock().IndexOf(weapon);

        logic.RunCommand(w, vm, (int)InputButtons.Attack, 10f, effectsLive);

        Assert.Equal(jolts, arms.Jolts);
    }

    /// <summary>GetAutoaimVector: EyeAngles() + m_vecPunchAngle.</summary>
    [Fact]
    public void A_bullet_goes_where_the_punched_view_points()
    {
        var carrier = Carrier();
        carrier.PunchAngle = new QAngle(-3f, 1f, 0f);
        Assert.Equal(new Vector(7f, 91f, 0f), WeaponKick.Aim(new Vector(10f, 90f, 0f), carrier));
        Assert.Equal(new Vector(10f, 90f, 0f), WeaponKick.Aim(new Vector(10f, 90f, 0f), null));
    }

    /// <summary>The shot reads the punch as it stood: the machine gun's reset and kick come after it.</summary>
    [Fact]
    public void The_shot_sees_the_punch_the_kick_comes_after()
    {
        var carrier = Carrier();
        carrier.PunchAngle = new QAngle(-3f, 0f, 0f);
        var arms = new Arms(carrier);
        var logic = new WeaponLogic(arms);
        var (w, vm) = Held(10);

        logic.RunCommand(w, vm, (int)InputButtons.Attack, 10f, true);

        Assert.Equal(-3f, arms.PunchAtShot.X);
        Assert.NotEqual(0f, carrier.PunchAngleVel.AsVector().X);
    }

    /// <summary>
    /// The server's state only mirrors the movement record, so a kick left on it was gone at
    /// the next command: one frame of view, and no recoil.
    /// </summary>
    [Fact]
    public void A_kick_is_handed_to_the_side_to_keep_and_a_shot_with_none_is_not()
    {
        var carrier = Carrier();
        var arms = new Arms(carrier);
        var logic = new WeaponLogic(arms);
        var (w, vm) = Held(10);
        logic.RunCommand(w, vm, (int)InputButtons.Attack, 10f, false);
        Assert.Equal(1, arms.PunchesKept);

        var crowbar = new WeaponLogic(arms);
        var (c, cvm) = Held(10);
        c.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponCrowbar);
        crowbar.RunCommand(c, cvm, (int)InputButtons.Attack, 10f, false);
        Assert.Equal(1, arms.PunchesKept);
    }

    [Fact]
    public void Holding_the_trigger_is_what_the_kick_grows_on()
    {
        var carrier = Carrier();
        var logic = new WeaponLogic(new Arms(carrier));
        var (w, vm) = Held(100);

        logic.RunCommand(w, vm, (int)InputButtons.Attack, 10f, false);
        Assert.Equal(10f, w.FireStartedAt);
        logic.RunCommand(w, vm, (int)InputButtons.Attack, 11f, false);
        Assert.Equal(10f, w.FireStartedAt);
        logic.RunCommand(w, vm, 0, 11.1f, false);
        Assert.Equal(0f, w.FireStartedAt);
    }
}
