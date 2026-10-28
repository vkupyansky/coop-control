using System;
using System.Runtime.InteropServices;

namespace OGCoop.Installer;

// Windows folders by their fixed ids, so folders the player moved are found too.
internal static class KnownFolders
{
    private static readonly Guid DownloadsId = new Guid("374DE290-123F-4565-9164-39C4925E467B");

    // Null when Windows does not know the folder.
    internal static string? Downloads()
    {
        if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out IntPtr path) != 0) return null;
        try { return Marshal.PtrToStringUni(path); }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
