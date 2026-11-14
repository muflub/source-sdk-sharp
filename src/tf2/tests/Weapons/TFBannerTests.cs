using SourceSharp.Abi;

using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Server.Weapons;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using TF2.Tests.Combat;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The soldier's banners over the machine's damage arm: the buff meter builds
/// from damage dealt through <see cref="TF2.Server.Combat.TFDamageLayer"/>
/// (stock's <c>HandleRageGain</c>, <c>tf_player.cpp:8718-8741</c> called at
/// :9722), a full meter deploys the buff to teammates in range, and the three
/// buffs' effects — the Buff Banner's mini-crits, Battalion's Backup's taken
/// scale, the Concheror's heal on hit.
/// </summary>
public sealed class TFBannerTests
{
    private const DamageType Ranged = DamageType.Bullet | TFDamageBits.UseDistanceMod;

    private const int Owner = 3;

    /// <summary>
    /// A hit the soldier deals fills their buff meter by the buff's scale of
    /// the damage over the divisor 6 (<c>tf_player.cpp:9722</c>, scales from
    /// <c>g_RageBuffTypes</c>, tf_shareddefs.h:2763-2765), and the meter caps
    /// at full (<c>SetRageMeter</c>'s <c>MIN(val, 100)</c>,
    /// tf_player_shared.cpp:13883). The stimulus must be non-zero: no buff,
    /// no gain.
    /// </summary>
    [Fact]
    public void DamageDealtBuildsRage()
    {
        var rig = new CombatRig();
        rig.AttackerState.PlayerClass = (int)TFClass.Soldier;
        rig.AttackerItem("set_buff_type", AttributeDescriptionFormat.Additive, 1f);

        // Twelve damage of the Buff Banner's scale 1: a sixth fills the meter
        // two points at a time.
        rig.Hit(12f, Ranged);
        Assert.Equal(2f, rig.AttackerState.RageMeter);

        // Past full the meter is held at full, stock's MIN(val, 100).
        rig.Hit(600f, Ranged);
        Assert.Equal(100f, rig.AttackerState.RageMeter);
    }

    /// <summary>
    /// The Buff Banner's offense buff mini-crits the buffed player's shots —
    /// the attacker-cond arm of the mini-crit ladder
    /// (<c>tf_gamerules.cpp:6140-6151</c>): a twelve-damage hit takes the
    /// mini-crit bonus on top of itself, the share
    /// <c>TF_DAMAGE_MINICRIT_MULTIPLIER</c> 1.35 leaves (12 + 0.35·12 = 16.2,
    /// tf_shareddefs.h), through the machine's own damage arm.
    /// </summary>
    [Fact]
    public void TheBuffBannerMiniCritsTheBuffedPlayersShots()
    {
        var rig = new CombatRig();
        rig.Conditions.Add(CombatRig.Attacker, TFCond.Offensebuff);

        Assert.Equal(16.2f, rig.Hit(12f, Ranged), 4);
    }

    /// <summary>
    /// The Battalion's Backup defense buff scales damage taken to 0.65 (the
    /// defense arm, <c>tf_gamerules.cpp:6470-6512</c>): a twenty-damage hit
    /// leaves thirteen. The high variant of the arm is another condition and
    /// stands outside this buff type's deploys.
    /// </summary>
    [Fact]
    public void TheBackupScalesDamageTaken()
    {
        var rig = new CombatRig();
        rig.Conditions.Add(CombatRig.Victim, TFCond.Defensebuff);

        Assert.Equal(13f, rig.Hit(20f, Ranged), 4);
    }

    /// <summary>
    /// The Concheror heals its carrier on a hit for the heal scale 0.35 of
    /// the damage dealt (the regen-on-damage buff, stock's
    /// <c>tf_dev_health_on_damage_recover_percentage</c> default,
    /// tf_weaponbase.cpp:5050): the twenty of the hit heals seven — stock's
    /// truncation into the int (:5215) — through the resupply's TakeHealth
    /// (:5231), and the hit itself still stands at twenty. The event
    /// (:5244-5255) names the amount, the healed edict and the held item's
    /// defindex, stock's -1 without one; the buff reaches its holders through
    /// the deploy (<see cref="TFRageMeter.BuffCond"/>).
    /// </summary>
    [Fact]
    public void TheConcherorHealsItsCarrierOnHit()
    {
        var rig = new CombatRig();
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(7));
        rig.Engine.Returns("GameEvents.EventFire", true);
        rig.Conditions.Add(CombatRig.Attacker, TFCond.Regenondamagebuff);

        Assert.Equal(20f, rig.Hit(20f, Ranged), 4);

        Assert.Contains("take 4 7 False", rig.Resupply.Takes);

        var amount = rig.Engine.Calls.Last(c =>
            c.Member == "GameEvents.EventSetInt" && c.ArgAs<string>(1) == "amount");
        Assert.Equal(7, amount.ArgAs<int>(2));

        var entindex = rig.Engine.Calls.Last(c =>
            c.Member == "GameEvents.EventSetInt" && c.ArgAs<string>(1) == "entindex");
        Assert.Equal(4, entindex.ArgAs<int>(2));

        var defIndex = rig.Engine.Calls.Last(c =>
            c.Member == "GameEvents.EventSetInt" && c.ArgAs<string>(1) == "weapon_def_index");
        Assert.Equal(-1, defIndex.ArgAs<int>(2));

        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventFire"));
    }

    /// <summary>
    /// The heal lands through the resupply's <c>TakeHealth</c>, so the cap is
    /// the seam's item-inclusive max health (the class's plus
    /// <c>add_maxhealth</c>, <c>TFPlayerResupply.cs:162-175</c>) — not a
    /// number the damage layer pre-computes. Twenty of damage asks the full
    /// seven onto a carrier at fifty whose max answers fifty-five: the health
    /// stops at fifty-five, and the ask carries the unfloored seven so the
    /// seam does the capping (stock's <c>TakeHealth</c>, applied at
    /// tf_weaponbase.cpp:5231).
    /// </summary>
    [Fact]
    public void TheConcherorsHealRespectsTheItemMaxHealth()
    {
        var rig = new CombatRig();
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(7));
        rig.Engine.Returns("GameEvents.EventFire", true);
        rig.Resupply.HealthAnswer = 50;
        rig.Resupply.MaxHealthAnswer = 55;   // what the seam answers for this carrier, item included
        rig.Conditions.Add(CombatRig.Attacker, TFCond.Regenondamagebuff);

        rig.Hit(20f, Ranged);

        Assert.Contains("take 4 7 False", rig.Resupply.Takes);
        Assert.Equal(55, rig.Resupply.HealthAnswer);   // not 57: the seam's cap holds
    }

    // ---------------------------------------------------------------------
    // The deploy. The behaviour's ask reaches the dispatcher and the
    // dispatcher's walk answers the team, the reach and the duration, in
    // the order stock reads them (tf_player_shared.cpp:13944-14006).
    // ---------------------------------------------------------------------

    /// <summary>The deploy's world: the engine answers the walk asks, one
    /// carrier per index, and the real condition set writes through.</summary>
    private sealed class BuffRig
    {
        public RecordingEngine Engine { get; } = TestServices.Fake();
        public TFMovementFixtures.Players Players { get; } = new();
        public RigConditions Conditions { get; }
        public FakeOwners Owners { get; } = new();

        private readonly Dictionary<int, Vector> _origins = [];
        private readonly Dictionary<int, int> _teams = [];

        public BuffRig()
        {
            Conditions = new RigConditions(Players);
            Engine.ReturnsEachCall("Players.GetTeam", () => Arg() is EntityHandle h ? TeamOf(h) : 0)
                  .ReturnsEachCall("Players.GetHandle", () => Arg() is int i ? HandleOf(i) : EntityHandle.None)
                  .ReturnsEachCall("Entities.GetOrigin", () => Arg() is EntityHandle h ? OriginOf(h) : Vector.Zero)
                  .Returns("Players.IsAlive", true); // the rage think's alive gate (:13806)
        }

        /// <summary>A player on <paramref name="index"/>, of <paramref name="team"/>,
        /// standing at <paramref name="origin"/>.</summary>
        public TFPlayerNetState Add(int index, TFTeam team, Vector origin)
        {
            _teams[index] = (int)team;
            _origins[index] = origin;
            return Players.Add(index, TFClass.Soldier);
        }

        public static EntityHandle HandleOf(int index) => new(0x0001_0000u | (uint)index);

        /// <summary>The behaviour over the parts the composed server hands it, the
        /// dispatcher behind the effects arm so one ask reaches the walk.</summary>
        public TFBuffItemBehaviour Behaviour(ITFWeaponEffects effects)
        {
            var files = WeaponFixtures.ShippedWithClasses();
            var log = new RecordingLog();
            Owners.Items[Owner] = BuffItemWith(setBuffType: 1);
            var parts = new TFWeaponParts(new TFWeaponScripts(log, files),
                                          TFWeaponHookChain.Of([new TFAttributeWeaponHook(new AttributeHooks()),
                                                                new TFCritBoostHook(), new TFKingReloadHook()]),
                                          new TFCritBuckets(), new FixedCritSettings(), Owners, effects,
                                          playerData: Players);
            return new TFBuffItemBehaviour(parts);
        }

        /// <summary>The dispatcher over the server's walk, as the side composes it.</summary>
        public TFWeaponEffects Dispatcher() => new(buffs: new TFBuffDeployment(Players, Conditions, Engine, Engine));

        public static WeaponAttackContext At(WeaponNetState weapon, float now) =>
            new(new WeaponType("tf_weapon_buff_item", 0.1f), weapon, default!, now, EffectsLive: true,
                new RecordingArms(), new WeaponCommand(1, 5, Owner));

        public static WeaponNetState Armed()
        {
            var weapon = new WeaponNetState();
            weapon.BindLocal();
            return weapon;
        }

        private int TeamOf(EntityHandle h) => _teams.TryGetValue(h.EntryIndex, out int t) ? t : 0;

        private Vector OriginOf(EntityHandle h) => _origins.TryGetValue(h.EntryIndex, out var o) ? o : Vector.Zero;

        private object? Arg() => Engine.Calls[^1].Args.Length > 0 ? Engine.Calls[^1].Args[0] : null;

        /// <summary>One item carrying only the buff's type, as the schema hands it out.</summary>
        public static IAttributeProvider BuffItemWith(float setBuffType)
        {
            var container = new AttributeContainer(null, AttributeProviderType.Generic);
            container.Set(AttributeValue.OfFloat(
                new AttributeDefinition(9100, "attr buff", TFRageMeter.BuffTypeAttribute, AttributeDescriptionFormat.Additive,
                                        AttributeEffectType.Neutral, false, false, AttributeValueType.Default,
                                        MergedKeys.Block("x")), setBuffType));
            return container;
        }
    }

    /// <summary>
    /// The deploy reaches the living teammates inside stock's reach
    /// (<c>fMaxRadius</c> 450 squared on the distance,
    /// <c>tf_player_shared.cpp:13944, :13963</c>): the deployer stands in the
    /// crowd like everyone else (<c>:13995</c>), the teammate across the map
    /// answers none (:13963), and another team is not this one's team
    /// (:13954). The Buff Banner's type 1 wears the offense buff
    /// (<c>g_SoldierBuffAttributeIDToConditionMap</c>,
    /// <c>tf_player_shared.cpp:264-273</c>).
    /// </summary>
    [Fact]
    public void AFullRageDeploysTheBuffToTeammatesInRange()
    {
        var rig = new BuffRig();
        rig.Add(Owner, TFTeam.Red, new Vector(0f, 0f, 64f));
        rig.Add(4, TFTeam.Red, new Vector(64f, 0f, 64f));      // inside the reach
        rig.Add(5, TFTeam.Red, new Vector(600f, 0f, 64f));     // outside it
        rig.Add(6, TFTeam.Blue, new Vector(10f, 0f, 64f));     // another team
        rig.Players.Predicted(Owner)!.RageMeter = TFRageMeter.FullMeter;

        var behaviour = rig.Behaviour(rig.Dispatcher());
        behaviour.PrimaryAttack(BuffRig.At(BuffRig.Armed(), 10f));

        Assert.True(rig.Conditions.InCond(BuffRig.HandleOf(Owner), TFCond.Offensebuff));   // the crowd includes him
        Assert.True(rig.Conditions.InCond(BuffRig.HandleOf(4), TFCond.Offensebuff));
        Assert.False(rig.Conditions.InCond(BuffRig.HandleOf(5), TFCond.Offensebuff));
        Assert.False(rig.Conditions.InCond(BuffRig.HandleOf(6), TFCond.Offensebuff));
    }

    /// <summary>
    /// The pulse holds stock's duration (:14006): every added condition
    /// carries 1.2 seconds, and the deploy's clocks stand where stock puts
    /// them — the refire one second out (the horn's hold at
    /// <c>tf_weapon_buff_item.cpp:134</c> stands in as the press clock) and
    /// the pulses once a second while their count holds (:13852-13862), the
    /// meter draining at the pulse count's share a second until it empties
    /// (:13830-13834). Stock runs both arms from the player's think (:3116),
    /// so here the frames after the press are the player's own tick, not the
    /// held item's frames. The held trigger's once-per-second is the machine's
    /// own press gate (<c>WeaponLogic.RunCommand</c> asks
    /// <c>NextPrimaryAttack</c> before it hands the press over).
    /// </summary>
    [Fact]
    public void TheBuffEndsAfterStocksDuration()
    {
        var rig = new BuffRig();
        rig.Add(Owner, TFTeam.Red, Vector.Zero);
        rig.Add(4, TFTeam.Red, new Vector(64f, 0f, 0f));
        rig.Players.Predicted(Owner)!.RageMeter = TFRageMeter.FullMeter;

        var effects = new RecordingEffects();
        var behaviour = rig.Behaviour(new DispatcherOf(rig, effects));
        var ticker = new TFBuffRageTicker(rig.Players, new WalkingBuffs(rig, effects), rig.Engine);
        ticker.Attach(BuffRig.HandleOf(Owner));

        var weapon = BuffRig.Armed();
        behaviour.PrimaryAttack(BuffRig.At(weapon, 10f));

        Assert.Equal(11f, weapon.NextPrimaryAttack, 4);
        Assert.Equal(TFRageMeter.BuffDurationSeconds, rig.Conditions.Of(4)!.Duration(TFCond.Offensebuff), 4);

        // The pulses: one ask a second while the count holds — the deploy's
        // own ask plus the ten pulses of g_RageBuffTypes, all from the
        // player's frames.
        for (var now = 10.5f; now <= 20.6f; now += 0.5f)
            ticker.Tick(now);

        Assert.Equal(TFRageMeter.BuffPulses + 1, effects.Buffs.Count);
        Assert.Equal(0f, rig.Players.Predicted(Owner)!.RageMeter, 4);
        Assert.False(rig.Players.Predicted(Owner)!.RageDraining);
    }

    /// <summary>
    /// The pulses ride the player's think (:3116), not the held item's frames:
    /// after the press, the held item changes and no item frame runs, yet the
    /// deploy keeps pulsing once a second on the player's ticks — and each
    /// pulse asks the buff type stamped on the player at the deploy (:13743),
    /// not what the new held item names (:13816's re-read is the drain arm's).
    /// </summary>
    [Fact]
    public void TheBuffKeepsPulsingAfterSwitchingWeapons()
    {
        var rig = new BuffRig();
        rig.Add(Owner, TFTeam.Red, Vector.Zero);
        rig.Add(4, TFTeam.Red, new Vector(64f, 0f, 0f));
        rig.Players.Predicted(Owner)!.RageMeter = TFRageMeter.FullMeter;

        var effects = new RecordingEffects();
        var behaviour = rig.Behaviour(new DispatcherOf(rig, effects));

        var weapon = BuffRig.Armed();
        behaviour.PrimaryAttack(BuffRig.At(weapon, 10f)); // held item type 1: the Buff Banner

        // The switch: a different buff item now stands in hand.
        rig.Owners.Items[Owner] = BuffRig.BuffItemWith(setBuffType: 2);

        var ticker = new TFBuffRageTicker(rig.Players, new WalkingBuffs(rig, effects), rig.Engine);
        ticker.Attach(BuffRig.HandleOf(Owner));
        for (var now = 10.5f; now <= 12.6f; now += 0.5f)
            ticker.Tick(now);

        // The deploy's own ask plus two pulse seconds — and every ask names
        // the stamped type 1, so the teammate carries the offense buff even
        // though the held item now names the backup.
        Assert.Equal(3, effects.Buffs.Count);
        Assert.All(effects.Buffs, ask => Assert.Equal(1, ask.BuffType));
        Assert.True(rig.Conditions.InCond(BuffRig.HandleOf(Owner), TFCond.Offensebuff));
        Assert.True(rig.Conditions.InCond(BuffRig.HandleOf(4), TFCond.Offensebuff));
        Assert.False(rig.Conditions.InCond(BuffRig.HandleOf(4), TFCond.Defensebuff));
    }

    /// <summary>
    /// The meter drains on the player's frames (:13830), whatever is held:
    /// with the banner put away after the press — no item frames at all — the
    /// player's tick drains the meter at the pulse count's share a second
    /// until it empties and the stamp clears (:13831-13834).
    /// </summary>
    [Fact]
    public void TheMeterDrainsWithTheBannerPutAway()
    {
        var rig = new BuffRig();
        rig.Add(Owner, TFTeam.Red, Vector.Zero);
        rig.Players.Predicted(Owner)!.RageMeter = TFRageMeter.FullMeter;

        var effects = new RecordingEffects();
        var behaviour = rig.Behaviour(new DispatcherOf(rig, effects));

        var weapon = BuffRig.Armed();
        behaviour.PrimaryAttack(BuffRig.At(weapon, 10f));
        Assert.True(rig.Players.Predicted(Owner)!.RageDraining);

        var ticker = new TFBuffRageTicker(rig.Players, new WalkingBuffs(rig, effects), rig.Engine);
        ticker.Attach(BuffRig.HandleOf(Owner));
        for (var now = 10.5f; now <= 20.6f; now += 0.5f)
            ticker.Tick(now);

        Assert.Equal(0f, rig.Players.Predicted(Owner)!.RageMeter, 4);
        Assert.False(rig.Players.Predicted(Owner)!.RageDraining);
    }

    /// <summary>
    /// The deploy's raise (<c>CTFBuffItem::RaiseFlag</c>,
    /// <c>tf_weapon_buff_item.cpp:227-256</c>): the horn of the held item
    /// sounds first (:186-212 — the Red horn for a Red owner), then the raise's
    /// <c>deploy_buff_banner</c> event with the buff type and the owner's
    /// userid, fired before anything else (:242-248), and the flag sound after
    /// the event (:251). Stock blows the horn at +0.22 s (:134) and raises on
    /// the anim's idle (:397-407); here both ride the press frame, horn first.
    /// </summary>
    [Fact]
    public void TheHornPlaysBeforeTheBuffRaises()
    {
        var rig = new BuffRig();
        rig.Add(Owner, TFTeam.Red, Vector.Zero);
        rig.Players.Predicted(Owner)!.RageMeter = TFRageMeter.FullMeter;
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(7));
        rig.Engine.Returns("GameEvents.EventFire", true);
        rig.Engine.Returns("Players.GetUserId", 103); // the owner's userid, as the server answers it (:247)

        var dispatcher = new TFWeaponEffects(buffs: new TFBuffDeployment(
            rig.Players, rig.Conditions, rig.Engine, rig.Engine, rig.Engine, rig.Engine));
        var behaviour = rig.Behaviour(dispatcher);
        behaviour.PrimaryAttack(BuffRig.At(BuffRig.Armed(), 10f));

        var calls = rig.Engine.Calls;
        int FirstMatch(Func<EngineCall, bool> match)
        {
            for (int i = 0; i < calls.Count; i++)
                if (match(calls[i]))
                    return i;
            return -1;
        }

        // The horn first: the Buff Banner's horn, the Red one for this Red owner (:193-199).
        int horn = FirstMatch(c => c.Member == "Sound.EntityEmitSound" && c.ArgAs<string>(1) == "Weapon_BuffBanner.HornRed");
        Assert.True(horn >= 0);

        // Then the raise's event (:243-249), before anything else raises the buff (:242's comment).
        int ev = FirstMatch(c => c.Member == "GameEvents.EventCreate" && c.ArgAs<string>(0) == "deploy_buff_banner");
        Assert.True(horn < ev);

        // The raise carries the buff type (:246) and the owner's userid (:247).
        var buffType = calls.Last(c => c.Member == "GameEvents.EventSetInt" && c.ArgAs<string>(1) == "buff_type");
        Assert.Equal(1, buffType.ArgAs<int>(2));
        var buffOwner = calls.Last(c => c.Member == "GameEvents.EventSetInt" && c.ArgAs<string>(1) == "buff_owner");
        Assert.Equal(103, buffOwner.ArgAs<int>(2));
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventFire"));

        // The flag sound follows the event (:251).
        int flag = FirstMatch(c => c.Member == "Sound.EntityEmitSound" && c.ArgAs<string>(1) == "Weapon_BuffBanner.Flag");
        Assert.True(ev < flag);

        // And the raise's last-item switch (:256) rides the script lane — here
        // no script row stands behind the buff item, so the ask waits with it.
        Assert.Equal(1, calls.Count(c => c.Member == "Sound.EntityEmitSound" && c.ArgAs<string>(1) == "Weapon_BuffBanner.HornRed"));
    }

    /// <summary>The dispatcher, recording each ask beside handing it to the walk.</summary>
    private sealed class DispatcherOf(BuffRig rig, RecordingEffects effects) : ITFWeaponEffects
    {
        public void LunchboxEat(in WeaponAttackContext attack, TFWeaponScript script) { }
        public void LunchboxDrop(in WeaponAttackContext attack, TFWeaponScript script, int mode) { }
        public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge)
            => effects.Projectile(in attack, script, shot, charge);
        public void Detonate(in WeaponAttackContext attack, TFWeaponScript script) => effects.Detonate(in attack, script);
        public void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot)
            => effects.Flames(in attack, script, shot);
        public void AirBlast(in WeaponAttackContext attack, TFWeaponScript script) => effects.AirBlast(in attack, script);
        public void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held) => effects.Heal(in attack, script, held);
        public void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed) => effects.Zoom(in attack, script, zoomed);
        public void Tracer(in WeaponAttackContext attack, TFWeaponScript script) => effects.Tracer(in attack, script);
        public bool ClassSpecialSkill(in WeaponAttackContext attack) => effects.ClassSpecialSkill(in attack);
        public void SelectLast(in WeaponAttackContext attack, TFWeaponScript script) => effects.SelectLast(in attack, script);
        public void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace)
            => effects.Melee(in attack, script, shot, in trace);
        public void MeleeSwingStarted(in WeaponAttackContext attack, float swingRange) { }
        public void AttackEnemyProjectiles(in WeaponAttackContext attack, int projectiles, bool critical)
            => effects.AttackEnemyProjectiles(in attack, projectiles, critical);
        public void RingOfFire(in WeaponAttackContext attack, int damage) => effects.RingOfFire(in attack, damage);
        public void Rearmed(in WeaponAttackContext attack) => effects.Rearmed(in attack);
        public void SpeakWeaponFire(in WeaponAttackContext attack, string concept) => effects.SpeakWeaponFire(in attack, concept);
        public void ClearWeaponFireScene(in WeaponAttackContext attack) => effects.ClearWeaponFireScene(in attack);
        public void FiringSpeech(in WeaponAttackContext attack) => effects.FiringSpeech(in attack);
        public void DeployBuff(in WeaponAttackContext attack, int buffType)
        {
            effects.DeployBuff(in attack, buffType);
            rig.Dispatcher().DeployBuff(in attack, buffType);
        }

        public void BlowBuffHorn(in WeaponAttackContext attack, int buffType)
        {
            effects.BlowBuffHorn(in attack, buffType);
            rig.Dispatcher().BlowBuffHorn(in attack, buffType);
        }

        public void RaiseBuffBanner(in WeaponAttackContext attack, int buffType)
        {
            effects.RaiseBuffBanner(in attack, buffType);
            rig.Dispatcher().RaiseBuffBanner(in attack, buffType);
        }
    }

    /// <summary>The walk of the deploy, recording each pulse ask beside handing it over.</summary>
    private sealed class WalkingBuffs(BuffRig rig, RecordingEffects effects) : ITFBuffEffects
    {
        private readonly TFBuffDeployment _walk = new(rig.Players, rig.Conditions, rig.Engine, rig.Engine);

        public void DeployBuff(int ownerIndex, int buffType)
        {
            effects.Buffs.Add((ownerIndex, buffType));
            _walk.DeployBuff(ownerIndex, buffType);
        }

        public void BlowBuffHorn(int ownerIndex, int buffType) => _walk.BlowBuffHorn(ownerIndex, buffType);

        public void RaiseBuffBanner(int ownerIndex, int buffType) => _walk.RaiseBuffBanner(ownerIndex, buffType);
    }

    /// <summary>
    /// Below a full meter the press answers nothing (:125, :13756): no ask,
    /// no clocks, no draining stamp — and the frames after it drain nothing
    /// either.
    /// </summary>
    [Fact]
    public void ADeployWithAnEmptyMeterDoesNothing()
    {
        var rig = new BuffRig();
        rig.Add(Owner, TFTeam.Red, Vector.Zero);
        rig.Players.Predicted(Owner)!.RageMeter = 99f;

        var effects = new RecordingEffects();
        var behaviour = rig.Behaviour(effects);

        var weapon = BuffRig.Armed();
        behaviour.PrimaryAttack(BuffRig.At(weapon, 10f));
        behaviour.ItemPostFrame(BuffRig.At(weapon, 10.05f));

        Assert.Empty(effects.Buffs);
        Assert.Equal(0f, weapon.NextPrimaryAttack, 4);
        Assert.False(rig.Players.Predicted(Owner)!.RageDraining);
        Assert.Equal(99f, rig.Players.Predicted(Owner)!.RageMeter, 4);
    }
}
