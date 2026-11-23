using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using TF2.Tests.Weapons;

using Xunit;
using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Player;

/// <summary>
/// <see cref="TFDeathSounds"/>: the class grunt of
/// <c>CTFPlayer::DeathSound</c> (tf_player.cpp:15228-15295) — which arm of the
/// kill's damage bits plays which grunt, in stock's order (fall, blast, crit,
/// melee, else generic), the silent killer's refusal beside them.
/// </summary>
public sealed class TFDeathSoundsTests
{
    private static readonly EntityHandle Victim = new(0x0001_0003u);   // index 3
    private static readonly EntityHandle Attacker = new(0x0001_0004u); // index 4

    private static TFDeathSounds Hook(RecordingEngine engine, TFDamageRecords records, TFPlayerNetState state,
                                       FakeOwners owners)
        => new(new FakePredictedData { Carrier = state },
               new TFClassTable(new RecordingLog(), WeaponFixtures.ShippedWithClasses()),
               engine, records, engine, engine, new AttributeHooks(), owners);

    private static TFPlayerNetState Scout()
    {
        var state = new TFPlayerNetState();
        state.BindLocal();
        state.SetClass(TFClass.Scout);
        return state;
    }

    /// <summary>The death walk over one recorded killing hit of <paramref name="bits"/>.</summary>
    private static (RecordingEngine Engine, List<EngineCall> Emits) Kill(TFPlayerNetState state, DamageType bits,
                                                                         EntityHandle attacker,
                                                                         TFDamageCustom custom = TFDamageCustom.None,
                                                                         FakeOwners? owners = null)
    {
        var engine = Fake();
        engine.Returns("Entities.IsValid", true);
        var records = new TFDamageRecords();
        records.Note(Victim.EntryIndex,
                     new TFHitRecord(attacker, 40f, bits, TFCritType.None, custom, TFWeaponId.None, "", 1f),
                     damager: true);
        Hook(engine, records, state, owners ?? new FakeOwners())
            .OnPlayerDied(new PlayerDeath(Victim, Victim.EntryIndex, attacker, default, EntityHandle.None));
        return (engine, [.. engine.AllOf("Sound.EntityEmitSound")]);
    }

    // ---- the arms, in stock's order ----------------------------------------

    /// <summary>A plain bullet kill takes the else arm (:15292-15294): the
    /// class script's generic grunt, to the dying player.</summary>
    [Fact]
    public void A_plain_kill_plays_the_classs_generic_grunt()
    {
        var (_, emits) = Kill(Scout(), DamageType.Bullet, EntityHandle.None);

        var emit = Assert.Single(emits);
        Assert.Equal(Victim, emit.ArgAs<EntityHandle>(0));
        Assert.Equal("Scout.Death", emit.ArgAs<string>(1));
    }

    /// <summary>The record's absence is no bits at all: the else arm, as stock's
    /// zeroed <c>m_LastDamageType</c> (:15292).</summary>
    [Fact]
    public void A_death_the_layer_never_saw_still_grunts_generic()
    {
        var engine = Fake();
        Hook(engine, new TFDamageRecords(), Scout(), new FakeOwners())
            .OnPlayerDied(new PlayerDeath(Victim, Victim.EntryIndex, EntityHandle.None, default, EntityHandle.None));

        Assert.Equal("Scout.Death", Assert.Single(engine.AllOf("Sound.EntityEmitSound")).ArgAs<string>(1));
    }

    /// <summary>The crit arm (:15282-15287) plays the class's crit grunt and then
    /// the receiver's pain (<c>PlayCritReceivedSound</c>, :15118-15130) — both,
    /// in order.</summary>
    [Fact]
    public void A_crit_kill_plays_the_crit_grunt_then_the_pain()
    {
        var (engine, emits) = Kill(Scout(), TFDamageBits.Critical, EntityHandle.None);

        Assert.Equal("Scout.CritDeath", Assert.Single(emits).ArgAs<string>(1));
        // The pain is the dying player's alone (CSingleUserRecipientFilter, :15122), unreliable.
        EngineCall pain = Assert.Single(engine.AllOf("Sound.EmitSoundToPlayer"));
        Assert.Equal((Victim, Victim, "TFPlayer.CritPain", false),
                     (pain.ArgAs<EntityHandle>(0), pain.ArgAs<EntityHandle>(1), pain.ArgAs<string>(2), pain.ArgAs<bool>(3)));
    }

    /// <summary>The melee arm (:15288-15291): the club's grunt alone.</summary>
    [Fact]
    public void A_melee_kill_plays_the_melee_grunt()
    {
        var (_, emits) = Kill(Scout(), DamageType.Club, EntityHandle.None);

        Assert.Equal("Scout.MeleeDeath", Assert.Single(emits).ArgAs<string>(1));
    }

    /// <summary>The blast arm (:15278-15281): the explosion grunt.</summary>
    [Fact]
    public void A_blast_kill_plays_the_explosion_grunt()
    {
        var (_, emits) = Kill(Scout(), DamageType.Blast, EntityHandle.None);

        Assert.Equal("Scout.ExplosionDeath", Assert.Single(emits).ArgAs<string>(1));
    }

    /// <summary>The fall arm (:15273-15277) is the fixed splat, not the class's
    /// own grunt.</summary>
    [Fact]
    public void A_fall_kill_plays_the_splat()
    {
        var (_, emits) = Kill(Scout(), DamageType.Fall, EntityHandle.None);

        Assert.Equal("Player.FallGib", Assert.Single(emits).ArgAs<string>(1));
    }

    // ---- the silent killer (:15251-15257) ----------------------------------

    /// <summary>Your Eternal Reward: the attacker's active weapon carrying
    /// <c>set_silent_killer</c> one (:6188) leaves the death without a grunt at
    /// all, even a crit's.</summary>
    [Fact]
    public void A_kill_by_a_silent_weapon_leaves_no_grunt()
    {
        var owners = new FakeOwners();
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(30062, "silent killer", "set_silent_killer",
                                                                AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                false, false, AttributeValueType.Default,
                                                                MergedKeys.Block("s")), 1f));
        owners.Items[Attacker.EntryIndex] = item;

        var engine = Fake();
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Players.GetActiveWeapon", "tf_weapon_knife");
        var records = new TFDamageRecords();
        records.Note(Victim.EntryIndex,
                     new TFHitRecord(Attacker, 60f, TFDamageBits.Critical, TFCritType.None,
                                     TFDamageCustom.Backstab, TFWeaponId.None, "", 1f),
                     damager: true);
        Hook(engine, records, Scout(), owners)
            .OnPlayerDied(new PlayerDeath(Victim, Victim.EntryIndex, Attacker, default, EntityHandle.None));

        Assert.Equal(0, engine.CountOf("Sound.EntityEmitSound"));
    }

    /// <summary>The same backstab by a knife that carries no attribute keeps its
    /// sound (:6188, the walk answering no): the crit grunt rides on.</summary>
    [Fact]
    public void A_backstab_by_a_plain_knife_keeps_the_crit_grunt()
    {
        var engine = Fake();
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Players.GetActiveWeapon", "tf_weapon_knife");
        var records = new TFDamageRecords();
        records.Note(Victim.EntryIndex,
                     new TFHitRecord(Attacker, 60f, TFDamageBits.Critical, TFCritType.None,
                                     TFDamageCustom.Backstab, TFWeaponId.None, "", 1f),
                     damager: true);
        Hook(engine, records, Scout(), new FakeOwners())
            .OnPlayerDied(new PlayerDeath(Victim, Victim.EntryIndex, Attacker, default, EntityHandle.None));

        Assert.Equal("Scout.CritDeath", Assert.Single(engine.AllOf("Sound.EntityEmitSound")).ArgAs<string>(1));
        Assert.Equal("TFPlayer.CritPain", Assert.Single(engine.AllOf("Sound.EmitSoundToPlayer")).ArgAs<string>(2));
    }

    // ---- the arms before the bits ------------------------------------------

    /// <summary>A player still choosing a class is silent (:15232-15233).</summary>
    [Fact]
    public void An_undefined_player_is_silent()
    {
        var state = new TFPlayerNetState();
        state.BindLocal();   // no class written: the undefined arm

        var (_, emits) = Kill(state, DamageType.Bullet, EntityHandle.None);

        Assert.Empty(emits);
    }

    /// <summary>A feigned death in a friendly disguise wears the disguise's
    /// script (:15241-15247): a scout disguised as a same-team heavy grunts
    /// heavy at the crit arm.</summary>
    [Fact]
    public void A_friendly_disguise_grunts_the_disguised_class()
    {
        var state = Scout();
        state.SetConditions(TFCondMask.Of([TFCond.FeignDeath, TFCond.Disguised]));
        state.DisguiseClass = (int)TFClass.HeavyWeapons;
        state.DisguiseTeam = (int)TFTeam.Red;

        var engine = Fake();
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Players.GetTeam", (int)TFTeam.Red);
        var records = new TFDamageRecords();
        records.Note(Victim.EntryIndex,
                     new TFHitRecord(EntityHandle.None, 40f, TFDamageBits.Critical, TFCritType.None,
                                     TFDamageCustom.None, TFWeaponId.None, "", 1f),
                     damager: true);
        Hook(engine, records, state, new FakeOwners())
            .OnPlayerDied(new PlayerDeath(Victim, Victim.EntryIndex, EntityHandle.None, default, EntityHandle.None));

        // The crit arm's pain goes to the dying player alone (EmitSoundToPlayer).
        Assert.Equal("Heavy.CritDeath", Assert.Single(engine.AllOf("Sound.EntityEmitSound")).ArgAs<string>(1));
    }

    /// <summary>An enemy disguise does not count (:15241): the spy's own class
    /// still grunts.</summary>
    [Fact]
    public void An_enemy_disguise_grunts_the_own_class()
    {
        var state = Scout();
        state.SetConditions(TFCondMask.Of([TFCond.FeignDeath, TFCond.Disguised]));
        state.DisguiseClass = (int)TFClass.HeavyWeapons;
        state.DisguiseTeam = (int)TFTeam.Red;

        var engine = Fake();
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Players.GetTeam", (int)TFTeam.Blue);
        var records = new TFDamageRecords();
        records.Note(Victim.EntryIndex,
                     new TFHitRecord(EntityHandle.None, 40f, DamageType.Club, TFCritType.None,
                                     TFDamageCustom.None, TFWeaponId.None, "", 1f),
                     damager: true);
        Hook(engine, records, state, new FakeOwners())
            .OnPlayerDied(new PlayerDeath(Victim, Victim.EntryIndex, EntityHandle.None, default, EntityHandle.None));

        Assert.Equal("Scout.MeleeDeath", Assert.Single(engine.AllOf("Sound.EntityEmitSound")).ArgAs<string>(1));
    }
}
