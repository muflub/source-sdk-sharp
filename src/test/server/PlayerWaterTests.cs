using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// CBasePlayer::WaterMove and UpdateUnderwaterState, HL2's arm: seven
/// seconds of air with the eyes under, then a drowning hit of ten from the
/// world each second; the suit's breather spends its power instead while it
/// has any (CHL2_Player::CanBreatheUnderwater, SetPlayerUnderwater); coming
/// up gasps and starts giving the drowning back; the feet entering or
/// leaving the water wade.
/// </summary>
public class PlayerWaterTests
{
    private static readonly EntityHandle Self = new(0x010003);

    private static (SsPlayer Player, RecordingEngine Engine, MoveState Move) Rig(float suitPower = 0f)
    {
        var engine = Fake();
        engine.Returns("Entities.GetHealth", 100)
              .Returns("Entities.GetIndex", 3)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.GetOrigin", Vector.Zero);
        engine.Strict = false;
        var moves = new ServerPlayerMoves();
        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid(), messages: engine, moves: moves, sound: engine,
                                damage: engine)
        {
            Handle = Self,
        };
        player.BindAreas(Areas(engine));
        player.State.BindLocal();
        player.State.Alive = true;
        player.State.SuitPower = suitPower;

        MoveState move = moves.For(Self).Move;
        move.SuitEquipped = true;
        move.SuitPower = suitPower;

        // Out of the water at 0: the air is full until 7.
        player.WaterMove(0f);
        engine.Clear();
        return (player, engine, move);
    }

    private static string[] Sounds(RecordingEngine engine)
        => engine.AllOf("Sound.EntityEmitSound").Select(c => (string)c.Args[1]!).ToArray();

    [Fact]
    public void SevenSecondsOfAirThenTenASecond()
    {
        var (player, engine, _) = Rig();
        player.State.WaterLevel = 3;

        player.WaterMove(6.9f);
        Assert.Equal(0, player.DamageReport.DrownOwed);

        player.WaterMove(7.1f);
        Assert.Equal(10, player.DamageReport.DrownOwed);
        // Through the engine's TakeDamage, from the world (entry 0).
        var hit = Assert.Single(engine.AllOf("Damage.Apply"));
        Assert.Equal((Self, new EntityHandle(0u), 10f, DamageType.Drown),
                     (hit.ArgAs<EntityHandle>(0), hit.ArgAs<EntityHandle>(1), hit.ArgAs<float>(2), hit.ArgAs<DamageType>(3)));

        player.WaterMove(7.5f);   // pain finished at 8.1
        Assert.Equal(10, player.DamageReport.DrownOwed);
        player.WaterMove(8.2f);
        Assert.Equal(20, player.DamageReport.DrownOwed);
    }

    /// <summary>The player's think runs it, first, as stock's PreThink does.</summary>
    [Fact]
    public void ThePlayersThinkRunsTheWaterMove()
    {
        var (player, _, _) = Rig();
        player.State.WaterLevel = 3;

        player.PreThink(7.1f);

        Assert.Equal(10, player.DamageReport.DrownOwed);
    }

    [Fact]
    public void TheSuitBreathesWhileItHasPower()
    {
        var (player, _, _) = Rig(suitPower: 40f);
        player.State.WaterLevel = 3;

        player.WaterMove(20f);

        Assert.Equal(0, player.DamageReport.DrownOwed);
    }

    [Fact]
    public void GodModeNeverDrowns()
    {
        var (player, _, _) = Rig();
        player.GodMode = true;
        player.State.WaterLevel = 3;

        player.WaterMove(20f);

        Assert.Equal(0, player.DamageReport.DrownOwed);
    }

    [Fact]
    public void ComingUpGaspsAndStartsGivingTheDrowningBack()
    {
        var (player, engine, _) = Rig();
        player.State.WaterLevel = 3;
        player.WaterMove(7.1f);

        player.State.WaterLevel = 0;
        player.WaterMove(7.2f);

        Assert.Contains("Player.DrownStart", Sounds(engine));
        Assert.True((player.DamageReport.Bits & (int)DamageType.DrownRecover) != 0, "recovering");
    }

    [Fact]
    public void TheBreatherRunsWhileTheEyesAreUnder()
    {
        var (player, _, move) = Rig(suitPower: 40f);

        player.State.WaterLevel = 3;
        player.WaterMove(1f);
        Assert.True(player.Underwater);
        Assert.Equal((GameMovement.SuitDeviceBreather, GameMovement.SuitDeviceBreatherDrain), (move.SuitDevices, move.SuitPowerLoad));

        player.State.WaterLevel = 2;
        player.WaterMove(2f);
        Assert.False(player.Underwater);
        Assert.Equal((0, 2f), (move.SuitDevices, move.SuitDevicesOffTime));
    }

    [Fact]
    public void TheFeetWadeInAndOut()
    {
        var (player, engine, _) = Rig();

        player.State.WaterLevel = 1;
        player.State.WaterType = 0x20;   // CONTENTS_WATER
        player.WaterMove(1f);
        player.State.WaterLevel = 0;
        player.WaterMove(2f);

        Assert.Equal(["Player.Wade", "Player.Wade"], Sounds(engine));
    }

    [Fact]
    public void ASpawnFillsTheLungs()
    {
        var (player, engine, _) = Rig();
        player.State.WaterLevel = 3;
        player.WaterMove(5f);

        engine.Returns("Clock.Now", 5f);
        player.OnSpawn();
        player.State.Alive = true;
        player.State.WaterLevel = 3;
        player.WaterMove(7.1f);

        Assert.Equal(0, player.DamageReport.DrownOwed);
    }
}
