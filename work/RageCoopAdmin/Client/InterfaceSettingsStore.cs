using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using OGCoop.Shared;

namespace RageCoopAdmin.Client;

internal sealed class InterfaceSettingsStore
{
    private const long MaximumFileBytes = 16 * 1024;
    private const int MaximumSwitches = 32;
    private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(InterfaceSettings));
    private readonly string _path;

    internal InterfaceSettingsStore(string dataFolder)
    {
        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            throw new ArgumentException("The resource data folder is required.", nameof(dataFolder));
        }

        _path = Path.Combine(dataFolder, "interface.xml");
    }

    internal InterfaceSettings Load() => LoadCore();

    private InterfaceSettings LoadCore()
    {
        if (!File.Exists(_path))
        {
            return new InterfaceSettings();
        }

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaximumFileBytes)
        {
            throw new InvalidDataException("The interface settings file is too large.");
        }

        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumFileBytes
        });
        reader.MoveToContent();
        // Without the attribute the serializer keeps the current version, so it is read first.
        bool unversioned = reader.GetAttribute("version") == null;
        if (Serializer.Deserialize(reader) is not InterfaceSettings settings)
        {
            throw new InvalidDataException("The interface settings format is invalid.");
        }

        // A file of an older version starts over from the defaults and is replaced on the next save.
        if (unversioned || settings.Version < InterfaceSettings.CurrentVersion)
        {
            return new InterfaceSettings();
        }

        Validate(settings);
        return settings;
    }

    internal void Save(InterfaceSettings settings)
    {
        using StorageFileLock held = StorageFileLock.Acquire(_path);
        // Options another version wrote are carried from the current file, even
        // when callers save a fresh settings object.
        settings.OtherOptions = LoadCore().OtherOptions;
        SaveCore(settings);
    }

    internal InterfaceSettings Update(Action<InterfaceSettings> change)
    {
        using StorageFileLock held = StorageFileLock.Acquire(_path);
        InterfaceSettings latest = LoadCore();
        change(latest);
        SaveCore(latest);
        return latest;
    }

    private void SaveCore(InterfaceSettings settings)
    {
        Validate(settings);
        AtomicFile.Write(_path, stream => Serializer.Serialize(stream, settings));
    }

    private static void Validate(InterfaceSettings settings)
    {
        if (settings.Version != InterfaceSettings.CurrentVersion || settings.RepairShortcut < 0 || settings.RepairShortcut > 1)
        {
            throw new InvalidDataException("Unsupported interface settings.");
        }

        if (settings.MenuLanguage != UiLanguage.Russian && settings.MenuLanguage != UiLanguage.English)
        {
            throw new InvalidDataException("Unsupported menu language.");
        }

        settings.Switches ??= new List<string>();
        if (settings.Switches.Count > MaximumSwitches || settings.Switches.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 32))
        {
            throw new InvalidDataException("Unsupported menu switches.");
        }
    }
}

[XmlRoot("Interface")]
public sealed partial class InterfaceSettings
{
    internal const int CurrentVersion = 5;

    [XmlAttribute("version")]
    public int Version { get; set; } = CurrentVersion;

    // 0 enables K, 1 disables it.
    public int RepairShortcut { get; set; }
    public bool GamepadShortcuts { get; set; } = true;
    public bool KeyboardBoost { get; set; } = true;
    public UiLanguage MenuLanguage { get; set; } = UiLanguage.Russian;
    // Absent in older files, so voice chat is on until the player turns it off.
    public bool VoiceChat { get; set; } = true;
    // Absent in older files, so a new car seats the player at the wheel until turned off.
    public bool DriveNewVehicle { get; set; } = true;
    // The Character and Weapons switches that are on (invincibility and the
    // like), turned on again at the next join. Absent in older files: all off.
    [XmlArrayItem("On")]
    public List<string> Switches { get; set; } = new List<string>();
    // Options this build does not know, written by another version, are kept as they are.
    [XmlAnyElement]
    public XmlElement[]? OtherOptions { get; set; }
}
