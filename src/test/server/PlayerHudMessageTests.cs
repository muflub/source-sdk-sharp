using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The rest of stock's per-player client-data update
/// (CBasePlayer::UpdateClientData, after CHL2_Player's Damage block):
/// ResetHUD once per spawn, Battery when the armour the client was told
/// differs, Train when the train state is new (every spawn). All reliable, to
/// the player alone, in stock's order.
/// </summary>
public class PlayerHudMessageTests
{
    private const int Slot = 4;

    private static (SsPlayer Player, RecordingEngine Engine) Spawned()
    {
        var engine = Fake();
        engine.Returns("Entities.GetHealth", 100)
              .Returns("Entities.GetIndex", Slot)
              .Returns("Shadows.Init", true)
              .Ignores("Render.SetVisible", "Entities.SetModel", "Entities.SetHealth", "Entities.GetOrigin");
        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid(), messages: engine) { Handle = new EntityHandle(0x010004) };
        player.BindAreas(Areas(engine));
        player.State.BindLocal();
        player.OnSpawn();
        engine.Clear();
        return (player, engine);
    }

    private static List<EngineCall> Sends(RecordingEngine engine) => engine.AllOf("UserMessages.Send").ToList();

    private static string Keys(RecordingEngine engine) => string.Join(",", Sends(engine).Select(c => (string)c.Args[0]!));

    [Fact]
    public void A_spawns_first_update_sends_damage_resethud_battery_and_train_in_stocks_order()
    {
        var (player, engine) = Spawned();

        player.UpdateClientData(1f);

        Assert.Equal("Damage,ResetHUD,Battery,Train", Keys(engine));
        Assert.All(Sends(engine), s => Assert.Equal((Slot, true), ((int)s.Args[2]!, (bool)s.Args[3]!)));
    }

    [Fact]
    public void The_spawn_messages_carry_stocks_values()
    {
        var (player, engine) = Spawned();
        player.Armour = 0;

        player.UpdateClientData(1f);

        byte[] Payload(string key) => (byte[])Sends(engine).Single(s => (string)s.Args[0]! == key).Args[4]!;
        Assert.Equal(new byte[] { 0 }, Payload("ResetHUD"));     // WRITE_BYTE( 0 )
        Assert.Equal(new byte[] { 0, 0 }, Payload("Battery"));   // WRITE_SHORT( m_ArmorValue )
        Assert.Equal(new byte[] { 0 }, Payload("Train"));        // TRAIN_NEW & 0xF
    }

    [Fact]
    public void Nothing_more_is_sent_until_something_changes()
    {
        var (player, engine) = Spawned();
        player.UpdateClientData(1f);
        player.UpdateClientData(1.015f);   // the Damage follow-up is not this class's business
        engine.Clear();

        player.UpdateClientData(1.030f);
        player.UpdateClientData(1.045f);

        Assert.Equal("", Keys(engine));
    }

    [Fact]
    public void A_change_of_armour_sends_the_new_battery_once()
    {
        var (player, engine) = Spawned();
        player.UpdateClientData(1f);
        player.UpdateClientData(1.015f);
        engine.Clear();

        player.Armour = 15;
        player.UpdateClientData(1.030f);
        player.UpdateClientData(1.045f);

        var battery = Assert.Single(Sends(engine));
        Assert.Equal("Battery", (string)battery.Args[0]!);
        Assert.Equal(new byte[] { 15, 0 }, (byte[])battery.Args[4]!);
    }

    [Fact]
    public void A_respawn_resets_the_hud_again()
    {
        var (player, engine) = Spawned();
        player.UpdateClientData(1f);
        engine.Clear();

        player.OnSpawn();
        player.UpdateClientData(5f);

        Assert.Contains("ResetHUD", Keys(engine), StringComparison.Ordinal);
        Assert.Contains("Train", Keys(engine), StringComparison.Ordinal);
    }
}
