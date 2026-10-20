extern alias client;
using SourceSharp.Abi;
using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// point_worldtext, both halves.
///
/// <para>
/// The entity itself has no behaviour at all — every input is an assignment and
/// stock's are too — so what is worth checking is the two places a value can be
/// silently wrong instead: the COLOUR, which is four channels squeezed into one
/// number and unpacked by C++ at the other end, and the DEFAULTS, which stock
/// sets in a constructor rather than in a keyfield and which a port therefore
/// loses by simply not writing them.
/// </para>
///
/// <para>
/// The channel order is the sharp one. Nothing in play would show it
/// transposed: text drawn in the wrong colour still looks like text, and the
/// only map in the tree has none. So it is pinned here against
/// <c>color32</c>'s own layout, which is what both the send prop and the ABI
/// call agree on.
/// </para>
/// </summary>
public class WorldTextTests
{

    /// <summary>
    /// The message width, and why it is not stock's.
    ///
    /// <para>
    /// Stock declares <c>CNetworkString( m_szText, MAX_PATH )</c>. Copied
    /// straight across, that would have been 260 bytes and a 280-byte state —
    /// and at the time, because every entity on the parallel hierarchy was
    /// allocated ONE blob sized to the greediest declaration, all 336 of them
    /// back on the 2048-byte tier that Step 13 had just got them off. It was
    /// written that way first and
    /// <c>NetTableTests.TierPressureIsPerClassNow</c> caught it, which is
    /// exactly what that test is for.
    /// </para>
    ///
    /// <para>
    /// Part 18 sizes each class to its own tier, so the blast radius is this
    /// class now rather than the whole hierarchy. That makes the mistake
    /// cheaper and not acceptable, which is why this stayed pinned.
    /// </para>
    ///
    /// <para>
    /// Pinned again HERE, from this class's side, because the failure it
    /// prevents is invisible from this file: widening the string back reads as a
    /// local generosity and is a bandwidth change for every entity in the game.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTextDoesNotSetTheHierarchysTier()
    {
        var all = StandaloneNetStates.Discover(typeof(WorldTextNetState).Assembly, new RecordingLog());

        int Size(string className) => all.First(s => s.ClassName == className).Size;

        // 148 bytes of its own, behind the engine block every blob opens with
        // (plan_entity.md §3.2). Stock's CPointWorldText is 280 bytes of class,
        // which is the number this is being compared with, so the class's own
        // declaration is what has to be counted rather than the whole blob.
        Assert.True(Size(EntityNames.PointWorldtext) == EngineBlock.SizeBytes + 148, "point_worldtext declares 148 bytes, not stock's 280");

        var greediest = all.OrderByDescending(s => s.Size).First();

        Assert.True(greediest.ClassName != EntityNames.PointWorldtext, "and it is not the greediest declaration on the hierarchy");

        Assert.True(Size(EntityNames.PointWorldtext) < greediest.Size, "so the shared blob is still sized by func_smokevolume");
    }

    [Fact]
    public void ColourPacking()
    {
        // Red in the LOW byte, which is color32's own layout in memory and what
        // stock's single 32-bit send prop carries. Each channel checked on its
        // own, because a transposition swaps two of them and a test that only
        // looked at white or black would pass through either order.
        Assert.True(PointWorldText.Pack(new Color32(255, 0, 0, 0)) == 0x0000_00FFu, "red is the low byte");
        Assert.True(PointWorldText.Pack(new Color32(0, 255, 0, 0)) == 0x0000_FF00u, "green is the second");
        Assert.True(PointWorldText.Pack(new Color32(0, 0, 255, 0)) == 0x00FF_0000u, "blue is the third");
        Assert.True(PointWorldText.Pack(new Color32(0, 0, 0, 255)) == 0xFF00_0000u, "alpha is the top byte");

        Assert.True(PointWorldText.Pack(new Color32(255, 255, 255, 255)) == 0xFFFF_FFFFu, "opaque white is all ones");

        // A map may write anything, and a channel that overflowed its byte would
        // corrupt the NEXT one rather than merely being too bright.
        Assert.True(PointWorldText.Pack(new Color32(300, 0, 0, 0)) == 0x0000_00FFu, "a channel above 255 is clamped rather than allowed to carry");

        Assert.True(PointWorldText.Pack(new Color32(-5, 0, 0, 0)) == 0x0000_0000u, "and a negative one is clamped too");
    }

    [Fact]
    public void KeysAndDefaults()
    {
        var text = Spawn(31,
            ("message", "hello world"), ("textsize", "24"),
            ("textspacingX", "3"), ("textspacingY", "5"),
            ("orientation", "2"), ("font", "1"), ("rainbow", "1"),
            ("color", "10 20 30 40"));

        Assert.True(text.State.Text == "hello world", "the message reaches the wire");
        Assert.True(text.State.TextSize == 24f, "and the text size");
        Assert.True(text.State.SpacingX == 3f, "and the horizontal spacing");
        Assert.True(text.State.SpacingY == 5f, "and the vertical spacing");
        Assert.True(text.State.Orientation == 2u, "and the orientation");
        Assert.True(text.State.Font == 1u, "and the font");
        Assert.True(text.State.Rainbow, "and the rainbow flag");

        Assert.True(text.State.Color == 0x281E_140Au, "a keyed colour arrives packed");

        // Stock's CONSTRUCTOR sets these before any key is read, so a map that
        // says nothing gets 10 and opaque white rather than 0 and transparent
        // black. Text of size zero draws nothing at all, which is the silent
        // failure this pins.
        var bare = Spawn(32);
        Assert.True(bare.State.TextSize == 10f, "text size defaults to stock's 10");
        Assert.True(bare.State.Color == 0xFFFF_FFFFu, "and the colour to opaque white");
        Assert.True(bare.State.SpacingX == 0f && bare.State.SpacingY == 0f, "with no spacing");
        Assert.True(!bare.State.Rainbow, "and no rainbow");

        // Stock reads the message in KeyValue rather than through a keyfield, so
        // an unset one is the empty buffer its constructor memset — not a null
        // the far side would have to guard.
        Assert.True(bare.State.Text == "", "an unkeyed message is empty rather than absent");
    }

    [Fact]
    public void Inputs()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PointWorldText).Assembly);
        registry.Services = Services();

        var handle = new EntityHandle(33);
        registry.Spawn(handle, EntityNames.PointWorldtext);
        var text = (PointWorldText)registry.Get(handle)!;

        registry.Input(handle, "SetText", "driven by a map", 0f, EntityHandle.None);
        Assert.True(text.State.Text == "driven by a map", "SetText writes the networked string");

        registry.Input(handle, "SetTextSize", "18", 18f, EntityHandle.None);
        Assert.True(text.State.TextSize == 18f, "SetTextSize writes the size");

        registry.Input(handle, "SetTextSpacingX", "2", 2f, EntityHandle.None);
        Assert.True(text.State.SpacingX == 2f, "SetTextSpacingX writes the horizontal spacing");

        registry.Input(handle, "SetTextSpacingY", "7", 7f, EntityHandle.None);
        Assert.True(text.State.SpacingY == 7f, "SetTextSpacingY writes the vertical spacing");

        registry.Input(handle, "SetOrientation", "1", 1f, EntityHandle.None);
        Assert.True(text.State.Orientation == 1u, "SetOrientation writes the orientation");

        registry.Input(handle, "SetFont", "3", 3f, EntityHandle.None);
        Assert.True(text.State.Font == 3u, "SetFont writes the font");

        registry.Input(handle, "SetRainbow", "1", 1f, EntityHandle.None);
        Assert.True(text.State.Rainbow, "SetRainbow switches it on");

        registry.Input(handle, "SetRainbow", "0", 0f, EntityHandle.None);
        Assert.True(!text.State.Rainbow, "and off again");

        // A colour input is text on the way in — stock's own KeyValue reads the
        // same three or four numbers out of a string — so this is where the
        // packing has to happen a second time.
        registry.Input(handle, "SetColor", "10 20 30 40", 0f, EntityHandle.None);
        Assert.True(text.State.Color == 0x281E_140Au, "SetColor packs what it was sent");

        // Alpha is optional, exactly as it is on the key, and defaults to opaque
        // rather than to transparent — text that faded out on a three-channel
        // input would be an entity that silently stopped existing.
        registry.Input(handle, "SetColor", "255 0 0", 0f, EntityHandle.None);
        Assert.True(text.State.Color == 0xFF00_00FFu, "a colour with no alpha is opaque");

        // Stock's sscanf would have filled every missing channel with 255, so a
        // mistyped input turns the text WHITE. Leaving it alone is the
        // divergence, and it is deliberate: see docs/divergences.md.
        registry.Input(handle, "SetColor", "not a colour", 0f, EntityHandle.None);
        Assert.True(text.State.Color == 0xFF00_00FFu, "an unparseable colour leaves the last one standing");
    }

    private sealed class Meshes : client::SourceSharp.Client.Interop.IClientWorldTextMeshes
    {
        public readonly List<string> Calls = new();

        public bool Update(EntityHandle owner, string material, Vector mins, Vector maxs)
        {
            Calls.Add($"update {owner.Value} {material} {mins.X} {maxs.X}");
            return true;
        }

        public void Destroy(EntityHandle owner) => Calls.Add($"destroy {owner.Value}");
    }

    /// <summary>
    /// The client half: it owns the layout (lane c2-sims) and hands the native
    /// text renderer the font page and the measured bounds.
    ///
    /// <para>
    /// A managed entity is never on the wire as <c>CPointWorldText</c>, so the
    /// class that owned the mesh builder is never built from a datatable. What
    /// this checks is that every field the datatable used to carry reaches the
    /// layout — a field dropped here is text drawn with a stale value and
    /// nothing to say so — and that the renderer is made and destroyed with
    /// the entity.
    /// </para>
    /// </summary>
    [Fact]
    public void TheClientHalfTakesEverythingIntoItsLayout()
    {
        var registry = new ClientStandaloneRegistry(ClientDoubles.UnattachedEngine());
        registry.RegisterAll(typeof(ClientWorldText).Assembly);

        var meshes = new Meshes();
        var services = ClientDoubles.Services(out var engine);
        services.AddInstance<client::SourceSharp.Client.Interop.IClientWorldTextMeshes>(meshes);
        registry.Services = services;
        engine.Ignores("EntityRender.RegisterRenderable", "EntityRender.UnregisterRenderable");

        // The index the creation push caches (Phase X4). ClientStandaloneRegistry
        // asks the engine once per entity so that nothing has to ask again per
        // frame; these tests are about what the entity draws, not what it is
        // numbered.
        engine.Ignores("Entities.GetIndex");

        var handle = new EntityHandle(0x0709_0001u);
        Assert.True(registry.Create(handle, EntityNames.PointWorldtext), "point_worldtext has a client half at all");

        var text = (ClientWorldText)registry.Get(handle)!;

        text.State.Text        = "on the far side";
        text.State.TextSize    = 24f;
        text.State.SpacingX    = 3f;
        text.State.SpacingY    = 5f;
        text.State.Color       = 0x281E_140Au;
        text.State.Orientation = 2u;
        text.State.Font        = 1u;
        text.State.Rainbow     = true;

        text.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        var layout = text.Layout;
        Assert.True(layout.TextLength == "on the far side".Length, "the text goes over");
        Assert.True(layout.TextSize == 24f, "and the size");
        Assert.True(layout.SpacingX == 3f && layout.SpacingY == 5f, "and both spacings");
        Assert.True(layout.R == 0x0A && layout.G == 0x14 && layout.B == 0x1E && layout.A == 0x28, "and the packed colour, r low");
        Assert.True(layout.Orientation == 2 && layout.Font == 1 && layout.Rainbow, "and the orientation, the font and the rainbow flag");

        // The place is NOT handed over on an update, and that is the design:
        // native follows the owner every frame and hands the place to each draw.
        Assert.Equal([$"update {handle.Value} editor/worldtext_2 {layout.BoundsMin.X} {layout.BoundsMax.X}"], meshes.Calls);
        Assert.True(layout.BoundsMax.X == layout.WorldWidth * 0.5f, "a view-facing text's bounds are a cube of half its width");

        text.OnRemoved();
        Assert.True(meshes.Calls[^1] == $"destroy {handle.Value}", "and the text goes when the entity does");
    }

    private static PointWorldText Spawn(uint index, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PointWorldText).Assembly);
        registry.Services = Services();

        var handle = new EntityHandle(index);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, EntityNames.PointWorldtext, key, value);

        registry.Spawn(handle, EntityNames.PointWorldtext);
        return (PointWorldText)registry.Get(handle)!;
    }
}
