using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// THE THREE KNOBS THE LEAF-MATH FILES USED TO DECLARE THEMSELVES, server half.
///
/// <para>
/// <c>anim_3wayblend</c> and <c>aim_constraint</c> were <c>static ConVar</c>s in
/// <c>public/bone_setup.cpp</c> (:1838, :4777) and <c>mod_load_showstall</c> one
/// in <c>public/studio.cpp</c> (:69). The first two are tuning values no engine
/// code defines, so <c>plan_shim.md</c> §6's rule applies: a ConVar's default
/// and semantics are not declared natively, and their native declarations
/// became <c>CManagedConVarRef</c> reads of these. The third turned out to be
/// the engine's own name (see below).
/// </para>
///
/// <para>
/// <b>DECLARED ON BOTH SIDES.</b> Both carry <c>FCVAR_REPLICATED</c>, which
/// only means anything if both DLLs register the name — that is what a
/// replicated ConVar IS. <c>mod_load_showstall</c>, the third knob these files
/// once declared, is the engine's own, so neither side declares it
/// (convar_ownership.h); <c>studio.cpp</c> reads the engine's by name.
/// </para>
///
/// <para>
/// <b>A READ BEFORE REGISTRATION ANSWERS ZERO, not the default</b>, and that is
/// a real difference from the native declaration rather than a detail:
/// <c>CManagedConVarRef</c>'s getters answer the declared zero until the name
/// appears in the engine's registry, so between DLL load and
/// <c>ConVarRegistry.RegisterAll</c> a <c>GetBool()</c> on a default-1 knob
/// reads FALSE. <c>util_shared_library.cpp</c>'s four <c>violence_*</c> refs
/// already accept exactly this, on the same reasoning: the first read of any of
/// these happens while a model is being posed or loaded, which is long after
/// managed startup. Nothing reads them at static-init time — that is the case
/// that would be wrong, and it is the case <c>CManagedConVarRef</c> exists to
/// stop being silently wrong.
/// </para>
/// </summary>
internal static class AnimationConVarsDoc
{
}

/// <summary>
/// Server half of <c>anim_3wayblend</c>. Read by
/// <c>bone_setup.cpp</c>'s <c>Calc3WayBlendIndices</c> caller, which falls back
/// to the 2x2 blend when it is off.
/// </summary>
[ConVar("anim_3wayblend", "1", Flags = ConVarFlags.Replicated,
        Help = "Toggle the 3-way animation blending code.")]
public sealed class AnimThreeWayBlendConVar : BoolConVar { }

/// <summary>
/// Server half of <c>aim_constraint</c>. Read by <c>bone_setup.cpp</c>'s
/// <c>DoAimAtBone</c>, which does nothing at all when it is off.
/// </summary>
[ConVar("aim_constraint", "1", Flags = ConVarFlags.Replicated,
        Help = "Toggle <aimconstraint> Helper Bones")]
public sealed class AimConstraintConVar : BoolConVar { }

// mod_load_showstall is the engine's (convar_ownership.h): studio.cpp's three
// reads resolve it by name.
