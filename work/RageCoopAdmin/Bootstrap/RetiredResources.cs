using System;
using System.IO;
using System.Linq;

// до переименования в OG Coop клиентский ресурс лежал в Resources/Client/CoopControl.
// RageCoop раздаёт игрокам каждую папку из Resources/Client, поэтому если новую папку
// сервера скопировали поверх старой, у игроков загрузились бы оба меню
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
