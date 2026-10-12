using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The six emitters whose whole job is a decision followed by a temp entity.
///
/// <para>
/// The temp entity cannot be checked from here — it goes to the wire and there
/// is no engine — so what is checked is everything on this side of it: which
/// keys were read, which spawnflag bits mean what, and the two pieces of
/// arithmetic that are easy to get subtly wrong and impossible to notice in
/// play. A splash whose depth curve is off by a factor draws at the wrong size
/// in deep water; a bubble interval whose cliff is on the wrong side of 19
/// fizzes at half the rate the mapper tuned. Neither reports anything.
/// </para>
/// </summary>
public class SmallEffectTests
{
    private static readonly EntityHandle Ent = new(0x0041_0041u);

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.001f;

    private static (ScriptedEntityRegistry, T) SpawnWith<T>(string className,
        params (string Key, string Value)[] keys) where T : ScriptedEntity
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvBlood).Assembly);
        registry.Services = Ignoring("Precache.PrecacheModel");

        // Keys before spawn, which is the order Source uses.
        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, className, key, value);

        registry.Spawn(Ent, className);

        return (registry, (T)registry.Get(Ent)!);
    }

    /// <summary>
    /// The spawnflag-to-effect mapping, which is two sets of bit values that
    /// have to line up: Source's FX_BLOODSPRAY_* on one side and env_blood's own
    /// spawnflags on the other. They are NOT the same numbers, and nothing in
    /// play would show a transposition — the wrong blood effect still looks like
    /// blood.
    /// </summary>
    [Fact]
    public void BloodSprayFlags()
    {
        Assert.True(EnvBlood.SprayFor(EnvBlood.SpawnFlag.None) == BloodSpray.None, "no spray flags means no spray");

        // 16 is SF_BLOOD_CLOUD, and FX_BLOODSPRAY_CLOUD is 0x04. The two
        // numbers being different is the whole reason this is tested.
        Assert.True(EnvBlood.SprayFor(EnvBlood.SpawnFlag.Cloud) == BloodSpray.Cloud, "the cloud flag asks for the cloud");

        Assert.True(EnvBlood.SprayFor(EnvBlood.SpawnFlag.Drops) == BloodSpray.Drops, "the drops flag asks for the drops");

        Assert.True(EnvBlood.SprayFor(EnvBlood.SpawnFlag.Gore) == BloodSpray.Gore, "the gore flag asks for the gore");

        Assert.True(EnvBlood.SprayFor(EnvBlood.SpawnFlag.Cloud | EnvBlood.SpawnFlag.Drops | EnvBlood.SpawnFlag.Gore)
                == (BloodSpray.Cloud | BloodSpray.Drops | BloodSpray.Gore), "all three together are all three");

        // The other spawnflags share the same word and must not leak in.
        Assert.True(EnvBlood.SprayFor(EnvBlood.SpawnFlag.Random | EnvBlood.SpawnFlag.Stream | EnvBlood.SpawnFlag.Player | EnvBlood.SpawnFlag.Decal) == BloodSpray.None, "the stream and decal flags are not spray effects");
    }

    /// <summary>
    /// The "color" key is an INDEX with exactly one recognised value, and every
    /// other number silently means red — including numbers that name real blood
    /// colours in the same engine enum. Kept from stock, so it is worth pinning.
    /// </summary>
    [Fact]
    public void BloodColourKey()
    {
        var (registry, blood) = SpawnWith<EnvBlood>(EntityNames.EnvBlood,
            ("color", "1"), ("amount", "50"));

        Assert.True(blood.Colour == BloodColor.Yellow, "colour 1 is yellow");

        registry.Clear();

        (_, blood) = SpawnWith<EnvBlood>(EntityNames.EnvBlood, ("color", "2"));
        Assert.True(blood.Colour == BloodColor.Red, "colour 2 is red, as stock leaves it");

        registry.Clear();

        (_, blood) = SpawnWith<EnvBlood>(EntityNames.EnvBlood);
        Assert.True(blood.Colour == BloodColor.Red, "no colour key at all is red");
    }

    /// <summary>
    /// How big a splash a given depth is worth. The curve is linear to 120 units
    /// and goes NEGATIVE past it, which is deliberate: the caller's floor is
    /// what rejects the small ones, and clamping here would draw a minimum-size
    /// splash where stock draws none.
    /// </summary>
    [Fact]
    public void SplashDepth()
    {
        Assert.True(Near(EnvSplash.DepthFactor(0f), 1f), "a splash at the surface is full size");

        Assert.True(Near(EnvSplash.DepthFactor(60f), 0.5f), "half the maximum depth is half the size");

        Assert.True(Near(EnvSplash.DepthFactor(120f), 0f), "at the maximum depth there is nothing left");

        Assert.True(EnvSplash.DepthFactor(240f) < 0f, "deeper than that goes negative rather than clamping");

        var (_, splash) = SpawnWith<EnvSplash>(EntityNames.EnvSplash, ("scale", "8"));
        Assert.True(Near(splash.Scale, 8f), "a splash reads its scale");
    }

    /// <summary>
    /// The bubble rate, whose key is called "frequency" and is neither a
    /// frequency nor seconds. The cliff at 19/20 is the part to pin: it is
    /// invisible in play and would be silently smoothed away by anybody
    /// "tidying" the arithmetic.
    /// </summary>
    [Fact]
    public void BubbleInterval()
    {
        Assert.True(Near(EnvBubbles.IntervalFor(0), 2.5f), "frequency 0 waits two and a half seconds");

        Assert.True(Near(EnvBubbles.IntervalFor(10), 1.5f), "frequency 10 waits a second and a half");

        Assert.True(Near(EnvBubbles.IntervalFor(19), 0.6f), "frequency 19 is the last of the linear range");

        Assert.True(Near(EnvBubbles.IntervalFor(20), 0.5f), "frequency 20 jumps to half a second");

        Assert.True(Near(EnvBubbles.IntervalFor(1000), 0.5f), "and stays there however large it gets");

        var (_, bubbles) = SpawnWith<EnvBubbles>(EntityNames.EnvBubbles,
            ("density", "12"), ("current", "40"), ("frequency", "20"));

        Assert.True(bubbles.Density == 12, "bubbles read their density");
        Assert.True(Near(bubbles.Current, 40f), "and the current that drags them");
    }

    /// <summary>
    /// A tracer's delay is CLAMPED rather than defaulted: a map that wrote 0
    /// gets one second, not a streak every tick.
    /// </summary>
    [Fact]
    public void TracerDelay()
    {
        var (_, tracer) = SpawnWith<EnvTracer>(EntityNames.EnvTracer,
            ("delay", "0"), ("target", "somewhere"));

        Assert.True(Near(tracer.Delay, 1f), "a zero delay becomes one second");

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvBlood).Assembly);
        registry.Services = Ignoring("Precache.PrecacheModel");
        registry.KeyValue(Ent, EntityNames.EnvTracer, "delay", "0.25");
        registry.Spawn(Ent, EntityNames.EnvTracer);

        Assert.True(Near(((EnvTracer)registry.Get(Ent)!).Delay, 0.25f), "a delay the map set is left alone");
    }

    /// <summary>
    /// The two entities with almost nothing in them, checked for the one thing
    /// each could get wrong: a funnel that reads its spawnflag backwards, and a
    /// dust puff that drops the keys it is meant to claim.
    /// </summary>
    [Fact]
    public void FunnelAndDustKeys()
    {
        var (_, dust) = SpawnWith<EnvDustPuff>(EntityNames.EnvDustpuff,
            ("scale", "16"), ("speed", "48"), ("color", "255 0 0"));

        Assert.True(Near(dust.Scale, 16f), "a dust puff reads its scale");
        Assert.True(Near(dust.Speed, 48f), "and its speed");

        // Claimed and unused, exactly as stock. The point of the field is that
        // the key is not reported as a mapper's typo.
        Assert.True(dust.ColourKey == "255 0 0", "the colour key is claimed even though nothing sends it");

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvBlood).Assembly);
        registry.Services = Ignoring("Precache.PrecacheModel");
        registry.Spawn(Ent, EntityNames.EnvFunnel);

        Assert.True(registry.Get(Ent) is EnvFunnel, "a funnel exists once spawned");
    }

    /// <summary>
    /// The only member of the particle, trail and smoke family that crossed.
    ///
    /// <para>
    /// Its two keys and the constant it fires with. The type matters more than
    /// it looks: MUZZLEFLASH_TYPE_DEFAULT is 1 and the enum beside it in
    /// shareddefs.h starts at 0, so a "default is zero" tidy-up would send
    /// MUZZLEFLASH_COMBINE and still draw a muzzle flash.
    /// </para>
    ///
    /// <para>
    /// The parentattachment key is claimed and unused. There is no parenting on
    /// the parallel hierarchy for it to name a point on, and a claimed key is
    /// the difference between a documented divergence and a mapper being told
    /// their map has a typo in it.
    /// </para>
    /// </summary>
    [Fact]
    public void MuzzleFlashKeys()
    {
        var (_, flash) = SpawnWith<EnvMuzzleFlash>(EntityNames.EnvMuzzleflash,
            ("scale", "4"), ("parentattachment", "muzzle"));

        Assert.True(Near(flash.Scale, 4f), "a muzzle flash reads its scale");

        Assert.True(((EnvMuzzleFlashVars)flash.Vars).ParentAttachment == "muzzle", "the attachment key is claimed even though nothing can use it");

        Assert.True(EnvMuzzleFlash.DefaultType == 1, "the flash fires as MUZZLEFLASH_TYPE_DEFAULT, which is 1 and not 0");
    }
}
