using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace OGCoop.Shared;

// The server keeps a yacht per RageCoop name and hands it to the other players.
// Every game puts the yachts of all players on the server on its own map.
[XmlRoot("Yacht")]
public sealed class YachtLook
{
    [XmlAttribute("version")]
    public int Version { get; set; } = 1;
    // GTA Online's berth number, 0-35 (YachtCodec.BerthCount).
    public int Berth { get; set; }
    // 0 Orion, 1 Pisces, 2 Aquaris.
    public int Model { get; set; }
    // GTA Online's colour scheme, 0-15.
    public int Color { get; set; }
    public bool GoldRails { get; set; }
    // 0 none, 1-4 Presidential, 5-8 Vivacious (green, blue, rose, gold).
    public int Lighting { get; set; }
    // 0 none, otherwise a flag of YachtCodec.FlagCount.
    public int Flag { get; set; }
}

internal static class YachtProtocol
{
    // Client to server: []. Server to client: [string own yacht] ("" when none yet).
    internal const int Request = 20774190;
    internal const int State = 20774191;
    // Client to server: [string yacht]. Server to client: [bool saved].
    internal const int Save = 20774192;
    internal const int Saved = 20774193;
    // Server to client: [string username, string yacht], another player's
    // yacht, "" when that player left and the yacht goes with them.
    internal const int Look = 20774194;
}

internal static class YachtCodec
{
    internal const int MaximumXmlCharacters = 1024;
    internal const int BerthCount = 36;
    // Berth 4 (apa_yacht_grp02_2) lies 271 m from the carrier off Zancudo.
    internal const int CarrierBerth = 4;
    internal const int ModelCount = 3;
    internal const int ColorCount = 16;
    internal const int LightingCount = 8;
    internal const int FlagCount = 46;
    private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(YachtLook));

    internal static bool IsBerth(int berth) => berth >= 0 && berth < BerthCount && berth != CarrierBerth;

    internal static string Encode(YachtLook look)
    {
        Validate(look);
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        Serializer.Serialize(writer, look);
        string xml = writer.ToString();
        if (xml.Length > MaximumXmlCharacters) throw new InvalidDataException("The yacht is too large.");
        return xml;
    }

    internal static YachtLook Decode(string xml)
    {
        if (xml == null || xml.Length > MaximumXmlCharacters) throw new InvalidDataException("The yacht is too large.");
        using XmlReader reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlCharacters
        });
        if (Serializer.Deserialize(reader) is not YachtLook look || look.Version != 1)
            throw new InvalidDataException("The yacht format is unsupported.");
        Validate(look);
        return look;
    }

    internal static void Validate(YachtLook look)
    {
        if (!IsBerth(look.Berth)) throw new InvalidDataException("The yacht berth is unknown.");
        if (look.Model is < 0 or >= ModelCount || look.Color is < 0 or >= ColorCount ||
            look.Lighting is < 0 or > LightingCount || look.Flag is < 0 or > FlagCount)
            throw new InvalidDataException("The yacht's model, colour, lights or flag are unknown.");
    }

    internal static YachtLook Copy(YachtLook look) => new YachtLook
    {
        Berth = look.Berth,
        Model = look.Model,
        Color = look.Color,
        GoldRails = look.GoldRails,
        Lighting = look.Lighting,
        Flag = look.Flag,
    };

    internal static bool Same(YachtLook? first, YachtLook? second) =>
        first == null || second == null ? first == second : Encode(first) == Encode(second);

    // Skips the carrier's berth, -1 when every berth is taken.
    internal static int FreeBerth(int from, Func<int, bool> taken)
    {
        for (int step = 0; step < BerthCount; step++)
        {
            int berth = ((from < 0 ? 0 : from) + step) % BerthCount;
            if (IsBerth(berth) && !taken(berth)) return berth;
        }
        return -1;
    }

    // Without a free berth or a successful save the yacht stays where it was.
    internal static bool MoveToFreeBerth(YachtLook look, int free, Func<YachtLook, bool> save)
    {
        if (free < 0) return false;
        int berth = look.Berth;
        look.Berth = free;
        if (save(look)) return true;
        look.Berth = berth;
        return false;
    }
}
