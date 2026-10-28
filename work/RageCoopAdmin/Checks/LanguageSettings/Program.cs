using System;
using System.IO;
using System.Linq;
using System.Threading;
using RageCoopAdmin.Client;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CoopLanguageCheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckNewProfile(directory);
            CheckOlderProfiles(directory);
            CheckInvalidLanguage(directory);
            CheckConcurrentChoices(directory);
            CheckWriteContention(directory);
            CheckOtherVersionOption(directory);
            Assert(!Directory.GetFiles(directory, "*.tmp").Any(), "writes leave no temporary settings files");
            Console.WriteLine($"Language settings checks: {_assertions} PASS, 0 FAIL");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CheckNewProfile(string directory)
    {
        var store = new InterfaceSettingsStore(directory);
        InterfaceSettings initial = store.Load();
        Assert(initial.MenuLanguage == UiLanguage.Russian && initial.Version == 5,
            "a missing optional language uses Russian in the existing settings schema");
        Assert(!Directory.EnumerateFiles(directory).Any(), "loading a new profile does not create a file");
        store.Save(new InterfaceSettings
        {
            MenuLanguage = UiLanguage.English,
            RepairShortcut = 1,
            GamepadShortcuts = false,
            KeyboardBoost = false
        });
        InterfaceSettings restored = new InterfaceSettingsStore(directory).Load();
        Assert(restored.MenuLanguage == UiLanguage.English && restored.RepairShortcut == 1 &&
            !restored.GamepadShortcuts && !restored.KeyboardBoost,
            "English and existing explicit opt-outs survive a complete storage reopen");
        string path = Path.Combine(directory, "interface.xml");
        Assert(File.ReadAllText(path).Contains("<MenuLanguage>en</MenuLanguage>"),
            "language has a stable named XML value independent of enum numbering");
        store.Update(value => value.MenuLanguage = UiLanguage.Russian);
        Assert(store.Load().MenuLanguage == UiLanguage.Russian && File.ReadAllText(path).Contains("<MenuLanguage>ru</MenuLanguage>"),
            "switching back to Russian persists without a new settings file");
    }

    private static void CheckOlderProfiles(string directory)
    {
        var store = new InterfaceSettingsStore(directory);
        string path = Path.Combine(directory, "interface.xml");
        for (int version = 0; version <= 5; version++)
        {
            string versionAttribute = version == 0 ? string.Empty : $" version=\"{version}\"";
            int repair = version <= 3 ? 2 : 1;
            string original = $"<Interface{versionAttribute}><RepairShortcut>{repair}</RepairShortcut>" +
                "<GamepadShortcuts>false</GamepadShortcuts><KeyboardBoost>false</KeyboardBoost>" +
                "<FutureOption>false</FutureOption></Interface>";
            File.WriteAllText(path, original);
            InterfaceSettings loaded = store.Load();
            // Only the current version keeps its choices, an older file starts from the defaults.
            bool current = version == 5;
            Assert(loaded.Version == 5 && loaded.MenuLanguage == UiLanguage.Russian &&
                loaded.RepairShortcut == (current ? 1 : 0) && loaded.GamepadShortcuts != current && loaded.KeyboardBoost != current,
                $"profile v{version} keeps its choices only in the current format and defaults the missing language");
            Assert(File.ReadAllText(path) == original, $"reading profile v{version} does not rewrite player settings");
            store.Update(value => value.MenuLanguage = UiLanguage.English);
            InterfaceSettings reopened = new InterfaceSettingsStore(directory).Load();
            Assert(reopened.Version == 5 && reopened.MenuLanguage == UiLanguage.English &&
                reopened.RepairShortcut == loaded.RepairShortcut && reopened.GamepadShortcuts == loaded.GamepadShortcuts &&
                reopened.KeyboardBoost == loaded.KeyboardBoost,
                $"saving the language over profile v{version} keeps the choices it was read with");
            Assert(current ? reopened.OtherOptions?.Single().InnerText == "false" : reopened.OtherOptions == null,
                $"an option another version wrote is kept only from the current format, v{version}");
        }
    }

    private static void CheckInvalidLanguage(string directory)
    {
        var store = new InterfaceSettingsStore(directory);
        string path = Path.Combine(directory, "interface.xml");
        foreach (string invalid in new[] { "de", "EN", "1", "-1", string.Empty })
        {
            string original = "<Interface version=\"5\"><MenuLanguage>" + invalid +
                "</MenuLanguage><KeyboardBoost>false</KeyboardBoost></Interface>";
            File.WriteAllText(path, original);
            MustReject(() => store.Load(), "unknown or malformed language cannot become a valid profile");
            MustReject(() => store.Update(value => value.GamepadShortcuts = false),
                "an update cannot overwrite a profile whose language is invalid");
            MustReject(() => store.Save(new InterfaceSettings()),
                "saving a fresh settings object cannot replace invalid existing choices");
            Assert(File.ReadAllText(path) == original, "a rejected XML language preserves the complete original file");
        }
        File.WriteAllText(path, "<Interface version=\"5\"><MenuLanguage>en</MenuLanguage></Interface>");
        string valid = File.ReadAllText(path);
        MustReject(() => store.Save(new InterfaceSettings { MenuLanguage = (UiLanguage)2 }),
            "an undefined in-memory enum is rejected before serialization");
        MustReject(() => store.Update(value => value.MenuLanguage = (UiLanguage)(-1)),
            "an update rejects an invalid enum without writing");
        Assert(File.ReadAllText(path) == valid, "invalid in-memory values preserve the saved language");
    }

    private static void CheckConcurrentChoices(string directory)
    {
        var first = new InterfaceSettingsStore(directory);
        var second = new InterfaceSettingsStore(directory);
        first.Save(new InterfaceSettings { MenuLanguage = UiLanguage.English });
        second.Update(value =>
        {
            value.RepairShortcut = 1;
            value.GamepadShortcuts = false;
            value.KeyboardBoost = false;
        });
        first.Update(value => value.MenuLanguage = UiLanguage.Russian);
        InterfaceSettings result = second.Load();
        Assert(result.MenuLanguage == UiLanguage.Russian && result.RepairShortcut == 1 &&
            !result.GamepadShortcuts && !result.KeyboardBoost,
            "a language-only write merges the current file instead of replacing another writer's controls");
        second.Update(value => value.KeyboardBoost = true);
        Assert(first.Load().MenuLanguage == UiLanguage.Russian && first.Load().KeyboardBoost,
            "a later control-only write retains the current language");
    }

    private static void CheckWriteContention(string directory)
    {
        var store = new InterfaceSettingsStore(directory);
        string path = Path.Combine(directory, "interface.xml");
        string previous = File.ReadAllText(path);
        Exception? failure = null;
        bool callbackRan = false;
        using (StorageFileLock held = StorageFileLock.Acquire(path))
        {
            var worker = new Thread(() =>
            {
                try
                {
                    store.Update(value => { callbackRan = true; value.MenuLanguage = UiLanguage.English; });
                }
                catch (Exception ex) { failure = ex; }
            });
            worker.Start();
            Assert(worker.Join(5000), "a contended language write fails promptly without blocking the player");
        }
        Assert(failure is IOException && !callbackRan && File.ReadAllText(path) == previous,
            "a failed lock does not alter the saved language or run the requested mutation");
        store.Update(value => value.MenuLanguage = UiLanguage.English);
        Assert(new InterfaceSettingsStore(directory).Load().MenuLanguage == UiLanguage.English,
            "a language change can be retried successfully after writer contention clears");
    }

    private static void CheckOtherVersionOption(string directory)
    {
        string path = Path.Combine(directory, "interface.xml");
        File.WriteAllText(path, "<Interface version=\"5\"><MenuLanguage>ru</MenuLanguage>" +
            "<FutureOption future=\"keep\">other-value</FutureOption></Interface>");
        var store = new InterfaceSettingsStore(directory);
        store.Update(value => value.MenuLanguage = UiLanguage.English);
        store.Save(new InterfaceSettings { MenuLanguage = UiLanguage.Russian });
        InterfaceSettings result = store.Load();
        Assert(result.MenuLanguage == UiLanguage.Russian && result.OtherOptions?.Length == 1 &&
            result.OtherOptions[0].InnerText == "other-value" &&
            result.OtherOptions[0].GetAttribute("future") == "keep",
            "language updates and direct saves keep an option of another version without interpreting it");
    }

    private static void MustReject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { _assertions++; return; }
        catch (InvalidOperationException) { _assertions++; return; }
        throw new InvalidOperationException(message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _assertions++;
    }
}
