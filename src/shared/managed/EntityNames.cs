namespace SourceSharp.Shared;

/// <summary>
/// Every classname this mod declares an entity for.
///
/// <para>
/// A classname is a STRING THE ENGINE MATCHES ON — the map writes it, the
/// factory is registered under it, the client picks its managed class by it, and
/// for a class with networked state of its own it is what the connect handshake
/// pairs the two tables by. Every one of those is a match that fails silently:
/// a misspelt name is not a compile error, it is an entity the map does not
/// spawn, or a client that begins joining and stops.
/// </para>
///
/// <para>
/// So they are here, once each, and the declarations reference them. The
/// constant's name is the classname in PascalCase with the underscores taken
/// out, mechanically and without exception — so it can be derived in either
/// direction without consulting anything.
/// </para>
///
/// <para>
/// This is a list of what the mod IMPLEMENTS, not of what Source defines. A name
/// that leaves the mod leaves this file with it, and the compiler says where it
/// was used.
/// </para>
/// </summary>
public static class EntityNames
{
    /// <summary>
    /// Edict 0 of every map, and the one name in this file whose entity class is
    /// NOT a managed one.
    ///
    /// <para>
    /// It is here because it names a networked table: <see cref="WorldNetState"/>
    /// declares what worldspawn tells the client, and both halves are built from
    /// that declaration. The shim behind it stays native — a <c>CWorld</c> on the
    /// server and a <c>C_World</c> on the client — because the engine assumes the
    /// world's INDEX, owns its brush model, and answers every trace that hits
    /// nothing with it.
    /// </para>
    /// </summary>
    public const string Worldspawn = "worldspawn";

    // Brush entities the map moves.
    public const string DynamicProp = "dynamic_prop";

    // Effects a map fires: sound, shake, explosion.
    public const string EnvEntityIgniter  = "env_entity_igniter";
    public const string EnvBeverage      = "env_beverage";
    public const string EnvPlayerSurfaceTrigger = "env_player_surface_trigger";
    public const string EnvShooter       = "env_shooter";
    public const string EnvLaser         = "env_laser";
    public const string EnvBeam          = "env_beam";
    public const string EnvFire          = "env_fire";
    public const string EnvFiresensor    = "env_firesensor";
    public const string EnvFiresource    = "env_firesource";
    public const string EnvEntityMaker   = "env_entity_maker";
    public const string EnvTexturetoggle = "env_texturetoggle";
    public const string EnvZoom          = "env_zoom";
    public const string EnvSpark        = "env_spark";
    public const string EnvFade          = "env_fade";
    public const string EnvGlobal        = "env_global";
    public const string EnvPhysexplosion = "env_physexplosion";
    public const string EnvPhysimpact    = "env_physimpact";
    public const string EnvShake         = "env_shake";
    public const string EnvViewpunch     = "env_viewpunch";
    public const string EnvBlood        = "env_blood";
    public const string EnvBubbles      = "env_bubbles";
    public const string EnvEmbers       = "env_embers";
    public const string EnvFunnel       = "env_funnel";
    public const string EnvGunfire      = "env_gunfire";
    public const string EnvMuzzleflash  = "env_muzzleflash";
    public const string EnvQuadraticbeam = "env_quadraticbeam";
    public const string EnvRotorshooter = "env_rotorshooter";
    public const string EnvSplash       = "env_splash";
    public const string EnvTracer       = "env_tracer";
    public const string EnvWind         = "env_wind";
    public const string FuncPrecipitation = "func_precipitation";
    public const string EnvExplosion     = "env_explosion";
    public const string Entityflame      = "entityflame";

    // The predicates a trigger asks before acting.
    public const string FilterActivatorClass = "filter_activator_class";
    public const string FilterActivatorName  = "filter_activator_name";
    public const string FilterActivatorTeam  = "filter_activator_team";
    public const string FilterDamageType     = "filter_damage_type";
    public const string FilterMulti          = "filter_multi";
    public const string FilterActivatorMassGreater = "filter_activator_mass_greater";
    public const string FilterBase           = "filter_base";
    public const string FilterEnemy          = "filter_enemy";

    // Brush entities. Doors, buttons, breakables, and the world's furniture.
    public const string FuncBreakable     = "func_breakable";
    public const string FuncBreakableSurf = "func_breakable_surf";
    public const string FuncBrush         = "func_brush";
    public const string FuncButton        = "func_button";
    public const string FuncDoor          = "func_door";
    public const string FuncDoorRotating  = "func_door_rotating";
    public const string FuncFriction      = "func_friction";
    public const string FuncIllusionary   = "func_illusionary";
    public const string FuncMovelinear    = "func_movelinear";
    public const string FuncPhysbox       = "func_physbox";
    public const string FuncPhysboxMultiplayer = "func_physbox_multiplayer";
    public const string FuncPlat          = "func_plat";
    public const string FuncRotButton     = "func_rot_button";
    public const string FuncRotating      = "func_rotating";
    public const string FuncWall          = "func_wall";
    public const string FuncWallToggle    = "func_wall_toggle";
    public const string FuncWaterAnalog   = "func_water_analog";
    public const string FuncWeightButton  = "func_weight_button";
    public const string FuncPushable     = "func_pushable";
    public const string FuncWater        = "func_water";
    public const string MomentaryRotButton = "momentary_rot_button";

    // Rules a map can impose on the round.
    public const string Gibshooter      = "gibshooter";
    public const string GameEnd         = "game_end";
    public const string GamePlayerEquip = "game_player_equip";
    public const string GamePlayerHurt  = "game_player_hurt";
    public const string GameScore       = "game_score";
    public const string GameText        = "game_text";
    public const string GameRagdollManager = "game_ragdoll_manager";
    public const string GameZonePlayer  = "game_zone_player";
    public const string GameGibManager  = "game_gib_manager";
    public const string GamePlayerTeam  = "game_player_team";
    public const string GameUi          = "game_ui";
    public const string GameWeaponManager = "game_weapon_manager";

    // Points in space with no shape: spawns, targets, landmarks.
    public const string InfoConstraintAnchor    = "info_constraint_anchor";
    public const string InfoLandmark            = "info_landmark";
    public const string InfoMassCenter          = "info_mass_center";
    public const string InfoNull                = "info_null";
    public const string InfoPlayerDeathmatch    = "info_player_deathmatch";
    public const string InfoPlayerStart         = "info_player_start";
    public const string InfoTarget              = "info_target";
    public const string InfoTeleportDestination = "info_teleport_destination";
    public const string InfoIntermission        = "info_intermission";

    // What a player walks into and collects.
    public const string ItemAmmo357         = "item_ammo_357";
    public const string ItemAmmo357Large    = "item_ammo_357_large";
    public const string ItemAmmoAr2         = "item_ammo_ar2";
    public const string ItemAmmoAr2Altfire  = "item_ammo_ar2_altfire";
    public const string ItemAmmoAr2Large    = "item_ammo_ar2_large";
    public const string ItemAmmoPistol      = "item_ammo_pistol";
    public const string ItemAmmoPistolLarge = "item_ammo_pistol_large";
    public const string ItemAmmoSmg1        = "item_ammo_smg1";
    public const string ItemAmmoSmg1Grenade = "item_ammo_smg1_grenade";
    public const string ItemAmmoSmg1Large   = "item_ammo_smg1_large";
    public const string ItemBattery         = "item_battery";
    public const string ItemBoxBuckshot     = "item_box_buckshot";
    public const string ItemHealthcharger   = "item_healthcharger";
    public const string ItemHealthkit       = "item_healthkit";
    public const string ItemHealthvial      = "item_healthvial";
    public const string ItemRpgRound        = "item_rpg_round";
    public const string ItemSuitcharger     = "item_suitcharger";
    public const string ItemSodacan         = "item_sodacan";

    // No shape and no model. Timers, counters and relays that only fire outputs.
    public const string LogicActiveAutosave = "logic_active_autosave";
    public const string LogicAuto           = "logic_auto";
    public const string LogicAutosave       = "logic_autosave";
    public const string LogicBranch         = "logic_branch";
    public const string LogicBranchListener = "logic_branch_listener";
    public const string LogicCase           = "logic_case";
    public const string LogicCollisionPair  = "logic_collision_pair";
    public const string LogicCompare        = "logic_compare";
    public const string LogicLineto         = "logic_lineto";
    public const string LogicMulticompare   = "logic_multicompare";
    public const string LogicRelay          = "logic_relay";
    public const string LogicScript         = "logic_script";
    public const string LogicTimer          = "logic_timer";
    public const string LogicProximity      = "logic_proximity";
    public const string LogicMeasureMovement = "logic_measure_movement";
    public const string LogicEventlistener = "logic_eventlistener";
    public const string LogicEventlistenerItemequip = "logic_eventlistener_itemequip";

    // Values a map does arithmetic with.
    public const string MathColorblend = "math_colorblend";
    public const string MathCounter    = "math_counter";
    public const string MathRemap      = "math_remap";

    // A door that opens by degrees rather than all at once.
    public const string MomentaryDoor = "momentary_door";

    // An AND gate over several inputs.
    public const string Multisource = "multisource";

    // Constraints, motors and the things that push physics objects about.
    public const string PhysBallsocket       = "phys_ballsocket";
    public const string PhysConstraint       = "phys_constraint";
    public const string PhysConstraintsystem = "phys_constraintsystem";
    public const string PhysConvert          = "phys_convert";
    public const string PhysHinge            = "phys_hinge";
    public const string PhysKeepupright      = "phys_keepupright";
    public const string PhysLengthconstraint = "phys_lengthconstraint";
    public const string PhysMagnet           = "phys_magnet";
    public const string PhysMotor            = "phys_motor";
    public const string PhysSpring           = "phys_spring";
    public const string PhysThruster         = "phys_thruster";
    public const string PhysSlideconstraint  = "phys_slideconstraint";
    public const string PhysPulleyconstraint = "phys_pulleyconstraint";
    public const string PhysRagdollconstraint = "phys_ragdollconstraint";
    public const string PhysRagdollmagnet   = "phys_ragdollmagnet";
    public const string PhysTorque           = "phys_torque";

    // The cannon that launches them.
    public const string PhysicsProp = "physics_prop";
    public const string PhysicsCannister = "physics_cannister";

    // Points that act on the world around them.
    public const string PointAnglesensor           = "point_anglesensor";
    public const string PointAngularvelocitysensor = "point_angularvelocitysensor";
    public const string PointEnableMotionFixup     = "point_enable_motion_fixup";
    public const string PointHurt                  = "point_hurt";
    public const string PointProximitySensor       = "point_proximity_sensor";
    public const string PointPush                  = "point_push";
    public const string PointTeleport              = "point_teleport";
    public const string PointVelocitysensor        = "point_velocitysensor";
    public const string PointPlayermoveconstraint  = "point_playermoveconstraint";
    public const string PointClientcommand         = "point_clientcommand";
    public const string PointServercommand         = "point_servercommand";
    public const string PointTemplate               = "point_template";
    public const string PointViewcontrol           = "point_viewcontrol";

    // Models the solver moves, and the ones it does not.
    public const string PropDoorRotating       = "prop_door_rotating";
    public const string PropDynamic            = "prop_dynamic";
    public const string PropDynamicOverride    = "prop_dynamic_override";
    public const string PropDynamicOrnament    = "prop_dynamic_ornament";
    public const string PropPhysics            = "prop_physics";
    public const string PropPhysicsMultiplayer  = "prop_physics_multiplayer";
    public const string PropPhysicsOverride    = "prop_physics_override";
    public const string PropPhysicsRespawnable = "prop_physics_respawnable";
    public const string PropSphere             = "prop_sphere";

    // The plainest movers.
    public const string SimplePhysicsBrush = "simple_physics_brush";
    public const string SimplePhysicsProp  = "simple_physics_prop";

    // This mod's own. Not stock, and not spawned by any Half-Life 2 map.
    public const string SsBarrel  = "ss_barrel";
    public const string SsBeam    = "ss_beam";
    public const string SsBolt       = "ss_bolt";
    public const string SsEnergyball = "ss_energyball";
    public const string SsEntityFlame = "ss_entityflame";
    public const string SsFiresmoke = "ss_firesmoke";
    /// <summary>
    /// One thrown piece of debris — stock's <c>gib</c>, as a managed entity.
    ///
    /// <para>
    /// WHY THE NAME CHANGED, where most of this port keeps stock's. <c>gib</c>
    /// is not a name a mapper writes and no map in this tree contains one; it
    /// is created by code, and the only code left that creates one is
    /// <see cref="EnvShooter"/>, which is C# already. The <c>ss_</c> prefix
    /// follows <c>ss_firesmoke</c>, which changed its name for exactly the same
    /// reason and recorded exactly this argument.
    /// </para>
    ///
    /// <para>
    /// It replaces <c>CGib</c> (game/server/gib.cpp, 768 lines), whose only
    /// consumer on the server link was our own shim — the "inverted" shape,
    /// a C++ class kept alive because C# asked native to build one.
    /// plan_entity.md step 4a.
    /// </para>
    /// </summary>
    public const string SsGib     = "ss_gib";
    public const string SsGrenade = "ss_grenade";
    public const string SsPickup  = "ss_pickup";
    public const string SsPlasma  = "ss_plasma";
    public const string SsPlayer  = "ss_player";

    /// <summary>
    /// The body a player leaves behind. See <c>SsRagdoll</c>.
    ///
    /// <para>
    /// A SEPARATE ENTITY from the player, which is the whole reason it exists
    /// rather than the corpse being the player entity gone limp. Respawning
    /// reuses the player's entity (<c>Players.Think</c> calls
    /// <c>Respawn</c> on the same handle), so a ragdolled player would stand
    /// up and walk away as their own corpse. Stock draws exactly this line for
    /// exactly this reason -- <c>hl2mp_ragdoll</c> is its own entity and
    /// <c>CHL2MP_Player::CreateRagdollEntity</c> is what makes one.
    /// </para>
    /// </summary>
    public const string SsRagdoll = "ss_ragdoll";
    public const string SsRocket  = "ss_rocket";

    /// <summary>
    /// One temp entity, fired where it stands.
    ///
    /// <para>
    /// The sandbox's only way to see a temp entity at all: a TE has no
    /// classname and no factory, so the coverage machinery that fails the build
    /// for an undemonstrated <c>[ScriptedEntity]</c> could not see one
    /// (plan 9.1). This gives it one classname standing for forty-three
    /// effects, told apart by the <c>te</c> keyvalue.
    /// </summary>
    public const string SsTeTester = "ss_te_tester";
    public const string SsViewModel = "ss_viewmodel";
    public const string SsWeapon  = "ss_weapon";

    // Volumes that act on whoever is inside them.
    public const string TriggerApplyImpulse     = "trigger_apply_impulse";
    public const string TriggerGravity          = "trigger_gravity";
    public const string TriggerHurt             = "trigger_hurt";
    public const string TriggerLook             = "trigger_look";
    public const string TriggerMultiple         = "trigger_multiple";
    public const string TriggerOnce             = "trigger_once";
    public const string TriggerProximity        = "trigger_proximity";
    public const string TriggerPush             = "trigger_push";
    public const string TriggerRemove           = "trigger_remove";
    public const string TriggerTeleport         = "trigger_teleport";
    public const string TriggerTeleportRelative = "trigger_teleport_relative";
    public const string Trigger                 = "trigger";
    public const string TriggerBrush            = "trigger_brush";
    public const string TriggerImpact           = "trigger_impact";
    public const string TriggerWind             = "trigger_wind";
    public const string TriggerVphysicsMotion   = "trigger_vphysics_motion";
    public const string TriggerPlayermovement   = "trigger_playermovement";
    public const string AiChangetarget          = "ai_changetarget";

    // --- wave 2: the sound, message and hud entities, and the nine weapons.
    public const string AmbientGeneric  = "ambient_generic";
    public const string EnvMessage      = "env_message";
    public const string EnvHudhint      = "env_hudhint";
    public const string EnvMicrophone   = "env_microphone";

    // --- wave 5: the map-facing half of the world's noise record.
    //
    // env_microphone above reads CSoundEnt; this writes into it. The pair is
    // the whole of what this mod does with the one piece of the AI that stayed.
    public const string AiSound         = "ai_sound";
    public const string TargetCdaudio   = "target_cdaudio";
    public const string Weapon357       = "weapon_357";
    public const string WeaponAr2       = "weapon_ar2";
    public const string WeaponCrossbow  = "weapon_crossbow";
    public const string WeaponCrowbar   = "weapon_crowbar";
    public const string WeaponFrag      = "weapon_frag";
    public const string WeaponPistol    = "weapon_pistol";
    public const string WeaponRpg       = "weapon_rpg";
    public const string WeaponShotgun   = "weapon_shotgun";
    public const string WeaponSmg1      = "weapon_smg1";

    // --- wave 1 of the remaining native classnames: lights and ropes, sky and
    // soundscape, the presentation brushes, the point/info controllers, and the
    // sprite/trail/smoke family. Added centrally and ahead of the agents,
    // because this is the one file every port touches.
    public const string Light                    = "light";
    public const string LightDynamic             = "light_dynamic";
    public const string LightEnvironment         = "light_environment";
    public const string LightGlspot              = "light_glspot";
    public const string LightSpot                = "light_spot";
    public const string PointSpotlight           = "point_spotlight";
    public const string SpotlightEnd             = "spotlight_end";
    public const string KeyframeRope             = "keyframe_rope";
    public const string MoveRope                 = "move_rope";
    public const string SkyCamera                = "sky_camera";
    public const string SkyboxSwapper            = "skybox_swapper";
    public const string EnvSoundscape            = "env_soundscape";
    public const string EnvSoundscapeProxy       = "env_soundscape_proxy";
    public const string EnvSoundscapeTriggerable = "env_soundscape_triggerable";
    public const string TriggerSoundscape        = "trigger_soundscape";
    public const string FuncAreaportal           = "func_areaportal";
    public const string FuncAreaportalwindow     = "func_areaportalwindow";
    public const string FuncDustcloud            = "func_dustcloud";
    public const string FuncDustmotes            = "func_dustmotes";
    public const string FuncClipVphysics         = "func_clip_vphysics";
    public const string FuncLod                  = "func_lod";
    public const string FuncOccluder             = "func_occluder";
    public const string FuncReflectiveGlass      = "func_reflective_glass";
    public const string FuncSmokevolume          = "func_smokevolume";
    public const string FuncVehicleclip          = "func_vehicleclip";
    public const string PointCamera              = "point_camera";
    public const string PointMessage             = "point_message";
    public const string PointTesla               = "point_tesla";
    public const string PointWorldtext           = "point_worldtext";
    public const string PointGamestatsCounter    = "point_gamestats_counter";

    /// <summary>
    /// The ABI gate's SDK-hierarchy fixture, and the port's own class:
    /// <c>sourcesharp/game/server/shell_abi_sdk_fixture.cpp</c>.
    ///
    /// <para>
    /// IT HAS NO CONSUMER. Step 8a deleted <c>AbiBothHierarchies</c>, the
    /// two-hierarchy probe that spawned this classname, so the constant and the
    /// native class behind it (<c>shell_abi_sdk_fixture.cpp</c>) are both
    /// residue awaiting lane <c>p4-exit</c>'s vpc flip — the ask is in
    /// <c>.claude/entity-port/requests/8a.md</c>. It stays declared while the
    /// class is still registered by a file the server compiles, because
    /// <c>ContractDiffTests</c> names it as one of the port's own map
    /// classnames.
    /// </para>
    ///
    /// <para>
    /// <c>stock_serverclass_census_test.cpp</c> still reads this file, but for
    /// the OPPOSITE fact from the one it used to: that nothing under
    /// <c>SourceSharp.Server</c> reaches for the classname any more. A managed
    /// consumer would be a two-hierarchy gate coming back.
    /// </para>
    /// </summary>
    public const string SourceSharpAbiSdkFixture = "sourcesharp_abi_sdk_fixture";
    public const string InfoCameraLink           = "info_camera_link";
    public const string InfoLightingRelative     = "info_lighting_relative";
    public const string InfoOverlayAccessor      = "info_overlay_accessor";
    public const string InfoParticleSystem       = "info_particle_system";
    public const string InfoProjecteddecal       = "info_projecteddecal";
    public const string Infodecal                = "infodecal";
    public const string MaterialModifyControl    = "material_modify_control";
    public const string ShadowControl            = "shadow_control";
    public const string WaterLodControl          = "water_lod_control";
    public const string ColorCorrection          = "color_correction";
    public const string ColorCorrectionVolume    = "color_correction_volume";
    public const string LogicNavigation          = "logic_navigation";
    public const string EnvFogController         = "env_fog_controller";
    public const string EnvTonemapController     = "env_tonemap_controller";
    public const string EnvDetailController      = "env_detail_controller";
    public const string EnvScreenoverlay         = "env_screenoverlay";
    public const string EnvScreeneffect          = "env_screeneffect";
    public const string EnvSprite                = "env_sprite";
    public const string EnvSpriteOriented        = "env_sprite_oriented";
    public const string EnvSpritetrail           = "env_spritetrail";
    public const string EnvSmokestack            = "env_smokestack";
    public const string EnvSmoketrail            = "env_smoketrail";
    public const string EnvSporetrail            = "env_sporetrail";
    public const string EnvSporeexplosion        = "env_sporeexplosion";
    public const string EnvFireTrail             = "env_fire_trail";
    public const string EnvSteam                 = "env_steam";
    public const string EnvSteamjet              = "env_steamjet";
    public const string EnvSun                   = "env_sun";
    public const string EnvGlow                  = "env_glow";
    public const string EnvLightglow             = "env_lightglow";
    public const string EnvDustpuff              = "env_dustpuff";
    public const string EnvDusttrail             = "env_dusttrail";
    public const string EnvParticleTrail         = "env_particle_trail";
    public const string EnvParticlelight         = "env_particlelight";
    public const string EnvRockettrail           = "env_rockettrail";
    public const string SparkShower              = "spark_shower";
    public const string TriggerTransition       = "trigger_transition";
    public const string TriggerCdaudio          = "trigger_cdaudio";
    public const string TriggerServerragdoll    = "trigger_serverragdoll";

    // The rails a mover follows, and the movers on them.
    public const string KeyframeTrack           = "keyframe_track";
    public const string MoveKeyframed           = "move_keyframed";
    public const string PathCorner              = "path_corner";
    public const string PathCornerCrash         = "path_corner_crash";
    public const string PathTrack               = "path_track";
    public const string FuncConveyor            = "func_conveyor";
    public const string FuncPlatrot             = "func_platrot";
    public const string FuncTrackautochange     = "func_trackautochange";
    public const string FuncTrackchange         = "func_trackchange";
    public const string FuncTracktrain          = "func_tracktrain";
    public const string FuncTrain               = "func_train";
    public const string FuncTraincontrols       = "func_traincontrols";

    // Acts on whoever set it off, from a point.
    public const string TargetChangegravity     = "target_changegravity";

    // The mod's own furniture: rosters, votes, sprays and the blocker.
    public const string PlayerSpeedmod          = "player_speedmod";
    public const string PlayerWeaponstrip       = "player_weaponstrip";
    public const string VoteController          = "vote_controller";

    /// <summary>
    /// The scoreboard singleton, and it is STOCK'S OWN NAME kept.
    ///
    /// <c>player_manager</c> was <c>LINK_ENTITY_TO_CLASS</c>'d to
    /// <c>CPlayerResource</c> in <c>game/server/player_resource.cpp</c> until
    /// that entity was ported here; the classname moved with the behaviour, so
    /// a packet dump, a <c>report_entities</c> and every note ever written about
    /// this entity still name the same thing. No map contains one — it is
    /// created by the rules, and now by <c>PlayerResource</c> at level init.
    /// </summary>
    public const string PlayerManager           = "player_manager";
}
