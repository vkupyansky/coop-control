using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CoopControl.Shared;

namespace CoopAdmin.Server;

// One garage file per RageCoop name in the resource data folder. RageCoop
// compares names case-insensitively, so the key is the lower-cased name.
// A former computer file placed in import/<name>.vehicle-presets.xml is merged
// into that player's garage the first time it is loaded.
internal sealed class GarageStore
{
    private readonly string _folder;

    internal GarageStore(string dataFolder)
    {
        if (string.IsNullOrWhiteSpace(dataFolder)) throw new ArgumentException("The resource data folder is required.", nameof(dataFolder));
        _folder = Path.Combine(dataFolder, "garages");
    }

    internal static string Key(string username)
    {
        string name = (username ?? string.Empty).Trim().ToLowerInvariant();
        bool plain = name.Length is > 0 and <= 32 && name[0] != '.' && name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.');
        if (plain) return name;
        using SHA256 hash = SHA256.Create();
        return "user-" + Convert.ToHexString(hash.ComputeHash(Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
    }

    internal GarageData Load(string username)
    {
        string key = Key(username);
        string path = Path.Combine(_folder, key + ".xml");
        if (File.Exists(path)) return GarageCodec.Decode(ReadBounded(path));
        var garage = new GarageData();
        string import = Path.Combine(_folder, "import", key + ".vehicle-presets.xml");
        if (!File.Exists(import)) return garage;
        using (var presets = new StringReader(ReadBounded(import)))
            GarageCodec.Merge(garage, GarageCodec.ReadLegacy(presets, null));
        Save(username, garage);
        File.Move(import, import + ".imported", overwrite: true);
        return garage;
    }

    internal void Save(string username, GarageData garage)
    {
        string xml = GarageCodec.Encode(garage);
        byte[] bytes = new UTF8Encoding(false).GetBytes(xml);
        AtomicFile.Write(Path.Combine(_folder, Key(username) + ".xml"), stream => stream.Write(bytes, 0, bytes.Length));
    }

    private static string ReadBounded(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > GarageCodec.MaximumXmlCharacters * 4L) throw new InvalidDataException("The garage file is too large.");
        return File.ReadAllText(path, Encoding.UTF8);
    }
}
