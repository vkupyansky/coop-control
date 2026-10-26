using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OGCoop.Installer;

internal enum GameKind
{
    Legacy,
    Enhanced,
}

internal sealed class GameFolder
{
    // ScriptHookV and the pinned RageCoop client support only this game build.
    internal const int SupportedBuild = 3889;

    internal GameFolder(string path, GameKind kind, Version? version)
    {
        Path = path;
        Kind = kind;
        Version = version;
    }

    internal string Path { get; }
    internal GameKind Kind { get; }
    internal Version? Version { get; }
    internal bool IsSupported => Kind == GameKind.Legacy && IsSupportedVersion(Version);

    internal static bool IsSupportedVersion(Version? version) =>
        version != null && version.Major == 1 && version.Minor == 0 && version.Build == SupportedBuild;

    // Accepts the game folder or GTA5.exe itself, as dragged into the console.
    internal static GameFolder? Inspect(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string full;
        try
        {
            full = System.IO.Path.GetFullPath(input!.Trim().Trim('"'));
            if (File.Exists(full)) full = System.IO.Path.GetDirectoryName(full)!;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or System.Security.SecurityException)
        {
            return null;
        }

        if (!Directory.Exists(full)) return null;
        string enhanced = System.IO.Path.Combine(full, "GTA5_Enhanced.exe");
        string legacy = System.IO.Path.Combine(full, "GTA5.exe");
        if (File.Exists(enhanced)) return new GameFolder(full, GameKind.Enhanced, ReadVersion(enhanced));
        return File.Exists(legacy) ? new GameFolder(full, GameKind.Legacy, ReadVersion(legacy)) : null;
    }

    private static Version? ReadVersion(string exe)
    {
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(exe);
            if (info.FileMajorPart == 0 && info.FileMinorPart == 0 && info.FileBuildPart == 0) return null;
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

// Finds installed copies through the Rockstar, Steam and Epic records and the
// usual library folders. Every candidate is confirmed by its GTA5.exe.
internal static class GameLocator
{
    internal static List<GameFolder> Find()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<GameFolder>();
        foreach (string candidate in Candidates())
        {
            GameFolder? folder = GameFolder.Inspect(candidate);
            if (folder != null && seen.Add(folder.Path.TrimEnd('\\'))) found.Add(folder);
        }

        return found.OrderByDescending(folder => folder.IsSupported).ThenBy(folder => folder.Kind).ToList();
    }

    internal static IEnumerable<string> ParseSteamLibraries(string vdf)
    {
        foreach (Match match in Regex.Matches(vdf, "\"path\"\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase))
            yield return match.Groups[1].Value.Replace("\\\\", "\\");
    }

    internal static string? ParseEpicLocation(string item)
    {
        Match match = Regex.Match(item, "\"InstallLocation\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return match.Success ? match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\/", "/") : null;
    }

    private static IEnumerable<string> Candidates() =>
        Safe(RockstarFolders).Concat(Safe(SteamFolders)).Concat(Safe(EpicFolders)).Concat(Safe(CommonFolders));

    // A broken record in one launcher must not hide the others.
    private static IEnumerable<string> Safe(Func<IEnumerable<string>> source)
    {
        List<string> items;
        try
        {
            items = source().ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            items = new List<string>();
        }

        return items;
    }

    private static IEnumerable<string> RockstarFolders()
    {
        foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? rockstar = machine.OpenSubKey(@"SOFTWARE\Rockstar Games");
            if (rockstar == null) continue;
            foreach (string name in rockstar.GetSubKeyNames())
            {
                using RegistryKey? game = rockstar.OpenSubKey(name);
                if (game == null) continue;
                foreach (string value in new[] { "InstallFolder", "InstallFolderSteam", "InstallFolderEpic" })
                    if (game.GetValue(value) is string path && path.Length > 0) yield return path;
            }
        }
    }

    private static IEnumerable<string> SteamFolders()
    {
        using RegistryKey? steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (steam?.GetValue("SteamPath") is not string root || root.Length == 0) yield break;
        string libraries = Path.Combine(root.Replace('/', '\\'), @"steamapps\libraryfolders.vdf");
        var roots = new List<string> { root.Replace('/', '\\') };
        if (File.Exists(libraries)) roots.AddRange(ParseSteamLibraries(File.ReadAllText(libraries)));
        foreach (string library in roots)
        {
            string common = Path.Combine(library, @"steamapps\common");
            if (!Directory.Exists(common)) continue;
            foreach (string folder in Directory.EnumerateDirectories(common)) yield return folder;
        }
    }

    private static IEnumerable<string> EpicFolders()
    {
        string manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Epic\EpicGamesLauncher\Data\Manifests");
        if (!Directory.Exists(manifests)) yield break;
        foreach (string item in Directory.EnumerateFiles(manifests, "*.item"))
        {
            string? location = ParseEpicLocation(File.ReadAllText(item));
            if (location != null) yield return location;
        }
    }

    private static IEnumerable<string> CommonFolders()
    {
        string[] parents =
        {
            "", "Games", "Program Files", "Program Files (x86)", @"Program Files\Rockstar Games", @"Program Files\Epic Games",
            @"Program Files (x86)\Steam\steamapps\common", @"SteamLibrary\steamapps\common", "Epic Games", "Rockstar Games",
            @"Games\Rockstar Games", @"Games\Epic Games", "VK Play", @"Games\VK Play",
        };
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
        {
            foreach (string parent in parents)
            {
                string directory = Path.Combine(drive.RootDirectory.FullName, parent);
                if (!Directory.Exists(directory)) continue;
                string[] children;
                try
                {
                    children = Directory.GetDirectories(directory);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (string child in children)
                {
                    string name = Path.GetFileName(child);
                    if (name.IndexOf("Grand Theft Auto", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.StartsWith("GTA", StringComparison.OrdinalIgnoreCase))
                        yield return child;
                }
            }
        }
    }
}
