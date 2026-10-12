using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Where a hit came FROM is its inflictor (stock's m_DmgOrigin =
/// info.GetInflictor()->GetAbsOrigin()), which is not always who is credited:
/// a grenade's blast comes from the grenade and is scored to its thrower. The
/// managed damage path carries the inflictor alongside the call
/// (<see cref="DamageInflictors"/>), and the player's Damage message reads it.
/// </summary>
public class DamageInflictorTests
{
    private static readonly EntityHandle Thrower = new(0x020007);
    private static readonly EntityHandle Grenade = new(0x030009);
    private static readonly Vector ThrowerAt = new(500f, 0f, 0f);
    private static readonly Vector GrenadeAt = new(-40f, 60f, 0f);

    private static (SsPlayer Player, RecordingEngine Engine) Rig(DamageInflictors inflictors)
    {
        var engine = Fake();
        engine.Returns("Entities.GetHealth", 100).Returns("Entities.GetIndex", 3)
              .Returns("Entities.IsValid", true).Returns("Entities.GetOrigin", ThrowerAt);
        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid(), messages: engine, inflictors: inflictors)
            { Handle = new EntityHandle(0x010003) };
        player.BindAreas(Areas(engine));
        player.State.BindLocal();
        player.UpdateClientData(0f);
        engine.Clear();
        return (player, engine);
    }

    private static Vector SentOrigin(RecordingEngine engine)
    {
        var send = engine.AllOf("UserMessages.Send").Single(c => (string)c.Args[0]! == "Damage");
        var reader = new UserMessageReader((byte[])send.Args[4]!);
        return Damage.Read(ref reader).Origin;
    }

    [Fact]
    public void A_blast_comes_from_its_inflictor_not_its_thrower()
    {
        var inflictors = new DamageInflictors();
        var (player, engine) = Rig(inflictors);

        using (inflictors.Push(Grenade, GrenadeAt))
            player.OnDamaged(Thrower, 20f, DamageType.Blast);
        player.UpdateClientData(1f);

        Assert.Equal(GrenadeAt, SentOrigin(engine));
    }

    [Fact]
    public void Without_an_inflictor_the_attacker_is_where_it_came_from()
    {
        var inflictors = new DamageInflictors();
        var (player, engine) = Rig(inflictors);

        using (inflictors.Push(Grenade, GrenadeAt)) { }   // a scope that ended is gone
        player.OnDamaged(Thrower, 20f, DamageType.Bullet);
        player.UpdateClientData(1f);

        Assert.Equal(ThrowerAt, SentOrigin(engine));
    }

    [Fact]
    public void Scopes_nest()
    {
        var inflictors = new DamageInflictors();
        using (inflictors.Push(Thrower, ThrowerAt))
        {
            using (inflictors.Push(Grenade, GrenadeAt))
                Assert.True(inflictors.TryCurrent(out _, out var inner) && inner.Equals(GrenadeAt));
            Assert.True(inflictors.TryCurrent(out _, out var outer) && outer.Equals(ThrowerAt));
        }
        Assert.False(inflictors.TryCurrent(out _, out _));
    }
}
