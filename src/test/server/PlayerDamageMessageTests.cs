using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The Damage user message, as stock sends it: summed over a frame and sent
/// once at the player's client-data update (CHL2_Player::UpdateClientData),
/// reliable, to that player alone. The bug these pin: the message was not sent
/// at all after CBasePlayer retired, and then once per hit.
/// </summary>
public class PlayerDamageMessageTests
{
    private const int Slot = 3;
    private static readonly EntityHandle Self = new(0x010003);
    private static readonly EntityHandle Shooter = new(0x020007);
    private static readonly Vector ShooterAt = new(128f, -64f, 32f);

    private static (SsPlayer Player, RecordingEngine Engine) Rig(int health = 100)
    {
        var engine = Fake();
        engine.Returns("Entities.GetHealth", health)
              .Returns("Entities.GetIndex", Slot)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.GetOrigin", ShooterAt);
        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid(), messages: engine) { Handle = Self };
        player.BindAreas(Areas(engine));
        player.State.BindLocal();

        // A player in the game has spawned and had its first update, which
        // sends the empty report a spawn asks for (A_spawn_sends_...): drained
        // here so each fact counts only what it caused.
        player.UpdateClientData(0f);
        engine.Clear();
        return (player, engine);
    }

    private static List<EngineCall> Sends(RecordingEngine engine)
        => engine.AllOf("UserMessages.Send").Where(c => (string)c.Args[0]! == "Damage").ToList();

    private static Damage Decode(EngineCall send)
    {
        var reader = new UserMessageReader((byte[])send.Args[4]!);
        var got = Damage.Read(ref reader);
        Assert.False(reader.Overran);
        return got;
    }

    [Fact]
    public void A_hit_sends_one_reliable_Damage_message_to_that_player_at_the_next_update()
    {
        var (player, engine) = Rig();

        player.OnDamaged(Shooter, 30f, DamageType.Bullet);
        Assert.Empty(Sends(engine));   // not per hit: at the update

        player.UpdateClientData(10f);

        var send = Assert.Single(Sends(engine));
        Assert.Equal((Slot, true), ((int)send.Args[2]!, (bool)send.Args[3]!));
        var got = Decode(send);
        // A bullet has no HUD art, so its bit is not sent; the origin is the attacker's.
        Assert.Equal((0, 30, 0, ShooterAt), ((int)got.Armour, (int)got.Health, got.DamageBits, got.Origin));
    }

    [Fact]
    public void Two_hits_in_one_frame_are_one_message_with_the_health_summed_and_the_last_hits_armour()
    {
        var (player, engine) = Rig();
        player.Armour = 100;

        player.OnDamaged(Shooter, 50f, DamageType.Bullet);   // 10 to health, 40 to armour
        player.OnDamaged(Shooter, 25f, DamageType.Bullet);   // 5 to health, 20 to armour
        player.UpdateClientData(10f);

        var got = Decode(Assert.Single(Sends(engine)));
        // Stock: m_DmgTake += (int)damage, but m_DmgSave = flArmor (assigned, not summed).
        Assert.Equal((20, 15), ((int)got.Armour, (int)got.Health));
    }

    [Fact]
    public void A_frame_with_no_damage_and_unchanged_bits_sends_nothing()
    {
        var (player, engine) = Rig();
        player.OnDamaged(Shooter, 30f, DamageType.Bullet);
        player.UpdateClientData(10f);
        Assert.Single(Sends(engine));   // the positive arm: the hit did send

        // The next frame the remembered kinds CHANGED (the bullet bit was
        // cleared after the send, and the HUD's copy still has it), so stock
        // sends once more, empty; only then are the bits unchanged.
        player.UpdateClientData(10.015f);
        var followUp = Decode(Sends(engine)[1]);
        Assert.Equal((0, 0, 0), ((int)followUp.Armour, (int)followUp.Health, followUp.DamageBits));

        player.UpdateClientData(10.030f);
        player.UpdateClientData(10.045f);

        Assert.Equal(2, Sends(engine).Count);
    }

    [Fact]
    public void A_spawn_sends_one_empty_report_and_then_nothing()
    {
        var (player, engine) = Rig();
        engine.Ignores("Shadows.Init", "Render.SetVisible", "Entities.SetModel", "Entities.SetHealth");
        engine.Returns("Shadows.Init", true);

        player.OnSpawn();
        player.UpdateClientData(1f);
        player.UpdateClientData(1.015f);

        // CBasePlayer::Spawn sets m_bitsHUDDamage = -1, which differs from the
        // cleared m_bitsDamageType: one message with nothing in it.
        var got = Decode(Assert.Single(Sends(engine)));
        Assert.Equal((0, 0, 0), ((int)got.Armour, (int)got.Health, got.DamageBits));
    }

    [Fact]
    public void God_mode_reports_nothing()
    {
        var (player, engine) = Rig();
        player.GodMode = true;

        player.OnDamaged(Shooter, 30f, DamageType.Bullet);
        player.UpdateClientData(10f);

        Assert.Empty(Sends(engine));
    }

    [Fact]
    public void The_world_hurts_from_no_direction()
    {
        var (player, engine) = Rig();

        player.OnDamaged(EntityHandle.None, 12f, DamageType.Fall);
        player.UpdateClientData(10f);

        Assert.Equal(default, Decode(Assert.Single(Sends(engine))).Origin);
    }
}

/// <summary>
/// The report's arithmetic apart from the player: stock's
/// CHL2_Player::UpdateClientData damage block, case by case.
/// </summary>
public class PlayerDamageReportTests
{
    private static readonly Vector From = new(1f, 2f, 3f);

    [Fact]
    public void Only_the_kinds_with_hud_art_are_sent()
    {
        var report = new PlayerDamageReport();
        report.Took(5, DamageType.Burn | DamageType.Bullet, From, 1f);

        Assert.True(report.TryFlush(1f, out var got));
        Assert.Equal((int)DamageType.Burn, got.DamageBits);
    }

    [Fact]
    public void The_totals_are_clamped_to_a_byte()
    {
        var report = new PlayerDamageReport();
        report.Took(200, DamageType.Blast, From, 1f);
        report.Took(200, DamageType.Blast, From, 1f);
        report.Absorbed(300f);

        Assert.True(report.TryFlush(1f, out var got));
        Assert.Equal((255, 255), ((int)got.Health, (int)got.Armour));
    }

    [Fact]
    public void Time_based_kinds_outlive_the_send_and_their_change_is_sent_once_more()
    {
        var report = new PlayerDamageReport();
        report.Took(5, DamageType.Burn | DamageType.Acid, From, 1f);

        Assert.True(report.TryFlush(1f, out var first));
        Assert.Equal((int)(DamageType.Burn | DamageType.Acid), first.DamageBits);

        // The HUD saw burn|acid; acid is time-based and stays, burn goes: the
        // remembered kinds changed, so stock sends again with nothing taken.
        Assert.True(report.TryFlush(1.015f, out var second));
        Assert.Equal(((int)DamageType.Acid, 0), (second.DamageBits, (int)second.Health));

        Assert.False(report.TryFlush(1.030f, out _));
    }

    [Fact]
    public void Poison_shows_only_within_a_tenth_of_a_second_of_the_bite()
    {
        var fresh = new PlayerDamageReport();
        fresh.Took(5, DamageType.Poison, From, 1f);
        Assert.True(fresh.TryFlush(1.05f, out var within));
        Assert.Equal((int)DamageType.Poison, within.DamageBits);

        var stale = new PlayerDamageReport();
        stale.Took(5, DamageType.Poison, From, 1f);
        Assert.True(stale.TryFlush(1.2f, out var late));
        Assert.Equal(0, late.DamageBits);
    }

    [Fact]
    public void An_origin_not_given_keeps_the_last_one()
    {
        var report = new PlayerDamageReport();
        report.Took(5, DamageType.Bullet, From, 1f);
        report.TryFlush(1f, out _);

        report.Took(5, DamageType.Bullet, null, 2f);
        Assert.True(report.TryFlush(2f, out var got));
        Assert.Equal(From, got.Origin);
    }
}

/// <summary>
/// Stock's UpdateGeigerCounter: the nearest radiation source's range over
/// four, as a byte, sent reliably when it changes, at most every quarter
/// second, starting two seconds after a spawn.
/// </summary>
public class PlayerGeigerTests
{
    private static (SsPlayer Player, RecordingEngine Engine) Spawned()
    {
        var engine = Fake();
        engine.Returns("Entities.GetHealth", 100).Returns("Entities.GetIndex", 3)
              .Returns("Shadows.Init", true)
              .Ignores("Render.SetVisible", "Entities.SetModel", "Entities.SetHealth", "Entities.GetOrigin");
        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid(), messages: engine) { Handle = new EntityHandle(0x010003) };
        player.BindAreas(Areas(engine));
        player.State.BindLocal();
        player.OnSpawn();
        engine.Clear();
        return (player, engine);
    }

    private static List<byte> Geigers(RecordingEngine engine)
        => engine.AllOf("UserMessages.Send").Where(c => (string)c.Args[0]! == "Geiger")
                 .Select(c => ((byte[])c.Args[4]!)[0]).ToList();

    [Fact]
    public void A_source_400_away_reads_100_once_after_the_spawn_delay()
    {
        var (player, engine) = Spawned();
        player.NotifyNearbyRadiationSource(400f);

        player.UpdateGeigerCounter(1.5f);          // inside the spawn's two seconds
        Assert.Empty(Geigers(engine));

        player.UpdateGeigerCounter(2.1f);
        player.UpdateGeigerCounter(2.4f);          // the same range again
        Assert.Equal(new byte[] { 100 }, Geigers(engine));
        Assert.True((bool)engine.AllOf("UserMessages.Send").Single().Args[3]!, "reliable");
    }

    [Fact]
    public void The_nearest_source_wins_and_the_far_value_clamps_to_a_byte()
    {
        var (player, engine) = Spawned();
        player.NotifyNearbyRadiationSource(400f);
        player.NotifyNearbyRadiationSource(800f);  // further: ignored
        player.UpdateGeigerCounter(3f);
        Assert.Equal(new byte[] { 100 }, Geigers(engine));
    }
}

/// <summary>
/// Stock's CheckTimeBasedDamage, every two seconds: each time-based kind the
/// player carries counts down its duration (2 intervals, 9 for poison's
/// recovery) and its bit clears when done; poison gives back up to 10 health
/// an interval until what it took is restored. Stock deals no damage here --
/// the OnTakeDamage calls are commented out -- so neither does this.
/// </summary>
public class TimeBasedDamageTests
{
    private static readonly Vector From = new(1f, 2f, 3f);

    [Fact]
    public void An_acid_burn_is_remembered_for_two_intervals_and_then_forgotten()
    {
        var report = new PlayerDamageReport();
        report.Took(5, DamageType.Acid, From, 1f);
        report.TryFlush(1f, out _);
        Assert.True((report.Bits & (int)DamageType.Acid) != 0, "acid outlives the send");

        report.CheckTimeBased(3f);   // first sight: the duration starts at 2
        report.CheckTimeBased(5f);   // 1 left
        Assert.True((report.Bits & (int)DamageType.Acid) != 0, "still burning");
        report.CheckTimeBased(7f);   // done
        Assert.True((report.Bits & (int)DamageType.Acid) == 0, "forgotten after its intervals");
    }

    [Fact]
    public void Nothing_happens_within_two_seconds_of_the_last_check()
    {
        var report = new PlayerDamageReport();
        report.Took(5, DamageType.Acid, From, 1f);
        report.CheckTimeBased(3f);
        report.CheckTimeBased(4f);
        report.CheckTimeBased(4.9f);
        report.CheckTimeBased(5f);
        report.CheckTimeBased(7f);
        Assert.True((report.Bits & (int)DamageType.Acid) == 0, "three real intervals, at 3, 5 and 7");
    }

    [Fact]
    public void Poison_gives_back_what_it_took_ten_an_interval()
    {
        var report = new PlayerDamageReport();
        report.Took(25, DamageType.Poison, From, 1f);

        var heals = new[] { 3f, 5f, 7f, 9f }.Select(report.CheckTimeBased).ToArray();

        Assert.Equal(new[] { 10, 10, 5, 0 }, heals);
    }

    [Fact]
    public void Drowning_comes_back_ten_an_interval_once_the_player_breathes()
    {
        var report = new PlayerDamageReport();
        report.Drowned(25);
        report.BreatheAgain();

        var heals = new[] { 3f, 5f, 7f }.Select(report.CheckTimeBased).ToArray();

        Assert.Equal(new[] { 10, 10, 5 }, heals);
        Assert.Equal(0, report.DrownOwed);
    }

    [Fact]
    public void Holding_the_breath_again_stops_the_giving_back()
    {
        var report = new PlayerDamageReport();
        report.Drowned(25);
        report.BreatheAgain();
        report.HoldBreath();

        Assert.Equal(0, report.CheckTimeBased(3f));
        Assert.True((report.Bits & (int)DamageType.DrownRecover) == 0, "no recovery under water");
    }

    [Fact]
    public void A_fresh_hit_of_the_same_kind_restarts_its_countdown()
    {
        var report = new PlayerDamageReport();
        report.Took(5, DamageType.Acid, From, 1f);
        report.CheckTimeBased(3f);   // 2
        report.CheckTimeBased(5f);   // 1
        report.Took(5, DamageType.Acid, From, 6f);
        report.CheckTimeBased(7f);   // restarted: 2
        report.CheckTimeBased(9f);   // 1
        Assert.True((report.Bits & (int)DamageType.Acid) != 0, "the second burn has its own two intervals");
    }
}
