using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OGCoop.Shared;

namespace CoopAdmin.Server;

// RageCoop compares names case-insensitively, so the key is the lower-cased name.
// An unusual name becomes a hash, so it is always a safe file name.
internal sealed class PlayerFiles
{
    private readonly long _maximumBytes;

    internal PlayerFiles(string dataFolder, string kind, long maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(dataFolder)) throw new ArgumentException("The resource data folder is required.", nameof(dataFolder));
        Folder = Path.Combine(dataFolder, kind);
        _maximumBytes = maximumBytes;
    }

    internal string Folder { get; }

    internal static string Key(string username) => Key(username, OperatingSystem.IsWindows());

    // On Windows con, nul, com1 and the like are devices, not files, and a trailing dot is
    // dropped. On Mac and Linux these names stay as they were, so players' files are not lost.
    internal static string Key(string username, bool windows)
    {
        string name = (username ?? string.Empty).Trim().ToLowerInvariant();
        bool plain = name.Length is > 0 and <= 32 && name[0] != '.' && name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.');
        if (plain && !(windows && WindowsReserved(name))) return name;
        using SHA256 hash = SHA256.Create();
        return "user-" + Convert.ToHexString(hash.ComputeHash(Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
    }

    private static bool WindowsReserved(string name)
    {
        if (name.EndsWith('.')) return true;
        string stem = name.Split('.')[0];
        return stem is "con" or "prn" or "aux" or "nul" ||
            stem.Length == 4 && (stem.StartsWith("com", StringComparison.Ordinal) || stem.StartsWith("lpt", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9';
    }

    internal string PathOf(string username) => Path.Combine(Folder, Key(username) + ".xml");

    internal string? Read(string username)
    {
        string path = PathOf(username);
        return File.Exists(path) ? ReadBounded(path) : null;
    }

    internal void Write(string username, string text)
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes(text);
        if (bytes.Length > _maximumBytes) throw new InvalidDataException("The player's file is too large.");
        AtomicFile.Write(PathOf(username), stream => stream.Write(bytes, 0, bytes.Length));
    }

    internal string ReadBounded(string path)
    {
        if (new FileInfo(path).Length > _maximumBytes) throw new InvalidDataException("The player's file is too large.");
        return File.ReadAllText(path, Encoding.UTF8);
    }
}
