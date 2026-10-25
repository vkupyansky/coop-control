using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CoopControl.Installer;

internal enum VehicleState
{
    // OpenIV.asi is missing: the game does not read the mods folder at all.
    NoModsLoader,
    Missing,
    Outdated,
    Current,
}

internal sealed class VehicleStatus
{
    internal VehicleStatus(VehicleState state, int expected, int current)
    {
        State = state;
        Expected = expected;
        Current = current;
    }

    internal VehicleState State { get; }
    internal int Expected { get; }
    internal int Current { get; }
}

// The OpenIV package copies whole dlc.rpf files into mods. Comparing their size
// and CRC with the package tells whether the installed cars match it. The
// dlclist inside update.rpf is encrypted and is not checked.
internal static class VehiclePack
{
    internal static VehicleStatus Inspect(string oiv, string game)
    {
        Dictionary<string, (uint Crc, long Size)> entries;
        var adds = new List<(string Source, string Target)>();
        var deletes = new List<string>();
        try
        {
            entries = ZipDirectory.Read(oiv);
            using ZipArchive archive = ZipFile.OpenRead(oiv);
            ZipArchiveEntry assembly = archive.GetEntry("assembly.xml") ?? throw new InvalidDataException("assembly.xml");
            XElement content;
            using (Stream stream = assembly.Open()) content = XDocument.Load(stream).Root?.Element("content") ?? throw new InvalidDataException("content");
            foreach (XElement add in content.Elements("add"))
                adds.Add(("content/" + ((string?)add.Attribute("source") ?? string.Empty).Replace('\\', '/'), add.Value.Trim()));
            deletes.AddRange(content.Elements("delete").Select(item => item.Value.Trim()));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            throw new InstallerException(Text.Get("Пакет машин повреждён: ", "The vehicle package is damaged: ") + Path.GetFileName(oiv),
                Text.Get("Скачайте архив заново и распакуйте его целиком.", "Download the archive again and extract all of it."), error);
        }

        if (!File.Exists(Path.Combine(game, "OpenIV.asi"))) return new VehicleStatus(VehicleState.NoModsLoader, adds.Count, 0);
        string mods = Path.Combine(game, "mods");
        int current = 0;
        bool stale = deletes.Any(path => File.Exists(Path.Combine(mods, path)));
        foreach ((string source, string target) in adds)
        {
            if (!entries.TryGetValue(source, out var expected))
                throw new InstallerException(Text.Get("В пакете машин нет файла ", "The vehicle package lacks ") + source,
                    Text.Get("Скачайте архив заново.", "Download the archive again."));
            string installed = Path.Combine(mods, target);
            if (!File.Exists(installed)) continue;
            if (new FileInfo(installed).Length == expected.Size && Crc32.Of(installed) == expected.Crc) current++;
            else stale = true;
        }

        VehicleState state = current == adds.Count && !stale ? VehicleState.Current
            : current == 0 && !stale ? VehicleState.Missing : VehicleState.Outdated;
        return new VehicleStatus(state, adds.Count, current);
    }
}

// Central directory of a ZIP (with ZIP64): name -> CRC-32 and size, without
// unpacking hundreds of megabytes.
internal static class ZipDirectory
{
    internal static Dictionary<string, (uint Crc, long Size)> Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        long end = FindEnd(stream, reader);
        stream.Position = end + 10;
        long count = reader.ReadUInt16();
        stream.Position = end + 16;
        long offset = reader.ReadUInt32();
        if (count == 0xFFFF || offset == 0xFFFFFFFF)
        {
            stream.Position = end - 20;
            if (reader.ReadUInt32() != 0x07064b50) throw new InvalidDataException("ZIP64 locator");
            stream.Position = end - 12;
            stream.Position = (long)reader.ReadUInt64();
            if (reader.ReadUInt32() != 0x06064b50) throw new InvalidDataException("ZIP64 end");
            stream.Position += 28;
            count = (long)reader.ReadUInt64();
            stream.Position += 8;
            offset = (long)reader.ReadUInt64();
        }

        if (count > 100_000 || offset < 0 || offset >= stream.Length) throw new InvalidDataException("central directory");
        var entries = new Dictionary<string, (uint, long)>(StringComparer.OrdinalIgnoreCase);
        stream.Position = offset;
        for (long index = 0; index < count; index++)
        {
            if (reader.ReadUInt32() != 0x02014b50) throw new InvalidDataException("entry");
            stream.Position += 4;
            ushort flags = reader.ReadUInt16();
            stream.Position += 6;
            uint crc = reader.ReadUInt32();
            stream.Position += 4;
            long size = reader.ReadUInt32();
            int nameLength = reader.ReadUInt16(), extraLength = reader.ReadUInt16(), commentLength = reader.ReadUInt16();
            stream.Position += 12;
            byte[] nameBytes = reader.ReadBytes(nameLength);
            string name = ((flags & 0x800) != 0 ? Encoding.UTF8 : Encoding.ASCII).GetString(nameBytes);
            long extraEnd = stream.Position + extraLength;
            while (size == 0xFFFFFFFF && stream.Position + 4 <= extraEnd)
            {
                ushort tag = reader.ReadUInt16(), length = reader.ReadUInt16();
                long next = stream.Position + length;
                if (tag == 1 && length >= 8) size = (long)reader.ReadUInt64();
                stream.Position = next;
            }

            stream.Position = extraEnd + commentLength;
            entries[name] = (crc, size);
        }

        return entries;
    }

    private static long FindEnd(FileStream stream, BinaryReader reader)
    {
        long minimum = Math.Max(0, stream.Length - 22 - 65535);
        for (long position = stream.Length - 22; position >= minimum; position--)
        {
            stream.Position = position;
            if (reader.ReadUInt32() == 0x06054b50) return position;
        }

        throw new InvalidDataException("end of central directory");
    }
}

internal static class Crc32
{
    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(index =>
    {
        uint value = (uint)index;
        for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
        return value;
    }).ToArray();

    internal static uint Of(string path)
    {
        uint crc = 0xFFFFFFFF;
        var buffer = new byte[1 << 20];
        using FileStream stream = File.OpenRead(path);
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            for (int index = 0; index < read; index++) crc = Table[(crc ^ buffer[index]) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}
