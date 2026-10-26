using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OGCoop.Shared;
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
    // The outside of the building in the Online map: its walls, hatches and
    // the ground around them (bob74_ipl's Exterior). The Online map has holes
    // there without them, so they stay on while it is on, never taken away.
    // Ipls are only the rooms, loaded when wanted.
    internal string[] World { get; private set; } = Array.Empty<string>();
    // A lighter building GTA shows from afar (the mansions, whose own one has
    // no distant view); off close by.
    internal string? Generic { get; private set; }
    // обновление Online, без которого этой комнаты нет, хотя здание есть
    internal string? Dlc { get; private set; }
    // въезд и выезд на машине: тестовая трасса
    internal bool ByCar { get; private set; }

    internal OnlineInterior Outside(string? generic, params string[] world)
    {
        Generic = generic;
        World = world;
        return this;
    }

    internal OnlineInterior RequiresDlc(string dlc)
    {
        Dlc = dlc;
        return this;
    }

    internal OnlineInterior Drivable()
    {
        ByCar = true;
        return this;
    }
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
        IconName = name;
        Doors = new[] { new InteriorDoor(this, 0, new Vector3(x, y, z)) };
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
    // первая дверь главная, остальные как в Online в других районах
    internal InteriorDoor[] Doors { get; private set; }
    internal string IconName { get; private set; }
    // особое место, на котором стоит здание (остров), -1 в городе
    internal int Place { get; private set; } = -1;

    internal InteriorBuilding RequiresDlc(string dlc)
    {
        Dlc = dlc;
        return this;
    }

    internal InteriorBuilding OnPlace(int place)
    {
        Place = place;
        return this;
    }

    internal InteriorBuilding AlsoAt(string iconName, params Vector3[] doors)
    {
        IconName = iconName;
        Doors = Doors.Concat(doors.Select((at, index) => new InteriorDoor(this, index + 1, at))).ToArray();
        return this;
    }
}

internal sealed class InteriorDoor
{
    internal InteriorDoor(InteriorBuilding building, int number, Vector3 position)
    {
        Building = building;
        Number = number;
        Position = position;
    }

    internal InteriorBuilding Building { get; }
    internal int Number { get; }
    internal Vector3 Position { get; }
    internal string Key => Number == 0 ? Building.Key : Building.Key + "_" + Number;
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
// CEO garages open from their office towers. Icons: the FiveM blip list source.
// у бизнесов все входы Online, у жилья и офисов по одному
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
        900, 925, 428, 928, 903, 959, // швейная фабрика, Smoke on the Water, особняк Эль Рубио, Helitours, ангар McKenzie, Kortz Center
    };

    private static readonly string[] None = Array.Empty<string>();

    // мастерская Kortz, одна на три особняка, в неё ходят из особняка (am_mp_mansion_art_workshop, обстановка bob74_ipl KortzWorkshop)
    private static OnlineInterior ArtWorkshop(string key) =>
        new OnlineInterior(key, "Мастерская", 2583.847f, 5965.467f, -50f, None,
            new InteriorSet("set_vault_door"), new InteriorSet("set_hacking_usb"), new InteriorSet("set_vault_hacking_angel"),
            new InteriorSet("set_chair_easel"), new InteriorSet("set_hazmat"), new InteriorSet("set_drills")).RequiresDlc("mp2026_01");

    // один гараж с мастерской на все четыре агентства, как в Online (bob74_ipl MpSecurityGarage)
    private static OnlineInterior AgencyGarage(string key) =>
        new OnlineInterior(key, "Гараж агентства", -1071.439f, -77.034f, -93.526f, new[] { "sf_int_placement_sec_interior_2_dlc_garage_sec_milo_" },
            new InteriorSet("entity_set_tints"), new InteriorSet("Entity_Set_Wallpaper_07"), new InteriorSet("Entity_Set_Workshop_Lights"));

    internal static readonly InteriorBuilding[] Buildings =
    {
        new InteriorBuilding("casino", "Казино Diamond", "Казино", Casino, 924.021f, 45.981f, 80.096f,
            new OnlineInterior("casino_floor", "Зал казино", 1105.483f, 220.212f, -48.99f, None)
                .Outside(null, "vw_casino_main", "hei_dlc_windows_casino", "hei_dlc_casino_aircon", "vw_dlc_casino_door", "hei_dlc_casino_door"),
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
                new InteriorSet("garage_decor_01"), new InteriorSet("numbering_style01_n1"), new InteriorSet("lighting_option01")),
            new OnlineInterior("arcadius_workshop", "Мастерская", -146.617f, -596.630f, 166.000f, new[] { "imp_dt1_02_modgarage" })),
        new InteriorBuilding("maze_bank", "Maze Bank Tower", "Офисы", Office, -67.094f, -802.442f, 43.227f,
            new OnlineInterior("maze_bank_office", "Офис CEO", -75.847f, -826.989f, 243.386f, new[] { "ex_dt1_11_office_03c" },
                new InteriorSet("office_chairs")),
            new OnlineInterior("maze_bank_garage", "Гараж", -84.219f, -823.085f, 221.000f, new[] { "imp_dt1_11_cargarage_a" },
                new InteriorSet("garage_decor_01"), new InteriorSet("numbering_style01_n1"), new InteriorSet("lighting_option01")),
            new OnlineInterior("maze_bank_workshop", "Мастерская", -73.904f, -821.620f, 284.000f, new[] { "imp_dt1_11_modgarage" })),
        new InteriorBuilding("lombank", "Lombank West", "Офисы", Office, -1581.501f, -558.258f, 33.953f,
            new OnlineInterior("lombank_office", "Офис CEO", -1579.756f, -565.066f, 108.523f, new[] { "ex_sm_13_office_03c" },
                new InteriorSet("office_chairs")),
            new OnlineInterior("lombank_garage", "Гараж", -1581.112f, -567.245f, 85.500f, new[] { "imp_sm_13_cargarage_a" },
                new InteriorSet("garage_decor_01"), new InteriorSet("numbering_style01_n1"), new InteriorSet("lighting_option01")),
            new OnlineInterior("lombank_workshop", "Мастерская", -1578.023f, -576.425f, 104.200f, new[] { "imp_sm_13_modgarage" })),
        new InteriorBuilding("maze_bank_west", "Maze Bank West", "Офисы", Office, -1379.546f, -499.178f, 32.157f,
            new OnlineInterior("maze_bank_west_office", "Офис CEO", -1392.667f, -480.474f, 72.042f, new[] { "ex_sm_15_office_03c" },
                new InteriorSet("office_chairs")),
            new OnlineInterior("maze_bank_west_garage", "Гараж", -1388.860f, -478.757f, 48.100f, new[] { "imp_sm_15_cargarage_b" },
                new InteriorSet("garage_decor_01"), new InteriorSet("numbering_style01_n1"), new InteriorSet("lighting_option01")),
            new OnlineInterior("maze_bank_west_workshop", "Мастерская", -1391.245f, -473.964f, 77.200f, new[] { "imp_sm_15_modgarage" })),

        new InteriorBuilding("clubhouse_sandy", "Клуб байкеров 1", "Байкеры", 492, 1737.878f, 3709.088f, 33.135f,
            new OnlineInterior("clubhouse_sandy_club", "Клуб", 1107.040f, -3157.399f, -37.519f, new[] { "bkr_biker_interior_placement_interior_0_biker_dlc_int_01_milo" },
                new InteriorSet("walls_02", 3), new InteriorSet("furnishings_01", 3), new InteriorSet("decorative_01"), new InteriorSet("mural_01")))
            // клубы 91-96 ведут в bkr_biker_dlc_int_01, 97-102 в int_02 (am_mp_property_ext func_6731, am_mp_hangar)
            .AlsoAt("Клуб байкеров", new Vector3(254.189f, -1809.183f, 26.181f), new Vector3(-1472.278f, -920.068f, 8.968f),
                new Vector3(38.948f, -1026.629f, 28.635f), new Vector3(46.903f, 2789.825f, 57.112f), new Vector3(-342.325f, 6065.316f, 30.490f)),
        new InteriorBuilding("clubhouse_paleto", "Клуб байкеров 2", "Байкеры", 492, -39.329f, 6420.603f, 30.702f,
            new OnlineInterior("clubhouse_paleto_club", "Клуб", 998.481f, -3164.711f, -38.907f, new[] { "bkr_biker_interior_placement_interior_1_biker_dlc_int_02_milo" },
                new InteriorSet("walls_01", 8), new InteriorSet("lower_walls_default", 8), new InteriorSet("furnishings_02", 8), new InteriorSet("decorative_02"), new InteriorSet("mural_09"), new InteriorSet("no_mod_booth"), new InteriorSet("no_gun_locker")))
            .AlsoAt("Клуб байкеров", new Vector3(939.716f, -1459.204f, 30.467f), new Vector3(189.762f, 309.749f, 104.471f),
                new Vector3(-21.959f, -191.362f, 51.560f), new Vector3(2472.220f, 4110.493f, 36.963f), new Vector3(-1134.762f, -1568.848f, 3.408f)),
        new InteriorBuilding("biker_meth", "Лаборатория мета", "Байкеры", 499, 1181.440f, -3113.820f, 5.030f,
            new OnlineInterior("biker_meth_lab", "Лаборатория", 1009.500f, -3196.600f, -38.997f, new[] { "bkr_biker_interior_placement_interior_2_biker_dlc_int_ware01_milo" },
                new InteriorSet("meth_lab_empty")))
            // бизнесы 22-41, по четыре каждого вида (func_6541)
            .AlsoAt("Лаборатория мета", new Vector3(52.903f, 6338.585f, 30.350f), new Vector3(1454.671f, -1651.986f, 66.000f), new Vector3(201.891f, 2461.782f, 54.689f)),
        new InteriorBuilding("biker_weed", "Ферма травы", "Байкеры", 496, 102.140f, 175.260f, 103.560f,
            new OnlineInterior("biker_weed_farm", "Ферма", 1051.491f, -3196.536f, -39.148f, new[] { "bkr_biker_interior_placement_interior_3_biker_dlc_int_ware02_milo" },
                new InteriorSet("weed_upgrade_equip"), new InteriorSet("weed_low_security"), new InteriorSet("weed_drying"), new InteriorSet("weed_chairs"), new InteriorSet("weed_production"), new InteriorSet("weed_growtha_stage2"), new InteriorSet("light_growtha_stage23_standard"), new InteriorSet("weed_hosea"), new InteriorSet("weed_growthb_stage3"), new InteriorSet("light_growthb_stage23_standard"), new InteriorSet("weed_hoseb"), new InteriorSet("weed_growthc_stage2"), new InteriorSet("light_growthc_stage23_standard"), new InteriorSet("weed_hosec"), new InteriorSet("weed_growthd_stage3"), new InteriorSet("light_growthd_stage23_standard"), new InteriorSet("weed_hosed"), new InteriorSet("weed_growthe_stage2"), new InteriorSet("light_growthe_stage23_standard"), new InteriorSet("weed_hosee"), new InteriorSet("weed_growthf_stage3"), new InteriorSet("light_growthf_stage23_standard"), new InteriorSet("weed_hosef"), new InteriorSet("weed_growthg_stage2"), new InteriorSet("light_growthg_stage23_standard"), new InteriorSet("weed_hoseg"), new InteriorSet("weed_growthh_stage3"), new InteriorSet("light_growthh_stage23_standard"), new InteriorSet("weed_hoseh"), new InteriorSet("weed_growthi_stage3"), new InteriorSet("light_growthi_stage23_standard"), new InteriorSet("weed_hosei")))
            .AlsoAt("Ферма травы", new Vector3(416.752f, 6520.753f, 26.712f), new Vector3(2848.369f, 4450.147f, 47.514f), new Vector3(136.973f, -2472.795f, 4.980f)),
        new InteriorBuilding("biker_coke", "Склад кокаина", "Байкеры", 497, -1462.622f, -381.826f, 37.802f,
            new OnlineInterior("biker_coke_lockup", "Склад", 1093.600f, -3196.600f, -38.998f, new[] { "bkr_biker_interior_placement_interior_4_biker_dlc_int_ware03_milo" },
                new InteriorSet("set_up"), new InteriorSet("equipment_basic"), new InteriorSet("coke_press_basic"), new InteriorSet("production_basic"), new InteriorSet("table_equipment")))
            .AlsoAt("Склад кокаина", new Vector3(51.765f, 6486.163f, 30.428f), new Vector3(387.533f, 3585.042f, 32.292f), new Vector3(-253.310f, -2591.150f, 4.970f)),
        new InteriorBuilding("biker_money", "Фабрика фальшивых денег", "Байкеры", 500, -1171.005f, -1380.922f, 3.937f,
            new OnlineInterior("biker_money_factory", "Фабрика", 1121.897f, -3195.338f, -40.403f, new[] { "bkr_biker_interior_placement_interior_5_biker_dlc_int_ware04_milo" },
                new InteriorSet("counterfeit_standard_equip"), new InteriorSet("counterfeit_security"), new InteriorSet("dryera_open"), new InteriorSet("dryerb_on"), new InteriorSet("dryerc_on"), new InteriorSet("dryerd_on"), new InteriorSet("money_cutter"), new InteriorSet("counterfeit_setup"), new InteriorSet("counterfeit_cashpile100b"), new InteriorSet("counterfeit_cashpile100a"), new InteriorSet("counterfeit_cashpile100d"), new InteriorSet("counterfeit_cashpile100c")))
            .AlsoAt("Фабрика фальшивых денег", new Vector3(-413.661f, 6171.938f, 30.478f), new Vector3(636.634f, 2785.126f, 41.011f), new Vector3(671.451f, -2667.502f, 5.081f)),
        new InteriorBuilding("biker_documents", "Мастерская документов", "Байкеры", 498, 299.071f, -759.072f, 28.333f,
            new OnlineInterior("biker_documents_office", "Мастерская", 1165.000f, -3196.600f, -39.013f, new[] { "bkr_biker_interior_placement_interior_6_biker_dlc_int_ware05_milo" },
                new InteriorSet("interior_basic"), new InteriorSet("security_low"), new InteriorSet("equipment_basic"), new InteriorSet("chair02"), new InteriorSet("chair01"), new InteriorSet("chair04"), new InteriorSet("chair03"), new InteriorSet("chair06"), new InteriorSet("chair05"), new InteriorSet("chair07")))
            .AlsoAt("Мастерская документов", new Vector3(-163.683f, 6334.845f, 30.581f), new Vector3(1657.066f, 4851.732f, 40.988f), new Vector3(-331.520f, -2778.970f, 4.120f)),
        new InteriorBuilding("bunker", "Бункер", "Бизнесы", 557, 1571.730f, 2252.610f, 76.836f,
            new OnlineInterior("bunker_inside", "Бункер", 892.638f, -3245.866f, -98.265f, new[] { "gr_grdlc_interior_placement_interior_1_grdlc_int_02_milo_" },
                new InteriorSet("Bunker_Style_A"), new InteriorSet("standard_bunker_set"), new InteriorSet("standard_security_set"), new InteriorSet("Office_Upgrade_set"), new InteriorSet("gun_locker_upgrade"), new InteriorSet("gun_range_lights"))
                .Outside(null, "gr_case0_bunkerclosed", "gr_case1_bunkerclosed", "gr_case2_bunkerclosed", "gr_case3_bunkerclosed", "gr_case4_bunkerclosed",
                    "gr_case5_bunkerclosed", "gr_case6_bunkerclosed", "gr_case7_bunkerclosed", "gr_case9_bunkerclosed", "gr_case10_bunkerclosed", "gr_case11_bunkerclosed"))
            // двери бункеров 70-80 (am_mp_bunker func_6244)
            .AlsoAt("Бункер", new Vector3(480.487f, 2996.070f, 41.616f), new Vector3(848.216f, 2993.330f, 43.383f), new Vector3(26.312f, 2955.610f, 56.382f),
                new Vector3(2127.750f, 3336.490f, 45.890f), new Vector3(2494.080f, 3140.060f, 48.934f), new Vector3(1826.220f, 4707.520f, 40.110f),
                new Vector3(-782.508f, 5935.050f, 21.986f), new Vector3(-392.703f, 4364.930f, 56.281f), new Vector3(-3057.530f, 3329.870f, 10.345f),
                new Vector3(-3182.620f, 1374.160f, 17.563f)),
        new InteriorBuilding("hangar", "Ангар", "Бизнесы", 569, -1153.875f, -3412.450f, 12.945f,
            new OnlineInterior("hangar_inside", "Ангар", -1267.000f, -3013.135f, -49.500f, new[] { "sm_smugdlc_interior_placement_interior_0_smugdlc_int_01_milo_" },
                new InteriorSet("set_tint_shell", 1), new InteriorSet("set_crane_tint", 1), new InteriorSet("set_floor_2"), new InteriorSet("set_floor_decal_1", 1), new InteriorSet("set_lighting_hangar_a"), new InteriorSet("set_lighting_wall_neutral"), new InteriorSet("set_lighting_tint_props", 2), new InteriorSet("set_modarea", 1), new InteriorSet("set_office_basic"), new InteriorSet("set_bedroom_modern"), new InteriorSet("set_bedroom_tint", 1), new InteriorSet("set_bedroom_blinds_open")))
            // ангары 83-87: два в аэропорту, три в Форт-Занкудо
            .AlsoAt("Ангар", new Vector3(-1397.425f, -3269.178f, 12.945f), new Vector3(-2019.963f, 3159.768f, 31.815f),
                new Vector3(-1876.317f, 3111.579f, 31.815f), new Vector3(-2468.584f, 3278.519f, 31.830f)),
        new InteriorBuilding("facility", "База Судного дня", "Бизнесы", 590, 2.430f, 6831.906f, 11.473f,
            new OnlineInterior("facility_inside", "База", 345.000f, 4842.000f, -60.000f, new[] { "xm_x17dlc_int_placement_interior_33_x17dlc_int_02_milo_" },
                new InteriorSet("set_int_02_shell", 1), new InteriorSet("set_int_02_decal_01"), new InteriorSet("set_int_02_lounge3", 1), new InteriorSet("set_int_02_sleep3", 1), new InteriorSet("set_int_02_security", 1), new InteriorSet("set_int_02_cannon", 1), new InteriorSet("Set_Int_02_Parts_Avenger2"), new InteriorSet("Set_Int_02_Parts_Avenger1"), new InteriorSet("Set_Int_02_Parts_Avenger3"), new InteriorSet("Set_Int_02_outfit_khanjali"), new InteriorSet("Set_Int_02_outfit_paramedic"), new InteriorSet("Set_Int_02_outfit_morgue"), new InteriorSet("Set_Int_02_outfit_steal_avenger"), new InteriorSet("Set_Int_02_outfit_serverfarm"), new InteriorSet("Set_Int_02_outfit_volatol"), new InteriorSet("Set_Int_02_outfit_sub_finale"), new InteriorSet("Set_Int_02_outfit_iaa"), new InteriorSet("Set_Int_02_outfit_riot_van"), new InteriorSet("Set_Int_02_outfit_foundry"), new InteriorSet("Set_Int_02_outfit_predator"), new InteriorSet("Set_Int_02_outfit_stromberg"), new InteriorSet("set_int_02_trophy1"), new InteriorSet("set_int_02_trophy_sub", 1), new InteriorSet("set_int_02_trophy_iaa"), new InteriorSet("set_int_02_clutter1"), new InteriorSet("set_int_02_clutter2"))
                .Outside(null, "xm_hatch_01_cutscene", "xm_hatch_02_cutscene", "xm_hatch_03_cutscene", "xm_hatch_04_cutscene", "xm_hatch_06_cutscene",
                    "xm_hatch_07_cutscene", "xm_hatch_08_cutscene", "xm_hatch_09_cutscene", "xm_hatch_10_cutscene", "xm_hatch_closed", "xm_siloentranceclosed_x17",
                    "xm_bunkerentrance_door", "xm_hatches_terrain", "xm_hatches_terrain_lod"))
            // люки баз 89-97
            .AlsoAt("База Судного дня", new Vector3(1287.413f, 2846.593f, 45.050f), new Vector3(19.022f, 2611.234f, 81.656f),
                new Vector3(2769.089f, 3920.189f, 41.474f), new Vector3(3407.521f, 5504.718f, 21.934f), new Vector3(-2231.284f, 2417.664f, 7.837f),
                new Vector3(-7.025f, 3326.452f, 37.287f), new Vector3(2073.153f, 1748.262f, 100.170f), new Vector3(1874.689f, 284.791f, 159.961f)),
        new InteriorBuilding("nightclub", "Ночной клуб", "Бизнесы", 614, 331.212f, -994.074f, 28.424f,
            new OnlineInterior("nightclub_inside", "Клуб", -1604.664f, -3012.583f, -78.000f, new[] { "ba_int_placement_ba_interior_0_dlc_int_01_ba_milo_", "ba_barriers_case0", "ba_case0_solomun", "ba_case0_dixon", "ba_case0_madonna", "ba_case0_taleofus", "ba_barriers_case1", "ba_case1_solomun", "ba_case1_dixon", "ba_case1_madonna", "ba_case1_taleofus", "ba_barriers_case2", "ba_case2_solomun", "ba_case2_dixon", "ba_case2_madonna", "ba_case2_taleofus", "ba_barriers_case3", "ba_case3_solomun", "ba_case3_dixon", "ba_case3_madonna", "ba_case3_taleofus", "ba_barriers_case4", "ba_case4_solomun", "ba_case4_dixon", "ba_case4_madonna", "ba_case4_taleofus", "ba_barriers_case5", "ba_case5_solomun", "ba_case5_dixon", "ba_case5_madonna", "ba_case5_taleofus", "ba_barriers_case6", "ba_case6_solomun", "ba_case6_dixon", "ba_case6_madonna", "ba_case6_taleofus", "ba_barriers_case7", "ba_case7_solomun", "ba_case7_dixon", "ba_case7_madonna", "ba_case7_taleofus", "ba_barriers_case8", "ba_case8_solomun", "ba_case8_dixon", "ba_case8_madonna", "ba_case8_taleofus", "ba_barriers_case9", "ba_case9_solomun", "ba_case9_dixon", "ba_case9_madonna", "ba_case9_taleofus" },
                new InteriorSet("Int01_ba_clubname_01"), new InteriorSet("Int01_ba_Style02"), new InteriorSet("Int01_ba_style02_podium"), new InteriorSet("Int01_ba_equipment_setup"), new InteriorSet("Int01_ba_equipment_upgrade"), new InteriorSet("Int01_ba_security_upgrade"), new InteriorSet("Int01_ba_dj01"), new InteriorSet("DJ_04_Lights_03"), new InteriorSet("Int01_ba_bar_content"), new InteriorSet("Int01_ba_booze_02"), new InteriorSet("Int01_ba_booze_01"), new InteriorSet("Int01_ba_booze_03"), new InteriorSet("Int01_ba_trophy01", 2)))
            // клубы 1-10 (am_mp_nightclub func_4981)
            .AlsoAt("Ночной клуб", new Vector3(734.321f, -1289.082f, 25.308f), new Vector3(-166.638f, -1292.334f, 30.342f),
                new Vector3(-23.351f, 220.098f, 105.753f), new Vector3(910.878f, -2097.885f, 29.561f), new Vector3(-668.879f, -2389.805f, 12.954f),
                new Vector3(230.887f, -3137.272f, 4.874f), new Vector3(376.349f, 225.427f, 102.089f), new Vector3(-1236.351f, -695.968f, 22.635f),
                new Vector3(-1175.685f, -1173.899f, 4.652f)),
        new InteriorBuilding("arcade", "Аркада", "Бизнесы", 740, -1270.591f, -305.000f, 36.074f,
            new OnlineInterior("arcade_inside", "Аркада", 2732.000f, -380.000f, -50.000f, None,
                new InteriorSet("entity_set_constant_geometry"), new InteriorSet("entity_set_arcade_set_ceiling_flat"), new InteriorSet("entity_set_mural_option_05"), new InteriorSet("entity_set_floor_option_01"), new InteriorSet("entity_set_arcade_set_trophy_claw"), new InteriorSet("entity_set_arcade_set_trophy_teller"), new InteriorSet("entity_set_arcade_set_trophy_gunner"), new InteriorSet("entity_set_arcade_set_trophy_brawler"), new InteriorSet("entity_set_arcade_set_trophy_cabs"), new InteriorSet("entity_set_plushie_01"), new InteriorSet("entity_set_plushie_07"), new InteriorSet("entity_set_plushie_08"), new InteriorSet("entity_set_plushie_09"), new InteriorSet("entity_set_big_screen"), new InteriorSet("entity_set_screens")),
            // подвал для планов ограбления казино, место из точек возрождения main.c, обстановка bob74_ipl DiamondArcadeBasement
            new OnlineInterior("arcade_basement", "Подвал", 2713.297f, -369.500f, -56.000f, None,
                new InteriorSet("set_plan_setup"), new InteriorSet("set_plan_scribbles"), new InteriorSet("set_plan_garage"), new InteriorSet("set_plan_bed"),
                new InteriorSet("set_plan_computer"), new InteriorSet("set_plan_plans"), new InteriorSet("set_plan_mechanic"), new InteriorSet("set_plan_cockroaches")))
            // аркады 128-133
            .AlsoAt("Аркада", new Vector3(-247.330f, 6213.446f, 30.944f), new Vector3(1696.231f, 4785.183f, 41.026f),
                new Vector3(-115.982f, -1772.695f, 28.826f), new Vector3(-599.494f, 280.406f, 81.074f), new Vector3(758.356f, -815.944f, 25.291f)),
        new InteriorBuilding("auto_shop", "Автомастерская", "Бизнесы", 779, 758.523f, -679.632f, 27.979f,
            new OnlineInterior("auto_shop_inside", "Мастерская", -1350.000f, 160.000f, -100.000f, new[] { "tr_tuner_shop_burton", "tr_tuner_shop_mesa", "tr_tuner_shop_mission", "tr_tuner_shop_rancho", "tr_tuner_shop_strawberry" },
                new InteriorSet("entity_set_car_lift_cutscene"), new InteriorSet("entity_set_train"), new InteriorSet("entity_set_scope"), new InteriorSet("entity_set_virus"), new InteriorSet("entity_set_lightbox"), new InteriorSet("entity_set_bedroom"), new InteriorSet("entity_set_style_9"), new InteriorSet("entity_set_laptop"), new InteriorSet("entity_set_plate"), new InteriorSet("entity_set_tints"), new InteriorSet("entity_set_jammers"), new InteriorSet("entity_set_IAA"), new InteriorSet("entity_set_car_lift_purchase"), new InteriorSet("entity_set_ecu"), new InteriorSet("entity_set_drive"), new InteriorSet("entity_set_thermal"), new InteriorSet("entity_set_car_lift_default"), new InteriorSet("entity_set_bombs")))
            // мастерские 149-153
            .AlsoAt("Автомастерская", new Vector3(-149.618f, -1341.022f, 28.907f), new Vector3(-173.468f, -35.728f, 51.213f),
                new Vector3(231.436f, -1874.801f, 25.551f), new Vector3(488.302f, -896.900f, 24.853f)),
        new InteriorBuilding("car_meet", "LS Car Meet", "Бизнесы", 777, 778.460f, -1870.152f, 28.295f,
            new OnlineInterior("car_meet_inside", "LS Car Meet", -2000.000f, 1113.211f, -25.362f, new[] { "tr_tuner_meetup", "tr_tuner_race_line" },
                new InteriorSet("entity_set_meet_crew"), new InteriorSet("entity_set_meet_lights"), new InteriorSet("entity_set_test_lights"), new InteriorSet("entity_set_player"), new InteriorSet("entity_set_time_trial")),
            // тестовая трасса: второй такой же интерьер на 53 м выше (am_mp_car_meet_sandbox func_3412), место у старта
            new OnlineInterior("car_meet_track", "Тестовая трасса", -2121.070f, 1103.783f, 24.664f, new[] { "tr_tuner_meetup", "tr_tuner_race_line" },
                new InteriorSet("entity_set_test_crew"), new InteriorSet("entity_set_test_lights"), new InteriorSet("entity_set_time_trial")).Drivable()),
        new InteriorBuilding("bail_office", "Офис поручителя", "Бизнесы", 893, -1415.176f, -656.989f, 27.673f,
            new OnlineInterior("bail_office_inside", "Офис", 565.886f, -2688.761f, -50.000f, None,
                new InteriorSet("set_style_03"), new InteriorSet("set_no_staff"), new InteriorSet("set_gunsafe_off"), new InteriorSet("set_trophy_10x"), new InteriorSet("set_trophy_24x"), new InteriorSet("set_trophy_100x"), new InteriorSet("set_new_plant"))
                .Outside(null, "m24_1_bailoffice_davis", "m24_1_bailoffice_delperro", "m24_1_bailoffice_missionrow", "m24_1_bailoffice_paletobay", "m24_1_bailoffice_vinewood",
                    "m24_1_legacyfixes", "m24_1_pizzasigns"))
            // офисы 167-171
            .AlsoAt("Офис поручителя", new Vector3(489.294f, -934.858f, 25.776f), new Vector3(82.223f, 23.278f, 68.055f),
                new Vector3(139.111f, -1697.505f, 28.301f), new Vector3(-59.827f, 6503.449f, 30.556f)),
        new InteriorBuilding("salvage_yard", "Свалка", "Бизнесы", 867, -12.710f, -1311.688f, 28.304f,
            new OnlineInterior("salvage_yard_inside", "Свалка", 1077.276f, -2274.876f, -50.000f, None,
                new InteriorSet("set_tint_b", 1), new InteriorSet("set_mechanic_upgrade"), new InteriorSet("set_safe_upgrade"), new InteriorSet("set_car_lift_01_up"), new InteriorSet("set_car_lift_02_up"))
                .Outside(null, "m23_2_sp1_03_reds", "m23_2_sc1_03_reds", "m23_2_id2_04_reds", "m23_2_cs1_05_reds", "m23_2_cs4_11_reds",
                    "m23_2_acp_collision_fixes_01", "m23_2_acp_collision_fixes_02", "m23_2_tug_collision", "m23_2_hei_yacht_collision_fixes",
                    "m23_2_vinewood_garage", "m23_2_lifeguard_access"))
            // свалки 162-166
            .AlsoAt("Свалка", new Vector3(-191.130f, 6268.015f, 30.489f), new Vector3(2514.075f, 4109.898f, 37.611f),
                new Vector3(-511.032f, -1740.551f, 18.345f), new Vector3(1191.276f, -1263.920f, 34.334f)),
        new InteriorBuilding("car_wash", "Автомойка Hands On", "Бизнесы", 931, 25.635f, -1409.308f, 28.333f,
            new OnlineInterior("car_wash_inside", "Автомойка", 26.074f, -1398.979f, -75.000f, None,
                new InteriorSet("set_carwash_tints")).Outside(null, "m25_1_carwash")).RequiresDlc("mp2025_01"),
        // интерьер 175: дверь из am_mp_smpl_interior_ext func_1364, место внутри из am_mp_sb_weed_shop func_1464,
        // офис общий с Higgins Helitours (176), у каждого своя отделка; дальше части карты обновления (bob74_ipl dlc_money/base)
        new InteriorBuilding("smoke_on_the_water", "Smoke on the Water", "Бизнесы", 925, -1163.663f, -1566.461f, 4.950f,
            new OnlineInterior("smoke_on_the_water_office", "Офис", -1164.213f, -1541.167f, -50.000f, None,
                new InteriorSet("set_smoke"), new InteriorSet("set_smoke_tint"))
                .Outside(null, "m25_1_smokeonthewater", "m25_1_legacy_fixes", "m25_1_mp2025_01_additions", "m25_1_bobcat", "m25_1_garage", "m25_1_quikpharma")).RequiresDlc("mp2025_01"),
        new InteriorBuilding("helitours", "Higgins Helitours", "Бизнесы", 928, -758.732f, -1515.335f, 5.363f,
            new OnlineInterior("helitours_office", "Офис", -1164.213f, -1541.167f, -50.000f, None,
                new InteriorSet("set_heli"), new InteriorSet("set_heli_tint")).Outside(null, "m25_1_helitours")).RequiresDlc("mp2025_01"),
        // интерьер 172 (Agents of Sabotage): двери из func_1770, место внутри из am_mp_hacker_den func_2053,
        // части карты обновления: аэродром в холмах и ворота ангара (bob74_ipl dlc_agents)
        new InteriorBuilding("agents_factory", "Швейная фабрика", "Бизнесы", 900, 738.468f, -954.586f, 24.375f,
            new OnlineInterior("agents_factory_inside", "Фабрика", 745.796f, -993.119f, -47.377f, None,
                new InteriorSet("set_pent_rob_idol_01"), new InteriorSet("set_pent_rob_idol_02"), new InteriorSet("set_zanc_armor"),
                new InteriorSet("set_sonar"), new InteriorSet("set_malware_drive"))
                .Outside(null, "m24_2_garment_factory", "m24_2_legacy_fixes", "m24_2_mp2024_02_additions", "m24_2_airstrip", "m24_2_prop_m42_hangerdoor_02a"))
            .AlsoAt("Швейная фабрика", new Vector3(739.597f, -970.088f, 23.457f)).RequiresDlc("mp2024_02"),
        // интерьер 173: дверь офиса из func_1510 (вторая в 7 м от неё не нужна), место внутри из am_mp_business_hub
        new InteriorBuilding("mckenzie_hangar", "Ангар McKenzie Field", "Бизнесы", 903, 2149.800f, 4789.330f, 41.490f,
            new OnlineInterior("mckenzie_hangar_office", "Офис", 2151.220f, 4785.887f, -47.375f, None,
                new InteriorSet("set_bed"), new InteriorSet("set_laptop"), new InteriorSet("set_24_2"))).RequiresDlc("mp2024_02"),
        new InteriorBuilding("music_studio", "Record A Studios", "Бизнесы", 819, -839.668f, -228.132f, 36.253f,
            new OnlineInterior("music_studio_inside", "Студия", -1010.220f, -52.210f, -100.000f, new[] { "sf_int_placement_sec_interior_1_dlc_studio_sec_milo_" },
                new InteriorSet("Entity_Set_Fire"), new InteriorSet("entity_set_default")).Outside(null, "sf_musicrooftop")),
        new InteriorBuilding("music_locker", "Music Locker", "Бизнесы", 136, 988.800f, 79.600f, 80.990f,
            new OnlineInterior("music_locker_inside", "Клуб", 1550.000f, 250.000f, -49.000f, new[] { "h4_clubposter_palmstraxx", "h4_clubposter_moodymann", "h4_clubposter_keinemusik" },
                new InteriorSet("int01_ba_security_upgrade"), new InteriorSet("int01_ba_equipment_setup"), new InteriorSet("int01_ba_style02_podium"), new InteriorSet("int01_ba_dj01"), new InteriorSet("int01_ba_bar_content"), new InteriorSet("int01_ba_lights_screen"), new InteriorSet("dj_04_lights_04"))
                .Outside(null, "h4_ch2_mansion_final")),
        new InteriorBuilding("freakshop", "Freakshop", "Бизнесы", 847, 580.219f, -421.294f, 23.730f,
            new OnlineInterior("freakshop_inside", "Freakshop", 570.971f, -420.073f, -70.000f, None,
                new InteriorSet("entity_set_roller_door_closed"))
                .Outside(null, "xm3_warehouse", "xm3_warehouse_grnd", "xm3_collision_fixes", "xm3_sum2_fix", "xm3_security_fix", "xm3_train_crash")),
        // склады особых грузов по размерам из am_mp_warehouse (func_159) и входам 1-22
        new InteriorBuilding("ceo_warehouse_small", "Малый склад", "Склады", 473, -424.828f, 185.825f, 79.775f,
            new OnlineInterior("ceo_warehouse_small_inside", "Малый склад", 1094.988f, -3101.776f, -39.000f, None))
            .AlsoAt("Склад особых грузов", new Vector3(54.191f, -2569.248f, 5.005f), new Vector3(-1083.054f, -1261.893f, 4.534f),
                new Vector3(896.367f, -1035.749f, 34.110f), new Vector3(247.473f, -1956.943f, 22.191f), new Vector3(274.522f, -3015.413f, 4.699f)),
        new InteriorBuilding("ceo_warehouse_medium", "Средний склад", "Склады", 473, -1268.119f, -812.274f, 16.108f,
            new OnlineInterior("ceo_warehouse_medium_inside", "Средний склад", 1056.486f, -3105.724f, -39.000f, None))
            .AlsoAt("Склад особых грузов", new Vector3(1569.690f, -2129.792f, 77.335f), new Vector3(-315.551f, -2698.654f, 6.550f),
                new Vector3(499.810f, -651.982f, 23.909f), new Vector3(-528.530f, -1784.573f, 20.585f), new Vector3(-295.860f, -1353.238f, 30.314f),
                new Vector3(349.839f, 328.889f, 103.272f), new Vector3(539.346f, -1945.682f, 23.984f)),
        new InteriorBuilding("ceo_warehouse_large", "Большой склад", "Склады", 473, 759.566f, -909.466f, 24.244f,
            new OnlineInterior("ceo_warehouse_large_inside", "Большой склад", 1006.967f, -3102.079f, -39.004f, None))
            .AlsoAt("Склад особых грузов", new Vector3(-1042.482f, -2023.516f, 12.162f), new Vector3(-873.650f, -2735.948f, 12.944f),
                new Vector3(926.282f, -1560.311f, 29.740f), new Vector3(1037.813f, -2173.062f, 30.533f), new Vector3(1019.116f, -2511.690f, 27.302f),
                new Vector3(-245.341f, 203.329f, 82.818f), new Vector3(96.154f, -2216.400f, 5.171f)),
        new InteriorBuilding("vehicle_warehouse", "Склад машин", "Склады", 524, 36.871f, -1284.028f, 28.292f,
            new OnlineInterior("vehicle_warehouse_upper", "Склад", 994.593f, -3002.594f, -39.647f, new[] { "imp_impexp_interior_placement_interior_1_impexp_intwaremed_milo_" },
                new InteriorSet("branded_style_set"), new InteriorSet("car_floor_hatch")),
            new OnlineInterior("vehicle_warehouse_lower", "Мастерская внизу", 969.538f, -3000.411f, -48.647f, new[] { "imp_impexp_interior_placement_interior_3_impexp_int_02_milo_" },
                new InteriorSet("pump_01"), new InteriorSet("pump_02"), new InteriorSet("pump_03"), new InteriorSet("pump_04"), new InteriorSet("pump_05"), new InteriorSet("pump_06"), new InteriorSet("pump_07"), new InteriorSet("pump_08")))
            // склады машин 60-69
            .AlsoAt("Склад машин", new Vector3(-631.975f, -1779.181f, 22.980f), new Vector3(1007.510f, -1855.032f, 30.040f),
                new Vector3(-72.339f, -1820.984f, 25.942f), new Vector3(1213.233f, -1251.249f, 35.326f), new Vector3(808.800f, -2222.500f, 28.624f),
                new Vector3(1754.546f, -1649.167f, 111.656f), new Vector3(144.316f, -3005.919f, 6.031f), new Vector3(-521.275f, -2196.688f, 5.394f),
                new Vector3(-1161.292f, -2163.700f, 12.381f)),
        new InteriorBuilding("eclipse_garage", "Гараж на Eclipse Boulevard", "Гаражи", 856, -274.241f, 282.802f, 89.022f,
            new OnlineInterior("eclipse_garage_inside", "Гараж", 519.248f, -2618.788f, -50.000f, None,
                new InteriorSet("entity_set_numbers_01"), new InteriorSet("entity_set_shell_01"), new InteriorSet("entity_set_tint_01", 1)).Outside(null, "xm3_garage_fix")),
        new InteriorBuilding("agency_rockford", "Агентство, Рокфорд-Хиллз", "Агентства", 826, -1028.914f, -410.970f, 32.287f,
            new OnlineInterior("agency_rockford_office", "Офис агентства", -1021.861f, -427.746f, 68.958f, new[] { "sf_fixeroffice_bh1_05" },
                new InteriorSet("Entity_Set_M_Motel"), new InteriorSet("Entity_Set_M_Billionaire"), new InteriorSet("Entity_Set_Standard_Office"), new InteriorSet("Entity_Set_M_Taxi"), new InteriorSet("Entity_Set_M_Trip_03"), new InteriorSet("Entity_Set_Moving"), new InteriorSet("Entity_Set_Spare_Seats"), new InteriorSet("Entity_Set_M_Vehicles"), new InteriorSet("Entity_Set_Wpaper_8"), new InteriorSet("Entity_Set_Player_Desk"), new InteriorSet("Entity_Set_Art_1"), new InteriorSet("Entity_Set_M_Hit_List"), new InteriorSet("Entity_Set_M_Attack"), new InteriorSet("Entity_set_disc_01"), new InteriorSet("Entity_Set_M_Setup"), new InteriorSet("Entity_Set_M_Gone_Golfing"), new InteriorSet("Entity_Set_M_50"), new InteriorSet("Entity_Set_M_Fire_Booth"), new InteriorSet("Entity_Set_M_Families"), new InteriorSet("Entity_Set_M_Trip_02"), new InteriorSet("Entity_Set_Player_Seats"), new InteriorSet("Entity_Set_M_Trip_01"), new InteriorSet("Entity_Set_M_Tuner"), new InteriorSet("Entity_Set_M_Ballas"), new InteriorSet("Entity_Set_M_The_Way"), new InteriorSet("Entity_Set_M_Hood"), new InteriorSet("Entity_Set_M_Yacht"), new InteriorSet("Entity_Set_M_Construction"), new InteriorSet("Entity_Set_M_Limo_Wallet"), new InteriorSet("Entity_Set_M_Limo_Photo"), new InteriorSet("Entity_Set_M_Promoter"), new InteriorSet("Entity_Set_M_Nightclub"), new InteriorSet("Entity_Set_M_Golf_Intro"), new InteriorSet("Entity_Set_Tint_AG")),
            AgencyGarage("agency_rockford_garage")),
        new InteriorBuilding("agency_hawick", "Агентство, Хавик", "Агентства", 826, 356.528f, -79.623f, 66.109f,
            new OnlineInterior("agency_hawick_office", "Офис агентства", 383.416f, -59.878f, 108.460f, new[] { "sf_fixeroffice_hw1_08" },
                new InteriorSet("Entity_Set_Armoury"), new InteriorSet("Entity_Set_M_Motel"), new InteriorSet("Entity_Set_M_Billionaire"), new InteriorSet("Entity_Set_M_Taxi"), new InteriorSet("Entity_Set_M_Trip_03"), new InteriorSet("Entity_Set_Wpaper_9"), new InteriorSet("Entity_Set_Moving"), new InteriorSet("Entity_Set_Spare_Seats"), new InteriorSet("Entity_Set_M_Vehicles"), new InteriorSet("Entity_Set_Player_Desk"), new InteriorSet("Entity_Set_M_Hit_List"), new InteriorSet("Entity_Set_M_Attack"), new InteriorSet("Entity_Set_M_Setup"), new InteriorSet("Entity_Set_M_Gone_Golfing"), new InteriorSet("Entity_Set_M_50"), new InteriorSet("Entity_set_disc_02"), new InteriorSet("Entity_Set_M_Fire_Booth"), new InteriorSet("Entity_Set_M_Families"), new InteriorSet("Entity_Set_M_Trip_02"), new InteriorSet("Entity_Set_Player_Seats"), new InteriorSet("Entity_Set_M_Trip_01"), new InteriorSet("Entity_Set_Art_2"), new InteriorSet("Entity_Set_M_Tuner"), new InteriorSet("Entity_Set_M_Ballas"), new InteriorSet("Entity_Set_M_The_Way"), new InteriorSet("Entity_Set_M_Hood"), new InteriorSet("Entity_Set_M_Yacht"), new InteriorSet("Entity_Set_M_Construction"), new InteriorSet("Entity_Set_M_Limo_Wallet"), new InteriorSet("Entity_Set_M_Limo_Photo"), new InteriorSet("Entity_Set_M_Promoter"), new InteriorSet("Entity_Set_M_Nightclub"), new InteriorSet("Entity_Set_M_Golf_Intro"), new InteriorSet("Entity_Set_Tint_AG")),
            AgencyGarage("agency_hawick_garage")),
        new InteriorBuilding("agency_vespucci", "Агентство, Веспуччи", "Агентства", 826, -991.021f, -763.971f, 14.773f,
            new OnlineInterior("agency_vespucci_office", "Офис агентства", -1004.230f, -761.208f, 66.991f, new[] { "sf_fixeroffice_kt1_05" },
                new InteriorSet("Entity_Set_M_Motel"), new InteriorSet("Entity_Set_M_Billionaire"), new InteriorSet("Entity_Set_Standard_Office"), new InteriorSet("Entity_Set_M_Taxi"), new InteriorSet("Entity_Set_Wpaper_2"), new InteriorSet("Entity_Set_M_Trip_03"), new InteriorSet("Entity_Set_Moving"), new InteriorSet("Entity_Set_Art_3"), new InteriorSet("Entity_Set_Spare_Seats"), new InteriorSet("Entity_Set_M_Vehicles"), new InteriorSet("Entity_Set_Player_Desk"), new InteriorSet("Entity_Set_M_Hit_List"), new InteriorSet("Entity_Set_M_Attack"), new InteriorSet("Entity_Set_M_Setup"), new InteriorSet("Entity_Set_M_Gone_Golfing"), new InteriorSet("Entity_Set_M_50"), new InteriorSet("Entity_set_disc_02"), new InteriorSet("Entity_Set_M_Fire_Booth"), new InteriorSet("Entity_Set_M_Families"), new InteriorSet("Entity_Set_M_Trip_02"), new InteriorSet("Entity_Set_Player_Seats"), new InteriorSet("Entity_Set_M_Trip_01"), new InteriorSet("Entity_Set_M_Tuner"), new InteriorSet("Entity_Set_M_Ballas"), new InteriorSet("Entity_Set_M_The_Way"), new InteriorSet("Entity_Set_M_Hood"), new InteriorSet("Entity_Set_M_Yacht"), new InteriorSet("Entity_Set_M_Construction"), new InteriorSet("Entity_Set_M_Limo_Wallet"), new InteriorSet("Entity_Set_M_Limo_Photo"), new InteriorSet("Entity_Set_M_Promoter"), new InteriorSet("Entity_Set_M_Nightclub"), new InteriorSet("Entity_Set_M_Golf_Intro"), new InteriorSet("Entity_Set_Tint_AG")),
            AgencyGarage("agency_vespucci_garage")),
        new InteriorBuilding("agency_little_seoul", "Агентство, Литл-Сеул", "Агентства", 826, -614.924f, -736.252f, 26.883f,
            new OnlineInterior("agency_little_seoul_office", "Офис агентства", -587.872f, -716.849f, 118.102f, new[] { "sf_fixeroffice_kt1_08" },
                new InteriorSet("Entity_Set_Armoury"), new InteriorSet("Entity_Set_M_Motel"), new InteriorSet("Entity_Set_M_Billionaire"), new InteriorSet("Entity_Set_M_Taxi"), new InteriorSet("Entity_Set_M_Trip_03"), new InteriorSet("Entity_Set_Moving"), new InteriorSet("Entity_Set_Spare_Seats"), new InteriorSet("Entity_Set_M_Vehicles"), new InteriorSet("Entity_Set_Player_Desk"), new InteriorSet("Entity_Set_Art_1"), new InteriorSet("Entity_Set_M_Hit_List"), new InteriorSet("Entity_Set_M_Attack"), new InteriorSet("Entity_Set_M_Setup"), new InteriorSet("Entity_Set_M_Gone_Golfing"), new InteriorSet("Entity_Set_M_50"), new InteriorSet("Entity_Set_M_Fire_Booth"), new InteriorSet("Entity_Set_M_Families"), new InteriorSet("Entity_set_disc_05"), new InteriorSet("Entity_Set_M_Trip_02"), new InteriorSet("Entity_Set_Player_Seats"), new InteriorSet("Entity_Set_M_Trip_01"), new InteriorSet("Entity_Set_M_Tuner"), new InteriorSet("Entity_Set_M_Ballas"), new InteriorSet("Entity_Set_M_The_Way"), new InteriorSet("Entity_Set_M_Hood"), new InteriorSet("Entity_Set_Wpaper_3"), new InteriorSet("Entity_Set_M_Yacht"), new InteriorSet("Entity_Set_M_Construction"), new InteriorSet("Entity_Set_M_Limo_Wallet"), new InteriorSet("Entity_Set_M_Limo_Photo"), new InteriorSet("Entity_Set_M_Promoter"), new InteriorSet("Entity_Set_M_Nightclub"), new InteriorSet("Entity_Set_M_Golf_Intro"), new InteriorSet("Entity_Set_Tint_AG")),
            AgencyGarage("agency_little_seoul_garage")),
        new InteriorBuilding("mansion_vinewood", "Особняк, Вайнвуд-Хиллз", "Особняки", 948, 542.665f, 773.172f, 190.247f,
            new OnlineInterior("mansion_vinewood_house", "Особняк", 543.852f, 712.754f, 201.000f, None,
                new InteriorSet("set_style_cali"), new InteriorSet("set_elev_cali"), new InteriorSet("set_art_coastal"), new InteriorSet("set_trophy_planter"), new InteriorSet("set_shelving_planter"), new InteriorSet("set_wallpaper_deco"), new InteriorSet("set_style_cali_tint", 0), new InteriorSet("set_ai_tablets_03"), new InteriorSet("set_pet_cat"), new InteriorSet("set_pet_dog"))
                .Outside("apa_ch2_04_mansion_generic", "m25_2_ch2_04_mansion_interior_a", "m25_2_ch2_04_mansion_interior_b", "m25_2_ch2_04_mansion_interior_c", "apa_ch2_04_mansion_shared", "apa_ch2_04_mansion_private", "apa_ch2_04_mansion_furniture", "apa_ch2_04_mansion_firepit", "apa_ch2_04_mansion_railings_p", "m25_2_east_mansion_gym", "m25_2_east_dog_house", "m25_2_mansion_props", "hei_ch1_roads_mansion", "apa_ch2_04_mansion_shared_distantlights", "apa_ch2_04_mansion_shared_lodlights", "m25_2_knoway_sign"),
            ArtWorkshop("mansion_vinewood_workshop")).RequiresDlc("mp2025_02"),
        new InteriorBuilding("mansion_richman", "Особняк, Ричман", "Особняки", 948, -1688.441f, 489.054f, 115.037f,
            new OnlineInterior("mansion_richman_house", "Особняк", -1630.434f, 470.852f, 128.000f, None,
                new InteriorSet("set_style_cali"), new InteriorSet("set_elev_cali"), new InteriorSet("set_art_coastal"), new InteriorSet("set_trophy_planter"), new InteriorSet("set_shelving_planter"), new InteriorSet("set_wallpaper_deco"), new InteriorSet("set_style_cali_tint", 0), new InteriorSet("set_ai_tablets_03"), new InteriorSet("set_pet_cat"), new InteriorSet("set_pet_dog"))
                .Outside("hei_ch1_06e_mansion_generic", "hei_ch1_06e_mansion_shared", "hei_ch1_06f_mansion_shared", "m25_2_ch1_06e_mansion_interior_a", "m25_2_ch1_06e_mansion_interior_b", "m25_2_ch1_06e_mansion_interior_c", "hei_ch1_06e_mansion_private", "hei_ch1_06e_mansion_furniture", "hei_ch1_06e_mansion_firepit", "hei_ch1_06e_mansion_railings_p", "m25_2_mansion_gym", "m25_2_dog_house", "m25_2_mansion_props", "hei_ch1_roads_mansion", "hei_ch1_06e_mansion_shared_distantlights", "hei_ch1_06e_mansion_shared_lodlights"),
            ArtWorkshop("mansion_richman_workshop")).RequiresDlc("mp2025_02"),
        new InteriorBuilding("mansion_tongva", "Особняк, Тонгва", "Особняки", 948, -2558.092f, 1910.113f, 155.860f,
            new OnlineInterior("mansion_tongva_house", "Особняк", -2601.712f, 1874.826f, 166.000f, None,
                new InteriorSet("set_style_cali"), new InteriorSet("set_elev_cali"), new InteriorSet("set_art_coastal"), new InteriorSet("set_trophy_planter"), new InteriorSet("set_shelving_planter"), new InteriorSet("set_wallpaper_deco"), new InteriorSet("set_style_cali_tint", 0), new InteriorSet("set_ai_tablets_03"), new InteriorSet("set_pet_cat"), new InteriorSet("set_pet_dog"))
                .Outside("hei_ch1_09_mansion_generic", "hei_ch1_09_mansion_shared", "m25_2_ch1_09_mansion_interior_a", "m25_2_ch1_09_mansion_interior_b", "m25_2_ch1_09_mansion_interior_c", "hei_ch1_09_mansion_private", "hei_ch1_09_mansion_furniture", "hei_ch1_09_mansion_firepit", "hei_ch1_09_mansion_railings_p", "m25_2_tongva_mansion_gym", "m25_2_tongva_dog_house", "m25_2_mansion_props", "hei_ch1_roads_mansion", "hei_ch1_09_mansion_shared_distantlights", "hei_ch1_09_mansion_shared_lodlights"),
            ArtWorkshop("mansion_tongva_workshop")).RequiresDlc("mp2025_02"),
        // залы ограбления Kortz Center (fm_content_kortz_entry): дверь у места закладки заряда, места внутри func_9196,
        // обстановка bob74_ipl dlc_kortz, наружные части музея и обновления там же
        new InteriorBuilding("kortz_center", "Kortz Center", "Ограбления", 959, -2193.900f, 248.200f, 174.800f,
            new OnlineInterior("kortz_exhibition", "Выставочный зал", 2618.028f, 5906.564f, -50.000f, None,
                new InteriorSet("set_background_art"), new InteriorSet("set_exhibit_walls"), new InteriorSet("set_exhibit_robbable"), new InteriorSet("set_exhibit_dressing"),
                new InteriorSet("set_alarm_panel"), new InteriorSet("set_camera_button"), new InteriorSet("set_cameras_02"), new InteriorSet("set_keypads_exhibit"),
                new InteriorSet("set_glass_pane"), new InteriorSet("set_tvs"))
                .Outside(null, "m26_1_mp2026_01_additions_critical_0", "m26_1_mp2026_01_additions_exterior", "m26_1_mp2026_01_additions_exterior_cctv",
                    "m26_1_mp2026_01_additions_kortz_lowerbarriers_up"),
            new OnlineInterior("kortz_vault", "Хранилище", 2622.156f, 5883.339f, -62.000f, None,
                new InteriorSet("set_lock_box"), new InteriorSet("set_station_2"), new InteriorSet("set_code_computer"), new InteriorSet("set_keypads_basement"),
                new InteriorSet("set_cameras_03"), new InteriorSet("set_lazer_switches"), new InteriorSet("collision_8p4piob")),
            new OnlineInterior("kortz_loading_bay", "Погрузка", 2693.671f, 5877.493f, -62.000f, None,
                new InteriorSet("set_mule_cargo"), new InteriorSet("set_keypads_loading_bay"), new InteriorSet("set_cameras_04"))).RequiresDlc("mp2026_01"),
        // комплекс на Кайо-Перико (fm_content_island_heist): дверь у западного входа в особняк (func_9317),
        // кабинет с сейфом и хранилище в интерьере h4_dlc_island_vault (func_10890), он есть, пока загружен остров
        new InteriorBuilding("el_rubio", "Особняк Эль Рубио", "Кайо-Перико", 428, 4995.553f, -5748.032f, 15.002f,
            new OnlineInterior("el_rubio_mansion", "Особняк", 5008.749f, -5760.614f, 14.500f, None,
                new InteriorSet("panther_set"))).OnPlace(SpecialPlaces.CayoPerico),
    };

    internal static readonly OnlineInterior[] All = Buildings.SelectMany(building => building.Interiors).ToArray();
    internal static readonly InteriorDoor[] Doors = Buildings.SelectMany(building => building.Doors).ToArray();
    // жильё, которое игрок делает своим: апартаменты, дома, особняки без мастерской и пентхаус казино
    internal static readonly OnlineInterior[] Homes = All.Where(interior =>
        (interior.Building.Group is "Апартаменты" or "Дома" or "Особняки" && !interior.Key.EndsWith("_workshop", StringComparison.Ordinal)) ||
        interior.Key == "casino_penthouse").ToArray();

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
    // круг для машины шире, в пеший не попасть колёсами
    internal const float CarScale = 3f;
    private bool _armed;

    internal bool Step(float distance, float scale = 1f)
    {
        if (!(distance <= ArmDistance * scale))
        {
            _armed = true;
            return false;
        }

        if (!_armed || distance > Radius * scale) return false;
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
    // Closer than this to a mansion the full building is seen (bob74_ipl).
    internal const float GenericDistance = 200f;

    private readonly OnlineInterior[] _interiors;
    private readonly Func<string, bool> _dlcPresent;
    private readonly bool[] _loaded;
    private readonly bool[] _furnished;
    // The pinned interior of each entry, 0 while not opened.
    private readonly int[] _opened;
    private readonly bool[] _generic;
    private readonly long[] _lastWanted;
    // Map parts several interiors share go once none of them needs them.
    private readonly Dictionary<string, int> _users = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    // Map parts already on before this resource asked for them belong to the
    // game's own map and are never taken away: that left holes in the map.
    private readonly HashSet<string> _probed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _gameOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private int _target = -1;
    private long _targetUntil;
    private bool _worldShown;
    // одну комнату делят несколько входов (гараж агентств, мастерская Kortz, офис Smoke on the Water и Helitours):
    // у каждого первый вход с той же точкой, комнату обставляет только её хозяин, последний, куда переносились
    private readonly int[] _room;
    private readonly Dictionary<int, int> _roomOwner = new Dictionary<int, int>();

    internal InteriorRuntime(OnlineInterior[] interiors, Func<string, bool>? dlcPresent = null)
    {
        _interiors = interiors;
        _dlcPresent = dlcPresent ?? InteriorNatives.IsDlcPresent;
        _loaded = new bool[interiors.Length];
        _furnished = new bool[interiors.Length];
        _opened = new int[interiors.Length];
        _generic = new bool[interiors.Length];
        _lastWanted = new long[interiors.Length];
        _room = interiors.Select(interior => Array.FindIndex(interiors, other => SamePlace(other, interior))).ToArray();
    }

    private static bool SamePlace(OnlineInterior first, OnlineInterior second) =>
        first.X == second.X && first.Y == second.Y && first.Z == second.Z;

    // хозяин комнаты по умолчанию её первый вход
    internal bool OwnsRoom(int index) =>
        (_roomOwner.TryGetValue(_room[index], out int owner) ? owner : _room[index]) == index;

    internal bool IsLoaded(int index) => _loaded[index];
    internal bool IsFurnished(int index) => _furnished[index];

    // The interior is streamed in, so a player can be put inside.
    internal bool IsReady(int index)
    {
        OnlineInterior interior = _interiors[index];
        int id = InteriorNatives.InteriorAt(interior.X, interior.Y, interior.Z);
        return id != 0 && InteriorNatives.IsReady(id);
    }
    internal bool HasState => _target >= 0 || _loaded.Any(loaded => loaded);

    internal void Target(int index, long now)
    {
        _target = index;
        _targetUntil = now + TargetMilliseconds;
        _lastWanted[index] = now;
        _roomOwner[_room[index]] = index;
        if (!_loaded[index]) Load(index);
    }

    internal void Tick(long now, Vector3? local)
    {
        if (_target >= 0 && now > _targetUntil) _target = -1;
        if (!_worldShown) ShowWorld();
        for (int index = 0; index < _interiors.Length; index++)
        {
            OnlineInterior interior = _interiors[index];
            if (InGame(interior))
                ShowGeneric(index, !local.HasValue || !Within(local.Value, interior.X, interior.Y, interior.Z, GenericDistance, threeD: false));

            bool owner = OwnsRoom(index);
            if (owner && (_target == index || (local.HasValue && IsNear(interior, local.Value))))
            {
                _lastWanted[index] = now;
                if (!_loaded[index]) Load(index);
                if (_opened[index] == 0) OpenInterior(index);
                if (!_furnished[index]) Furnish(index);
            }
            else if (_loaded[index] && (!owner || now - _lastWanted[index] >= LeaveMilliseconds))
            {
                Unload(index);
            }
        }
    }

    // The buildings' outsides go on once for the Online map and stay, also
    // after leaving the server: that map has holes without them.
    private void ShowWorld()
    {
        _worldShown = true;
        foreach (OnlineInterior interior in _interiors)
        {
            if (!InGame(interior)) continue;
            foreach (string ipl in interior.World) SpecialPlaceNatives.RequestIpl(ipl);
        }
    }

    private bool InGame(OnlineInterior interior) =>
        (interior.Building.Dlc == null || _dlcPresent(interior.Building.Dlc)) && (interior.Dlc == null || _dlcPresent(interior.Dlc));

    // наши интерьеры в этой комнате игры (у общего гаража агентств их четыре), по ним ищут выход
    internal int[] Find(int room) =>
        room == 0 ? Array.Empty<int>() : Enumerable.Range(0, _opened.Length).Where(index => _opened[index] == room).ToArray();

    internal void Reset()
    {
        _target = -1;
        for (int index = 0; index < _interiors.Length; index++)
        {
            if (_loaded[index]) Unload(index);
        }

        _roomOwner.Clear();
    }

    internal static bool IsNear(OnlineInterior interior, Vector3 position) =>
        Within(position, interior.X, interior.Y, interior.Z, NearInside, threeD: true) ||
        interior.Building.Doors.Any(door => Within(position, door.Position.X, door.Position.Y, door.Position.Z, NearEntrance, threeD: false));

    private static bool Within(Vector3 position, float x, float y, float z, float reach, bool threeD)
    {
        float dx = position.X - x, dy = position.Y - y, dz = threeD ? position.Z - z : 0f;
        return Finite.Is(position) && dx * dx + dy * dy + dz * dz <= reach * reach;
    }

    private void Load(int index)
    {
        OnlineInterior interior = _interiors[index];
        _loaded[index] = true;
        foreach (string ipl in interior.Ipls) Use(ipl);
        _furnished[index] = interior.Sets.Length == 0;
    }

    private void Use(string ipl)
    {
        if (_probed.Add(ipl) && SpecialPlaceNatives.IsIplActive(ipl)) _gameOwned.Add(ipl);
        if (_gameOwned.Contains(ipl)) return;
        _users.TryGetValue(ipl, out int users);
        if (users == 0) SpecialPlaceNatives.RequestIpl(ipl);
        _users[ipl] = users + 1;
    }

    private void Drop(string ipl)
    {
        if (_gameOwned.Contains(ipl) || !_users.TryGetValue(ipl, out int users)) return;
        if (users > 1)
        {
            _users[ipl] = users - 1;
            return;
        }

        _users.Remove(ipl);
        SpecialPlaceNatives.RemoveIpl(ipl);
    }

    private void ShowGeneric(int index, bool show)
    {
        string? generic = _interiors[index].Generic;
        if (generic == null || _generic[index] == show) return;
        _generic[index] = show;
        if (show) SpecialPlaceNatives.RequestIpl(generic);
        else SpecialPlaceNatives.RemoveIpl(generic);
    }

    // The interior exists once its map parts are in; it is opened and pinned
    // then, retried each tick until it exists.
    private void OpenInterior(int index)
    {
        OnlineInterior interior = _interiors[index];
        int id = InteriorNatives.InteriorAt(interior.X, interior.Y, interior.Z);
        if (id == 0) return;
        InteriorNatives.Open(id);
        _opened[index] = id;
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

    // The rooms go: unpinned, without their furnishing and their map parts.
    private void Unload(int index)
    {
        OnlineInterior interior = _interiors[index];
        // общую комнату держит другой вход, пока он загружен, снимается только чужая для него обстановка
        OnlineInterior[] others = _interiors.Where((other, i) => i != index && _loaded[i] && SamePlace(other, interior)).ToArray();
        if (_opened[index] != 0 && others.Length == 0) InteriorNatives.Release(_opened[index]);
        _opened[index] = 0;
        if (interior.Sets.Length > 0 && _furnished[index])
        {
            int id = InteriorNatives.InteriorAt(interior.X, interior.Y, interior.Z);
            if (id != 0)
            {
                InteriorSet[] gone = interior.Sets.Where(set => !others.Any(other => other.Sets.Any(kept => kept.Name == set.Name))).ToArray();
                foreach (InteriorSet set in gone) InteriorNatives.Deactivate(id, set.Name);
                // оставшийся вход обставлен раньше, без обновления комната осталась бы со старой отделкой
                if (others.Length > 0 && gone.Length > 0) InteriorNatives.Refresh(id);
            }
        }

        foreach (string ipl in interior.Ipls) Drop(ipl);
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

    // Story mode keeps some Online interiors switched off or capped (the
    // casino among them): they never stream in until opened, and a player
    // sent there lands under the map or on the roof above. Pinned, the
    // interior stays in memory while it is wanted.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Open(int interior)
    {
        if (Function.Call<bool>(Hash.IS_INTERIOR_DISABLED, interior)) Function.Call(Hash.DISABLE_INTERIOR, interior, false);
        if (Function.Call<bool>(Hash.IS_INTERIOR_CAPPED, interior)) Function.Call(Hash.CAP_INTERIOR, interior, false);
        Function.Call(Hash.PIN_INTERIOR_IN_MEMORY, interior);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Release(int interior) => Function.Call(Hash.UNPIN_INTERIOR, interior);

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
