using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// <c>RecieveTeleportingPlayer</c>'s telefrag (tf_obj_teleporter.cpp:927-961,
/// :1012-1015): the box the arrival will occupy — the deck position, the
/// player hull and four units of slack — is swept, and every enemy player in
/// it takes 1000 DMG_CRUSH with TF_DMG_CUSTOM_TELEFRAG from the traveller,
/// before the traveller is moved.
/// </summary>
public class TFTeleporterTelefragFacts
{
    /// <summary>The box the exit sweeps for the deck arrival of this port.</summary>
    private static (Vector Mins, Vector Maxs) DeckBox()
    {
        Vector expand = new(4f, 4f, 4f);
        Vector deck = new(0f, 0f, TFTeleporter.ArrivalHeight);   // both ends stand at the origin
        return (deck + TF2.Shared.Movement.TFViewVectors.Params.HullMin - expand,
                deck + TF2.Shared.Movement.TFViewVectors.Params.HullMax + expand);
    }

    /// <summary>A booked rider and a full trip's worth of thinking.</summary>
    private static void Trip(BuildingsRig rig, TFTeleporter entrance, TFTeleporter exit)
    {
        EntityHandle rider = TeleporterHarness.AStandingRider(rig);
        entrance.OnTouch(rider);
        TeleporterHarness.Tick(rig, entrance, exit, 11f);
    }

    private static int FirstCall(BuildingsRig rig, string member)
    {
        for (int i = 0; i < rig.Engine.Calls.Count; i++)
            if (rig.Engine.Calls[i].Member == member)
                return i;
        return -1;
    }

    [Fact]
    public void AnEnemyInTheArrivalBoxIsCrushedByTheTraveller()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        TeleporterHarness.Tick(rig, entrance, exit, 1f);   // the pair to READY

        (EntityHandle enemy, _) = rig.Players.Add(TFTeam.Blue);
        rig.FillsSphere(enemy);
        (TFDamageCustom, TFWeaponId, string)? scope = null;
        rig.Engine.WhenCalled("Damage.TakeDamageAnswered", _ =>
        {
            rig.Scopes.TryCurrent(out var custom, out var weapon, out var name);
            scope = (custom, weapon, name);
        });

        EntityHandle rider = TeleporterHarness.AStandingRider(rig);
        entrance.OnTouch(rider);
        TeleporterHarness.Tick(rig, entrance, exit, 11f);   // the send, the fades, the arrival

        EngineCall blow = Assert.Single(rig.Engine.AllOf("Damage.TakeDamageAnswered"));
        Assert.Equal(enemy, blow.ArgAs<EntityHandle>(0));
        Assert.Equal(rider, blow.ArgAs<EntityHandle>(1));   // attacker and infliction are the traveller
        Assert.Equal(1000f, blow.ArgAs<float>(2), 1f);
        Assert.Equal(DamageType.Crush, blow.ArgAs<DamageType>(3));
        Assert.Equal((TFDamageCustom.Telefrag, TFWeaponId.None, ""), scope);

        // Stock kills before the move (:1012-1017) — and the traveller arrives
        // through the fragged box.
        int hurt = FirstCall(rig, "Damage.TakeDamageAnswered");
        int trip = FirstCall(rig, "Entities.Teleport");
        Assert.True(hurt >= 0);
        Assert.True(hurt < trip);
        EngineCall arrival = Assert.Single(rig.Engine.AllOf("Entities.Teleport"));
        Assert.Equal(rider, arrival.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void TheSweepIsTheArrivalBoxWithFourUnitsOfSlack()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        TeleporterHarness.Tick(rig, entrance, exit, 1f);

        (EntityHandle enemy, _) = rig.Players.Add(TFTeam.Blue);
        rig.FillsSphere(enemy);
        Trip(rig, entrance, exit);

        EngineCall sweep = Assert.Single(rig.Engine.AllOf("WorldQuery.EntitiesInSphere"));
        (Vector mins, Vector maxs) = DeckBox();
        Assert.Equal((mins + maxs) * 0.5f, sweep.ArgAs<Vector>(0), (a, b) => (a - b).Length < 1e-3f);
        Assert.Equal((maxs - mins).Length * 0.5f, sweep.ArgAs<float>(1), 3f);
    }

    [Fact]
    public void ATeammateInTheBoxIsNotHurt()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        TeleporterHarness.Tick(rig, entrance, exit, 1f);

        (EntityHandle mate, _) = rig.Players.Add();   // the builder's team, like the rider
        rig.FillsSphere(mate);

        Trip(rig, entrance, exit);

        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
        Assert.Equal(1, rig.Engine.CountOf("Entities.Teleport"));   // the trip still completes
    }

    [Fact]
    public void AnEnemyOutsideTheBoxIsNotHurt()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        TeleporterHarness.Tick(rig, entrance, exit, 1f);

        // In the sphere superset, out of the box: the retest is what spares
        // them, so the sweep must find them for this to mean anything.
        (EntityHandle enemy, _) = rig.Players.Add(TFTeam.Blue);
        rig.Origins[enemy] = new Vector(100f, 0f, 0f);
        rig.FillsSphere(enemy);

        Trip(rig, entrance, exit);

        Assert.Equal(1, rig.Engine.CountOf("WorldQuery.EntitiesInSphere"));   // the sweep ran, found them
        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
        Assert.Equal(1, rig.Engine.CountOf("Entities.Teleport"));
    }
}
