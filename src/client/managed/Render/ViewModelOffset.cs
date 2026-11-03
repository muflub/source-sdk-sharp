using System;

namespace SourceSharp.Client.Render;

/// <summary>
/// The viewmodels' extra origin, stock's min mode (<c>tf_viewmodel.cpp:196-221</c>):
/// <c>tf_use_min_viewmodels</c>, or a nonempty <c>tf_viewmodels_offset_override</c>,
/// holds the weapon's viewmodel offset along the eye's forward, right and up —
/// the item's <c>min_viewmodel_offset</c> attribute, or the override. The game
/// owns the convars and the attribute; the plan only carries the answer to
/// native, with the bob it rides (<see cref="IViewModelBob"/>).
/// </summary>
public interface IViewModelOffset
{
    /// <summary>Writes the offset's x y z, along the eye's forward, right and up; false when no min mode stands.</summary>
    bool Offset(Span<float> offset);
}
