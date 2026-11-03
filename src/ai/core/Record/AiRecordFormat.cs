using System.Buffers.Binary;
using System.Text;

namespace SourceSharp.Ai;

/// <summary>
/// The record types of an <c>.airec</c> file (plan_hl2mp_ai §2.6). Each record
/// is <c>u8 type, u32 payload length, payload</c>, little-endian (memory
/// length-prefixed-buffers), after the file's magic and format version.
/// </summary>
public enum AiRecordType : byte
{
    Header = 1,
    BotAdded = 2,
    BotRemoved = 3,
    Event = 4,
    Snapshot = 5,
    Intent = 6,
    UserCmd = 7,
    World = 8,
}

/// <summary>
/// What the brains of a recording knew of the world (format 2's World record):
/// the map's knowledge (§5.0), the tick, the skill curves' text (§10) and the
/// nav graph's hash. A replay rebuilds the brains' world from it and the
/// graph the hash names, so a live recording replays as the game decided.
/// </summary>
public sealed record AiRecordedWorld(MapKnowledge Map, float TickSeconds, string CurvesJson, ulong NavGraphHash);

/// <summary>
/// What a recording was made under: the match seed, the map, the nav graph's
/// hash, the skill parameters' hash, and the session's sizes.
/// </summary>
public readonly record struct AiRecordingHeader(
    ulong MatchSeed,
    string Map,
    ulong NavGraphHash,
    ulong SkillParamsHash,
    int Slots,
    int SightedCapacity);

/// <summary>The file's framing and every payload's encoding. Pure functions; the stream is the caller's.</summary>
public static class AiRecordFormat
{
    /// <summary>The first eight bytes of every <c>.airec</c> file.</summary>
    public static ReadOnlySpan<byte> Magic => "SSAIREC\0"u8;

    /// <summary>The format this build writes. It reads this one and 1 (format 2 added the World record).</summary>
    public const ushort Version = 2;

    /// <summary>The largest payload a reader accepts; anything bigger is a corrupt length.</summary>
    public const int MaxPayload = 1 << 24;

    public static void WriteFileHeader(Stream s)
    {
        s.Write(Magic);
        Span<byte> v = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(v, Version);
        s.Write(v);
    }

    /// <summary>Reads and checks the magic and version; throws naming what is wrong.</summary>
    public static void ReadFileHeader(Stream s)
    {
        Span<byte> head = stackalloc byte[10];
        if (s.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) != head.Length)
            throw new InvalidDataException("not an .airec file: shorter than its header");
        if (!head[..8].SequenceEqual(Magic))
            throw new InvalidDataException("not an .airec file: the magic is wrong");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(head[8..]);
        if (version is < 1 or > Version)
            throw new InvalidDataException($".airec format {version} is not one this build reads (1..{Version})");
    }

    /// <summary>Writes one record: its type, its length, its payload.</summary>
    public static void WriteRecord(Stream s, AiRecordType type, ReadOnlySpan<byte> payload)
    {
        Span<byte> head = stackalloc byte[5];
        head[0] = (byte)type;
        BinaryPrimitives.WriteUInt32LittleEndian(head[1..], (uint)payload.Length);
        s.Write(head);
        s.Write(payload);
    }

    /// <summary>The next record, or false at a clean end; a torn or unknown record throws.</summary>
    public static bool TryReadRecord(Stream s, out AiRecordType type, out byte[] payload)
    {
        Span<byte> head = stackalloc byte[5];
        int got = s.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        if (got == 0)
        {
            type = default;
            payload = [];
            return false;
        }

        if (got != head.Length)
            throw new InvalidDataException("a record's header is cut short");

        type = (AiRecordType)head[0];
        if (!Enum.IsDefined(type))
            throw new InvalidDataException($"record type {head[0]} is not one format {Version} has");

        uint length = BinaryPrimitives.ReadUInt32LittleEndian(head[1..]);
        if (length > MaxPayload)
            throw new InvalidDataException($"a {type} record claims {length} bytes, more than {MaxPayload}");

        payload = new byte[length];
        if (s.ReadAtLeast(payload, payload.Length, throwOnEndOfStream: false) != payload.Length)
            throw new InvalidDataException($"a {type} record is cut short");
        return true;
    }

    // ---- payloads ----------------------------------------------------------

    public static void Write(BinaryWriter w, in AiRecordingHeader h)
    {
        w.Write(h.MatchSeed);
        byte[] map = Encoding.UTF8.GetBytes(h.Map);
        w.Write((ushort)map.Length);
        w.Write(map);
        w.Write(h.NavGraphHash);
        w.Write(h.SkillParamsHash);
        w.Write(h.Slots);
        w.Write(h.SightedCapacity);
    }

    public static void Write(BinaryWriter w, AiRecordedWorld world)
    {
        WriteString(w, world.Map.Map);
        w.Write(world.TickSeconds);
        w.Write(world.NavGraphHash);
        w.Write(world.Map.Items.Length);
        foreach (ItemSpawn i in world.Map.Items)
        {
            w.Write(i.Index);
            WriteString(w, i.ClassName);
            w.Write((byte)i.Kind);
            Write(w, i.Position);
            w.Write(i.RespawnSeconds);
            w.Write(i.WeaponKind);
        }

        w.Write(world.Map.Spawns.Length);
        foreach (AiVector s in world.Map.Spawns)
            Write(w, s);
        w.Write(world.Map.Weapons.All.Length);
        foreach (WeaponFacts f in world.Map.Weapons.All)
        {
            w.Write(f.Kind);
            WriteString(w, f.Name);
            w.Write(f.ClipSize);
            w.Write(f.CadenceSeconds);
            w.Write(f.ReloadSeconds);
            w.Write(f.ReloadAudible);
            w.Write(f.ShellByShell);
            w.Write(f.ProjectileSpeed);
        }

        byte[] curves = Encoding.UTF8.GetBytes(world.CurvesJson);
        w.Write(curves.Length);
        w.Write(curves);
    }

    public static AiRecordedWorld ReadWorld(BinaryReader r)
    {
        string map = ReadString(r);
        float tick = r.ReadSingle();
        ulong nav = r.ReadUInt64();
        var items = new ItemSpawn[Count(r)];
        for (int i = 0; i < items.Length; i++)
            items[i] = new ItemSpawn(r.ReadInt32(), ReadString(r), (ItemKind)r.ReadByte(), ReadVector(r), r.ReadSingle(), r.ReadInt32());
        var spawns = new AiVector[Count(r)];
        for (int i = 0; i < spawns.Length; i++)
            spawns[i] = ReadVector(r);
        var weapons = new WeaponFacts[Count(r)];
        for (int i = 0; i < weapons.Length; i++)
            weapons[i] = new WeaponFacts(r.ReadInt32(), ReadString(r), r.ReadInt32(), r.ReadSingle(), r.ReadSingle(), r.ReadBoolean(), r.ReadBoolean(), r.ReadSingle());
        string curves = Encoding.UTF8.GetString(r.ReadBytes(Count(r)));
        return new AiRecordedWorld(new MapKnowledge(map, [.. items], [.. spawns], new WeaponKnowledge(weapons)), tick, curves, nav);
    }

    private static int Count(BinaryReader r)
    {
        int n = r.ReadInt32();
        if (n < 0 || n > MaxPayload)
            throw new InvalidDataException($"a count of {n} is no count");
        return n;
    }

    private static void WriteString(BinaryWriter w, string s)
    {
        byte[] b = Encoding.UTF8.GetBytes(s);
        w.Write((ushort)b.Length);
        w.Write(b);
    }

    private static string ReadString(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));

    public static AiRecordingHeader ReadHeader(BinaryReader r)
    {
        ulong seed = r.ReadUInt64();
        string map = Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
        return new AiRecordingHeader(seed, map, r.ReadUInt64(), r.ReadUInt64(), r.ReadInt32(), r.ReadInt32());
    }

    public static void Write(BinaryWriter w, ActorId a)
    {
        w.Write(a.Slot);
        w.Write(a.Serial);
    }

    public static ActorId ReadActor(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32());

    public static void Write(BinaryWriter w, AiVector v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    public static AiVector ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    public static void Write(BinaryWriter w, AiAngles a)
    {
        w.Write(a.Pitch);
        w.Write(a.Yaw);
        w.Write(a.Roll);
    }

    public static AiAngles ReadAngles(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    public static void Write(BinaryWriter w, in AiEvent e)
    {
        w.Write((byte)e.Kind);
        Write(w, e.Bot);
        w.Write(e.Tick.Value);
        Write(w, e.Other);
        Write(w, e.Position);
        Write(w, e.Velocity);
        w.Write(e.Amount);
        w.Write(e.Code);
        w.Write(e.Code2);
    }

    public static AiEvent ReadEvent(BinaryReader r)
        => new((AiEventKind)r.ReadByte(), ReadActor(r), new Tick(r.ReadInt32()), ReadActor(r),
               ReadVector(r), ReadVector(r), r.ReadSingle(), r.ReadInt32(), r.ReadInt32());

    public static void Write(BinaryWriter w, in SelfState s)
    {
        Write(w, s.Origin);
        Write(w, s.Velocity);
        Write(w, s.Eye);
        w.Write(s.Health);
        w.Write(s.Armour);
        w.Write(s.ActiveWeapon);
        w.Write(s.Clip);
        w.Write(s.Reserve);
        w.Write((byte)s.Flags);
    }

    public static SelfState ReadSelf(BinaryReader r)
        => new(ReadVector(r), ReadVector(r), ReadAngles(r), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
               r.ReadInt32(), r.ReadInt32(), (SelfFlags)r.ReadByte());

    public static void Write(BinaryWriter w, in ActorFacts f)
    {
        Write(w, f.Id);
        Write(w, f.Origin);
        Write(w, f.Velocity);
        w.Write(f.WeaponSeen);
        w.Write((byte)f.Activity);
        w.Write(f.VisibleFraction);
    }

    public static ActorFacts ReadFacts(BinaryReader r)
        => new(ReadActor(r), ReadVector(r), ReadVector(r), r.ReadInt32(), (ActorActivity)r.ReadByte(), r.ReadSingle());

    /// <summary>A snapshot: its tick, then each present view in slot order.</summary>
    public static void Write(BinaryWriter w, AiSnapshot s)
    {
        w.Write(s.Tick.Value);
        int present = 0;
        for (int slot = 0; slot < s.Slots; slot++)
            if (s.ViewFor(slot) is not null)
                present++;
        w.Write((ushort)present);
        for (int slot = 0; slot < s.Slots; slot++)
        {
            if (s.ViewFor(slot) is not { } view)
                continue;
            Write(w, view.Bot);
            Write(w, view.Self);
            w.Write((ushort)view.SightedCount);
            foreach (ref readonly ActorFacts f in view.Sighted)
                Write(w, f);
        }
    }

    /// <summary>Reads a snapshot payload into <paramref name="ring"/>'s slot for its tick (not yet published).</summary>
    public static AiSnapshot ReadSnapshot(BinaryReader r, SnapshotRing ring)
    {
        AiSnapshot s = ring.BeginWrite(new Tick(r.ReadInt32()));
        int views = r.ReadUInt16();
        for (int i = 0; i < views; i++)
        {
            ActorId bot = ReadActor(r);
            PerceivedView view = s.Add(bot, ReadSelf(r));
            int sighted = r.ReadUInt16();
            for (int j = 0; j < sighted; j++)
                view.AddSighted(ReadFacts(r));
        }

        return s;
    }

    public static void Write(BinaryWriter w, in IntentCommand i)
    {
        Write(w, i.Bot);
        w.Write(i.FromTick.Value);
        w.Write(i.Seq);
        w.Write(i.ExpiresTick.Value);

        MoveIntent m = i.Move;
        w.Write((byte)m.Kind);
        w.Write((byte)m.Speed);
        w.Write(m.CornerCount);
        Write(w, m.Corner0);
        Write(w, m.Corner1);
        Write(w, m.Corner2);
        Write(w, m.Corner3);
        Write(w, m.Direction);
        w.Write(m.CrouchOn);
        w.Write(m.Ticks);

        LookIntent l = i.Look;
        w.Write((byte)l.Kind);
        Write(w, l.Point);
        Write(w, l.Actor);
        w.Write((byte)l.Aim);
        w.Write(l.LeadSeconds);
        w.Write(l.Ticks);

        ActionIntent a = i.Action;
        w.Write((byte)a.Kind);
        w.Write((byte)a.Style);
        w.Write(a.WeaponSlot);
        w.Write(a.OneShotSeq);

        DecisionReason d = i.Reason;
        w.Write(d.NodeId);
        w.Write(d.Utility);
        w.Write(d.ScoreboardTerm);
        w.Write(d.AmmoTerm);
    }

    public static IntentCommand ReadIntent(BinaryReader r)
    {
        ActorId bot = ReadActor(r);
        var from = new Tick(r.ReadInt32());
        int seq = r.ReadInt32();
        var expires = new Tick(r.ReadInt32());
        var move = new MoveIntent((MoveKind)r.ReadByte(), (MoveSpeed)r.ReadByte(), r.ReadByte(), ReadVector(r),
                                  ReadVector(r), ReadVector(r), ReadVector(r), ReadVector(r), r.ReadBoolean(), r.ReadInt32());
        var look = new LookIntent((LookKind)r.ReadByte(), ReadVector(r), ReadActor(r), (AimPoint)r.ReadByte(),
                                  r.ReadSingle(), r.ReadInt32());
        var action = new ActionIntent((ActionKind)r.ReadByte(), (FireStyle)r.ReadByte(), r.ReadInt32(), r.ReadInt32());
        var reason = new DecisionReason(r.ReadInt32(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        return new IntentCommand(bot, from, seq, expires, move, look, action, reason);
    }

    public static void Write(BinaryWriter w, in AiUserCmd c)
    {
        Write(w, c.Actor);
        w.Write(c.Tick.Value);
        w.Write(c.CommandNumber);
        w.Write(c.TickCount);
        Write(w, c.View);
        w.Write(c.ForwardMove);
        w.Write(c.SideMove);
        w.Write(c.UpMove);
        w.Write(c.Buttons);
        w.Write(c.Impulse);
        w.Write(c.WeaponSelect);
        w.Write(c.WeaponSubtype);
        w.Write(c.RandomSeed);
        w.Write(c.MouseDx);
        w.Write(c.MouseDy);
    }

    public static AiUserCmd ReadUserCmd(BinaryReader r)
        => new(ReadActor(r), new Tick(r.ReadInt32()), r.ReadInt32(), r.ReadInt32(), ReadAngles(r), r.ReadSingle(),
               r.ReadSingle(), r.ReadSingle(), r.ReadInt32(), r.ReadByte(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
               r.ReadInt16(), r.ReadInt16());
}
