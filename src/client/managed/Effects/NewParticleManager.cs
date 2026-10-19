//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Diagnostics;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// particlemgr.cpp's <c>CParticleMgr::UpdateNewEffects</c> for the new-style
/// effects (C7 particles phase 3): one <see cref="NewParticleEffect"/> per
/// native shell (shell_new_particles_client.cpp), and the frame that updates
/// them. The particle library steps, bounds and renders the collections
/// (<see cref="IParticleCollections"/>); this decides.
/// </summary>
/// <remarks>
/// The frame is UpdateNewEffects', step for step:
/// - the library's last simulation time set to the frame's;
/// - the sim list (BuildParticleSimList), then the retirement pass;
/// - each listed effect's step (ProcessPSystem): the first box, the drawn
///   flag cleared, the seconds to simulate, all stepped in one library batch;
/// - DetectChanges for each listed effect;
/// - the removals UpdateAllEffects makes after it.
/// The effects' Update is empty in this build (no owner particle property),
/// so nothing runs before the list. A retirement's replacement is carried out
/// by native after the frame and steps from the next one (stock replaced
/// mid-frame); retirement is a cheat-only setting, off by default.
/// </remarks>
public sealed class NewParticleManager(IParticleCollections library) : IClientNewParticles, IReloadParticipant
{
    private readonly Dictionary<uint, NewParticleEffect> _effects = [];
    private readonly Dictionary<ulong, int> _retireChecked = [];
    private readonly List<int> _listed = [];
    private readonly List<int> _retire = [];
    private readonly HashSet<uint> _seen = [];
    private readonly List<NewParticleCommit> _commits = [];
    private ParticleSimEntry[] _entries = new ParticleSimEntry[64];
    private ParticleSimResult[] _results = new ParticleSimResult[64];
    private NewParticleRetireIn[] _retireIn = new NewParticleRetireIn[64];

    public int Count => _effects.Count;

    /// <summary>The model for a live effect, or null.</summary>
    public NewParticleEffect? Effect(uint id) => _effects.GetValueOrDefault(id);

    /// <summary>One line per effect: id and flags.</summary>
    public IEnumerable<string> Describe()
    {
        foreach ((uint id, NewParticleEffect e) in _effects)
            yield return $"{id} flags {(int)e.Flags}";
    }

    private static void Grow<T>(ref T[] array, int count)
    {
        if (array.Length < count)
            Array.Resize(ref array, Math.Max(count, array.Length * 2));
    }

    /// <summary>
    /// UpdateNewEffects over the effects native listed (newest first). An
    /// effect native no longer lists went with its shell and is dropped first.
    /// Answers the commits written.
    /// </summary>
    public int Frame(in NewParticleFrameHeader header, ReadOnlySpan<NewParticleIn> effects, Span<NewParticleCommit> commits)
    {
        _commits.Clear();
        _seen.Clear();
        foreach (ref readonly NewParticleIn fx in effects)
            _seen.Add(fx.Id);
        foreach (uint gone in _effects.Keys.Where(id => !_seen.Contains(id)).ToList())
            _effects.Remove(gone);

        // AddEffect's render group, and Release's removal test (at the frame
        // after it, when nothing has stepped since).
        foreach (ref readonly NewParticleIn fx in effects)
        {
            if (!_effects.TryGetValue(fx.Id, out NewParticleEffect? e))
            {
                _effects[fx.Id] = e = new NewParticleEffect();
                if ((fx.Flags & NewParticleInFlags.ViewModel) != 0)
                    Commit(fx.Id, NewParticleActions.ViewGroup);
            }
            if ((fx.Flags & NewParticleInFlags.Released) != 0)
                e.Release(0, (fx.Flags & NewParticleInFlags.Allocated) != 0, fx.Finished != 0);
        }

        float dt = header.Dt * header.Timescale;
        library.SetLastSimulationTime(header.CurTime);
        long started = Stopwatch.GetTimestamp();   // BeginSimulateParticles

        // BuildParticleSimList.
        _listed.Clear();
        Grow(ref _entries, effects.Length);
        Grow(ref _results, effects.Length);
        for (int i = 0; i < effects.Length; i++)
        {
            ref readonly NewParticleIn fx = ref effects[i];
            (bool listed, bool boundsOnly) = _effects[fx.Id].SimList(header.CurTime, fx.NextSleepTime, fx.Active, fx.HasMoved != 0);
            if (!listed)
                continue;
            _entries[_listed.Count] = new ParticleSimEntry { Collection = fx.Collection, BoundsOnly = boundsOnly ? 1 : 0 };
            _listed.Add(i);
        }

        Retire(header, effects);

        // ProcessPSystem, each listed effect: the first box, then its step.
        for (int n = 0; n < _listed.Count; n++)
        {
            ref readonly NewParticleIn fx = ref effects[_listed[n]];
            NewParticleEffect e = _effects[fx.Id];
            bool recalculated = false;
            if (e.FirstBoxRecalculates)
                recalculated = RecalculateBoundingBox(fx);
            e.FirstBox(recalculated);
            _entries[n].Dt = e.Step(dt);
        }
        if (_listed.Count > 0)
            library.SimulateBatch(_entries.AsSpan(0, _listed.Count), _results.AsSpan(0, _listed.Count));

        // The step's end, then DetectChanges, then the removals.
        for (int n = 0; n < _listed.Count; n++)
        {
            ref readonly NewParticleIn fx = ref effects[_listed[n]];
            NewParticleEffect e = _effects[fx.Id];
            ref readonly ParticleSimResult r = ref _results[n];
            e.Finished(r.Finished != 0);
            bool changed = e.DetectChanges(r.Min, r.Max, header.AabbBuffer, (fx.Flags & NewParticleInFlags.RenderHandle) != 0);
            Commit(fx.Id, changed ? NewParticleActions.Changed : NewParticleActions.None);
        }
        // EndSimulateParticles: the library keeps the frame's profile when it
        // was a spike (cl_dump_particle_stats writes what it kept).
        double seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        library.CommitProfileInformation(seconds > 0.001 * header.SpikeThresholdMs);

        foreach (ref readonly NewParticleIn fx in effects)
        {
            if (_effects[fx.Id].Has(NewParticleFlags.Remove))
            {
                Commit(fx.Id, NewParticleActions.Remove);
                _effects.Remove(fx.Id);
            }
        }

        int written = Math.Min(_commits.Count, commits.Length);
        for (int i = 0; i < written; i++)
            commits[i] = _commits[i];
        return written;
    }

    private void Commit(uint id, NewParticleActions actions)
    {
        if (actions != NewParticleActions.None)
            _commits.Add(new NewParticleCommit { Id = id, Actions = actions });
    }

    // RecalculateBoundingBox: the library bloats by the control points; an
    // inside-out box collapses to the sort origin.
    private bool RecalculateBoundingBox(in NewParticleIn fx)
    {
        library.BloatBounds(fx.Collection);
        library.Bounds(fx.Collection, out Vector min, out Vector max);
        bool ok = NewParticleEffect.RecalculateBoundingBox(ref min, ref max, fx.SortOrigin);
        if (!ok)
            library.SetBounds(fx.Collection, min, max);
        return ok;
    }

    // EarlyRetireParticleSystems over the listed effects. Stock rebuilt the
    // list after a pass; the replacements join next frame here, so the list
    // it would rebuild is the one it has.
    private void Retire(in NewParticleFrameHeader header, ReadOnlySpan<NewParticleIn> effects)
    {
        if (header.RetireCost == 0.0f || _listed.Count == 0)
            return;
        Grow(ref _retireIn, _listed.Count);
        for (int n = 0; n < _listed.Count; n++)
        {
            ref readonly NewParticleIn fx = ref effects[_listed[n]];
            _retireIn[n] = new NewParticleRetireIn
            {
                Definition = fx.Definition,
                CullRadius = fx.CullRadius,
                CullFillCost = fx.CullFillCost,
                CullPoint = fx.CullPoint,
                HasReplacement = (fx.Flags & NewParticleInFlags.HasReplacement) != 0,
                FirstFrame = _effects[fx.Id].Has(NewParticleFlags.FirstFrame),
            };
        }
        var view = new NewParticleRetireView
        {
            X = header.ViewX,
            Y = header.ViewY,
            Width = header.ViewWidth,
            Height = header.ViewHeight,
            Fov = header.ViewFov,
            Origin = header.ViewOrigin,
            WorldToScreen = header.WorldToScreen,
        };
        _retire.Clear();
        NewParticleEffect.EarlyRetire(_retireIn.AsSpan(0, _listed.Count), view, header.RetireCost, header.FrameCount, _retireChecked, _retire);
        foreach (int n in _retire)
            Commit(effects[_listed[n]].Id, NewParticleActions.Replace);
    }

    /// <summary>CNewParticleEffect::DrawModel's decisions for one draw, and its debug box.</summary>
    public NewParticleDrawOutRaw Draw(in NewParticleDrawInRaw raw)
    {
        var d = new NewParticleDrawIn
        {
            Flags = raw.Flags,
            DrawParticles = raw.DrawParticles != 0,
            ClientModeDraws = raw.ClientModeDraws != 0,
            RenderSystems = raw.RenderSystems != 0,
            Camera = raw.Camera,
            Min = raw.Min,
            Max = raw.Max,
            MaxDrawDistance = raw.MaxDrawDistance,
            NoDrawTimeToGoToSleep = raw.NoDrawTimeToGoToSleep,
            LastSimulationTime = raw.LastSimulationTime,
            NextSleepTime = raw.NextSleepTime,
            SkipRenderControlPoint = raw.SkipRenderControlPoint,
            HighestControlPoint = raw.HighestControlPoint,
            CameraObject = raw.CameraObject,
            SkipObject = raw.SkipObject,
            ThirdPerson = raw.ThirdPerson != 0,
            LocalPlayer = raw.LocalPlayer,
            ObserverMode = raw.ObserverMode,
            ObserverTarget = raw.ObserverTarget,
        };
        NewParticleDraw draw = NewParticleEffect.Draw(d);
        var output = new NewParticleDrawOutRaw
        {
            Action = (int)draw.Action,
            SetSleep = draw.Reason == NewParticleDrawReason.Distance ? 1 : 0,
            NextSleepTime = draw.NextSleepTime,
        };

        // DebugDrawBbox: at the distance cull (culled) and after a draw.
        bool culled = draw.Reason == NewParticleDrawReason.Distance;
        if ((culled || draw.Reason == NewParticleDrawReason.Drawn) && (raw.ShowBBox != 0 || raw.ShowBBoxCost != 0))
        {
            bool auto = _effects.TryGetValue(raw.Id, out NewParticleEffect? e) && e.Has(NewParticleFlags.AutoUpdateBBox);
            (bool box, int r, int g, int b) = NewParticleEffect.DebugBox(culled, raw.ShowBBoxCost, raw.Active, auto);
            output.BBox = box ? 1 : 0;
            output.BBoxR = r;
            output.BBoxG = g;
            output.BBoxB = b;
        }
        return output;
    }

    public void Save(ReloadWriter writer)
    {
        writer.Write(_effects.Count);
        foreach ((uint id, NewParticleEffect e) in _effects)
        {
            writer.Write(unchecked((int)id));
            writer.Write((int)e.Flags);
            WriteVector(writer, e.LastMin);
            WriteVector(writer, e.LastMax);
        }
        writer.Write(_retireChecked.Count);
        foreach ((ulong def, int frame) in _retireChecked)
        {
            writer.Write(unchecked((int)def));
            writer.Write(unchecked((int)(def >> 32)));
            writer.Write(frame);
        }
    }

    public void Restore(ReloadReader reader)
    {
        _effects.Clear();
        _retireChecked.Clear();
        int count = reader.ReadInt();
        for (int i = 0; i < count; i++)
        {
            uint id = unchecked((uint)reader.ReadInt());
            var e = new NewParticleEffect { Flags = (NewParticleFlags)reader.ReadInt() };
            e.LastMin = ReadVector(reader);
            e.LastMax = ReadVector(reader);
            _effects[id] = e;
        }
        int defs = reader.ReadInt();
        for (int i = 0; i < defs; i++)
        {
            ulong lo = unchecked((uint)reader.ReadInt()), hi = unchecked((uint)reader.ReadInt());
            _retireChecked[lo | (hi << 32)] = reader.ReadInt();
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
