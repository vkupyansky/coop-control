using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace OGCoop.Shared;

// A player's character: the GTA Online body (man or woman) with clothes and
// accessories, or the player's own story hero. The server keeps it per
// RageCoop name and hands it to the other players, because RageCoop sends
// clothes as bytes and cuts every Online item numbered above 255.
[XmlRoot("Character")]
public sealed class CharacterLook
{
    [XmlAttribute("version")]
    public int Version { get; set; } = 1;
    // 0: the story hero the player has; otherwise CharacterModels.Male or Female.
    public int Model { get; set; }
    [XmlArrayItem("Part")]
    public List<CharacterPart> Clothes { get; set; } = new List<CharacterPart>();
    [XmlArrayItem("Part")]
    public List<CharacterPart> Props { get; set; } = new List<CharacterPart>();
    // The face and hair; absent means CharacterHead.Default (characters saved
    // before the face editor).
    public CharacterHead? Head { get; set; }
    [XmlArrayItem("Tattoo")]
    public List<CharacterTattoo> Tattoos { get; set; } = new List<CharacterTattoo>();

    // Characters without tattoos keep the form they had before tattoos.
    public bool ShouldSerializeTattoos() => Tattoos is { Count: > 0 };

    internal bool IsOnline => Model != 0;
}

// A tattoo: an item of one of GTA's decoration collections, both by name hash.
public sealed class CharacterTattoo
{
    [XmlAttribute("collection")]
    public int Collection { get; set; }
    [XmlAttribute("name")]
    public int Name { get; set; }
}

// An Online face as GTA Online's creator builds it: parents and how much the
// face and skin take after the father (percent), 20 face features (−10…10,
// tenths of GTA's −1…1), eye colour, hair colour with highlights and the head
// overlays (eyebrows, beard, make-up, skin details).
public sealed class CharacterHead
{
    [XmlAttribute("mother")]
    public int Mother { get; set; } = 21;
    [XmlAttribute("father")]
    public int Father { get; set; }
    [XmlAttribute("resemblance")]
    public int Resemblance { get; set; } = 50;
    [XmlAttribute("skin")]
    public int SkinTone { get; set; } = 50;
    [XmlAttribute("eyes")]
    public int EyeColor { get; set; }
    [XmlAttribute("hair")]
    public int HairColor { get; set; }
    [XmlAttribute("highlight")]
    public int HairHighlight { get; set; }
    [XmlArrayItem("Feature")]
    public List<CharacterFeature> Features { get; set; } = new List<CharacterFeature>();
    [XmlArrayItem("Overlay")]
    public List<CharacterOverlay> Overlays { get; set; } = new List<CharacterOverlay>();

    // A new Online face: Hannah and Benjamin, natural eyebrows, black hair.
    internal static CharacterHead Default(int model) => new CharacterHead
    {
        Resemblance = model == CharacterModels.Female ? 20 : 80,
        Overlays = { new CharacterOverlay { Slot = CharacterCodec.Eyebrows, Style = 0, Opacity = 100 } },
    };

    internal int Feature(int index) => Features.FirstOrDefault(feature => feature.Index == index)?.Value ?? 0;

    internal void SetFeature(int index, int value)
    {
        Features.RemoveAll(feature => feature.Index == index);
        if (value != 0) Features.Add(new CharacterFeature { Index = index, Value = value });
    }

    // Absent overlays are not worn.
    internal CharacterOverlay Overlay(int slot) =>
        Overlays.FirstOrDefault(overlay => overlay.Slot == slot) ?? new CharacterOverlay { Slot = slot, Style = -1, Opacity = 100 };

    // Kept even while not worn, so a chosen strength or colour survives
    // switching the style off and on.
    internal void SetOverlay(CharacterOverlay overlay)
    {
        Overlays.RemoveAll(existing => existing.Slot == overlay.Slot);
        Overlays.Add(overlay);
    }

    internal CharacterHead Copy() => new CharacterHead
    {
        Mother = Mother,
        Father = Father,
        Resemblance = Resemblance,
        SkinTone = SkinTone,
        EyeColor = EyeColor,
        HairColor = HairColor,
        HairHighlight = HairHighlight,
        Features = Features.Select(feature => new CharacterFeature { Index = feature.Index, Value = feature.Value }).ToList(),
        Overlays = Overlays.Select(overlay => overlay.Copy()).ToList(),
    };
}

public sealed class CharacterFeature
{
    [XmlAttribute("index")]
    public int Index { get; set; }
    [XmlAttribute("value")]
    public int Value { get; set; }
}

// A head overlay: style (−1 none), strength in percent and colour.
public sealed class CharacterOverlay
{
    [XmlAttribute("slot")]
    public int Slot { get; set; }
    [XmlAttribute("style")]
    public int Style { get; set; }
    [XmlAttribute("opacity")]
    public int Opacity { get; set; } = 100;
    [XmlAttribute("color")]
    public int Color { get; set; }

    internal CharacterOverlay Copy() => new CharacterOverlay { Slot = Slot, Style = Style, Opacity = Opacity, Color = Color };
}

// One clothing slot (component 0–11) or accessory slot (prop anchor); a prop
// drawable of -1 means nothing is worn there.
public sealed class CharacterPart
{
    [XmlAttribute("slot")]
    public int Slot { get; set; }
    [XmlAttribute("drawable")]
    public int Drawable { get; set; }
    [XmlAttribute("texture")]
    public int Texture { get; set; }
}

internal static class CharacterModels
{
    internal const int Male = 0x705E61F2;               // mp_m_freemode_01
    internal const int Female = unchecked((int)0x9C9EFFD8); // mp_f_freemode_01

    internal static bool IsOnline(int model) => model == Male || model == Female;
}

internal static class CharacterProtocol
{
    // Client → server: []. Server → client: [string own look] ("" when none is saved yet).
    internal const int Request = 20774180;
    internal const int State = 20774181;
    // Client → server: [string look]. Server → client: [bool saved].
    internal const int Save = 20774182;
    internal const int Saved = 20774183;
    // Server → client: [string username, string look]: another player's character.
    internal const int Look = 20774184;
}

internal static class CharacterCodec
{
    internal const int MaximumXmlCharacters = 16 * 1024;
    internal const int ComponentCount = 12;
    internal const int MaximumDrawable = 4095;
    internal const int MaximumTexture = 255;
    // Hats, glasses, ears, watches and bracelets: the accessories GTA Online sells.
    internal static readonly int[] PropSlots = { 0, 1, 2, 6, 7 };
    // GTA's parent faces: 0–20 and 42–44 fathers, 21–41 and 45 mothers.
    internal const int ParentCount = 46;
    internal const int FeatureCount = 20;
    internal const int FeatureLimit = 10;
    internal const int EyeColorCount = 32;
    internal const int TintCount = 64;
    // Head overlays 0–10: blemishes, beard, eyebrows, ageing, make-up, blush,
    // complexion, sun damage, lipstick, moles and freckles, chest hair.
    internal const int OverlayCount = 11;
    internal const int Eyebrows = 2;
    internal const int MaximumOverlayStyle = 254;
    internal const int MaximumTattoos = 64;
    private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(CharacterLook));

    internal static string Encode(CharacterLook look)
    {
        Validate(look);
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        Serializer.Serialize(writer, look);
        string xml = writer.ToString();
        if (xml.Length > MaximumXmlCharacters) throw new InvalidDataException("The character is too large.");
        return xml;
    }

    internal static CharacterLook Decode(string xml)
    {
        if (xml == null || xml.Length > MaximumXmlCharacters) throw new InvalidDataException("The character is too large.");
        using XmlReader reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlCharacters
        });
        if (Serializer.Deserialize(reader) is not CharacterLook look || look.Version != 1 || look.Clothes == null || look.Props == null ||
            look.Tattoos == null)
            throw new InvalidDataException("The character format is unsupported.");
        Validate(look);
        return look;
    }

    // A story hero carries no clothes of ours; an Online body lists every
    // clothing slot once and only the accessory slots GTA Online uses.
    internal static void Validate(CharacterLook look)
    {
        if (look.Model != 0 && !CharacterModels.IsOnline(look.Model)) throw new InvalidDataException("The character body is unknown.");
        if (!look.IsOnline)
        {
            if (look.Clothes.Count != 0 || look.Props.Count != 0 || look.Head != null || look.Tattoos.Count != 0)
                throw new InvalidDataException("A story hero keeps the game's own clothes, face and tattoos.");
            return;
        }
        if (look.Head != null) ValidateHead(look.Head);
        if (look.Tattoos.Count > MaximumTattoos || look.Tattoos.Any(tattoo => tattoo == null || tattoo.Collection == 0 || tattoo.Name == 0) ||
            look.Tattoos.Select(tattoo => (tattoo.Collection, tattoo.Name)).Distinct().Count() != look.Tattoos.Count)
            throw new InvalidDataException("The character's tattoos are invalid.");
        if (look.Clothes.Count != ComponentCount || look.Clothes.Any(part => part == null) ||
            !look.Clothes.Select(part => part.Slot).OrderBy(slot => slot).SequenceEqual(Enumerable.Range(0, ComponentCount)) ||
            look.Clothes.Any(part => part.Drawable is < 0 or > MaximumDrawable || part.Texture is < 0 or > MaximumTexture))
            throw new InvalidDataException("The character must list each of the 12 clothing slots once.");
        if (look.Props.Count > PropSlots.Length || look.Props.Any(part => part == null || !PropSlots.Contains(part.Slot)) ||
            look.Props.Select(part => part.Slot).Distinct().Count() != look.Props.Count ||
            look.Props.Any(part => part.Drawable is < -1 or > MaximumDrawable || part.Texture is < 0 or > MaximumTexture))
            throw new InvalidDataException("The character's accessories are invalid.");
    }

    private static void ValidateHead(CharacterHead head)
    {
        if (head.Mother is < 0 or >= ParentCount || head.Father is < 0 or >= ParentCount ||
            head.Resemblance is < 0 or > 100 || head.SkinTone is < 0 or > 100 || head.EyeColor is < 0 or >= EyeColorCount ||
            head.HairColor is < 0 or >= TintCount || head.HairHighlight is < 0 or >= TintCount)
            throw new InvalidDataException("The character's parents, eyes or hair colour are invalid.");
        if (head.Features == null || head.Features.Count > FeatureCount || head.Features.Any(feature => feature == null) ||
            head.Features.Select(feature => feature.Index).Distinct().Count() != head.Features.Count ||
            head.Features.Any(feature => feature.Index is < 0 or >= FeatureCount || feature.Value is < -FeatureLimit or > FeatureLimit))
            throw new InvalidDataException("The character's face features are invalid.");
        if (head.Overlays == null || head.Overlays.Count > OverlayCount || head.Overlays.Any(overlay => overlay == null) ||
            head.Overlays.Select(overlay => overlay.Slot).Distinct().Count() != head.Overlays.Count ||
            head.Overlays.Any(overlay => overlay.Slot is < 0 or >= OverlayCount || overlay.Style is < -1 or > MaximumOverlayStyle ||
                overlay.Opacity is < 0 or > 100 || overlay.Color is < 0 or >= TintCount))
            throw new InvalidDataException("The character's eyebrows, beard, make-up or skin details are invalid.");
    }

    internal static bool SameLook(CharacterLook? first, CharacterLook? second) =>
        first == null || second == null ? first == second : Encode(first) == Encode(second);
}
