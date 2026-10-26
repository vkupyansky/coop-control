using System;
using System.Linq;
using OGCoop.Shared;

namespace RageCoopAdmin.Client;

// A berth of GTA Online's yacht: its point and heading from the yacht script
// (am_mp_yacht, func_55; berth 14 is the script's own reference berth) and its
// map parts, hull and interior, apa_yacht_grpGG_N(_int).
internal sealed class YachtBerth
{
    internal YachtBerth(int id, string area, int number, float x, float y, float heading)
    {
        Id = id;
        Area = area;
        Number = number;
        X = x;
        Y = y;
        Heading = heading;
    }

    internal int Id { get; }
    internal string Area { get; }
    internal int Number { get; }
    internal float X { get; }
    internal float Y { get; }
    // The script's point is on the yacht at this height.
    internal const float Z = 9.7027f;
    internal float Heading { get; }
    internal string Hull => "apa_yacht_grp" + (Id / 3 + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "_" + (Id % 3 + 1);
    internal string Interior => Hull + "_int";
}

// GTA Online's yacht, as the yacht script and HKH191's Working Galaxy Super
// Yacht (MIT) put it together: the hull is a map part with its interior; the
// upper decks, rails, lights, door and flags are objects on top of it.
internal static class Yachts
{
    internal static readonly YachtBerth[] Berths =
    {
        new YachtBerth(0, "Чумаш", 1, -3555.115f, 1473.013f, 57f),
        new YachtBerth(1, "Чумаш", 2, -3147.049f, 2827.088f, -88f),
        new YachtBerth(2, "Чумаш", 3, -3277.473f, 2159.85f, -93f),
        new YachtBerth(3, "Лаго-Занкудо", 1, -2822.419f, 4054.84f, 72f),
        new YachtBerth(4, "Лаго-Занкудо", 2, -3249.849f, 3704.681f, -98f),
        new YachtBerth(5, "Лаго-Занкудо", 3, -2383.193f, 4685.003f, 47f),
        new YachtBerth(6, "Пасифик-Блафс", 1, -3224.686f, -215.9825f, -3f),
        new YachtBerth(7, "Пасифик-Блафс", 2, -3447.876f, 291.9275f, 97f),
        new YachtBerth(8, "Пасифик-Блафс", 3, -2713.098f, -528.3185f, -33f),
        new YachtBerth(9, "Веспуччи", 1, -1981.618f, -1537.269f, 142f),
        new YachtBerth(10, "Веспуччи", 2, -2100.817f, -2533.233f, -143f),
        new YachtBerth(11, "Веспуччи", 3, -1599.642f, -1891.277f, 112f),
        new YachtBerth(12, "Аэропорт", 1, -733.6151f, -3916.985f, -168f),
        new YachtBerth(13, "Аэропорт", 2, -363.3534f, -3568.56f, 57f),
        new YachtBerth(14, "Аэропорт", 3, -1478.436f, -3753.538f, -18f),
        new YachtBerth(15, "Терминал", 1, 1535.974f, -3061.877f, 62f),
        new YachtBerth(16, "Терминал", 2, 2471.418f, -2430.93f, 12f),
        new YachtBerth(17, "Терминал", 3, 2067.371f, -2813.01f, -148f),
        new YachtBerth(18, "Паломино", 1, 3021.088f, -1513.602f, 72f),
        new YachtBerth(19, "Паломино", 2, 3025.956f, -704.3854f, -98f),
        new YachtBerth(20, "Паломино", 3, 2961.863f, -2007.631f, 47f),
        new YachtBerth(21, "Татавиам", 1, 3398.169f, 1958.521f, 77f),
        new YachtBerth(22, "Татавиам", 2, 3428.681f, 1202.06f, -148f),
        new YachtBerth(23, "Татавиам", 3, 3787.83f, 2567.884f, -93f),
        new YachtBerth(24, "Сан-Шаньский хребет", 1, 4235.946f, 4004.252f, -118f),
        new YachtBerth(25, "Сан-Шаньский хребет", 2, 4245.151f, 4595.375f, -68f),
        new YachtBerth(26, "Сан-Шаньский хребет", 3, 4209.057f, 3392.705f, -98f),
        new YachtBerth(27, "Гора Гордо", 1, 3738.81f, 5768.252f, -43f),
        new YachtBerth(28, "Гора Гордо", 2, 3472.966f, 6315.245f, -23f),
        new YachtBerth(29, "Гора Гордо", 3, 3693.468f, 5194.659f, 122f),
        new YachtBerth(30, "Прокопио", 1, 572.9806f, 7142.138f, -58f),
        new YachtBerth(31, "Прокопио", 2, 2024.036f, 6907.536f, -173f),
        new YachtBerth(32, "Прокопио", 3, 1377.296f, 6863.23f, -3f),
        new YachtBerth(33, "Палето-Бэй", 1, -1169.36f, 6000.214f, -88f),
        new YachtBerth(34, "Палето-Бэй", 2, -759.2205f, 6573.955f, -153f),
        new YachtBerth(35, "Палето-Бэй", 3, -373.8432f, 6964.86f, -108f),
    };

    internal static readonly string[] Models = { "Orion", "Pisces", "Aquaris" };

    // GTA Online's colour schemes; the number is the objects' tint.
    internal static readonly string[] Colors =
    {
        "Pacific", "Nautical", "Mariner", "Merchant", "Pristine", "Azure", "Uniform", "Ruby", "Mediterranean", "Vintage",
        "Continental", "Battleship", "Command", "Classico", "Intrepid", "Voyager",
    };

    // Lighting 1–8: Presidential, then Vivacious, each green, blue, rose, gold.
    internal static readonly string[] Lights = { "Presidential", "Vivacious" };
    internal static readonly string[] LightColors = { "зелёный", "синий", "розовый", "золотой" };

    // The flag objects of GTA Online's yacht, sorted by their Russian name.
    internal static readonly (string Model, string Name)[] Flags =
    {
        ("apa_prop_flag_australia", "Австралия"), ("apa_prop_flag_austria", "Австрия"), ("apa_prop_flag_england", "Англия"),
        ("apa_prop_flag_argentina", "Аргентина"), ("apa_prop_flag_belgium", "Бельгия"), ("apa_prop_flag_brazil", "Бразилия"),
        ("apa_prop_flag_uk_yt", "Великобритания"), ("apa_prop_flag_hungary", "Венгрия"), ("apa_prop_flag_german_yt", "Германия"),
        ("apa_prop_flag_denmark", "Дания"), ("apa_prop_flag_eu_yt", "Евросоюз"), ("apa_prop_flag_israel", "Израиль"),
        ("apa_prop_flag_ireland", "Ирландия"), ("apa_prop_flag_spain", "Испания"), ("apa_prop_flag_italy", "Италия"),
        ("apa_prop_flag_canada_yt", "Канада"), ("apa_prop_flag_china", "Китай"), ("apa_prop_flag_colombia", "Колумбия"),
        ("apa_prop_flag_lstein", "Лихтенштейн"), ("apa_prop_flag_malta", "Мальта"), ("apa_prop_flag_mexico_yt", "Мексика"),
        ("apa_prop_flag_netherlands", "Нидерланды"), ("apa_prop_flag_nigeria", "Нигерия"), ("apa_prop_flag_newzealand", "Новая Зеландия"),
        ("apa_prop_flag_norway", "Норвегия"), ("apa_prop_flag_palestine", "Палестина"), ("apa_prop_flag_poland", "Польша"),
        ("apa_prop_flag_portugal", "Португалия"), ("apa_prop_flag_puertorico", "Пуэрто-Рико"), ("apa_prop_flag_russia_yt", "Россия"),
        ("apa_prop_flag_slovakia", "Словакия"), ("apa_prop_flag_slovenia", "Словения"), ("apa_prop_flag_us_yt", "США"),
        ("apa_prop_flag_turkey", "Турция"), ("apa_prop_flag_wales", "Уэльс"), ("apa_prop_flag_finland", "Финляндия"),
        ("apa_prop_flag_france", "Франция"), ("apa_prop_flag_croatia", "Хорватия"), ("apa_prop_flag_czechrep", "Чехия"),
        ("apa_prop_flag_switzerland", "Швейцария"), ("apa_prop_flag_sweden", "Швеция"), ("apa_prop_flag_scotland_yt", "Шотландия"),
        ("apa_prop_flag_southafrica", "ЮАР"), ("apa_prop_flag_southkorea", "Южная Корея"), ("apa_prop_flag_jamaica", "Ямайка"),
        ("apa_prop_flag_japan_yt", "Япония"),
    };

    internal static string BerthName(YachtBerth berth) => berth.Area + " " + berth.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // The objects on the hull for a look: the upper decks and their colour
    // parts take the hull colour, the rails theirs, the rest none.
    internal static (string Model, YachtPart Kind)[] Parts(YachtLook look)
    {
        int model = look.Model + 1;
        var parts = new System.Collections.Generic.List<(string, YachtPart)> { ("apa_mp_apa_yacht_option" + model, YachtPart.Colored) };
        if (look.Model == 1)
        {
            parts.Add(("apa_mp_apa_yacht_option2_cola", YachtPart.Colored));
            parts.Add(("apa_mp_apa_yacht_option2_colb", YachtPart.Rails));
        }
        else if (look.Model == 2)
        {
            foreach (string part in new[] { "cola", "colc", "cole" }) parts.Add(("apa_mp_apa_yacht_option3_" + part, YachtPart.Colored));
            foreach (string part in new[] { "colb", "cold" }) parts.Add(("apa_mp_apa_yacht_option3_" + part, YachtPart.Rails));
        }
        parts.Add(("apa_mp_apa_yacht_o" + model + "_rail_" + (look.GoldRails ? "b" : "a"), YachtPart.Rails));
        if (look.Lighting > 0)
        {
            int style = (look.Lighting - 1) / 4 + 1;
            char colour = (char)('a' + (look.Lighting - 1) % 4);
            parts.Add(("apa_mp_apa_y" + model + "_l" + style + colour, YachtPart.Plain));
        }
        parts.Add(("apa_mp_apa_yacht_door", YachtPart.Door));
        parts.Add(("apa_mp_apa_yacht_jacuzzi_ripple1", YachtPart.Jacuzzi));
        parts.Add(("apa_mp_apa_yacht_jacuzzi_ripple2", YachtPart.Jacuzzi));
        return parts.ToArray();
    }

    // Every model a yacht look may need, for the catalog check.
    internal static string[] AllModels() =>
        Enumerable.Range(0, YachtCodec.ModelCount).SelectMany(model => Enumerable.Range(0, YachtCodec.LightingCount + 1).SelectMany(lighting =>
            new[] { false, true }.SelectMany(gold => Parts(new YachtLook { Model = model, Lighting = lighting, GoldRails = gold }).Select(part => part.Model))))
            .Concat(Flags.Select(flag => flag.Model)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

// Where an object sits on the hull and which colour it takes.
internal enum YachtPart { Colored, Rails, Plain, Door, Jacuzzi }
