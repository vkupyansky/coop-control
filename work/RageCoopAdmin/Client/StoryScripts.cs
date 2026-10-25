using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GTA;
using GTA.Native;

namespace RageCoopAdmin.Client;

// Story-mode scripts stopped while connected: the player's choice on the
// "Скрипты сюжета GTA V" page (selection/current, version 15), built from
// work/StoryScripts/catalog.json. Story missions and calls, strangers and
// freaks, random events, side jobs and collectibles. The scripts that run the
// game, shops, phone, Los Santos Customs, respawn and the map icons
// (blip_controller: it draws the shops' icons too) stay; StoryBlipHider hides
// the story characters' icons instead. Scripts the game started once at load
// (the story flow, calls) stay stopped until GTA restarts; those the world
// starts on its own (random events, side jobs) return after disconnecting.
internal static class StoryScripts
{
    internal static readonly string[] Stopped =
    {
        // Управление сюжетом (67)
        "achievement_controller", "af_intro_t_sandy", "autosave_controller", "bootycallhandler",
        "buddydeathresponse", "charactergoals", "comms_controller", "completionpercentage_controller", "drf1",
        "drf2", "drf3", "drf4", "drf5", "email_controller", "exile_city_denial", "family1taxi", "family_scene_f0",
        "family_scene_f1", "family_scene_m", "family_scene_t0", "family_scene_t1", "flow_controller", "flow_help",
        "flowintrotitle", "flowstartaccept", "friendactivity", "friends_controller", "lesterhandler", "letterscraps",
        "maude_postbailbond", "me_amanda1", "me_jimmy1", "me_tracey1", "minigame_ending_stinger",
        "minigame_stats_tracker", "mission_repeat_controller", "mission_stat_alerter", "mission_stat_watcher",
        "mission_triggerer_a", "mission_triggerer_b", "mission_triggerer_c", "mission_triggerer_d",
        "player_scene_f_lamgraff", "player_scene_f_lamtaunt", "player_scene_f_taxi", "player_scene_ft_franklin1",
        "player_scene_m_cinema", "player_scene_m_fbi2", "player_scene_m_kids", "player_scene_m_shopping",
        "player_scene_mf_traffic", "player_scene_t_bbfight", "player_scene_t_chasecar", "player_scene_t_insult",
        "player_scene_t_park", "player_scene_t_tie", "player_timetable_scene", "postkilled_bailbond2",
        "postrc_barry1and2", "postrc_barry4", "postrc_epsilon4", "postrc_nigel3", "randomchar_controller",
        "sh_intro_f_hills", "sh_intro_m_home", "shrinkletter", "stripperhome",
        // Сюжетные миссии (97)
        "agency_heist1", "agency_heist2", "agency_heist3a", "agency_heist3b", "agency_prep1", "agency_prep2amb",
        "armenian1", "armenian2", "armenian3", "assassin_bus", "assassin_construction", "assassin_hooker",
        "assassin_multi", "assassin_rankup", "assassin_valet", "carsteal1", "carsteal2", "carsteal3", "carsteal4",
        "chinese1", "chinese2", "docks2asubhandler", "docks_heista", "docks_heistb", "docks_prep1", "docks_prep2b",
        "docks_setup", "exile1", "exile2", "exile3", "family1", "family2", "family3", "family4", "family5",
        "family6", "fbi1", "fbi2", "fbi3", "fbi4", "fbi4_intro", "fbi4_prep1", "fbi4_prep2", "fbi4_prep3",
        "fbi4_prep3amb", "fbi4_prep4", "fbi4_prep5", "fbi5a", "finale_choice", "finale_credits", "finale_endgame",
        "finale_heist1", "finale_heist2_intro", "finale_heist2a", "finale_heist2b", "finale_heist_prepa",
        "finale_heist_prepb", "finale_heist_prepc", "finale_heist_prepd", "finale_heist_prepeamb", "finale_intro",
        "finalea", "finaleb", "finalec1", "finalec2", "franklin0", "franklin1", "franklin2", "heist_ctrl_agency",
        "heist_ctrl_docks", "heist_ctrl_finale", "heist_ctrl_jewel", "heist_ctrl_rural", "jewelry_heist",
        "jewelry_prep1a", "jewelry_prep1b", "jewelry_prep2a", "jewelry_setup1", "lamar1", "lester1", "martin1",
        "michael1", "michael2", "michael3", "michael4", "michael4leadout", "prologue1", "rural_bank_heist",
        "rural_bank_prep1", "rural_bank_setup", "solomon1", "solomon2", "solomon3", "trevor1", "trevor2", "trevor3",
        "trevor4",
        // Незнакомцы и чудаки (102)
        "abigail1", "abigail2", "altruist_cult", "ambient_mrsphilips", "ambient_solomon", "ambient_tonya",
        "ambient_tonyacall", "ambient_tonyacall2", "ambient_tonyacall5", "bailbond1", "bailbond2", "bailbond3",
        "bailbond4", "bailbond_launcher", "barry1", "barry2", "barry3", "barry3a", "barry3c", "barry4", "dreyfuss1",
        "epscars", "epsdesert", "epsilon1", "epsilon2", "epsilon3", "epsilon4", "epsilon5", "epsilon6", "epsilon7",
        "epsilon8", "epsilontract", "epsrobes", "extreme1", "extreme2", "extreme3", "extreme4", "fame_or_shame_set",
        "fameorshame_eps", "fameorshame_eps_1", "fanatic1", "fanatic2", "fanatic3", "hao1", "hunting1", "hunting2",
        "josh1", "josh2", "josh3", "josh4", "launcher_abigail", "launcher_barry", "launcher_dreyfuss",
        "launcher_epsilon", "launcher_extreme", "launcher_fanatic", "launcher_hao", "launcher_hunting",
        "launcher_josh", "launcher_maude", "launcher_minute", "launcher_mrsphilips", "launcher_nigel",
        "launcher_omega", "launcher_paparazzo", "launcher_thelastone", "launcher_tonya", "maude1", "minute1",
        "minute2", "minute3", "mrsphilips1", "mrsphilips2", "nigel1", "nigel1a", "nigel1b", "nigel1c", "nigel1d",
        "nigel2", "nigel3", "omega1", "omega2", "paparazzo1", "paparazzo2", "paparazzo3", "paparazzo3a",
        "paparazzo3b", "paparazzo4", "rampage1", "rampage2", "rampage3", "rampage4", "rampage5",
        "rampage_controller", "selling_short_1", "selling_short_2", "thelastone", "tonya1", "tonya2", "tonya3",
        "tonya4", "tonya5",
        // Случайные события (38)
        "re_abandonedcar", "re_accident", "re_armybase", "re_arrests", "re_atmrobbery", "re_bikethief", "re_border",
        "re_burials", "re_bus_tours", "re_cartheft", "re_chasethieves", "re_crashrescue", "re_cultshootout",
        "re_dealgonewrong", "re_domestic", "re_drunkdriver", "re_duel", "re_gang_intimidation", "re_gangfight",
        "re_getaway_driver", "re_hitch_lift", "re_homeland_security", "re_lossantosintl", "re_lured", "re_monkey",
        "re_mountdance", "re_muggings", "re_paparazzi", "re_prison", "re_prisonerlift", "re_prisonvanbreak",
        "re_rescuehostage", "re_seaplane", "re_securityvan", "re_shoprobbery", "re_snatched", "re_stag_do",
        "re_yetarian",
        // Развлечения и подработки (34)
        "controller_races", "controller_taxi", "controller_towing", "controller_trafficking", "country_race",
        "country_race_controller", "launcher_offroadracing", "launcher_pilotschool", "launcher_racing",
        "launcher_rampage", "launcher_stunts", "launcher_triathlon", "mission_race", "offroad_races", "pilot_school",
        "sp_pilotschool_reg", "stunt_plane_races", "taxi_clowncar", "taxi_cutyouin", "taxi_deadline",
        "taxi_followcar", "taxi_gotyounow", "taxi_gotyourback", "taxi_needexcitement", "taxi_procedural",
        "taxi_takeiteasy", "taxi_taketobest", "taxitutorial", "towing", "traffick_air", "traffick_ground",
        "traffickingsettings", "traffickingteleport", "triathlonsp",
        // Коллекционные предметы (11)
        "ambient_diving", "ambient_sonar", "ambient_ufos", "flyunderbridges", "murdermystery", "photographymonkey",
        "photographywildlife", "shot_bikejump", "spaceshipparts", "ufo", "underwaterpickups",
        // Живой мир (2)
        "cheat_controller", "restrictedareas",
    };
}

// Checks a few names each frame and stops any that runs; a parent that starts
// one again (the game restarts some on its own) is caught on the next pass.
// Game thread only.
internal sealed class StoryScriptStopper
{
    internal const int PerFrame = 40;

    private readonly string[] _names;
    private readonly int[] _hashes;
    private readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);
    private readonly Action<string>? _log;
    private int _next;

    internal StoryScriptStopper(string[] names, Action<string>? log = null)
    {
        _names = names;
        _hashes = new int[names.Length];
        for (int i = 0; i < names.Length; i++) _hashes[i] = unchecked((int)StringHash.AtStringHash(names[i]));
        _log = log;
    }

    internal int StoppedCount => _logged.Count;

    internal void Frame()
    {
        if (_names.Length == 0) return;
        for (int step = 0; step < PerFrame && step < _names.Length; step++)
        {
            int index = _next;
            _next = (_next + 1) % _names.Length;
            if (!StoryScriptNatives.IsRunning(_hashes[index])) continue;
            StoryScriptNatives.Stop(_names[index]);
            if (_logged.Add(_names[index])) _log?.Invoke("Coop Control: stopped story script " + _names[index] + ".");
        }
    }
}

// Story characters' map icons: mission letters, strangers' question marks and
// story contacts. blip_controller draws them together with the icons of shops,
// Los Santos Customs and activities, so it keeps running and only these icons
// are hidden, once a second (it may show them again). Sprite numbers from the
// FiveM blip list source (docs: game-references/blips, build 3258; radar_*_family,
// *_strand, story contacts, friends); none is used by our places or by RageCoop players. Hidden icons stay hidden until GTA restarts,
// like the story flow that placed them. Game thread only.
internal sealed class StoryBlipHider
{
    internal const long IntervalMilliseconds = 1_000;

    internal static readonly int[] Sprites =
    {
        66, // radar_random_character
        76, 77, 78, 79, 86, 88, // armenian, lester, michael, trevor, lamar, franklin _family
        89, 106, 108, 112, 114, 115, 120, 123, 124, // chinese_strand, fbi_officers_strand, financier_strand, michael_family_exile, random_female/male, solomon_strand, trevor_family_exile, michael_trevor_family
        165, 166, 167, 168, 169, 170, 172, // friend_franklin/michael/trevor _p/_x, friend_lamar
        206, // epsilon
        207, 208, 209, 210, 211, 212, 213, 214, // financier_strand_grey, trevor/franklin_family variants, franklin_a/b/c
        292, 293, 352, 355, // band_strand, simeon_family, martin_madrazzo, devin
        381, 382, 383, 384, 385, 386, 387, 388, 389, // barry, dom, maryann, cletus, josh, minute, omega, tonya, paparazzo
        400, // abigail
    };

    private readonly Action<string>? _log;
    private long _next;
    private bool _logged;

    internal StoryBlipHider(Action<string>? log = null) => _log = log;

    internal void Tick(long now)
    {
        if (now < _next) return;
        _next = now + IntervalMilliseconds;
        int hidden = 0;
        foreach (int sprite in Sprites)
        {
            for (int blip = StoryBlipNatives.First(sprite); StoryBlipNatives.Exists(blip); blip = StoryBlipNatives.Next(sprite))
            {
                if (!StoryBlipNatives.IsShown(blip)) continue;
                StoryBlipNatives.Hide(blip);
                hidden++;
            }
        }

        if (hidden > 0 && !_logged)
        {
            _logged = true;
            _log?.Invoke("Coop Control: hid " + hidden + " story map icons.");
        }
    }

    internal void Reset()
    {
        _next = 0;
        _logged = false;
    }
}

// GTA boundary; isolated checks replace these calls.
internal static class StoryBlipNatives
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int First(int sprite) => Function.Call<int>(Hash.GET_FIRST_BLIP_INFO_ID, sprite);
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Next(int sprite) => Function.Call<int>(Hash.GET_NEXT_BLIP_INFO_ID, sprite);
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool Exists(int blip) => blip != 0 && Function.Call<bool>(Hash.DOES_BLIP_EXIST, blip);
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsShown(int blip) => Function.Call<int>(Hash.GET_BLIP_INFO_ID_DISPLAY, blip) != 0;
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Hide(int blip) => Function.Call(Hash.SET_BLIP_DISPLAY, blip, 0);
}

// GTA boundary; isolated checks replace these calls.
internal static class StoryScriptNatives
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsRunning(int hash) => Function.Call<int>((Hash)0x2C83A9DA6BFFC4F9UL, hash) > 0; // GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Stop(string name) => Function.Call((Hash)0x9DC711BC69C548DFUL, name); // TERMINATE_ALL_SCRIPTS_WITH_THIS_NAME
}
