using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

using SpawnFlag = SourceSharp.Server.EnvExplosion.SpawnFlag;
using EffectFlag = SourceSharp.Server.EnvExplosion.EffectFlag;

namespace SourceSharp.Tests;

/// <summary>
/// env_explosion's arithmetic, which is all of it that is a decision.
///
/// <para>
/// Every number here was read off explode.cpp rather than derived. The curve is
/// odd enough that "obviously right" is not available: the sprite scale is
/// negative below magnitude 50 and the two NOCLAMP flags widen the clamp rather
/// than removing it, so a test that guessed would agree with a port that
/// guessed the same way.
/// </para>
/// </summary>
public class EnvExplosionTests
{
    //-------------------------------------------------------------------------
    // Sprite scale: (magnitude - 50) * 0.6, clamped.
    //-------------------------------------------------------------------------

    [Fact]
    public void ABareExplosionScalesItsFireballFromTheMagnitude()
    {
        // 150 -> (150-50)*0.6 = 60, over the default ceiling of 50.
        Assert.True(EnvExplosion.SpriteScale(150, SpawnFlag.None) == 50,
            "a magnitude of 150 clamps to the default ceiling of 50");

        // 100 -> (100-50)*0.6 = 30, inside both clamps and therefore untouched.
        Assert.True(EnvExplosion.SpriteScale(100, SpawnFlag.None) == 30,
            "a magnitude of 100 scales to 30, which no clamp touches");
    }

    [Fact]
    public void TheFloorCatchesEveryMagnitudeBelowStocksKnee()
    {
        // The curve is zero at 50 and NEGATIVE below it. Without the floor a
        // small explosion would send a negative sprite scale onto the wire.
        Assert.True(EnvExplosion.SpriteScale(50, SpawnFlag.None) == 10,
            "magnitude 50 sits exactly on the curve's zero and is floored to 10");

        Assert.True(EnvExplosion.SpriteScale(1, SpawnFlag.None) == 10,
            "a tiny magnitude is floored to 10 rather than going negative");
    }

    [Fact]
    public void TheNoClampFlagsWidenTheClampRatherThanRemovingIt()
    {
        // Stock's own comment: "Don't inhibit clamping altogether. Just relax
        // it a bit." A reader who assumed NOCLAMPMIN meant "no minimum" would
        // expect the raw negative here.
        Assert.True(EnvExplosion.SpriteScale(1, SpawnFlag.NoClampMin) == 1,
            "NOCLAMPMIN relaxes the floor to 1, and does not remove it");

        Assert.True(EnvExplosion.SpriteScale(1000, SpawnFlag.NoClampMax) == 200,
            "NOCLAMPMAX relaxes the ceiling to 200, and does not remove it");
    }

    [Fact]
    public void EachClampFlagLeavesTheOtherEndAlone()
    {
        // The two are independent in stock -- separate ifs, not one branch --
        // so a mapper who sets one still gets the other's default.
        Assert.True(EnvExplosion.SpriteScale(1000, SpawnFlag.NoClampMin) == 50,
            "NOCLAMPMIN does not touch the ceiling, which stays at 50");

        Assert.True(EnvExplosion.SpriteScale(1, SpawnFlag.NoClampMax) == 10,
            "NOCLAMPMAX does not touch the floor, which stays at 10");
    }

    //-------------------------------------------------------------------------
    // Radius.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheRadiusIsDerivedFromTheMagnitudeUnlessTheMapperSaidOtherwise()
    {
        Assert.True(EnvExplosion.EffectiveRadius(100, 0) == 250,
            "an unset radius override derives 2.5 units per point of magnitude");

        Assert.True(EnvExplosion.EffectiveRadius(100, 120) == 120,
            "a radius override is used exactly as the mapper wrote it");
    }

    [Fact]
    public void OnlyAPositiveOverrideCounts()
    {
        // Stock tests `> 0`, not `!= 0`. A negative would otherwise become the
        // radius and the falloff divides by it.
        Assert.True(EnvExplosion.EffectiveRadius(60, -5) == 150,
            "a negative radius override is ignored in favour of the derived radius");
    }

    //-------------------------------------------------------------------------
    // Spawnflags -> TE_EXPLFLAG_*. The translation stock calls stupid.
    //-------------------------------------------------------------------------

    [Fact]
    public void EachSuppressionSpawnflagSetsItsOwnWireBit()
    {
        // Deliberately one at a time: the two vocabularies number differently,
        // so a transposition would survive a test that only checked the union.
        Assert.True(Has(SpawnFlag.NoFireball, EffectFlag.NoFireball),
            "NOFIREBALL sets TE_EXPLFLAG_NOFIREBALL");
        Assert.True(Has(SpawnFlag.NoSound, EffectFlag.NoSound),
            "NOSOUND sets TE_EXPLFLAG_NOSOUND");
        Assert.True(Has(SpawnFlag.RandomOrientation, EffectFlag.Rotate),
            "RND_ORIENT sets TE_EXPLFLAG_ROTATE");
        Assert.True(Has(SpawnFlag.NoParticles, EffectFlag.NoParticles),
            "NOPARTICLES sets TE_EXPLFLAG_NOPARTICLES");
        Assert.True(Has(SpawnFlag.NoDynamicLights, EffectFlag.NoDynamicLights),
            "NODLIGHTS sets TE_EXPLFLAG_NODLIGHTS");
        Assert.True(Has(SpawnFlag.NoFireballSmoke, EffectFlag.NoFireballSmoke),
            "NOFIREBALLSMOKE sets TE_EXPLFLAG_NOFIREBALLSMOKE");
    }

    private static bool Has(SpawnFlag flag, EffectFlag expected)
        => EnvExplosion.EffectFlags(flag, RenderMode.TransAdd).HasFlag(expected);

    [Fact]
    public void SpawnflagsWithNoWireBitOfTheirOwnSendNothing()
    {
        // NOSMOKE, NODECAL, NOSPARKS, NODAMAGE, SURFACEONLY, REPEATABLE and the
        // two clamp flags are all decided SERVER-side. None of them is a temp
        // entity bit, and a port that mapped one onto a TE flag by name would
        // suppress the wrong half of the effect.
        SpawnFlag serverOnly = SpawnFlag.NoSmoke | SpawnFlag.NoDecal | SpawnFlag.NoSparks
                             | SpawnFlag.NoDamage | SpawnFlag.SurfaceOnly | SpawnFlag.Repeatable
                             | SpawnFlag.NoClampMin | SpawnFlag.NoClampMax
                             | SpawnFlag.GenericDamage;

        Assert.True(EnvExplosion.EffectFlags(serverOnly, RenderMode.TransAdd) == EffectFlag.None,
            "the nine server-side spawnflags put nothing at all on the wire");
    }

    [Fact]
    public void TheRenderModeDecidesBetweenAlphaAndAdditive()
    {
        Assert.True(EnvExplosion.EffectFlags(SpawnFlag.None, RenderMode.TransAlpha) == EffectFlag.DrawAlpha,
            "a TransAlpha explosion draws alpha");

        Assert.True(EnvExplosion.EffectFlags(SpawnFlag.None, RenderMode.TransAdd) == EffectFlag.None,
            "a TransAdd explosion sets neither the alpha nor the non-additive bit");
    }

    [Fact]
    public void EveryOtherRenderModeIsExplicitlyNonAdditive()
    {
        // This is the clause that catches a bare map-placed env_explosion: its
        // render mode is Normal, so it sends NOADDITIVE while an explosion
        // created in code sends neither bit. The two look different on screen
        // and stock has it exactly this way.
        Assert.True(EnvExplosion.EffectFlags(SpawnFlag.None, RenderMode.Normal) == EffectFlag.NoAdditive,
            "a mapper who set no render mode gets a non-additive fireball");

        Assert.True(EnvExplosion.EffectFlags(SpawnFlag.None, RenderMode.Glow) == EffectFlag.NoAdditive,
            "every render mode that is neither TransAlpha nor TransAdd is non-additive");
    }

    //-------------------------------------------------------------------------
    // Damage type.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnExplosionDealsBlastDamageUnlessToldOtherwise()
    {
        Assert.True(EnvExplosion.DamageTypeFor(SpawnFlag.None, null) == DamageType.Blast,
            "a bare explosion deals blast damage");

        Assert.True(EnvExplosion.DamageTypeFor(SpawnFlag.GenericDamage, null) == DamageType.Generic,
            "the GENERIC_DAMAGE spawnflag downgrades the blast to generic damage");
    }

    [Fact]
    public void ACallerThatNamedADamageTypeWins()
    {
        // ExplosionCreate's iCustomDamageType. Stock spells "none" as -1 and
        // the port spells it as null, but the precedence is the same: a named
        // type beats the spawnflag.
        Assert.True(EnvExplosion.DamageTypeFor(SpawnFlag.GenericDamage, DamageType.EnergyBeam) == DamageType.EnergyBeam,
            "a custom damage type overrides even the GENERIC_DAMAGE spawnflag");
    }

    [Fact]
    public void SurfaceOnlyIsAddedToWhateverTheDamageAlreadyWas()
    {
        // Additive, not exclusive -- it is OR'd on after the choice, so it must
        // survive alongside both arms.
        Assert.True(EnvExplosion.DamageTypeFor(SpawnFlag.SurfaceOnly, null)
                    == (DamageType.Blast | DamageType.BlastSurface),
            "SURFACEONLY adds the surface bit to a blast rather than replacing it");

        Assert.True(EnvExplosion.DamageTypeFor(SpawnFlag.SurfaceOnly | SpawnFlag.GenericDamage, null)
                    == (DamageType.Generic | DamageType.BlastSurface),
            "SURFACEONLY adds the surface bit to generic damage too");
    }

    //-------------------------------------------------------------------------
    // What the sprite scale becomes on the wire.
    //-------------------------------------------------------------------------

    [Fact]
    public void ASuppressedFireballIsSentAsZeroRatherThanNotSent()
    {
        // The same temp entity carries the sound, the particles and the
        // dynamic lights, so a suppressed fireball still has to be sent -- at
        // scale zero. Skipping the message would silence all four.
        Assert.True(EnvExplosion.WireScale(50, SpawnFlag.NoFireball) == 0f,
            "NOFIREBALL sends the temp entity with a zero-scale sprite");

        Assert.True(EnvExplosion.WireScale(50, SpawnFlag.None) == 5f,
            "the wire scale is a tenth of the sprite scale");
    }
}
