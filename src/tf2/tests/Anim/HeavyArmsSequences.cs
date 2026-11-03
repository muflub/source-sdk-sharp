using System.Buffers.Binary;
using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared.Bones;
using SourceSharp.Tests;

using Xunit;

namespace TF2.Tests.Anim;

/// <summary>
/// The heavy's arms as the shipped <c>c_heavy_arms.mdl</c> actually tags it:
/// the minigun's poses, read through the production first-match activity
/// lookup (<see cref="ModelDescriptor.LookupSequenceByActivity"/>), are the
/// indexes both sides of the crossing must answer — idle <c>m_idle</c>, the
/// looping fire pose <c>m_fire</c>, the held spin pose <c>m_spool_idle</c>,
/// and the wind-up/settle pair <c>m_spool_up</c>/<c>m_spool_down</c>. The
/// tags are the role-prefixed ones (<c>ACT_PRIMARY_*</c>,
/// <c>ACT_PRIMARY_ATTACK_STAND_*</c>), which is why the viewmodel's rule
/// translates the plain <c>ACT_VM_*</c> ask first, exactly like stock's
/// <c>TranslateViewmodelHandActivity</c>.
/// </summary>
public sealed class HeavyArmsSequences
{
    private const string Mdl = "models/weapons/c_models/c_heavy_arms.mdl";

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void The_idle_ask_answers_the_heavys_idle_pose()
    {
        Assert.Equal(20, Descriptor().LookupSequenceByActivity("ACT_PRIMARY_VM_IDLE"));
    }

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void The_fire_ask_answers_the_looping_fire_pose()
    {
        Assert.Equal(21, Descriptor().LookupSequenceByActivity("ACT_PRIMARY_VM_PRIMARYATTACK"));
    }

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void The_secondary_ask_answers_the_held_spin_pose()
    {
        Assert.Equal(25, Descriptor().LookupSequenceByActivity("ACT_PRIMARY_VM_SECONDARYATTACK"));
    }

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void The_windup_and_settle_asks_answer_the_spool_up_and_spool_down_poses()
    {
        ModelDescriptor descriptor = Descriptor();
        Assert.Equal(22, descriptor.LookupSequenceByActivity("ACT_PRIMARY_ATTACK_STAND_PREFIRE"));
        Assert.Equal(23, descriptor.LookupSequenceByActivity("ACT_PRIMARY_ATTACK_STAND_POSTFIRE"));
    }

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void An_untranslated_ask_answers_nothing_so_the_role_rule_is_load_bearing()
    {
        // The mdl tags the minigun's poses ACT_PRIMARY_* only; a plain ACT_VM_*
        // ask (a side that skipped the rule's translate) must answer none, not
        // a different pose — the same -1 on both sides is what keeps the
        // predicted sequence index a pure function of the model.
        Assert.Equal(-1, Descriptor().LookupSequenceByActivity("ACT_VM_PRIMARYATTACK"));
    }

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void The_miniguns_poses_are_plain_single_cell_anims()
    {
        StudioHdr hdr = LoadHeavyArms();

        // No DELTA (accumulate onto the caller's pose), no POST (blend after
        // the main pose), no AUTOPLAY, and no autolayers: each of the six
        // answers one pose cell, which is why a plain single-cell decode is
        // what both sides need, and why the gun's lift rides the sequence's
        // own frames rather than a layer.
        for (int i = 20; i <= 25; i++)
        {
            MstudioSeqDesc d = hdr.SeqDesc(i);
            Assert.Equal(0, d.Flags & (StudioConstants.STUDIO_DELTA
                                       | StudioConstants.STUDIO_POST
                                       | StudioConstants.STUDIO_AUTOPLAY));
            Assert.Equal(0, d.NumAutoLayers);
            Assert.Equal(1, d.NumBlends);
            Assert.Equal(1, d.GroupSize0);
            Assert.Equal(1, d.GroupSize1);
        }
    }

    private static ModelDescriptor Descriptor()
    {
        StudioHdr hdr = LoadHeavyArms();

        var sequences = new List<ModelSequence>(hdr.NumSequences);
        for (int i = 0; i < hdr.NumSequences; i++)
        {
            MstudioSeqDesc d = hdr.SeqDesc(i);
            sequences.Add(new ModelSequence(i, d.Label, d.ActivityName, d.Flags, 0f,
                                            Array.Empty<ModelSequenceEvent>(), d.ActWeight));
        }

        return ModelDescriptor.Create(0, Array.Empty<string>(), Array.Empty<Vector>(), sequences);
    }

    private static StudioHdr LoadHeavyArms()
    {
        string content = RepoLayout.Repo("bin", RepoLayout.Config, "content", "tf");
        byte[] bytes = ReadModel(content, Mdl)
                       ?? throw new InvalidOperationException($"no {Mdl} under {content}");

        // The shipped arms mdl chains to its includes; resolve them through the
        // same GAME-path lookup so TryCreate sees what the engine's filesystem
        // would hand it.
        if (!StudioHdr.TryCreate(bytes,
                name => ReadModel(content,
                    name.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) ? name : name + ".mdl"),
                out StudioHdr? hdr) || hdr is null)
            throw new InvalidOperationException($"{Mdl}: StudioHdr refused it");

        return hdr;
    }

    // GAME-path lookup, the way gameinfo mounts it: the loose directory
    // second, the archive packs first (models are packed; the loose tf/models
    // holds only props_movies). A part is a file beside the directory file.
    private static byte[]? ReadModel(string contentDir, string path)
    {
        string loose = Path.Combine(contentDir, path.Replace('\\', '/'));
        foreach (string pack in new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" })
        {
            string dir = Path.Combine(contentDir, pack);
            if (File.Exists(dir) && FromDirVpk(dir, path) is { } packed)
                return packed;
        }
        return File.Exists(loose) ? File.ReadAllBytes(loose) : null;
    }

    private static byte[]? FromDirVpk(string dirVpk, string path)
    {
        byte[] image = File.ReadAllBytes(dirVpk);
        if (image.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(image) != 0x55aa1234)
            return null;
        int version = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(4));
        int dirSize = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(8));
        int headerSize = version == 1 ? 12 : version == 2 ? 28 : 0;
        if (headerSize == 0 || dirSize < 0 || headerSize + (long)dirSize > image.Length)
            return null;

        ReadOnlySpan<byte> directory = image.AsSpan(headerSize, dirSize);
        string want = path.Replace('\\', '/').ToLowerInvariant();
        string baseName = dirVpk.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase)
            ? dirVpk[..^"_dir.vpk".Length]
            : dirVpk[..^".vpk".Length];

        int off = 0;
        while (ReadString(directory, ref off) is { Length: > 0 } extension)
        {
            while (ReadString(directory, ref off) is { Length: > 0 } folder)
            {
                while (ReadString(directory, ref off) is { Length: > 0 } name)
                {
                    string leaf = extension == " " ? name : name + "." + extension;
                    string full = folder == " " ? leaf : folder + "/" + leaf;

                    Need(directory, off, 6);
                    int preloadSize = BinaryPrimitives.ReadUInt16LittleEndian(directory[(off + 4)..]);
                    off += 6;

                    var parts = new List<(int Archive, uint Offset, uint Length)>();
                    while (true)
                    {
                        Need(directory, off, 2);
                        ushort archiveIndex = BinaryPrimitives.ReadUInt16LittleEndian(directory[off..]);
                        if (archiveIndex == 0xffff)
                        {
                            off += 2;
                            break;
                        }
                        Need(directory, off, 10);
                        parts.Add((archiveIndex,
                                   BinaryPrimitives.ReadUInt32LittleEndian(directory[(off + 2)..]),
                                   BinaryPrimitives.ReadUInt32LittleEndian(directory[(off + 6)..])));
                        off += 10;
                    }

                    Need(directory, off, preloadSize);
                    byte[] preload = directory.Slice(off, preloadSize).ToArray();
                    off += preloadSize;

                    if (full.ToLowerInvariant() != want)
                        continue;

                    long total = preload.Length;
                    foreach ((_, _, uint length) in parts)
                        total += length;
                    var bytes = new byte[total];
                    preload.CopyTo(bytes, 0);
                    int at = preload.Length;
                    foreach ((int archiveIndex, uint offset, uint length) in parts)
                    {
                        string file = archiveIndex == 0x7fff
                            ? dirVpk
                            : $"{baseName}_{archiveIndex.ToString("D3", CultureInfo.InvariantCulture)}.vpk";
                        long baseOffset = archiveIndex == 0x7fff ? headerSize + (long)dirSize : 0;
                        using var stream = File.OpenRead(file);
                        stream.Seek(baseOffset + offset, SeekOrigin.Begin);
                        stream.ReadExactly(bytes, at, (int)length);
                        at += (int)length;
                    }
                    return bytes;
                }
            }
        }
        return null;
    }

    private static string ReadString(ReadOnlySpan<byte> directory, ref int offset)
    {
        int end = directory[offset..].IndexOf((byte)0);
        if (end < 0)
            throw new InvalidDataException("a name runs to the end of the directory");
        string value = Encoding.Latin1.GetString(directory.Slice(offset, end));
        offset += end + 1;
        return value;
    }

    private static void Need(ReadOnlySpan<byte> directory, int offset, int bytes)
    {
        if (offset + bytes > directory.Length)
            throw new InvalidDataException($"entry at {offset} needs more bytes than the directory holds");
    }
}
