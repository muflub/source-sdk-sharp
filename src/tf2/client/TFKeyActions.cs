//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Input;

namespace TF2.Client;

// TF2's key actions, one class each: the rows of its scripts/kb_act.lst and the keys of its
// cfg/config_default.cfg. Declaring any replaces the core's list (KeyActions), so TF2 shows no
// gravity gun, and a mod over TF2 adds to these by Order.

// #Valve_Movement_Title

[KeyAction("+forward", "#Valve_Move_Forward", Section = "#Valve_Movement_Title", Order = 10, Keys = ["w"])]
public sealed class MoveForwardKey;

[KeyAction("+back", "#Valve_Move_Back", Section = "#Valve_Movement_Title", Order = 20, Keys = ["s"])]
public sealed class MoveBackKey;

[KeyAction("+moveleft", "#Valve_Move_Left", Section = "#Valve_Movement_Title", Order = 30, Keys = ["a"])]
public sealed class MoveLeftKey;

[KeyAction("+moveright", "#Valve_Move_Right", Section = "#Valve_Movement_Title", Order = 40, Keys = ["d"])]
public sealed class MoveRightKey;

[KeyAction("+jump", "#Valve_Jump", Section = "#Valve_Movement_Title", Order = 50, Keys = ["SPACE"])]
public sealed class JumpKey;

[KeyAction("+duck", "#Valve_Duck", Section = "#Valve_Movement_Title", Order = 60, Keys = ["CTRL"])]
public sealed class DuckKey;

[KeyAction("+moveup", "#Valve_Swim_Up", Section = "#Valve_Movement_Title", Order = 70, Keys = ["'"])]
public sealed class SwimUpKey;

[KeyAction("+movedown", "#Valve_Swim_Down", Section = "#Valve_Movement_Title", Order = 80, Keys = ["/"])]
public sealed class SwimDownKey;

[KeyAction("+lookup", "#Valve_Look_Up", Section = "#Valve_Movement_Title", Order = 90, Keys = ["PGUP"])]
public sealed class LookUpKey;

[KeyAction("+lookdown", "#Valve_Look_Down", Section = "#Valve_Movement_Title", Order = 100, Keys = ["PGDN"])]
public sealed class LookDownKey;

// #Valve_Communication_Title

[KeyAction("+voicerecord", "#Valve_Use_Voice_Communication", Section = "#Valve_Communication_Title", Order = 110, Keys = ["v"])]
public sealed class UseVoiceCommunicationKey;

[KeyAction("say", "#Valve_Chat_Message", Section = "#Valve_Communication_Title", Order = 120, Keys = ["y"])]
public sealed class ChatMessageKey;

[KeyAction("say_team", "#Valve_Team_Message", Section = "#Valve_Communication_Title", Order = 130, Keys = ["u"])]
public sealed class TeamMessageKey;

[KeyAction("voice_menu_1", "#TF_Voice_Menu_A", Section = "#Valve_Communication_Title", Order = 140, Keys = ["z"])]
public sealed class VoiceMenuAKey;

[KeyAction("voice_menu_2", "#TF_Voice_Menu_B", Section = "#Valve_Communication_Title", Order = 150, Keys = ["x"])]
public sealed class VoiceMenuBKey;

[KeyAction("voice_menu_3", "#TF_Voice_Menu_C", Section = "#Valve_Communication_Title", Order = 160, Keys = ["c"])]
public sealed class VoiceMenuCKey;

[KeyAction("+helpme", "#TF_Call_For_Help", Section = "#Valve_Communication_Title", Order = 170)]
public sealed class CallForHelpKey;

// #TF_Title

[KeyAction("changeclass", "#Valve_Change_Class", Section = "#TF_Title", Order = 180, Keys = [","])]
public sealed class ChangeClassKey;

[KeyAction("changeteam", "#Valve_Change_Team", Section = "#TF_Title", Order = 190, Keys = ["."])]
public sealed class ChangeTeamKey;

[KeyAction("open_charinfo_direct", "#TF_Open_Charinfo", Section = "#TF_Title", Order = 200, Keys = ["m"])]
public sealed class OpenCharinfoKey;

[KeyAction("open_charinfo_backpack", "#TF_Open_Charinfo_Backpack", Section = "#TF_Title", Order = 210, Keys = ["n"])]
public sealed class OpenCharinfoBackpackKey;

[KeyAction("show_quest_log", "#TF_Show_Quest_Log", Section = "#TF_Title", Order = 220, Keys = ["F2"])]
public sealed class ShowQuestLogKey;

[KeyAction("show_matchmaking", "#TF_Matchmaking_Show", Section = "#TF_Title", Order = 230)]
public sealed class MatchmakingShowKey;

[KeyAction("+quickswitch", "#TF_Quickswitch", Section = "#TF_Title", Order = 240)]
public sealed class QuickswitchKey;

[KeyAction("load_itempreset 0", "#TF_LoadPreset0", Section = "#TF_Title", Order = 250)]
public sealed class LoadPreset0Key;

[KeyAction("load_itempreset 1", "#TF_LoadPreset1", Section = "#TF_Title", Order = 260)]
public sealed class LoadPreset1Key;

[KeyAction("load_itempreset 2", "#TF_LoadPreset2", Section = "#TF_Title", Order = 270)]
public sealed class LoadPreset2Key;

[KeyAction("load_itempreset 3", "#TF_LoadPreset3", Section = "#TF_Title", Order = 280)]
public sealed class LoadPreset3Key;

[KeyAction("dropitem", "#TF_DropFlag", Section = "#TF_Title", Order = 290, Keys = ["e", "l"])]
public sealed class DropFlagKey;

[KeyAction("+taunt", "#TF_Taunt", Section = "#TF_Title", Order = 300, Keys = ["g"])]
public sealed class TauntKey;

[KeyAction("+use_action_slot_item", "#TF_Action", Section = "#TF_Title", Order = 310, Keys = ["h"])]
public sealed class ActionKey;

[KeyAction("+context_action", "#TF_Context_Action", Section = "#TF_Title", Order = 320)]
public sealed class ContextActionKey;

[KeyAction("showmapinfo", "#TF_MapInfo", Section = "#TF_Title", Order = 330, Keys = ["i"])]
public sealed class MapInfoKey;

[KeyAction("+inspect", "#TF_Inspect", Section = "#TF_Title", Order = 340, Keys = ["f"])]
public sealed class InspectKey;

[KeyAction("callvote", "#TF_call_vote", Section = "#TF_Title", Order = 350, Keys = ["o"])]
public sealed class CallVoteKey;

[KeyAction("adminmenu", "#SS_UI_Admin_Title", Section = "#TF_Title", Order = 351, Keys = ["F4"])]
public sealed class TFAdminMenuKey;

[KeyAction("player_ready_toggle", "#TF_Player_ReadyToggle", Section = "#TF_Title", Order = 360)]
public sealed class PlayerReadyToggleKey;

// #TF_ClassSpecific_Keys

[KeyAction("+attack2", "#TF_ClassSkill_Demoman", Section = "#TF_ClassSpecific_Keys", Order = 370)]
public sealed class ClassSkillDemomanKey;

[KeyAction("+attack2", "#TF_ClassSkill_Heavy", Section = "#TF_ClassSpecific_Keys", Order = 380)]
public sealed class ClassSkillHeavyKey;

[KeyAction("+attack2", "#TF_ClassSkill_Medic", Section = "#TF_ClassSpecific_Keys", Order = 390)]
public sealed class ClassSkillMedicKey;

[KeyAction("+attack2", "#TF_ClassSkill_Sniper", Section = "#TF_ClassSpecific_Keys", Order = 400)]
public sealed class ClassSkillSniperKey;

[KeyAction("+attack2", "#TF_ClassSkill_Spy", Section = "#TF_ClassSpecific_Keys", Order = 410)]
public sealed class ClassSkillSpyKey;

[KeyAction("lastdisguise", "#TF_ClassSkill_Spy_LastDisguise", Section = "#TF_ClassSpecific_Keys", Order = 420, Keys = ["b"])]
public sealed class ClassSkillSpyLastDisguiseKey;

[KeyAction("disguiseteam", "#TF_ClassSkill_Spy_DisguiseTeam", Section = "#TF_ClassSpecific_Keys", Order = 430, Keys = ["-"])]
public sealed class ClassSkillSpyDisguiseTeamKey;

// #Valve_Combat_Title

[KeyAction("+attack", "#Valve_Primary_Attack", Section = "#Valve_Combat_Title", Order = 440, Keys = ["MOUSE1"])]
public sealed class PrimaryAttackKey;

[KeyAction("+attack2", "#Valve_Secondary_Attack", Section = "#Valve_Combat_Title", Order = 450, Keys = ["MOUSE2"])]
public sealed class SecondaryAttackKey;

[KeyAction("+attack3", "#TF_Special_Attack", Section = "#Valve_Combat_Title", Order = 460, Keys = ["MOUSE3"])]
public sealed class SpecialAttackKey;

[KeyAction("+reload", "#Valve_Reload_Weapon", Section = "#Valve_Combat_Title", Order = 470, Keys = ["r"])]
public sealed class ReloadWeaponKey;

[KeyAction("invprev", "#Valve_Previous_Weapon", Section = "#Valve_Combat_Title", Order = 480, Keys = ["MWHEELUP"])]
public sealed class PreviousWeaponKey;

[KeyAction("invnext", "#Valve_Next_Weapon", Section = "#Valve_Combat_Title", Order = 490, Keys = ["MWHEELDOWN"])]
public sealed class NextWeaponKey;

[KeyAction("lastinv", "#Valve_Last_Weapon_Used", Section = "#Valve_Combat_Title", Order = 500, Keys = ["q"])]
public sealed class LastWeaponUsedKey;

[KeyAction("slot1", "#Valve_Weapon_Category_1", Section = "#Valve_Combat_Title", Order = 510, Keys = ["1"])]
public sealed class WeaponCategory1Key;

[KeyAction("slot2", "#Valve_Weapon_Category_2", Section = "#Valve_Combat_Title", Order = 520, Keys = ["2"])]
public sealed class WeaponCategory2Key;

[KeyAction("slot3", "#Valve_Weapon_Category_3", Section = "#Valve_Combat_Title", Order = 530, Keys = ["3"])]
public sealed class WeaponCategory3Key;

[KeyAction("slot4", "#Valve_Weapon_Category_4", Section = "#Valve_Combat_Title", Order = 540, Keys = ["4"])]
public sealed class WeaponCategory4Key;

[KeyAction("slot5", "#Valve_Weapon_Category_5", Section = "#Valve_Combat_Title", Order = 550, Keys = ["5"])]
public sealed class WeaponCategory5Key;

[KeyAction("slot6", "#Valve_Weapon_Category_6", Section = "#Valve_Combat_Title", Order = 560, Keys = ["6"])]
public sealed class WeaponCategory6Key;

[KeyAction("slot7", "#Valve_Weapon_Category_7", Section = "#Valve_Combat_Title", Order = 570, Keys = ["7"])]
public sealed class WeaponCategory7Key;

[KeyAction("slot8", "#Valve_Weapon_Category_8", Section = "#Valve_Combat_Title", Order = 580, Keys = ["8"])]
public sealed class WeaponCategory8Key;

[KeyAction("slot9", "#Valve_Weapon_Category_9", Section = "#Valve_Combat_Title", Order = 590, Keys = ["9"])]
public sealed class WeaponCategory9Key;

[KeyAction("slot10", "#Valve_Weapon_Category_0", Section = "#Valve_Combat_Title", Order = 600, Keys = ["0"])]
public sealed class WeaponCategory0Key;

// #Valve_Miscellaneous_Title

[KeyAction("impulse 201", "#Valve_Spray_Logo", Section = "#Valve_Miscellaneous_Title", Order = 610, Keys = ["t"])]
public sealed class SprayLogoKey;

[KeyAction("+showscores", "#Valve_Display_Scores", Section = "#Valve_Miscellaneous_Title", Order = 620, Keys = ["TAB"])]
public sealed class DisplayScoresKey;

[KeyAction("screenshot", "#Valve_Take_Screen_Shot", Section = "#Valve_Miscellaneous_Title", Order = 630, Keys = ["F5"])]
public sealed class TakeScreenShotKey;

[KeyAction("save_replay", "#Replay_SaveReplay", Section = "#Valve_Miscellaneous_Title", Order = 640, Keys = ["F6"])]
public sealed class ReplaySaveReplayKey;

[KeyAction("abuse_report_queue", "#AbuseReport_QueueCommandName", Section = "#Valve_Miscellaneous_Title", Order = 650, Keys = ["F7"])]
public sealed class AbuseReportQueueCommandNameKey;

[KeyAction("quit", "#Valve_Quit_Game", Section = "#Valve_Miscellaneous_Title", Order = 660)]
public sealed class QuitGameKey;

[KeyAction("toggleconsole", "#Valve_Console_Toggle", Section = "#Valve_Miscellaneous_Title", Order = 670, Keys = ["`"])]
public sealed class ConsoleToggleKey;

[KeyAction("askconnect_accept", "#Valve_Accept_Redirect", Section = "#Valve_Miscellaneous_Title", Order = 680)]
public sealed class AcceptRedirectKey;

[KeyAction("cl_trigger_first_notification", "#TF_Notification_Trigger", Section = "#Valve_Miscellaneous_Title", Order = 690, Keys = ["j"])]
public sealed class NotificationTriggerKey;

[KeyAction("cl_decline_first_notification", "#TF_Notification_Decline", Section = "#Valve_Miscellaneous_Title", Order = 700, Keys = ["k"])]
public sealed class NotificationDeclineKey;

// Bound by default, and no row of the page (stock lists them in neither mod's kb_act.lst).

[KeyAction("pause", Keys = ["PAUSE"])]
public sealed class PauseKey;

[KeyAction("escape", Keys = ["ESCAPE"])]
public sealed class EscapeKey;

[KeyAction("centerview", Keys = ["END"])]
public sealed class CenterviewKey;

[KeyAction("+strafe", Keys = ["ALT"])]
public sealed class StrafeKey;

[KeyAction("+klook", Keys = ["INS"])]
public sealed class KlookKey;

[KeyAction("quit prompt", Keys = ["F10"])]
public sealed class QuitPromptKey;

[KeyAction("replay_togglereplaytips", Keys = ["F12"])]
public sealed class ReplayTogglereplaytipsKey;

[KeyAction("say_party", Keys = ["p"])]
public sealed class SayPartyKey;

[KeyAction("+showroundinfo", Keys = ["F1"])]
public sealed class ShowroundinfoKey;

[KeyAction("voicemenu 0 0", Keys = ["e"])]
public sealed class Voicemenu00Key;
