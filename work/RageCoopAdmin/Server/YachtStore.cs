using OGCoop.Shared;

namespace CoopAdmin.Server;

internal sealed class YachtStore
{
    private readonly PlayerFiles _files;

    internal YachtStore(string dataFolder) => _files = new PlayerFiles(dataFolder, "yachts", YachtCodec.MaximumXmlCharacters * 4L);

    // Null when the player has no yacht yet.
    internal YachtLook? Load(string username) => _files.Read(username) is string xml ? YachtCodec.Decode(xml) : null;

    internal void Save(string username, YachtLook look) => _files.Write(username, YachtCodec.Encode(look));
}
