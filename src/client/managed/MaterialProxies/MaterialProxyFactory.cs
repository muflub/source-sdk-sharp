using SourceSharp.Abi;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>
/// The proxies client.so answers by name (<c>&lt;Name&gt;_IMaterialProxy003</c>):
/// every name the 24 stock proxy TUs and shell_materialproxies.cpp exposed,
/// each with the managed class that does what the stock one did. Native's
/// trampoline registers the same list (shell_materialproxies.cpp's
/// <c>s_ProxyNames</c>) plus the names the mods' providers answer;
/// MaterialProxyFactoryTests holds the two equal.
/// </summary>
public sealed class MaterialProxyFactory
{
    private readonly Dictionary<string, Func<MaterialProxy>> _make;
    private readonly Func<IReadOnlyList<IMaterialProxyProvider>>? _providers;
    private IReadOnlyList<IMaterialProxyProvider>? _asked;

    public MaterialProxyFactory(ILog log, IProxyMath math, Func<bool> developer, IPostProxyValues? post = null,
                                Render.Shadows.IShadowProxySource? shadows = null,
                                Func<IReadOnlyList<IMaterialProxyProvider>>? providers = null,
                                IPlayerProxyEyes? eyes = null)
    {
        _providers = providers;
        IPostProxyValues postValues = post ?? new NoPostValues();
        Render.Shadows.IShadowProxySource shadowSource = shadows ?? new NoShadows();
        IPlayerProxyEyes eyesOf = eyes ?? new NoPlayerProxyEyes();
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
            // proxyplayer.cpp's seven (the console asked for "PlayerTeamMatch" and
            // "PlayerProximity" by name on ctf_2fort; the file's other exposed
            // names land with them — PlayerLogoOnModel is commented out in stock,
            // "This is here for reference, but we don't want people to use it")
            ["PlayerProximity"] = () => new PlayerProximityProxy(eyesOf),
            ["PlayerTeamMatch"] = () => new PlayerTeamMatchProxy(eyesOf),
            ["PlayerView"] = () => new PlayerViewProxy(eyesOf),
            ["PlayerSpeed"] = () => new PlayerSpeedProxy(eyesOf),
            ["PlayerPosition"] = () => new PlayerPositionProxy(eyesOf),
            ["EntitySpeed"] = () => new EntitySpeedProxy(eyesOf),
            ["EntityRandom"] = () => new EntityRandomProxy(),
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
            // clientshadowmgr.cpp's two (C7 cluster 12 S2; Q1, "move to c#")
            ["Shadow"] = () => new ShadowProxy(shadowSource),
            ["ShadowModel"] = () => new ShadowModelProxy(shadowSource),
        };
    }

    /// <summary>The names, in no particular order.</summary>
    public IReadOnlyCollection<string> Names => _make.Keys;

    /// <summary>
    /// A fresh proxy for <paramref name="name"/>, or null for a name this client does not
    /// answer. The core's table first, then the providers, in registration order — the
    /// first ask that resolves them refuses a name two sides define, so a material's
    /// answer never depends on the order its definers loaded.
    /// </summary>
    public MaterialProxy? Create(string name)
    {
        if (_make.TryGetValue(name, out Func<MaterialProxy>? make))
            return make();

        var providers = _providers?.Invoke() ?? [];
        if (providers.Count == 0)
            return null;

        if (!ReferenceEquals(providers, _asked))
        {
            RefuseRepeatedNames(providers);
            _asked = providers;
        }

        foreach (IMaterialProxyProvider provider in providers)
        {
            MaterialProxy? proxy = provider.Create(name);
            if (proxy is not null)
                return proxy;
        }
        return null;
    }

    /// <summary>A provider whose names repeat the core's or another provider's is refused, naming both.</summary>
    private void RefuseRepeatedNames(IReadOnlyList<IMaterialProxyProvider> providers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string core in _make.Keys)
            seen.Add(core);
        foreach (IMaterialProxyProvider provider in providers)
        {
            foreach (string name in provider.Names)
            {
                if (!seen.Add(name))
                    throw new InvalidOperationException(
                        $"material proxy \"{name}\" is defined twice: {provider.GetType().FullName} and " +
                        "the core's table or a provider registered before it. A material's answer must not " +
                        "depend on the order its definers loaded.");
            }
        }
    }
}

/// <summary>No shadow manager: no atlas, and no shadow a handle names.</summary>
internal sealed class NoShadows : Render.Shadows.IShadowProxySource
{
    public int AtlasPage => 0;

    public bool TryGetShadowInfo(int clientShadowHandle, out Interop.ShadowEngineInfo info)
    {
        info = default;
        return false;
    }
}

/// <summary>No post program: the values a fresh one would bind (zeros, bloom on).</summary>
internal sealed class NoPostValues : IPostProxyValues
{
    public Render.MotionBlurValues MotionBlur => default;

    public Render.EnginePostValues EnginePost => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);
}
