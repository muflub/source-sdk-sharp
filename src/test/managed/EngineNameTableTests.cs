using System.Text;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>A name read from the engine is one string however often it is read, and always the bytes' own text.</summary>
public class EngineNameTableTests
{
    [Theory]
    [InlineData("models/player/male_01.mdl")]
    [InlineData("prop_physics")]
    [InlineData("models/été.mdl")]
    public void ANameIsTheBytesTextAndTheSameStringEachTime(string name)
    {
        var table = new EngineNameTable();
        byte[] utf8 = Encoding.UTF8.GetBytes(name);

        string first = table.Of(utf8);

        Assert.Equal(name, first);
        Assert.Same(first, table.Of(utf8));
        Assert.Equal(1, table.Count);
    }

    [Fact]
    public void TwoNamesAreTwoStrings()
    {
        var table = new EngineNameTable();
        Assert.Equal("a", table.Of("a"u8));
        Assert.Equal("ab", table.Of("ab"u8));
        Assert.Equal("a", table.Of("a"u8));
        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void NoBytesIsTheEmptyName()
    {
        var table = new EngineNameTable();
        Assert.Equal("", table.Of(default));
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void ANameLongerThanTheLookupIsStillItsText()
    {
        var table = new EngineNameTable();
        string longName = new('x', 1000);
        Assert.Equal(longName, table.Of(Encoding.UTF8.GetBytes(longName)));
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void ANameAlreadyHeldAllocatesNothing()
    {
        var table = new EngineNameTable();
        byte[] utf8 = "models/player/male_01.mdl"u8.ToArray();
        for (int i = 0; i < 8; i++)
            table.Of(utf8);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
            table.Of(utf8);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void TheTableStopsGrowingAtItsBoundAndStillAnswers()
    {
        var table = new EngineNameTable();
        for (int i = 0; i < EngineNameTable.MaxNames; i++)
            table.Of(Encoding.UTF8.GetBytes("n" + i));
        Assert.Equal(EngineNameTable.MaxNames, table.Count);

        Assert.Equal("one-more", table.Of("one-more"u8));
        Assert.Equal(EngineNameTable.MaxNames, table.Count);
        Assert.Equal("n7", table.Of("n7"u8));
    }
}

/// <summary>A catalogue's list and a name's UTF-8 for a native call cost nothing per read.</summary>
public class PerReadAllocationTests
{
    [Fact]
    public void ACataloguesListIsOneObject()
    {
        var ammo = SourceSharp.Shared.AmmoCatalog.Stock();
        var weapons = SourceSharp.Shared.WeaponCatalog.Stock();
        Assert.Same(ammo.All, ammo.All);
        Assert.Same(weapons.All, weapons.All);
        Assert.Equal(ammo.Count, ammo.All.Count);
        Assert.Equal(weapons.Count, weapons.All.Count);
    }

    [Fact]
    public void ANameFitsItsRoomNulTerminated()
    {
        Span<byte> room = stackalloc byte[64];
        room.Fill(0xFF);

        ReadOnlySpan<byte> bytes = Vtable.Utf8("cl_predict", room);

        Assert.Equal("cl_predict\0"u8.ToArray(), bytes.ToArray());
        Assert.True(bytes.Overlaps(room), "the name went in the caller's room");
    }

    [Fact]
    public void ANameThatMightNotFitGetsAnArray()
    {
        Span<byte> room = stackalloc byte[8];

        ReadOnlySpan<byte> bytes = Vtable.Utf8("a_longer_convar", room);

        Assert.Equal("a_longer_convar\0"u8.ToArray(), bytes.ToArray());
        Assert.False(bytes.Overlaps(room));
    }
}
