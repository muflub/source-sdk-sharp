//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Ropes;

namespace SourceSharp.Client.Beams;

/// <summary>What the beam bookkeeping asks of the engine.</summary>
public interface IBeamWorld
{
    /// <summary>Whether a model index resolves (<c>modelinfo-&gt;GetModel</c>).</summary>
    bool ModelExists(int modelIndex);

    /// <summary><c>modelinfo-&gt;GetModelFrameCount</c>.</summary>
    int ModelFrameCount(int modelIndex);

    /// <summary><c>modelinfo-&gt;GetModelIndex( name )</c>.</summary>
    int ModelIndexForName(string name);

    /// <summary>The entity at an entry index, or <see cref="EntityHandle.None"/> (<c>BeamEndpointFromIndex</c>).</summary>
    EntityHandle EntityAt(int entry);

    /// <summary>Whether the entity has a model (<c>GetModel() != NULL</c>).</summary>
    bool EntityHasModel(EntityHandle entity);

    /// <summary>
    /// The position of an entity endpoint (an attachment, a hitbox or the
    /// origin), written into <paramref name="position"/> as stock's
    /// by-reference argument is: a failed lookup may leave it as it was.
    /// </summary>
    bool EntityPosition(EntityHandle entity, int attachment, bool useHitboxes, ref Vector position);

    /// <summary><c>engine-&gt;IsBoxVisible</c>.</summary>
    bool IsBoxVisible(in Vector mins, in Vector maxs);

    /// <summary><c>engine-&gt;CullBox</c>.</summary>
    bool CullBox(in Vector mins, in Vector maxs);
}

/// <summary>The leaf-system renderables beams are drawn through.</summary>
public interface IBeamRenderables
{
    /// <summary><c>AddRenderable( beam, RENDER_GROUP_TRANSLUCENT_ENTITY )</c>.</summary>
    int Add(Beam beam);

    /// <summary><c>RenderableChanged</c>: the beam moved or grew.</summary>
    void Changed(Beam beam);

    /// <summary><c>RemoveRenderable</c>.</summary>
    void Remove(int handle);
}

/// <summary>
/// <c>CViewRenderBeams</c> (view_beams.cpp): the beams alive in the client,
/// their creation by kind, their per-frame update (lifetimes, noise, entity
/// endpoints, ring growth) and their draw by type. The active list is newest
/// first, as stock's was, because the update order is the order the shared
/// noise stream is drawn in.
/// </summary>
public sealed class BeamSystem
{
    /// <summary><c>DEFAULT_PARTICLES</c>: the trail nodes follow beams share.</summary>
    public const int DefaultTrails = 2048;

    private const int RenderNormal = 0;    // kRenderNormal
    private const int RenderGlow = 3;      // kRenderGlow
    private const int RenderTransAdd = 5;  // kRenderTransAdd

    private readonly List<Beam> _active = [];
    private readonly IBeamWorld _world;
    private readonly IBeamRenderables _renderables;
    private readonly UniformStream _random = new();
    private int _freeTrails;

    public BeamSystem(IBeamWorld world, IBeamRenderables renderables, int trails = DefaultTrails)
    {
        _world = world;
        _renderables = renderables;
        _freeTrails = trails;
        Trails = trails;
    }

    /// <summary>The trail pool's size (<c>-particles</c>).</summary>
    public int Trails { get; }

    /// <summary>The beams alive, newest first.</summary>
    public IReadOnlyList<Beam> Active => _active;

    /// <summary>The noise stream (<c>beamRandom</c>).</summary>
    public UniformStream Random => _random;

    //-------------------------------------------------------------------------
    // Reload: every live beam, its trail and the noise stream, carried whole.
    // The renderable indices are native's and outlive the managed build.
    //-------------------------------------------------------------------------

    internal void Save(ReloadWriter w)
    {
        w.Write(Trails);
        w.Write(_freeTrails);
        _random.Save(w);
        w.Write(_active.Count);

        foreach (Beam b in _active)
        {
            w.Write((int)b.Type);
            w.Write((int)b.Flags);
            w.Write(b.NumAttachments);
            for (int i = 0; i < Beam.MaxEnts; i++)
            {
                RopeCarry.Write(w, b.Attachment[i]);
                w.Write((int)b.Entity[i].Value);
                w.Write(b.AttachmentIndex[i]);
            }
            RopeCarry.Write(w, b.Delta);
            w.Write(b.T);
            w.Write(b.TWritten);
            w.Write(b.Freq);
            w.Write(b.Die);
            w.Write(b.Width);
            w.Write(b.EndWidth);
            w.Write(b.FadeLength);
            w.Write(b.Amplitude);
            w.Write(b.Life);
            w.Write(b.R);
            w.Write(b.G);
            w.Write(b.B);
            w.Write(b.Brightness);
            w.Write(b.Speed);
            w.Write(b.FrameRate);
            w.Write(b.Frame);
            w.Write(b.Segments);
            w.Write(b.ModelIndex);
            w.Write(b.HaloIndex);
            w.Write(b.HaloScale);
            w.Write(b.FrameCount);
            foreach (float n in b.Noise)
                w.Write(n);
            w.Write(b.StartRadius);
            w.Write(b.EndRadius);
            w.Write(b.CalculatedNoise);
            w.Write(b.HdrColorScale);
            w.Write(b.HaloProxySize);
            RopeCarry.Write(w, b.Mins);
            RopeCarry.Write(w, b.Maxs);
            w.Write(b.Renderable);

            int trail = 0;
            for (BeamTrail? t = b.Trail; t is not null; t = t.Next)
                trail++;
            w.Write(trail);
            for (BeamTrail? t = b.Trail; t is not null; t = t.Next)
            {
                w.Write(t.Die);
                RopeCarry.Write(w, t.Org);
                RopeCarry.Write(w, t.Vel);
            }
        }
    }

    internal static BeamSystem Restore(ReloadReader r, IBeamWorld world, IBeamRenderables renderables)
    {
        var system = new BeamSystem(world, renderables, r.ReadInt());
        system._freeTrails = r.ReadInt();
        system._random.Restore(r);
        int count = r.ReadInt();

        for (int k = 0; k < count; k++)
        {
            var b = new Beam
            {
                Type = (BeamType)r.ReadInt(),
                Flags = (BeamFlags)r.ReadInt(),
                NumAttachments = r.ReadInt(),
            };
            for (int i = 0; i < Beam.MaxEnts; i++)
            {
                b.Attachment[i] = RopeCarry.Read(r);
                b.Entity[i] = new EntityHandle((uint)r.ReadInt());
                b.AttachmentIndex[i] = r.ReadInt();
            }
            b.Delta = RopeCarry.Read(r);
            b.T = r.ReadFloat();
            b.TWritten = r.ReadBool();
            b.Freq = r.ReadFloat();
            b.Die = r.ReadFloat();
            b.Width = r.ReadFloat();
            b.EndWidth = r.ReadFloat();
            b.FadeLength = r.ReadFloat();
            b.Amplitude = r.ReadFloat();
            b.Life = r.ReadFloat();
            b.R = r.ReadFloat();
            b.G = r.ReadFloat();
            b.B = r.ReadFloat();
            b.Brightness = r.ReadFloat();
            b.Speed = r.ReadFloat();
            b.FrameRate = r.ReadFloat();
            b.Frame = r.ReadFloat();
            b.Segments = r.ReadInt();
            b.ModelIndex = r.ReadInt();
            b.HaloIndex = r.ReadInt();
            b.HaloScale = r.ReadFloat();
            b.FrameCount = r.ReadInt();
            for (int i = 0; i < b.Noise.Length; i++)
                b.Noise[i] = r.ReadFloat();
            b.StartRadius = r.ReadFloat();
            b.EndRadius = r.ReadFloat();
            b.CalculatedNoise = r.ReadBool();
            b.HdrColorScale = r.ReadFloat();
            b.HaloProxySize = r.ReadFloat();
            b.Mins = RopeCarry.Read(r);
            b.Maxs = RopeCarry.Read(r);
            b.Renderable = r.ReadInt();

            int trail = r.ReadInt();
            BeamTrail? last = null;
            for (int i = 0; i < trail; i++)
            {
                var t = new BeamTrail { Die = r.ReadFloat(), Org = RopeCarry.Read(r), Vel = RopeCarry.Read(r) };
                if (last is null)
                    b.Trail = t;
                else
                    last.Next = t;
                last = t;
            }

            system._active.Add(b);
        }

        return system;
    }

    //-------------------------------------------------------------------------
    // Allocation.
    //-------------------------------------------------------------------------

    private Beam Alloc(bool renderable)
    {
        var beam = new Beam();
        _active.Insert(0, beam);

        if (renderable)
            beam.Renderable = _renderables.Add(beam);

        return beam;
    }

    // BeamFree: only the DEAD trail nodes go back to the pool, as stock's did;
    // Reset then drops the live ones, which the pool never sees again.
    private void Free(Beam beam, float now)
    {
        FreeDeadTrails(beam, now);
        if (beam.Renderable >= 0)
            _renderables.Remove(beam.Renderable);
        beam.Reset();
    }

    /// <summary><c>FreeBeam</c>: a beam its creator is done with.</summary>
    public void FreeBeam(Beam beam, float now)
    {
        if (_active.Remove(beam))
            Free(beam, now);
    }

    /// <summary><c>ClearBeams</c>: every beam and trail goes.</summary>
    public void Clear()
    {
        foreach (Beam beam in _active)
        {
            if (beam.Renderable >= 0)
                _renderables.Remove(beam.Renderable);
        }

        _active.Clear();
        _freeTrails = Trails;
    }

    // FreeDeadTrails, with "now" passed so Free can return every node.
    private void FreeDeadTrails(Beam beam, float now)
    {
        while (beam.Trail is { } head && head.Die < now)
        {
            beam.Trail = head.Next;
            _freeTrails++;
        }

        for (BeamTrail? p = beam.Trail; p is not null; p = p.Next)
        {
            while (p.Next is { } kill && kill.Die < now)
            {
                p.Next = kill.Next;
                _freeTrails++;
            }
        }
    }

    //-------------------------------------------------------------------------
    // Kill.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>KillDeadBeams</c>: an entity went. Every beam that STARTS on it
    /// loses its endpoints and dies (its trail with it) — except a follow beam,
    /// which keeps its trail and fades out on its own.
    /// </summary>
    public void KillDeadBeams(EntityHandle dead, float now)
    {
        bool valid = dead != EntityHandle.None;
        var keep = new List<Beam>(_active.Count);

        // Stock rebuilds the list by prepending, which reverses it.
        foreach (Beam beam in _active)
        {
            bool startIsDead = beam.Entity[0] == dead;
            if (!valid || !startIsDead)
            {
                keep.Insert(0, beam);
                continue;
            }

            beam.Flags &= ~(BeamFlags.StartEntity | BeamFlags.EndEntity);

            if (beam.Type != BeamType.Follow)
            {
                beam.Die = (float)(now - 0.1);
                for (BeamTrail? t = beam.Trail; t is not null; t = t.Next)
                    t.Die = (float)(now - 0.1);
                Free(beam, now);
            }
            else
            {
                keep.Insert(0, beam);
            }
        }

        _active.Clear();
        _active.AddRange(keep);
    }

    //-------------------------------------------------------------------------
    // Creation.
    //-------------------------------------------------------------------------

    private void SetupBeam(Beam beam, in BeamInfo info, float now)
    {
        if (!_world.ModelExists(info.ModelIndex))
            return;

        beam.Type = (int)info.Type < 0 ? BeamType.Points : info.Type;
        beam.ModelIndex = info.ModelIndex;
        beam.HaloIndex = info.HaloIndex;
        beam.HaloScale = info.HaloScale;
        beam.Frame = 0;
        beam.FrameRate = 0;
        beam.FrameCount = _world.ModelFrameCount(info.ModelIndex);
        beam.Freq = now * info.Speed;
        beam.Die = now + info.Life;
        beam.Width = info.Width;
        beam.EndWidth = info.EndWidth;
        beam.FadeLength = info.FadeLength;
        beam.Amplitude = info.Amplitude;
        beam.Brightness = info.Brightness;
        beam.Speed = info.Speed;
        beam.Life = info.Life;
        beam.Flags = 0;
        beam.Attachment[0] = info.Start;
        beam.Attachment[1] = info.End;
        beam.Delta = info.End - info.Start;

        beam.Segments = info.Segments == -1 ? SegmentsFor(beam) : info.Segments;
    }

    // One segment per 4 units of a noisy beam, per 16 of a calm one.
    private static int SegmentsFor(Beam beam)
        => beam.Amplitude >= 0.50
            ? (int)(beam.Delta.Length * 0.25 + 3)
            : (int)(beam.Delta.Length * 0.075 + 3);

    private static void SetBeamAttributes(Beam beam, in BeamInfo info)
    {
        beam.Frame = (float)info.StartFrame;
        beam.FrameRate = info.FrameRate;
        beam.Flags |= info.Flags;
        beam.R = info.Red;
        beam.G = info.Green;
        beam.B = info.Blue;
    }

    private bool CullBeam(in Vector start, in Vector end, bool pvsOnly)
    {
        Vector mins = default, maxs = default;
        for (int i = 0; i < 3; i++)
        {
            float s = Get(start, i), e = Get(end, i);
            float lo, hi;
            if (s < e)
            {
                lo = s;
                hi = e;
            }
            else
            {
                lo = e;
                hi = s;
            }
            if (lo == hi)
                hi += 1;
            Set(ref mins, i, lo);
            Set(ref maxs, i, hi);
        }

        if (_world.IsBoxVisible(mins, maxs))
        {
            if (pvsOnly || !_world.CullBox(mins, maxs))
                return true;
        }

        return false;
    }

    private static float Get(in Vector v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    private static void Set(ref Vector v, int i, float f)
    {
        if (i == 0) v.X = f;
        else if (i == 1) v.Y = f;
        else v.Z = f;
    }

    private Beam? CreateGeneric(ref BeamInfo info, float now)
    {
        Beam beam = Alloc(info.Renderable);
        beam.Die = now;

        if (info.ModelIndex < 0)
            return null;

        SetupBeam(beam, info, now);
        return beam;
    }

    private void ResolveNames(ref BeamInfo info)
    {
        if (info.ModelName is not null && info.ModelIndex == -1)
            info.ModelIndex = _world.ModelIndexForName(info.ModelName);
        if (info.HaloName is not null && info.HaloIndex == -1)
            info.HaloIndex = _world.ModelIndexForName(info.HaloName);
    }

    private bool HasModel(EntityHandle e) => e != EntityHandle.None && _world.EntityHasModel(e);

    /// <summary><c>CreateBeamEnts( BeamInfo_t &amp; )</c>: a beam between two entities.</summary>
    public Beam? CreateBeamEnts(BeamInfo info, float now)
    {
        if (info.Life != 0 && (!HasModel(info.StartEnt) || !HasModel(info.EndEnt)))
            return null;

        info.Start = default;
        info.End = default;

        Beam? beam = CreateGeneric(ref info, now);
        if (beam is null)
            return null;

        beam.Type = (int)info.Type < 0 ? BeamType.Points : info.Type;
        beam.Flags = BeamFlags.StartEntity | BeamFlags.EndEntity;
        beam.Entity[0] = info.StartEnt;
        beam.AttachmentIndex[0] = info.StartAttachment;
        beam.Entity[1] = info.EndEnt;
        beam.AttachmentIndex[1] = info.EndAttachment;

        SetBeamAttributes(beam, info);
        if (info.Life == 0)
            beam.Flags |= BeamFlags.Forever;

        UpdateBeam(beam, 0, now);
        return beam;
    }

    /// <summary><c>CreateBeamEntPoint( BeamInfo_t &amp; )</c>: an entity and a point, or two points.</summary>
    public Beam? CreateBeamEntPoint(BeamInfo info, float now)
    {
        if (info.Life != 0)
        {
            if (info.StartEnt != EntityHandle.None && !_world.EntityHasModel(info.StartEnt))
                return null;
            if (info.EndEnt != EntityHandle.None && !_world.EntityHasModel(info.EndEnt))
                return null;
        }

        ResolveNames(ref info);

        Beam? beam = CreateGeneric(ref info, now);
        if (beam is null)
            return null;

        beam.Type = BeamType.Points;
        beam.Flags = 0;

        if (info.StartEnt != EntityHandle.None)
        {
            beam.Flags |= BeamFlags.StartEntity;
            beam.Entity[0] = info.StartEnt;
            beam.AttachmentIndex[0] = info.StartAttachment;
            info.Start = default;
        }

        if (info.EndEnt != EntityHandle.None)
        {
            beam.Flags |= BeamFlags.EndEntity;
            beam.Entity[1] = info.EndEnt;
            beam.AttachmentIndex[1] = info.EndAttachment;
            info.End = default;
        }

        SetBeamAttributes(beam, info);
        if (info.Life == 0)
            beam.Flags |= BeamFlags.Forever;

        UpdateBeam(beam, 0, now);
        return beam;
    }

    /// <summary><c>CreateBeamPoints( BeamInfo_t &amp; )</c>: a beam between two points.</summary>
    public Beam? CreateBeamPoints(BeamInfo info, float now)
    {
        if (info.Life != 0 && !CullBeam(info.Start, info.End, true))
            return null;

        ResolveNames(ref info);

        Beam? beam = CreateGeneric(ref info, now);
        if (beam is null)
            return null;

        SetBeamAttributes(beam, info);
        if (info.Life == 0)
            beam.Flags |= BeamFlags.Forever;

        return beam;
    }

    /// <summary><c>CreateBeamCirclePoints( BeamInfo_t &amp; )</c>: a disk or a cylinder.</summary>
    public Beam? CreateBeamCirclePoints(BeamInfo info, float now)
    {
        Beam? beam = CreateGeneric(ref info, now);
        if (beam is null)
            return null;

        beam.Type = info.Type;
        SetBeamAttributes(beam, info);
        if (info.Life == 0)
            beam.Flags |= BeamFlags.Forever;

        return beam;
    }

    /// <summary><c>CreateBeamFollow( BeamInfo_t &amp; )</c>: a trail behind an entity.</summary>
    public Beam? CreateBeamFollow(BeamInfo info, float now)
    {
        info.Start = default;
        info.End = default;
        info.Speed = 1.0f;

        Beam? beam = CreateGeneric(ref info, now);
        if (beam is null)
            return null;

        beam.Type = BeamType.Follow;
        beam.Flags = BeamFlags.StartEntity;
        beam.Entity[0] = info.StartEnt;
        beam.AttachmentIndex[0] = info.StartAttachment;

        info.FrameRate = 1.0f;
        info.StartFrame = 0;
        SetBeamAttributes(beam, info);

        UpdateBeam(beam, 0, now);
        return beam;
    }

    /// <summary><c>CreateBeamRingPoint( BeamInfo_t &amp; )</c>: a ring growing around a point.</summary>
    public Beam? CreateBeamRingPoint(BeamInfo info, float now)
    {
        info.Start = info.Center;
        info.End = info.Center;

        Beam? beam = CreateGeneric(ref info, now);
        if (beam is null)
            return null;

        beam.Type = BeamType.RingPoint;
        beam.StartRadius = info.StartRadius;
        beam.EndRadius = info.EndRadius;
        beam.Attachment[2] = info.Center;

        SetBeamAttributes(beam, info);
        if (info.Life == 0)
            beam.Flags |= BeamFlags.Forever;

        return beam;
    }

    /// <summary><c>CreateBeamRing( BeamInfo_t &amp; )</c>: a ring between two entities.</summary>
    public Beam? CreateBeamRing(BeamInfo info, float now)
    {
        if (info.Life != 0 && (!HasModel(info.StartEnt) || !HasModel(info.EndEnt)))
            return null;

        info.Start = default;
        info.End = default;

        Beam? beam = CreateGeneric(ref info, now);
        if (beam is null)
            return null;

        beam.Type = BeamType.Ring;
        beam.Flags = BeamFlags.StartEntity | BeamFlags.EndEntity;
        beam.Entity[0] = info.StartEnt;
        beam.AttachmentIndex[0] = info.StartAttachment;
        beam.Entity[1] = info.EndEnt;
        beam.AttachmentIndex[1] = info.EndAttachment;

        SetBeamAttributes(beam, info);
        if (info.Life == 0)
            beam.Flags |= BeamFlags.Forever;

        UpdateBeam(beam, 0, now);
        return beam;
    }

    /// <summary><c>UpdateBeamInfo</c>: move a beam its creator owns.</summary>
    public void UpdateBeamInfo(Beam beam, in BeamInfo info)
    {
        beam.Attachment[0] = info.Start;
        beam.Attachment[1] = info.End;
        beam.Delta = info.End - info.Start;
        SetBeamAttributes(beam, info);
    }

    //-------------------------------------------------------------------------
    // The flat creators: entity INDICES (BEAMENT_ENTITY | BEAMENT_ATTACHMENT << 12).
    //-------------------------------------------------------------------------

    private EntityHandle EntOf(int packed) => _world.EntityAt(packed & 0xFFF);

    private static int AttachmentOf(int packed) => (packed >> 12) & 0xF;

    private static BeamInfo Info(int modelIndex, int haloIndex, float haloScale, float life, float width,
                                 float endWidth, float fadeLength, float amplitude, float brightness, float speed,
                                 int startFrame, float frameRate, float r, float g, float b)
        => new()
        {
            ModelIndex = modelIndex, HaloIndex = haloIndex, HaloScale = haloScale, Life = life, Width = width,
            EndWidth = endWidth, FadeLength = fadeLength, Amplitude = amplitude, Brightness = brightness,
            Speed = speed, StartFrame = startFrame, FrameRate = frameRate, Red = r, Green = g, Blue = b,
        };

    public Beam? CreateBeamEnts(int startEnt, int endEnt, int modelIndex, int haloIndex, float haloScale,
                                float life, float width, float endWidth, float fadeLength, float amplitude,
                                float brightness, float speed, int startFrame, float frameRate,
                                float r, float g, float b, int type, float now)
    {
        BeamInfo info = Info(modelIndex, haloIndex, haloScale, life, width, endWidth, fadeLength, amplitude,
                             brightness, speed, startFrame, frameRate, r, g, b);
        info.Type = (BeamType)type;
        info.StartEnt = EntOf(startEnt);
        info.StartAttachment = AttachmentOf(startEnt);
        info.EndEnt = EntOf(endEnt);
        info.EndAttachment = AttachmentOf(endEnt);
        return CreateBeamEnts(info, now);
    }

    public Beam? CreateBeamEntPoint(int startEnt, Vector? start, int endEnt, Vector? end, int modelIndex,
                                    int haloIndex, float haloScale, float life, float width, float endWidth,
                                    float fadeLength, float amplitude, float brightness, float speed, int startFrame,
                                    float frameRate, float r, float g, float b, float now)
    {
        BeamInfo info = Info(modelIndex, haloIndex, haloScale, life, width, endWidth, fadeLength, amplitude,
                             brightness, speed, startFrame, frameRate, r, g, b);

        if (startEnt <= 0)
        {
            info.Start = start ?? default;
            info.StartEnt = EntityHandle.None;
        }
        else
        {
            info.StartEnt = EntOf(startEnt);
            info.StartAttachment = AttachmentOf(startEnt);
            if (info.StartEnt == EntityHandle.None)
                return null;
        }

        if (endEnt <= 0)
        {
            info.End = end ?? default;
            info.EndEnt = EntityHandle.None;
        }
        else
        {
            info.EndEnt = EntOf(endEnt);
            info.EndAttachment = AttachmentOf(endEnt);
            if (info.EndEnt == EntityHandle.None)
                return null;
        }

        return CreateBeamEntPoint(info, now);
    }

    public Beam? CreateBeamPoints(in Vector start, in Vector end, int modelIndex, int haloIndex, float haloScale,
                                  float life, float width, float endWidth, float fadeLength, float amplitude,
                                  float brightness, float speed, int startFrame, float frameRate,
                                  float r, float g, float b, float now)
    {
        BeamInfo info = Info(modelIndex, haloIndex, haloScale, life, width, endWidth, fadeLength, amplitude,
                             brightness, speed, startFrame, frameRate, r, g, b);
        info.Start = start;
        info.End = end;
        return CreateBeamPoints(info, now);
    }

    public Beam? CreateBeamRing(int startEnt, int endEnt, int modelIndex, int haloIndex, float haloScale,
                                float life, float width, float endWidth, float fadeLength, float amplitude,
                                float brightness, float speed, int startFrame, float frameRate,
                                float r, float g, float b, int flags, float now)
    {
        BeamInfo info = Info(modelIndex, haloIndex, haloScale, life, width, endWidth, fadeLength, amplitude,
                             brightness, speed, startFrame, frameRate, r, g, b);
        info.StartEnt = EntOf(startEnt);
        info.StartAttachment = AttachmentOf(startEnt);
        info.EndEnt = EntOf(endEnt);
        info.EndAttachment = AttachmentOf(endEnt);
        info.Flags = (BeamFlags)flags;
        return CreateBeamRing(info, now);
    }

    public Beam? CreateBeamRingPoint(in Vector center, float startRadius, float endRadius, int modelIndex,
                                     int haloIndex, float haloScale, float life, float width, float endWidth,
                                     float fadeLength, float amplitude, float brightness, float speed,
                                     int startFrame, float frameRate, float r, float g, float b, int flags,
                                     float now)
    {
        BeamInfo info = Info(modelIndex, haloIndex, haloScale, life, width, endWidth, fadeLength, amplitude,
                             brightness, speed, startFrame, frameRate, r, g, b);
        info.Center = center;
        info.StartRadius = startRadius;
        info.EndRadius = endRadius;
        info.Flags = (BeamFlags)flags;
        return CreateBeamRingPoint(info, now);
    }

    public Beam? CreateBeamFollow(int startEnt, int modelIndex, int haloIndex, float haloScale, float life,
                                  float width, float endWidth, float fadeLength, float r, float g, float b,
                                  float brightness, float now)
    {
        var info = new BeamInfo
        {
            StartEnt = EntOf(startEnt),
            StartAttachment = AttachmentOf(startEnt),
            ModelIndex = modelIndex,
            HaloIndex = haloIndex,
            HaloScale = haloScale,
            Life = life,
            Width = width,
            EndWidth = endWidth,
            FadeLength = fadeLength,
            Brightness = brightness,
            Red = r,
            Green = g,
            Blue = b,
            Amplitude = life,
        };
        return CreateBeamFollow(info, now);
    }

    //-------------------------------------------------------------------------
    // The noise.
    //-------------------------------------------------------------------------

    /// <summary><c>Noise</c>: midpoint displacement, second half first, halving the scale.</summary>
    public void Noise(float[] noise, int offset, int divs, float scale)
    {
        int div2 = divs >> 1;
        if (divs < 2)
            return;

        noise[offset + div2] = (float)((noise[offset] + noise[offset + divs]) * 0.5 + scale * _random.RandomFloat(-1, 1));

        if (div2 > 1)
        {
            Noise(noise, offset + div2, div2, (float)(scale * 0.5));
            Noise(noise, offset, div2, (float)(scale * 0.5));
        }
    }

    /// <summary><c>SineNoise</c>: one half sine across the beam.</summary>
    public static void SineNoise(float[] noise, int divs)
    {
        float freq = 0;
        float step = (float)(Math.PI / (float)divs);

        for (int i = 0; i < divs; i++)
        {
            noise[i] = MathF.Sin(freq);
            freq += step;
        }
    }

    //-------------------------------------------------------------------------
    // The frame.
    //-------------------------------------------------------------------------

    private bool RecomputeBeamEndpoints(Beam beam, float now)
    {
        bool hitboxes = (beam.Flags & BeamFlags.UseHitboxes) != 0;

        if ((beam.Flags & BeamFlags.StartEntity) != 0)
        {
            if (_world.EntityPosition(beam.Entity[0], beam.AttachmentIndex[0], hitboxes, ref beam.Attachment[0]))
            {
                beam.Flags |= BeamFlags.StartVisible;
            }
            else
            {
                if ((beam.Flags & BeamFlags.Forever) == 0)
                    beam.Flags &= ~BeamFlags.StartEntity;
            }

            if ((beam.Flags & BeamFlags.StartVisible) == 0)
                return false;
        }

        if ((beam.Flags & BeamFlags.EndEntity) != 0)
        {
            if (_world.EntityPosition(beam.Entity[1], beam.AttachmentIndex[1], hitboxes, ref beam.Attachment[1]))
            {
                beam.Flags |= BeamFlags.EndVisible;
            }
            else
            {
                if ((beam.Flags & BeamFlags.Forever) == 0)
                {
                    beam.Flags &= ~BeamFlags.EndEntity;
                    beam.Die = now;
                    return false;
                }
                return false;
            }

            if ((beam.Flags & BeamFlags.EndVisible) == 0)
                return false;
        }

        return true;
    }

    /// <summary><c>UpdateBeam</c>: one beam's step (frametime 0 at creation reseeds the noise).</summary>
    public void UpdateBeam(Beam beam, float frameTime, float now)
    {
        if (beam.ModelIndex < 0)
        {
            beam.Die = now;
            return;
        }

        if (frameTime == 0.0f)
            _random.SetSeed((int)now);

        if ((beam.Flags & BeamFlags.OnlyNoiseOnce) == 0)
            beam.Freq += frameTime;
        else
            beam.Freq += frameTime * _random.RandomFloat(1, 2);

        beam.Noise[0] = 0;
        beam.Noise[Beam.NoiseDivisions] = 0;

        if (beam.Amplitude != 0)
        {
            if ((beam.Flags & BeamFlags.OnlyNoiseOnce) == 0 || !beam.CalculatedNoise)
            {
                if ((beam.Flags & BeamFlags.SineNoise) != 0)
                    SineNoise(beam.Noise, Beam.NoiseDivisions);
                else
                    Noise(beam.Noise, 0, Beam.NoiseDivisions, 1.0f);

                beam.CalculatedNoise = true;
            }
        }

        if ((beam.Flags & (BeamFlags.StartEntity | BeamFlags.EndEntity)) != 0)
        {
            if (!RecomputeBeamEndpoints(beam, now))
                return;

            beam.Delta = beam.Attachment[1] - beam.Attachment[0];
            beam.Segments = SegmentsFor(beam);
        }

        switch (beam.Type)
        {
            case BeamType.Spline:
                for (int i = 1; i < beam.NumAttachments; i++)
                {
                    if (!_world.EntityPosition(beam.Entity[i], beam.AttachmentIndex[i],
                                               (beam.Flags & BeamFlags.UseHitboxes) != 0, ref beam.Attachment[i]))
                        beam.Attachment[i] = beam.Attachment[i - 1];
                }
                break;

            case BeamType.RingPoint:
            {
                float dr = beam.EndRadius - beam.StartRadius;
                if (dr != 0.0f)
                {
                    float frac = 1.0f;
                    float remaining = beam.Die - now;
                    if (remaining < beam.Life && beam.Life > 0.0f)
                        frac = remaining / beam.Life;

                    frac = 1.0f < frac ? 1.0f : frac;     // MIN( 1.0f, frac )
                    frac = 0.0f > frac ? 0.0f : frac;     // MAX( 0.0f, frac )
                    frac = 1.0f - frac;

                    Vector endpos = beam.Attachment[2];
                    endpos.X += (beam.StartRadius + frac * dr) / 2.0f;
                    Vector startpos = beam.Attachment[2];
                    startpos.X -= (beam.StartRadius + frac * dr) / 2.0f;

                    beam.Attachment[0] = startpos;
                    beam.Attachment[1] = endpos;
                    beam.Delta = beam.Attachment[1] - beam.Attachment[0];
                    beam.Segments = SegmentsFor(beam);
                }
                break;
            }

            case BeamType.Points:
                if (!CullBeam(beam.Attachment[0], beam.Attachment[1], false))
                    return;
                break;
        }

        beam.T = beam.Freq + (beam.Die - now);
        beam.T = beam.T != 0 ? beam.Freq / beam.T : 1.0f;
        beam.TWritten = true;

        if (beam.FadeLength == 0)
            beam.FadeLength = beam.Delta.Length;
    }

    /// <summary><c>UpdateTempEntBeams</c>: retire the dead, step and bound the living.</summary>
    public void UpdateTempEntBeams(float now, float frameTime)
    {
        if (_active.Count == 0 || frameTime == 0.0f)
            return;

        for (int i = 0; i < _active.Count;)
        {
            Beam beam = _active[i];

            if ((beam.Flags & BeamFlags.Forever) == 0 && beam.Die <= now)
            {
                _active.RemoveAt(i);
                Free(beam, now);
                continue;
            }

            UpdateBeam(beam, frameTime, now);
            beam.ComputeBounds();

            if (beam.Renderable >= 0)
                _renderables.Changed(beam);

            i++;
        }
    }

    //-------------------------------------------------------------------------
    // The draw.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>DrawBeam( C_Beam *, ITraceFilter * )</c> for a beam between two
    /// points: a Beam_t built from the entity's own state on the stack, never
    /// listed or given a renderable, then updated and drawn at once, from
    /// inside the entity's DrawModel. Its life is 0, so its die time is now.
    /// </summary>
    public void DrawEntityBeam(BeamInfo info, BeamFlags entityFlags, float hdrColorScale, BeamDraw draw,
                               IBeamDrawWorld world, float now, float frameTime)
    {
        var beam = new Beam();
        info.Life = 0;
        ResolveNames(ref info);
        SetupBeam(beam, info, now);
        SetBeamAttributes(beam, info);

        beam.Flags |= entityFlags & (BeamFlags.SineNoise | BeamFlags.Solid | BeamFlags.ShadeIn
                                     | BeamFlags.ShadeOut | BeamFlags.NoTile);
        beam.HdrColorScale = hdrColorScale;

        UpdateBeam(beam, frameTime, now);
        DrawBeam(beam, draw, world);
    }

    /// <summary><c>DrawBeam( Beam_t * )</c>: frame, render mode, colour, then the shape by type.</summary>
    public void DrawBeam(Beam beam, BeamDraw draw, IBeamDrawWorld world)
    {
        BeamView view = draw.View;

        if (view.DrawBeams == 0)
            return;

        if (beam.Delta.Length < 0.1)
            return;

        if (beam.ModelIndex < 0)
        {
            beam.Die = view.CurTime;
            return;
        }

        if (!_world.ModelExists(beam.ModelIndex))
            return;

        bool halo = _world.ModelExists(beam.HaloIndex);

        int frame = (int)(beam.Frame + view.CurTime * beam.FrameRate) % beam.FrameCount;
        int renderMode = (beam.Flags & BeamFlags.Solid) != 0 ? RenderNormal : RenderTransAdd;

        Vector src = new(beam.R, beam.G, beam.B);
        Vector color;
        if ((beam.Flags & BeamFlags.FadeIn) != 0)
            color = src * beam.T;
        else if ((beam.Flags & BeamFlags.FadeOut) != 0)
            color = src * (1.0f - beam.T);
        else
            color = src;

        color = color * (float)(1 / 255.0);
        src = color;
        color = color * (float)((float)beam.Brightness / 255.0);

        switch (beam.Type)
        {
            case BeamType.Disk:
                draw.DrawDisk(beam.ModelIndex, frame, renderMode, beam.Attachment[0], beam.Delta, beam.Width,
                              beam.Amplitude, beam.Freq, beam.Speed, beam.Segments, color, beam.HdrColorScale);
                break;

            case BeamType.Cylinder:
                draw.DrawCylinder(beam.ModelIndex, frame, renderMode, beam.Attachment[0], beam.Delta, beam.Width,
                                  beam.Amplitude, beam.Freq, beam.Speed, beam.Segments, color, beam.HdrColorScale);
                break;

            case BeamType.Points:
                if (halo)
                    DrawBeamWithHalo(beam, frame, renderMode, color, src, draw, world);
                else
                    draw.DrawSegs(beam.Noise, beam.ModelIndex, frame, renderMode, beam.Attachment[0], beam.Delta,
                                  beam.Width, beam.EndWidth, beam.Amplitude, beam.Freq, beam.Speed, beam.Segments,
                                  beam.Flags, color, beam.FadeLength, beam.HdrColorScale);
                break;

            case BeamType.Follow:
                DrawBeamFollow(beam, frame, renderMode, view.FrameTime, color, draw, world);
                break;

            case BeamType.Ring:
            case BeamType.RingPoint:
                draw.DrawRing(beam.Noise, (n, divs, scale) => Noise(n, 0, divs, scale), beam.ModelIndex, frame,
                              renderMode, beam.Attachment[0], beam.Delta, beam.Width, beam.Amplitude, beam.Freq,
                              beam.Speed, beam.Segments, color, beam.HdrColorScale);
                break;

            case BeamType.Spline:
                draw.DrawSplineSegs(beam.Noise, beam.ModelIndex, halo ? beam.HaloIndex : -1, beam.HaloScale, frame,
                                    renderMode, beam.NumAttachments, beam.Attachment, beam.Width, beam.EndWidth,
                                    beam.Amplitude, beam.Freq, beam.Speed, beam.Segments, beam.Flags, color,
                                    beam.FadeLength, beam.HdrColorScale);
                break;

            case BeamType.Laser:
                DrawLaser(beam, frame, renderMode, color, draw);
                break;

            case BeamType.Tesla:
                draw.DrawTeslaSegs(beam.Noise, beam.ModelIndex, frame, renderMode, beam.Attachment[0], beam.Delta,
                                   beam.Width, beam.EndWidth, beam.Amplitude, beam.Freq, beam.Speed, beam.Segments,
                                   beam.Flags, color, beam.FadeLength, beam.HdrColorScale);
                break;
        }
    }

    private void DrawBeamWithHalo(Beam beam, int frame, int renderMode, in Vector color, in Vector srcColor,
                                  BeamDraw draw, IBeamDrawWorld world)
    {
        BeamView view = draw.View;

        Vector beamDir = beam.Attachment[1] - beam.Attachment[0];
        RopeMath.Normalize(ref beamDir);

        Vector localDir = view.Origin - beam.Attachment[0];
        RopeMath.Normalize(ref localDir);

        float dot = beamDir.Dot(localDir);
        float fade = dot < 0.0f ? 0 : dot * 2.0f;

        // CalcClosestPointOnLine( eye, a0, a0 + dir * 2 ): the distance is re-measured after.
        Vector lineEnd = beam.Attachment[0] + (beamDir * 2);
        Vector dir = lineEnd - beam.Attachment[0];
        float div = dir.Dot(dir);
        float t = div < 0.00001f ? 0 : (dir.Dot(view.Origin) - dir.Dot(beam.Attachment[0])) / div;
        Vector closest = new(beam.Attachment[0].X + dir.X * t, beam.Attachment[0].Y + dir.Y * t,
                             beam.Attachment[0].Z + dir.Z * t);
        float distToLine = (view.Origin - closest).Length;

        float dotScale = 1.0f;
        float distThreshold = beam.Width * 4.0f;
        if (distToLine < distThreshold)
        {
            dotScale = RopeMath.RemapVal(distToLine, distThreshold, beam.Width, 1.0f, 0.0f);
            dotScale = Math.Clamp(dotScale, 0.0f, 1.0f);
        }

        Vector scaleColor = new(color.X * dotScale, color.Y * dotScale, color.Z * dotScale);

        if ((beam.Flags & BeamFlags.HaloBeam) != 0)
        {
            draw.DrawSegs(beam.Noise, beam.ModelIndex, frame, renderMode, beam.Attachment[0], beam.Delta,
                          beam.Width, beam.EndWidth, beam.Amplitude, beam.Freq, beam.Speed, beam.Segments,
                          beam.Flags, scaleColor, beam.FadeLength, beam.HdrColorScale);
        }
        else
        {
            draw.DrawSegs(beam.Noise, beam.ModelIndex, frame, renderMode, beam.Attachment[0], beam.Delta,
                          beam.Width, beam.Width, beam.Amplitude, beam.Freq, beam.Speed, 2, beam.Flags,
                          scaleColor, beam.FadeLength, beam.HdrColorScale);
        }

        Vector source = beam.Attachment[0];
        float visible = world.HaloVisibility(beam, source, beam.HaloProxySize);

        if (fade != 0 && visible > 0.0f)
        {
            float haloScale = RopeMath.RemapVal(distToLine, distThreshold, beam.Width * 0.5f, 1.0f, 2.0f);
            haloScale = Math.Clamp(haloScale, 1.0f, 2.0f);
            haloScale *= beam.HaloScale;

            float colorFade = fade * fade;
            colorFade = Math.Clamp(colorFade, 0.0f, 1.0f);

            Vector haloColor = srcColor * (colorFade * visible);
            draw.BeamDrawHalo(beam.HaloIndex, frame, RenderGlow, source, haloScale, haloColor, beam.HdrColorScale);
        }
    }

    private static void DrawLaser(Beam beam, int frame, int renderMode, in Vector color, BeamDraw draw)
    {
        BeamView view = draw.View;
        Vector color2 = color;

        Vector beamDir = beam.Attachment[1] - beam.Attachment[0];
        RopeMath.Normalize(ref beamDir);

        Vector forward = MoveVec.AngleForward(view.Angles);
        float dot = beamDir.Dot(forward);

        if (dot > 0)
            return;

        float fade = (float)Math.Pow(dot, 10);

        Vector localDir = view.Origin - beam.Attachment[0];
        dot = beamDir.Dot(localDir);
        Vector projection = dot * beamDir;
        float distance = (localDir - projection).Length;

        if (distance > 30)
        {
            distance = 1 - ((distance - 30) / 64);
            if (distance <= 0)
                fade = 0;
            else
                fade = (float)(fade * Math.Pow(distance, 3));
        }

        if (fade < (1.0f / 255.0f))
            return;

        color2 = color2 * fade;

        draw.DrawSegs(beam.Noise, beam.ModelIndex, frame, renderMode, beam.Attachment[0], beam.Delta, beam.Width,
                      beam.EndWidth, beam.Amplitude, beam.Freq, beam.Speed, beam.Segments, beam.Flags, color2,
                      beam.FadeLength);
    }

    private void DrawBeamFollow(Beam beam, int frame, int renderMode, float frameTime, in Vector color, BeamDraw draw,
                                IBeamDrawWorld world)
    {
        BeamView view = draw.View;

        FreeDeadTrails(beam, view.CurTime);

        BeamTrail? particles = beam.Trail;
        BeamTrail? pnew = null;
        float div = 0;

        if ((beam.Flags & BeamFlags.StartEntity) != 0)
        {
            if (particles is not null)
            {
                Vector d = particles.Org - beam.Attachment[0];
                div = d.Length;
                if (div >= 32 && _freeTrails > 0)
                {
                    pnew = new BeamTrail();
                    _freeTrails--;
                }
            }
            else if (_freeTrails > 0)
            {
                pnew = new BeamTrail();
                _freeTrails--;
                div = 0;
            }
        }

        if (pnew is not null)
        {
            pnew.Org = beam.Attachment[0];
            pnew.Die = view.CurTime + beam.Amplitude;
            pnew.Vel = default;

            beam.Die = view.CurTime + beam.Amplitude;
            pnew.Next = particles;
            beam.Trail = pnew;
            particles = pnew;
        }

        if (particles is null)
            return;

        Vector delta, screen, screenLast;

        if (pnew is null && div != 0)
        {
            delta = beam.Attachment[0];
            screenLast = world.ScreenPosition(beam.Attachment[0]);
            screen = world.ScreenPosition(particles.Org);
        }
        else if (particles.Next is not null)
        {
            delta = particles.Org;
            screenLast = world.ScreenPosition(particles.Org);
            screen = world.ScreenPosition(particles.Next.Org);
        }
        else
        {
            return;
        }

        draw.DrawBeamFollow(beam.ModelIndex, beam.Trail!, frame, renderMode, delta, screen, screenLast, beam.Die,
                            beam.Flags, beam.Width, beam.Amplitude, beam.Freq, color);

        for (BeamTrail? p = beam.Trail; p is not null; p = p.Next)
            p.Org = new Vector(p.Org.X + p.Vel.X * frameTime, p.Org.Y + p.Vel.Y * frameTime,
                               p.Org.Z + p.Vel.Z * frameTime);
    }
}
