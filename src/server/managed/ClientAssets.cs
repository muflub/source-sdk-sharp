using SourceSharp.Abi;

namespace SourceSharp.Server;

//=============================================================================
// WHAT THE CLIENT NEEDS TO EXIST BEFORE ANYONE PLAYS.
//
// This was game/server/client.cpp's `ClientPrecache()` -- twenty-three asset
// names in one flat list at the bottom of a 1,800-line file, called once from
// CWorld::Precache. Which assets a game needs is a decision about what the game
// IS, so it belongs here rather than in C++ (CLAUDE.md).
//
// WHY IT IS FOUR CLASSES AND NOT ONE LIST.
//
// Stock's list was flat and its four groups were separated by blank lines and
// two comments. Flattening them again would lose the only thing that says WHY
// each name is there, and the reason matters more than usual for a precache:
// the failure mode of a wrong or missing entry is a missing effect at RUNTIME
// -- never a build error -- so the next person to touch this list has nothing
// to reason from but the grouping. Each class below carries its group's reason
// and what breaks without it.
//
// AND WHY [Precache] AND NOT A METHOD. The attribute is discovered by
// reflection over every loaded assembly that could carry one
// (PrecacheRegistry.PrecacheAll, driven from ServerModule's LevelInit, exactly
// where Flames.Precache runs). A method has to be REMEMBERED by a caller, and
// this project has already paid twice for that: CBaseTempEntity::PrecacheTempEnts
// walked a singleton list that an eviction silently shortened, and
// CEnvSpark::Precache's "DoSpark" went with its file and every audible spark
// played silence for months (behaviour ledger row S7).
//
// THE TWO KINDS ARE NOT INTERCHANGEABLE, and stock used two different calls:
//
//   PrecacheKind.Model   -> Engine.PrecacheModel -> SourceSharp_PrecacheModel
//                           -> Precache_Model, which is EXACTLY what stock's
//                           `Precache_Model( "cable/cable.vmt" )` called. A
//                           .vmt precached this way is a sprite/material MODEL
//                           and gets an index in the networked model string
//                           table, which is how a beam can send four bytes
//                           instead of a path.
//   PrecacheKind.Sound   -> Engine.PrecacheSound -> SourceSharp_PrecacheSound,
//                           which dispatches on the NAME SHAPE: a name with no
//                           slash is a soundscript entry and goes to
//                           GameSound_PrecacheScript -- which is the call stock
//                           made for all sixteen of these. None of the sixteen
//                           contains a slash, and ClientAssetsTests asserts
//                           that rather than assuming it.
//
// `Hud.Hint` sat inside `#ifndef HL2MP` in stock. HL2MP is defined by no vpc in
// this tree (`grep -c 'DHL2MP' src/_vpc_/ninja/sdk_csharp_debug.ninja` = 0), so
// the arm this build takes precaches it, and it is here unguarded for that
// reason rather than by oversight.
//=============================================================================

/// <summary>
/// The seven materials a rope, a cable and a beam are drawn with.
///
/// <para>
/// Stock: <c>ClientPrecache()</c>'s "Precache cable textures" block,
/// <c>client.cpp:456</c>. Every one of them is a <c>.vmt</c> precached as a
/// MODEL, which is what gives it an index in the networked model string table
/// — a rope or a beam networks that index rather than the path.
/// </para>
///
/// <para>
/// Without them a rope draws as the purple-and-black error checkerboard and a
/// beam draws as nothing at all, on the client only, with no server-side
/// symptom whatsoever.
/// </para>
/// </summary>
[Precache(PrecacheKind.Model, "cable/cable.vmt")]
[Precache(PrecacheKind.Model, "cable/cable_lit.vmt")]
[Precache(PrecacheKind.Model, "cable/chain.vmt")]
[Precache(PrecacheKind.Model, "cable/rope.vmt")]
[Precache(PrecacheKind.Model, "sprites/blueglow1.vmt")]
[Precache(PrecacheKind.Model, "sprites/purpleglow1.vmt")]
[Precache(PrecacheKind.Model, "sprites/purplelaser1.vmt")]
public static class RopeAndBeamMaterials
{
}

/// <summary>
/// The three sounds the player's own body makes, and the hint tone.
///
/// <para>
/// Stock: <c>ClientPrecache()</c>, <c>client.cpp:466-469</c>.
/// <c>Hud.Hint</c> was inside <c>#ifndef HL2MP</c> there; HL2MP is defined by no
/// vpc in this tree, so this build precached it and still does.
/// </para>
///
/// <para>
/// <c>Player.FallDamage</c> is the one with a live customer in this mod: the
/// managed player takes fall damage. A precache that is not done makes the
/// engine warn once and play silence, so the damage still lands and only the
/// noise goes missing — the exact shape that hid <c>DoSpark</c> for months.
/// </para>
/// </summary>
[Precache(PrecacheKind.Sound, "Hud.Hint")]
[Precache(PrecacheKind.Sound, "Player.FallDamage")]
[Precache(PrecacheKind.Sound, "Player.Swim")]
public static class PlayerFeedbackSounds
{
}

/// <summary>
/// The player's water noises (<c>CBasePlayer::Precache</c>, player.cpp:5217):
/// the gasp after drowning, its continuation, the wade in and out, and the
/// client's underwater ambience. SsPlayer's WaterMove plays the first and
/// the wade.
/// </summary>
[Precache(PrecacheKind.Sound, "Player.DrownStart")]
[Precache(PrecacheKind.Sound, "Player.DrownContinue")]
[Precache(PrecacheKind.Sound, "Player.Wade")]
[Precache(PrecacheKind.Sound, "Player.AmbientUnderWater")]
public static class PlayerWaterSounds
{
}

/// <summary>
/// The +USE noises (<c>CHL2_Player::Precache</c>, hl2_player.cpp:436, :441):
/// a press that found something, and one that found nothing. PlayerUse plays them.
/// </summary>
[Precache(PrecacheKind.Sound, "HL2Player.Use")]
[Precache(PrecacheKind.Sound, "HL2Player.UseDeny")]
public static class PlayerUseSounds
{
}

/// <summary>
/// The sprint's two noises (<c>CHL2_Player::Precache</c>, hl2_player.cpp:434):
/// the start, and the click when the suit has too little power for one. The
/// movement loop's HL2 player plays them (GameMovement.Hl2Player).
/// </summary>
[Precache(PrecacheKind.Sound, "HL2Player.SprintNoPower")]
[Precache(PrecacheKind.Sound, "HL2Player.SprintStart")]
public static class SprintSounds
{
}

/// <summary>
/// The five noises the weapon-selection HUD makes.
///
/// <para>
/// Stock: <c>ClientPrecache()</c>'s "General HUD sounds" block,
/// <c>client.cpp:472-477</c>. They are emitted by the CLIENT's HUD, and
/// precached on the SERVER because that is what builds the networked sound
/// string table — which is why a list of client assets lived in a server file
/// and why moving it does not move which side plays them.
/// </para>
/// </summary>
[Precache(PrecacheKind.Sound, "Player.PickupWeapon")]
[Precache(PrecacheKind.Sound, "Player.DenyWeaponSelection")]
[Precache(PrecacheKind.Sound, "Player.WeaponSelected")]
[Precache(PrecacheKind.Sound, "Player.WeaponSelectionClose")]
[Precache(PrecacheKind.Sound, "Player.WeaponSelectionMoveSlot")]
public static class WeaponSelectionSounds
{
}

/// <summary>
/// The eight impact noises the legacy temp-entity system plays when a gib or a
/// shell casing hits something.
///
/// <para>
/// Stock: <c>ClientPrecache()</c>'s "General legacy temp ents sounds" block,
/// <c>client.cpp:480-487</c>. The customer is
/// <c>game/client/c_te_legacytempents.cpp</c>, which IS compiled into this
/// build's client — so this is not a group that can be retired with the SDK
/// hierarchy.
/// </para>
///
/// <para>
/// The surface a gib bounces off picks which of the eight plays. Losing one
/// means one material silently stops making a noise while the other seven still
/// do, which reads as an audio mix problem rather than as a missing precache.
/// </para>
/// </summary>
[Precache(PrecacheKind.Sound, "Bounce.Glass")]
[Precache(PrecacheKind.Sound, "Bounce.Metal")]
[Precache(PrecacheKind.Sound, "Bounce.Flesh")]
[Precache(PrecacheKind.Sound, "Bounce.Wood")]
[Precache(PrecacheKind.Sound, "Bounce.Shrapnel")]
[Precache(PrecacheKind.Sound, "Bounce.ShotgunShell")]
[Precache(PrecacheKind.Sound, "Bounce.Shell")]
[Precache(PrecacheKind.Sound, "Bounce.Concrete")]
public static class LegacyTempEntBounceSounds
{
}
