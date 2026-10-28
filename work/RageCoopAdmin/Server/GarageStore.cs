using OGCoop.Shared;

namespace CoopAdmin.Server;

internal sealed class GarageStore
{
    private readonly PlayerFiles _files;

    internal GarageStore(string dataFolder) => _files = new PlayerFiles(dataFolder, "garages", GarageCodec.MaximumXmlCharacters * 4L);

    internal GarageData Load(string username) =>
        _files.Read(username) is string xml ? GarageCodec.Decode(xml) : new GarageData();

    internal void Save(string username, GarageData garage) => _files.Write(username, GarageCodec.Encode(garage));
}
