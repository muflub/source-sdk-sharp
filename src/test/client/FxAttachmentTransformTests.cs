using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>FX_GetAttachmentTransform</c>'s two forms, C# on <see cref="IFxFlashReads"/>
/// since fx.cpp left (lane c5-particles): stock refused an invalid handle and an
/// attachment below 1 without asking the renderable, and otherwise answered the
/// renderable's attachment, the matrix form its translation.
/// </summary>
public class FxAttachmentTransformTests
{
    private sealed class Renderables : IFxFlashReads
    {
        public bool HasRenderable = true;
        public readonly List<(uint Handle, int Attachment)> Asked = [];

        public bool RenderableAttachment(uint handle, int attachment, out Vector origin, out QAngle angles)
        {
            Asked.Add((handle, attachment));
            origin = HasRenderable ? new Vector(1.5f, -2.25f, 3.125f) : default;
            angles = HasRenderable ? new QAngle(10, 20, 30) : default;
            return HasRenderable;
        }

        public int EntityIndex(uint handle) => throw new InvalidOperationException();
        public void Elight(int key, in Vector origin, byte r, byte g, byte b, sbyte exponent, float radius, float decay, float die) => throw new InvalidOperationException();
        public Vector VectorRandom(float lo, float hi) => throw new InvalidOperationException();
        public int MaterialsCached() => throw new InvalidOperationException();
    }

    private static IFxFlashReads As(Renderables r) => r;

    [Fact]
    public void A_valid_handle_and_attachment_answer_the_renderables_attachment()
    {
        var r = new Renderables();
        Assert.True(As(r).AttachmentTransform(7, 1, out Vector origin, out QAngle angles));
        Assert.Equal(new Vector(1.5f, -2.25f, 3.125f), origin);
        Assert.Equal(new QAngle(10, 20, 30), angles);
        Assert.Equal([(7u, 1)], r.Asked);
    }

    [Fact]
    public void The_invalid_handle_is_refused_without_asking()
    {
        var r = new Renderables();
        Assert.False(As(r).AttachmentTransform(EntityHandle.None.Value, 1, out Vector origin, out QAngle angles));
        Assert.Equal(default, origin);
        Assert.Equal(default, angles);
        Assert.Empty(r.Asked);
    }

    [Fact]
    public void An_attachment_below_one_is_refused_without_asking()
    {
        var r = new Renderables();
        Assert.False(As(r).AttachmentTransform(7, 0, out Vector origin, out _));
        Assert.Equal(default, origin);
        Assert.Empty(r.Asked);
    }

    [Fact]
    public void A_handle_with_no_renderable_answers_false_and_zeros()
    {
        var r = new Renderables { HasRenderable = false };
        Assert.False(As(r).AttachmentTransform(7, 2, out Vector origin, out QAngle angles));
        Assert.Equal(default, origin);
        Assert.Equal(default, angles);
        Assert.Single(r.Asked);
    }

    [Fact]
    public void The_matrix_form_answers_the_origin_as_its_translation()
    {
        var r = new Renderables();
        Assert.True(As(r).AttachmentMatrixOrigin(7, 3, out Vector origin));
        Assert.Equal(new Vector(1.5f, -2.25f, 3.125f), origin);
        Assert.False(As(r).AttachmentMatrixOrigin(EntityHandle.None.Value, 3, out origin));
        Assert.Equal(default, origin);
    }
}
