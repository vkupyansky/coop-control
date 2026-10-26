using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using OGCoop.Shared;
using GTA;
using GTA.Native;

namespace RageCoopAdmin.Client;

// A tattoo GTA Online's parlours sell for one Online body.
internal sealed class TattooOption
{
    internal TattooOption(int collection, int name, int zone, string label)
    {
        Collection = collection;
        Name = name;
        Zone = zone;
        Label = label;
    }

    internal int Collection { get; }
    internal int Name { get; }
    // GTA's body part of the decoration: 0 torso, 1 head, 2–3 arms, 4–5 legs.
    internal int Zone { get; }
    internal string Label { get; }

    internal bool Is(CharacterTattoo tattoo) => tattoo.Collection == Collection && tattoo.Name == Name;

    internal CharacterTattoo ToTattoo() => new CharacterTattoo { Collection = Collection, Name = Name };
}

// The tattoos of each Online body, as the parlour script (tattoo_shop) builds
// its list: the launch ones from its own table, the later ones from the game's
// tattoo data of every update. Read once per body. Game thread only.
internal static class CharacterTattoos
{
    internal static readonly string[] Zones = { "Торс", "Голова", "Левая рука", "Правая рука", "Левая нога", "Правая нога" };

    // The parlour script's launch tattoos: text label, award tattoo, number.
    // Crew emblems, T-shirt decals and hair fades in the same table are not
    // tattoos and are left out.
    private static readonly (string Label, bool Award, string Number)[] Launch =
    {
        ("TAT_FM_008", true, "000"), ("TAT_FM_009", true, "001"), ("TAT_FM_010", true, "002"), ("TAT_FM_011", true, "003"),
        ("TAT_FM_012", true, "004"), ("TAT_FM_013", true, "005"), ("TAT_FM_014", true, "006"), ("TAT_FM_015", true, "007"),
        ("TAT_FM_016", true, "008"), ("TAT_FM_017", true, "009"), ("TAT_FM_018", true, "010"), ("TAT_FM_019", true, "011"),
        ("TAT_FM_020", true, "012"), ("TAT_FM_021", true, "013"), ("TAT_FM_022", true, "014"), ("TAT_FM_023", true, "015"),
        ("TAT_FM_024", true, "016"), ("TAT_FM_025", true, "017"), ("TAT_FM_026", true, "018"), ("TAT_FM_027", true, "019"),
        ("TAT_FM_201", false, "005"), ("TAT_FM_202", false, "006"), ("TAT_FM_203", false, "015"), ("TAT_FM_204", false, "000"),
        ("TAT_FM_205", false, "001"), ("TAT_FM_206", false, "003"), ("TAT_FM_207", false, "014"), ("TAT_FM_208", false, "018"),
        ("TAT_FM_209", false, "002"), ("TAT_FM_210", false, "007"), ("TAT_FM_211", false, "008"), ("TAT_FM_212", false, "017"),
        ("TAT_FM_213", false, "009"), ("TAT_FM_214", false, "011"), ("TAT_FM_215", false, "013"), ("TAT_FM_216", false, "016"),
        ("TAT_FM_217", false, "019"), ("TAT_FM_218", false, "010"), ("TAT_FM_219", false, "004"), ("TAT_FM_220", false, "012"),
        ("TAT_FM_221", false, "020"), ("TAT_FM_222", false, "021"), ("TAT_FM_223", false, "022"), ("TAT_FM_224", false, "023"),
        ("TAT_FM_225", false, "024"), ("TAT_FM_226", false, "025"), ("TAT_FM_227", false, "026"), ("TAT_FM_228", false, "027"),
        ("TAT_FM_229", false, "028"), ("TAT_FM_230", false, "029"), ("TAT_FM_231", false, "030"), ("TAT_FM_232", false, "031"),
        ("TAT_FM_233", false, "032"), ("TAT_FM_234", false, "033"), ("TAT_FM_235", false, "034"), ("TAT_FM_236", false, "035"),
        ("TAT_FM_237", false, "036"), ("TAT_FM_238", false, "037"), ("TAT_FM_239", false, "038"), ("TAT_FM_240", false, "039"),
        ("TAT_FM_241", false, "040"), ("TAT_FM_242", false, "041"), ("TAT_FM_243", false, "042"), ("TAT_FM_244", false, "043"),
        ("TAT_FM_245", false, "044"), ("TAT_FM_246", false, "045"), ("TAT_FM_247", false, "047"),
    };
    // The script names this one's item for the man's body on both bodies.
    private const string ManOnlyLabel = "TAT_FM_204";
    private const string LaunchCollection = "multiplayer_overlays";
    // GTA's tattoo data record: 8-byte fields; collection and item in fields 2
    // and 3, the text label written in place from field 7. The buffer is far
    // larger than the record and zeroed, so the label always ends.
    private const int RecordBytes = 256;
    private const int CollectionOffset = 2 * 8;
    private const int NameOffset = 3 * 8;
    private const int LabelOffset = 7 * 8;
    private const int MaximumUpdateItems = 4096;
    private static readonly Regex Label = new Regex("^[A-Za-z0-9_]{1,31}$", RegexOptions.CultureInvariant);
    private static readonly Dictionary<int, TattooOption[]> Known = new Dictionary<int, TattooOption[]>();

    internal static TattooOption[] For(int model, Action<string>? log = null)
    {
        if (!CharacterModels.IsOnline(model)) return Array.Empty<TattooOption>();
        if (Known.TryGetValue(model, out TattooOption[]? known)) return known;
        bool man = model == CharacterModels.Male;
        var options = new List<TattooOption>();
        var seen = new HashSet<(int, int)>();
        int collection = Joaat(LaunchCollection);
        foreach ((string label, bool award, string number) in Launch)
        {
            string item = (award ? "FM_Tat_Award_" : "FM_Tat_") + (man || label == ManOnlyLabel ? "M_" : "F_") + number;
            Add(options, seen, collection, Joaat(item), label);
        }
        int launch = options.Count;

        int character = man ? 3 : 4; // GTA's tattoo data: 3 the Online man, 4 the woman
        int count = Math.Min(MaximumUpdateItems, Function.Call<int>((Hash)0x278F76C3B0A8F109UL, character)); // GET_NUM_TATTOO_SHOP_DLC_ITEMS
        int named = 0;
        IntPtr record = Marshal.AllocHGlobal(RecordBytes);
        try
        {
            var empty = new byte[RecordBytes];
            for (int index = 0; index < count; index++)
            {
                Marshal.Copy(empty, 0, record, RecordBytes);
                if (!Function.Call<bool>((Hash)0xFF56381874F82086UL, character, index, record)) continue; // GET_TATTOO_SHOP_DLC_ITEM_DATA
                string label = Marshal.PtrToStringAnsi(IntPtr.Add(record, LabelOffset)) ?? string.Empty;
                if (Add(options, seen, Marshal.ReadInt32(record, CollectionOffset), Marshal.ReadInt32(record, NameOffset), label) &&
                    options[options.Count - 1].Label.Length > 0) named++;
            }
        }
        finally { Marshal.FreeHGlobal(record); }

        TattooOption[] result = options.ToArray();
        Known[model] = result;
        log?.Invoke("OG Coop: tattoos for the " + (man ? "man's" : "woman's") + " body: " + launch + " launch, " +
            (result.Length - launch) + " from updates (" + named + " named) of " + count + " in the game's data.");
        return result;
    }

    // The tattoo's name in the game's language; null when it has none.
    internal static string? Name(TattooOption option)
    {
        if (option.Label.Length == 0 || !Function.Call<bool>(Hash.DOES_TEXT_LABEL_EXIST, option.Label)) return null;
        string name = Game.GetLocalizedString(option.Label);
        return string.IsNullOrWhiteSpace(name) || name == "NULL" ? null : name.Trim();
    }

    private static bool Add(List<TattooOption> options, HashSet<(int, int)> seen, int collection, int name, string label)
    {
        if (collection == 0 || name == 0 || !seen.Add((collection, name))) return false;
        int zone = Function.Call<int>((Hash)0x9FD452BFBE7A7A8BUL, collection, name); // GET_PED_DECORATION_ZONE_FROM_HASHES
        if (zone < 0 || zone >= Zones.Length) return false;
        options.Add(new TattooOption(collection, name, zone, Label.IsMatch(label) ? label : string.Empty));
        return true;
    }

    private static int Joaat(string value) => unchecked((int)StringHash.AtStringHash(value));
}
