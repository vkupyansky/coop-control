using System.IO;
using System.Xml;
using OGCoop.Shared;

namespace CoopAdmin.Server;

// жильё игрока в файле по имени RageCoop (PlayerFiles), рядом с гаражами
internal sealed class HomeStore
{
    private const int MaximumBytes = 256;
    private readonly PlayerFiles _files;

    internal HomeStore(string dataFolder) => _files = new PlayerFiles(dataFolder, "homes", MaximumBytes);

    // пустая строка, если жильё не выбрано
    internal string Load(string username)
    {
        if (_files.Read(username) is not string xml) return string.Empty;
        string key;
        try
        {
            using XmlReader reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumBytes
            });
            reader.MoveToContent();
            if (reader.Name != "Home") throw new InvalidDataException("The home format is unsupported.");
            key = reader.ReadElementContentAsString();
        }
        catch (XmlException error) { throw new InvalidDataException("The home file is damaged.", error); }
        if (!HomeCodec.IsKey(key)) throw new InvalidDataException("The home is unknown.");
        return key;
    }

    internal void Save(string username, string key)
    {
        // в ключе только латинские буквы, цифры и подчёркивание, экранировать нечего
        if (!HomeCodec.IsKey(key)) throw new InvalidDataException("The home is unknown.");
        _files.Write(username, "<Home>" + key + "</Home>");
    }
}
