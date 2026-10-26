using System.IO;
using OGCoop.Shared;

namespace CoopAdmin.Server;

// One garage file per RageCoop name (PlayerFiles). A former computer file
// placed in import/<name>.vehicle-presets.xml is merged into that player's
// garage the first time it is loaded.
internal sealed class GarageStore
{
    private readonly PlayerFiles _files;

    internal GarageStore(string dataFolder) => _files = new PlayerFiles(dataFolder, "garages", GarageCodec.MaximumXmlCharacters * 4L);

    internal GarageData Load(string username)
    {
        if (_files.Read(username) is string xml) return GarageCodec.Decode(xml);
        var garage = new GarageData();
        string import = Path.Combine(_files.Folder, "import", PlayerFiles.Key(username) + ".vehicle-presets.xml");
        if (!File.Exists(import)) return garage;
        using (var presets = new StringReader(_files.ReadBounded(import)))
            GarageCodec.Merge(garage, GarageCodec.ReadLegacy(presets, null));
        Save(username, garage);
        File.Move(import, import + ".imported", overwrite: true);
        return garage;
    }

    internal void Save(string username, GarageData garage) => _files.Write(username, GarageCodec.Encode(garage));
}
