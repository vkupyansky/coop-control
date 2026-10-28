using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace OGCoop.Shared;

// One player's saved cars and favourite models. The server keeps it per
// RageCoop name, so every computer the player connects from sees the same list.
[XmlRoot("Garage")]
public sealed class GarageData
{
    [XmlAttribute("version")]
    public int Version { get; set; } = 1;
    [XmlArrayItem("Vehicle")]
    public List<VehiclePreset> Vehicles { get; set; } = new List<VehiclePreset>();
    [XmlArrayItem("Model")]
    public List<int> Favorites { get; set; } = new List<int>();
}

public sealed class VehiclePreset
{
    public string Name { get; set; } = string.Empty;
    public VehicleAppearance Appearance { get; set; } = new VehicleAppearance();
}

internal enum GarageResult { Saved = 0, Full = 1, DuplicateName = 2, Invalid = 3, Missing = 4, Unavailable = 5 }

internal static class GarageProtocol
{
    // Client to server: []. Server to client: [string garage].
    internal const int Request = 20774160;
    internal const int State = 20774161;
    // Client to server: [int token, int operation, value...]. Server to client: [int token, int result].
    internal const int Change = 20774162;
    internal const int Result = 20774163;
    internal const int SavePreset = 1;      // [token, op, string name, string appearance]
    internal const int DeletePreset = 2;    // [token, op, string name]
    internal const int AddFavorite = 3;     // [token, op, int model]
    internal const int RemoveFavorite = 4;  // [token, op, int model]
}

internal static class GarageCodec
{
    internal const int MaximumPresets = 64;
    internal const int MaximumFavorites = 2048;
    internal const int MaximumNameLength = 40;
    internal const int MaximumXmlCharacters = 1024 * 1024;
    private static readonly XmlSerializer GarageSerializer = new XmlSerializer(typeof(GarageData));

    internal static string Encode(GarageData garage)
    {
        Validate(garage);
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        GarageSerializer.Serialize(writer, garage);
        string xml = writer.ToString();
        if (xml.Length > MaximumXmlCharacters) throw new InvalidDataException("The garage is too large.");
        return xml;
    }

    internal static GarageData Decode(string xml)
    {
        if (xml == null || xml.Length > MaximumXmlCharacters) throw new InvalidDataException("The garage is too large.");
        if (Read(GarageSerializer, new StringReader(xml)) is not GarageData garage || garage.Version != 1 ||
            garage.Vehicles == null || garage.Favorites == null) throw new InvalidDataException("The garage format is unsupported.");
        Validate(garage);
        return garage;
    }

    internal static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name!.Length <= MaximumNameLength &&
        !name.Any(c => char.IsControl(c) || c == '~');

    internal static void Validate(GarageData garage)
    {
        if (garage.Vehicles.Count > MaximumPresets || garage.Vehicles.Any(preset => preset == null || !IsValidName(preset.Name) || preset.Appearance == null) ||
            garage.Vehicles.Select(preset => preset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != garage.Vehicles.Count ||
            garage.Favorites.Count > MaximumFavorites || garage.Favorites.Any(model => model == 0) ||
            garage.Favorites.Distinct().Count() != garage.Favorites.Count)
            throw new InvalidDataException("The garage must have up to 64 distinctly named cars and 2048 distinct favourite models.");
        foreach (VehiclePreset preset in garage.Vehicles) VehicleAppearanceCodec.Validate(preset.Appearance);
    }

    // Applies one client request to the stored garage. Returns false when the
    // request is malformed, so the server can ignore it without a reply.
    internal static bool TryApply(GarageData garage, object[] args, out int token, out GarageResult result)
    {
        token = 0;
        result = GarageResult.Invalid;
        if (args == null || args.Length < 3 || args[0] is not int requestToken || args[1] is not int operation) return false;
        token = requestToken;
        try
        {
            result = operation switch
            {
                GarageProtocol.SavePreset when args.Length == 4 && args[2] is string name && args[3] is string appearance =>
                    Save(garage, name, VehicleAppearanceCodec.Decode(appearance)),
                GarageProtocol.DeletePreset when args.Length == 3 && args[2] is string name =>
                    garage.Vehicles.RemoveAll(preset => string.Equals(preset.Name, name, StringComparison.Ordinal)) > 0 ? GarageResult.Saved : GarageResult.Missing,
                GarageProtocol.AddFavorite when args.Length == 3 && args[2] is int model && model != 0 =>
                    garage.Favorites.Contains(model) ? GarageResult.Saved :
                    garage.Favorites.Count >= MaximumFavorites ? GarageResult.Full : Add(garage.Favorites, model),
                GarageProtocol.RemoveFavorite when args.Length == 3 && args[2] is int model =>
                    garage.Favorites.Remove(model) ? GarageResult.Saved : GarageResult.Missing,
                _ => GarageResult.Invalid
            };
        }
        catch (InvalidDataException) { result = GarageResult.Invalid; }
        catch (InvalidOperationException) { result = GarageResult.Invalid; }
        return true;
    }

    private static GarageResult Save(GarageData garage, string name, VehicleAppearance appearance)
    {
        if (!IsValidName(name)) return GarageResult.Invalid;
        if (garage.Vehicles.Any(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))) return GarageResult.DuplicateName;
        if (garage.Vehicles.Count >= MaximumPresets) return GarageResult.Full;
        garage.Vehicles.Add(new VehiclePreset { Name = name, Appearance = appearance });
        return GarageResult.Saved;
    }

    private static GarageResult Add(List<int> favorites, int model)
    {
        favorites.Add(model);
        return GarageResult.Saved;
    }

    private static object? Read(XmlSerializer serializer, TextReader text)
    {
        using XmlReader reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlCharacters
        });
        return serializer.Deserialize(reader);
    }
}
