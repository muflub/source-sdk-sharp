//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Effects;

/// <summary>
/// particlemgr.cpp's <c>CParticleMgr</c> for the old-style effects (C7
/// particles phase 2.3): one <see cref="ParticleBinding"/> per native
/// executor, in the order they were added, and the loop that updates them.
/// Each native executor is a renderable that answers the leaf system from what
/// this commits and asks this how to draw.
/// </summary>
/// <remarks>
/// The loop is UpdateAllEffects', step for step:
/// - the first box;
/// - the drawn flag cleared;
/// - the sim's Update (its spawns linked at the list's head, at where the
///   executor stands after its follow);
/// - the step (skipped on the first frame);
/// - DetectChanges.
/// The sims are managed, so Update and the step are calls, not crossings.
/// </remarks>
public sealed class ClientParticleManager(Func<IClientParticleSims> sims, IUniformRandom random, ParticleViews views)
    : IClientParticleBindings, IReloadParticipant
{
    /// <summary>The executor's spawns per update (shell_managed_particles.cpp's kMaxEmitPerFrame).</summary>
    public const int MaxEmitPerFrame = 256;

    /// <summary>model_types.h's STUDIO_SSAODEPTHTEXTURE and STUDIO_SHADOWDEPTHTEXTURE: DrawModel draws nothing into a depth pass.</summary>
    public const int DepthPassFlags = 0x08000000 | 0x40000000;

    private sealed class Entry(uint owner, int channel)
    {
        public readonly uint Owner = owner;
        public readonly int Channel = channel;
        public ParticleBinding Binding = new();
        public int MaterialFlags;
        public bool DrawBucket;
    }

    private readonly List<Entry> _entries = [];
    private readonly int[] _ids = new int[MaxEmitPerFrame];

    /// <summary>CParticleMgr::m_FrameCode, which the constructor sets to 1.</summary>
    public ushort FrameCode { get; private set; } = 1;

    public int Count => _entries.Count;

    private Entry? Find(uint owner, int channel)
    {
        foreach (Entry e in _entries)
        {
            if (e.Owner == owner && e.Channel == channel)
                return e;
        }
        return null;
    }

    /// <summary>One line per binding, in update order: owner, channel, particles, drawn last frame, flags.</summary>
    public IEnumerable<string> Describe()
    {
        foreach (Entry e in _entries)
        {
            yield return $"{e.Owner} {e.Channel} particles {e.Binding.ActiveParticles} " +
                         $"drawn {(e.Binding.Has(ParticleBindingFlags.DrawnPrevFrame) ? 1 : 0)} flags {(int)e.Binding.Flags}";
        }
    }

    /// <summary>The binding for an executor, or null.</summary>
    public ParticleBinding? Binding(uint owner, int channel) => Find(owner, channel)?.Binding;

    /// <summary>
    /// The executor started (CManagedParticleEffect::Start): its always-simulate
    /// flag, then AddEffect (at the end of the list), its one material, its cull
    /// radius.
    /// </summary>
    public void Add(uint owner, int channel, float cullRadius, bool alwaysSimulate)
    {
        Remove(owner, channel);
        var e = new Entry(owner, channel);
        e.Binding.Set(ParticleBindingFlags.AlwaysSimulate, alwaysSimulate);
        e.Binding.Material(0);
        e.Binding.SetParticleCullRadius(cullRadius);
        _entries.Add(e);
    }

    public void Remove(uint owner, int channel) => _entries.RemoveAll(e => e.Owner == owner && e.Channel == channel);

    /// <summary>SetParticleCullRadius, from ParticlesConfigure.</summary>
    public void Configure(uint owner, int channel, float cullRadius) => Find(owner, channel)?.Binding.SetParticleCullRadius(cullRadius);

    /// <summary>
    /// CManagedParticleEffect::SetSpace's binding half: drawn before the view
    /// model, a singleton out of the leaf system, an attachment's space (the
    /// follow native read, and the local transform no longer applied for it).
    /// </summary>
    public void SetSpace(uint owner, int channel, ParticleSpace flags, in ParticleFollow follow)
    {
        ParticleBinding? b = Find(owner, channel)?.Binding;
        if (b == null)
            return;
        if ((flags & ParticleSpace.BeforeViewModel) != 0)
            b.SetDrawBeforeViewModel(true);
        if ((flags & ParticleSpace.Singleton) != 0)
            b.SetDrawThruLeafSystem(false);
        if ((flags & ParticleSpace.Attachment) != 0)
        {
            ApplyFollow(b, follow.Followed != 0, follow.Matrix, follow.HasBox != 0, follow.Mins, follow.Maxs);
            b.Set(ParticleBindingFlags.AutoApplyLocalTransform, false);
        }
    }

    private static void ApplyFollow(ParticleBinding b, bool followed, Matrix3x4Raw matrix, bool hasBox, Vector mins, Vector maxs)
    {
        if (!followed)
            return;
        b.SetLocalSpaceTransform(matrix);
        if (hasBox)
            b.SetBBox(mins, maxs, disableAutoUpdate: true);
    }

    /// <summary>
    /// UpdateAllEffects' old-style half, for the executors native listed: a
    /// binding whose executor is not listed went with it and is dropped first.
    /// Answers the commits, one per binding in update order.
    /// </summary>
    public int Frame(in ParticleFrameHeader header, ReadOnlySpan<ParticleFrameIn> inputs, float aabbBuffer, Span<ParticleCommit> commits)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (IndexOf(inputs, _entries[i].Owner, _entries[i].Channel) < 0)
                _entries.RemoveAt(i);
        }

        float dt = header.Dt;
        if (dt > 0.1f)
            dt = 0.1f;

        int written = 0;
        // A copy: a sim's update may start or stop an executor.
        foreach (Entry e in _entries.ToArray())
        {
            int at = IndexOf(inputs, e.Owner, e.Channel);
            if (at < 0)
                continue;
            ref readonly ParticleFrameIn input = ref inputs[at];
            ParticleBinding b = e.Binding;
            e.MaterialFlags = input.MaterialFlags;
            if (b.Has(ParticleBindingFlags.Remove))
                continue;

            if (b.Has(ParticleBindingFlags.NeedsBBoxUpdate))
            {
                if (!b.Has(ParticleBindingFlags.AutoUpdateBBox) || b.RecalculateBoundingBox(input.Origin))
                    b.Set(ParticleBindingFlags.NeedsBBoxUpdate, false);
            }

            b.Set(ParticleBindingFlags.Drawn, false);
            Vector origin = Update(e, b, header, input, dt);

            if (b.Has(ParticleBindingFlags.FirstFrame))
                b.Set(ParticleBindingFlags.FirstFrame, false);
            else
                Simulate(e, b, dt, origin);

            bool changed = b.DetectChanges(aabbBuffer);
            if (written < commits.Length)
            {
                commits[written++] = new ParticleCommit
                {
                    Owner = e.Owner, Channel = e.Channel, Min = b.Min, Max = b.Max, Flags = (int)b.Flags,
                    Active = b.ActiveParticles, Changed = changed ? 1 : 0,
                };
            }
        }
        return written;
    }

    // CManagedParticleEffect::Update: the sim's spawns and box, the follow, the
    // spawns linked. Answers where the executor stands after it.
    private Vector Update(Entry e, ParticleBinding b, in ParticleFrameHeader header, in ParticleFrameIn input, float dt)
    {
        var emit = new ParticleEmit
        {
            Dt = dt,
            WasDrawnPrevFrame = b.Has(ParticleBindingFlags.DrawnPrevFrame) ? 1 : 0,
            Origin = input.Origin,
            Angles = input.Angles,
            CurTime = header.CurTime,
            FrameTime = header.FrameTime,
            AlwaysSimulate = b.Has(ParticleBindingFlags.AlwaysSimulate) ? 1 : 0,
            ViewOrigin = header.ViewOrigin,
        };
        int added = sims().Emit(e.Owner, e.Channel, ref emit, _ids);

        if (b.Has(ParticleBindingFlags.AlwaysSimulate) != (emit.AlwaysSimulate != 0))
            b.Set(ParticleBindingFlags.AlwaysSimulate, emit.AlwaysSimulate != 0);
        if (emit.HasBounds != 0)
            b.SetBBox(emit.Mins, emit.Maxs, disableAutoUpdate: false);

        Vector origin = input.Origin;
        if (input.Followed != 0)
        {
            ApplyFollow(b, true, input.Matrix, input.HasBox != 0, input.Mins, input.Maxs);
            origin = input.FollowOrigin;
        }

        for (int i = 0; i < added && i < MaxEmitPerFrame; i++)
            b.AddParticle(0, _ids[i], origin);
        return origin;
    }

    // SimulateParticles: the box counter and its draw, the sim's step over the
    // list (the dead unlinked in list order, its own spawns linked after), the
    // full box when it was drawn. The executor's ShouldSimulate is always true.
    private void Simulate(Entry e, ParticleBinding b, float dt, Vector origin)
    {
        bool full = b.BeginBBoxUpdate(random.RandomInt);

        List<BoundParticle> list = b.Materials[0];
        int n = list.Count;
        var ids = new int[n];
        for (int i = 0; i < n; i++)
            ids[i] = list[i].Id;
        var alive = new byte[n];

        views.FloatMode.Sample();
        IClientParticleSims s = sims();
        int result = s.Simulate(e.Owner, e.Channel, dt, b.Has(ParticleBindingFlags.DrawnPrevFrame), ids, alive);
        if (result > 0)
        {
            for (int i = 0; i < n; i++)
            {
                if (alive[i] == 0)
                    b.RemoveParticle(ids[i]);
            }
            int spawned = s.TakeSimulateSpawns(e.Owner, e.Channel, _ids);
            for (int k = 0; k < spawned && k < MaxEmitPerFrame; k++)
                b.AddParticle(0, _ids[k], origin);
        }

        if (full)
            b.EndBBoxUpdate(origin);
    }

    private static int IndexOf(ReadOnlySpan<ParticleFrameIn> inputs, uint owner, int channel)
    {
        for (int i = 0; i < inputs.Length; i++)
        {
            if (inputs[i].Owner == owner && inputs[i].Channel == channel)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// CParticleEffectBinding::DrawModel's decisions: whether it draws, its
    /// frame time and sort choice, the frame-buffer updates before its material,
    /// the wireframe pass and the debug box, and the quads in list order (Kind -1
    /// for a particle its sim drew nothing for). Answers the quads written, or
    /// more than <paramref name="quads"/> holds (nothing written past it).
    /// </summary>
    public int DrawBegin(in ParticleDrawIn input, out ParticleDrawOut output, Span<ParticleQuadRaw> quads)
    {
        output = default;
        Entry? e = Find(input.Owner, input.Channel);
        if (e != null && e.Binding.Materials[0].Count > quads.Length)
            return e.Binding.Materials[0].Count;
        if (e == null || input.DrawParticles == 0 || input.ClientModeDraws == 0 || (input.Flags & DepthPassFlags) != 0)
            return 0;

        ParticleBinding b = e.Binding;
        b.Set(ParticleBindingFlags.Drawn, true);
        output.Active = b.ActiveParticles;
        output.Min = b.Min;
        output.Max = b.Max;
        if (b.ActiveParticles == 0)
        {
            output.Result = 1;
            return 0;
        }

        e.DrawBucket = ParticleBinding.ChooseBucketSort(random.RandomInt);
        output.FrameTime = b.DrawFrameTime(FrameCode, input.FrameTime);
        output.RefractUpdate = (e.MaterialFlags & 1) != 0 ? 1 : 0;
        output.ScreenUpdate = (e.MaterialFlags & 2) != 0 ? 1 : 0;
        output.Wireframe = input.Wireframe;
        DebugBox(b, input, ref output);
        output.Result = 2;

        List<BoundParticle> list = b.Materials[0];
        // The sim answers its quads newest first; each listed particle takes its own.
        var drawn = new ParticleQuadRaw[Math.Max(list.Count, 1) + 32];
        views.Key = input.ViewKey;
        int n = sims().Draw(e.Owner, e.Channel, drawn);
        if (n > drawn.Length)
        {
            drawn = new ParticleQuadRaw[n + 32];
            n = sims().Draw(e.Owner, e.Channel, drawn);
        }
        var byId = new Dictionary<int, int>(Math.Max(n, 0));
        for (int i = 0; i < n && i < drawn.Length; i++)
            byId.TryAdd(drawn[i].Id, i);
        for (int i = 0; i < list.Count; i++)
        {
            if (byId.TryGetValue(list[i].Id, out int q))
                quads[i] = drawn[q];
            else
                quads[i] = new ParticleQuadRaw { Id = list[i].Id, Kind = -1 };
        }
        return list.Count;
    }

    // DrawModel's !IsRetail() box: by cost (green, green to blue, blue to red,
    // red) when cl_particle_show_bbox_cost is set, else red for a box kept up to
    // date and green for one set by hand; cheap systems only on a negative cost.
    private static void DebugBox(ParticleBinding b, in ParticleDrawIn input, ref ParticleDrawOut output)
    {
        if (input.ShowBBox == 0 && input.ShowBBoxCost == 0)
            return;
        int cost = input.ShowBBoxCost;
        bool cheap = false;
        if (cost < 0)
        {
            cost = -cost;
            cheap = true;
        }
        int r, g, bl;
        bool draw = true;
        if (cost > 0)
        {
            float amount = b.ActiveParticles / (float)cost;
            if (amount < 0.5f)
            {
                if (cheap)
                    (r, g, bl) = (0, 255, 0);
                else
                    (draw, r, g, bl) = (false, 0, 0, 0);
            }
            else if (amount < 1.0f)
            {
                int blend = Math.Min(255, Math.Max(0, (int)(512.0f * (amount - 0.5f))));
                (r, g, bl) = (0, 255 - blend, blend);
            }
            else if (amount < 2.0f)
            {
                int blend = Math.Min(255, Math.Max(0, (int)(256.0f * (amount - 1.0f))));
                (r, g, bl) = (blend, 0, 255 - blend);
            }
            else
            {
                (r, g, bl) = (255, 0, 0);
            }
        }
        else if (b.Has(ParticleBindingFlags.AutoUpdateBBox))
        {
            (r, g, bl) = (255, 0, 0);
        }
        else
        {
            (r, g, bl) = (0, 255, 0);
        }
        output.BBox = draw ? 1 : 0;
        (output.BBoxR, output.BBoxG, output.BBoxB) = (r, g, bl);
    }

    /// <summary>How many quads the executor's draw lists: its binding's particles.</summary>
    public int DrawQuadCount(uint owner, int channel) => Find(owner, channel)?.Binding.Materials[0].Count ?? 0;

    /// <summary>The draw's sort keys, in list order: what the draw leaves the list in.</summary>
    public void DrawEnd(uint owner, int channel, ReadOnlySpan<float> keys)
    {
        Entry? e = Find(owner, channel);
        if (e == null || e.Binding.Materials.Count == 0)
            return;
        e.Binding.SortAfterDraw(0, keys, e.DrawBucket);
    }

    /// <summary>IncrementFrameCode: on a wrap every binding's code goes back to 0 and the manager's to 1.</summary>
    public void IncrementFrameCode()
    {
        ++FrameCode;
        if (FrameCode == 0)
        {
            foreach (Entry e in _entries)
                e.Binding.FrameCode = 0;
            FrameCode = 1;
        }
    }

    /// <summary>PostRender: each binding's drawn flag becomes last frame's, and its first frame is over.</summary>
    public void PostRender()
    {
        foreach (Entry e in _entries)
            e.Binding.PostRender();
    }

    public void Save(ReloadWriter writer)
    {
        writer.Write((int)FrameCode);
        writer.Write(_entries.Count);
        foreach (Entry e in _entries)
        {
            ParticleBinding b = e.Binding;
            writer.Write(unchecked((int)e.Owner));
            writer.Write(e.Channel);
            writer.Write(e.MaterialFlags);
            writer.Write(e.DrawBucket);
            writer.Write((int)b.Flags);
            WriteVector(writer, b.Min);
            WriteVector(writer, b.Max);
            WriteVector(writer, b.LastMin);
            WriteVector(writer, b.LastMax);
            writer.Write(b.CullRadius);
            writer.Write((int)b.FrameCode);
            writer.Write((int)b.UpdateBBoxCounter);
            foreach (float f in b.LocalSpaceTransform)
                writer.Write(f);
            writer.Write(b.Materials.Count);
            foreach (List<BoundParticle> list in b.Materials)
            {
                writer.Write(list.Count);
                foreach (BoundParticle p in list)
                {
                    writer.Write(p.Id);
                    WriteVector(writer, p.Position);
                }
            }
        }
    }

    public void Restore(ReloadReader reader)
    {
        _entries.Clear();
        FrameCode = (ushort)reader.ReadInt();
        int count = reader.ReadInt();
        Span<float> local = stackalloc float[12];
        for (int i = 0; i < count; i++)
        {
            var e = new Entry(unchecked((uint)reader.ReadInt()), reader.ReadInt())
            {
                MaterialFlags = reader.ReadInt(),
                DrawBucket = reader.ReadBool(),
            };
            var b = new ParticleBinding();
            var flags = (ParticleBindingFlags)reader.ReadInt();
            b.Min = ReadVector(reader);
            b.Max = ReadVector(reader);
            b.LastMin = ReadVector(reader);
            b.LastMax = ReadVector(reader);
            b.SetParticleCullRadius(reader.ReadFloat());
            b.FrameCode = (ushort)reader.ReadInt();
            b.UpdateBBoxCounter = (ushort)reader.ReadInt();
            for (int k = 0; k < 12; k++)
                local[k] = reader.ReadFloat();
            b.SetLocalSpaceTransform(local);
            int materials = reader.ReadInt();
            for (int m = 0; m < materials; m++)
            {
                b.Material(m);
                int n = reader.ReadInt();
                var particles = new BoundParticle[n];
                for (int k = 0; k < n; k++)
                    particles[k] = new BoundParticle(reader.ReadInt(), ReadVector(reader));
                // AddParticle links at the head: in reverse, they come back in order.
                for (int k = n - 1; k >= 0; k--)
                    b.AddParticle(m, particles[k].Id, particles[k].Position);
            }
            b.Flags = flags;
            e.Binding = b;
            _entries.Add(e);
        }
    }

    private static void WriteVector(ReloadWriter w, Vector v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static Vector ReadVector(ReloadReader r) => new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
}

/// <summary>
/// The executor managed starts its sims on: native's (INativeParticleExecutor),
/// with each executor's binding kept in <see cref="ClientParticleManager"/>,
/// which is what CManagedParticleEffect::Start and its setters did to the
/// stock binding.
/// </summary>
public sealed class BoundParticleExecutor(INativeParticleExecutor native, ClientParticleManager manager) : IClientParticleExecutor
{
    public bool Create(EntityHandle owner, int channel, string material, float cullRadius, bool alwaysSimulate)
    {
        if (!native.Create(owner, channel, material, cullRadius, alwaysSimulate))
            return false;
        manager.Add(owner.Value, channel, cullRadius, alwaysSimulate);
        return true;
    }

    public bool CreateAt(int id, string material, Vector origin, float cullRadius, bool alwaysSimulate)
    {
        if (!native.CreateAt(id, material, origin, cullRadius, alwaysSimulate))
            return false;
        manager.Add(ClientParticleEffects.NoOwner.Value, id, cullRadius, alwaysSimulate);
        return true;
    }

    public void Configure(EntityHandle owner, int channel, float cullRadius)
    {
        native.Configure(owner, channel, cullRadius);
        manager.Configure(owner.Value, channel, cullRadius);
    }

    public void Destroy(EntityHandle owner, int channel)
    {
        native.Destroy(owner, channel);
        manager.Remove(owner.Value, channel);
    }

    public void SetLights(EntityHandle owner, int channel, ReadOnlySpan<float> lights) => native.SetLights(owner, channel, lights);

    public bool MaterialVarInt(EntityHandle owner, int channel, string var, out int value)
        => native.MaterialVarInt(owner, channel, var, out value);

    public ParticleFollow SetSpace(EntityHandle owner, int channel, uint entity, int attachment, ParticleSpace flags)
    {
        ParticleFollow follow = native.SetSpace(owner, channel, entity, attachment, flags);
        manager.SetSpace(owner.Value, channel, flags, follow);
        return follow;
    }
}

/// <summary>
/// The old-style manager's bindings, for a live check that they were updated
/// and drawn and that they crossed a reload: each owner, channel, particles
/// and whether it was drawn last frame.
/// </summary>
[ConCommand("cs_cl_particle_bindings", Help = "SourceSharp: the old-style particle manager's bindings (C7 particles phase 2.3)")]
public sealed class ClientParticleBindingsCommand(ILog log, ClientParticleManager manager) : IConCommand
{
    public void Execute(string args)
    {
        foreach (string line in manager.Describe())
            log.Msg($"[SourceSharp/Particles] binding {line}\n");
        log.Msg($"[SourceSharp/Particles] {manager.Count} binding(s), frame code {manager.FrameCode}\n");
    }
}

