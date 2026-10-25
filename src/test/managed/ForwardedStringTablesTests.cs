using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The id-addressed string tables over the container's forwarders
/// (plan_server_native_v2 T3). These are stringtable_abi_test's facts, which
/// held CShellStringTables (shell_stringtables.h) to the same rules, carried
/// to <see cref="ForwardedStringTables"/> over a fake engine container.
/// </summary>
public sealed unsafe class ForwardedStringTablesTests : System.IDisposable
{
    private const int Full = 0xFFFF;

    private sealed class Table
    {
        public int Id;
        public int Max;
        public nint Name;
        public readonly List<nint> Strings = new();
    }

    // The fake engine (test state; the analyzer's no-statics rule is the runtime's).
    private static readonly List<Table> s_Tables = new();
    private static bool s_RefuseCreate;
    private static readonly List<byte> s_AddServerFlags = new();

    private static string Text(byte* p) => Marshal.PtrToStringUTF8((nint)p)!;
    private static void* Ptr(Table t) => (void*)(t.Id + 1);
    private static Table? Of(void* p) => s_Tables.FirstOrDefault(t => t.Id + 1 == (nint)p);

    [UnmanagedCallersOnly] private static void* FindTable(void* self, byte* name)
    {
        string n = Text(name);
        Table? t = s_Tables.FirstOrDefault(x => Marshal.PtrToStringUTF8(x.Name) == n);
        return t is null ? null : Ptr(t);
    }

    [UnmanagedCallersOnly] private static void* CreateStringTable(void* self, byte* name, int max, int fixedSize, int bits)
    {
        if (s_RefuseCreate)
            return null;
        var t = new Table { Id = s_Tables.Count, Max = max, Name = Marshal.StringToCoTaskMemUTF8(Text(name)) };
        s_Tables.Add(t);
        return Ptr(t);
    }

    [UnmanagedCallersOnly] private static void* GetTable(void* self, int id)
        => id >= 0 && id < s_Tables.Count ? Ptr(s_Tables[id]) : null;

    [UnmanagedCallersOnly] private static byte* GetTableName(void* t) => (byte*)Of(t)!.Name;
    [UnmanagedCallersOnly] private static int GetTableId(void* t) => Of(t)!.Id;
    [UnmanagedCallersOnly] private static int GetNumStrings(void* t) => Of(t)!.Strings.Count;

    [UnmanagedCallersOnly] private static int AddString(void* t, byte isServer, byte* value, int length, void* userData)
    {
        s_AddServerFlags.Add(isServer);
        Table table = Of(t)!;
        string v = Text(value);
        int at = table.Strings.FindIndex(s => Marshal.PtrToStringUTF8(s) == v);
        if (at >= 0)
            return at;
        if (table.Strings.Count >= table.Max)
            return Full;
        table.Strings.Add(Marshal.StringToCoTaskMemUTF8(v));
        return table.Strings.Count - 1;
    }

    [UnmanagedCallersOnly] private static byte* GetString(void* t, int i) => (byte*)Of(t)!.Strings[i];

    [UnmanagedCallersOnly] private static int FindStringIndex(void* t, byte* value)
    {
        string v = Text(value);
        int at = Of(t)!.Strings.FindIndex(s => Marshal.PtrToStringUTF8(s) == v);
        return at < 0 ? Full : at;
    }

    private readonly FakeShimRoot _root = new();
    private readonly nint* _self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));
    private readonly RecordingLog _log = new();

    private string Warned => string.Concat(_log.Lines.Where(l => l.Warning).Select(l => l.Text));

    public ForwardedStringTablesTests()
    {
        s_Tables.Clear();
        s_AddServerFlags.Clear();
        s_RefuseCreate = false;
    }

    public void Dispose()
    {
        NativeMemory.Free(_self);
        _root.Dispose();
    }

    private void Serve(string version)
    {
        _root.Interface(version, _self).Forwarders(out NetworkStringTableContainerFwd* c);
        c->FindTable = &FindTable;
        c->CreateStringTable = &CreateStringTable;
        c->GetTable = &GetTable;
        _root.Forwarders(out NetworkStringTableFwd* t);
        t->GetTableName = &GetTableName;
        t->GetTableId = &GetTableId;
        t->GetNumStrings = &GetNumStrings;
        t->AddString = &AddString;
        t->GetString = &GetString;
        t->FindStringIndex = &FindStringIndex;
    }

    private ForwardedStringTables Server(bool serve = true)
    {
        if (serve)
            Serve("VEngineServerStringTable001");
        var side = TestSides.Create(Side.Server);
        side.Attachment.Initialize();
        Assert.True(side.Attachment.AttachShims(_root.Bind(), out string why), why);
        return ForwardedStringTables.Acquire(side, _log);
    }

    private ForwardedStringTables Client()
    {
        Serve("VEngineClientStringTable001");
        var side = TestSides.Create(Side.Client);
        side.Attachment.Initialize();
        Assert.True(side.Attachment.AttachShims(_root.Bind(), out string why), why);
        return ForwardedStringTables.Acquire(side, _log);
    }

    [Fact]
    public void ATableIsCreatedFilledAndReadBack()
    {
        var abi = Server();
        int id = abi.Create("SourceSharpManaged", 32);
        Assert.True(id >= 0);
        Assert.Equal(0, abi.AddString(id, "abi 208"));
        Assert.Equal("abi 208", abi.GetString(id, 0));
        Assert.Equal(1, abi.GetNumStrings(id));
    }

    [Fact]
    public void TheIdIsTheEnginesOwnTableId()
    {
        var abi = Server();
        int first = abi.Create("First", 8), second = abi.Create("Second", 8);
        Assert.NotEqual(first, second);
        Assert.Equal(first, abi.Find("First"));
        Assert.Equal(second, abi.Find("Second"));
    }

    [Fact]
    public void CreatingATakenNameAnswersTheExistingTableAndSaysSo()
    {
        var abi = Server();
        int first = abi.Create("Taken", 8);
        Assert.Equal(first, abi.Create("Taken", 16));
        Assert.Single(s_Tables);
        Assert.Contains("'Taken' already exists", Warned);
    }

    [Fact]
    public void AStaleTableIdIsRefusedRatherThanIndexedInto()
    {
        var abi = Server();
        Assert.Equal(-1, abi.AddString(42, "x"));
        Assert.Equal(-1, abi.GetNumStrings(42));
        Assert.Null(abi.GetString(42, 0));
        Assert.Contains("ask for it again by name", Warned);
    }

    [Fact]
    public void TheInvalidTableSentinelIsNotResolved()
    {
        var abi = Server();
        abi.Create("Zero", 4);
        Assert.Equal(-1, abi.GetNumStrings(-1));
    }

    [Fact]
    public void AMissingStringIsMinusOneAndNotSixtyFiveThousand()
    {
        var abi = Server();
        int id = abi.Create("T", 4);
        Assert.Equal(-1, abi.FindStringIndex(id, "absent"));
    }

    [Fact]
    public void AFullTableRefuses()
    {
        var abi = Server();
        int id = abi.Create("Small", 1);
        Assert.Equal(0, abi.AddString(id, "a"));
        Assert.Equal(-1, abi.AddString(id, "b"));
        Assert.Contains("'Small' is full", Warned);
    }

    [Fact]
    public void TheSameStringTwiceIsOneEntryAtOneIndex()
    {
        var abi = Server();
        int id = abi.Create("T", 4);
        Assert.Equal(abi.AddString(id, "a"), abi.AddString(id, "a"));
        Assert.Equal(1, abi.GetNumStrings(id));
    }

    [Fact]
    public void AnIndexPastTheEndIsNull()
    {
        var abi = Server();
        int id = abi.Create("T", 4);
        abi.AddString(id, "a");
        Assert.Null(abi.GetString(id, 1));
        Assert.Null(abi.GetString(id, -1));
    }

    [Fact]
    public void AnEntryThatDoesNotFitIsRefusedAndOneThatExactlyFitsIsNot()
    {
        var abi = Server();
        int id = abi.Create("T", 4);
        string fits = new('x', ForwardedStringTables.MaxEntryBytes - 1);
        string over = new('y', ForwardedStringTables.MaxEntryBytes);
        abi.AddString(id, fits);
        abi.AddString(id, over);
        Assert.Equal(fits, abi.GetString(id, 0));
        Assert.Null(abi.GetString(id, 1));
        Assert.Contains("was NOT truncated", Warned);
    }

    [Fact]
    public void AnEmptyEntryIsEmptyAndNotARefusal()
    {
        var abi = Server();
        int id = abi.Create("T", 4);
        abi.AddString(id, "");
        Assert.Equal("", abi.GetString(id, 0));
    }

    [Fact]
    public void AnAbsentTableIsMinusOneAndAnEmptyOneCountsZero()
    {
        var abi = Server();
        int id = abi.Create("T", 4);
        Assert.Equal(0, abi.GetNumStrings(id));
        Assert.Equal(-1, abi.Find("Absent"));
        Assert.Empty(Warned);   // finding an absent table is silent
    }

    [Fact]
    public void ANamelessTableAndOneWithNoRoomAreRefused()
    {
        var abi = Server();
        Assert.Equal(-1, abi.Create("", 4));
        Assert.Equal(-1, abi.Create("NoRoom", 0));
        Assert.Empty(s_Tables);
    }

    [Fact]
    public void AnEngineRefusalToCreateIsMinusOne()
    {
        var abi = Server();
        s_RefuseCreate = true;
        Assert.Equal(-1, abi.Create("Late", 4));
        Assert.Contains("the engine refused to create it", Warned);
    }

    [Fact]
    public void WithNoContainerEveryOperationIsRefused()
    {
        var abi = Server(serve: false);
        Assert.Equal(-1, abi.Create("T", 4));
        Assert.Equal(-1, abi.Find("T"));
        Assert.Equal(-1, abi.AddString(0, "a"));
        Assert.Equal(-1, abi.FindStringIndex(0, "a"));
        Assert.Null(abi.GetString(0, 0));
        Assert.Equal(-1, abi.GetNumStrings(0));
        Assert.Contains("no VEngineServerStringTable001", Warned);
    }

    [Fact]
    public void EveryAddIsAServerAdd()
    {
        var abi = Server();
        int id = abi.Create("T", 4);
        abi.AddString(id, "a");
        abi.AddString(id, "b");
        Assert.Equal(new byte[] { 1, 1 }, s_AddServerFlags.ToArray());
    }

    [Fact]
    public void TheClientReadsButNeverCreatesOrAdds()
    {
        var abi = Client();
        s_Tables.Add(new Table { Id = 0, Max = 4, Name = Marshal.StringToCoTaskMemUTF8("Shared") });
        s_Tables[0].Strings.Add(Marshal.StringToCoTaskMemUTF8("a"));

        Assert.Equal(0, abi.Find("Shared"));
        Assert.Equal("a", abi.GetString(0, 0));
        Assert.Equal(0, abi.FindStringIndex(0, "a"));
        Assert.Equal(-1, abi.Create("New", 4));
        Assert.Equal(-1, abi.AddString(0, "b"));
        Assert.Single(s_Tables);
        Assert.Empty(s_AddServerFlags);
    }

    // EnginePrecache's materials and particle systems, through these tables
    // since 2026-10-19, where two server slots made the same AddString.
    private IEnginePrecache Precache(Side side)
    {
        Serve(side == Side.Server ? "VEngineServerStringTable001" : "VEngineClientStringTable001");
        var api = TestSides.Create(side);
        api.Attachment.Initialize();
        string why;
        Assert.True(api.Attachment.AttachShims(_root.Bind(), out why), why);
        return SideBindings.Of<IEnginePrecache>(api);
    }

    private static string[] Strings(string table)
        => s_Tables.Single(t => Marshal.PtrToStringUTF8(t.Name) == table).Strings.Select(x => Marshal.PtrToStringUTF8(x)!).ToArray();

    [Fact]
    public void AMaterialAndAParticleSystemGoInTheirOwnTables()
    {
        var precache = Precache(Side.Server);
        Server(serve: false).Create("Materials", 1024);
        Server(serve: false).Create("ParticleEffectNames", 8192);

        precache.PrecacheMaterial("debug/debugempty");
        precache.PrecacheParticleSystem("error");
        precache.PrecacheMaterial("");

        Assert.Equal(new[] { "debug/debugempty" }, Strings("Materials"));
        Assert.Equal(new[] { "error" }, Strings("ParticleEffectNames"));
        Assert.Equal(new byte[] { 1, 1 }, s_AddServerFlags);
    }

    [Fact]
    public void TheClientPrecachesNothing()
    {
        Server().Create("Materials", 1024);
        var precache = Precache(Side.Client);

        precache.PrecacheMaterial("debug/debugempty");

        Assert.Empty(Strings("Materials"));
    }
}
