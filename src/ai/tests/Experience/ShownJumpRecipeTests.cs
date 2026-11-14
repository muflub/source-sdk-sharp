using System.Collections.Immutable;
using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// A jump a player showed as the store keeps it (lane G6a, <see cref="ShownJumpRecipe"/>): its exemplar recipe is format 3
/// (never a solver's 1) and reads back as it was written; its count row's key is the same for every sighting of the jump.
/// </summary>
public sealed class ShownJumpRecipeTests
{
    private static ShownJumpRecipe Jump(float takeoffX = 10.5f, int air = 40, params ShownJumpCommand[] trace)
        => new(ShownJumpKind.Blast, ShownJumpFlags.OwnBlast | ShownJumpFlags.ShotSeen | ShownJumpFlags.JumpPressed,
               new AiVector(takeoffX, -20.25f, 64f), new AiVector(300f, 400f, 314.5f), new AiVector(1f, 2f, 3f), new AiVector(0f, 250f, 289f),
               new AiVector(-100f, 600f, 800f), new AiVector(10f, 20f, -300f), air, 47, 33, 32, 34, 36,
               trace.Length > 0 ? [.. trace] : [new(ShownJumpCommand.Jump | ShownJumpCommand.Duck, 12.5f, 190f, 400f, -200f), new(ShownJumpCommand.Attack, 89f, -45.25f, 0f, 0f)]);

    [Fact]
    public void AnExemplarReadsBackAsItWasWritten()
    {
        ShownJumpRecipe jump = Jump();
        Assert.True(ShownJumpRecipe.TryRead(jump.Recipe(), out ShownJumpRecipe? read));
        Assert.Equal(jump with { Trace = [jump.Trace[0] with { Yaw = -170f }, jump.Trace[1]] }, read);
    }

    [Fact]
    public void AnExemplarIsFormatThreeWithAHeaderAndSevenBytesACommand()
    {
        LinkRecipe recipe = Jump().Recipe();
        Assert.Equal(3, recipe.Bytes[0]);
        Assert.Equal(90 + 2 * 7, recipe.Bytes.Length);
    }

    [Fact]
    public void ASolversRecipeIsNoExemplar()
        => Assert.False(ShownJumpRecipe.TryRead(new LinkRecipe(ImmutableArray.Create(new byte[42])
                                                                   .SetItem(0, 1)), out _));

    [Fact]
    public void ACountRowsRecipeIsNoExemplar() => Assert.False(ShownJumpRecipe.TryRead(ShownJumpRecipe.CountRecipe, out _));

    [Fact]
    public void AnExemplarCutShortIsRefused()
    {
        ImmutableArray<byte> bytes = Jump().Recipe().Bytes;
        Assert.False(ShownJumpRecipe.TryRead(new LinkRecipe(bytes.RemoveAt(bytes.Length - 1)), out _));
    }

    [Fact]
    public void AnExemplarRunningOnIsRefused()
    {
        ImmutableArray<byte> bytes = Jump().Recipe().Bytes;
        Assert.False(ShownJumpRecipe.TryRead(new LinkRecipe(bytes.Add(0)), out _));
    }

    [Fact]
    public void TwoSightingsWithOtherNumbersShareTheCountRow()
    {
        ShownJumpRecipe a = Jump(takeoffX: 10.5f, air: 40), b = Jump(takeoffX: 30f, air: 52);
        Assert.Equal(a.CountKey(3), b.CountKey(3));
        Assert.NotEqual(a.ExemplarKey(3), b.ExemplarKey(3));
    }

    [Fact]
    public void TheCountRowIsAHumansOfTheJumpsBinsAndKind()
    {
        LinkKey key = Jump().CountKey(3);
        Assert.Equal(new LinkKey(3, new WorldBin(0, -1, 0), new WorldBin(4, 6, 4), (int)NavLinkKind.Blast, LinkSource.Human, ShownJumpRecipe.CountRecipe), key);
    }

    [Fact]
    public void AnExemplarOfALongFlightStaysUnderFourKilobytes()
    {
        var trace = Enumerable.Repeat(new ShownJumpCommand(0, 0f, 0f, 0f, 0f), 33 + 400 + 2).ToArray();
        Assert.True(Jump(trace: trace).Recipe().Bytes.Length < 4096);
    }
}
