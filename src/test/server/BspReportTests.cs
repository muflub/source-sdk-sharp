using System.IO.Compression;
using System.Text;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Reading a whole map file.
///
/// Every decoder here reads a packed binary structure at a fixed offset, which
/// is the kind of code that fails quietly: an offset that is wrong by four bytes
/// produces plausible numbers rather than an exception, and a plausible number in
/// a debugger is worse than no number at all. So the maps these tests read are
/// built here, byte by byte, with the values chosen so that reading the wrong
/// field cannot possibly return the right answer -- no zeroes, no repeats, and
/// nothing that could be confused with its neighbour.
///
/// The offsets themselves come from the structures in public/bspfile.h and
/// public/gamebspfile.h, and were checked against two real compiled maps.
/// </summary>
public class BspReportTests
{

    //-------------------------------------------------------------------------

    [Fact]
    public void HeaderAndLumps()
    {
        var map = new Builder();

        map.Text(0, "{\n\"classname\" \"worldspawn\"\n}\n{\n\"classname\" \"light\"\n}\n{\n\"classname\" \"light\"\n}\0");

        // Three planes' worth of bytes, so the count comes from the stride
        // rather than from anything the header says.
        map.Bytes(1, new byte[20 * 3]);
        map.Bytes(8, new byte[4 * 9]);
        map.Version(10, 1);
        map.Bytes(10, new byte[32 * 5]);

        var report = Read(map, "test_header");

        Assert.True(report.Ident == "VBSP", "the identifier is read");
        Assert.True(report.Version == 21, "the version is read");
        Assert.True(report.Revision == 4242, "the revision is read");
        Assert.True(report.Map == "test_header", "the map is named");

        Assert.True(report.Lumps.Count == 64, "all sixty-four lumps are reported");
        Assert.True(report.Lumps[43].Name == "TEXDATA_STRING_DATA", "a lump is named");
        Assert.True(report.Lumps[1].Length == 60, "a lump's length is reported");

        // The whole point of the stride table: a size becomes a count.
        Assert.True(report.Lumps[1].Records == 3, "a fixed-size lump reports a record count");
        Assert.True(report.Lumps[8].Records == 9, "luxels are counted");

        // A lump with no fixed record must say so rather than claim zero, which
        // would read as "this map has no entities".
        Assert.True(report.Lumps[0].Records == -1, "a lump with no fixed record reports no count");

        // The leaf record shed its ambient light cube in version 1, and reading
        // an old map with the new stride would report nearly twice the leaves.
        Assert.True(report.Lumps[10].Records == 5, "the leaf stride follows the lump version");

        Assert.True(report.Entities.Count == 3, "entities are read");
        Assert.True(report.Classes[0].ClassName == "light" && report.Classes[0].Count == 2, "classnames are counted");
        Assert.True(report.Classes[1].ClassName == "worldspawn", "the histogram is ordered by count");

        Assert.True(report.Lumps[42].Length == 0, "a lump that is not there is empty");
        Assert.True(report.Warnings.Count == 0, "nothing was warned about");
    }

    [Fact]
    public void Materials()
    {
        var map = new Builder();

        // Deliberately with an empty name in the middle: the offset table is
        // what separates the names, and anything that split the text on nulls
        // instead would lose it and shift every name after it.
        var names = new List<string> { "concrete/floor01", "", "tools/toolsnodraw" };
        var text = new List<byte>();
        var offsets = new List<byte>();

        foreach (var name in names)
        {
            offsets.AddRange(BitConverter.GetBytes(text.Count));
            text.AddRange(Encoding.UTF8.GetBytes(name));
            text.Add(0);
        }

        map.Bytes(43, text.ToArray());
        map.Bytes(44, offsets.ToArray());

        // Two texdatas, pointing at the first and third names.
        var texData = new byte[64];
        Write(texData, 0, 0.5f); Write(texData, 4, 0.25f); Write(texData, 8, 0.125f);
        Write(texData, 12, 0); Write(texData, 16, 512); Write(texData, 20, 256);
        Write(texData, 24, 128); Write(texData, 28, 64);
        Write(texData, 32 + 12, 2); Write(texData, 32 + 16, 32); Write(texData, 32 + 20, 16);
        map.Bytes(2, texData);

        // Two texinfos: the first onto texdata 1, the second onto texdata 0.
        var texInfo = new byte[144];
        Write(texInfo, 68, 1);
        Write(texInfo, 72 + 68, 0);
        map.Bytes(6, texInfo);

        // Three faces: two through texinfo 1 onto texdata 0, one onto texdata 1.
        // Each with an area and a lightmap, because what a material costs is
        // added up across its faces rather than read from anywhere.
        var faces = new byte[56 * 3];

        for (int i = 0; i < 3; i++)
        {
            int at = i * 56;

            WriteShort(faces, at + 10, (short)(i < 2 ? 1 : 0));
            Write(faces, at + 24, 100f * (i + 1));    // area
            Write(faces, at + 36, 1); Write(faces, at + 40, 1);   // a 2x2 lightmap
            faces[at + 17] = 255; faces[at + 18] = 255; faces[at + 19] = 255;
        }

        map.Bytes(7, faces);

        var report = Read(map, "test_materials");

        Assert.True(report.Materials.Count == 3, "every material name is read");
        Assert.True(report.Materials[0] == "concrete/floor01", "a material name is read whole");
        Assert.True(report.Materials[1] == "", "an empty material name keeps its place");
        Assert.True(report.Materials[2] == "tools/toolsnodraw", "the name after an empty one is not shifted");

        Assert.True(report.TexDatas.Count == 2, "texdata is read");
        Assert.True(report.TexDatas[0].Name == "concrete/floor01", "a texdata resolves to its name");
        Assert.True(report.TexDatas[1].Name == "tools/toolsnodraw", "the second texdata resolves to its own name");
        Assert.True(report.TexDatas[0].Width == 512 && report.TexDatas[0].Height == 256, "the source image size is read");
        Assert.True(report.TexDatas[0].ViewWidth == 128 && report.TexDatas[0].ViewHeight == 64, "the view size is read");
        Assert.True(Math.Abs(report.TexDatas[0].Reflectivity[1] - 0.25f) < 0.0001f, "reflectivity is read");

        // Two hops, faces to texinfo to texdata. This is the column that says
        // which materials the map is actually built out of.
        Assert.True(report.TexDatas[0].Faces == 2 && report.TexDatas[1].Faces == 1, "faces are counted through texinfo onto the material");

        // What the map is actually built out of, and what lighting it cost --
        // neither is in the lump, and both are the reason to walk the faces.
        Assert.True(report.TexDatas[0].Area == 300f && report.TexDatas[1].Area == 300f, "surface area is added up per material");
        Assert.True(report.TexDatas[0].Luxels == 8 && report.TexDatas[1].Luxels == 4, "lightmap samples are added up per material");
    }

    [Fact]
    public void StaticProps()
    {
        var map = new Builder();
        map.Bytes(35, StaticPropLump(version: 5, stride: 60));

        var report = Read(map, "test_props");

        Assert.True(report.GameLumps.Count == 1, "the game lump directory is read");
        Assert.True(report.GameLumps[0].Id == "sprp", "a game lump is named by its four-character code");
        Assert.True(report.GameLumps[0].Version == 5, "a game lump's version is read");

        Assert.True(report.PropModels.Count == 2, "the prop model dictionary is read");
        Assert.True(report.PropModels[0] == "models/props_junk/wood_crate001a.mdl", "a model name is trimmed of its padding");

        Assert.True(report.Props.Count == 2, "both props are read");

        var first = report.Props[0];

        Assert.True(first.Model == "models/props_junk/wood_crate001a.mdl", "a prop's model is resolved through the dictionary");
        Assert.True(first.Origin[0] == 128f && first.Origin[1] == -64f && first.Origin[2] == 32.5f, "a prop's origin is read");
        Assert.True(first.Angles[1] == 270f, "a prop's angles are read");
        Assert.True(first.Skin == 3, "a prop's skin is read");
        Assert.True(first.FadeMin == 400f && first.FadeMax == 900f, "a prop's fade distances are read");
        Assert.True(first.Solid == 6, "a prop's solidity is read");
        Assert.True(first.Flags == 0x11, "a prop's flags are read");
        Assert.True(first.FirstLeaf == 7 && first.LeafCount == 4, "a prop's leaves are read");

        Assert.True(report.Props[1].Model == "models/props_c17/oildrum001.mdl", "the second prop is read");
        Assert.True(report.Props[1].Skin == 1, "the second prop's own values are read");
    }

    /// <summary>
    /// The version the tools in this tree write.
    ///
    /// Version 10 padded the record and moved the flags to the end. Everything
    /// between the origin and the lighting origin stayed where it was, and this
    /// is what says so: the same values, read out of a longer record.
    /// </summary>
    [Fact]
    public void StaticPropsVersion10()
    {
        var map = new Builder();
        map.Bytes(35, StaticPropLump(version: 10, stride: 72));

        var report = Read(map, "test_props_v10");

        Assert.True(report.Props.Count == 2, "version ten props are read");
        Assert.True(report.Props[0].Skin == 3, "a version ten prop's skin is still at the same offset");
        Assert.True(report.Props[0].FadeMin == 400f && report.Props[0].FadeMax == 900f, "a version ten prop's fades are still at the same offset");

        // The one field that did move. Reading it out of the old place would
        // give whatever the padding byte holds.
        Assert.True(report.Props[0].Flags == 0x140, "a version ten prop's flags are read from the end of the record");
    }

    [Fact]
    public void Lights()
    {
        var map = new Builder();

        var light = new byte[88];
        Write(light, 0, 16f); Write(light, 4, 32f); Write(light, 8, 64f);
        Write(light, 12, 0.5f); Write(light, 16, 0.25f); Write(light, 20, 0.75f);
        Write(light, 24, 0f); Write(light, 28, 0f); Write(light, 32, -1f);
        Write(light, 36, 9);                          // cluster
        Write(light, 40, 2);                          // emit_spotlight
        Write(light, 44, 33);                         // style
        Write(light, 48, 0.7f);                       // stopdot
        Write(light, 52, 0.6f);                       // stopdot2
        Write(light, 56, 1f);                         // exponent
        Write(light, 60, 512f);                       // radius
        Write(light, 64, 0.5f);                       // constant attenuation, NOT the radius
        Write(light, 84, 41);                         // owner
        map.Bytes(15, light);

        var hdr = new byte[88];
        Write(hdr, 40, 3);                            // emit_skylight
        map.Bytes(54, hdr);

        var report = Read(map, "test_lights");

        Assert.True(report.WorldLights.Count == 2, "both lighting sets are read");

        var spot = report.WorldLights[0];

        Assert.True(spot.Origin[2] == 64f, "a light's origin is read");
        Assert.True(Math.Abs(spot.Intensity[2] - 0.75f) < 0.0001f, "a light's intensity is read");
        Assert.True(spot.Normal[2] == -1f, "a light's normal is read");
        Assert.True(spot.Type == "spotlight", "a light's kind is named");
        Assert.True(spot.Style == 33, "a light's style is read");

        // The trap: the constant attenuation term sits directly after the radius,
        // and reading it instead gives a number that looks like a distance.
        Assert.True(spot.Radius == 512f, "a light's radius is the radius");

        Assert.True(spot.Owner == 41, "a light's owning entity is read");
        Assert.True(!spot.Hdr, "an LDR light is not marked HDR");
        Assert.True(report.WorldLights[1].Hdr, "the HDR light is marked");
        Assert.True(report.WorldLights[1].Type == "skylight", "the HDR light's kind is read");
    }

    [Fact]
    public void SmallLumps()
    {
        var map = new Builder();

        var cubemaps = new byte[32];
        Write(cubemaps, 0, 100); Write(cubemaps, 4, -200); Write(cubemaps, 8, 300);
        cubemaps[12] = 3;
        Write(cubemaps, 16, 1); Write(cubemaps, 20, 2); Write(cubemaps, 24, 3);
        cubemaps[28] = 0;
        map.Bytes(42, cubemaps);

        var models = new byte[48];
        Write(models, 0, -512f); Write(models, 12, 512f); Write(models, 24, 8f);
        map.Bytes(14, models);

        var disp = new byte[176];
        Write(disp, 0, 64f); Write(disp, 4, 128f); Write(disp, 8, -16f);
        Write(disp, 20, 3);                           // power
        Write(disp, 32, 1);                           // contents
        WriteShort(disp, 36, 91);                     // the face it came from
        map.Bytes(26, disp);

        // One material, one texdata, one texinfo, so the overlay has a name to
        // resolve to rather than an index.
        map.Bytes(43, Encoding.UTF8.GetBytes("decals/exit\0"));
        map.Bytes(44, BitConverter.GetBytes(0));

        var texData = new byte[32];
        Write(texData, 12, 0);
        map.Bytes(2, texData);

        var texInfo = new byte[72];
        Write(texInfo, 68, 0);
        map.Bytes(6, texInfo);

        var overlay = new byte[352];
        Write(overlay, 0, 77);                        // id
        WriteShort(overlay, 4, 0);                    // texinfo
        WriteShort(overlay, 6, unchecked((short)((2 << 14) | 5)));   // render order and face count
        Write(overlay, 328, 1f); Write(overlay, 332, 2f); Write(overlay, 336, 3f);
        map.Bytes(45, overlay);

        map.Bytes(4, BitConverter.GetBytes(37));

        var report = Read(map, "test_small");

        Assert.True(report.Cubemaps.Count == 2, "cubemaps are read");
        Assert.True(report.Cubemaps[0].Origin[0] == 100 && report.Cubemaps[0].Origin[1] == -200, "a cubemap's position is read");
        Assert.True(report.Cubemaps[0].Size == 3, "a cubemap's size is read");
        Assert.True(report.Cubemaps[1].Size == 0, "a default-sized cubemap reads as zero");

        Assert.True(report.Models.Count == 1, "brush models are read");
        Assert.True(report.Models[0].Mins[0] == -512f && report.Models[0].Maxs[0] == 512f, "a brush model's bounds are read");

        Assert.True(report.Displacements.Count == 1, "displacements are read");
        Assert.True(report.Displacements[0].StartPosition[1] == 128f, "a displacement's start is read");
        Assert.True(report.Displacements[0].Power == 3, "a displacement's power is read");
        Assert.True(report.Displacements[0].Vertices == 81, "a displacement's vertex count is worked out");
        Assert.True(report.Displacements[0].MapFace == 91, "a displacement's face is read");

        Assert.True(report.Overlays.Count == 1, "overlays are read");
        Assert.True(report.Overlays[0].Id == 77, "an overlay's id is read");
        Assert.True(report.Overlays[0].Material == "decals/exit", "an overlay resolves its material");
        Assert.True(report.Overlays[0].Origin[2] == 3f, "an overlay's origin is read");

        // The face count shares its two bytes with the render order, and taking
        // the whole word would report thirty-two thousand faces on one decal.
        Assert.True(report.Overlays[0].FaceCount == 5, "an overlay's face count is masked out of its render order");

        Assert.True(report.VisClusters == 37, "the visibility cluster count is read");
    }

    [Fact]
    public void PakFile()
    {
        var map = new Builder();

        using var zipped = new MemoryStream();

        using (var zip = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = new StreamWriter(zip.CreateEntry("materials/maps/test/cube.vtf").Open());
            entry.Write(new string('x', 300));
        }

        map.Bytes(40, zipped.ToArray());

        var report = Read(map, "test_pak");

        Assert.True(report.Pak.Count == 1, "the pakfile is listed");
        Assert.True(report.Pak[0].Name == "materials/maps/test/cube.vtf", "a pakfile entry keeps its whole path");
        Assert.True(report.Pak[0].Bytes == 300, "a pakfile entry reports its real size");
        Assert.True(report.Warnings.Count == 0, "the pakfile opened without complaint");
    }

    /// <summary>
    /// The small lumps that each answer one question about how a map was built.
    ///
    /// None of them is visible anywhere else in the tool, and each is one number
    /// that changes what somebody goes and looks at next: whether the map has
    /// areaportals, whether it has water, and whether vrad baked the prop
    /// lighting it was asked to.
    /// </summary>
    [Fact]
    public void BuildLumps()
    {
        var map = new Builder();

        // Two areas, the second with three portals starting at the second one.
        var areas = new byte[16];
        Write(areas, 0, 0); Write(areas, 4, 0);
        Write(areas, 8, 3); Write(areas, 12, 1);
        map.Bytes(20, areas);

        var portal = new byte[12];
        WriteShort(portal, 0, 7);                     // the key an entity binds to
        WriteShort(portal, 2, 2);                     // the area it looks into
        WriteShort(portal, 4, 40);                    // first clip vertex
        WriteShort(portal, 6, 4);                     // clip vertices
        Write(portal, 8, 91);                         // plane
        map.Bytes(21, portal);

        // One body of water, naming the material on its surface.
        map.Bytes(43, Encoding.UTF8.GetBytes("water/water_canal\0"));
        map.Bytes(44, BitConverter.GetBytes(0));

        var texData = new byte[32];
        Write(texData, 12, 0);
        map.Bytes(2, texData);

        var texInfo = new byte[72];
        Write(texInfo, 68, 0);
        map.Bytes(6, texInfo);

        var water = new byte[10];
        Write(water, 0, 128f);
        Write(water, 4, -64f);
        WriteShort(water, 8, 0);
        map.Bytes(36, water);

        map.Bytes(9, BitConverter.GetBytes(2));       // occluder count
        map.Bytes(59, BitConverter.GetBytes(3));      // both static prop lighting bits

        var report = Read(map, "test_build");

        Assert.True(report.Areas.Count == 2, "areas are read");
        Assert.True(report.Areas[1].Portals == 3 && report.Areas[1].FirstPortal == 1, "an area's portals are read, and not swapped with where they start");

        Assert.True(report.AreaPortals.Count == 1, "areaportals are read");
        Assert.True(report.AreaPortals[0].Key == 7, "an areaportal's key is read");
        Assert.True(report.AreaPortals[0].OtherArea == 2, "an areaportal says which area it looks into");
        Assert.True(report.AreaPortals[0].Plane == 91, "an areaportal's plane is read");

        Assert.True(report.Water.Count == 1, "water is read");
        Assert.True(report.Water[0].SurfaceZ == 128f && report.Water[0].MinZ == -64f, "water knows its surface and its floor");
        Assert.True(report.Water[0].Material == "water/water_canal", "water resolves the material on it");

        Assert.True(report.Occluders == 2, "occluders are counted");

        // Two bits, both set, and both worth reading as words: a map compiled
        // without baked prop lighting looks subtly wrong and says nothing.
        Assert.True(report.LevelFlags.Contains("LDR") && report.LevelFlags.Contains("HDR"), "the level's flags are spelled out");

        Assert.True(Read(new Builder(), "test_bare").Areas.Count == 0, "a map with none of these says so");
    }

    /// <summary>
    /// How much of the map an average place in it can see.
    ///
    /// The one important fact about a compiled map that is nowhere in the file:
    /// the lump holds the visible sets, not their sizes, so the only way to know
    /// is to decompress every one of them and count. The encoding is zero runs,
    /// and the trap in it is that the run length counts bytes rather than
    /// clusters -- reading it as clusters under-counts by eight.
    /// </summary>
    [Fact]
    public void Visibility()
    {
        var map = new Builder();

        // Three clusters, so one byte holds a whole visible set.
        var vis = new List<byte>();
        vis.AddRange(BitConverter.GetBytes(3));

        // The offset table: two per cluster, and only the first is the one that
        // says what can be seen.
        int table = 4 + 3 * 8;
        vis.AddRange(BitConverter.GetBytes(table));       // cluster 0 sees...
        vis.AddRange(BitConverter.GetBytes(0));
        vis.AddRange(BitConverter.GetBytes(table + 1));   // cluster 1 sees...
        vis.AddRange(BitConverter.GetBytes(0));
        vis.AddRange(BitConverter.GetBytes(table + 2));   // cluster 2 sees...
        vis.AddRange(BitConverter.GetBytes(0));

        vis.Add(0b0000_0111);                             // all three
        vis.Add(0b0000_0001);                             // one
        vis.Add(0);                                       // a zero run...
        vis.Add(1);                                       // ...of one byte: nothing

        map.Bytes(4, vis.ToArray());

        var report = Read(map, "test_vis");

        Assert.True(report.VisClusters == 3, "the cluster count is read");
        Assert.True(report.VisMax == 3, "the worst case is the biggest visible set");
        Assert.True(report.VisAverage == (3 + 1 + 0) / 3, "the average is across every cluster");

        // A map compiled without vis draws everything, everywhere, and is the
        // commonest reason a small map runs badly.
        var none = Read(new Builder(), "test_novis");
        Assert.True(none.VisClusters == 0 && none.VisAverage == -1, "a map with no visibility reports none rather than nothing");
    }

    /// <summary>
    /// The lumps read one at a time.
    ///
    /// Big enough that sending them with everything else would mean paying
    /// megabytes to look at something small, so they have an endpoint of their
    /// own -- and every one of them resolves a material, which is the part most
    /// worth checking.
    /// </summary>
    [Fact]
    public void Detail()
    {
        var map = new Builder();

        map.Bytes(43, Encoding.UTF8.GetBytes("nature/blendrock\0tools/toolsnodraw\0"));

        var table = new byte[8];
        Write(table, 0, 0);
        Write(table, 4, 17);
        map.Bytes(44, table);

        var texData = new byte[64];
        Write(texData, 12, 0);
        Write(texData, 32 + 12, 1);
        map.Bytes(2, texData);

        // Two texinfos: an ordinary bumped rock at sixteen units per luxel, and
        // a nodraw at the same scale.
        var texInfo = new byte[144];
        Write(texInfo, 32, 1f / 16f);                 // lightmap S axis, whose length is the scale
        Write(texInfo, 64, 0x0800);                   // SURF_BUMPLIGHT
        Write(texInfo, 68, 0);
        Write(texInfo, 72 + 32, 1f / 8f);
        Write(texInfo, 72 + 64, 0x0080 | 0x0200);     // SURF_NODRAW | SURF_SKIP
        Write(texInfo, 72 + 68, 1);
        map.Bytes(6, texInfo);

        // Two faces: one bumped and lit by two styles, one with no lightmap.
        var faces = new byte[112];
        WriteShort(faces, 0, 12);                     // plane
        faces[2] = 1;                                 // side
        faces[3] = 1;                                 // on node
        Write(faces, 4, 300);                         // first edge
        WriteShort(faces, 8, 4);                      // edges
        WriteShort(faces, 10, 0);                     // texinfo
        WriteShort(faces, 12, -1);                    // no displacement
        faces[16] = 0; faces[17] = 3; faces[18] = 255; faces[19] = 255;
        Write(faces, 20, 4096);                       // lightmap offset
        Write(faces, 24, 512f);                       // area
        Write(faces, 36, 3); Write(faces, 40, 4);     // lightmap size, less one each way
        Write(faces, 44, 77);                         // the face it was split from
        WriteShort(faces, 48, unchecked((short)0x8002));  // two primitives, and the shadow bit

        WriteShort(faces, 56 + 10, 1);                // the nodraw face
        Write(faces, 56 + 20, -1);                    // and it has no lightmap at all
        map.Bytes(7, faces);

        string path = Write(map.Build());

        try
        {
            var detail = BspLumpDetail.Read(path, "test_detail", 7);

            Assert.True(detail.Faces.Count == 2, "faces are read one lump at a time");
            Assert.True(detail.Name == "FACES" && detail.Known, "the lump says what it is");

            var face = detail.Faces[0];

            Assert.True(face.Material == "nature/blendrock", "a face resolves its material");
            Assert.True(face.Area == 512f, "a face's area is read");
            Assert.True(face.Plane == 12 && face.Side && face.OnNode && face.Edges == 4, "a face's plane and winding are read");

            // Stored one less than the samples on each axis, because a flat face
            // still has a luxel at both ends of it.
            Assert.True(face.LightmapWidth == 4 && face.LightmapHeight == 5, "a face's lightmap is the stored size plus one each way");

            Assert.True(face.Styles == 2, "a face's light styles are counted, and 255 is not one");

            // 4x5 samples, two styles, and four lightmaps because it is bumped.
            // Leaving out any of the three under-reports by a plausible factor.
            Assert.True(face.Luxels == 4 * 5 * 2 * 4, "a face's luxels count styles and the bump basis");

            Assert.True(detail.Faces[1].Luxels == 0, "a face with no lightmap costs no luxels");
            Assert.True(face.OrigFace == 77, "a face names what it was split from");

            // The top bit of the primitive count is a flag about shadows, and
            // leaving it in reports thirty-two thousand primitives on one face.
            Assert.True(face.Primitives == 2, "a face's primitive count is not its shadow flag");

            var infos = BspLumpDetail.Read(path, "test_detail", 6);

            Assert.True(infos.TexInfos.Count == 2, "texinfo is read");
            Assert.True(infos.TexInfos[0].Material == "nature/blendrock", "a texinfo resolves its material");
            Assert.True(Math.Abs(infos.TexInfos[0].LightmapScale - 16f) < 0.01f
                && Math.Abs(infos.TexInfos[1].LightmapScale - 8f) < 0.01f, "a texinfo's lightmap scale is the reciprocal of its axis");

            // The flags worth reading a map for: all of these are invisible in
            // the game and all of them change what the compiler did.
            Assert.True(infos.TexInfos[1].Surface.Contains("nodraw") && infos.TexInfos[1].Surface.Contains("skip"), "a texinfo's surface flags are spelled out");
            Assert.True(infos.TexInfos[0].Surface == "bumplight", "a surface with no flags says nothing");

            Assert.True(BspLumpDetail.Read(path, "test_detail", 27).Known, "the original faces are the same record read from another lump");
            Assert.True(!BspLumpDetail.Read(path, "test_detail", 8).Known, "a lump nothing decodes says so rather than coming back empty");
        }
        finally
        {
            File.Delete(path);
        }

        Leaves();
        Brushes();
    }

    /// <summary>
    /// The leaves, at both versions of the record.
    ///
    /// Version 1 moved the ambient light cube into a lump of its own and took
    /// twenty-four bytes with it. Everything read here is in the part that did
    /// not move, so the same values written at both strides must read back the
    /// same -- and a reader that used one stride for both would find nonsense in
    /// every leaf after the first.
    /// </summary>
    private static void Leaves()
    {
        foreach (int version in new[] { 0, 1 })
        {
            int stride = version >= 1 ? 32 : 56;

            var map = new Builder();
            map.Version(10, version);

            var leaves = new byte[stride * 2];

            for (int i = 0; i < 2; i++)
            {
                int at = i * stride;

                Write(leaves, at, i == 0 ? 0x10001 : 0x1);       // solid, and playerclip on the first
                WriteShort(leaves, at + 4, (short)(i == 0 ? 5 : -1));

                // Nine bits of area and seven of flags in one short. Reading the
                // whole short as the area gives thousands on a map with four.
                WriteShort(leaves, at + 6, unchecked((short)((2 << 9) | 3)));

                WriteShort(leaves, at + 8, -128); WriteShort(leaves, at + 10, -64);
                WriteShort(leaves, at + 12, 0);
                WriteShort(leaves, at + 14, 128); WriteShort(leaves, at + 16, 64);
                WriteShort(leaves, at + 18, 96);
                WriteShort(leaves, at + 20, 11); WriteShort(leaves, at + 22, 4);
                WriteShort(leaves, at + 24, 7); WriteShort(leaves, at + 26, 2);
                WriteShort(leaves, at + 28, (short)(i == 0 ? 1 : -1));
            }

            map.Bytes(10, leaves);

            string path = Write(map.Build());

            try
            {
                var read = BspLumpDetail.Read(path, "test_leaves", 10);

                Assert.True(read.Leaves.Count == 2, $"leaves are read at lump version {version}");

                var leaf = read.Leaves[0];

                Assert.True(leaf.Contents.Contains("solid") && leaf.Contents.Contains("playerclip"), $"a leaf's contents are spelled out at version {version}");
                Assert.True(leaf.Cluster == 5, $"a leaf's cluster is read at version {version}");
                Assert.True(leaf.Area == 3, $"a leaf's area is masked out of its flags at version {version}");
                Assert.True(leaf.Faces == 4 && leaf.Brushes == 2 && leaf.FirstFace == 11, $"a leaf's faces and brushes are read at version {version}");
                Assert.True(leaf.Mins[0] == -128 && leaf.Maxs[2] == 96, $"a leaf's bounds are read at version {version}");
                Assert.True(leaf.Water == 1 && read.Leaves[1].Water == -1, $"a leaf out of water says so at version {version}");

                // The second record is the one that catches a wrong stride: it
                // reads as the tail of the first.
                Assert.True(read.Leaves[1].Cluster == -1 && read.Leaves[1].Area == 3, $"the second leaf is a leaf at version {version}");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    private static void Brushes()
    {
        var map = new Builder();

        map.Bytes(43, Encoding.UTF8.GetBytes("tools/toolsclip\0"));
        map.Bytes(44, BitConverter.GetBytes(0));

        var texData = new byte[32];
        Write(texData, 12, 0);
        map.Bytes(2, texData);

        var texInfo = new byte[72];
        Write(texInfo, 68, 0);
        map.Bytes(6, texInfo);

        var brushes = new byte[24];
        Write(brushes, 0, 0); Write(brushes, 4, 6); Write(brushes, 8, 0x1);
        Write(brushes, 12, 6); Write(brushes, 16, 4); Write(brushes, 20, 0x10000 | 0x8000000);
        map.Bytes(18, brushes);

        var sides = new byte[16];
        WriteShort(sides, 0, 31); WriteShort(sides, 2, 0);
        WriteShort(sides, 4, -1); WriteShort(sides, 6, 0);
        WriteShort(sides, 8, 32); WriteShort(sides, 10, 0);
        WriteShort(sides, 12, 5); WriteShort(sides, 14, 1);
        map.Bytes(19, sides);

        string path = Write(map.Build());

        try
        {
            var read = BspLumpDetail.Read(path, "test_brushes", 18);

            Assert.True(read.Brushes.Count == 2, "brushes are read");
            Assert.True(read.Brushes[0].Sides == 6, "a brush's sides are read");
            Assert.True(read.Brushes[0].Contents == "solid", "a solid brush says solid");

            // The combination is what makes this readable: nobody says
            // "contents 0x8010000", they say "playerclip, and a detail brush".
            Assert.True(read.Brushes[1].Contents == "playerclip detail", "a brush's contents are spelled out in the order the bits are in");

            var sideList = BspLumpDetail.Read(path, "test_brushes", 19).BrushSides;

            Assert.True(sideList.Count == 2, "brush sides are read");
            Assert.True(sideList[0].Material == "tools/toolsclip", "a brush side resolves its material");
            Assert.True(sideList[0].Plane == 31, "a brush side's plane is read");
            Assert.True(sideList[0].DispInfo == -1, "a brush side with no displacement says so");
            Assert.True(!sideList[0].Bevel && sideList[1].Bevel, "a bevel is told from a real side");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A map that has been cut short.
    ///
    /// The normal state of a build output that was interrupted, and the moment a
    /// tool that reads maps is most useful. Throwing here would mean the one file
    /// worth inspecting is the one that cannot be opened.
    /// </summary>
    [Fact]
    public void Truncation()
    {
        var map = new Builder();
        map.Bytes(1, new byte[20 * 2]);
        map.Text(0, "{\n\"classname\" \"worldspawn\"\n}\0");

        var bytes = map.Build();

        // Point the faces lump at a megabyte that is not there.
        int at = 8 + 7 * 16;
        BitConverter.GetBytes(1 << 20).CopyTo(bytes, at);
        BitConverter.GetBytes(1 << 20).CopyTo(bytes, at + 4);

        var report = Read(bytes, "test_truncated");

        Assert.True(report.Warnings.Count == 1, "a lump past the end is complained about");
        Assert.True(report.Warnings[0].Contains("FACES"), "the complaint names the lump");
        Assert.True(report.Entities.Count == 1, "the rest of the map is still read");
        Assert.True(report.Lumps[1].Records == 2, "the intact lumps still report their counts");
        Assert.True(report.Lumps[7].Length == 0, "the broken lump is reported as empty");

        bool refused = false;

        try
        {
            Read("not a bsp"u8.ToArray(), "test_junk");
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        Assert.True(refused, "a file that is not a map is refused");
    }

    /// <summary>
    /// The stride table itself.
    ///
    /// It is what turns every lump size on the page into a count, so a wrong
    /// entry here is a wrong number in front of somebody with no way to tell.
    /// </summary>
    [Fact]
    public void Strides()
    {
        Assert.True(BspReport.StrideOf(1, 0) == 20, "a plane is twenty bytes");
        Assert.True(BspReport.StrideOf(3, 0) == 12, "a vertex is three floats");
        Assert.True(BspReport.StrideOf(6, 0) == 72, "a texinfo is seventy-two bytes");
        Assert.True(BspReport.StrideOf(7, 1) == 56, "a face is fifty-six bytes");
        Assert.True(BspReport.StrideOf(14, 0) == 48, "a brush model is forty-eight bytes");
        Assert.True(BspReport.StrideOf(15, 0) == 88, "a world light is eighty-eight bytes");
        Assert.True(BspReport.StrideOf(26, 0) == 176, "a displacement is a hundred and seventy-six bytes");
        Assert.True(BspReport.StrideOf(45, 0) == 352, "an overlay is three hundred and fifty-two bytes");

        // The one stride that depends on the lump's version.
        Assert.True(BspReport.StrideOf(10, 0) == 56, "an old leaf carries its light cube");
        Assert.True(BspReport.StrideOf(10, 1) == 32, "a version one leaf does not");

        // The lumps that are streams rather than arrays have to report nothing
        // rather than a plausible number.
        Assert.True(BspReport.StrideOf(0, 0) == 0, "the entity lump has no stride");
        Assert.True(BspReport.StrideOf(4, 0) == 0, "visibility has no stride");
        Assert.True(BspReport.StrideOf(40, 0) == 0, "the pakfile has no stride");
        Assert.True(BspReport.StrideOf(35, 0) == 0, "the game lump has no stride");

        Assert.True(BspReport.LumpNames.Length == 64, "every lump has a name");
        Assert.True(BspReport.LumpNames[35] == "GAME_LUMP", "a lump is named as the engine names it");
    }

    //-------------------------------------------------------------------------
    // Building a map to read.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A small but complete map, for anything that needs a real file to point at.
    ///
    /// Shared with the route tests rather than built twice: what they check is
    /// that the endpoint hands the report out intact, and two different sample
    /// maps would let the two halves drift apart without either failing.
    /// </summary>
    /// <summary>A map with two brush models: *0 the world, *1 a 64 x 112 x 2 slab.</summary>
    internal static byte[] MapWithBrushModels()
    {
        var map = new Builder();

        map.Text(0, "{\n\"classname\" \"worldspawn\"\n}\0");

        var models = new byte[96];
        Write(models, 0, -512f); Write(models, 4, -512f); Write(models, 8, -64f);
        Write(models, 12, 512f); Write(models, 16, 512f); Write(models, 20, 64f);
        Write(models, 48, -32f); Write(models, 52, -56f); Write(models, 56, -1f);
        Write(models, 60, 32f); Write(models, 64, 56f); Write(models, 68, 1f);
        map.Bytes(14, models);

        return map.Build();
    }

    internal static byte[] SampleMap()
    {
        var map = new Builder();

        map.Text(0, "{\n\"classname\" \"worldspawn\"\n}\n{\n\"classname\" \"light\"\n}\0");
        map.Bytes(1, new byte[20 * 4]);
        map.Bytes(43, Encoding.UTF8.GetBytes("concrete/floor01\0"));
        map.Bytes(44, BitConverter.GetBytes(0));

        var texData = new byte[32];
        Write(texData, 12, 0);
        Write(texData, 16, 512);
        Write(texData, 20, 512);
        map.Bytes(2, texData);

        var cubemap = new byte[16];
        Write(cubemap, 0, 64);
        Write(cubemap, 4, 128);
        Write(cubemap, 8, 16);
        cubemap[12] = 2;
        map.Bytes(42, cubemap);

        map.Bytes(35, StaticPropLump(version: 5, stride: 60));

        return map.Build();
    }

    /// <summary>
    /// The static prop game lump, at a given version and record size.
    ///
    /// Both versions of the record are written by the same code with the same
    /// values, because the property under test is that they read back the same:
    /// version 10 padded the record, and everything before the padding stayed
    /// where it was.
    /// </summary>
    private static byte[] StaticPropLump(int version, int stride)
    {
        var lump = new List<byte>();

        void Name(string text)
        {
            var padded = new byte[128];
            Encoding.UTF8.GetBytes(text).CopyTo(padded, 0);
            lump.AddRange(padded);
        }

        lump.AddRange(BitConverter.GetBytes(2));      // dictionary entries
        Name("models/props_junk/wood_crate001a.mdl");
        Name("models/props_c17/oildrum001.mdl");

        // The leaf array, which this reader steps over. Three entries, so a
        // reader that skipped the wrong number of bytes lands mid-record and
        // produces nonsense rather than a clean failure.
        lump.AddRange(BitConverter.GetBytes(3));
        lump.AddRange(BitConverter.GetBytes((ushort)11));
        lump.AddRange(BitConverter.GetBytes((ushort)12));
        lump.AddRange(BitConverter.GetBytes((ushort)13));

        lump.AddRange(BitConverter.GetBytes(2));      // props

        Prop(0, 128f, -64f, 32.5f, 0f, 270f, 0f, skin: 3, flags: version >= 10 ? 0x140 : 0x11, leaf: 7, leaves: 4, solid: 6);
        Prop(1, -256f, 512f, 8f, 15f, 90f, 45f, skin: 1, flags: version >= 10 ? 0x40 : 0x01, leaf: 2, leaves: 1, solid: 0);

        void Prop(int type, float x, float y, float z, float pitch, float yaw, float roll,
                  int skin, int flags, int leaf, int leaves, byte solid)
        {
            var record = new byte[stride];

            Write(record, 0, x); Write(record, 4, y); Write(record, 8, z);
            Write(record, 12, pitch); Write(record, 16, yaw); Write(record, 20, roll);
            WriteShort(record, 24, (short)type);
            WriteShort(record, 26, (short)leaf);
            WriteShort(record, 28, (short)leaves);
            record[30] = solid;

            if (version >= 10)
                Write(record, 64, flags);
            else
                record[31] = (byte)flags;

            Write(record, 32, skin);
            Write(record, 36, 400f);                  // fade min
            Write(record, 40, 900f);                  // fade max
            Write(record, 44, x); Write(record, 48, y); Write(record, 52, z);

            lump.AddRange(record);
        }

        // The game lump's directory, with the sub-lump's offset left for the
        // builder to fix up: the offsets in it are into the file, not into the
        // lump, which is the trap this format is known for.
        var directory = new List<byte>();
        directory.AddRange(BitConverter.GetBytes(1));
        directory.AddRange(BitConverter.GetBytes(0x73707270));   // 'sprp'
        directory.AddRange(BitConverter.GetBytes((ushort)0));
        directory.AddRange(BitConverter.GetBytes((ushort)version));
        directory.AddRange(BitConverter.GetBytes(Builder.GameLumpOffsetMarker));
        directory.AddRange(BitConverter.GetBytes(lump.Count));

        directory.AddRange(lump);
        return directory.ToArray();
    }

    /// <summary>
    /// A BSP file, laid out the way the format wants: a header of sixty-four
    /// lump pointers, then the lumps, four-byte aligned.
    /// </summary>
    private sealed class Builder
    {
        /// <summary>
        /// Stands in for a game sub-lump's file offset until the file has one.
        ///
        /// The offset cannot be known while the sub-lump is being built, because
        /// it is an offset into the finished file. Anything writing this format
        /// for real has the same problem and solves it the same way.
        /// </summary>
        public const int GameLumpOffsetMarker = unchecked((int)0xDEADBEEF);

        private const int HeaderSize = 8 + 64 * 16 + 4;

        private readonly byte[]?[] _lumps = new byte[64][];
        private readonly int[] _versions = new int[64];

        public void Bytes(int lump, byte[] bytes) => _lumps[lump] = bytes;

        public void Text(int lump, string text) => _lumps[lump] = Encoding.UTF8.GetBytes(text);

        public void Version(int lump, int version) => _versions[lump] = version;

        public byte[] Build()
        {
            var file = new List<byte>();

            file.AddRange("VBSP"u8.ToArray());
            file.AddRange(BitConverter.GetBytes(21));
            file.AddRange(new byte[64 * 16 + 4]);

            var placed = new (int Offset, int Length)[64];

            for (int i = 0; i < 64; i++)
            {
                var bytes = _lumps[i];

                if (bytes is null || bytes.Length == 0)
                    continue;

                placed[i] = (file.Count, bytes.Length);
                file.AddRange(bytes);

                while (file.Count % 4 != 0)
                    file.Add(0);
            }

            var result = file.ToArray();

            for (int i = 0; i < 64; i++)
            {
                int at = 8 + i * 16;
                BitConverter.GetBytes(placed[i].Offset).CopyTo(result, at);
                BitConverter.GetBytes(placed[i].Length).CopyTo(result, at + 4);
                BitConverter.GetBytes(_versions[i]).CopyTo(result, at + 8);
            }

            BitConverter.GetBytes(4242).CopyTo(result, HeaderSize - 4);

            // The game lump's sub-lump offsets are file offsets. Now that the
            // lump has a home, the placeholder can become a real one.
            if (placed[35].Length > 0)
            {
                var marker = BitConverter.GetBytes(GameLumpOffsetMarker);

                for (int at = placed[35].Offset; at + 4 <= placed[35].Offset + placed[35].Length; at += 4)
                {
                    if (result[at] != marker[0] || result[at + 1] != marker[1]
                        || result[at + 2] != marker[2] || result[at + 3] != marker[3])
                    {
                        continue;
                    }

                    // The directory is one entry with its payload behind it.
                    BitConverter.GetBytes(placed[35].Offset + 20).CopyTo(result, at);
                    break;
                }
            }

            return result;
        }
    }

    private static BspReport Read(Builder map, string name) => Read(map.Build(), name);

    /// <summary>Reads a map from a temporary file, and does not leave it behind.</summary>
    private static BspReport Read(byte[] bytes, string name)
    {
        string path = Write(bytes);

        try
        {
            return BspReport.Read(path, name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A map on disk, because the reader takes a path.
    ///
    /// It reads by seeking rather than by holding the file, which is the whole
    /// reason it takes a path and not a stream: a map is megabytes and the
    /// interesting part of it is kilobytes.
    /// </summary>
    private static string Write(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(),
            $"sourcesharp-bspreport-{Guid.NewGuid():N}.bsp");

        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void Write(byte[] bytes, int at, float value)
        => BitConverter.GetBytes(value).CopyTo(bytes, at);

    private static void Write(byte[] bytes, int at, int value)
        => BitConverter.GetBytes(value).CopyTo(bytes, at);

    private static void WriteShort(byte[] bytes, int at, short value)
        => BitConverter.GetBytes(value).CopyTo(bytes, at);
}
