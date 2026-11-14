using System.Buffers.Binary;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// What the bots know of a TF2 map before playing (P6 step 5), diffed against the placements it
/// is built from: every pack by size where it lies, every <c>info_player_teamspawn</c> as its
/// team's spawn, every respawn room and resupply cabinet as its team's volume, and nothing else
/// (no deathmatch spawn, no other entity). The placements come from the level's entities, because
/// a shipped TF2 map's entity lump is LZMA-compressed (the last fact holds that premise).
/// </summary>
public sealed class TFMapKnowledgeTests
{
    private static readonly MapEntity[] Placed =
    [
        new("info_player_teamspawn", null, new AiVector(10, 0, 0), null, null, Team: 2),
        new("item_healthkit_small", null, new AiVector(20, 0, 0), null, null),
        new("info_player_teamspawn", null, new AiVector(30, 0, 0), null, null, Team: 3),
        new("item_ammopack_medium", null, new AiVector(40, 0, 0), null, null),
        new("item_healthkit_full", null, new AiVector(50, 0, 0), null, null),
        new("func_respawnroom", null, default, new AiVector(-100, -100, 0), new AiVector(100, 100, 128), Team: 3),
        new("func_regenerate", null, default, new AiVector(0, 0, 0), new AiVector(40, 40, 96), Team: 2),
        new("info_player_deathmatch", null, new AiVector(60, 0, 0), null, null),
        new("prop_dynamic", null, new AiVector(70, 0, 0), null, null),
    ];

    private static readonly MapKnowledge Known = TFMapKnowledge.Build("ctf_test", new MapEntities(Placed, null), new WeaponKnowledge([]), default, BodyKnowledge.Default());

    [Fact]
    public void EveryPackIsKnownBySizeWhereItLies()
        => Assert.Equal([("item_healthkit_small", ItemKind.HealthSmall, new AiVector(20, 0, 0)), ("item_ammopack_medium", ItemKind.AmmoMedium, new AiVector(40, 0, 0)),
                         ("item_healthkit_full", ItemKind.HealthLarge, new AiVector(50, 0, 0))],
                        Known.Items.Select(i => (i.ClassName, i.Kind, i.Position)));

    [Fact]
    public void APackComesBackAsTheModsPackDoes()
        => Assert.All(Known.Items, i => Assert.Equal(global::TF2.Server.Entities.Items.TFPowerup.ItemRespawnTime, i.RespawnSeconds));

    [Fact]
    public void EveryTeamSpawnIsItsTeamsAndThereIsNoDeathmatchSpawn()
    {
        Assert.Equal([new TeamSpawn(2, new AiVector(10, 0, 0)), new TeamSpawn(3, new AiVector(30, 0, 0))], Known.TeamSpawns.ToArray());
        Assert.Empty(Known.Spawns);
    }

    [Fact]
    public void EveryRespawnRoomAndCabinetIsItsTeamsVolume()
        => Assert.Equal([new TeamZone(3, ZoneKind.RespawnRoom, new AiVector(-100, -100, 0), new AiVector(100, 100, 128)),
                         new TeamZone(2, ZoneKind.Resupply, new AiVector(0, 0, 0), new AiVector(40, 40, 96))], Known.TeamZones.ToArray());

    [Fact]
    public void AZoneHoldsWhatIsInsideItsVolume()
    {
        var zone = new TeamZone(2, ZoneKind.RespawnRoom, new AiVector(0, 0, 0), new AiVector(10, 10, 10));
        Assert.True(zone.Contains(new AiVector(5, 5, 5)));
        Assert.False(zone.Contains(new AiVector(5, 11, 5)));
    }

    /// <summary>
    /// The premise the placements rest on: the shipped ctf_2fort's entity lump has a non-zero
    /// uncompressed size (its LZMA mark), so the lump reader cannot read it: it reads no entities
    /// and says why. Should a reader learn LZMA, this goes red and the knowledge can come from the
    /// lump again.
    /// </summary>
    [Fact]
    public void TheShippedTwoFortsEntityLumpIsCompressed()
    {
        string path = RepoLayout.Repo("bin", RepoLayout.Config, "content", "tf", "maps", "ctf_2fort.bsp");
        Assert.True(File.Exists(path), $"{path} is missing: the game's content dir holds the shipped maps");
        byte[] head = new byte[8 + 16];
        using (FileStream f = File.OpenRead(path))
            f.ReadExactly(head);
        Assert.NotEqual(0, BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(8 + 12)));
        MapEntities read = MapEntities.Read(File.ReadAllBytes(path));
        Assert.Empty(read.All);
        Assert.Contains("LZMA-compressed", read.Unread, StringComparison.Ordinal);
    }
}
