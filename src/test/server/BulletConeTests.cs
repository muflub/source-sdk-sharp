using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Each hitscan row's cone is stock's GetBulletSpread: VECTOR_CONE_N DEGREES is
/// sin( N / 2 ), so the row's half-angle is half of stock's N (shared/hl2mp:
/// the SMG1's 5, the AR2's 3, the shotgun's 10 for both triggers; the 357
/// fires vec3_origin, straight; the pistol's starts at 1).
///
/// <para>
/// Found against stock (2026-10-25): the SMG1 read 3.0, the AR2 2.0, the 357
/// 0.5 and the pistol 1.0, each a wider cone than stock's.
/// </para>
/// </summary>
public class BulletConeTests
{
    [Theory]
    [InlineData(EntityNames.WeaponPistol, 1f)]
    [InlineData(EntityNames.Weapon357, 0f)]
    [InlineData(EntityNames.WeaponSmg1, 5f)]
    [InlineData(EntityNames.WeaponAr2, 3f)]
    [InlineData(EntityNames.WeaponShotgun, 10f)]
    public void TheRowsConeIsStocks(string weapon, float stockDegrees)
    {
        var type = WeaponCatalog.Stock().For(weapon)!.Value;

        Assert.Equal(stockDegrees / 2f, type.Spread, 3);
    }

    [Fact]
    public void TheShotgunsSecondTriggerIsTheSameTenDegrees()
        => Assert.Equal(5f, WeaponCatalog.Stock().For(EntityNames.WeaponShotgun)!.Value.Secondary!.Value.Spread, 3);
}
