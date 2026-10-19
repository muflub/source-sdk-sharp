using System;
using System.Collections.Generic;
using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The message of the day, read in C#: stock's LoadSpecificMOTDMsg lookup order,
/// size cap and bytes, and IsValidPath (gameinterface.cpp:2031-2110).
/// </summary>
public class MessageOfTheDayTests
{
    private sealed class Files : IEngineFiles
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public byte[]? ReadFileOnPath(string path, string pathId) => string.Equals(pathId, "GAME", StringComparison.Ordinal) ? ReadGameFileBytes(path) : null;

        public readonly Dictionary<string, byte[]> Bytes = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Asked = new();

        public int GameFileSize(string path) => Bytes.TryGetValue(path, out var b) ? b.Length : -1;
        public string? ReadGameFile(string path) => null;

        public byte[]? ReadGameFileBytes(string path)
        {
            Asked.Add(path);
            return Bytes.TryGetValue(path, out var b) ? b : null;
        }
    }

    private sealed class Panel : IInfoPanel
    {
        public readonly Dictionary<string, byte[]> Written = new();

        public bool Set(string name, ReadOnlySpan<byte> data)
        {
            Written[name] = data.ToArray();
            return true;
        }
    }

    private static (MessageOfTheDay Motd, Files Files, Panel Panel) Build()
    {
        var files = new Files();
        var panel = new Panel();
        return (new MessageOfTheDay(new RecordingLog(), null!, null!, files, panel), files, panel);
    }

    private static byte[] B(string s) => System.Text.Encoding.ASCII.GetBytes(s);

    [Fact]
    public void TheCfgFolderIsLookedInFirst()
    {
        var (motd, files, panel) = Build();
        files.Bytes["cfg/motd.txt"] = B("hello");
        files.Bytes["motd.txt"] = B("root");

        Assert.True(motd.LoadOne("motd.txt", "motd.txt", "motd"));
        Assert.Equal(B("hello\0"), panel.Written["motd"]);
    }

    [Fact]
    public void TheRootIsWhereItUsedToGo()
    {
        var (motd, files, panel) = Build();
        files.Bytes["motd.txt"] = B("root");

        Assert.True(motd.LoadOne("motd.txt", "motd.txt", "motd"));
        Assert.Equal(B("root\0"), panel.Written["motd"]);
    }

    [Fact]
    public void TheShippedDefaultIsReadOnlyForTheDefaultName()
    {
        var (motd, files, panel) = Build();
        files.Bytes["cfg/motd_default.txt"] = B("shipped");

        Assert.True(motd.LoadOne("MOTD.TXT", "motd.txt", "motd"));
        Assert.Equal(B("shipped\0"), panel.Written["motd"]);

        var (other, otherFiles, otherPanel) = Build();
        otherFiles.Bytes["cfg/custom_default.txt"] = B("never");
        Assert.False(other.LoadOne("custom.txt", "motd.txt", "motd"));
        Assert.Empty(otherPanel.Written);
    }

    [Fact]
    public void TheLookupOrderIsCfgThenRootThenDefault()
    {
        var (motd, files, _) = Build();

        Assert.False(motd.LoadOne("motd_text.txt", "motd_text.txt", "motd_text"));
        Assert.Equal(new[] { "cfg/motd_text.txt", "motd_text.txt", "cfg/motd_text_default.txt" }, files.Asked);
    }

    [Fact]
    public void NothingFoundWritesNothing()
    {
        var (motd, _, panel) = Build();

        Assert.False(motd.LoadOne("motd.txt", "motd.txt", "motd"));
        Assert.Empty(panel.Written);
    }

    [Fact]
    public void AFileOver2048BytesIsRefusedAnd2048IsNot()
    {
        var (motd, files, panel) = Build();
        files.Bytes["cfg/big.txt"] = new byte[MessageOfTheDay.MaxBytes + 1];
        files.Bytes["cfg/edge.txt"] = new byte[MessageOfTheDay.MaxBytes];

        Assert.False(motd.LoadOne("big.txt", "motd.txt", "motd"));
        Assert.True(motd.LoadOne("edge.txt", "motd.txt", "motd_text"));
        Assert.False(panel.Written.ContainsKey("motd"));
        Assert.Equal(MessageOfTheDay.MaxBytes + 1, panel.Written["motd_text"].Length);
    }

    [Fact]
    public void TheBytesGoOutAsReadWithATerminatingNul()
    {
        // Not decoded: a byte that is not UTF-8 survives.
        var (motd, files, panel) = Build();
        files.Bytes["cfg/motd.txt"] = new byte[] { 0x3C, 0xFF, 0xC3 };

        Assert.True(motd.LoadOne("motd.txt", "motd.txt", "motd"));
        Assert.Equal(new byte[] { 0x3C, 0xFF, 0xC3, 0x00 }, panel.Written["motd"]);
    }

    [Theory]
    [InlineData("motd.txt", true)]
    [InlineData("cfg/motd.txt", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("\\server\\share", false)]
    [InlineData("c:motd.txt", false)]
    [InlineData("../motd.txt", false)]
    [InlineData("cfg/../../motd.txt", false)]
    public void APathIsValidAsStockJudgedIt(string? path, bool valid)
        => Assert.Equal(valid, MessageOfTheDay.IsValidPath(path));

    [Fact]
    public void TheInfoPanelWritesToTheEngineTableOfThatName()
    {
        var table = new FakeTable();
        var panel = new InfoPanel(new FakeDirectory { Tables = { ["InfoPanel"] = table } });

        Assert.True(panel.Set("motd", B("x\0")));
        Assert.Equal("motd", table.LastValue);
        Assert.Equal(B("x\0"), table.LastData);
    }

    [Fact]
    public void WithNoInfoPanelTableNothingIsWritten()
        => Assert.False(new InfoPanel(new FakeDirectory()).Set("motd", B("x\0")));

    private sealed class FakeDirectory : INetworkStringTableDirectory
    {
        public readonly Dictionary<string, INetworkStringTableRef> Tables = new();
        public INetworkStringTableRef? Find(string name) => Tables.TryGetValue(name, out var t) ? t : null;
    }

    private sealed class FakeTable : INetworkStringTableRef
    {
        public string? LastValue;
        public byte[]? LastData;
        public string Name => "InfoPanel";

        public int AddString(string value, ReadOnlySpan<byte> userData)
        {
            LastValue = value;
            LastData = userData.ToArray();
            return 0;
        }

        public int FindStringIndex(string value) => value == LastValue ? 0 : INetworkStringTableRef.InvalidStringIndex;
    }
}
