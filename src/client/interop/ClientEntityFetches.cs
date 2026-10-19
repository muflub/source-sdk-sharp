using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// Registers the client's engine adapters: every client-only seam whose real
/// implementation calls a shim area, built over the side's attached areas.
///
/// <para>
/// The client's composition root (<c>ClientModule</c>) asks this seam rather
/// than <see cref="ClientEntityFetches"/> directly, so a test that composes a
/// client module hands it fakes and never reaches a shim area: what such a
/// test checks is the managed composition, and the adapters' own slot calls
/// have facts of their own over a fake shim root.
/// </para>
/// </summary>
public interface IClientEngineAdapters
{
    /// <summary>Registers the adapters in <paramref name="services"/>, before the engine areas.</summary>
    void AddTo(ServiceContainer services, ModuleEngine engine);
}

/// <summary>
/// The client's three per-frame entity fetches, built by the client
/// composition and handed to its <c>IEngineEntities</c> binding (P1 run 3).
///
/// <para>
/// They read client-only slots, so they are client code: the shared binding
/// names only <see cref="IInterpolationBlocks"/>, <see cref="IRenderPolicyBlocks"/>
/// and <see cref="IClientReplayEngine"/>, and takes these from the container
/// when it is built (EngineServices.Build picks the longest constructor the
/// container satisfies). Registered before the areas, so the binding and the
/// container hold the same three objects.
/// </para>
/// </summary>
public sealed class ClientEntityFetches : IClientEngineAdapters
{
    /// <summary>
    /// Registers this side's three, over its own slots. Call before
    /// <see cref="ModuleEngine.AddTo"/>, and only once the client's shim areas
    /// are attached: every adapter takes the areas it calls by constructor, so
    /// an unattached engine is refused here rather than handing them nulls.
    /// The game never composes before the attach (LoadSideV2 fails the load
    /// first), and the test doubles attach a fake root.
    /// </summary>
    public void AddTo(ServiceContainer services, ModuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(engine);

        ClientShimAreas areas = engine.Api.ClientShims;

        services.AddInstance<IInterpolationBlocks>(new InterpolationBlocks(areas.StandaloneClient));
        services.AddInstance<IRenderPolicyBlocks>(new RenderPolicyBlocks(areas.StandaloneClient));
        services.AddInstance<IClientReplayEngine>(new ClientReplayEngine(areas.PredictionClient));

        // The recv tables and schema refusals, for the dev API's nettables
        // route (plan_client_novalve §4.4). A read, not a fetch, but over the
        // same slots and with the same reason to live here: it is unsafe.
        services.AddInstance<IClientRecvTables>(new ClientRecvTables(areas.StandaloneClient));

        // The rope executor and the view, light and world-hull reads a managed
        // rope makes (plan_client_novalve §2 cluster 6).
        services.AddInstance<IClientRopeExecutor>(new ClientRopeExecutor(areas.EffectsRenderClient));
        var ropeExecutor = new ClientRopeExecutor(areas.EffectsRenderClient);
        services.AddInstance<IClientRopeExecutor>(ropeExecutor);
        services.AddInstance<IClientComputeLighting>(ropeExecutor);

        // The beam executor and the reads the managed beam system makes
        // (plan_client_novalve §2 cluster 5).
        services.AddInstance<IClientBeamExecutor>(new ClientBeamExecutor(areas.EffectsRenderClient));

        // The UI's draw primitives (plan_client_ui U2), for VguiBackend.
        services.AddInstance<IUiSurface>(new ClientUiSurface(areas.UiClient));
        services.AddInstance<IClientDebugOverlay>(new ClientDebugOverlay(areas.UiClient));

        // The second cameras' render targets (lane camproto).
        services.AddInstance<IViewTargets>(new ClientViewTargets(areas.ViewClient));

        // The observer's camera (plan_client_novalve C7 item 5).
        services.AddInstance<IObserverCameraSlots>(new ObserverCameraSlots(areas.ViewClient));

        // The flashlight's executor and its traces (plan_client_novalve C7 cluster 12).
        services.AddInstance<IFlashlightSlots>(new FlashlightSlots(engine.Api));

        // The net channel and the diagnostics ledgers, for the netgraph and
        // absences routes; one reader over the slots serves both.
        var readers = new ClientNetReaders(areas.DiagnosticsClient, areas.NetClient);
        services.AddInstance<IClientNetChannel>(readers);
        services.AddInstance<IClientDiagnosticsLedgers>(readers);

        // The managed particle sims' draw executor (lane c2-sims): its
        // lifetime, which managed decides.
        // Wrapped by the managed manager's (ClientModule: BoundParticleExecutor).
        services.AddInstance<INativeParticleExecutor>(new ClientParticleExecutor(areas.ParticlesClient));
        // The particles shell's started systems, for the particles route (lane c5-particles).
        services.AddInstance<IClientParticleSystems>(new ClientParticleSystems(areas.ParticlesClient));
        // The ParticleEffect client effect's carry-out (lane c5-particles).
        services.AddInstance<IClientParticleOneShots>(new ClientParticleOneShots(areas.ParticlesClient));
        // The particle collision's world trace (lane c5-particles).
        services.AddInstance<IParticleWorldTrace>(new ClientParticleWorldTrace(areas.ParticlesClient));
        var particleTrace = new ClientParticleWorldTrace(areas.ParticlesClient);
        services.AddInstance<IParticleWorldTrace>(particleTrace);
        services.AddInstance<IParticleMaskTrace>(particleTrace);
        // The view the particle manager draws in, read once per view, and the
        // glows' pixel visibility (lane c5-particles).
        // libss_particles.so's ISSParticleLib, bound directly (plan_particles_so.md).
        // The library the view asks for the throttle: the one already
        // registered, when the host (or a fact) has registered one, else the
        // side's own over its engine interfaces.
        IParticleLibrary? particleLibrary = services.Resolve<IParticleLibrary>();
        if (particleLibrary is null)
        {
            particleLibrary = new ParticleLibrary(engine.Api.Interfaces);
            services.AddInstance(particleLibrary);
        }

        // The new-style effects' collections are the same library's
        // (C7 particles phase 4), when it serves them.
        if (particleLibrary is IParticleCollections collections && services.Resolve<IParticleCollections>() is null)
            services.AddInstance<IParticleCollections>(collections);
        var particleView = new ClientParticleViewSlots(areas.ParticlesClient, particleLibrary);
        services.AddInstance<IParticleViewSource>(particleView);
        services.AddInstance<IParticlePixelVisibility>(particleView);
        services.AddInstance<IParticleThrottle>(particleView);
        services.AddInstance(new ParticleViews(particleView));
        // The dust motes' brush test (lane c5-particles).
        services.AddInstance<IClientBrushContents>(new ClientBrushContents(areas.ParticlesClient));
        services.AddInstance<IClientModelNames>(new ClientModelNames(engine.Api));
        services.AddInstance<IClientPlaneTrace>(new ClientPlaneTrace(areas.ParticlesClient));
        // point_worldtext's native text renderer (lane c2-sims): its lifetime.
        services.AddInstance<IClientWorldTextMeshes>(new ClientWorldTextMeshes(areas.ParticlesClient));
        // The client wind's publication (lane c2-sims).
        services.AddInstance<IClientWindPublisher>(new ClientWindPublisher(areas.EffectsRenderClient));
        // The material proxies' engine reads (lane c5-proxies): FindVar and
        // FindTexture at Init, the texture, entity and view reads of a bind.
        services.AddInstance<IClientMaterialProxyExecutor>(new ClientMaterialProxyExecutor(areas.MaterialProxyClient));
        // The native effect registrations, by walk index, for the managed
        // dispatch table (plan_client_novalve §2 cluster 4).
        services.AddInstance<IClientEffectRegistrations>(new ClientEffectRegistrations(areas.FxClient));

        // IEffects' FX_* routines and SuppressTE's two answers (cluster 3),
        // for the managed ClientEffects; one object over the slots serves both.
        var fx = new ClientFxSlots(areas.FxClient);
        services.AddInstance<IFxSuppression>(fx);
        services.AddInstance<IFxImpactOverlay>(fx);

        // The client-side effect list's carry-out (cluster 3).
        services.AddInstance<IFxRender>(new ClientFxRenderSlots(areas.FxClient));
        services.AddInstance<IFxSpriteGlow>(new ClientFxSpriteGlowSlots(areas.FxClient));
        services.AddInstance<IFxSpriteDraw>(new ClientFxSpriteDrawSlots(areas.FxClient));
        services.AddInstance<IFxPrecache>(new ClientFxPrecacheSlots(areas.FxClient));

        // The client soundscape system's executors (sound cluster 17).
        services.AddInstance<ISoundscapeAudio>(new SoundscapeAudioSlots(areas.Console, areas.SoundscapeClient));
        var emitters = new ClientFxEmitterSlots(areas.FxClient);
        services.AddInstance<IFxFlashReads>(emitters);
        services.AddInstance<IFxGameSounds>(emitters);
        services.AddInstance<IFxTempEntModels>(emitters);
        // The client weather's engine reads (lane c2-sims).
        services.AddInstance<IClientPrecipitationEngine>(new ClientPrecipitationSlots(areas.PrecipitationClient));
        // The client's physics callbacks' engine reads (lane c6-input).
        var physics = new ClientPhysicsSlots(areas.PhysicsClient, engine.Api.VPhysics);
        services.AddInstance<IClientImpactSoundWorld>(physics);
        // The level's environment: ClientPhysicsSystem makes it and writes it here.
        var environment = new ClientPhysicsEnvironment();
        services.AddInstance(environment);
        services.AddInstance<IClientPhysicsEngine>(environment);
        services.AddInstance<IClientPhysicsObjectReads>(physics);
        services.AddInstance<IClientSplashCarryOut>(physics);
        // The convar watch and the decided values (C8, IShimConVarsClient).
        services.AddInstance<IClientConVarSlots>(new ClientConVarSlots(areas.ConVarsClient));
    }
}
