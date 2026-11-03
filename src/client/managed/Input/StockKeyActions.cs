//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//


namespace SourceSharp.Client.Input;

// The core game's key actions, one class each, as its convars are: what stock HL2MP lists in
// scripts/kb_act.lst (a row of the keyboard page) and binds in scripts/kb_def.lst (a fresh
// player's keys). KeyActionsOracleTests holds them to both. A mod that declares any replaces
// these: see KeyActions.

// #Valve_Movement_Title

[KeyAction("+forward", "#Valve_Move_Forward", Section = "#Valve_Movement_Title", Order = 10, Keys = ["w", "UPARROW"])]
public sealed class MoveForwardKey;

[KeyAction("+back", "#Valve_Move_Back", Section = "#Valve_Movement_Title", Order = 20, Keys = ["s", "DOWNARROW"])]
public sealed class MoveBackKey;

[KeyAction("+moveleft", "#Valve_Move_Left", Section = "#Valve_Movement_Title", Order = 30, Keys = ["a", ","])]
public sealed class MoveLeftKey;

[KeyAction("+moveright", "#Valve_Move_Right", Section = "#Valve_Movement_Title", Order = 40, Keys = ["d", "."])]
public sealed class MoveRightKey;

[KeyAction("+speed", "#Valve_Sprint", Section = "#Valve_Movement_Title", Order = 50, Keys = ["SHIFT"])]
public sealed class SprintKey;

[KeyAction("+jump", "#Valve_Jump", Section = "#Valve_Movement_Title", Order = 60, Keys = ["SPACE"])]
public sealed class JumpKey;

[KeyAction("+duck", "#Valve_Duck", Section = "#Valve_Movement_Title", Order = 70, Keys = ["CTRL"])]
public sealed class DuckKey;

// #Valve_Combat_Title

[KeyAction("+attack", "#Valve_Primary_Attack", Section = "#Valve_Combat_Title", Order = 80, Keys = ["MOUSE1", "ENTER"])]
public sealed class PrimaryAttackKey;

[KeyAction("+attack2", "#Valve_Secondary_Attack", Section = "#Valve_Combat_Title", Order = 90, Keys = ["MOUSE2", "\\\\"])]
public sealed class SecondaryAttackKey;

[KeyAction("+reload", "#Valve_Reload_Weapon", Section = "#Valve_Combat_Title", Order = 100, Keys = ["r"])]
public sealed class ReloadWeaponKey;

[KeyAction("+use", "#Valve_Use_Items", Section = "#Valve_Combat_Title", Order = 110, Keys = ["e"])]
public sealed class UseItemsKey;

[KeyAction("phys_swap", "#Valve_Gravity_Gun", Section = "#Valve_Combat_Title", Order = 120)]
public sealed class GravityGunKey;

[KeyAction("impulse 100", "#Valve_Flashlight", Section = "#Valve_Combat_Title", Order = 130, Keys = ["f"])]
public sealed class FlashlightKey;

[KeyAction("+zoom", "#Valve_Suit_Zoom", Section = "#Valve_Combat_Title", Order = 140, Keys = ["z"])]
public sealed class SuitZoomKey;

[KeyAction("slot1", "#Valve_Weapon_Category_1", Section = "#Valve_Combat_Title", Order = 150, Keys = ["1"])]
public sealed class WeaponCategory1Key;

[KeyAction("slot2", "#Valve_Weapon_Category_2", Section = "#Valve_Combat_Title", Order = 160, Keys = ["2"])]
public sealed class WeaponCategory2Key;

[KeyAction("slot3", "#Valve_Weapon_Category_3", Section = "#Valve_Combat_Title", Order = 170, Keys = ["3"])]
public sealed class WeaponCategory3Key;

[KeyAction("slot4", "#Valve_Weapon_Category_4", Section = "#Valve_Combat_Title", Order = 180, Keys = ["4"])]
public sealed class WeaponCategory4Key;

[KeyAction("slot5", "#Valve_Weapon_Category_5", Section = "#Valve_Combat_Title", Order = 190, Keys = ["5"])]
public sealed class WeaponCategory5Key;

[KeyAction("slot6", "#Valve_Weapon_Category_6", Section = "#Valve_Combat_Title", Order = 200, Keys = ["6"])]
public sealed class WeaponCategory6Key;

[KeyAction("invnext", "#Valve_Next_Weapon", Section = "#Valve_Combat_Title", Order = 210, Keys = ["MWHEELDOWN", "]"])]
public sealed class NextWeaponKey;

[KeyAction("invprev", "#Valve_Previous_Weapon", Section = "#Valve_Combat_Title", Order = 220, Keys = ["MWHEELUP", "["])]
public sealed class PreviousWeaponKey;

[KeyAction("lastinv", "#Valve_Last_Weapon_Used", Section = "#Valve_Combat_Title", Order = 230, Keys = ["q"])]
public sealed class LastWeaponUsedKey;

[KeyAction("use weapon_crowbar;use weapon_stunstick", "#HL2_Crowbar_Menu", Section = "#Valve_Combat_Title", Order = 240)]
public sealed class CrowbarKey;

[KeyAction("use weapon_physcannon", "#HL2_GravityGun_Menu", Section = "#Valve_Combat_Title", Order = 250)]
public sealed class UseWeaponPhyscannonKey;

[KeyAction("use weapon_pistol", "#HL2_Pistol_Menu", Section = "#Valve_Combat_Title", Order = 260)]
public sealed class PistolKey;

[KeyAction("use weapon_357", "#HL2_357Handgun_Menu", Section = "#Valve_Combat_Title", Order = 270)]
public sealed class Handgun357Key;

[KeyAction("use weapon_smg1", "#HL2_SMG1_Menu", Section = "#Valve_Combat_Title", Order = 280)]
public sealed class SMG1Key;

[KeyAction("use weapon_ar2", "#HL2_Pulse_Rifle_Menu", Section = "#Valve_Combat_Title", Order = 290)]
public sealed class PulseRifleKey;

[KeyAction("use weapon_shotgun", "#HL2_Shotgun_Menu", Section = "#Valve_Combat_Title", Order = 300)]
public sealed class ShotgunKey;

[KeyAction("use weapon_crossbow", "#HL2_Crossbow_Menu", Section = "#Valve_Combat_Title", Order = 310)]
public sealed class CrossbowKey;

[KeyAction("use weapon_frag", "#HL2_Grenade_Menu", Section = "#Valve_Combat_Title", Order = 320)]
public sealed class GrenadeKey;

[KeyAction("use weapon_rpg", "#HL2_RPG_Menu", Section = "#Valve_Combat_Title", Order = 330)]
public sealed class RPGKey;

[KeyAction("use weapon_SLAM", "#HL2_SLAM_Menu", Section = "#Valve_Combat_Title", Order = 340)]
public sealed class SLAMKey;

// #Valve_Communication_Title

[KeyAction("+voicerecord", "#Valve_Use_Voice_Communication", Section = "#Valve_Communication_Title", Order = 350)]
public sealed class UseVoiceCommunicationKey;

[KeyAction("messagemode", "#Valve_Chat_Message", Section = "#Valve_Communication_Title", Order = 360, Keys = ["y"])]
public sealed class ChatMessageKey;

[KeyAction("messagemode2", "#Valve_Team_Message", Section = "#Valve_Communication_Title", Order = 370, Keys = ["u"])]
public sealed class TeamMessageKey;

// #Valve_Miscellaneous_Title

[KeyAction("jpeg", "#Valve_Take_Screen_Shot", Section = "#Valve_Miscellaneous_Title", Order = 380, Keys = ["F5"])]
public sealed class TakeScreenShotKey;

[KeyAction("+showscores", "#Valve_Display_Scores", Section = "#Valve_Miscellaneous_Title", Order = 390, Keys = ["TAB"])]
public sealed class DisplayScoresKey;

[KeyAction("impulse 201", "#Valve_Spray_Logo", Section = "#Valve_Miscellaneous_Title", Order = 400, Keys = ["t"])]
public sealed class SprayLogoKey;

[KeyAction("pause", "#Valve_Pause_Game", Section = "#Valve_Miscellaneous_Title", Order = 410, Keys = ["PAUSE"])]
public sealed class PauseGameKey;

[KeyAction("quit", "#Valve_Quit_Game", Section = "#Valve_Miscellaneous_Title", Order = 420)]
public sealed class QuitGameKey;

[KeyAction("timeleft", "Time Left in Round", Section = "#Valve_Miscellaneous_Title", Order = 430)]
public sealed class TimeLeftInRoundKey;

[KeyAction("askconnect_accept", "#Valve_Accept_Redirect", Section = "#Valve_Miscellaneous_Title", Order = 440)]
public sealed class AcceptRedirectKey;

// #Valve_Miscellaneous_Keyboard_Keys_Title

[KeyAction("+walk", "#Valve_Walk", Section = "#Valve_Miscellaneous_Keyboard_Keys_Title", Order = 450, Keys = ["ALT"])]
public sealed class WalkKey;

[KeyAction("+left", "#Valve_Turn_Left", Section = "#Valve_Miscellaneous_Keyboard_Keys_Title", Order = 460, Keys = ["LEFTARROW"])]
public sealed class TurnLeftKey;

[KeyAction("+right", "#Valve_Turn_Right", Section = "#Valve_Miscellaneous_Keyboard_Keys_Title", Order = 470, Keys = ["RIGHTARROW"])]
public sealed class TurnRightKey;

[KeyAction("+moveup", "#Valve_Swim_Up", Section = "#Valve_Miscellaneous_Keyboard_Keys_Title", Order = 480, Keys = ["v", "'"])]
public sealed class SwimUpKey;

[KeyAction("+movedown", "#Valve_Swim_Down", Section = "#Valve_Miscellaneous_Keyboard_Keys_Title", Order = 490, Keys = ["/"])]
public sealed class SwimDownKey;

[KeyAction("+lookup", "#Valve_Look_Up", Section = "#Valve_Miscellaneous_Keyboard_Keys_Title", Order = 500, Keys = ["PGUP"])]
public sealed class LookUpKey;

[KeyAction("+lookdown", "#Valve_Look_Down", Section = "#Valve_Miscellaneous_Keyboard_Keys_Title", Order = 510, Keys = ["PGDN"])]
public sealed class LookDownKey;

[KeyAction("+strafe", "#Valve_Strafe_Modifier", Section = "#Valve_Miscellaneous_Keyboard_Keys_Title", Order = 520)]
public sealed class StrafeModifierKey;

[KeyAction("+klook", "#Valve_Keyboard_Look_Modifier", Section = "#Valve_Miscellaneous_Keyboard_Keys_Title", Order = 530, Keys = ["INS"])]
public sealed class KeyboardLookModifierKey;

// Bound by default, and no row of the page (stock lists them in neither mod's kb_act.lst).

[KeyAction("impulse 50", Keys = ["c"])]
public sealed class Impulse50Key;

[KeyAction("centerview", Keys = ["END"])]
public sealed class CenterViewKey;

[KeyAction("+mlook", Keys = [";"])]
public sealed class MlookKey;

[KeyAction("save quick", Keys = ["F6"])]
public sealed class SaveQuickKey;

[KeyAction("load quick", Keys = ["F7"])]
public sealed class LoadQuickKey;

[KeyAction("quit prompt", Keys = ["F10"])]
public sealed class QuitPromptKey;

[KeyAction("escape", Keys = ["ESCAPE"])]
public sealed class EscapeKey;

[KeyAction("toggleconsole", Keys = ["~", "`"])]
public sealed class ToggleconsoleKey;

[KeyAction("sizeup", Keys = ["+", "="])]
public sealed class SizeUpKey;

[KeyAction("sizedown", Keys = ["-"])]
public sealed class SizeDownKey;

[KeyAction("dropprimary", Keys = ["g"])]
public sealed class DropPrimaryKey;

[KeyAction("callvote", "#SS_UI_Vote_Call", Section = "#Valve_Miscellaneous_Title", Order = 445, Keys = ["o"], Radial = true)]
public sealed class CallVoteKey;

[KeyAction("adminmenu", "#SS_UI_Admin_Title", Section = "#Valve_Miscellaneous_Title", Order = 446, Keys = ["F4"], Radial = true)]
public sealed class AdminMenuKey;
