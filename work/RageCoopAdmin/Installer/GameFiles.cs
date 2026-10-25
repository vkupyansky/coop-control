using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CoopControl.Installer;

internal sealed class CopyResult
{
    internal int Added { get; set; }
    internal int Updated { get; set; }
    internal int Unchanged { get; set; }
    internal int Settings { get; set; }
    internal List<string> Changed { get; } = new List<string>();
    internal bool AnyChange => Added + Updated + Settings > 0;
}

// Copies the package folder next to GTA5.exe. Files already identical are left
// alone; settings files keep the player's own lines and only get our keys.
internal static class GameFiles
{
    internal static CopyResult Install(string payload, string game, bool apply)
    {
        string root = Path.GetFullPath(payload).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var result = new CopyResult();
        foreach (string source in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new InstallerException(Text.Get("В пакете ссылка вместо файла: ", "The package contains a link instead of a file: ") + source,
                    Text.Get("Распакуйте архив заново.", "Extract the archive again."));
            string relative = source.Substring(root.Length);
            string target = Path.Combine(game, relative);
            if (relative.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
            {
                string existing = ReadText(target);
                string merged = IniMerge.Merge(existing, ReadText(source));
                // Line endings alone are not a change: a file copied earlier keeps its own.
                if (Normalize(merged) == Normalize(existing))
                {
                    result.Unchanged++;
                    continue;
                }

                result.Settings++;
                result.Changed.Add(relative);
                if (apply) WriteAtomic(target, Encoding.UTF8.GetBytes(merged));
                continue;
            }

            if (File.Exists(target))
            {
                if (SameContent(source, target))
                {
                    result.Unchanged++;
                    continue;
                }

                result.Updated++;
            }
            else
            {
                result.Added++;
            }

            result.Changed.Add(relative);
            if (apply) WriteAtomic(target, File.ReadAllBytes(source));
        }

        return result;
    }

    internal static bool SameContent(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length) return false;
        return Sha256(first).SequenceEqual(Sha256(second));
    }

    internal static byte[] Sha256(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return sha.ComputeHash(stream);
    }

    internal static string Hex(byte[] bytes) => string.Concat(bytes.Select(value => value.ToString("x2")));

    // A crash mid-copy never leaves a half-written DLL in the game folder.
    internal static void WriteAtomic(string target, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "." + Path.GetFileName(target) + ".coop-new");
        File.WriteAllBytes(temporary, bytes);
        try
        {
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string ReadText(string path) => Encoding.UTF8.GetString(File.ReadAllBytes(path)).TrimStart('﻿');

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();
}

// Settings files without a full parser: our keys replace the same keys, every
// other line (comments, the player's own keys, other sections) stays as it was.
internal static class IniMerge
{
    internal static string Merge(string existing, string wanted)
    {
        var lines = Split(existing);
        var pending = new List<(string Section, string Key, string Line)>();
        string section = string.Empty;
        foreach (string line in Split(wanted))
        {
            if (TrySection(line, out string name)) section = name;
            else if (TryKey(line, out string key)) pending.Add((section, key, line.Trim()));
        }

        section = string.Empty;
        for (int index = 0; index < lines.Count; index++)
        {
            if (TrySection(lines[index], out string name))
            {
                section = name;
                continue;
            }

            if (!TryKey(lines[index], out string key)) continue;
            int match = pending.FindIndex(item => Same(item.Section, section) && Same(item.Key, key));
            if (match < 0) continue;
            lines[index] = pending[match].Line;
            pending.RemoveAt(match);
        }

        foreach (var group in pending.GroupBy(item => item.Section, StringComparer.OrdinalIgnoreCase))
        {
            int at = InsertionPoint(lines, group.Key);
            if (at < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add(string.Empty);
                lines.Add("[" + group.Key + "]");
                at = lines.Count;
            }

            lines.InsertRange(at, group.Select(item => item.Line));
        }

        return string.Join("\r\n", lines) + "\r\n";
    }

    // End of the section's last key, or the file start for keys without a section.
    private static int InsertionPoint(List<string> lines, string section)
    {
        if (section.Length == 0)
        {
            int first = lines.FindIndex(line => TrySection(line, out _));
            int end = first < 0 ? lines.Count : first;
            while (end > 0 && lines[end - 1].Trim().Length == 0) end--;
            return end;
        }

        int header = lines.FindIndex(line => TrySection(line, out string name) && Same(name, section));
        if (header < 0) return -1;
        int next = lines.FindIndex(header + 1, line => TrySection(line, out _));
        int stop = next < 0 ? lines.Count : next;
        while (stop > header + 1 && lines[stop - 1].Trim().Length == 0) stop--;
        return stop;
    }

    private static List<string> Split(string text)
    {
        var lines = text.TrimStart('﻿').Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static bool TrySection(string line, out string name)
    {
        string trimmed = line.Trim();
        name = trimmed.Length > 2 && trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']' ? trimmed.Substring(1, trimmed.Length - 2).Trim() : string.Empty;
        return name.Length > 0;
    }

    private static bool TryKey(string line, out string key)
    {
        string trimmed = line.Trim();
        int equals = trimmed.IndexOf('=');
        key = equals > 0 && trimmed[0] != ';' && trimmed[0] != '#' ? trimmed.Substring(0, equals).Trim() : string.Empty;
        return key.Length > 0;
    }

    private static bool Same(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
