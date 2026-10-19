extern alias client;
using SourceSharp.Abi;
using client::SourceSharp.Client.Effects;
using client::SourceSharp.Client.Interop;
using client::SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// env_spritetrail, both halves.
///
/// <para>
/// The entity is a <see cref="SpriteBase"/> and inherits every rule
/// <see cref="SpriteTests"/> already pins, so what is left worth checking is the
/// three places this one differs: which material it draws with, the CONSTRUCTOR
/// defaults it would otherwise lose, and the fact that its client half hands
/// state to a stock object instead of drawing.
/// </para>
/// </summary>
public class SpriteTrailTests
{
    private static readonly EntityHandle Ent = new(0x0055_0055u);

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.001f;

    private static (ScriptedEntityRegistry, EnvSpriteTrail, RecordingEngine) SpawnWith(
        params (string Key, string Value)[] keys)
    {
        var services = Services(out var engine);

        engine.Returns("Precache.PrecacheModel", 77);
        engine.Returns("Precache.ModelFrameCount", 1);
        engine.Returns("Entities.GetAngles", new Vector(0f, 0f, 0f));

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvSpriteTrail).Assembly);
        registry.Services = services;

        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, EntityNames.EnvSpritetrail, key, value);

        registry.Spawn(Ent, EntityNames.EnvSpritetrail);

        return (registry, (EnvSpriteTrail)registry.Get(Ent)!, engine);
    }

    /// <summary>
    /// A trail draws with "spritename", not with "model" — which is the whole
    /// reason the key exists. Stock's Spawn prefers a model that was set by code
    /// and falls back to the mapper's material; a map only ever writes the
    /// material.
    /// </summary>
    [Fact]
    public void TheMaterialComesFromSpritename()
    {
        var (_, trail, engine) = SpawnWith(("spritename", "sprites/bluelaser1.vmt"));

        var precache = engine.Calls.LastOrDefault(c => c.Member == "Precache.PrecacheModel");

        Assert.True(precache.ArgAs<string>(0) == "sprites/bluelaser1.vmt", "a trail precaches its spritename");
        Assert.True(trail.Trail.ModelIndex == 77, "and networks the index that came back");
    }

    /// <summary>And "model" wins when a map sets both, which is stock's order.</summary>
    [Fact]
    public void ModelWinsOverSpritenameWhenBothAreSet()
    {
        var (_, _, engine) = SpawnWith(("spritename", "sprites/bluelaser1.vmt"),
                                       ("model", "sprites/glow06.spr"));

        var precache = engine.Calls.LastOrDefault(c => c.Member == "Precache.PrecacheModel");

        Assert.True(precache.ArgAs<string>(0) == "sprites/glow06.spr", "the model key wins, as stock's Spawn has it");
    }

    /// <summary>
    /// The map's three ribbon keys land where the client reads them.
    /// </summary>
    [Fact]
    public void TheRibbonKeysReachTheWire()
    {
        var (_, trail, _) = SpawnWith(("spritename", "sprites/bluelaser1.vmt"),
                                      ("lifetime", "1.5"),
                                      ("startwidth", "16"),
                                      ("endwidth", "2"));

        Assert.True(Near(trail.Trail.LifeTime, 1.5f), "a segment lives as long as the map said");
        Assert.True(Near(trail.Trail.StartWidth, 16f), "and the ribbon is as wide at the entity");
        Assert.True(Near(trail.Trail.EndWidth, 2f), "and as wide at the far end");
    }

    /// <summary>
    /// The trap this family keeps setting: stock's CONSTRUCTOR default for the
    /// end width is MINUS ONE, meaning "as wide as the start". A managed field
    /// left at zero would taper every unset trail to a point while reporting the
    /// key correctly.
    /// </summary>
    [Fact]
    public void AnUnsetEndWidthIsMinusOneRatherThanZero()
    {
        var (_, trail, _) = SpawnWith(("spritename", "sprites/bluelaser1.vmt"),
                                      ("startwidth", "16"));

        Assert.True(Near(trail.Trail.EndWidth, -1f),
                    "a trail with no endwidth key keeps stock's -1, which means 'the same as the start'");
    }

    /// <summary>
    /// The client half hands over the state and draws nothing itself.
    ///
    /// <para>
    /// It is the only one of the four sprite classnames that keeps a stock
    /// client OBJECT, so the assertion is that it FEEDS one rather than that it
    /// declares a custom draw — a trail that quietly drew itself would show a
    /// dot instead of a ribbon.
    /// </para>
    /// </summary>
    [Fact]
    public void TheClientHalfFeedsAStockObject()
    {
        var registry = new ClientStandaloneRegistry(ClientDoubles.UnattachedEngine());
        registry.RegisterAll(typeof(ClientSpriteTrail).Assembly);
        var services = ClientDoubles.Services(out var engine);
        engine.Ignores("EntityRender.RegisterRenderable", "EntityRender.UnregisterRenderable");

        // The ribbon's ring is managed (ClientSpriteTrails, lane c2-fx); this
        // test draws nothing, so its executor refuses every call.
        var trails = new ClientSpriteTrails(TestDebugHooks.Unasked<IClientBeamExecutor>(), engine);
        services.AddInstance(trails);
        registry.Services = services;

        // The index the creation push caches (Phase X4). ClientStandaloneRegistry
        // asks the engine once per entity so that nothing has to ask again per
        // frame; these tests are about what the entity draws, not what it is
        // numbered.
        engine.Ignores("Entities.GetIndex");

        var handle = new EntityHandle(0x0055_0066u);
        Assert.True(registry.Create(handle, EntityNames.EnvSpritetrail), "env_spritetrail has a client half at all");

        var trail = (ClientSpriteTrail)registry.Get(handle)!;

        trail.State.ModelIndex = 77;
        trail.State.LifeTime = 1.5f;
        trail.State.StartWidth = 16f;
        trail.State.EndWidth = 2f;
        trail.State.RenderMode = (int)RenderMode.TransAdd;
        trail.State.Red = 120;
        trail.State.Green = 190;
        trail.State.Blue = 255;
        trail.State.Brightness = 255;
        trail.State.Scale = 1f;
        trail.State.HdrColorScale = 1f;
        trail.State.Visible = true;

        trail.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        var update = engine.Calls.LastOrDefault(c => c.Member == "Effects.SpriteTrailUpdate");

        Assert.True(update.Member == "Effects.SpriteTrailUpdate", "an update asks native to build the trail's renderable");
        Assert.True(update.ArgAs<int>(1) == (int)RenderMode.TransAdd, "with the render mode, which native bounds-checks");
        Assert.True(update.ArgAs<bool>(2), "and whether it is drawn");

        // Nothing else crosses: the ring and the numbers it is drawn with are
        // the managed ribbon's, and the POSITION is native's per-frame sweep,
        // because a managed entity that only MOVED delivers no data change.
        Assert.True(update.Args.Length == 3, "and nothing else");

        // And it draws nothing here. The object native made is a real client
        // entity with its own DrawModel; a custom draw would be a second,
        // wrong drawing on top of it.
        Assert.True(trail.OnDraw() == 0, "the client half draws nothing itself");
        Assert.True(engine.CountOf("EntityRender.SetCustomDraw") == 0, "and does not claim a custom draw");

        Assert.True(trails.Count == 1, "the managed ribbon has the entity's numbers");

        trail.OnRemoved();
        Assert.True(engine.Calls.Any(c => c.Member == "Effects.SpriteTrailDestroy"),
                    "and the trail goes when the entity does");
        Assert.True(trails.Count == 0, "on both sides");
    }
}
