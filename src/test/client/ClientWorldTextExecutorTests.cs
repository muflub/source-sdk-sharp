using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// point_worldtext as the native text renderer sees it (lane c2-sims): each
/// update hands over the font page and the measured bounds; each draw lays out
/// four packed vertices per glyph from the place and view native hands in; the
/// rainbow reads its speeds as atof does; a removal destroys the renderer; a
/// reload carries the layout.
/// </summary>
public class ClientWorldTextExecutorTests
{
    private static readonly EntityHandle Owner = new(83u);

    private sealed class Meshes : IClientWorldTextMeshes
    {
        public readonly List<string> Calls = new();

        public bool Update(EntityHandle owner, string material, Vector mins, Vector maxs)
        {
            Calls.Add($"update {owner.Value} {material} {mins.X} {maxs.X}");
            return true;
        }

        public void Destroy(EntityHandle owner) => Calls.Add($"destroy {owner.Value}");
    }

    private static (ClientWorldText Text, RecordingEngine Engine) Text(Meshes meshes)
    {
        var engine = new RecordingEngine();
        var text = new ClientWorldText(engine, meshes, engine);
        text.BindEngine(engine, engine);
        text.State.BindLocal();
        text.Handle = Owner;
        text.State.Text = "Hi there";
        text.State.TextSize = 10;
        text.State.Color = 0xff4080ff;
        text.State.Font = 4;
        return (text, engine);
    }

    private static WorldTextView View() => new()
    {
        Origin = new Vector(100, 200, 300), Forward = new Vector(1, 0, 0), Right = new Vector(0, -1, 0), Up = new Vector(0, 0, 1), CurTime = 3,
    };

    [Fact]
    public void AnUpdateHandsOverTheFontPageAndTheBoundsAndARemovalDestroys()
    {
        var meshes = new Meshes();
        var (text, _) = Text(meshes);
        text.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        text.OnRemoved();
        Assert.Equal([$"update 83 editor/worldtext_5 {text.Layout.BoundsMin.X} {text.Layout.BoundsMax.X}", "destroy 83"], meshes.Calls);
        Assert.True(text.Layout.BoundsMax.Y > text.Layout.BoundsMin.Y, "the bounds were measured");
    }

    [Fact]
    public void ADrawIsFourPackedVerticesPerGlyphAtTheHandedPlace()
    {
        var (text, _) = Text(new Meshes());
        text.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        IManagedWorldTextSource source = text;
        var view = View();

        var out_ = new WorldTextVertexRaw[64];
        int n = source.Draw(view, out_);
        Assert.True(n == 7 * 4, $"seven glyphs and a space ({n})");
        Assert.True(out_.Take(n).All(v => v.Color == 0xff4080ff), "the colour packed r low");
        Assert.True(out_.Take(n).All(v => MathF.Abs(v.Position.X - 100) < 1e-3f), "laid out from the handed place, in its plane");
        Assert.True(source.Draw(view, new WorldTextVertexRaw[4]) == n, "a short buffer is told the count it needs");
    }

    [Fact]
    public void TheRainbowReadsItsSpeedsAsAtofDoes()
    {
        var (text, engine) = Text(new Meshes());
        engine.Returns("Console.FindConVarValue", "100.0f");
        text.State.Rainbow = true;
        text.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        IManagedWorldTextSource source = text;
        var out_ = new WorldTextVertexRaw[64];
        int n = source.Draw(View(), out_);

        var plain = new WorldTextLayout();
        plain.Update("Hi there", 10, 0, 0, 0xff4080ff, 0, 4, true);
        var want = new WorldTextVertex[64];
        plain.Layout(View().Origin, default, View().Right, View().Up, 3, 100f, 100f, want);
        Assert.True(n > 0 && (out_[0].Color & 0xffffff) == (want[0].R | ((uint)want[0].G << 8) | ((uint)want[0].B << 16)),
            "'100.0f' is 100, as the engine's atof reads it");
    }

    [Fact]
    public void AReloadCarriesTheLayout()
    {
        var (before, _) = Text(new Meshes());
        before.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        var record = new SavedEntity(Owner.Value, before.ClassName, string.Empty);
        before.SaveState(record);

        var meshes = new Meshes();
        var (after, _) = Text(meshes);
        Assert.True(after.Layout.TextLength == 0, "a rebuilt entity holds no layout");
        after.LoadState(record);
        Assert.True(after.Layout.TextLength == before.Layout.TextLength && after.Layout.WorldWidth == before.Layout.WorldWidth,
            "the layout came across");
        after.OnRemoved();
        Assert.Equal(["destroy 83"], meshes.Calls);
    }
}
