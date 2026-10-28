using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace OGCoop.Installer;

internal static class Prerequisites
{
    internal const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
    internal const string NetFrameworkUrl = "https://dotnet.microsoft.com/download/dotnet-framework/net48";
    internal const string ScriptHookVPage = "https://www.dev-c.com/gtav/scripthookv/";
    // .NET Framework 4.8 release key, SHVDN 3 and RageCoop need it.
    private const int NetFramework48 = 528040;

    internal static bool VcRedistInstalled()
    {
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? runtime = machine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            if (runtime?.GetValue("Installed") is int installed && installed == 1) return true;
        }

        return false;
    }

    internal static bool NetFrameworkInstalled()
    {
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? full = machine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
        return full?.GetValue("Release") is int release && release >= NetFramework48;
    }

    // Microsoft's own installer, checked by its signature before it runs.
    internal static void InstallVcRedist(Action<string> say)
    {
        string file = Path.Combine(Path.GetTempPath(), "vc_redist.x64.exe");
        say(Text.Get("Скачиваю Visual C++ с сайта Microsoft…", "Downloading Visual C++ from Microsoft…"));
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        try
        {
            using (var client = new WebClient()) client.DownloadFile(VcRedistUrl, file);
            string signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(file)).GetNameInfo(X509NameType.SimpleName, false);
            if (!string.Equals(signer, "Microsoft Corporation", StringComparison.Ordinal))
                throw new InstallerException(Text.Get("Скачанный файл Visual C++ подписан не Microsoft.", "The downloaded Visual C++ file is not signed by Microsoft."),
                    Text.Get("Установите его вручную: ", "Install it manually: ") + VcRedistUrl);
            say(Text.Get("Устанавливаю Visual C++ (подтвердите запрос Windows)…", "Installing Visual C++ (confirm the Windows prompt)…"));
            using Process process = Process.Start(new ProcessStartInfo(file, "/install /passive /norestart") { UseShellExecute = true })!;
            process.WaitForExit();
            // 1638: a newer version is already installed. 3010: needs a restart.
            if (process.ExitCode is not (0 or 1638 or 3010))
                throw new InstallerException(Text.Get("Visual C++ не установился, код ", "Visual C++ setup failed, code ") + process.ExitCode,
                    Text.Get("Установите его вручную: ", "Install it manually: ") + VcRedistUrl);
            if (process.ExitCode == 3010) say(Text.Get("После установки перезагрузите компьютер.", "Restart the computer after setup."));
        }
        catch (Exception error) when (error is WebException or IOException or System.Security.Cryptography.CryptographicException or Win32Exception)
        {
            throw new InstallerException(Text.Get("Не удалось установить Visual C++: ", "Could not install Visual C++: ") + error.Message,
                Text.Get("Установите его вручную: ", "Install it manually: ") + VcRedistUrl, error);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }
}

// ScriptHookV may not be redistributed. When the package lacks it, the player
// downloads the official ZIP once and the installer takes the two files from it.
internal static class ScriptHookV
{
    internal static readonly (string Name, long Size, string Sha256)[] Files =
    {
        ("ScriptHookV.dll", 1987072, "126be57ca9dca00e471f9de611766009c900aad88f89ae6c2926ec62e5aaeb4f"),
        ("dinput8.dll", 131072, "9fd9e02353b7d39fe07b9667f7ea2697229a7f2d0e7d389eb79eb212b1bb181d"),
    };

    // ScriptHookV.dll is tied to the game build. Old mods may have left one for another
    // build, and RageCoop then fails to load although the installer said done. Other mods
    // (OpenIV) install the dinput8.dll loader too, so its presence is enough.
    internal static bool Installed(string game, (string Name, long Size, string Sha256)[]? expected = null)
    {
        expected ??= Files;
        (string name, long size, string sha256) = expected[0];
        string hook = Path.Combine(game, name);
        if (!File.Exists(hook) || new FileInfo(hook).Length != size || !File.Exists(Path.Combine(game, expected[1].Name))) return false;
        return string.Equals(BitConverter.ToString(GameFiles.Sha256(hook)).Replace("-", string.Empty), sha256, StringComparison.OrdinalIgnoreCase);
    }

    // Returns the ZIP used, or null when no matching download was found.
    internal static string? TryImport(string game, string[] folders, bool apply, (string Name, long Size, string Sha256)[]? expected = null)
    {
        expected ??= Files;
        foreach (string folder in folders.Where(Directory.Exists))
        {
            foreach (string zip in Directory.EnumerateFiles(folder, "*.zip").Where(IsScriptHookZip).OrderByDescending(File.GetLastWriteTimeUtc))
            {
                byte[][]? payloads = Extract(zip, expected);
                if (payloads == null) continue;
                if (apply)
                    for (int index = 0; index < expected.Length; index++)
                        GameFiles.WriteAtomic(Path.Combine(game, expected[index].Name), payloads[index]);
                return zip;
            }
        }

        return null;
    }

    private static bool IsScriptHookZip(string path)
    {
        string name = Path.GetFileName(path);
        return name.StartsWith("ScriptHookV", StringComparison.OrdinalIgnoreCase) && name.IndexOf("DotNet", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static byte[][]? Extract(string zip, (string Name, long Size, string Sha256)[] expected)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(zip);
            var payloads = new byte[expected.Length][];
            for (int index = 0; index < expected.Length; index++)
            {
                (string name, long size, string sha) = expected[index];
                ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(item =>
                    string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) && item.Length == size);
                if (entry == null) return null;
                using Stream stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                byte[] bytes = memory.ToArray();
                using var hash = System.Security.Cryptography.SHA256.Create();
                if (GameFiles.Hex(hash.ComputeHash(bytes)) != sha) return null;
                payloads[index] = bytes;
            }

            return payloads;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
