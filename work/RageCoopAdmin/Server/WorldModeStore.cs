using System;
using System.IO;
using System.Text;
using OGCoop.Shared;

namespace CoopAdmin.Server;

// Passing through traffic stays as the players last set it after a server
// restart (installing an update restarts the server). One short line next to
// the garages; the frozen time and weather still start fresh.
internal sealed class WorldModeStore
{
    private const string On = "ghost-traffic=on";
    private const string Off = "ghost-traffic=off";
    private const long MaximumFileBytes = 64;
    private readonly string _path;

    internal WorldModeStore(string dataFolder)
    {
        if (string.IsNullOrWhiteSpace(dataFolder)) throw new ArgumentException("The resource data folder is required.", nameof(dataFolder));
        _path = Path.Combine(dataFolder, "world-modes.txt");
    }

    // Absent: never set, so off. A damaged file is reported by the caller.
    internal bool? LoadGhostTraffic()
    {
        if (!File.Exists(_path)) return false;
        if (new FileInfo(_path).Length > MaximumFileBytes) return null;
        string text = File.ReadAllText(_path, Encoding.ASCII).Trim();
        return text == On ? true : text == Off ? false : null;
    }

    internal void SaveGhostTraffic(bool enabled)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(enabled ? On : Off);
        AtomicFile.Write(_path, stream => stream.Write(bytes, 0, bytes.Length));
    }
}
