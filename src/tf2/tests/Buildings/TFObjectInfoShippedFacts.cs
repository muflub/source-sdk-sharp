using TF2.Server.Buildings;
using TF2.Shared;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The engineer's object rows against the shipped <c>scripts/objects.txt</c> (tf/tf2_misc_dir.vpk, sha256
/// 4e7057487dab8f5d..., read with tools/vpk-extract on 2026-11-20): every buildable's <c>UpgradeCost</c> is 200 and
/// its <c>UpgradeDuration</c> 1.5 s. The rows had the teleporter's upgrade at 100 metal and the upgrades at 20 s (the
/// sentry's 10 s), so an upgrading sentry stood silent for 10 s where stock's stands for 1.5 s (tf_obj.cpp:3044-3048
/// reads the row's duration).
/// </summary>
public class TFObjectInfoShippedFacts
{
    [Fact]
    public void TheTeleporterUpgradeCostsTwoHundredMetalAsShipped()
        => Assert.Equal(200, TFObjectInfo.Of(TFObjectType.Teleporter)!.UpgradeCost);

    [Fact]
    public void EveryBuildableUpgradesInOneAndAHalfSecondsAsShipped()
    {
        Assert.Equal(1.5f, TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeDuration);
        Assert.Equal(1.5f, TFObjectInfo.Of(TFObjectType.Dispenser)!.UpgradeDuration);
        Assert.Equal(1.5f, TFObjectInfo.Of(TFObjectType.Teleporter)!.UpgradeDuration);
    }
}
