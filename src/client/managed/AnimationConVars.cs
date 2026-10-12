using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// THE THREE KNOBS THE LEAF-MATH FILES USED TO DECLARE THEMSELVES, client half.
///
/// <para>
/// The whole argument is in <c>SourceSharp.Server.AnimationConVarsDoc</c> and
/// is not repeated: these are the other half of three names that this build
/// registers from BOTH DLLs, two of them because <c>FCVAR_REPLICATED</c> means
/// nothing otherwise, and the third because
/// <c>sourcesharp/public/studio.cpp</c> is a row in both <c>_csharp.vpc</c>
/// file lists and so declared it twice already.
/// </para>
///
/// <para>
/// The client is where two of the three actually bite. <c>bone_setup.cpp</c>'s
/// blend and aim-constraint code runs on both sides — the server poses a
/// skeleton for attachments and hitbox traces — but the pixels are here.
/// </para>
/// </summary>
internal static class AnimationConVarsDoc
{
}

/// <summary>Client half of <c>anim_3wayblend</c>.</summary>
[ConVar("anim_3wayblend", "1", Flags = ConVarFlags.Replicated,
        Help = "Toggle the 3-way animation blending code.")]
public sealed class ClAnimThreeWayBlendConVar : BoolConVar { }

/// <summary>Client half of <c>aim_constraint</c>.</summary>
[ConVar("aim_constraint", "1", Flags = ConVarFlags.Replicated,
        Help = "Toggle <aimconstraint> Helper Bones")]
public sealed class ClAimConstraintConVar : BoolConVar { }

// mod_load_showstall is the engine's (convar_ownership.h); see the server's copy.
