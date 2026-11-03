//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Client.Ragdolls;

/// <summary>A body's dissolve begins: what its client half asks of <see cref="RagdollDissolves"/>.</summary>
public interface IRagdollDissolves
{
    /// <summary>The body began to dissolve at <paramref name="start"/>, on the game's clock. Once per body.</summary>
    void Begin(EntityHandle body, float start);

    /// <summary>The body is gone.</summary>
    void End(EntityHandle body);
}

/// <summary>Sparks for a dissolve, one burst a material: stock's m_pEmitter->AddParticle.</summary>
public interface IDissolveParticles
{
    void Emit(string material, List<SimpleParticle> particles, Vector sortOrigin);
}

/// <summary>
/// C_EntityDissolve for ENTITY_DISSOLVE_NORMAL (c_entitydissolve.cpp), on a client
/// ragdoll: ClientThink's fade to black then to nothing, Simulate's near-zero gravity,
/// and DrawModel's sparks over each hitbox.
/// </summary>
public sealed class RagdollDissolves(IClientRagdolls ragdolls, IStandaloneBlocks blocks, ClientSkeletons skeletons,
                                     IEngineEntityRender render, IPixelVisSlots view, IUniformRandom random,
                                     IDissolveParticles particles, IGameClock clock, Func<float> gravity)
    : IClientFrameThink, IRagdollDissolves, IOrderedHook
{
    /// <summary>What Simulate adds against gravity: a little more of it, so the body lifts.</summary>
    private const float Lift = 1.02f;

    private const string SparkMaterial = "effects/spark", GlowMaterial = "effects/combinemuzzle2";

    /// <summary>m_vEffectColor's default.</summary>
    private const byte EffectColor = 255;

    private readonly List<(EntityHandle Body, float Start)> _live = [];
    private readonly List<SimpleParticle> _sparks = [], _glows = [];
    private BoneMatrix3x4[] _bones = [];
    private int[] _owned = [];

    public int HookOrder => 8100;

    public int Live => _live.Count;

    public void Begin(EntityHandle body, float start)
    {
        if (_live.FindIndex(d => d.Body == body) < 0)
            _live.Add((body, start));
    }

    public void End(EntityHandle body) => _live.RemoveAll(d => d.Body == body);

    public void ClientThink(float frameTime)
    {
        float now = clock.Now;
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            (EntityHandle body, float start) = _live[i];
            float elapsed = now - start;
            Fade(body, elapsed);

            if (elapsed > RagdollDissolve.Seconds)
            {
                _live.RemoveAt(i);
                continue;
            }

            ragdolls.AddVelocity(body, new Vector(0f, 0f, Lift * gravity() * frameTime));
            Sparks(body, elapsed);
        }
    }

    /// <summary>ClientThink's colour: to black over the first second, to nothing at the end.</summary>
    private void Fade(EntityHandle body, float elapsed)
    {
        Span<byte> span = blocks.Block(body);
        if (span.Length >= EngineBlock.SizeBytes)
        {
            byte tint = (byte)((1f - RagdollDissolve.FadeIn(elapsed)) * 255f);
            ref EngineBlock block = ref MemoryMarshal.AsRef<EngineBlock>(span);
            block.Client.RenderColorR = block.Client.RenderColorG = block.Client.RenderColorB = tint;
        }

        render.SetBlending(body, translucent: true, ignoreZ: false, (int)(RagdollDissolve.ModelFadeOut(elapsed) * 255f));
    }

    /// <summary>DrawModel's particles, per hitbox.</summary>
    private void Sparks(EntityHandle body, float elapsed)
    {
        Span<byte> span = blocks.Block(body);
        if (span.Length < EngineBlock.SizeBytes)
            return;

        EngineBlock block = MemoryMarshal.Read<EngineBlock>(span);
        if (block.Head.ModelIndex <= 0 || skeletons.Models.OpenStudioHdr(block.Head.ModelIndex) is not { } hdr
            || hdr.NumHitboxSets <= block.Client.HitboxSet || hdr.NumBones <= 0)
            return;

        if (_bones.Length < hdr.NumBones)
        {
            _bones = new BoneMatrix3x4[hdr.NumBones];
            _owned = new int[hdr.NumBones];
        }

        if (ragdolls.WriteBones(body, _bones, _owned) <= 0)
            return;

        float fadeIn = RagdollDissolve.FadeIn(elapsed);
        float fade = fadeIn >= 1f ? (elapsed > RagdollDissolve.Seconds ? 0f : 1f) : fadeIn;

        // "Skew the particles in front or in back of their targets."
        Vector forward = view.View().Forward;
        Vector skew = forward * (8f - (1f - fade) * 32f);
        float spriteScale = Math.Clamp(elapsed / RagdollDissolve.Seconds, 0.75f, 1f);
        int glowCount = (int)Math.Clamp(3f * fade, 0f, 3f);

        _sparks.Clear();
        _glows.Clear();
        MstudioHitboxSet set = hdr.HitboxSet(block.Client.HitboxSet);
        Vector first = default;

        for (int h = 0; h < set.NumHitboxes; h++)
        {
            MstudioBBox box = set.Hitbox(h);
            if (box.Bone < 0 || box.Bone >= hdr.NumBones)
                continue;

            Span(box, _bones[box.Bone], out Vector center, out Vector x, out Vector y);
            if (h == 0)
                first = center;

            float xScale = x.Length * 0.75f, yScale = y.Length * 0.75f;
            Vector xDir = xScale > 0f ? x * (0.75f / xScale) : x, yDir = yScale > 0f ? y * (0.75f / yScale) : y;

            for (int j = 0; j < 2; j++)
            {
                Vector offset = xDir * random.RandomFloat(-xScale * 0.5f, xScale * 0.5f)
                              + yDir * random.RandomFloat(-yScale * 0.5f, yScale * 0.5f) + skew;
                if (random.RandomInt(0, 2) != 0)
                    continue;

                Vector velocity = new(random.RandomFloat(-4f, 4f), random.RandomFloat(-4f, 4f), random.RandomFloat(16f, 64f));
                bool last = glowCount == 0;
                _sparks.Add(new SimpleParticle
                {
                    Position = center + offset,
                    Velocity = velocity,
                    StartSize = (byte)((last ? 2f : random.RandomFloat(4f, 6f)) * spriteScale),
                    EndSize = 0,
                    DieTime = random.RandomFloat(0.4f, 0.5f) * (last ? 2f : 1f),
                    Lifetime = 0f,
                    Roll = random.RandomInt(0, 360),
                    RollDelta = last ? random.RandomFloat(-4f, 4f) : random.RandomFloat(-8f, 8f),
                    R = EffectColor, G = EffectColor, B = EffectColor,
                    StartAlpha = 255, EndAlpha = 0,
                });
            }

            for (int j = 0; j < glowCount; j++)
            {
                Vector offset = xDir * random.RandomFloat(-xScale * 0.5f, xScale * 0.5f)
                              + yDir * random.RandomFloat(-yScale * 0.5f, yScale * 0.5f) + skew;
                _glows.Add(new SimpleParticle
                {
                    Position = center + offset,
                    Velocity = new Vector(random.RandomFloat(-4f, 4f), random.RandomFloat(-4f, 4f), random.RandomFloat(16f, 64f)),
                    StartSize = (byte)(random.RandomFloat(8f, 12f) * spriteScale),
                    EndSize = 0,
                    DieTime = 0.1f,
                    Lifetime = 0f,
                    Roll = random.RandomInt(0, 360),
                    RollDelta = random.RandomFloat(-2f, 2f),
                    R = EffectColor, G = EffectColor, B = EffectColor,
                    StartAlpha = 255, EndAlpha = 0,
                });
            }
        }

        if (_sparks.Count > 0)
            particles.Emit(SparkMaterial, _sparks, first);
        if (_glows.Count > 0)
            particles.Emit(GlowMaterial, _glows, first);
    }

    /// <summary>ComputeRenderInfo, plainly: a hitbox's centre in the world and its two longest axes, half lengths.</summary>
    internal static void Span(MstudioBBox box, in BoneMatrix3x4 bone, out Vector center, out Vector x, out Vector y)
    {
        Vector min = new(box.BbMin.X, box.BbMin.Y, box.BbMin.Z), max = new(box.BbMax.X, box.BbMax.Y, box.BbMax.Z);
        Vector c = (min + max) * 0.5f, half = (max - min) * 0.5f;
        Vector col0 = new(bone.Row0.X, bone.Row1.X, bone.Row2.X), col1 = new(bone.Row0.Y, bone.Row1.Y, bone.Row2.Y),
               col2 = new(bone.Row0.Z, bone.Row1.Z, bone.Row2.Z), at = new(bone.Row0.W, bone.Row1.W, bone.Row2.W);
        center = at + col0 * c.X + col1 * c.Y + col2 * c.Z;

        Vector a = col0 * half.X, b = col1 * half.Y, d = col2 * half.Z;
        if (a.Length < b.Length)
            (a, b) = (b, a);
        if (b.Length < d.Length)
            (b, d) = (d, b);
        if (a.Length < b.Length)
            (a, b) = (b, a);
        x = a;
        y = b;
    }
}
