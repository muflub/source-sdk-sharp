using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Movement;
using TF2.Shared.Player;

using Xunit;

using TF2.Tests.Weapons;

namespace TF2.Tests.Player;

/// <summary>
/// The demoman shield's charge (<c>CTFWearableDemoShield</c>,
/// tf_wearable_weapons.cpp): the attack2 that starts it, the ends that
/// grant the next melee's crit, and the walk while it runs.
/// </summary>
public sealed class TFShieldChargeTests
{
    private const int Index = PlayerRig.Index;

    private static EntityHandle Player => PlayerRig.Player;

    /// <summary>The Chargin' Targe, the trimmed schema's demoshield.</summary>
    private const int Targe = 131;

    private const int Attack = (int)InputButtons.Attack;

    private static int NoCrit => TFWeaponWorld.MeleeSeed(1500, 10000);

    /// <summary>
    /// One demoman and the carrier's chain over the rig: the hooked conditions
    /// and their ticker, the charge's meter and end, the shield skill — with
    /// the Targe worn so the demoshield walk finds it.
    /// </summary>
    private sealed class ChargeRig
    {
        public readonly PlayerRig Player = new();
        public readonly WearableDirectory Directory = new();
        public readonly TFPlayerConditions Conditions;
        public readonly TFPlayerConditionTicker Ticker;
        public readonly TFShieldChargeMeter Meter;
        public readonly TFShieldCharge Shields;
        public readonly TFClassSpecialSkill Skill;

        private uint _next = 0x0003_0050;

        /// <param name="replayClock">
        /// Bind the rig's engine as the meter's environment, so its tick is
        /// sliced by the interval the client's replay steps in.
        /// </param>
        public ChargeRig(bool replayClock = false)
        {
            // What a bash asks: a forward, a miss unless a fact says otherwise, the shake's centre.
            Player.Engine.Returns("Entities.GetEyeAngles", default(Vector));
            Player.Engine.Returns("Players.GetShootOrigin", default(Vector));
            Player.Engine.Returns("WorldQuery.TraceBox",
                new SurfaceTraceResult(false, 1f, default, default, EntityHandle.None, 0, 0));
            Player.Engine.Returns("Entities.GetWorldSpaceCenter", default(Vector));
            Player.Engine.Returns("Networking.EntityGetState", default(EntityState));
            Player.Engine.Returns("Networking.EntityGetState.moveType", default(MoveType));
            if (replayClock)
                Player.Engine.Returns("Environment.Globals", new Globals(PredictionDeps.SharedGlobals()));
            Conditions = new(Player.Carriers, Player.Engine);
            Ticker = new(Player.Carriers, Conditions, Player.Engine);
            var wearables = new TFWearables(Player.Carriers, Player.Engine, Directory, Player.Engine, Player.Engine, Player.Log);
            Shields = new(Player.Carriers, Directory, Player.Schema, Player.Engine, Player.Engine,
                          Player.Engine, Player.Engine, Player.Engine,
                          new ScreenEffects(Player.Engine, Player.Engine, Player.Engine, Player.Engine),
                          Player.Log);
            var end = new TFShieldChargeEnd(Player.Carriers, Shields, null, Player.Engine);
            Meter = replayClock
                ? new(Player.Carriers, Conditions, Player.Items, null, null, null, Player.Engine)
                : new(Player.Carriers, Conditions, Player.Items, null, null, null);
            Player.Extra.AddRange([Ticker, wearables, end, Meter]);
            Skill = new(Player.Carriers, Player.Engine, owners: null, shields: Shields);

            // The class first: the attach window's create is the carrier's, so
            // the player is in before the wearables' own factory stands.
            Player.As(TFClass.Demoman);
        }

        /// <summary>The demoman wears the Targe: one tf_wearable of item 131.</summary>
        public void WearsShield()
        {
            Player.Engine.Returns("Precache.PrecacheModel", 42);
            Player.Engine.ReturnsEachCall("Entities.Create", () =>
            {
                var handle = new EntityHandle(_next++);
                Directory.Add(handle, Player.Engine);
                return handle;
            });
            Player.Carriers.Part<TFWearables>(Index)!
                .Rebuild(PlayerRig.Player, TFClass.Demoman, TFTeam.Red,
                         [new LoadoutItem(TFLoadoutSlot.Secondary, ItemInstance.Base(Player.Schema.Item(Targe)!))]);
        }

        /// <summary>The charge begins the stock way: the condition, the meter.</summary>
        public void Charging(float meter)
        {
            Conditions.Add(PlayerRig.Player, TFCond.ShieldCharge);
            Player.State.ChargeMeter = meter;
        }
    }

    /// <summary>The wearable entities the rig's creates handed out, by handle.</summary>
    private sealed class WearableDirectory : IEntityDirectory
    {
        private readonly Dictionary<EntityHandle, TFWearableEntity> _byHandle = new();

        public void Add(EntityHandle handle, IEngineEntityRender render)
        {
            var entity = new TFWearableEntity(render);
            entity.State.BindLocal();
            _byHandle[handle] = entity;
        }

        ScriptedEntity? IEntityDirectory.Get(EntityHandle h) => _byHandle.GetValueOrDefault(h);
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => className == TFEntityNames.Wearable;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>
    /// The attack2 of a shielded demoman whose meter is full starts the charge
    /// (<c>DoSpecialAction</c> through <c>DoClassSpecialSkill</c>'s demoman
    /// arm, tf_player_shared.cpp:12363-12368, tf_wearable_weapons.cpp:125-134):
    /// <c>TF_COND_SHIELD_CHARGE</c> for the charge's second and a half. The
    /// meter short of full refuses it (<c>CanCharge</c>, :113-114).
    /// </summary>
    [Fact]
    public void AShieldCarrierChargesOnAttack2()
    {
        var rig = new ChargeRig();
        rig.WearsShield();

        // CanCharge's meter gate: the empty meter's press is no charge.
        rig.Skill.DoSpecialSkill(Index);
        Assert.False(rig.Player.State.InCond(TFCond.ShieldCharge));

        rig.Player.State.ChargeMeter = 100f;

        rig.Skill.DoSpecialSkill(Index);

        Assert.True(rig.Player.State.InCond(TFCond.ShieldCharge));
        Assert.Equal(TFShieldCharge.BaseChargeTime,
                     rig.Player.Carriers.ConditionsOf(Index)!.Duration(TFCond.ShieldCharge));
    }

    /// <summary>
    /// The charge ends twice (tf_player_shared.cpp:6070-6095): the meter's
    /// drain emptying ends it at its time — the meter's own tick removes the
    /// condition (:6029-6032) — and the impact's <c>EndCharge</c>, the walk's
    /// bump arm through the carrier (tf_gamemovement.cpp:1925-1929), zeroes
    /// the condition's duration for the ticker's next pass and grants the
    /// next melee's crit (:6087-6091, :6125-6146).
    /// </summary>
    /// <summary>
    /// One bash a charge (m_bImpactedSomething, tf_wearable_weapons.cpp:147-150): the
    /// first contact lands and the next asks nothing. Live, the latch was inverted, so
    /// a first bump passed through and every later one hit.
    /// </summary>
    [Fact]
    public void A_charge_bashes_once_and_on_the_first_contact()
    {
        var rig = new ChargeRig();
        rig.Charging(30f);
        rig.Player.Engine.Returns("Entities.IsPlayer", true);
        rig.Player.Engine.Returns("WorldQuery.TraceBox",
            new SurfaceTraceResult(true, 0.5f, default, default, new EntityHandle(0x0001_0009u), 0, 0));

        var rules = new TFMovementRules(TFMovementFixtures.Server(), rig.Player.Carriers);

        rules.ChargeBump(Index, PlayerRig.Player);
        Assert.Single(rig.Player.Engine.AllOf("Sound.EntityEmitSound"));

        rules.ChargeBump(Index, PlayerRig.Player);
        Assert.Single(rig.Player.Engine.AllOf("Sound.EntityEmitSound"));
    }

    [Fact]
    public void AChargeEndsAtItsTimeOrOnImpact()
    {
        // At its time: the drain to empty.
        var timed = new ChargeRig();
        timed.Conditions.Add(Player, TFCond.ShieldCharge);
        timed.Player.State.ChargeMeter = 100f;

        for (float now = 0f; now <= 2f; now += 1f)
        {
            timed.Ticker.Tick(now);
            timed.Meter.Tick(now);
        }

        Assert.Equal(0f, timed.Player.State.ChargeMeter);
        Assert.False(timed.Player.State.InCond(TFCond.ShieldCharge));

        // On impact: the movement's bump of a player, the end's duration zero.
        var hit = new ChargeRig();
        hit.Charging(30f);
        var rules = new TFMovementRules(TFMovementFixtures.Server(), hit.Player.Carriers);

        rules.ChargeBump(Index, PlayerRig.Player);

        Assert.Equal(0f, hit.Player.Carriers.ConditionsOf(Index)!.Duration(TFCond.ShieldCharge));
        Assert.Equal((int)TFMeleeCrit.Crit, hit.Player.State.NextMeleeCrit);

        hit.Ticker.Tick(1f);
        hit.Ticker.Tick(1.1f);

        Assert.False(hit.Player.State.InCond(TFCond.ShieldCharge));
    }

    /// <summary>
    /// The charged swing's crit, end to end: the swing's ask ends the charge
    /// (<c>EndSpecialAction</c> through <c>EndClassSpecialSkill</c>,
    /// tf_wearable_weapons.cpp:72-87) and the grant rides the meter — 40 or
    /// under a full crit, 75 or under a mini-crit (<c>CalcChargeCrit</c>,
    /// tf_player_shared.cpp:6125-6146) — which the swing's own helpers read
    /// before the bucket (tf_weaponbase_melee.cpp:1040-1105) and the smack
    /// spends (:384). The mini rides the smack's hit as the charge's
    /// mini-crit, never over a roll the swing made itself (:855-863).
    /// </summary>
    /// <summary>
    /// The server's meter is integrated once per tick, the add the client's
    /// replay makes once per command — a frame's ticks sliced, not one add of
    /// the frame's whole span, whose rounding is the byte the prediction
    /// compare counts (<c>TFPlayerPrediction</>'s charge tick against this
    /// component). The drain runs to empty and the refill that follows, the
    /// whole shape a charge leaves behind.
    /// </summary>
    [Fact]
    public void TheServerIntegratesTheMeterOncePerTickAsTheReplayDoes()
    {
        const float interval = PredictionDeps.Tick;
        var rig = new ChargeRig(replayClock: true);
        rig.Charging(64f);
        rig.Meter.Tick(0f);   // the attach frame: the prime that starts the clock, no integration

        // The replay's own chain, the client arm's expressions over the same
        // numbers: the drain to empty, then the refill toward full.
        float replay = 64f;
        bool charging = true;

        float now = 0f;
        for (int frame = 0; frame < 240; frame++)
        {
            for (int tick = 0; tick < 1 + frame % 3; tick++)   // frames of one, two and three ticks
            {
                now += interval;

                if (charging)
                {
                    replay -= interval * (100f / 1.5f);
                    if (replay <= 0f)
                    {
                        replay = 0f;
                        charging = false;
                    }
                }
                else if (replay < 100f)
                {
                    replay = MathF.Min(100f, replay + interval * MathF.Max(8.3f, 1f));
                }
            }

            rig.Meter.Tick(now);
        }

        Assert.Equal(BitConverter.SingleToInt32Bits(replay),
                     BitConverter.SingleToInt32Bits(rig.Player.State.ChargeMeter));
    }

    [Fact]
    public void AChargedSwingCrits()
    {
        var rig = new ChargeRig();
        rig.Charging(30f);
        rig.Skill.EndSpecialSkill(Index);

        Assert.Equal((int)TFMeleeCrit.Crit, rig.Player.State.NextMeleeCrit);

        var data = new FakePredictedData { Carrier = rig.Player.State };
        var world = new TFWeaponWorld(data);
        var (w, vm) = world.Armed("tf_weapon_sword", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);

        // The swing asked the end first (tf_weaponbase_melee.cpp:201) and
        // rolls the grant: the crit bit on the air.
        Assert.Equal(Index, world.Effects.ClassSkillEnds.Single());
        Assert.True((w.BehaviourMode & TFMeleeBehaviour.ModeSwingCrit) != 0);

        world.Run(w, vm, 0, now: 10.215f, seed: NoCrit);

        var smack = Assert.Single(world.Effects.Swings);
        Assert.True(smack.Shot.Critical);
        Assert.False(smack.Shot.Mini);

        // The smack spent the grant: the next swing rolls plain.
        Assert.Equal((int)TFMeleeCrit.NoCrit, rig.Player.State.NextMeleeCrit);

        // Again, the meter in the mini band: the same chain, the mini-crit.
        rig.Charging(60f);
        rig.Skill.EndSpecialSkill(Index);

        Assert.Equal((int)TFMeleeCrit.MiniCrit, rig.Player.State.NextMeleeCrit);

        world.Run(w, vm, Attack, now: 12f, seed: NoCrit);
        world.Run(w, vm, 0, now: 12.215f, seed: NoCrit);

        var mini = world.Effects.Swings[^1];
        Assert.False(mini.Shot.Critical);
        Assert.True(mini.Shot.Mini);
    }
}
