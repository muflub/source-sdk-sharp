using System;
using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// PlayerDamageReport against the pristine upstream: the numbers and the
/// shape of CHL2_Player::UpdateClientData's Damage block and the
/// CBasePlayer::OnTakeDamage lines that feed it, read from valve-sdk.lock's
/// clone, never the farm. The managed facts (PlayerDamageMessageTests) state
/// the behaviour; these pin that the behaviour they state is stock's.
/// </summary>
public class PlayerDamageReportOracleTests
{
    private const string Hl2Player = "src/game/server/hl2/hl2_player.cpp";
    private const string Player = "src/game/server/player.cpp";

    /// <summary>The body of <paramref name="cls"/>::<paramref name="method"/> with its comments removed.</summary>
    private static string Body(string file, string cls, string method)
    {
        string text = ValveSdk.Read(file);
        var m = Regex.Match(text,
            @"\n\w[\w\s\*&]*\b" + Regex.Escape(cls) + "::" + Regex.Escape(method) + @"\s*\([^)]*\)\s*\n\{(?<body>.*?)\n\}",
            RegexOptions.Singleline);
        Assert.True(m.Success, $"no {cls}::{method} in {file}: this oracle's authority is gone");
        string body = Regex.Replace(m.Groups["body"].Value, @"//[^\n]*", "");
        Assert.True(body.Length > 200, $"{cls}::{method} read as {body.Length} characters");
        return body;
    }

    private static string DamageBlock() => Body(Hl2Player, "CHL2_Player", "UpdateClientData");

    [Fact]
    public void The_message_is_sent_when_anything_was_taken_or_the_kinds_changed()
        => Assert.Matches(@"if\s*\(\s*m_DmgTake\s*\|\|\s*m_DmgSave\s*\|\|\s*m_bitsHUDDamage\s*!=\s*m_bitsDamageType\s*\)",
                          DamageBlock());

    [Fact]
    public void Both_totals_are_clamped_to_the_byte_the_report_clamps_to()
    {
        string body = DamageBlock();
        foreach (string total in new[] { "m_DmgTake", "m_DmgSave" })
        {
            var m = Regex.Match(body, total + @"\s*=\s*clamp\(\s*" + total + @"\s*,\s*0\s*,\s*(?<max>\d+)\s*\)");
            Assert.True(m.Success, total + " is no longer clamped in " + Hl2Player);
            Assert.Equal(PlayerDamageReport.ByteMax, int.Parse(m.Groups["max"].Value, CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void Poison_is_dropped_past_the_reports_window()
    {
        var m = Regex.Match(DamageBlock(), @"flLastPoisonedDelta\s*>\s*(?<window>[\d.]+)f");
        Assert.True(m.Success, "the poison window is gone from " + Hl2Player);
        Assert.Equal(PlayerDamageReport.PoisonWindow,
                     float.Parse(m.Groups["window"].Value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_bits_are_the_hud_kinds_and_the_time_based_kinds_outlive_the_send()
    {
        string body = DamageBlock();
        Assert.Matches(@"visibleDamageBits\s*=\s*m_bitsDamageType\s*&\s*iShowHudDamage", body);
        Assert.Matches(@"iShowHudDamage\s*=\s*g_pGameRules->Damage_GetShowOnHud\(\s*\)", body);
        Assert.Matches(@"m_bitsHUDDamage\s*=\s*m_bitsDamageType\s*;", body);
        Assert.Matches(@"iTimeBasedDamage\s*=\s*g_pGameRules->Damage_GetTimeBased\(\s*\)", body);
        Assert.Matches(@"m_bitsDamageType\s*&=\s*iTimeBasedDamage", body);
    }

    [Fact]
    public void It_goes_reliably_to_the_player_alone_save_then_take_then_bits_then_origin()
    {
        string body = DamageBlock();
        Assert.Matches(@"CSingleUserRecipientFilter\s+user\(\s*this\s*\)\s*;\s*user\.MakeReliable\(\s*\)", body);
        Assert.Matches(@"WRITE_BYTE\(\s*m_DmgSave\s*\);\s*WRITE_BYTE\(\s*m_DmgTake\s*\);\s*WRITE_LONG\(\s*visibleDamageBits\s*\);"
                     + @"\s*WRITE_FLOAT\(\s*damageOrigin\.x\s*\);\s*WRITE_FLOAT\(\s*damageOrigin\.y\s*\);\s*WRITE_FLOAT\(\s*damageOrigin\.z\s*\);",
                       body);

        // The managed payload writes the same order: save (Armour) first, take
        // (Health) second, then the bits.
        var buffer = new byte[64];
        int n = new Damage { Armour = 11, Health = 22, DamageBits = 0x01020304 }.Write(buffer);
        Assert.Equal(18, n);
        Assert.Equal(new byte[] { 11, 22, 4, 3, 2, 1 }, buffer[..6]);
    }

    [Fact]
    public void A_hit_sums_the_health_assigns_the_armour_and_forces_a_resend()
    {
        string body = Body(Player, "CBasePlayer", "OnTakeDamage");
        Assert.Matches(@"m_DmgTake\s*\+=\s*\(int\)info\.GetDamage\(\)", body);
        Assert.Matches(@"m_DmgSave\s*=\s*flArmor\s*;", body);
        Assert.DoesNotMatch(@"m_DmgSave\s*\+=", body);
        Assert.Matches(@"m_bitsDamageType\s*\|=\s*bitsDamage\s*;\s*m_bitsHUDDamage\s*=\s*-1\s*;", body);
        Assert.Matches(@"m_DmgOrigin\s*=\s*info\.GetInflictor\(\)->GetAbsOrigin\(\)", body);
        Assert.Matches(@"if\s*\(\s*!info\.GetDamage\(\)\s*\)\s*return 0;", body);
    }

    [Fact]
    public void A_spawn_forgets_what_the_hud_was_told()
        => Assert.Matches(@"m_DmgTake\s*=\s*0;\s*m_DmgSave\s*=\s*0;\s*m_bitsHUDDamage\s*=\s*-1;\s*m_bitsDamageType\s*=\s*0;",
                          Body(Player, "CBasePlayer", "Spawn"));

    /// <summary>
    /// HL2MP sends what CHL2_Player sends: its player does not override
    /// UpdateClientData, and its rules do not override the two masks, so
    /// CMultiplayRules' (DamageRulesOracleTests pins those) are the answer.
    /// </summary>
    [Fact]
    public void Hl2mp_overrides_neither_the_update_nor_the_masks()
    {
        foreach (string file in new[] { "src/game/server/hl2mp/hl2mp_player.h", "src/game/server/hl2mp/hl2mp_player.cpp",
                                        "src/game/shared/hl2mp/hl2mp_gamerules.h", "src/game/shared/hl2mp/hl2mp_gamerules.cpp",
                                        "src/game/shared/teamplay_gamerules.h", "src/game/shared/teamplay_gamerules.cpp" })
        {
            string text = ValveSdk.Read(file);
            Assert.True(text.Length > 1000, file + " read short");
            Assert.DoesNotMatch(@"\bUpdateClientData\s*\(\s*(void)?\s*\)", text);
            Assert.DoesNotMatch(@"Damage_GetShowOnHud|Damage_GetTimeBased", text);
        }

        Assert.Contains("class CHL2MP_Player : public CHL2_Player", ValveSdk.Read("src/game/server/hl2mp/hl2mp_player.h"), StringComparison.Ordinal);
        Assert.Contains("class CHL2MPRules : public CTeamplayRules", ValveSdk.Read("src/game/shared/hl2mp/hl2mp_gamerules.h"), StringComparison.Ordinal);
        Assert.Contains("class CTeamplayRules : public CMultiplayRules", ValveSdk.Read("src/game/shared/teamplay_gamerules.h"), StringComparison.Ordinal);
    }
}

/// <summary>
/// PlayerDamageReport.CheckTimeBased against stock's CheckTimeBasedDamage:
/// the durations by itbd_ index (the LIVE defines, not the commented-out HL1
/// table above the function), the recover literals and the two-second gate.
/// </summary>
public class TimeBasedDamageOracleTests
{
    private static string Code => Regex.Replace(ValveSdk.Read("src/game/server/player.cpp"), @"//[^\n]*", "");

    private static int Define(string name)
    {
        var m = Regex.Match(Code, @"#define\s+" + name + @"\s+(?<v>\d+)\b");
        Assert.True(m.Success, name + " is gone from player.cpp");
        return int.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void The_durations_are_stocks_by_itbd_index()
    {
        string body = Regex.Match(Code, @"void CBasePlayer::CheckTimeBasedDamage\(\)\s*\{(?<b>.*?)\n\}", RegexOptions.Singleline).Groups["b"].Value;
        Assert.True(body.Length > 500, "CheckTimeBasedDamage read short");
        int drown = int.Parse(Regex.Match(body, @"bDuration\s*=\s*(?<v>\d+);\s*//\s*get up to 5\*10|case itbd_DrownRecover:.*?bDuration\s*=\s*(?<v>\d+);", RegexOptions.Singleline).Groups["v"].Value, CultureInfo.InvariantCulture);
        int poison = int.Parse(Regex.Match(body, @"case itbd_PoisonRecover:.*?bDuration\s*=\s*(?<v>\d+);", RegexOptions.Singleline).Groups["v"].Value, CultureInfo.InvariantCulture);
        Assert.Matches(@"abs\(\s*gpGlobals->curtime\s*-\s*m_tbdPrev\s*\)\s*<\s*2\.0", body);
        Assert.Matches(@"MIN\(\s*m_nPoisonDmg\s*-\s*m_nPoisonRestored\s*,\s*10\s*\)", body);

        var stock = new[] { Define("PARALYZE_DURATION"), Define("NERVEGAS_DURATION"), poison, Define("RADIATION_DURATION"),
                            drown, Define("ACID_DURATION"), Define("SLOWBURN_DURATION"), Define("SLOWFREEZE_DURATION") };
        Assert.Equal(stock, PlayerDamageReport.Durations.Select(d => (int)d).ToArray());
        Assert.Equal((2f, 10), (PlayerDamageReport.TimeBasedInterval, PlayerDamageReport.PoisonRecoverStep));
    }
}

/// <summary>
/// SsPlayer.UpdateClientData's base half against CBasePlayer::UpdateClientData
/// in the pristine upstream: the order, the payloads and the spawn's resets.
/// </summary>
public class PlayerHudMessagesOracleTests
{
    private static string Body(string file, string cls, string method)
    {
        string text = ValveSdk.Read(file);
        var m = Regex.Match(text,
            @"\n\w[\w\s\*&]*\b" + Regex.Escape(cls) + "::" + Regex.Escape(method) + @"\s*\([^)]*\)\s*\n\{(?<body>.*?)\n\}",
            RegexOptions.Singleline);
        Assert.True(m.Success, $"no {cls}::{method} in {file}: this oracle's authority is gone");
        return Regex.Replace(m.Groups["body"].Value, @"//[^\n]*", "");
    }

    [Fact]
    public void Damage_goes_first_then_the_base_update()
    {
        string body = Body("src/game/server/hl2/hl2_player.cpp", "CHL2_Player", "UpdateClientData");
        int damage = body.IndexOf("\"Damage\"", StringComparison.Ordinal);
        int baseCall = body.IndexOf("BaseClass::UpdateClientData()", StringComparison.Ordinal);
        Assert.True(damage > 0 && baseCall > damage, $"Damage at {damage}, the base update at {baseCall}");
    }

    [Fact]
    public void The_base_update_sends_resethud_then_gametitle_then_battery_then_train()
    {
        string body = Body("src/game/server/player.cpp", "CBasePlayer", "UpdateClientData");
        Assert.Matches(@"UserMessageBegin\(\s*user,\s*""ResetHUD""\s*\);\s*WRITE_BYTE\(\s*0\s*\);", body);
        Assert.Matches(@"UserMessageBegin\(\s*user,\s*""Battery""\s*\);\s*WRITE_SHORT\(\s*\(int\)m_ArmorValue\s*\);", body);
        Assert.Matches(@"if\s*\(\s*m_ArmorValue\s*!=\s*m_iClientBattery\s*\)", body);
        Assert.Matches(@"CSingleUserRecipientFilter\s+user\(\s*this\s*\);\s*user\.MakeReliable\(\)", body);

        int reset = body.IndexOf("\"ResetHUD\"", StringComparison.Ordinal);
        int title = body.IndexOf("\"GameTitle\"", StringComparison.Ordinal);
        int battery = body.IndexOf("\"Battery\"", StringComparison.Ordinal);
        int train = body.IndexOf("CheckTrainUpdate()", StringComparison.Ordinal);
        Assert.True(reset > 0 && reset < title && title < battery && battery < train,
                    $"ResetHUD {reset}, GameTitle {title}, Battery {battery}, train {train}");

        string trainBody = Body("src/game/server/player.cpp", "CBasePlayer", "CheckTrainUpdate");
        Assert.Matches(@"if\s*\(\s*\(\s*m_iTrain\s*&\s*TRAIN_NEW\s*\)\s*\)", trainBody);
        Assert.Matches(@"WRITE_BYTE\(\s*m_iTrain\s*&\s*0xF\s*\);", trainBody);
        Assert.Matches(@"m_iTrain\s*&=\s*~TRAIN_NEW;", trainBody);
    }

    [Fact]
    public void A_spawn_resends_the_hud_the_battery_and_the_train()
    {
        string body = Body("src/game/server/player.cpp", "CBasePlayer", "Spawn");
        Assert.Matches(@"m_fInitHUD\s*=\s*true;", body);
        Assert.Matches(@"m_iClientBattery\s*=\s*-1;", body);
        Assert.Matches(@"m_iTrain\s*=\s*TRAIN_NEW;", body);

        var m = Regex.Match(ValveSdk.Read("src/game/shared/baseplayer_shared.h"), @"#define\s+TRAIN_NEW\s+(?<v>0x[0-9a-fA-F]+)");
        Assert.True(m.Success, "TRAIN_NEW is gone from baseplayer_shared.h");
        Assert.Equal(SsPlayer.TrainNew, Convert.ToInt32(m.Groups["v"].Value, 16));
    }
}

/// <summary>DeathNotice against pristine CHL2MPRules::DeathNotice: the renames, the fields, the priority.</summary>
public class DeathNoticeOracleTests
{
    private static string Body()
    {
        string code = Regex.Replace(ValveSdk.Read("src/game/shared/hl2mp/hl2mp_gamerules.cpp"), @"//[^\n]*", "");
        var m = Regex.Match(code, @"void CHL2MPRules::DeathNotice\(.*?\)\s*\{(?<b>.*?)\n\}", RegexOptions.Singleline);
        Assert.True(m.Success, "CHL2MPRules::DeathNotice is gone");
        return m.Groups["b"].Value;
    }

    [Fact]
    public void The_renames_are_stocks()
    {
        string b = Body();
        foreach (var (prefix, len) in new[] { ("weapon_", 7), ("npc_", 4), ("func_", 5) })
            Assert.Matches(@"strncmp\(\s*killer_weapon_name,\s*""" + prefix + @"""\s*,\s*" + len + @"\s*\)\s*==\s*0\s*\)\s*\{\s*killer_weapon_name\s*\+=\s*" + len + ";", b);
        Assert.Matches(@"strstr\(\s*killer_weapon_name,\s*""physics""\s*\)\s*\)\s*\{\s*killer_weapon_name\s*=\s*""physics"";", b);
        Assert.Equal(("combine_ball", "smg1_grenade", "slam", "world"),
                     (DeathNotice.WeaponName("prop_combine_ball"), DeathNotice.WeaponName("grenade_ar2"),
                      DeathNotice.WeaponName("satchel"), DeathNotice.WeaponName("world")));
        Assert.Contains(@"""prop_combine_ball"" ) == 0 )", b.Replace("\t", " "), StringComparison.Ordinal);
        Assert.Contains(@"killer_weapon_name = ""smg1_grenade"";", b, StringComparison.Ordinal);
        Assert.Contains(@"killer_weapon_name = ""slam"";", b, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fields_are_stocks()
    {
        string b = Body();
        Assert.Matches(@"CreateEvent\(\s*""player_death""\s*\)", b);
        Assert.Matches(@"SetInt\(\s*""userid"",\s*pVictim->GetUserID\(\)\s*\)", b);
        Assert.Matches(@"SetInt\(\s*""attacker"",\s*killer_ID\s*\)", b);
        Assert.Matches(@"SetString\(\s*""weapon"",\s*killer_weapon_name\s*\)", b);
        Assert.Matches(@"SetInt\(\s*""priority"",\s*" + DeathNotice.Priority + @"\s*\)", b);
        Assert.DoesNotMatch(@"""headshot""", b);
        Assert.Equal("player_death", DeathNotice.EventName);
    }
}
