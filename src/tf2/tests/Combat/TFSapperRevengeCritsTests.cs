using SourceSharp.Abi;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Tests.Player;
using TF2.Tests.Rules;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// <see cref="TFSapperRevengeCrits"/>: the bucket behind stock's
/// <c>CTFPlayerShared::SetRevengeCrits</c> (<c>tf_player_shared.cpp:14472-14488</c>) —
/// the store, its clamp, the <c>TF_COND_CRITBOOSTED</c> toggle behind
/// <c>CanHaveRevengeCrits</c>' sapper arm (<c>tf_weaponbase.cpp:6647-6652</c>),
/// and the revolver's spend (:159-171).
/// </summary>
public sealed class TFSapperRevengeCritsTests
{
    private static (PlayerRig Rig, TFPlayerConditions Conditions, FakeOwners Owners, TFSapperRevengeCrits Bucket)
        AComposedPlayer(bool collecting)
    {
        var rig = new PlayerRig();
        rig.Join();
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>());
        var owners = new FakeOwners();
        owners.Held[PlayerRig.Index] = "tf_weapon_revolver";
        if (collecting)
        {
            var item = new AttributeContainer();
            item.Set(AttributeValue.OfFloat(new AttributeDefinition(30062, "sapper crits", "sapper_kills_collect_crits",
                                                                    AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                    false, false, AttributeValueType.Default,
                                                                    MergedKeys.Block("s")), 1f));
            owners.Items[PlayerRig.Index] = item;
        }
        return (rig, conditions, owners, new TFSapperRevengeCrits(conditions, rig.Engine, owners, new AttributeHooks()));
    }

    /// <summary>The grant (tf_obj.cpp:269-283): the owner of the sapper riding the
    /// removed building takes one crit, and under the collecting item the first
    /// stored crit adds the boost (:14479-14482).</summary>
    [Fact]
    public void AGrantedCritBoostsACollectingHolder()
    {
        var (_, conditions, _, bucket) = AComposedPlayer(collecting: true);

        bucket.Grant(PlayerRig.Player);

        Assert.Equal(1, bucket.Count(PlayerRig.Index));
        Assert.True(conditions.InCond(PlayerRig.Player, TFCond.Critboosted));
    }

    /// <summary>The gate before the boost (tf_weaponbase.cpp:6647-6652): without the
    /// held item's <c>sapper_kills_collect_crits</c> the bucket still stores, but
    /// nothing boosts (:14476-6652's zero-answer).</summary>
    [Fact]
    public void AGrantedCritStandsPlainForAPlainHolder()
    {
        var (_, conditions, _, bucket) = AComposedPlayer(collecting: false);

        bucket.Grant(PlayerRig.Player);

        Assert.Equal(1, bucket.Count(PlayerRig.Index));
        Assert.False(conditions.InCond(PlayerRig.Player, TFCond.Critboosted));
    }

    /// <summary>The spend (tf_weapon_revolver.cpp:159-171, gate header :51): the shot
    /// under the collecting item pays one stored crit, and the last one's removal
    /// takes the boost (:14483-14486).</summary>
    [Fact]
    public void ASpentCritTakesTheBoostAwayWithIt()
    {
        var (_, conditions, _, bucket) = AComposedPlayer(collecting: true);
        bucket.Grant(PlayerRig.Player);

        bucket.SpendOnShot(PlayerRig.Index);

        Assert.Equal(0, bucket.Count(PlayerRig.Index));
        Assert.False(conditions.InCond(PlayerRig.Player, TFCond.Critboosted));
    }

    /// <summary>The same gate before the spend: a plain held item does not collect,
    /// and its shots spend nothing (:51's zero answers no).</summary>
    [Fact]
    public void AShotByAPlainHolderSpendsNothing()
    {
        var (_, _, _, bucket) = AComposedPlayer(collecting: false);
        bucket.Grant(PlayerRig.Player);
        Assert.Equal(1, bucket.Count(PlayerRig.Index));

        // The plain held item: the gate answers no, the bucket keeps its crit.
        bucket.SpendOnShot(PlayerRig.Index);
        Assert.Equal(1, bucket.Count(PlayerRig.Index));
    }

    /// <summary>The clamp (tf_player_shared.cpp:14474): the bucket rides its ceiling
    /// of 35 however many grants land.</summary>
    [Fact]
    public void TheBucketClampsAtThirtyFiveStoredCrits()
    {
        var (_, _, _, bucket) = AComposedPlayer(collecting: true);

        for (int i = 0; i < 40; i++)
            bucket.Grant(PlayerRig.Player);

        Assert.Equal(TFSapperRevengeCrits.MaxStoredCrits, bucket.Count(PlayerRig.Index));
        Assert.Equal(35, TFSapperRevengeCrits.MaxStoredCrits);
    }
}
