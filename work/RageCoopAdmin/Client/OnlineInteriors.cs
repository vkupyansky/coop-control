using System;
using System.Linq;
using System.Runtime.CompilerServices;
using CoopControl.Shared;
using GTA.Math;
using GTA.Native;

namespace RageCoopAdmin.Client;

// A furnishing set inside an interior, optionally tinted (colour index).
internal readonly struct InteriorSet
{
    internal InteriorSet(string name, int tint = -1)
    {
        Name = name;
        Tint = tint;
    }

    internal string Name { get; }
    internal int Tint { get; }
}

// One GTA Online interior. X/Y/Z is where a player appears inside and where
// the exit is; it also identifies the interior (GET_INTERIOR_AT_COORDS).
internal sealed class OnlineInterior
{
    internal OnlineInterior(string key, string name, float x, float y, float z, string[] ipls, params InteriorSet[] sets)
    {
        Key = key;
        Name = name;
        X = x;
        Y = y;
        Z = z;
        Ipls = ipls;
        Sets = sets;
    }

    internal string Key { get; }
    internal string Name { get; }
    internal float X { get; }
    internal float Y { get; }
    internal float Z { get; }
    internal string[] Ipls { get; }
    internal InteriorSet[] Sets { get; }
    internal InteriorBuilding Building { get; set; } = null!;
    internal int Index { get; set; }
}

// A street entrance of GTA Online: the door marker, its map icon and the
// interiors behind it (several apartments share one tower lobby, as in Online).
internal sealed class InteriorBuilding
{
    internal InteriorBuilding(string key, string name, string group, int sprite, float x, float y, float z, params OnlineInterior[] interiors)
    {
        Key = key;
        Name = name;
        Group = group;
        Sprite = sprite;
        X = x;
        Y = y;
        Z = z;
        Interiors = interiors;
        foreach (OnlineInterior interior in interiors) interior.Building = this;
    }

    internal string Key { get; }
    internal string Name { get; }
    internal string Group { get; }
    internal int Sprite { get; }
    internal float X { get; }
    internal float Y { get; }
    internal float Z { get; }
    internal OnlineInterior[] Interiors { get; }
    // A GTA Online update this building needs; absent in older games, so it is hidden there.
    internal string? Dlc { get; private set; }

    internal InteriorBuilding RequiresDlc(string dlc)
    {
        Dlc = dlc;
        return this;
    }
}

// GTA Online interiors with their street entrances. Inside points, map parts
// and furnishing sets: Bob74's bob74_ipl (MIT), its default furnishing. Entrances:
// the game's own scripts (decompiled calamity-inc/GTA-V-Decompiled-Scripts for
// 3889): am_mp_property_ext (homes, offices, clubhouses, biker businesses),
// am_mp_casino, and the shared property tables in the am_mp_* property scripts
// (bunkers, hangars, facilities, nightclubs, arcades, agencies, auto shops, car
// meet, bail offices, salvage yards, car wash, mansions, special cargo and
// vehicle warehouses with am_mp_warehouse's sizes) and the business scripts
// (am_mp_music_studio, am_mp_casino_nightclub for the Music Locker beside the
// casino, am_mp_multistorey_garage, am_mp_juggalo_hideout for the Freakshop).
// CEO garages open from their office towers. One entrance per interior; Online
// has several for most. Icons: the FiveM blip list source.
internal static class OnlineInteriors
{
    internal const int Safehouse = 40;
    internal const int Office = 475;
    internal const int Casino = 679;
    internal static readonly int[] Icons =
    {
        Safehouse, Office, Casino, 492, 496, 497, 498, 499, 500, // clubhouse, weed, crack, fake id, meth, money
        557, 569, 590, 614, 740, 777, 779, 826, 867, 893, 931, 948, // bunker, hangar, facility, nightclub, arcade, car meet,
        // auto shop, agency, salvage yard, bail office, car wash, mansion
        136, 473, 524, 819, 847, 856, // music venue, warehouse, vehicle warehouse, music studio, Freakshop, garage
    };

    private static readonly string[] None = Array.Empty<string>();

    internal static readonly InteriorBuilding[] Buildings =
    {
        new InteriorBuilding("casino", "Казино Diamond", "Казино", Casino, 924.021f, 45.981f, 80.096f,
            new OnlineInterior("casino_floor", "Зал казино", 1105.483f, 220.212f, -48.99f,
                new[] { "vw_casino_main", "hei_dlc_windows_casino", "hei_dlc_casino_aircon", "vw_dlc_casino_door", "hei_dlc_casino_door" }),
            new OnlineInterior("casino_penthouse", "Пентхаус казино", 976.636f, 70.295f, 115.164f,
                new[] { "vw_casino_penthouse" },
                new InteriorSet("Set_Pent_Tint_Shell", 1), new InteriorSet("Set_Pent_Pattern_01", 1),
                new InteriorSet("Set_Pent_Spa_Bar_Open"), new InteriorSet("Set_Pent_Media_Bar_Open"), new InteriorSet("Set_Pent_Dealer"))),

        new InteriorBuilding("eclipse", "Eclipse Towers", "Апартаменты", Safehouse, -773.478f, 310.632f, 84.698f,
            new OnlineInterior("eclipse_penthouse_1", "Пентхаус 1", -787.781f, 334.923f, 215.838f, new[] { "apa_v_mp_h_01_a" }),
            new OnlineInterior("eclipse_penthouse_2", "Пентхаус 2", -773.226f, 322.825f, 194.886f, new[] { "apa_v_mp_h_03_b" }),
            new OnlineInterior("eclipse_penthouse_3", "Пентхаус 3", -787.781f, 334.923f, 186.113f, new[] { "apa_v_mp_h_07_c" }),
            new OnlineInterior("eclipse_apartment_3", "Апартаменты 3", -773.407f, 341.766f, 211.397f, None)),
        new InteriorBuilding("integrity", "4 Integrity Way", "Апартаменты", Safehouse, -47.315f, -585.977f, 36.959f,
            new OnlineInterior("integrity_30", "Апартаменты 30", -35.313f, -580.420f, 88.712f, None),
            new OnlineInterior("integrity_28", "Апартаменты 28", -18.079f, -583.673f, 79.466f, None)),
        new InteriorBuilding("del_perro", "Del Perro Heights", "Апартаменты", Safehouse, -1443.094f, -544.768f, 33.742f,
            new OnlineInterior("del_perro_7", "Апартаменты 7", -1477.140f, -538.750f, 55.526f, None),
            new OnlineInterior("del_perro_4", "Апартаменты 4", -1468.140f, -541.815f, 73.444f, None)),
        new InteriorBuilding("richards_majestic", "Richards Majestic", "Апартаменты", Safehouse, -932.747f, -383.925f, 37.961f,
            new OnlineInterior("richards_majestic_2", "Апартаменты 2", -915.811f, -379.432f, 113.675f, None)),
        new InteriorBuilding("tinsel_towers", "Tinsel Towers", "Апартаменты", Safehouse, -619.132f, 37.884f, 42.588f,
            new OnlineInterior("tinsel_towers_42", "Апартаменты 42", -614.860f, 40.678f, 97.600f, None)),

        new InteriorBuilding("wild_oats", "3655 Wild Oats Drive", "Дома", Safehouse, -178.228f, 490.886f, 134.047f,
            new OnlineInterior("wild_oats_house", "Дом", -169.286f, 486.494f, 137.444f, None)),
        new InteriorBuilding("north_conker_2044", "2044 North Conker Avenue", "Дома", Safehouse, 339.574f, 439.708f, 145.590f,
            new OnlineInterior("north_conker_2044_house", "Дом", 340.941f, 437.180f, 149.393f, None)),
        new InteriorBuilding("north_conker_2045", "2045 North Conker Avenue", "Дома", Safehouse, 369.589f, 417.481f, 136.834f,
            new OnlineInterior("north_conker_2045_house", "Дом", 373.023f, 416.105f, 145.701f, None)),
        new InteriorBuilding("hillcrest_2862", "2862 Hillcrest Avenue", "Дома", Safehouse, -679.546f, 592.516f, 139.693f,
            new OnlineInterior("hillcrest_2862_house", "Дом", -676.127f, 588.612f, 145.170f, None)),
        new InteriorBuilding("hillcrest_2868", "2868 Hillcrest Avenue", "Дома", Safehouse, -764.616f, 618.314f, 137.554f,
            new OnlineInterior("hillcrest_2868_house", "Дом", -763.107f, 615.906f, 144.140f, None)),
        new InteriorBuilding("hillcrest_2874", "2874 Hillcrest Avenue", "Дома", Safehouse, -861.252f, 684.892f, 146.264f,
            new OnlineInterior("hillcrest_2874_house", "Дом", -857.798f, 682.563f, 152.653f, None)),
        new InteriorBuilding("whispymound", "2677 Whispymound Drive", "Дома", Safehouse, 124.457f, 551.888f, 181.658f,
            new OnlineInterior("whispymound_house", "Дом", 120.500f, 549.952f, 184.097f, None)),
        new InteriorBuilding("mad_wayne", "2133 Mad Wayne Thunder Drive", "Дома", Safehouse, -1292.456f, 440.945f, 93.757f,
            new OnlineInterior("mad_wayne_house", "Дом", -1288.000f, 440.748f, 97.695f, None)),

        new InteriorBuilding("arcadius", "Arcadius Business Center", "Офисы", Office, -117.530f, -605.716f, 35.286f,
            new OnlineInterior("arcadius_office", "Офис CEO", -141.199f, -620.913f, 168.821f, new[] { "ex_dt1_02_office_03c" },
                new InteriorSet("office_chairs")),
            new OnlineInterior("arcadius_garage", "Гараж", -191.013f, -579.143f, 135.000f, new[] { "imp_dt1_02_cargarage_a" },
                new InteriorSet("garage_decor_01"), new InteriorSet("numbering_style01_n1"), new InteriorSet("lighting_option01"))),
        new InteriorBuilding("maze_bank", "Maze Bank Tower", "Офисы", Office, -67.094f, -802.442f, 43.227f,
            new OnlineInterior("maze_bank_office", "Офис CEO", -75.847f, -826.989f, 243.386f, new[] { "ex_dt1_11_office_03c" },
                new InteriorSet("office_chairs")),
            new OnlineInterior("maze_bank_garage", "Гараж", -84.219f, -823.085f, 221.000f, new[] { "imp_dt1_11_cargarage_a" },
                new InteriorSet("garage_decor_01"), new InteriorSet("numbering_style01_n1"), new InteriorSet("lighting_option01"))),
        new InteriorBuilding("lombank", "Lombank West", "Офисы", Office, -1581.501f, -558.258f, 33.953f,
            new OnlineInterior("lombank_office", "Офис CEO", -1579.756f, -565.066f, 108.523f, new[] { "ex_sm_13_office_03c" },
                new InteriorSet("office_chairs")),
            new OnlineInterior("lombank_garage", "Гараж", -1581.112f, -567.245f, 85.500f, new[] { "imp_sm_13_cargarage_a" },
                new InteriorSet("garage_decor_01"), new InteriorSet("numbering_style01_n1"), new InteriorSet("lighting_option01"))),
        new InteriorBuilding("maze_bank_west", "Maze Bank West", "Офисы", Office, -1379.546f, -499.178f, 32.157f,
            new OnlineInterior("maze_bank_west_office", "Офис CEO", -1392.667f, -480.474f, 72.042f, new[] { "ex_sm_15_office_03c" },
                new InteriorSet("office_chairs")),
            new OnlineInterior("maze_bank_west_garage", "Гараж", -1388.860f, -478.757f, 48.100f, new[] { "imp_sm_15_cargarage_b" },
                new InteriorSet("garage_decor_01"), new InteriorSet("numbering_style01_n1"), new InteriorSet("lighting_option01"))),

        new InteriorBuilding("clubhouse_sandy", "Клуб байкеров, Сэнди-Шорс", "Байкеры", 492, 1737.878f, 3709.088f, 33.135f,
            new OnlineInterior("clubhouse_sandy_club", "Клуб", 1107.040f, -3157.399f, -37.519f, new[] { "bkr_biker_interior_placement_interior_0_biker_dlc_int_01_milo" },
                new InteriorSet("walls_02", 3), new InteriorSet("furnishings_01", 3), new InteriorSet("decorative_01"), new InteriorSet("mural_01"))),
        new InteriorBuilding("clubhouse_paleto", "Клуб байкеров, Палето-Бэй", "Байкеры", 492, -39.329f, 6420.603f, 30.702f,
            new OnlineInterior("clubhouse_paleto_club", "Клуб", 998.481f, -3164.711f, -38.907f, new[] { "bkr_biker_interior_placement_interior_1_biker_dlc_int_02_milo" },
                new InteriorSet("walls_01", 8), new InteriorSet("lower_walls_default", 8), new InteriorSet("furnishings_02", 8), new InteriorSet("decorative_02"), new InteriorSet("mural_09"), new InteriorSet("no_mod_booth"), new InteriorSet("no_gun_locker"))),
        new InteriorBuilding("biker_meth", "Лаборатория мета", "Байкеры", 499, 1181.440f, -3113.820f, 5.030f,
            new OnlineInterior("biker_meth_lab", "Лаборатория", 1009.500f, -3196.600f, -38.997f, new[] { "bkr_biker_interior_placement_interior_2_biker_dlc_int_ware01_milo" },
                new InteriorSet("meth_lab_empty"))),
        new InteriorBuilding("biker_weed", "Ферма травы", "Байкеры", 496, 102.140f, 175.260f, 103.560f,
            new OnlineInterior("biker_weed_farm", "Ферма", 1051.491f, -3196.536f, -39.148f, new[] { "bkr_biker_interior_placement_interior_3_biker_dlc_int_ware02_milo" },
                new InteriorSet("weed_upgrade_equip"), new InteriorSet("weed_low_security"), new InteriorSet("weed_drying"), new InteriorSet("weed_chairs"), new InteriorSet("weed_production"), new InteriorSet("weed_growtha_stage2"), new InteriorSet("light_growtha_stage23_standard"), new InteriorSet("weed_hosea"), new InteriorSet("weed_growthb_stage3"), new InteriorSet("light_growthb_stage23_standard"), new InteriorSet("weed_hoseb"), new InteriorSet("weed_growthc_stage2"), new InteriorSet("light_growthc_stage23_standard"), new InteriorSet("weed_hosec"), new InteriorSet("weed_growthd_stage3"), new InteriorSet("light_growthd_stage23_standard"), new InteriorSet("weed_hosed"), new InteriorSet("weed_growthe_stage2"), new InteriorSet("light_growthe_stage23_standard"), new InteriorSet("weed_hosee"), new InteriorSet("weed_growthf_stage3"), new InteriorSet("light_growthf_stage23_standard"), new InteriorSet("weed_hosef"), new InteriorSet("weed_growthg_stage2"), new InteriorSet("light_growthg_stage23_standard"), new InteriorSet("weed_hoseg"), new InteriorSet("weed_growthh_stage3"), new InteriorSet("light_growthh_stage23_standard"), new InteriorSet("weed_hoseh"), new InteriorSet("weed_growthi_stage3"), new InteriorSet("light_growthi_stage23_standard"), new InteriorSet("weed_hosei"))),
        new InteriorBuilding("biker_coke", "Склад кокаина", "Байкеры", 497, -1462.622f, -381.826f, 37.802f,
            new OnlineInterior("biker_coke_lockup", "Склад", 1093.600f, -3196.600f, -38.998f, new[] { "bkr_biker_interior_placement_interior_4_biker_dlc_int_ware03_milo" },
                new InteriorSet("set_up"), new InteriorSet("equipment_basic"), new InteriorSet("coke_press_basic"), new InteriorSet("production_basic"), new InteriorSet("table_equipment"))),
        new InteriorBuilding("biker_money", "Фабрика фальшивых денег", "Байкеры", 500, -1171.005f, -1380.922f, 3.937f,
            new OnlineInterior("biker_money_factory", "Фабрика", 1121.897f, -3195.338f, -40.403f, new[] { "bkr_biker_interior_placement_interior_5_biker_dlc_int_ware04_milo" },
                new InteriorSet("counterfeit_standard_equip"), new InteriorSet("counterfeit_security"), new InteriorSet("dryera_open"), new InteriorSet("dryerb_on"), new InteriorSet("dryerc_on"), new InteriorSet("dryerd_on"), new InteriorSet("money_cutter"), new InteriorSet("counterfeit_setup"), new InteriorSet("counterfeit_cashpile100b"), new InteriorSet("counterfeit_cashpile100a"), new InteriorSet("counterfeit_cashpile100d"), new InteriorSet("counterfeit_cashpile100c"))),
        new InteriorBuilding("biker_documents", "Мастерская документов", "Байкеры", 498, 299.071f, -759.072f, 28.333f,
            new OnlineInterior("biker_documents_office", "Мастерская", 1165.000f, -3196.600f, -39.013f, new[] { "bkr_biker_interior_placement_interior_6_biker_dlc_int_ware05_milo" },
                new InteriorSet("interior_basic"), new InteriorSet("security_low"), new InteriorSet("equipment_basic"), new InteriorSet("chair02"), new InteriorSet("chair01"), new InteriorSet("chair04"), new InteriorSet("chair03"), new InteriorSet("chair06"), new InteriorSet("chair05"), new InteriorSet("chair07"))),
        new InteriorBuilding("bunker", "Бункер", "Бизнесы", 557, 1571.730f, 2252.610f, 76.836f,
            new OnlineInterior("bunker_inside", "Бункер", 892.638f, -3245.866f, -98.265f, new[] { "gr_grdlc_interior_placement_interior_1_grdlc_int_02_milo_", "gr_case0_bunkerclosed", "gr_case1_bunkerclosed", "gr_case2_bunkerclosed", "gr_case3_bunkerclosed", "gr_case4_bunkerclosed", "gr_case5_bunkerclosed", "gr_case6_bunkerclosed", "gr_case7_bunkerclosed", "gr_case9_bunkerclosed", "gr_case10_bunkerclosed", "gr_case11_bunkerclosed" },
                new InteriorSet("Bunker_Style_A"), new InteriorSet("standard_bunker_set"), new InteriorSet("standard_security_set"), new InteriorSet("Office_Upgrade_set"), new InteriorSet("gun_locker_upgrade"), new InteriorSet("gun_range_lights"))),
        new InteriorBuilding("hangar", "Ангар в аэропорту", "Бизнесы", 569, -1153.875f, -3412.450f, 12.945f,
            new OnlineInterior("hangar_inside", "Ангар", -1267.000f, -3013.135f, -49.500f, new[] { "sm_smugdlc_interior_placement_interior_0_smugdlc_int_01_milo_" },
                new InteriorSet("set_tint_shell", 1), new InteriorSet("set_crane_tint", 1), new InteriorSet("set_floor_2"), new InteriorSet("set_floor_decal_1", 1), new InteriorSet("set_lighting_hangar_a"), new InteriorSet("set_lighting_wall_neutral"), new InteriorSet("set_lighting_tint_props", 2), new InteriorSet("set_modarea", 1), new InteriorSet("set_office_basic"), new InteriorSet("set_bedroom_modern"), new InteriorSet("set_bedroom_tint", 1), new InteriorSet("set_bedroom_blinds_open"))),
        new InteriorBuilding("facility", "База Судного дня", "Бизнесы", 590, 2.430f, 6831.906f, 11.473f,
            new OnlineInterior("facility_inside", "База", 345.000f, 4842.000f, -60.000f, new[] { "xm_hatch_01_cutscene", "xm_hatch_02_cutscene", "xm_hatch_03_cutscene", "xm_hatch_04_cutscene", "xm_hatch_06_cutscene", "xm_hatch_07_cutscene", "xm_hatch_08_cutscene", "xm_hatch_09_cutscene", "xm_hatch_10_cutscene", "xm_hatch_closed", "xm_siloentranceclosed_x17", "xm_bunkerentrance_door", "xm_hatches_terrain", "xm_hatches_terrain_lod", "xm_x17dlc_int_placement_interior_33_x17dlc_int_02_milo_" },
                new InteriorSet("set_int_02_shell", 1), new InteriorSet("set_int_02_decal_01"), new InteriorSet("set_int_02_lounge3", 1), new InteriorSet("set_int_02_sleep3", 1), new InteriorSet("set_int_02_security", 1), new InteriorSet("set_int_02_cannon", 1), new InteriorSet("Set_Int_02_Parts_Avenger2"), new InteriorSet("Set_Int_02_Parts_Avenger1"), new InteriorSet("Set_Int_02_Parts_Avenger3"), new InteriorSet("Set_Int_02_outfit_khanjali"), new InteriorSet("Set_Int_02_outfit_paramedic"), new InteriorSet("Set_Int_02_outfit_morgue"), new InteriorSet("Set_Int_02_outfit_steal_avenger"), new InteriorSet("Set_Int_02_outfit_serverfarm"), new InteriorSet("Set_Int_02_outfit_volatol"), new InteriorSet("Set_Int_02_outfit_sub_finale"), new InteriorSet("Set_Int_02_outfit_iaa"), new InteriorSet("Set_Int_02_outfit_riot_van"), new InteriorSet("Set_Int_02_outfit_foundry"), new InteriorSet("Set_Int_02_outfit_predator"), new InteriorSet("Set_Int_02_outfit_stromberg"), new InteriorSet("set_int_02_trophy1"), new InteriorSet("set_int_02_trophy_sub", 1), new InteriorSet("set_int_02_trophy_iaa"), new InteriorSet("set_int_02_clutter1"), new InteriorSet("set_int_02_clutter2"))),
        new InteriorBuilding("nightclub", "Ночной клуб", "Бизнесы", 614, 331.212f, -994.074f, 28.424f,
            new OnlineInterior("nightclub_inside", "Клуб", -1604.664f, -3012.583f, -78.000f, new[] { "ba_int_placement_ba_interior_0_dlc_int_01_ba_milo_", "ba_barriers_case0", "ba_case0_solomun", "ba_case0_dixon", "ba_case0_madonna", "ba_case0_taleofus", "ba_barriers_case1", "ba_case1_solomun", "ba_case1_dixon", "ba_case1_madonna", "ba_case1_taleofus", "ba_barriers_case2", "ba_case2_solomun", "ba_case2_dixon", "ba_case2_madonna", "ba_case2_taleofus", "ba_barriers_case3", "ba_case3_solomun", "ba_case3_dixon", "ba_case3_madonna", "ba_case3_taleofus", "ba_barriers_case4", "ba_case4_solomun", "ba_case4_dixon", "ba_case4_madonna", "ba_case4_taleofus", "ba_barriers_case5", "ba_case5_solomun", "ba_case5_dixon", "ba_case5_madonna", "ba_case5_taleofus", "ba_barriers_case6", "ba_case6_solomun", "ba_case6_dixon", "ba_case6_madonna", "ba_case6_taleofus", "ba_barriers_case7", "ba_case7_solomun", "ba_case7_dixon", "ba_case7_madonna", "ba_case7_taleofus", "ba_barriers_case8", "ba_case8_solomun", "ba_case8_dixon", "ba_case8_madonna", "ba_case8_taleofus", "ba_barriers_case9", "ba_case9_solomun", "ba_case9_dixon", "ba_case9_madonna", "ba_case9_taleofus" },
                new InteriorSet("Int01_ba_clubname_01"), new InteriorSet("Int01_ba_Style02"), new InteriorSet("Int01_ba_style02_podium"), new InteriorSet("Int01_ba_equipment_setup"), new InteriorSet("Int01_ba_equipment_upgrade"), new InteriorSet("Int01_ba_security_upgrade"), new InteriorSet("Int01_ba_dj01"), new InteriorSet("DJ_04_Lights_03"), new InteriorSet("Int01_ba_bar_content"), new InteriorSet("Int01_ba_booze_02"), new InteriorSet("Int01_ba_booze_01"), new InteriorSet("Int01_ba_booze_03"), new InteriorSet("Int01_ba_trophy01", 2))),
        new InteriorBuilding("arcade", "Аркада", "Бизнесы", 740, -1270.591f, -305.000f, 36.074f,
            new OnlineInterior("arcade_inside", "Аркада", 2732.000f, -380.000f, -50.000f, None,
                new InteriorSet("entity_set_constant_geometry"), new InteriorSet("entity_set_arcade_set_ceiling_flat"), new InteriorSet("entity_set_mural_option_05"), new InteriorSet("entity_set_floor_option_01"), new InteriorSet("entity_set_arcade_set_trophy_claw"), new InteriorSet("entity_set_arcade_set_trophy_teller"), new InteriorSet("entity_set_arcade_set_trophy_gunner"), new InteriorSet("entity_set_arcade_set_trophy_brawler"), new InteriorSet("entity_set_arcade_set_trophy_cabs"), new InteriorSet("entity_set_plushie_01"), new InteriorSet("entity_set_plushie_07"), new InteriorSet("entity_set_plushie_08"), new InteriorSet("entity_set_plushie_09"), new InteriorSet("entity_set_big_screen"), new InteriorSet("entity_set_screens"))),
        new InteriorBuilding("auto_shop", "Автомастерская", "Бизнесы", 779, 758.523f, -679.632f, 27.979f,
            new OnlineInterior("auto_shop_inside", "Мастерская", -1350.000f, 160.000f, -100.000f, new[] { "tr_tuner_shop_burton", "tr_tuner_shop_mesa", "tr_tuner_shop_mission", "tr_tuner_shop_rancho", "tr_tuner_shop_strawberry" },
                new InteriorSet("entity_set_car_lift_cutscene"), new InteriorSet("entity_set_train"), new InteriorSet("entity_set_scope"), new InteriorSet("entity_set_virus"), new InteriorSet("entity_set_lightbox"), new InteriorSet("entity_set_bedroom"), new InteriorSet("entity_set_style_9"), new InteriorSet("entity_set_laptop"), new InteriorSet("entity_set_plate"), new InteriorSet("entity_set_tints"), new InteriorSet("entity_set_jammers"), new InteriorSet("entity_set_IAA"), new InteriorSet("entity_set_car_lift_purchase"), new InteriorSet("entity_set_ecu"), new InteriorSet("entity_set_drive"), new InteriorSet("entity_set_thermal"), new InteriorSet("entity_set_car_lift_default"), new InteriorSet("entity_set_bombs"))),
        new InteriorBuilding("car_meet", "LS Car Meet", "Бизнесы", 777, 778.460f, -1870.152f, 28.295f,
            new OnlineInterior("car_meet_inside", "LS Car Meet", -2000.000f, 1113.211f, -25.362f, new[] { "tr_tuner_meetup", "tr_tuner_race_line" },
                new InteriorSet("entity_set_meet_crew"), new InteriorSet("entity_set_meet_lights"), new InteriorSet("entity_set_test_lights"), new InteriorSet("entity_set_player"), new InteriorSet("entity_set_time_trial"))),
        new InteriorBuilding("bail_office", "Офис поручителя", "Бизнесы", 893, -1415.176f, -656.989f, 27.673f,
            new OnlineInterior("bail_office_inside", "Офис", 565.886f, -2688.761f, -50.000f, new[] { "m24_1_bailoffice_davis", "m24_1_bailoffice_delperro", "m24_1_bailoffice_missionrow", "m24_1_bailoffice_paletobay", "m24_1_bailoffice_vinewood" },
                new InteriorSet("set_style_03"), new InteriorSet("set_no_staff"), new InteriorSet("set_gunsafe_off"), new InteriorSet("set_trophy_10x"), new InteriorSet("set_trophy_24x"), new InteriorSet("set_trophy_100x"), new InteriorSet("set_new_plant"))),
        new InteriorBuilding("salvage_yard", "Свалка", "Бизнесы", 867, -12.710f, -1311.688f, 28.304f,
            new OnlineInterior("salvage_yard_inside", "Свалка", 1077.276f, -2274.876f, -50.000f, new[] { "m23_2_sp1_03_reds", "m23_2_sc1_03_reds", "m23_2_id2_04_reds", "m23_2_cs1_05_reds", "m23_2_cs4_11_reds" },
                new InteriorSet("set_tint_b", 1), new InteriorSet("set_mechanic_upgrade"), new InteriorSet("set_safe_upgrade"), new InteriorSet("set_car_lift_01_up"), new InteriorSet("set_car_lift_02_up"))),
        new InteriorBuilding("car_wash", "Автомойка Hands On", "Бизнесы", 931, 25.635f, -1409.308f, 28.333f,
            new OnlineInterior("car_wash_inside", "Автомойка", 26.074f, -1398.979f, -75.000f, new[] { "m25_1_carwash" },
                new InteriorSet("set_carwash_tints"))).RequiresDlc("mp2025_01"),
        new InteriorBuilding("music_studio", "Record A Studios", "Бизнесы", 819, -839.668f, -228.132f, 36.253f,
            new OnlineInterior("music_studio_inside", "Студия", -1010.220f, -52.210f, -100.000f, new[] { "sf_int_placement_sec_interior_1_dlc_studio_sec_milo_" },
                new InteriorSet("Entity_Set_Fire"), new InteriorSet("entity_set_default"))),
        new InteriorBuilding("music_locker", "Music Locker", "Бизнесы", 136, 988.800f, 79.600f, 80.990f,
            new OnlineInterior("music_locker_inside", "Клуб", 1550.000f, 250.000f, -49.000f, new[] { "h4_clubposter_palmstraxx", "h4_clubposter_moodymann", "h4_clubposter_keinemusik" },
                new InteriorSet("int01_ba_security_upgrade"), new InteriorSet("int01_ba_equipment_setup"), new InteriorSet("int01_ba_style02_podium"), new InteriorSet("int01_ba_dj01"), new InteriorSet("int01_ba_bar_content"), new InteriorSet("int01_ba_lights_screen"), new InteriorSet("dj_04_lights_04"))),
        new InteriorBuilding("freakshop", "Freakshop", "Бизнесы", 847, 580.219f, -421.294f, 23.730f,
            new OnlineInterior("freakshop_inside", "Freakshop", 570.971f, -420.073f, -70.000f, new[] { "xm3_warehouse", "xm3_warehouse_grnd" },
                new InteriorSet("entity_set_roller_door_closed"))),
        new InteriorBuilding("ceo_warehouse_small", "Склад, Вест-Вайнвуд", "Склады", 473, -424.828f, 185.825f, 79.775f,
            new OnlineInterior("ceo_warehouse_small_inside", "Малый склад", 1094.988f, -3101.776f, -39.000f, None)),
        new InteriorBuilding("ceo_warehouse_medium", "Склад, Дель-Перро", "Склады", 473, -1268.119f, -812.274f, 16.108f,
            new OnlineInterior("ceo_warehouse_medium_inside", "Средний склад", 1056.486f, -3105.724f, -39.000f, None)),
        new InteriorBuilding("ceo_warehouse_large", "Склад, Ла-Меса", "Склады", 473, 759.566f, -909.466f, 24.244f,
            new OnlineInterior("ceo_warehouse_large_inside", "Большой склад", 1006.967f, -3102.079f, -39.004f, None)),
        new InteriorBuilding("vehicle_warehouse", "Склад машин, Строберри", "Склады", 524, 36.871f, -1284.028f, 28.292f,
            new OnlineInterior("vehicle_warehouse_upper", "Склад", 994.593f, -3002.594f, -39.647f, new[] { "imp_impexp_interior_placement_interior_1_impexp_intwaremed_milo_" },
                new InteriorSet("branded_style_set"), new InteriorSet("car_floor_hatch")),
            new OnlineInterior("vehicle_warehouse_lower", "Мастерская внизу", 969.538f, -3000.411f, -48.647f, new[] { "imp_impexp_interior_placement_interior_3_impexp_int_02_milo_" },
                new InteriorSet("pump_01"), new InteriorSet("pump_02"), new InteriorSet("pump_03"), new InteriorSet("pump_04"), new InteriorSet("pump_05"), new InteriorSet("pump_06"), new InteriorSet("pump_07"), new InteriorSet("pump_08"))),
        new InteriorBuilding("eclipse_garage", "Гараж на Eclipse Boulevard", "Гаражи", 856, -274.241f, 282.802f, 89.022f,
            new OnlineInterior("eclipse_garage_inside", "Гараж", 519.248f, -2618.788f, -50.000f, new[] { "xm3_garage_fix" },
                new InteriorSet("entity_set_numbers_01"), new InteriorSet("entity_set_shell_01"), new InteriorSet("entity_set_tint_01", 1))),
        new InteriorBuilding("agency_rockford", "Агентство, Рокфорд-Хиллз", "Агентства", 826, -1028.914f, -410.970f, 32.287f,
            new OnlineInterior("agency_rockford_office", "Офис агентства", -1021.861f, -427.746f, 68.958f, new[] { "sf_fixeroffice_bh1_05" },
                new InteriorSet("Entity_Set_M_Motel"), new InteriorSet("Entity_Set_M_Billionaire"), new InteriorSet("Entity_Set_Standard_Office"), new InteriorSet("Entity_Set_M_Taxi"), new InteriorSet("Entity_Set_M_Trip_03"), new InteriorSet("Entity_Set_Moving"), new InteriorSet("Entity_Set_Spare_Seats"), new InteriorSet("Entity_Set_M_Vehicles"), new InteriorSet("Entity_Set_Wpaper_8"), new InteriorSet("Entity_Set_Player_Desk"), new InteriorSet("Entity_Set_Art_1"), new InteriorSet("Entity_Set_M_Hit_List"), new InteriorSet("Entity_Set_M_Attack"), new InteriorSet("Entity_set_disc_01"), new InteriorSet("Entity_Set_M_Setup"), new InteriorSet("Entity_Set_M_Gone_Golfing"), new InteriorSet("Entity_Set_M_50"), new InteriorSet("Entity_Set_M_Fire_Booth"), new InteriorSet("Entity_Set_M_Families"), new InteriorSet("Entity_Set_M_Trip_02"), new InteriorSet("Entity_Set_Player_Seats"), new InteriorSet("Entity_Set_M_Trip_01"), new InteriorSet("Entity_Set_M_Tuner"), new InteriorSet("Entity_Set_M_Ballas"), new InteriorSet("Entity_Set_M_The_Way"), new InteriorSet("Entity_Set_M_Hood"), new InteriorSet("Entity_Set_M_Yacht"), new InteriorSet("Entity_Set_M_Construction"), new InteriorSet("Entity_Set_M_Limo_Wallet"), new InteriorSet("Entity_Set_M_Limo_Photo"), new InteriorSet("Entity_Set_M_Promoter"), new InteriorSet("Entity_Set_M_Nightclub"), new InteriorSet("Entity_Set_M_Golf_Intro"), new InteriorSet("Entity_Set_Tint_AG"))),
        new InteriorBuilding("agency_hawick", "Агентство, Хавик", "Агентства", 826, 356.528f, -79.623f, 66.109f,
            new OnlineInterior("agency_hawick_office", "Офис агентства", 383.416f, -59.878f, 108.460f, new[] { "sf_fixeroffice_hw1_08" },
                new InteriorSet("Entity_Set_Armoury"), new InteriorSet("Entity_Set_M_Motel"), new InteriorSet("Entity_Set_M_Billionaire"), new InteriorSet("Entity_Set_M_Taxi"), new InteriorSet("Entity_Set_M_Trip_03"), new InteriorSet("Entity_Set_Wpaper_9"), new InteriorSet("Entity_Set_Moving"), new InteriorSet("Entity_Set_Spare_Seats"), new InteriorSet("Entity_Set_M_Vehicles"), new InteriorSet("Entity_Set_Player_Desk"), new InteriorSet("Entity_Set_M_Hit_List"), new InteriorSet("Entity_Set_M_Attack"), new InteriorSet("Entity_Set_M_Setup"), new InteriorSet("Entity_Set_M_Gone_Golfing"), new InteriorSet("Entity_Set_M_50"), new InteriorSet("Entity_set_disc_02"), new InteriorSet("Entity_Set_M_Fire_Booth"), new InteriorSet("Entity_Set_M_Families"), new InteriorSet("Entity_Set_M_Trip_02"), new InteriorSet("Entity_Set_Player_Seats"), new InteriorSet("Entity_Set_M_Trip_01"), new InteriorSet("Entity_Set_Art_2"), new InteriorSet("Entity_Set_M_Tuner"), new InteriorSet("Entity_Set_M_Ballas"), new InteriorSet("Entity_Set_M_The_Way"), new InteriorSet("Entity_Set_M_Hood"), new InteriorSet("Entity_Set_M_Yacht"), new InteriorSet("Entity_Set_M_Construction"), new InteriorSet("Entity_Set_M_Limo_Wallet"), new InteriorSet("Entity_Set_M_Limo_Photo"), new InteriorSet("Entity_Set_M_Promoter"), new InteriorSet("Entity_Set_M_Nightclub"), new InteriorSet("Entity_Set_M_Golf_Intro"), new InteriorSet("Entity_Set_Tint_AG"))),
        new InteriorBuilding("agency_vespucci", "Агентство, Веспуччи", "Агентства", 826, -991.021f, -763.971f, 14.773f,
            new OnlineInterior("agency_vespucci_office", "Офис агентства", -1004.230f, -761.208f, 66.991f, new[] { "sf_fixeroffice_kt1_05" },
                new InteriorSet("Entity_Set_M_Motel"), new InteriorSet("Entity_Set_M_Billionaire"), new InteriorSet("Entity_Set_Standard_Office"), new InteriorSet("Entity_Set_M_Taxi"), new InteriorSet("Entity_Set_Wpaper_2"), new InteriorSet("Entity_Set_M_Trip_03"), new InteriorSet("Entity_Set_Moving"), new InteriorSet("Entity_Set_Art_3"), new InteriorSet("Entity_Set_Spare_Seats"), new InteriorSet("Entity_Set_M_Vehicles"), new InteriorSet("Entity_Set_Player_Desk"), new InteriorSet("Entity_Set_M_Hit_List"), new InteriorSet("Entity_Set_M_Attack"), new InteriorSet("Entity_Set_M_Setup"), new InteriorSet("Entity_Set_M_Gone_Golfing"), new InteriorSet("Entity_Set_M_50"), new InteriorSet("Entity_set_disc_02"), new InteriorSet("Entity_Set_M_Fire_Booth"), new InteriorSet("Entity_Set_M_Families"), new InteriorSet("Entity_Set_M_Trip_02"), new InteriorSet("Entity_Set_Player_Seats"), new InteriorSet("Entity_Set_M_Trip_01"), new InteriorSet("Entity_Set_M_Tuner"), new InteriorSet("Entity_Set_M_Ballas"), new InteriorSet("Entity_Set_M_The_Way"), new InteriorSet("Entity_Set_M_Hood"), new InteriorSet("Entity_Set_M_Yacht"), new InteriorSet("Entity_Set_M_Construction"), new InteriorSet("Entity_Set_M_Limo_Wallet"), new InteriorSet("Entity_Set_M_Limo_Photo"), new InteriorSet("Entity_Set_M_Promoter"), new InteriorSet("Entity_Set_M_Nightclub"), new InteriorSet("Entity_Set_M_Golf_Intro"), new InteriorSet("Entity_Set_Tint_AG"))),
        new InteriorBuilding("agency_little_seoul", "Агентство, Литл-Сеул", "Агентства", 826, -614.924f, -736.252f, 26.883f,
            new OnlineInterior("agency_little_seoul_office", "Офис агентства", -587.872f, -716.849f, 118.102f, new[] { "sf_fixeroffice_kt1_08" },
                new InteriorSet("Entity_Set_Armoury"), new InteriorSet("Entity_Set_M_Motel"), new InteriorSet("Entity_Set_M_Billionaire"), new InteriorSet("Entity_Set_M_Taxi"), new InteriorSet("Entity_Set_M_Trip_03"), new InteriorSet("Entity_Set_Moving"), new InteriorSet("Entity_Set_Spare_Seats"), new InteriorSet("Entity_Set_M_Vehicles"), new InteriorSet("Entity_Set_Player_Desk"), new InteriorSet("Entity_Set_Art_1"), new InteriorSet("Entity_Set_M_Hit_List"), new InteriorSet("Entity_Set_M_Attack"), new InteriorSet("Entity_Set_M_Setup"), new InteriorSet("Entity_Set_M_Gone_Golfing"), new InteriorSet("Entity_Set_M_50"), new InteriorSet("Entity_Set_M_Fire_Booth"), new InteriorSet("Entity_Set_M_Families"), new InteriorSet("Entity_set_disc_05"), new InteriorSet("Entity_Set_M_Trip_02"), new InteriorSet("Entity_Set_Player_Seats"), new InteriorSet("Entity_Set_M_Trip_01"), new InteriorSet("Entity_Set_M_Tuner"), new InteriorSet("Entity_Set_M_Ballas"), new InteriorSet("Entity_Set_M_The_Way"), new InteriorSet("Entity_Set_M_Hood"), new InteriorSet("Entity_Set_Wpaper_3"), new InteriorSet("Entity_Set_M_Yacht"), new InteriorSet("Entity_Set_M_Construction"), new InteriorSet("Entity_Set_M_Limo_Wallet"), new InteriorSet("Entity_Set_M_Limo_Photo"), new InteriorSet("Entity_Set_M_Promoter"), new InteriorSet("Entity_Set_M_Nightclub"), new InteriorSet("Entity_Set_M_Golf_Intro"), new InteriorSet("Entity_Set_Tint_AG"))),
        new InteriorBuilding("mansion_vinewood", "Особняк, Вайнвуд-Хиллз", "Особняки", 948, 542.665f, 773.172f, 190.247f,
            new OnlineInterior("mansion_vinewood_house", "Особняк", 543.852f, 712.754f, 201.000f, new[] { "m25_2_ch2_04_mansion_interior_a", "m25_2_ch2_04_mansion_interior_b", "m25_2_ch2_04_mansion_interior_c", "apa_ch2_04_mansion_shared", "apa_ch2_04_mansion_private", "apa_ch2_04_mansion_furniture", "apa_ch2_04_mansion_firepit", "apa_ch2_04_mansion_railings_p", "m25_2_east_mansion_gym", "m25_2_east_dog_house", "m25_2_mansion_props", "hei_ch1_roads_mansion", "apa_ch2_04_mansion_shared_distantlights", "apa_ch2_04_mansion_shared_lodlights" },
                new InteriorSet("set_style_cali"), new InteriorSet("set_elev_cali"), new InteriorSet("set_art_coastal"), new InteriorSet("set_trophy_planter"), new InteriorSet("set_shelving_planter"), new InteriorSet("set_wallpaper_deco"), new InteriorSet("set_style_cali_tint", 0), new InteriorSet("set_ai_tablets_03"), new InteriorSet("set_pet_cat"), new InteriorSet("set_pet_dog"))).RequiresDlc("mp2025_02"),
        new InteriorBuilding("mansion_richman", "Особняк, Ричман", "Особняки", 948, -1688.441f, 489.054f, 115.037f,
            new OnlineInterior("mansion_richman_house", "Особняк", -1630.434f, 470.852f, 128.000f, new[] { "hei_ch1_06e_mansion_shared", "hei_ch1_06f_mansion_shared", "m25_2_ch1_06e_mansion_interior_a", "m25_2_ch1_06e_mansion_interior_b", "m25_2_ch1_06e_mansion_interior_c", "hei_ch1_06e_mansion_private", "hei_ch1_06e_mansion_furniture", "hei_ch1_06e_mansion_firepit", "hei_ch1_06e_mansion_railings_p", "m25_2_mansion_gym", "m25_2_dog_house", "m25_2_mansion_props", "hei_ch1_roads_mansion", "hei_ch1_06e_mansion_shared_distantlights", "hei_ch1_06e_mansion_shared_lodlights" },
                new InteriorSet("set_style_cali"), new InteriorSet("set_elev_cali"), new InteriorSet("set_art_coastal"), new InteriorSet("set_trophy_planter"), new InteriorSet("set_shelving_planter"), new InteriorSet("set_wallpaper_deco"), new InteriorSet("set_style_cali_tint", 0), new InteriorSet("set_ai_tablets_03"), new InteriorSet("set_pet_cat"), new InteriorSet("set_pet_dog"))).RequiresDlc("mp2025_02"),
        new InteriorBuilding("mansion_tongva", "Особняк, Тонгва", "Особняки", 948, -2558.092f, 1910.113f, 155.860f,
            new OnlineInterior("mansion_tongva_house", "Особняк", -2601.712f, 1874.826f, 166.000f, new[] { "hei_ch1_09_mansion_shared", "m25_2_ch1_09_mansion_interior_a", "m25_2_ch1_09_mansion_interior_b", "m25_2_ch1_09_mansion_interior_c", "hei_ch1_09_mansion_private", "hei_ch1_09_mansion_furniture", "hei_ch1_09_mansion_firepit", "hei_ch1_09_mansion_railings_p", "m25_2_tongva_mansion_gym", "m25_2_tongva_dog_house", "m25_2_mansion_props", "hei_ch1_roads_mansion", "hei_ch1_09_mansion_shared_distantlights", "hei_ch1_09_mansion_shared_lodlights" },
                new InteriorSet("set_style_cali"), new InteriorSet("set_elev_cali"), new InteriorSet("set_art_coastal"), new InteriorSet("set_trophy_planter"), new InteriorSet("set_shelving_planter"), new InteriorSet("set_wallpaper_deco"), new InteriorSet("set_style_cali_tint", 0), new InteriorSet("set_ai_tablets_03"), new InteriorSet("set_pet_cat"), new InteriorSet("set_pet_dog"))).RequiresDlc("mp2025_02"),
    };

    internal static readonly OnlineInterior[] All = Buildings.SelectMany(building => building.Interiors).ToArray();

    static OnlineInteriors()
    {
        for (int index = 0; index < All.Length; index++) All[index].Index = index;
    }
}

// Steps into a door circle once per approach: the player must first be farther
// than ArmDistance, so arriving inside a marker (after a teleport) never fires it.
internal sealed class DoorTrigger
{
    internal const float Radius = 1.2f;
    internal const float ArmDistance = 2.5f;
    private bool _armed;

    internal bool Step(float distance)
    {
        if (!(distance <= ArmDistance))
        {
            _armed = true;
            return false;
        }

        if (!_armed || distance > Radius) return false;
        _armed = false;
        return true;
    }

    internal void Disarm() => _armed = false;
}

// Loads an interior's map parts and furnishing while someone is near its
// entrance or inside it (or teleports there), and unloads it ten seconds after
// they leave: loading every interior at once would fill the game's map pools.
// Interiors exist only on the Online map. Game thread only.
internal sealed class InteriorRuntime
{
    internal const float NearEntrance = 150f;
    internal const float NearInside = 120f;
    internal const long LeaveMilliseconds = 10_000;
    internal const long TargetMilliseconds = 30_000;

    private readonly OnlineInterior[] _interiors;
    private readonly bool[] _loaded;
    private readonly bool[] _furnished;
    private readonly long[] _lastWanted;
    private int _target = -1;
    private long _targetUntil;

    internal InteriorRuntime(OnlineInterior[] interiors)
    {
        _interiors = interiors;
        _loaded = new bool[interiors.Length];
        _furnished = new bool[interiors.Length];
        _lastWanted = new long[interiors.Length];
    }

    internal bool IsLoaded(int index) => _loaded[index];
    internal bool IsFurnished(int index) => _furnished[index];
    internal bool HasState => _target >= 0 || _loaded.Any(loaded => loaded);

    internal void Target(int index, long now)
    {
        _target = index;
        _targetUntil = now + TargetMilliseconds;
        _lastWanted[index] = now;
        if (!_loaded[index]) Load(index);
    }

    internal void Tick(long now, Vector3? local)
    {
        if (_target >= 0 && now > _targetUntil) _target = -1;
        for (int index = 0; index < _interiors.Length; index++)
        {
            if (_target == index || (local.HasValue && IsNear(_interiors[index], local.Value)))
            {
                _lastWanted[index] = now;
                if (!_loaded[index]) Load(index);
                if (!_furnished[index]) Furnish(index);
            }
            else if (_loaded[index] && now - _lastWanted[index] >= LeaveMilliseconds)
            {
                Unload(index);
            }
        }
    }

    internal void Reset()
    {
        _target = -1;
        for (int index = 0; index < _interiors.Length; index++)
        {
            if (_loaded[index]) Unload(index);
        }
    }

    internal static bool IsNear(OnlineInterior interior, Vector3 position) =>
        Within(position, interior.X, interior.Y, interior.Z, NearInside, threeD: true) ||
        Within(position, interior.Building.X, interior.Building.Y, interior.Building.Z, NearEntrance, threeD: false);

    private static bool Within(Vector3 position, float x, float y, float z, float reach, bool threeD)
    {
        float dx = position.X - x, dy = position.Y - y, dz = threeD ? position.Z - z : 0f;
        return Finite.Is(position) && dx * dx + dy * dy + dz * dz <= reach * reach;
    }

    private void Load(int index)
    {
        _loaded[index] = true;
        foreach (string ipl in _interiors[index].Ipls) SpecialPlaceNatives.RequestIpl(ipl);
        _furnished[index] = _interiors[index].Sets.Length == 0;
    }

    // Furnishing needs the interior streamed in; retried each tick until it is.
    private void Furnish(int index)
    {
        OnlineInterior interior = _interiors[index];
        int id = InteriorNatives.InteriorAt(interior.X, interior.Y, interior.Z);
        if (id == 0 || !InteriorNatives.IsReady(id)) return;
        foreach (InteriorSet set in interior.Sets)
        {
            InteriorNatives.Activate(id, set.Name);
            if (set.Tint >= 0) InteriorNatives.Tint(id, set.Name, set.Tint);
        }

        InteriorNatives.Refresh(id);
        _furnished[index] = true;
    }

    private void Unload(int index)
    {
        OnlineInterior interior = _interiors[index];
        if (interior.Sets.Length > 0)
        {
            int id = InteriorNatives.InteriorAt(interior.X, interior.Y, interior.Z);
            if (id != 0)
            {
                foreach (InteriorSet set in interior.Sets) InteriorNatives.Deactivate(id, set.Name);
            }
        }

        foreach (string ipl in interior.Ipls) SpecialPlaceNatives.RemoveIpl(ipl);
        _loaded[index] = false;
        _furnished[index] = false;
        if (_target == index) _target = -1;
    }
}

// GTA boundary; isolated checks replace these calls.
internal static class InteriorNatives
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int InteriorAt(float x, float y, float z) => Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, x, y, z);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsReady(int interior) => Function.Call<bool>(Hash.IS_INTERIOR_READY, interior);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Activate(int interior, string set) => Function.Call((Hash)0x55E86AF2712B36A1UL, interior, set); // ACTIVATE_INTERIOR_ENTITY_SET

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Deactivate(int interior, string set) => Function.Call(Hash.DEACTIVATE_INTERIOR_ENTITY_SET, interior, set);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Tint(int interior, string set, int tint) => Function.Call((Hash)0xC1F1920BAF281317UL, interior, set, tint); // SET_INTERIOR_ENTITY_SET_TINT_INDEX

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Refresh(int interior) => Function.Call(Hash.REFRESH_INTERIOR, interior);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsDlcPresent(string dlc) => Function.Call<bool>((Hash)0x812595A0644CE1DEUL, unchecked((int)GTA.StringHash.AtStringHash(dlc))); // IS_DLC_PRESENT
}
