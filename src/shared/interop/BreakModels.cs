//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using System;
using System.Collections.Generic;

namespace SourceSharp.Abi;

/// <summary>
/// The pieces a breakable model turns into, answered in C#. What
/// <c>SourceSharp_ModelBreakPieceCount</c> and <c>SourceSharp_ModelBreakPiece</c>
/// (shell_breakpieces.cpp, since deleted) answered natively, with the same
/// answers: 588 of 588 pieces on dm_lockdown, field for field.
///
/// <para>
/// <b>ONE PARSE PER MODEL PER LEVEL.</b> The native pair re-parsed the model's
/// whole "break" list for the count and again for every piece, so a model of N
/// pieces cost N + 1 parses per break and per precache walk. Here the blocks are
/// parsed once and cached by model name until level shutdown (each side clears
/// its own with its <see cref="ModelDescriptors"/>).
/// </para>
///
/// <para>
/// <b>ONE INSTANCE PER SIDE</b> (plan_abi_hotreload D8): each side's engine owns
/// its own (<c>EngineSide.Breaks</c>), over that side's model info, precache and
/// descriptors. It used to be a static class whose two tables were chosen by
/// the thread's active side, so two game instances in one process shared them.
/// </para>
///
/// <para>
/// <b>WHAT IS NOT CACHED, AND WHY.</b> The block's KEYS are cached; the answer is
/// resolved per call. Two inputs change between calls: the caller's defaults,
/// and whether the piece's model is precached yet. A piece asked about during
/// the level-load precache walk has no model index, so it gets its raw offset,
/// exactly as the native slot's <c>placementOrigin</c> read did; the same piece
/// asked at the break, when everything is loaded, gets the corrected one.
/// Caching the resolved piece would freeze the first answer.
/// </para>
/// </summary>
public sealed class BreakModels
{
    /// <summary>The attachment both the parent and the piece are placed by.</summary>
    public const string PlacementAttachment = "placementOrigin";

    private static readonly IReadOnlyList<IReadOnlyList<BreakKey>> None = Array.Empty<IReadOnlyList<BreakKey>>();

    private readonly Dictionary<string, IReadOnlyList<IReadOnlyList<BreakKey>>> _table =
        new(StringComparer.OrdinalIgnoreCase);
    private int _parses;

    // The side's engine reads: a model's index by name (-1 when it has none),
    // the precache that gives it one, and its collision text by index.
    private readonly Func<string, int> _modelIndex;
    private readonly Func<string, int>? _precache;
    private readonly Func<int, string?> _collisionText;

    // The side's descriptors, for the placement attachments.
    private readonly ModelDescriptors _descriptors;

    // Where a model with no pieces to read is said (statics to zero).
    private readonly ILog _log;

    /// <summary>
    /// The same over the reads given: what the side's constructor passes, and
    /// what a fact passes instead of an engine.
    /// </summary>
    internal BreakModels(Func<string, int> modelIndex, Func<string, int>? precache,
                         Func<int, string?> collisionText, ModelDescriptors descriptors, ILog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _modelIndex = modelIndex ?? throw new ArgumentNullException(nameof(modelIndex));
        _precache = precache;
        _collisionText = collisionText ?? throw new ArgumentNullException(nameof(collisionText));
        _descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
    }

    /// <summary>How many collision texts have been parsed since the last <see cref="Clear"/>.</summary>
    public int Parses => _parses;

    /// <summary>Forget everything. Called from this side's level-shutdown crossing.</summary>
    public void Clear()
    {
        _table.Clear();
        _parses = 0;
    }

    /// <summary>
    /// How many pieces <paramref name="model"/> breaks into. Precaches the model
    /// if it has no index yet, as the native count did, and answers 0 when it
    /// cannot be loaded or has no collision text.
    /// </summary>
    public int Count(string model) => BlocksFor(model).Count;

    /// <summary>
    /// One piece, resolved against the caller's defaults. False when the model
    /// has no such piece.
    /// </summary>
    public bool PieceAt(string model, int index, float defaultBurstScale,
                               CollisionGroup defaultCollisionGroup,
                               out string pieceModel, out BreakPiece piece)
    {
        pieceModel = string.Empty;
        piece = default;

        if (index < 0)
            return false;

        IReadOnlyList<IReadOnlyList<BreakKey>> blocks = BlocksFor(model);

        if (index >= blocks.Count)
            return false;

        BreakModelFields f = BreakModelText.Resolve(blocks[index], defaultBurstScale, defaultCollisionGroup);

        pieceModel = f.ModelName;
        piece = new BreakPiece
        {
            Offset = LocalOffset(f.Offset, model, f.ModelName),
            Velocity = f.Velocity,
            Health = f.Health,
            FadeTime = f.FadeTime,
            FadeMinDist = f.FadeMinDist,
            FadeMaxDist = f.FadeMaxDist,
            BurstScale = f.BurstScale,
            CollisionGroup = (int)f.CollisionGroup,
            IsRagdoll = f.IsRagdoll ? 1 : 0,
            IsMotionDisabled = f.IsMotionDisabled ? 1 : 0,
            MultiplayerBreakMode = f.MultiplayerBreakMode,
        };
        return true;
    }

    /// <summary>
    /// The placement correction for one piece, from what the two models have:
    /// the parent's placementOrigin, or, when the piece has one too, the
    /// piece's minus the parent's. (0,0,0) is a real attachment position, which
    /// is why "has one" is a flag and not a sentinel vector.
    /// </summary>
    public static Vector PlacementOrigin(bool parentHas, Vector parent, bool pieceHas, Vector piece)
    {
        Vector parentOrigin = parentHas ? parent : Vector.Zero;

        return pieceHas ? piece - parentOrigin : parentOrigin;
    }

    /// <summary>
    /// Where the piece sits in the breaking prop's own frame: the block's
    /// offset less the placement correction. The caller rotates it by the
    /// prop's angles and adds the prop's origin.
    /// </summary>
    public static Vector LocalOffset(Vector blockOffset, bool parentHas, Vector parent,
                                     bool pieceHas, Vector piece)
        => blockOffset - PlacementOrigin(parentHas, parent, pieceHas, piece);

    // Each model's placementOrigin, read from its descriptor. A model that is
    // not precached has no studio data to read, so it has no placement -- the
    // model index is checked FIRST, so no descriptor is fetched (and cached
    // empty) for a model that is not loaded yet.
    private Vector LocalOffset(Vector blockOffset, string parentModel, string pieceModel)
    {
        bool parentHas = TryPlacement(parentModel, out Vector parent);
        bool pieceHas = TryPlacement(pieceModel, out Vector piece);

        return LocalOffset(blockOffset, parentHas, parent, pieceHas, piece);
    }

    private bool TryPlacement(string model, out Vector origin)
    {
        origin = Vector.Zero;

        if (model.Length == 0 || _modelIndex(model) <= 0)
            return false;

        return _descriptors.ForModel(model).TryAttachmentOrigin(PlacementAttachment, out origin);
    }

    private IReadOnlyList<IReadOnlyList<BreakKey>> BlocksFor(string model)
    {
        if (string.IsNullOrEmpty(model))
            return None;

        if (_table.TryGetValue(model, out IReadOnlyList<IReadOnlyList<BreakKey>>? cached))
            return cached;

        int index = _modelIndex(model);

        if (index == -1)
        {
            // SourceSharp_BreakModelIndex: a model nobody precached yet is
            // precached here, because a prop's break list is asked for before
            // anything else has needed the model.
            index = _precache is null ? -1 : _precache(model);

            if (index == -1)
            {
                _log.Warning($"[SourceSharp] cannot read break pieces: no model '{model}'\n");
                return None;
            }
        }

        _parses++;
        IReadOnlyList<IReadOnlyList<BreakKey>> blocks =
            BreakModelText.Blocks(_collisionText(index));

        _table[model] = blocks;
        return blocks;
    }
}
