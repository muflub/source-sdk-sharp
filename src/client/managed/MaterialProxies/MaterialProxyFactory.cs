using SourceSharp.Abi;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>
/// The proxies client.so answers by name (<c>&lt;Name&gt;_IMaterialProxy003</c>):
/// every name the 24 stock proxy TUs and shell_materialproxies.cpp exposed,
/// each with the managed class that does what the stock one did. Native's
/// trampoline registers the same list (shell_materialproxies.cpp's
/// <c>s_ProxyNames</c>); MaterialProxyFactoryTests holds the two equal.
/// </summary>
public sealed class MaterialProxyFactory
{
    private readonly Dictionary<string, Func<MaterialProxy>> _make;

    public MaterialProxyFactory(ILog log, IProxyMath math, Func<bool> developer, IPostProxyValues? post = null)
    {
        IPostProxyValues postValues = post ?? new NoPostValues();
        _make = new Dictionary<string, Func<MaterialProxy>>(StringComparer.Ordinal)
        {
            // mathproxy.cpp
            ["Add"] = () => new AddProxy(),
            ["Subtract"] = () => new SubtractProxy(),
            ["Multiply"] = () => new MultiplyProxy(),
            ["Divide"] = () => new DivideProxy(),
            ["Clamp"] = () => new ClampProxy(),
            ["Sine"] = () => new SineProxy(),
            ["Equals"] = () => new EqualsProxy(),
            ["Frac"] = () => new FracIntProxy(frac: true),
            ["Int"] = () => new FracIntProxy(frac: false),
            ["LinearRamp"] = () => new LinearRampProxy(),
            ["UniformNoise"] = () => new UniformNoiseProxy(),
            ["GaussianNoise"] = () => new GaussianNoiseProxy(),
            ["Exponential"] = () => new ExponentialProxy(math),
            ["Abs"] = () => new AbsProxy(),
            ["Empty"] = () => new EmptyProxy(),
            ["LessOrEqual"] = () => new LessOrEqualProxy(),
            ["WrapMinMax"] = () => new WrapMinMaxProxy(),
            ["SelectFirstIfNonZero"] = () => new SelectFirstIfNonZeroProxy(),
            // timematerialproxy.cpp, matrixproxy.cpp
            ["CurrentTime"] = () => new CurrentTimeProxy(),
            ["TextureTransform"] = () => new TextureTransformProxy(math),
            ["MatrixRotate"] = () => new MatrixRotateProxy(math),
            // the animated-texture family, toggletextureproxy.cpp, texturescrollmaterialproxy.cpp
            ["AnimatedTexture"] = () => new AnimatedTextureProxy(),
            ["AnimatedEntityTexture"] = () => new AnimatedEntityTextureProxy(),
            ["AnimatedOffsetTexture"] = () => new AnimatedOffsetTextureProxy(),
            ["AnimateSpecificTexture"] = () => new AnimateSpecificTextureProxy(),
            ["ToggleTexture"] = () => new ToggleTextureProxy(),
            ["TextureScroll"] = () => new TextureScrollProxy(),
            // the entity proxies
            ["EntityOrigin"] = () => new EntityOriginProxy(0f),
            ["EntityOriginAlyx"] = () => new EntityOriginProxy(15.0f),
            ["Ep1IntroVortRefract"] = () => new Ep1IntroVortRefractProxy(),
            ["Alpha"] = () => new AlphaProxy(),
            ["IsNPC"] = () => new IsNpcProxy(),
            ["Health"] = () => new HealthProxy(),
            ["lampbeam"] = () => new LampProxy(halo: false),
            ["lamphalo"] = () => new LampProxy(halo: true),
            ["Pupil"] = () => new PupilProxy(log),
            ["Camo"] = () => new CamoProxy(),
            // WaterLODMaterialProxy.cpp, WorldDimsProxy.cpp, dummyproxy.cpp
            ["WaterLOD"] = () => new WaterLodProxy(),
            ["WorldDims"] = () => new WorldDimsProxy(),
            ["Dummy"] = () => new DummyProxy(log, developer),
            // shell_materialproxies.cpp's four
            ["Shield"] = () => new NullProxy(),
            ["HeliBlade"] = () => new NullProxy(),
            ["VortEmissive"] = () => new NullProxy(),
            ["PlayerLogo"] = () => new PlayerLogoProxy(),
            // viewpostprocess.cpp's two (C7: the post program's values)
            ["MotionBlur"] = () => new MotionBlurProxy(postValues),
            ["engine_post"] = () => new EnginePostProxy(postValues),
            // particle_proxies.cpp
            ["ParticleSphereProxy"] = () => new ParticleSphereProxy(),
        };
    }

    /// <summary>The names, in no particular order.</summary>
    public IReadOnlyCollection<string> Names => _make.Keys;

    /// <summary>A fresh proxy for <paramref name="name"/>, or null for a name this client does not answer.</summary>
    public MaterialProxy? Create(string name) => _make.TryGetValue(name, out Func<MaterialProxy>? make) ? make() : null;
}

/// <summary>No post program: the values a fresh one would bind (zeros, bloom on).</summary>
internal sealed class NoPostValues : IPostProxyValues
{
    public Render.MotionBlurValues MotionBlur => default;

    public Render.EnginePostValues EnginePost => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);
}
