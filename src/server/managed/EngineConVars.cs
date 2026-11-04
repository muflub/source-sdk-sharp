using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>The engine's server tags string; <see cref="ServerTags"/> writes it.</summary>
[EngineConVar("sv_tags")]
public sealed class SvTagsConVar : StringConVar { }

/// <summary>The engine's game port.</summary>
[EngineConVar("hostport")]
public sealed class HostPortConVar : IntConVar { }

/// <summary>The engine's SourceTV port.</summary>
[EngineConVar("tv_port")]
public sealed class TvPortConVar : IntConVar { }

/// <summary>The engine's client port.</summary>
[EngineConVar("clientport")]
public sealed class ClientPortConVar : IntConVar { }

/// <summary>How far a shot bias may reshape a group, downward.</summary>
[EngineConVar("ai_shot_bias_min", Fallback = "-1")]
public sealed class AiShotBiasMinConVar : FloatConVar { }

/// <summary>How far a shot bias may reshape a group, upward.</summary>
[EngineConVar("ai_shot_bias_max", Fallback = "1")]
public sealed class AiShotBiasMaxConVar : FloatConVar { }

/// <summary>The renderer's tone-map scale, which an env_tonemap_controller blends.</summary>
[EngineConVar("mat_hdr_tonemapscale", Fallback = "1")]
public sealed class MatHdrTonemapScaleConVar : FloatConVar { }

/// <summary>The renderer's DirectX level, for how many gibs a level may keep.</summary>
[EngineConVar("mat_dxlevel", Fallback = "90")]
public sealed class MatDxLevelConVar : IntConVar { }
