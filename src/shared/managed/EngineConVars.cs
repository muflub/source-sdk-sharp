using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>Whether a single-player game simulates on alternate ticks.</summary>
[EngineConVar("sv_alternateticks")]
public sealed class SvAlternateTicksConVar : BoolConVar { }

/// <summary>Whether cheats are on. Read wherever a cheat is gated; missing means off.</summary>
[EngineConVar("sv_cheats")]
public sealed class SvCheatsConVar : BoolConVar { }

/// <summary>The engine's developer level.</summary>
[EngineConVar("developer")]
public sealed class DeveloperConVar : IntConVar { }

/// <summary>Whether human blood shows; on until the engine says otherwise.</summary>
[EngineConVar("violence_hblood", Fallback = "1")]
public sealed class ViolenceHBloodConVar : BoolConVar { }

/// <summary>Whether alien blood shows; on until the engine says otherwise.</summary>
[EngineConVar("violence_ablood", Fallback = "1")]
public sealed class ViolenceABloodConVar : BoolConVar { }

/// <summary>Whether human gibs show; on until the engine says otherwise.</summary>
[EngineConVar("violence_hgibs", Fallback = "1")]
public sealed class ViolenceHGibsConVar : BoolConVar { }

/// <summary>Whether alien gibs show; on until the engine says otherwise.</summary>
[EngineConVar("violence_agibs", Fallback = "1")]
public sealed class ViolenceAGibsConVar : BoolConVar { }

/// <summary>The engine's closecaption switch.</summary>
[EngineConVar("closecaption")]
public sealed class CloseCaptionConVar : BoolConVar { }

/// <summary>The server's name.</summary>
[EngineConVar("hostname")]
public sealed class HostnameConVar : StringConVar { }

/// <summary>The engine's floor on a client's update rate.</summary>
[EngineConVar("sv_minupdaterate")]
public sealed class SvMinUpdateRateConVar : FloatConVar { }

/// <summary>The engine's ceiling on a client's update rate.</summary>
[EngineConVar("sv_maxupdaterate")]
public sealed class SvMaxUpdateRateConVar : FloatConVar { }

/// <summary>The engine's floor on a client's interp ratio; -1 means no bound.</summary>
[EngineConVar("sv_client_min_interp_ratio", Fallback = "-1")]
public sealed class SvClientMinInterpRatioConVar : FloatConVar { }

/// <summary>The engine's ceiling on a client's interp ratio.</summary>
[EngineConVar("sv_client_max_interp_ratio", Fallback = "5")]
public sealed class SvClientMaxInterpRatioConVar : FloatConVar { }
