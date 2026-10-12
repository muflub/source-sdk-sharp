using System.Reflection;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The reload carrier holds bytes (<c>plan_abi_hotreload</c> D6): every record a
/// hot reload carries goes through <see cref="SavedEntityCodec"/> into
/// <see cref="ReloadStore"/> and back. These are the codec's reload sections and
/// the carrier's rules, one behaviour each.
/// </summary>
public class ReloadCarrierTests
{
    // This class's own side state (statics to zero): xUnit builds one
    // instance per fact, so no fact sees another's.
    private readonly ReloadStore _reload = new(Side.Server);
    private readonly ReloadStore _clientReload = new(Side.Client);

    //-- the I/O section ------------------------------------------------------

    private static SavedIo SomeIo() => new(
        new[]
        {
            new SavedIoEvent(1.5f, "door_*", 0x0001_0002u, "Open", "fast", 0x0003_0004u, 0x0005_0006u, 7),
            new SavedIoEvent(0f, "", 0x0008_0009u, "Kill", null, uint.MaxValue, uint.MaxValue, -1),
        },
        new[]
        {
            new SavedConnection(0x000A_000Bu, "OnTrigger", "lamp", "TurnOn", "1", 0.25f, -1, 12),
            new SavedConnection(0x000C_000Du, "OnBreak", "!activator", "Kill", null, 0f, 1, 13),
        });

    [Fact]
    public void TheIoSectionCarriesEveryEventField()
    {
        var io = SomeIo();

        Assert.True(SavedEntityCodec.TryReadIo(SavedEntityCodec.EncodeIo(io), out var back, out string? why), why);

        Assert.Equal(io.Events, back.Events);
    }

    [Fact]
    public void TheIoSectionCarriesEveryConnectionField()
    {
        var io = SomeIo();

        Assert.True(SavedEntityCodec.TryReadIo(SavedEntityCodec.EncodeIo(io), out var back, out string? why), why);

        Assert.Equal(io.Connections, back.Connections);
    }

    /// <summary>
    /// A null parameter and an empty one are different connections: a null
    /// parameter passes the output's own value, an empty one passes nothing.
    /// </summary>
    [Fact]
    public void TheIoSectionKeepsANullParameterNull()
    {
        var io = new SavedIo(Array.Empty<SavedIoEvent>(), new[]
        {
            new SavedConnection(1u, "OnA", "t", "In", null, 0f, -1, 1),
            new SavedConnection(2u, "OnB", "t", "In", "", 0f, -1, 2),
        });

        Assert.True(SavedEntityCodec.TryReadIo(SavedEntityCodec.EncodeIo(io), out var back, out string? why), why);

        Assert.Null(back.Connections[0].Parameter);
        Assert.Equal("", back.Connections[1].Parameter);
    }

    //-- the shell-less section -----------------------------------------------

    /// <summary>
    /// EVERY PUBLIC PROPERTY, found by reflection, so a member added to
    /// <see cref="ShellLessEntity"/> and not to the codec fails here rather than
    /// coming back from a reload at its default.
    /// </summary>
    [Fact]
    public void TheShellLessSectionCarriesEveryProperty()
    {
        var e = new ShellLessEntity(new EntityHandle(0x0007_8001u), "logic_relay")
        {
            Name = "relay_a",
            SpawnFlags = 3,
            Health = 77,
            Origin = new Vector(1f, 2f, 3f),
            Angles = new Vector(4f, 5f, 6f),
            Model = "models/x.mdl",
            SpawnOrigin = new Vector(7f, 8f, 9f),
            SpawnAngles = new Vector(10f, 11f, 12f),
            HasSpawned = true,
        };
        e.ApplyShellKey("parentname", "train");
        e.ApplyShellKey("parentattachment", "hook");

        var properties = typeof(ShellLessEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Every property holds a non-default value, so a field the codec drops
        // cannot pass by coming back at the default it already had.
        foreach (var p in properties)
        {
            object? value = p.GetValue(e);
            Assert.True(value is not null && !value.Equals(p.PropertyType.IsValueType
                            ? Activator.CreateInstance(p.PropertyType) : null),
                        $"the fixture sets {p.Name}");
        }

        Assert.True(SavedEntityCodec.TryReadShellLess(SavedEntityCodec.EncodeShellLess(new[] { e }),
                                                      out var back, out string? why), why);

        var b = Assert.Single(back);
        foreach (var p in properties)
            Assert.True(Equals(p.GetValue(e), p.GetValue(b)), $"{p.Name} comes back");

        Assert.True(properties.Length >= 13, $"the walk saw every property ({properties.Length})");
    }

    [Fact]
    public void TheShellLessSectionKeepsAnUnsetParentNull()
    {
        var e = new ShellLessEntity(new EntityHandle(0x0007_8002u), "logic_auto");

        Assert.True(SavedEntityCodec.TryReadShellLess(SavedEntityCodec.EncodeShellLess(new[] { e }),
                                                      out var back, out string? why), why);

        Assert.Null(Assert.Single(back).ParentName);
    }

    //-- the participant-record section ----------------------------------------

    private static Dictionary<string, ReloadRecord> SomeParts()
    {
        var think = new ReloadRecord("ThinkScheduler");
        think.Set("pending", 2);
        think.AddRow().Set("handle", 0x0001_0002).Set("context", "fade");
        think.AddRow().Set("handle", 0x0003_0004).Set("context", "");

        var pickups = new ReloadRecord("Pickups");
        pickups.Set("placed", true);

        return new Dictionary<string, ReloadRecord>(StringComparer.Ordinal)
        {
            ["ThinkScheduler"] = think,
            ["Pickups"] = pickups,
        };
    }

    [Fact]
    public void ThePartsSectionCarriesEveryScalar()
    {
        var parts = SomeParts();

        Assert.True(SavedEntityCodec.TryReadParts(SavedEntityCodec.EncodeParts(parts), out var back, out string? why), why);

        Assert.Equal(parts.Keys, back.Keys);
        foreach (var (key, record) in parts)
        {
            Assert.Equal(record.Owner, back[key].Owner);
            Assert.Equal(record.Scalars, back[key].Scalars);
        }
    }

    /// <summary>Rows in the order they were written: a schedule is a sequence.</summary>
    [Fact]
    public void ThePartsSectionCarriesEveryRowInOrder()
    {
        var parts = SomeParts();

        Assert.True(SavedEntityCodec.TryReadParts(SavedEntityCodec.EncodeParts(parts), out var back, out string? why), why);

        var rows = back["ThinkScheduler"].Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal(parts["ThinkScheduler"].Rows[0].Fields, rows[0].Fields);
        Assert.Equal(parts["ThinkScheduler"].Rows[1].Fields, rows[1].Fields);
    }

    //-- the entity section ----------------------------------------------------

    /// <summary>
    /// A reload carries what a <c>.sav</c> refuses: a long value is a reason to
    /// refuse a file, never a reason to lose an entity across a reload.
    /// </summary>
    [Fact]
    public void TheEntitySectionCarriesAValueTheSaveFormatRefuses()
    {
        var e = new SavedEntity(0x0001_0005u, "ss_note", "note");
        e.SaveVar("message", new string('x', SavedEntityCodec.MaxStringBytes + 1), 0f);

        Assert.Equal(-1, SavedEntityCodec.Write(new[] { e }, new byte[1 << 16], new RecordingLog()));

        byte[]? bytes = SavedEntityCodec.EncodeForReload(new[] { e }, new RecordingLog());
        Assert.NotNull(bytes);
        Assert.True(SavedEntityCodec.TryReadForReload(bytes, out var back, out string? why), why);

        Assert.Equal(e.Vars["message"].Value, Assert.Single(back).Vars["message"].Value);
    }

    //-- refusals ---------------------------------------------------------------

    [Fact]
    public void ASectionRefusesAnotherSectionsBytes()
    {
        byte[] io = SavedEntityCodec.EncodeIo(SomeIo());

        Assert.False(SavedEntityCodec.TryReadParts(io, out _, out string? why));
        Assert.Contains("magic", why);
    }

    [Fact]
    public void ASectionRefusesAFlippedByte()
    {
        byte[] bytes = SavedEntityCodec.EncodeParts(SomeParts());
        bytes[^1] ^= 0x20;

        Assert.False(SavedEntityCodec.TryReadParts(bytes, out _, out string? why));
        Assert.Contains("checksum", why);
    }

    //-- the carrier -------------------------------------------------------------

    /// <summary>
    /// Putting entities keeps what is already waiting and replaces only the
    /// same handle -- the rule the typed store had, kept across the bytes.
    /// </summary>
    [Fact]
    public void PuttingEntitiesReplacesOnlyTheSameHandle()
    {
        ReloadCarrier.Put(_reload, new SavedEntity(1u, "a", "first"));
        ReloadCarrier.Put(_reload, new SavedEntity(2u, "b", "other"));
        ReloadCarrier.Put(_reload, new SavedEntity(1u, "a", "second"));

        var taken = ReloadCarrier.Take(_reload);

        Assert.Equal(new[] { "second", "other" }, taken.Select(e => e.Name));
    }

    [Fact]
    public void TakingEntitiesEmptiesTheSection()
    {
        ReloadCarrier.Put(_clientReload, new SavedEntity(3u, "c", ""));

        Assert.Single(ReloadCarrier.Take(_clientReload));
        Assert.Empty(ReloadCarrier.Take(_clientReload));
    }

    [Fact]
    public void PeekingTheIoLeavesItWaiting()
    {
        ReloadCarrier.PutIo(_reload, SomeIo());

        Assert.Equal(4, ReloadCarrier.PeekIo(_reload).Count);
        Assert.Equal(4, ReloadCarrier.IoCount(_reload));
    }

    [Fact]
    public void ClearingASideDropsItsShellLessEntities()
    {
        ReloadCarrier.PutShellLess(_reload,
            new[] { new ShellLessEntity(new EntityHandle(0x0007_8003u), "logic_relay") });
        Assert.Equal(1, ReloadCarrier.ShellLessCount(_reload));

        _reload.Clear();

        Assert.Empty(ReloadCarrier.TakeShellLess(_reload));
    }

    /// <summary>
    /// A section that does not decode restores nothing, rather than a world
    /// rebuilt from misparsed bytes.
    /// </summary>
    [Fact]
    public void ASectionThatDoesNotDecodeRestoresNothing()
    {
        byte[] bytes = SavedEntityCodec.EncodeForReload(new[] { new SavedEntity(4u, "d", "") }, new RecordingLog())!;

        // INSIDE THE CLASSNAME, so the shape still parses and only the
        // checksum can tell: "d" becomes "e".
        int at = Array.IndexOf(bytes, (byte)'d', SavedEntityCodec.ChecksumFrom);
        Assert.True(at > 0, "the classname is in the bytes");
        bytes[at] = (byte)'e';
        _reload.Put(ReloadCarrier.EntitiesSection, bytes, 1);

        Assert.Empty(ReloadCarrier.Take(_reload));
    }

    //-- the pin this step removes ------------------------------------------------

    /// <summary>
    /// <see cref="ReloadStore"/> names no record type: none of its fields,
    /// parameters or return types, nor its nested types', is a
    /// <see cref="SavedEntity"/>, <see cref="SavedIo"/>,
    /// <see cref="ShellLessEntity"/> or <see cref="ReloadRecord"/> -- the pin
    /// that kept all four, and what they name, in SourceSharp.Abi.
    /// </summary>
    [Fact]
    public void TheStoreNamesNoRecordType()
    {
        var forbidden = new[] { typeof(SavedEntity), typeof(SavedIo), typeof(ShellLessEntity), typeof(ReloadRecord),
                                typeof(SavedIoEvent), typeof(SavedConnection) };
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                               | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var types = new List<Type> { typeof(ReloadStore) };
        types.AddRange(typeof(ReloadStore).GetNestedTypes(All));

        var named = new List<Type>();
        foreach (var t in types)
        {
            foreach (var f in t.GetFields(All))
                named.Add(f.FieldType);

            foreach (var m in t.GetMethods(All))
            {
                named.Add(m.ReturnType);
                named.AddRange(m.GetParameters().Select(p => p.ParameterType));
            }
        }

        static IEnumerable<Type> Expand(Type t)
        {
            yield return t;
            var inner = t.HasElementType ? new[] { t.GetElementType()! } : t.GetGenericArguments();
            foreach (var g in inner)
                foreach (var x in Expand(g))
                    yield return x;
        }

        var all = named.SelectMany(Expand).ToList();

        Assert.Contains(typeof(byte[]), named);
        Assert.Empty(all.Intersect(forbidden));
    }
}
