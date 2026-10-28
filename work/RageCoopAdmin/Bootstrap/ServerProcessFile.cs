using System;
using System.Globalization;
using System.IO;

// server.pid beside RageCoop.Server.dll lets the one-click stop script find
// exactly this server. It is removed on a normal exit, and the stop script also
// removes a file left by a forced stop.
internal static class ServerProcessFile
{
    internal const string Name = "server.pid";

    internal static void Create(string directory)
    {
        string path = Path.Combine(directory, Name);
        string pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        try
        {
            File.WriteAllText(path, pid);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The server still runs, only the stop button cannot find it.
            Console.Error.WriteLine("OG Coop: could not write " + path + ": " + error.Message);
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Remove(path, pid);
    }

    internal static void Remove(string path, string pid)
    {
        try
        {
            if (File.Exists(path) && File.ReadAllText(path).Trim() == pid) File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("OG Coop: could not remove " + path + ": " + error.Message);
        }
    }
}
