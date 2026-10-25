using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CoopControl.Installer;
using CoopControl.Shared;

namespace InstallerCheck;

internal static class CheckProgram
{
    private static int _checks;

    private static int Main()
    {
        Text.Russian = false;
        string root = Path.Combine(Path.GetTempPath(), "coop-installer-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CheckIniMerge();
            CheckGameFiles(root);
            CheckGameFolders(root);
            CheckLauncherRecords();
            CheckVehiclePack(root);
            CheckScriptHookImport(root);
            CheckReleaseList();
            CheckOptions();
        }
        finally
        {
            Directory.Delete(root, true);
        }

        Console.WriteLine("Installer checks passed: " + _checks);
        return 0;
    }

    private static void CheckIniMerge()
    {
        string existing = "; player comment\r\nReloadKeyBinding=Insert\r\nMyOwnKey=1\r\n\r\n[OTHER]\r\nkeep = yes\r\n";
        string merged = IniMerge.Merge(existing, "ReloadKeyBinding=None\nScriptTimeoutThreshold=5000\n");
        Assert(merged.Contains("; player comment") && merged.Contains("MyOwnKey=1") && merged.Contains("[OTHER]\r\nkeep = yes"),
            "Settings merge keeps the player's comments, own keys and other sections.");
        Assert(merged.Contains("ReloadKeyBinding=None") && !merged.Contains("Insert") &&
            merged.IndexOf("ScriptTimeoutThreshold=5000", StringComparison.Ordinal) < merged.IndexOf("[OTHER]", StringComparison.Ordinal),
            "Our keys replace the same keys; a missing key without a section goes before the first section.");
        Assert(IniMerge.Merge(merged, "ReloadKeyBinding=None\nScriptTimeoutThreshold=5000\n") == merged, "Merging again changes nothing.");
        string sectioned = IniMerge.Merge("[MODKIT_SETTINGS]\r\nMODKITARRAY_SIZE = 1024\r\n", "[MODKIT_SETTINGS]\nMODKITARRAY_SIZE = 65535\n");
        Assert(sectioned == "[MODKIT_SETTINGS]\r\nMODKITARRAY_SIZE = 65535\r\n", "A key inside a section is replaced in that section, with spaces around '='.");
        string added = IniMerge.Merge("[A]\r\nx=1\r\n", "[B]\ny=2\n[A]\nz=3\n");
        Assert(added == "[A]\r\nx=1\r\nz=3\r\n\r\n[B]\r\ny=2\r\n", "Missing keys go to the end of their section; a missing section is appended.");
    }

    private static void CheckGameFiles(string root)
    {
        string payload = Path.Combine(root, "payload"), game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(payload, @"scripts\RageCoop"));
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(payload, "ScriptHookV.dll"), "hook");
        File.WriteAllText(Path.Combine(payload, @"scripts\RageCoop\RageCoop.Client.dll"), "client");
        File.WriteAllText(Path.Combine(payload, "ScriptHookVDotNet.ini"), "ReloadKeyBinding=None\n");
        CopyResult dry = GameFiles.Install(payload, game, apply: false);
        Assert(dry.Added == 3 && Directory.GetFileSystemEntries(game).Length == 0, "Check mode reports new files and writes nothing.");
        CopyResult first = GameFiles.Install(payload, game, apply: true);
        Assert(first.Added == 3 && File.ReadAllText(Path.Combine(game, @"scripts\RageCoop\RageCoop.Client.dll")) == "client",
            "A first installation copies every file with its folders.");
        CopyResult again = GameFiles.Install(payload, game, apply: true);
        Assert(again.Unchanged == 3 && !again.AnyChange, "Running the installer again over the same version changes nothing.");
        File.WriteAllText(Path.Combine(payload, @"scripts\RageCoop\RageCoop.Client.dll"), "client-2");
        File.WriteAllText(Path.Combine(game, "ScriptHookVDotNet.ini"), "ReloadKeyBinding=Insert\nMine=1\n");
        CopyResult update = GameFiles.Install(payload, game, apply: true);
        Assert(update.Updated == 1 && update.Settings == 1 && update.Changed.Count == 2 &&
            File.ReadAllText(Path.Combine(game, @"scripts\RageCoop\RageCoop.Client.dll")) == "client-2",
            "An update replaces only the changed file and merges the settings file.");
        string ini = File.ReadAllText(Path.Combine(game, "ScriptHookVDotNet.ini"));
        Assert(ini.Contains("ReloadKeyBinding=None") && ini.Contains("Mine=1"), "The merged settings keep the player's own key.");
        Assert(!Directory.EnumerateFiles(game, "*.coop-new", SearchOption.AllDirectories).Any(), "No temporary file is left after an update.");
    }

    private static void CheckGameFolders(string root)
    {
        Assert(GameFolder.IsSupportedVersion(new Version(1, 0, 3889, 0)) && !GameFolder.IsSupportedVersion(new Version(1, 0, 3586, 0)) &&
            !GameFolder.IsSupportedVersion(null), "Only GTA V Legacy 1.0.3889 is supported; an unreadable version is not.");
        string legacy = Path.Combine(root, "Legacy"), enhanced = Path.Combine(root, "Enhanced"), empty = Path.Combine(root, "Empty");
        foreach (string folder in new[] { legacy, enhanced, empty }) Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(legacy, "GTA5.exe"), "not a real exe");
        File.WriteAllText(Path.Combine(enhanced, "GTA5_Enhanced.exe"), "not a real exe");
        GameFolder? byFile = GameFolder.Inspect("\"" + Path.Combine(legacy, "GTA5.exe") + "\"");
        Assert(byFile != null && byFile.Path == legacy && byFile.Kind == GameKind.Legacy && byFile.Version == null && !byFile.IsSupported,
            "A dragged, quoted GTA5.exe selects its folder; a file without version information is not accepted as supported.");
        Assert(GameFolder.Inspect(enhanced)?.Kind == GameKind.Enhanced, "GTA V Enhanced is recognised and refused by its own executable.");
        Assert(GameFolder.Inspect(empty) == null && GameFolder.Inspect("") == null && GameFolder.Inspect("\0bad") == null,
            "Folders without the game and invalid input are rejected without an exception.");
    }

    private static void CheckLauncherRecords()
    {
        string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}";
        Assert(GameLocator.ParseSteamLibraries(vdf).SequenceEqual(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }),
            "Steam libraries are read from libraryfolders.vdf.");
        Assert(GameLocator.ParseEpicLocation("{ \"DisplayName\": \"Grand Theft Auto V Legacy\", \"InstallLocation\": \"C:\\\\Program Files\\\\Epic Games\\\\GTAV\" }") ==
            @"C:\Program Files\Epic Games\GTAV" && GameLocator.ParseEpicLocation("{}") == null, "The Epic install location is read from its manifest.");
    }

    private static void CheckVehiclePack(string root)
    {
        string oiv = Path.Combine(root, "cars.oiv"), game = Path.Combine(root, "cars-game");
        byte[] car = Encoding.ASCII.GetBytes(new string('c', 5000)), other = Encoding.ASCII.GetBytes(new string('d', 5000));
        using (ZipArchive archive = ZipFile.Open(oiv, ZipArchiveMode.Create))
        {
            Write(archive, "assembly.xml", Encoding.UTF8.GetBytes("<package><content><add source=\"rc_one/dlc.rpf\">update\\x64\\dlcpacks\\rc_one\\dlc.rpf</add>" +
                "<delete>update\\x64\\dlcpacks\\rc_old\\dlc.rpf</delete></content></package>"), CompressionLevel.Optimal);
            Write(archive, "content/rc_one/dlc.rpf", car, CompressionLevel.NoCompression);
        }

        Directory.CreateDirectory(game);
        Assert(VehiclePack.Inspect(oiv, game).State == VehicleState.NoModsLoader, "Without OpenIV.asi the player is told to install the ASI first.");
        File.WriteAllText(Path.Combine(game, "OpenIV.asi"), "asi");
        Assert(VehiclePack.Inspect(oiv, game).State == VehicleState.Missing, "Cars that were never installed are reported as missing.");
        string installed = Path.Combine(game, @"mods\update\x64\dlcpacks\rc_one\dlc.rpf");
        Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
        File.WriteAllBytes(installed, other);
        Assert(VehiclePack.Inspect(oiv, game).State == VehicleState.Outdated, "A car file of the same size but other content needs an update.");
        File.WriteAllBytes(installed, car);
        VehicleStatus current = VehiclePack.Inspect(oiv, game);
        Assert(current.State == VehicleState.Current && current.Expected == 1 && current.Current == 1, "Matching car files mean OpenIV is not needed.");
        string old = Path.Combine(game, @"mods\update\x64\dlcpacks\rc_old\dlc.rpf");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllText(old, "old");
        Assert(VehiclePack.Inspect(oiv, game).State == VehicleState.Outdated, "A removed model still installed means the package must be applied again.");
        Assert(Crc32.Of(Write(root, "crc.txt", "123456789")) == 0xCBF43926, "CRC-32 matches the standard check value.");
        File.WriteAllText(Path.Combine(root, "broken.oiv"), "not a zip");
        Assert(Throws(() => VehiclePack.Inspect(Path.Combine(root, "broken.oiv"), game)), "A damaged package is reported with a clear error.");
    }

    private static void CheckScriptHookImport(string root)
    {
        string downloads = Path.Combine(root, "Downloads"), game = Path.Combine(root, "hook-game");
        Directory.CreateDirectory(downloads);
        Directory.CreateDirectory(game);
        byte[] hook = Encoding.ASCII.GetBytes("hook"), loader = Encoding.ASCII.GetBytes("loader");
        var expected = new[] { ("ScriptHookV.dll", (long)hook.Length, Sha(hook)), ("dinput8.dll", (long)loader.Length, Sha(loader)) };
        using (ZipArchive dotnet = ZipFile.Open(Path.Combine(downloads, "ScriptHookVDotNet.zip"), ZipArchiveMode.Create))
            Write(dotnet, "bin/ScriptHookV.dll", hook, CompressionLevel.Optimal);
        using (ZipArchive wrong = ZipFile.Open(Path.Combine(downloads, "ScriptHookV_old.zip"), ZipArchiveMode.Create))
        {
            Write(wrong, "bin/ScriptHookV.dll", Encoding.ASCII.GetBytes("hooq"), CompressionLevel.Optimal);
            Write(wrong, "bin/dinput8.dll", loader, CompressionLevel.Optimal);
        }

        Assert(ScriptHookV.TryImport(game, new[] { downloads }, true, expected) == null && Directory.GetFiles(game).Length == 0,
            "Another version of ScriptHookV and the SHVDN archive are never taken.");
        using (ZipArchive right = ZipFile.Open(Path.Combine(downloads, "ScriptHookV_3889.0.zip"), ZipArchiveMode.Create))
        {
            Write(right, "ScriptHookV_3889.0/bin/ScriptHookV.dll", hook, CompressionLevel.Optimal);
            Write(right, "ScriptHookV_3889.0/bin/dinput8.dll", loader, CompressionLevel.Optimal);
        }

        Assert(ScriptHookV.TryImport(game, new[] { downloads }, false, expected) != null && Directory.GetFiles(game).Length == 0,
            "Check mode finds the official download without writing.");
        Assert(ScriptHookV.TryImport(game, new[] { downloads, Path.Combine(root, "missing") }, true, expected)?.EndsWith("ScriptHookV_3889.0.zip") == true &&
            File.ReadAllText(Path.Combine(game, "dinput8.dll")) == "loader", "The matching official ZIP supplies both files without unpacking by hand.");
        Assert(ScriptHookV.Files.All(file => file.Sha256.Length == 64), "The pinned ScriptHookV hashes are complete.");
    }

    private static void CheckReleaseList()
    {
        var next = new Version(ReleaseCheck.Current.Major, ReleaseCheck.Current.Minor, ReleaseCheck.Current.Build + 1);
        Assert(ReleaseCheck.Newest("[{\"tag_name\":\"v9.0.0-wip\",\"draft\":true},{\"tag_name\":\"v" + next + "-wip\",\"draft\":false}]") == next &&
            ReleaseCheck.IsNewer(next) && !ReleaseCheck.IsNewer(ReleaseCheck.Current),
            "On .NET Framework the installer reads GitHub's release list the same way: drafts skipped, only a newer version offered.");
    }

    private static void CheckOptions()
    {
        Options options = Options.Parse(new[] { "--check", "--game", @"C:\Games\GTA V", "--lang", "ru", "--no-pause" });
        Assert(options.Check && options.NoPause && options.Game == @"C:\Games\GTA V" && options.Language == "ru" && !options.Elevated,
            "Command-line options are read for checks and the administrator restart.");
        Assert(Throws(() => Options.Parse(new[] { "--lang", "de" })) && Throws(() => Options.Parse(new[] { "--game" })),
            "Unknown or incomplete options are refused.");
    }

    private static void Write(ZipArchive archive, string name, byte[] data, CompressionLevel level)
    {
        using Stream stream = archive.CreateEntry(name, level).Open();
        stream.Write(data, 0, data.Length);
    }

    private static string Write(string root, string name, string text)
    {
        string path = Path.Combine(root, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static string Sha(byte[] data)
    {
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(data).Select(value => value.ToString("x2")));
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception error) when (error is InstallerException or ArgumentException)
        {
            return true;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + message);
        _checks++;
        Console.WriteLine("PASS " + message);
    }
}
