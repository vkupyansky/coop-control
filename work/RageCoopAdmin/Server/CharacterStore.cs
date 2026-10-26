using OGCoop.Shared;

namespace CoopAdmin.Server;

// One character file per RageCoop name (PlayerFiles), next to the garages.
internal sealed class CharacterStore
{
    private readonly PlayerFiles _files;

    internal CharacterStore(string dataFolder) => _files = new PlayerFiles(dataFolder, "characters", CharacterCodec.MaximumXmlCharacters * 4L);

    // Null when the player has not made a character yet.
    internal CharacterLook? Load(string username) => _files.Read(username) is string xml ? CharacterCodec.Decode(xml) : null;

    internal void Save(string username, CharacterLook look) => _files.Write(username, CharacterCodec.Encode(look));
}
