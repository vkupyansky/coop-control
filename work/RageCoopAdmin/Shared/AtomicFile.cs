using System;
using System.IO;

namespace CoopControl.Shared;

// Writes beside the target and replaces it in one step: a reader never sees a
// half-written file, and a failed write leaves the previous file intact.
internal static class AtomicFile
{
    internal static void Write(string path, Action<Stream> write)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? throw new InvalidOperationException("The file has no folder.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
