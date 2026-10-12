using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_novalve.md P2.2: the usercmd decode in C#, held to stock
/// <c>ReadUsercmd</c> on recorded packets (Fixtures/usercmd-decode-stock.txt,
/// see its README), plus the planning and queueing the same call does.
/// </summary>
public class UserCmdCodecTests
{
    // This class's own command queues and shadow run (statics to zero):
    // xUnit builds one instance per fact, so no fact sees another's.
    private readonly PlayerCommandQueue _queue = new(new RecordingLog());

    private sealed record Packet(int Line, int NumCmds, int TotalCmds, int StartBit, int EndBit,
                                 float MaxMove, byte[] Bytes, List<string[]> Cmds);

    private static List<Packet> Corpus()
    {
        string path = Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "managed",
                                   "SourceSharp.Tests", "Fixtures", "usercmd-decode-stock.txt");
        Assert.True(File.Exists(path), "the recorded stock decode is missing: " + path);

        var packets = new List<Packet>();
        Packet? cur = null;
        int line = 0;
        foreach (string raw in File.ReadLines(path))
        {
            line++;
            if (raw.StartsWith('#') || raw.Length == 0)
                continue;
            string[] f = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f[0] == "P")
            {
                cur = new Packet(line, int.Parse(f[1]), int.Parse(f[2]), int.Parse(f[3]), int.Parse(f[4]),
                                 BitConverter.Int32BitsToSingle(unchecked((int)Hex(f[5]))),
                                 Convert.FromHexString(f[6]), new List<string[]>());
                packets.Add(cur);
            }
            else
            {
                Assert.NotNull(cur);
                cur!.Cmds.Add(f);
            }
        }
        return packets;
    }

    private static uint Hex(string s) => uint.Parse(s, NumberStyles.HexNumber);
    private static uint Bits(float f) => unchecked((uint)BitConverter.SingleToInt32Bits(f));

    [Fact]
    public void TheCorpusIsThereAndReachesEveryField()
    {
        var corpus = Corpus();
        Assert.Equal(550, corpus.Count);

        int weapon = 0, subtype = 0, mouse = 0, impulse = 0;
        foreach (var p in corpus)
        {
            Assert.Equal(p.TotalCmds, p.Cmds.Count);
            foreach (var c in p.Cmds)
            {
                if (c[11] != "0") weapon++;
                if (c[12] != "0") subtype++;
                if (c[14] != "0" || c[15] != "0") mouse++;
                if (c[10] != "0") impulse++;
            }
        }
        // A corpus that stopped reaching a field would let a decoder that got
        // that field wrong pass.
        Assert.True(weapon > 100 && subtype > 100 && mouse > 100 && impulse > 100,
            $"weapon {weapon}, subtype {subtype}, mouse {mouse}, impulse {impulse}");
    }

    [Fact]
    public void EveryRecordedPacketDecodesAsStockDid()
    {
        Span<DecodedUserCmd> got = stackalloc DecodedUserCmd[PlayerCommandQueue.MaxBackup];
        int commands = 0;

        foreach (var p in Corpus())
        {
            int end = UserCmdCodec.Decode(p.Bytes, p.StartBit, p.EndBit, p.TotalCmds, p.MaxMove, got);
            Assert.True(end == p.EndBit, $"line {p.Line}: ended at bit {end}, stock at {p.EndBit}");

            for (int i = 0; i < p.TotalCmds; i++)
            {
                string[] e = p.Cmds[i];
                ref DecodedUserCmd g = ref got[i];
                string at = $"line {p.Line} cmd {i}";

                Assert.True(int.Parse(e[1]) == g.Cmd.CommandNumber, at + " command_number");
                Assert.True(int.Parse(e[2]) == g.Cmd.TickCount, at + " tick_count");
                Assert.True(Hex(e[3]) == Bits(g.Cmd.Pitch), at + " pitch");
                Assert.True(Hex(e[4]) == Bits(g.Cmd.Yaw), at + " yaw");
                Assert.True(Hex(e[5]) == Bits(g.Cmd.Roll), at + " roll");
                Assert.True(Hex(e[6]) == Bits(g.Cmd.ForwardMove), at + " forwardmove");
                Assert.True(Hex(e[7]) == Bits(g.Cmd.SideMove), at + " sidemove");
                Assert.True(Hex(e[8]) == Bits(g.Cmd.UpMove), at + " upmove");
                Assert.True(int.Parse(e[9]) == g.Cmd.Buttons, at + " buttons");
                Assert.True(int.Parse(e[10]) == g.Cmd.Impulse, at + " impulse");
                Assert.True(int.Parse(e[11]) == g.Cmd.WeaponSelect, at + " weaponselect");
                Assert.True(int.Parse(e[12]) == g.WeaponSubtype, at + " weaponsubtype");
                Assert.True(int.Parse(e[13]) == g.RandomSeed, at + " random_seed");
                Assert.True(int.Parse(e[14]) == g.Cmd.MouseDx, at + " mousedx");
                Assert.True(int.Parse(e[15]) == g.Cmd.MouseDy, at + " mousedy");
                commands++;
            }
        }

        Assert.True(commands > 2000, "the corpus decoded only " + commands + " commands");
    }

    [Fact]
    public void ATruncatedPacketIsRefused()
    {
        var p = Corpus()[0];
        Span<DecodedUserCmd> got = stackalloc DecodedUserCmd[PlayerCommandQueue.MaxBackup];
        Assert.Equal(-1, UserCmdCodec.Decode(p.Bytes, p.StartBit, p.EndBit - 1, p.TotalCmds, p.MaxMove, got));
    }

    [Fact]
    public void TheSeedIsStocksMd5PseudoRandom()
    {
        // MD5 of the four little-endian bytes of 0, digest bytes 6..9.
        // md5(00 00 00 00) = f1d3ff8443297732862df21dc4e57262
        Assert.Equal(0x2d863277u, UserCmdCodec.Md5PseudoRandom(0));
    }

    [Theory]
    // Computed with Python's hashlib, not .NET's MD5, so this does not check an
    // implementation against itself: md5(seed as 4 LE bytes), digest bytes 6..9.
    // Since tier1's checksum_md5.cpp left the build the CLIENT's command seed
    // comes from here too (the Md5PseudoRandom slot), so both sides stand on it.
    [InlineData(1u, 0xf70b7539u)]
    [InlineData(2u, 0xce9e4160u)]
    [InlineData(1000u, 0xca2bfc1eu)]
    [InlineData(123456789u, 0x462d08aeu)]
    [InlineData(0x7fffffffu, 0xedf71c10u)]
    [InlineData(0xffffffffu, 0x250f055bu)]
    public void Md5PseudoRandomMatchesAnIndependentMd5(uint seed, uint expected)
        => Assert.Equal(expected, UserCmdCodec.Md5PseudoRandom(seed));

    [Fact]
    public void TheCommandSeedDropsTheTopBitAsStockDid()
        => Assert.Equal(1844911120, UserCmdCodec.RandomSeedFor(0x7fffffff));

    [Fact]
    public void TheNativeBufferHoldsTheMostAPacketCanQueue()
    {
        // shell_gameclients.cpp sizes its answer array SOURCESHARP_MAX_QUEUED_USERCMDS
        // = 64 + 24; a plan larger than that is refused, not truncated.
        Assert.Equal(64 + 24, PlayerCommandQueue.MaxQueuedPerPacket);
    }

    [Fact]
    public void DecodeAndQueueAnswersTheQueuedCommandsInRunOrder()
    {
        var p = Corpus().Find(x => x.TotalCmds > x.NumCmds && x.NumCmds >= 2)!;
        _queue.MaxMoveMagnitude = () => p.MaxMove;
        var player = new EntityHandle(0x00ABCDEFu);
        var outCmds = new DecodedUserCmd[PlayerCommandQueue.MaxQueuedPerPacket];

        int n = _queue.DecodeAndQueue(player, p.Bytes, p.StartBit, p.EndBit, p.NumCmds,
                                                  p.TotalCmds, 0, true, outCmds, out int bitsRead);

        Assert.Equal(p.EndBit, bitsRead);
        Assert.Equal(p.NumCmds, n);
        // Oldest first: the packet's array is newest first.
        for (int i = 0; i < n; i++)
            Assert.Equal(int.Parse(p.Cmds[p.NumCmds - 1 - i][1]), outCmds[i].Cmd.CommandNumber);
    }

    [Fact]
    public void AnIgnoredPacketIsDecodedButQueuesNothing()
    {
        var p = Corpus()[0];
        _queue.MaxMoveMagnitude = () => p.MaxMove;
        var outCmds = new DecodedUserCmd[PlayerCommandQueue.MaxQueuedPerPacket];

        int n = _queue.DecodeAndQueue(new EntityHandle(0x00ABCDF0u), p.Bytes, p.StartBit, p.EndBit,
                                                  p.NumCmds, p.TotalCmds, 0, false, outCmds, out int bitsRead);
        Assert.Equal(0, n);
        Assert.Equal(p.EndBit, bitsRead);
    }

    [Fact]
    public void CountsOutOfRangeAreRefusedAsStockRefusedThem()
    {
        _queue.MaxMoveMagnitude = () => 1000f;
        var outCmds = new DecodedUserCmd[PlayerCommandQueue.MaxQueuedPerPacket];
        byte[] none = new byte[8];

        Assert.Equal(-1, _queue.DecodeAndQueue(new EntityHandle(1), none, 0, 64, 2, 1, 0, true, outCmds, out _));
        Assert.Equal(-1, _queue.DecodeAndQueue(new EntityHandle(1), none, 0, 64, 1, 63, 0, true, outCmds, out _));
        Assert.Equal(-1, _queue.DecodeAndQueue(new EntityHandle(1), none, 0, 64, -1, 1, 0, true, outCmds, out _));
    }
}
