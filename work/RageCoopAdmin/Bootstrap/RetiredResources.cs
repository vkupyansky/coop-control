using System;
using System.IO;
using System.Linq;

// Before the rename to OG Coop the client resource lived in Resources/Client/CoopControl.
// RageCoop sends players every folder in Resources/Client, so with the new server folder
// copied over the old one players would load both menus.
internal static class RetiredResources
{
    internal static readonly string Folder = Path.Combine("Resources", "Client", "CoopControl");
    private const string ClientFile = "RageCoopAdmin.Client.dll";
    private static readonly string[] Ours = { ClientFile, "0Harmony.dll" };
    private static readonly string[] Ignored = { ".DS_Store", "Thumbs.db", "desktop.ini" };

    internal static bool Remove(string serverDirectory)
    {
        string folder = Path.Combine(serverDirectory, Folder);
        if (!Directory.Exists(folder)) return false;
        string[] files = Directory.GetFiles(folder).Select(path => Path.GetFileName(path)!).ToArray();
        bool former = (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0 &&
            Directory.GetDirectories(folder).Length == 0 &&
            files.Contains(ClientFile, StringComparer.OrdinalIgnoreCase) &&
            files.All(name => Ours.Concat(Ignored).Contains(name, StringComparer.OrdinalIgnoreCase));
        if (!former)
        {
            Console.Error.WriteLine("OG Coop: " + folder + " holds other files and was left as is. " +
                "If it is the former Coop Control menu, delete it: players would get two menus.");
            return false;
        }

        try
        {
            foreach (string name in files)
            {
                string path = Path.Combine(folder, name);
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            Directory.Delete(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("OG Coop: could not delete the former menu folder " + folder +
                ". Delete it by hand, otherwise players would get two menus.", error);
        }

        Console.WriteLine("OG Coop: removed the former menu folder " + Folder + ".");
        return true;
    }
}
