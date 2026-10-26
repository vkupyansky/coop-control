using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using GTA;
using LemonUI.Menus;

internal static partial class Program
{
    private static int CheckPublicEngine()
    {
        var boundary = new Harmony("friendslegacy.check.publicengine");
        try
        {
            boundary.Patch(typeof(Scaleform).GetMethod("RequestMovie", Static, null, new[] { typeof(string) }, null)!,
                prefix: new HarmonyMethod(typeof(Program), nameof(SkipMovie)));
            InitializeHost();
            CheckEngineGuardComposition();
            Console.WriteLine("Bounded actual engine guard composition passed: install, shared sends, packet decode, reinstall and unload against the loaded baseline; GTA natives and live connections were isolated. This is not the complete input or game test suite.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            RemoveSharedRuntimeGuards();
            foreach (Guard guard in Guards.Reverse()) { guard.Uninstall(); }
            boundary.UnpatchAll(boundary.Id);
        }
    }

    private static int CheckReleaseProfile(bool publicRelease)
    {
        var boundary = new Harmony("friendslegacy.check.publicrelease");
        string directory = Path.Combine(Path.GetTempPath(), "CoopPublicRelease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            boundary.Patch(typeof(Scaleform).GetMethod("RequestMovie", Static, null, new[] { typeof(string) }, null)!,
                prefix: new HarmonyMethod(typeof(Program), nameof(SkipMovie)));
            InitializeHost();
            if (!publicRelease)
            {
                CheckLocalVehicleInput();
                Console.WriteLine("Private client profile passed against the actual built DLL; GTA graphics and physical devices were isolated.");
                return 0;
            }
            const string catalogName = "RageCoopAdmin.Client.AddonVehicles.json";
            Assert(Resource.GetManifestResourceNames().OrderBy(name => name, StringComparer.Ordinal)
                    .SequenceEqual(new[] { catalogName, "RageCoopAdmin.Client.Assets.MenuEnglish.resources" }),
                "The actual public DLL contains only the vehicle catalog and the English menu text, with no PNG or FSS assets.");
            Type catalog = Resource.GetType("RageCoopAdmin.Client.AddonVehicleCatalog", true)!;
            var entries = (IList)catalog.GetMethod("LoadEmbedded", Static)!.Invoke(null, Array.Empty<object>())!;
            Assert(entries.Count == 0, "The actual public DLL loads a valid empty vehicle catalog.");

            PatchMenuStorageBoundaries(boundary);
            var host = new MenuStorageHost(directory);
            host.Set("_interfaceSettings", Activator.CreateInstance(Resource.GetType("RageCoopAdmin.Client.InterfaceSettings", true)!)!);
            host.Set("_mainMenu", new NativeMenu("Проверка", "Главное меню"));
            host.Call("BuildInterfaceMenu");
            NativeMenu settings = host.Pool.OfType<NativeMenu>().Single(menu => menu.Name == "Настройки");
            Assert(settings.Items.Select(item => item.Title).SequenceEqual(new[]
                { "Язык / Language", "Ремонт клавишей K", "Сочетания на геймпаде", "Ускоритель (O / A)", "Сразу за руль", "Голосовой чат", "О дополнении" }),
                "The real public settings menu has the language selector, five controls and About, without an empty HUD option.");
            int count = host.Pool.Count();
            host.Call("BuildAddonVehiclesMenu");
            Assert(host.Pool.Count() == count && host.Get("_addonVehiclesMenu") == null,
                "The real builder adds no empty add-on vehicle submenu.");
            CheckVehicleToolsComposition();
            CheckMenuStorageComposition();
            CheckOtherSettingsPreservation(directory);
            Console.WriteLine("Public client profile passed against the actual built DLL; GTA graphics and network connections were isolated.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            boundary.UnpatchAll(boundary.Id);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CheckOtherSettingsPreservation(string directory)
    {
        Type settingsType = Resource.GetType("RageCoopAdmin.Client.InterfaceSettings", true)!;
        Type storeType = Resource.GetType("RageCoopAdmin.Client.InterfaceSettingsStore", true)!;
        object store = Activator.CreateInstance(storeType, Instance, null, new object[] { directory }, null)!;
        string path = Path.Combine(directory, "interface.xml");
        File.WriteAllText(path, "<Interface version=\"5\"><RepairShortcut>0</RepairShortcut>" +
            "<FutureOption mode=\"keep\">false</FutureOption></Interface>");
        object loaded = storeType.GetMethod("Load", Instance)!.Invoke(store, Array.Empty<object>())!;
        settingsType.GetProperty("RepairShortcut")!.SetValue(loaded, 1);
        storeType.GetMethod("Save", Instance)!.Invoke(store, new[] { loaded });
        void Preserved(string label)
        {
            var document = new System.Xml.XmlDocument();
            document.Load(path);
            Assert(document.SelectSingleNode("/Interface/FutureOption[@mode='keep']")?.InnerText == "false", label);
        }
        Preserved("Saving an edited settings object keeps an option another version wrote, as it was.");
        object fresh = Activator.CreateInstance(settingsType)!;
        storeType.GetMethod("Save", Instance)!.Invoke(store, new[] { fresh });
        Preserved("Saving a fresh settings object also keeps the latest option of another version.");
        var change = new Action<object>(value => settingsType.GetProperty("RepairShortcut")!.SetValue(value, 1));
        storeType.GetMethod("Update", Instance)!.Invoke(store, new object[] { change });
        Preserved("The actual Update transaction carries another version's option through a control change.");
    }
}
